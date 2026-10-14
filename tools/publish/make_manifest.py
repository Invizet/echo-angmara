"""Собирает файлы перевода для лаунчера и манифест (l10n/ru.v2.json).

Берёт готовый перевод из LIVE-папки (echoes_l10n.py) и перекладывает его так, чтобы части ставились независимо:
  * шрифты: описания (0x42) лежат в текстах, а их текстуры глифов (0x41) — в картинках. Текстуры глифов
    переносятся в 25_42_text.patches, иначе без «Изображений» русский текст не виден;
  * пути к роликам: строковые таблицы со ссылками на .bik вынимаются из текстов в отдельный маленький файл
    в трёх вариантах — лаунчер кладёт в local\\ ровно один по сочетанию галочек «Тексты» и «Видеоролики»:
        25_videopaths_ru.patches          русский текст, русские ролики (raw/ru/*_ru.bik)
        25_videopaths_ru_envideo.patches  русский текст, английские ролики — тексты без роликов
        25_videopaths_en_ruvideo.patches  английский текст, русские ролики — ролики без текстов
  * стартовый ролик клиент берёт из raw/en при первом запуске, по пути не из текстов. С роликами — кладём на его
    место русский, без них — английский оригинал (скрытая часть base: качается, только если у игрока его нет).

Имена ассетов на GitHub Releases плоские, поэтому имя ассета уникально (проверяется); одинаковые по SHA-256 файлы
заливаются один раз. С --reuse файлы, уже залитые в прошлый релиз (тот же SHA-256), берутся по старым ссылкам.

    python make_manifest.py --root "C:\\The Lord of the Rings Online" --version 2.4.0 \\
        --en-intro "D:\\...\\raw\\en\\introcinematic\\lotro_intro_cinematic.bik" \\
        --reuse https://invizet.github.io/echo-angmara/l10n/ru.json \\
        --base-url https://github.com/Invizet/echo-angmara/releases/download/l10n-ru-2.4.0-2/ --out site/l10n/ru.v2.json

  --local  — откуда брать echoespatch/local (по умолчанию <root>/echoespatch/local_RU_LIVE)
  --build  — куда класть пересобранные файлы (по умолчанию <out>.build/)
Рядом с манифестом пишется <out>.upload.txt — что залить в релиз (путь|имя ассета).
"""
import argparse
import datetime
import hashlib
import json
import os
import sys
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lotro_patches as lp  # noqa: E402

INTRO_EN = "raw/en/introcinematic/lotro_intro_cinematic.bik"
INTRO_RU = "raw/ru/introcinematic/lotro_intro_cinematic_ru.bik"
VP_RU, VP_RU_ENV, VP_EN_RUV = "25_videopaths_ru.patches", "25_videopaths_ru_envideo.patches", "25_videopaths_en_ruvideo.patches"


def local(name):
    return "echoespatch/local/" + name


def components(a):
    """id, заголовок, описание, файлы [(путь в игре, источник, when, имя ассета)], hidden.

    when — доп. условие по галочкам других частей ({"video": False} = только без роликов). Обычный файл стоит
    при русском языке и включённой своей части; файл скрытой части — всегда, если не перебит другим вариантом
    того же пути (варианты одного пути перебираются по порядку в манифесте).
    """
    L, B = a.local, a.build
    return [
        ("text", "Тексты и шрифты", "Весь текст игры на русском, вместе со шрифтами.", [
            (local("25_42_text.patches"), os.path.join(B, "25_42_text.patches"), None, None),
            (local(VP_RU), os.path.join(B, VP_RU), {"video": True}, None),
            (local(VP_RU_ENV), os.path.join(B, VP_RU_ENV), {"video": False}, None),
        ], False),
        ("sounds", "Озвучка", "Русские голоса и звуки.", [
            (local("0a_sounds.patches"), os.path.join(L, "0a_sounds.patches"), None, None)], False),
        ("images", "Изображения", "Переведённые карты, логотипы и загрузочные экраны.", [
            (local("41_imgs.patches"), os.path.join(B, "41_imgs.patches"), None, None)], False),
        ("xlat", "Поиск по аукциону", "Словарь названий предметов: искать на аукционе можно по-русски.", [
            (local("xlat.dat"), os.path.join(L, "xlat.dat"), None, None)], False),
        ("video", "Видеоролики", "Ролики с русской озвучкой; работают и с английскими текстами.",
         [(rel, os.path.join(a.root, rel.replace("/", os.sep)), None, None) for rel in walk(a.root, "raw/ru/")] + [
             (INTRO_EN, os.path.join(a.root, INTRO_RU.replace("/", os.sep)), None, None),
             (local(VP_EN_RUV), os.path.join(B, VP_EN_RUV), {"text": False}, None),
         ], False),
        # английский оригинал стартового ролика — на случай, когда русский не нужен (EN или без роликов)
        ("base", "Оригинальные файлы игры", None, [
            (INTRO_EN, a.en_intro, None, "lotro_intro_cinematic_en.bik")], True),
    ]


