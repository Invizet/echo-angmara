"""Строит манифест перевода (l10n/ru.json) из папки игры или папки сборки.

Состав компонентов задан здесь; имена ассетов на GitHub Releases плоские (basename), поэтому
basename каждого файла должен быть уникален — проверяется.

    python make_manifest.py --root "C:\\The Lord of the Rings Online" --version 3.4.2 \
        --base-url https://github.com/Invizet/echo-angmara/releases/download/l10n-ru-3.4.2/ --out site/l10n/ru.json

  --local  — откуда брать файлы echoespatch/local (по умолчанию <root>/echoespatch/local — актуальная установка)
"""
import argparse
import datetime
import hashlib
import json
import os
import sys

COMPONENTS = [
    # id, заголовок, описание, файлы/маски относительно корня игры, requires
    ("text", "Тексты и шрифты", "Весь текст игры на русском. Шрифты лежат в том же файле — отдельно не ставятся.",
     ["echoespatch/local/25_42_text.patches"], []),
    ("sounds", "Озвучка", "Русские голоса и звуки.", ["echoespatch/local/0a_sounds.patches"], []),
    ("images", "Картинки", "Переведённые надписи на картинках интерфейса и загрузочных экранах.",
     ["echoespatch/local/41_imgs.patches"], []),
    ("xlat", "Русский поиск на аукционе", "Словарь названий предметов: искать на аукционе можно по-русски.",
     ["echoespatch/local/xlat.dat"], []),
    ("video", "Видеоролики", "Ролики с русской озвучкой. Пути к ним прописаны в текстах — без русских текстов игра их не покажет.",
     # стартовый ролик клиент берёт из raw/en при первом запуске — кладём русский на его место
     ["raw/ru/", "raw/en/introcinematic/lotro_intro_cinematic.bik"], ["text"]),
]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def resolve(root, local_src, rel):
    """Путь в манифесте → реальный файл на диске (echoespatch/local берём из LIVE-папки сборки)."""
    if rel.startswith("echoespatch/local/"):
        return os.path.join(local_src, rel.split("/", 2)[2])
    return os.path.join(root, rel.replace("/", os.sep))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--local")
    ap.add_argument("--version", required=True)
    ap.add_argument("--base-url", required=True, help="куда будут залиты файлы (с / на конце)")
    ap.add_argument("--notes")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    local_src = args.local or os.path.join(args.root, "echoespatch", "local")

    comps, seen = [], {}
    for cid, title, desc, entries, requires in COMPONENTS:
        files = []
        for e in entries:
            if e.endswith("/"):  # вся папка
                base = os.path.join(args.root, e.replace("/", os.sep))
                rels = sorted(os.path.relpath(os.path.join(d, f), args.root).replace(os.sep, "/")
                              for d, _, fs in os.walk(base) for f in fs)
            else:
                rels = [e]
            for rel in rels:
                if os.path.basename(rel).lower() == "desktop.ini":
                    continue
                src = resolve(args.root, local_src, rel)
                if not os.path.isfile(src):
                    sys.exit(f"Нет файла: {src}")
                name = os.path.basename(rel)
                if name in seen:
                    sys.exit(f"Имя {name} повторяется ({seen[name]} и {rel}) — на GitHub Releases ассеты плоские")
                seen[name] = rel
                files.append({"path": rel, "size": os.path.getsize(src), "sha256": sha256(src),
                              "urls": [args.base_url + name], "_src": src})
                print(f"  {rel}  {files[-1]['size'] / 2**20:.1f} МБ", file=sys.stderr)
        comps.append({"id": cid, "title": title, "description": desc, "requires": requires, "default": True, "files": files})

    manifest = {"lang": "ru", "version": args.version, "released": datetime.date.today().isoformat(),
                "notes": args.notes, "components": comps}
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    # список «что заливать» — рядом, для шага публикации
    with open(args.out + ".files.txt", "w", encoding="utf-8") as f:
        for c in comps:
            for x in c["files"]:
                f.write(x.pop("_src") + "\n")
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)
    total = sum(x["size"] for c in comps for x in c["files"])
    print(f"Манифест v{args.version}: {sum(len(c['files']) for c in comps)} файлов, {total / 2**20:.0f} МБ → {args.out}", file=sys.stderr)


if __name__ == "__main__":
    main()
