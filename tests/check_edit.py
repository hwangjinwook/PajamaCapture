# 간단 편집 시험 결과 확인: python tests/check_edit.py <원본.gif> <selftest edit 로 만든 폴더>  (Pillow·NumPy 필요)
# 1) 편집 창이 불러온 원본 프레임이 Pillow 로 푼 프레임과 같은지(덧그림 GIF 를 제대로 합쳤는지)
# 2) 저장한 GIF 의 크기·프레임 수·프레임 시간·그림이 기대값과 같은지
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

source, folder = Path(sys.argv[1]), Path(sys.argv[2])
want = json.loads((folder / "expected.json").read_text(encoding="utf-8"))
ok = True


def rgb(im):
    return np.asarray(im.convert("RGB")).astype(int)


src = Image.open(source)
good = src.n_frames == want["source_frames"]
ok &= good
print("원본 프레임 수", src.n_frames, "통과" if good else "실패")
for i in range(src.n_frames):
    src.seek(i)
    diff = int(np.abs(rgb(src) - rgb(Image.open(folder / f"src_{i}.png"))).max())
    ms = src.info.get("duration")
    good = diff == 0 and ms == want["source_durations"][i]
    ok &= good
    print(f"원본 {i}: 불러온 그림 차이 {diff}, 시간 {ms}ms", "통과" if good else "실패")

out = Image.open(folder / "edited.gif")
good = out.size == (want["width"], want["height"]) and out.n_frames == want["frames"]
ok &= good
print("결과 크기", out.size, "프레임 수", out.n_frames, "통과" if good else "실패")
for i in range(min(out.n_frames, want["frames"])):
    out.seek(i)
    diff = int(np.abs(rgb(out) - rgb(Image.open(folder / f"want_{i}.png"))).max())
    ms = out.info.get("duration")
    good = diff <= 12 and ms == want["durations"][i]
    ok &= good
    print(f"결과 {i}: 색 차이 최대 {diff}, 시간 {ms}ms (기대 {want['durations'][i]}ms)", "통과" if good else "실패")
print("모두 통과" if ok else "실패 있음")
sys.exit(0 if ok else 1)