def walk(root, rel_dir):
    base = os.path.join(root, rel_dir.replace("/", os.sep))
    return sorted(os.path.relpath(os.path.join(d, f), root).replace(os.sep, "/")
                  for d, _, fs in os.walk(base) for f in fs if f.lower() != "desktop.ini")


# ---------- пересборка .patches ----------

def font_textures(text_entries, img_dids):
    """Текстуры глифов = RenderSurface (0x41), на которые ссылаются шрифты (0x42)."""
    refs = set()
    for did, _, _, data in text_entries:
        if did >> 24 == 0x42:
            refs.update(int.from_bytes(data[o:o + 4], "little") for o in range(len(data) - 3))
    return refs & img_dids


def build(a):
    os.makedirs(a.build, exist_ok=True)
    text = lp.read_patches(os.path.join(a.local, "25_42_text.patches"))
    imgs = lp.read_patches(os.path.join(a.local, "41_imgs.patches"))

    fonts = font_textures(text, {e[0] for e in imgs})
    if not fonts:
        sys.exit("Не нашёл текстур шрифтов в 41_imgs.patches")
    # текстура глифа — 8-битная (формат 0x1C); сверка, что ссылка из шрифта не зацепила случайную картинку
    odd = [hex(d) for d, _, _, x in imgs if d in fonts and x[16:20] != b"\x1c\0\0\0"]
    if odd:
        sys.exit(f"Шрифты ссылаются на текстуры необычного формата: {odd}")

    ru_tables = {did: lp.StringTable(data) for did, _, _, data in text if did >> 24 == 0x25}
    vp = sorted(did for did, t in ru_tables.items()
                if any(".bik" in s.lower() for parts, _, _ in t.fragments.values() for s in parts))
    if not vp:
        sys.exit("Не нашёл строк с путями к роликам (.bik)")

    sys.path.insert(0, a.l10n)
    import datfile
    en_dat = datfile.DatFile(a.en_dat)
    variants = {VP_RU: [], VP_RU_ENV: [], VP_EN_RUV: []}
    n_paths = 0
    for did in vp:
        ru = ru_tables[did]
        res = en_dat.fetch(did)
        if res is None:
            sys.exit(f"{did:#x} нет в {a.en_dat}")
        en = lp.StringTable(res.data)
        if set(en.fragments) != set(ru.fragments):  # порядок фрагментов в EN и RU разный — каждый вариант хранит свой
            sys.exit(f"{did:#x}: фрагменты EN и RU не совпадают")
        bik = [f for f, (parts, _, _) in ru.fragments.items() if any(".bik" in s.lower() for s in parts)]
        for f in bik:
            if ru.fragments[f][1:] != en.fragments[f][1:]:
                sys.exit(f"{did:#x}/{f}: у EN и RU разные ссылки/аргументы")
        n_paths += len(bik)
        ru_en, en_ru = lp.StringTable(ru.to_bytes()), lp.StringTable(en.to_bytes())
        for f in bik:
            ru_en.fragments[f][0] = list(en.fragments[f][0])
            en_ru.fragments[f][0] = list(ru.fragments[f][0])
        ver = next(e[1] for e in text if e[0] == did)
        variants[VP_RU].append((did, ver, 0, ru.to_bytes()))
        variants[VP_RU_ENV].append((did, ver, 0, ru_en.to_bytes()))
        variants[VP_EN_RUV].append((did, ver, 0, en_ru.to_bytes()))
    en_dat.close()

    vp_set = set(vp)
    new_text = [e for e in text if e[0] not in vp_set] + [e for e in imgs if e[0] in fonts]
    new_imgs = [e for e in imgs if e[0] not in fonts]
    lp.write_patches(os.path.join(a.build, "25_42_text.patches"), new_text)
    lp.write_patches(os.path.join(a.build, "41_imgs.patches"), new_imgs)
    for name, entries in variants.items():
        lp.write_patches(os.path.join(a.build, name), entries)
    print(f"Шрифты: {len(fonts)} текстур глифов перенесены в тексты; картинок осталось {len(new_imgs)}.", file=sys.stderr)
    print(f"Пути к роликам: {len(vp)} таблиц ({', '.join(f'{d:#x}' for d in vp)}), {n_paths} строк.", file=sys.stderr)


