"""Radio / instructor placeholder lines.

    python tools/radio_post.py <raw_dir> <out_dir> <radio_lines.tsv> [--real] [--only id1,id2] [--sr 44100]

radio: pitched down to a male register, band-passed 350-2900 Hz, soft-clipped, hiss, squelch clicks.
instructor: pitched down slightly, clean. Output 22.05 kHz 16-bit mono WAV, peak -1 dBFS.
--real (w9, ElevenLabs): the raw files are real voices -> no pitch shift (any sample rate is resampled),
radio keeps the band-pass/squelch, instructor only a gentle 70 Hz-9 kHz clean-up.
"""
import csv
import os
import sys

import numpy as np
from scipy.signal import butter, lfilter, resample

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import voice_post as vp  # noqa: E402

SR = 22050


def bandpass(x, lo, hi):
    b, a = butter(4, [lo / (SR / 2), hi / (SR / 2)], btype="band")
    return lfilter(b, a, x)


def radio(x, rng, real=False):
    y = x if real else vp.pitch_shift(x, 0.72)
    y = bandpass(y, 350, 2900)
    y = np.tanh(y * 3.2) / np.tanh(3.2)
    hiss = bandpass(rng.standard_normal(len(y)) * 0.03, 800, 4000)
    y = y * 0.85 + hiss
    n1, n2 = int(0.06 * SR), int(0.18 * SR)
    click = bandpass(rng.standard_normal(n1) * 0.5, 600, 3500) * np.linspace(1, 0, n1)
    tail = bandpass(rng.standard_normal(n2) * 0.25, 500, 4000) * np.linspace(1, 0, n2) ** 2
    return np.concatenate([click, np.zeros(int(0.05 * SR)), y, tail])


def instructor(x, real=False):
    if real:
        return bandpass(x, 70, 9000)
    return bandpass(vp.pitch_shift(x, 0.80), 90, 7000)


def main():
    global SR
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    raw, out, tsv = args[:3]
    real = "--real" in sys.argv
    if "--sr" in sys.argv:
        SR = int(sys.argv[sys.argv.index("--sr") + 1])
        vp.SR = SR
    only = None
    if "--only" in sys.argv:
        only = set(sys.argv[sys.argv.index("--only") + 1].split(","))
    os.makedirs(out, exist_ok=True)
    rng = np.random.default_rng(5)
    with open(tsv, encoding="utf-8") as f:
        rows = list(csv.DictReader(f, delimiter="\t"))
    n = 0
    for r in rows:
        if only is not None and r["id"] not in only:
            continue
        for lang in ("ru", "kz"):
            p = os.path.join(raw, f"{r['id']}_{lang}.wav")
            if not os.path.exists(p):
                continue
            x, sr = vp.read_wav(p)
            if sr != SR:
                x = resample(x, int(len(x) * SR / sr))
            y = radio(x, rng, real) if r["speaker"] == "radio" else instructor(x, real)
            y = y / (np.max(np.abs(y)) + 1e-6) * 0.89
            vp.write_wav(os.path.join(out, f"{r['id']}_{lang}.wav"), y)
            n += 1
    print(f"[radio_post] wrote {n} files to {out}")


if __name__ == "__main__":
    main()
