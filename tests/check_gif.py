# 합성 GIF 시험 결과 확인: python tests/check_gif.py <selftest gif 로 만든 폴더>  (Pillow·NumPy 필요)
import sys
from pathlib import Path

import numpy as np
from PIL import Image

folder = Path(sys.argv[1])
im = Image.open(folder / "test.gif")
expected_frames = [0, 1, 2, 3, 6, 8]          # 같은 그림·너무 촘촘한 장면은 합쳐져야 한다
expected_ms = [100, 100, 100, 200, 500, 400]  # 장면마다 보이는 시간
ok = im.n_frames == len(expected_frames)
print("장면 수", im.n_frames, "통과" if ok else "실패")
for i, src in enumerate(expected_frames):
    im.seek(i)
    got = np.asarray(im.convert("RGB")).astype(int)
    want = np.asarray(Image.open(folder / f"frame_{src}.png").convert("RGB")).astype(int)
    diff = int(np.abs(got - want).max())
    ms = im.info.get("duration")
    good = diff <= 8 and ms == expected_ms[i]
    ok &= good
    print(f"장면 {i}: 색 차이 최대 {diff}, 시간 {ms}ms", "통과" if good else "실패")
print("모두 통과" if ok else "실패 있음")
sys.exit(0 if ok else 1)
