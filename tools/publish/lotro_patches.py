"""Форматы LOTRO для сборки файлов лаунчера: архив .patches и строковая таблица (.stt, DBOType 0x25).

.patches  = <2LQ MAGIC, dir_offset, 0>, тела записей подряд, на dir_offset: <L n> + n×<5L did, version, iteration, offset, size>
            (по возрастанию did), затем <L 0> (свободных блоков нет). Так же пишет echoes_l10n.py (generate_patches_in).
.stt      = <L file_id><L version><B type><vle n> + n фрагментов:
            <Q frag_id><L k> + k кусков (vle длина + UTF-16LE), <L r> + r×<L> ссылок, <B g> + g групп аргументов
            (<L m> + m×(vle + UTF-16LE)). Куски склеиваются плейсхолдерами — здесь их не трогаем.
"""
import struct

MAGIC = 0x7ADA05EC


def read_patches(path):
    """→ список (did, version, iteration, data)."""
    with open(path, "rb") as f:
        d = f.read()
    magic, dir_offset, _ = struct.unpack_from("<2LQ", d, 0)
    if magic != MAGIC:
        raise ValueError(f"{path}: не .patches (magic {magic:#x})")
    n, = struct.unpack_from("<L", d, dir_offset)
    out = []
    for i in range(n):
        did, ver, it, off, size = struct.unpack_from("<5L", d, dir_offset + 4 + 20 * i)
        out.append((did, ver, it, d[off:off + size]))
    return out


def write_patches(path, entries):
    entries = sorted(entries, key=lambda e: e[0])
    if len({e[0] for e in entries}) != len(entries):
        raise ValueError(f"{path}: повторяющиеся DataID")
    with open(path, "wb") as f:
        f.write(struct.pack("<2LQ", MAGIC, 0, 0))
        offsets = []
        for _, _, _, data in entries:
            offsets.append(f.tell())
            f.write(data)
        dir_offset = f.tell()
        f.write(struct.pack("<L", len(entries)))
        for (did, ver, it, data), off in zip(entries, offsets):
            f.write(struct.pack("<5L", did, ver, it, off, len(data)))
        f.write(struct.pack("<L", 0))
        f.seek(4)
        f.write(struct.pack("<L", dir_offset))


# ---------- строковые таблицы ----------

def _read_vle(d, o):
    a = d[o]
    if a & 0x80 == 0:
        return a, o + 1
    if a == 0xE0:
        return struct.unpack_from("<L", d, o + 1)[0], o + 5
    b = d[o + 1]
    if a & 0x40:
        c, = struct.unpack_from("<H", d, o + 2)
        return c | ((b | ((a & 0x3F) << 8)) << 16), o + 4
    return b | ((a & 0x7F) << 8), o + 2


def _vle(v):
    if v < 0x80:
        return struct.pack("B", v)
    if v < 0x4000:
        return struct.pack("BB", 0x80 | (v >> 8), v & 0xFF)
    if v < 0x20000000:
        return struct.pack("BBH", 0xC0 | (v >> 24), (v >> 16) & 0xFF, v & 0xFFFF)
    return struct.pack("<BL", 0xE0, v)


def _read_str(d, o):
    n, o = _read_vle(d, o)
    return d[o:o + 2 * n].decode("utf-16le"), o + 2 * n


def _str(s):
    b = s.encode("utf-16le")
    return _vle(len(b) // 2) + b


class StringTable:
    """Разобранная .stt; fragments: {frag_id: [куски, ссылки, группы аргументов]} в исходном порядке."""

    def __init__(self, data):
        self.file_id, self.version, self.type = struct.unpack_from("<LLB", data, 0)
        n, o = _read_vle(data, 9)
        self.fragments = {}
        for _ in range(n):
            frag, k = struct.unpack_from("<QL", data, o)
            o += 12
            parts = []
            for _ in range(k):
                s, o = _read_str(data, o)
                parts.append(s)
            r, = struct.unpack_from("<L", data, o)
            refs = list(struct.unpack_from(f"<{r}L", data, o + 4))
            o += 4 + 4 * r
            g = data[o]
            o += 1
            args = []
            for _ in range(g):
                m, = struct.unpack_from("<L", data, o)
                o += 4
                group = []
                for _ in range(m):
                    s, o = _read_str(data, o)
                    group.append(s)
                args.append(group)
            self.fragments[frag] = [parts, refs, args]
        if o != len(data):
            raise ValueError(f"{self.file_id:#x}: лишние {len(data) - o} байт после разбора")

    def to_bytes(self):
        out = bytearray(struct.pack("<LLB", self.file_id, self.version, self.type))
        out += _vle(len(self.fragments))
        for frag, (parts, refs, args) in self.fragments.items():
            out += struct.pack("<QL", frag, len(parts))
            for s in parts:
                out += _str(s)
            out += struct.pack(f"<L{len(refs)}L", len(refs), *refs)
            out += struct.pack("B", len(args))
            for group in args:
                out += struct.pack("<L", len(group))
                for s in group:
                    out += _str(s)
        return bytes(out)
