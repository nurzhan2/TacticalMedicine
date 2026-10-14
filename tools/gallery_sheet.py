"""Contact sheets from Temp/w1_gallery/<category>__<slug>.png (Editor/AssetGallery) -> docs/screens/v2_w1_*.jpg."""
import os, sys
from collections import defaultdict
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
src = os.path.join(ROOT, "Logs", "w1_gallery")
dst = os.path.join(ROOT, "docs", "screens")
os.makedirs(dst, exist_ok=True)
groups = defaultdict(list)
for f in sorted(os.listdir(src)):
    if f.endswith(".png") and "__" in f:
        cat, slug = f[:-4].split("__", 1)
        groups[cat].append((slug, os.path.join(src, f)))
cell_w, cell_h, cols = 400, 300, 4
for cat, items in groups.items():
    for page in range(0, len(items), 12):
        chunk = items[page:page + 12]
        rows = (len(chunk) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * cell_w, rows * cell_h), (20, 20, 20))
        d = ImageDraw.Draw(sheet)
        for i, (slug, p) in enumerate(chunk):
            im = Image.open(p).convert("RGB").resize((cell_w, cell_h))
            x, y = (i % cols) * cell_w, (i // cols) * cell_h
            sheet.paste(im, (x, y))
            d.rectangle([x, y, x + cell_w, y + 18], fill=(0, 0, 0))
            d.text((x + 4, y + 3), slug, fill=(255, 230, 120))
        name = "v2_w1_gallery_%s%s.jpg" % (cat, "" if page == 0 else "_%d" % (page // 12 + 1))
        sheet.save(os.path.join(dst, name), quality=85)
        print("sheet", name)
lp = os.path.join(src, "lineup.png")
if os.path.exists(lp):
    Image.open(lp).convert("RGB").save(os.path.join(dst, "v2_w1_lineup.jpg"), quality=85)
    print("sheet v2_w1_lineup.jpg")
