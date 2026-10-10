"""Radio / instructor placeholder lines.

    python tools/radio_post.py <raw_dir> <out_dir> <radio_lines.tsv>

radio: pitched down to a male register, band-passed 350-2900 Hz, soft-clipped, hiss, squelch clicks.
instructor: pitched down slightly, clean. Output 22.05 kHz 16-bit mono WAV, peak -1 dBFS.
"""
import csv
import os
import sys

import numpy as np
from scipy.signal import butter, lfilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import voice_post as vp  # noqa: E402

SR = 22050


def bandpass(x, lo, hi):
    b, a = butter(4, [lo / (SR / 2), hi / (SR / 2)], btype="band")
    return lfilter(b, a, x)


def radio(x, rng):
    y = vp.pitch_shift(x, 0.72)
    y = bandpass(y, 350, 2900)
    y = np.tanh(y * 3.2) / np.tanh(3.2)
    hiss = bandpass(rng.standard_normal(len(y)) * 0.03, 800, 4000)
    y = y * 0.85 + hiss
    n1, n2 = int(0.06 * SR), int(0.18 * SR)
    click = bandpass(rng.standard_normal(n1) * 0.5, 600, 3500) * np.linspace(1, 0, n1)
    tail = bandpass(rng.standard_normal(n2) * 0.25, 500, 4000) * np.linspace(1, 0, n2) ** 2
    return np.concatenate([click, np.zeros(int(0.05 * SR)), y, tail])


def instructor(x):
    return bandpass(vp.pitch_shift(x, 0.80), 90, 7000)


def main():
    raw, out, tsv = sys.argv[1:4]
    os.makedirs(out, exist_ok=True)
    rng = np.random.default_rng(5)
    with open(tsv, encoding="utf-8") as f:
        rows = list(csv.DictReader(f, delimiter="\t"))
    n = 0
    for r in rows:
        for lang in ("ru", "kz"):
            p = os.path.join(raw, f"{r['id']}_{lang}.wav")
            if not os.path.exists(p):
                continue
            x, _ = vp.read_wav(p)
            y = radio(x, rng) if r["speaker"] == "radio" else instructor(x)
            y = y / (np.max(np.abs(y)) + 1e-6) * 0.89
            vp.write_wav(os.path.join(out, f"{r['id']}_{lang}.wav"), y)
            n += 1
    print(f"[radio_post] wrote {n} files to {out}")


if __name__ == "__main__":
    main()
