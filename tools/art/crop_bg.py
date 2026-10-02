"""Кадрирует арт под окно лаунчера 1040x620 (с запасом x2 под HiDPI)."""
import sys
from PIL import Image
src, out, castle_x, crop_w, top = sys.argv[1], sys.argv[2], float(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
im = Image.open(src)
W, H = im.size
ratio = 1040 / 620
crop_h = round(crop_w / ratio)
cx = 0.43 * W                     # центр крепости в исходнике
left = round(cx - castle_x * crop_w)
left = max(0, min(left, W - crop_w))
top = max(0, min(top, H - crop_h))
box = (left, top, left + crop_w, top + crop_h)
im.crop(box).resize((2080, 1240), Image.LANCZOS).save(out, quality=90, optimize=True, progressive=True)
print("box", box, "->", out)
