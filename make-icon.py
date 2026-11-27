# 파자마 캡쳐 아이콘(icon.png, icon.ico)을 그린다. 다시 만들려면: python make-icon.py (Pillow 필요)
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).parent
S = 1024  # 크게 그린 뒤 줄여서 가장자리를 매끄럽게 한다
PINK, DEEP, CREAM, DARK = (255, 143, 163), (255, 111, 138), (253, 250, 245), (51, 48, 44)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((40, 40, S - 40, S - 40), radius=220, fill=PINK)
# 캡쳐 모서리 표시(네 귀퉁이 꺾쇠)
w, m, L = 56, 150, 190
for x, y, dx, dy in ((m, m, 1, 1), (S - m, m, -1, 1), (m, S - m, 1, -1), (S - m, S - m, -1, -1)):
    d.rounded_rectangle((min(x, x + dx * L), min(y, y + dy * w) if dy > 0 else y - w, max(x, x + dx * L), max(y, y + dy * w) if dy > 0 else y), radius=26, fill=CREAM)
    d.rounded_rectangle((min(x, x + dx * w) if dx > 0 else x - w, min(y, y + dy * L), max(x, x + dx * w) if dx > 0 else x, max(y, y + dy * L)), radius=26, fill=CREAM)
# 가운데 녹화 점(진분홍) + 테두리
c, r = S // 2, 165
d.ellipse((c - r - 34, c - r - 34, c + r + 34, c + r + 34), fill=CREAM)
d.ellipse((c - r, c - r, c + r, c + r), fill=DEEP)
d.ellipse((c - 70, c - 95, c - 10, c - 35), fill=(255, 214, 222))  # 반짝임

img.resize((256, 256), Image.LANCZOS).save(HERE / "icon.png")
img.resize((256, 256), Image.LANCZOS).save(HERE / "icon.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print("만듦:", HERE / "icon.ico")