# ---------- манифест ----------

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_reuse(src):
    if not src:
        return {}
    if src.startswith("http"):
        with urllib.request.urlopen(src, timeout=60) as r:
            m = json.load(r)
    else:
        with open(src, encoding="utf-8") as f:
            m = json.load(f)
    return {x["sha256"]: x["urls"] for c in m["components"] for x in c["files"]}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--local")
    ap.add_argument("--l10n", help="папка echoes_l10n.py (нужен datfile.py), по умолчанию <root>/L10N")
    ap.add_argument("--en-dat", help="английский client_local, по умолчанию <root>/client_local_EN_GB.dat")
    ap.add_argument("--en-intro", required=True, help="оригинальный английский lotro_intro_cinematic.bik")
    ap.add_argument("--build")
    ap.add_argument("--reuse", help="прошлый манифест (путь или URL): файлы с тем же SHA-256 не перезаливаются")
    ap.add_argument("--version", required=True)
    ap.add_argument("--base-url", required=True, help="куда будут залиты новые файлы (с / на конце)")
    ap.add_argument("--notes")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    a.local = a.local or os.path.join(a.root, "echoespatch", "local_RU_LIVE")
    a.l10n = a.l10n or os.path.join(a.root, "L10N")
    a.en_dat = a.en_dat or os.path.join(a.root, "client_local_EN_GB.dat")
    a.build = a.build or a.out + ".build"

    build(a)
    reuse = load_reuse(a.reuse)
    if sha256(a.en_intro) == sha256(os.path.join(a.root, INTRO_RU.replace("/", os.sep))):
        sys.exit("--en-intro — это русский ролик, нужен английский оригинал")

    comps, assets, uploads, by_sha = [], set(), [], {}
    for cid, title, desc, entries, hidden in components(a):
        files = []
        for rel, src, when, asset in entries:
            if not os.path.isfile(src):
                sys.exit(f"Нет файла: {src}")
            sha = sha256(src)
            if sha not in by_sha:
                if sha in reuse:
                    by_sha[sha] = reuse[sha]
                else:
                    name = asset or os.path.basename(rel)
                    if name in assets:
                        sys.exit(f"Ассет {name} повторяется — на GitHub Releases имена плоские")
                    assets.add(name)
                    by_sha[sha] = [a.base_url + name]
                    uploads.append(f"{src}|{name}")
            f = {"path": rel, "size": os.path.getsize(src), "sha256": sha, "urls": by_sha[sha]}
            if when:
                f["when"] = when
            files.append(f)
            print(f"  {cid:6} {rel}  {f['size'] / 2**20:.1f} МБ" + (f"  when={when}" if when else ""), file=sys.stderr)
        c = {"id": cid, "title": title, "description": desc, "requires": [], "default": True, "files": files}
        if hidden:
            c["hidden"] = True
        comps.append(c)

    manifest = {"format": 2, "lang": "ru", "version": a.version, "released": datetime.date.today().isoformat(),
                "notes": a.notes, "components": comps}
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out + ".upload.txt", "w", encoding="utf-8") as f:
        f.write("".join(u + "\n" for u in uploads))
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)
    total = sum(x["size"] for c in comps for x in c["files"])
    new = sum(os.path.getsize(u.split("|")[0]) for u in uploads)
    print(f"Манифест v{a.version}: {sum(len(c['files']) for c in comps)} файлов, {total / 2**20:.0f} МБ → {a.out}; "
          f"залить {len(uploads)} новых ({new / 2**20:.0f} МБ), список — {a.out}.upload.txt", file=sys.stderr)


if __name__ == "__main__":
    main()
