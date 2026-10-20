"""Erlan's ElevenLabs lines -> Unity-ready voice clips.

    python tools/erlan_post.py [raw_dir] [out_dir]

Decodes SourceAssets/Voice/eleven_raw_erlan/<id>_ru.mp3 (44.1 kHz, PyAV - no ffmpeg binary needed), mixes to
mono, resamples to 22.05 kHz, trims leading/trailing silence (-45 dBFS gate, 60 ms kept), normalises to about
-18 dBFS RMS (peak capped at -1 dBFS), 8 ms fade in / 40 ms fade out, writes 16-bit mono WAV to
Assets/Resources/Erlan/<id>_ru.wav (loaded by FieldMedic.Audio.VoiceOver.ErlanClip).
"""
import os
import sys
import wave

import av
import numpy as np
from scipy.signal import resample_poly

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SR = 22050


def decode(path):
    c = av.open(path)
    rs = av.AudioResampler(format="flt", layout="mono", rate=44100)
    chunks = []
    for frame in c.decode(audio=0):
        for f in rs.resample(frame):
            chunks.append(f.to_ndarray().reshape(-1))
    for f in rs.resample(None):
        chunks.append(f.to_ndarray().reshape(-1))
    c.close()
    return np.concatenate(chunks).astype(np.float64)


def trim(x, sr, gate_db=-45.0, keep_ms=60):
    win = int(sr * 0.01)
    n = len(x) // win
    if n == 0:
        return x
    rms = np.sqrt(np.mean(x[: n * win].reshape(n, win) ** 2, axis=1) + 1e-12)
    loud = np.where(20 * np.log10(rms) > gate_db)[0]
    if len(loud) == 0:
        return x
    keep = int(sr * keep_ms / 1000)
    a = max(0, loud[0] * win - keep)
    b = min(len(x), (loud[-1] + 1) * win + keep)
    return x[a:b]


def process(src, dst):
    x = decode(src)
    x = resample_poly(x, 1, 2)  # 44.1k -> 22.05k
    x = x - np.mean(x)
    x = trim(x, SR)
    # RMS over the voiced part only (frames above -40 dBFS), target -18 dBFS
    win = int(SR * 0.02)
    n = max(1, len(x) // win)
    fr = x[: n * win].reshape(n, win) if len(x) >= win else x.reshape(1, -1)
    frms = np.sqrt(np.mean(fr ** 2, axis=1) + 1e-12)
    voiced = frms[20 * np.log10(frms) > -40]
    rms = np.sqrt(np.mean(voiced ** 2)) if len(voiced) else np.sqrt(np.mean(x ** 2) + 1e-12)
    g = 10 ** (-18 / 20) / max(rms, 1e-6)
    peak = np.max(np.abs(x)) * g
    if peak > 10 ** (-1 / 20):
        g *= 10 ** (-1 / 20) / peak
    x = x * g
    fi, fo = int(SR * 0.008), int(SR * 0.04)
    x[:fi] *= np.linspace(0, 1, fi)
    x[-fo:] *= np.linspace(1, 0, fo)
    y = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(dst, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(y.tobytes())
    return len(y) / SR, 20 * np.log10(np.sqrt(np.mean((y / 32768.0) ** 2)) + 1e-12)


def main():
    raw = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "SourceAssets", "Voice", "eleven_raw_erlan")
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "Assets", "Resources", "Erlan")
    os.makedirs(out, exist_ok=True)
    for f in sorted(os.listdir(raw)):
        if not f.endswith(".mp3"):
            continue
        dur, db = process(os.path.join(raw, f), os.path.join(out, f[:-4] + ".wav"))
        print(f"{f[:-4]}: {dur:.2f} s, {db:.1f} dBFS RMS")


if __name__ == "__main__":
    main()
