"""Собирает последние посты публичного Telegram-канала в news.json для лаунчера «Эхо Ангмара».

Источник — веб-превью https://t.me/s/<канал>, токен не нужен. Запускается из GitHub Actions
(вне РФ), картинки скачиваются к себе, чтобы лаунчер не ходил на CDN Telegram.

    python fetch_telegram.py --channel echoesofangmar --out ../../news-out --limit 20
"""
import argparse
import hashlib
import html
import json
import os
import re
import sys
import urllib.parse
import urllib.request
from html.parser import HTMLParser

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) EchoAngmaraNews/1.0"
# Теги, которые лаунчер умеет рисовать; всё прочее разворачивается в текст
ALLOWED = {"b", "strong", "i", "em", "u", "s", "del", "a", "br", "blockquote", "code", "pre"}


def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=30) as r:
        return r.read()


class PostsParser(HTMLParser):
    """Разбирает страницу t.me/s/... в список постов: id, дата, html текста, картинки."""

    def __init__(self, channel):
        super().__init__(convert_charrefs=True)
        self.channel = channel
        self.posts = []
        self.cur = None
        self.text_depth = 0      # глубина внутри tgme_widget_message_text
        self.emoji_depth = 0     # внутри <i class="emoji"> оставляем только сам символ
        self.out = []

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        cls = a.get("class", "") or ""
        if "tgme_widget_message " in cls + " " and a.get("data-post"):
            self.cur = {"id": int(a["data-post"].rsplit("/", 1)[1]), "photos": [], "date": None}
            self.posts.append(self.cur)
            return
        if self.cur is None:
            return
        if "tgme_widget_message_photo_wrap" in cls:
            m = re.search(r"background-image:url\('([^']+)'\)", a.get("style", ""))
            if m:
                self.cur["photos"].append(m.group(1))
        if tag == "time" and a.get("datetime") and self.cur["date"] is None:
            self.cur["date"] = a["datetime"]
        if self.text_depth:
            self.text_depth += 1 if tag not in ("br",) else 0
            if tag == "i" and "emoji" in cls:
                self.emoji_depth = 1
                return
            if self.emoji_depth:
                self.emoji_depth += 1 if tag != "br" else 0
                return
            if tag in ALLOWED:
                if tag == "a":
                    href = a.get("href", "")
                    if href.startswith("?"):
                        href = f"https://t.me/s/{self.channel}{href}"
                    self.out.append(f'<a href="{html.escape(href, quote=True)}">')
                elif tag == "br":
                    self.out.append("<br/>")
                else:
                    self.out.append(f"<{tag}>")
            return
        if "tgme_widget_message_text" in cls and "js-message_text" in cls:
            self.text_depth = 1
            self.out = []

    def handle_startendtag(self, tag, attrs):
        # <br/> и <img/>: только открытие, иначе сбивается счётчик глубины
        self.handle_starttag(tag, attrs)

    def handle_endtag(self, tag):
        if tag == "br" or not self.text_depth:
            return
        self.text_depth -= 1
        if self.emoji_depth:
            self.emoji_depth -= 1
            return
        if self.text_depth == 0:
            self.cur["html"] = "".join(self.out).strip()
            return
        if tag in ALLOWED and tag != "br":
            self.out.append(f"</{tag}>")

    def handle_data(self, data):
        if self.text_depth:
            self.out.append(html.escape(data, quote=False))


def plain(h):
    t = re.sub(r"<br/>|</blockquote>|<blockquote>", "\n", h)
    return html.unescape(re.sub(r"<[^>]+>", "", t)).strip()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--channel", default="echoesofangmar")
    ap.add_argument("--out", default="news-out")
    ap.add_argument("--limit", type=int, default=20)
    args = ap.parse_args()

    p = PostsParser(args.channel)
    p.feed(fetch(f"https://t.me/s/{args.channel}").decode("utf-8"))
    posts = [x for x in p.posts if x.get("html") or x["photos"]]
    posts.sort(key=lambda x: x["id"], reverse=True)
    posts = posts[: args.limit]
    if not posts:
        sys.exit("Не нашлось ни одного поста — Telegram поменял разметку?")

    img_dir = os.path.join(args.out, "img")
    os.makedirs(img_dir, exist_ok=True)
    keep = set()
    items = []
    for x in posts:
        photos = []
        for n, url in enumerate(x["photos"]):
            if url.startswith("//"):
                url = "https:" + url
            name = f"{x['id']}_{n}{os.path.splitext(urllib.parse.urlparse(url).path)[1] or '.jpg'}"
            path = os.path.join(img_dir, name)
            if not os.path.exists(path):
                try:
                    data = fetch(url)
                    with open(path, "wb") as f:
                        f.write(data)
                except Exception as e:  # картинка не критична
                    print(f"  картинка {url}: {e}", file=sys.stderr)
                    continue
            keep.add(name)
            photos.append("img/" + name)
        h = x.get("html", "")
        items.append({
            "id": x["id"],
            "url": f"https://t.me/{args.channel}/{x['id']}",
            "date": x["date"],
            "html": h,
            "text": plain(h),
            "photos": photos,
        })

    for name in os.listdir(img_dir):  # старые картинки ушедших постов
        if name not in keep:
            os.remove(os.path.join(img_dir, name))

    doc = {"source": f"https://t.me/{args.channel}", "items": items}
    body = json.dumps(doc, ensure_ascii=False, indent=1)
    doc["hash"] = hashlib.sha256(body.encode()).hexdigest()[:16]
    with open(os.path.join(args.out, "news.json"), "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    print(f"Постов: {len(items)}, последний #{items[0]['id']} от {items[0]['date']}, картинок: {len(keep)}")


if __name__ == "__main__":
    main()
