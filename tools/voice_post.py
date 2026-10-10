"""Post-process TTS placeholder voice lines: male register + pain strain.

    python tools/voice_post.py <raw_dir> <out_dir> <voice_lines.tsv>

Per line: pitch shift down (phase vocoder, duration kept) so the female TTS voice sits in a male register,
then by `strain` (0..1): extra pitch drop, tremor (6-9 Hz amplitude + pitch wobble), breathiness (band-passed
noise following the speech envelope), soft clipping (tanh, never hard clipping), short inhale before
panic/scream lines. Output: 22.05 kHz 16-bit mono WAV, peak -1 dBFS.
"""
import csv
import os
import sys
import wave

import numpy as np
from scipy.signal import butter, lfilter, resample

SR = 22050


def read_wav(p):
    with wave.open(p, "rb") as w:
        sr = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    return x, sr


def write_wav(p, x):
    y = np.clip(x, -1, 1)
    with wave.open(p, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((y * 32767).astype(np.int16).tobytes())


def stft(x, n=1024, hop=256):
    win = np.hanning(n).astype(np.float32)
    pad = np.concatenate([np.zeros(n), x, np.zeros(n)])
    frames = [np.fft.rfft(pad[i:i + n] * win) for i in range(0, len(pad) - n, hop)]
    return np.array(frames), win


def istft(S, win, n=1024, hop=256, length=None):
    out = np.zeros(hop * len(S) + n)
    norm = np.zeros_like(out)
    for i, f in enumerate(S):
        out[i * hop:i * hop + n] += np.fft.irfft(f, n) * win
        norm[i * hop:i * hop + n] += win ** 2
    out = out / np.maximum(norm, 1e-3)
    out = out[n:]
    return out[:length] if length else out


def time_stretch(x, rate, n=1024, hop=256):
    """Phase vocoder: rate > 1 -> shorter."""
    S, win = stft(x, n, hop)
    steps = np.arange(0, len(S) - 2, rate)
    phase = np.angle(S[0])
    omega = 2 * np.pi * hop * np.arange(S.shape[1]) / n
    out = []
    for t in steps:
        i = int(t)
        frac = t - i
        mag = (1 - frac) * np.abs(S[i]) + frac * np.abs(S[i + 1])
        out.append(mag * np.exp(1j * phase))
        dphi = np.angle(S[i + 1]) - np.angle(S[i]) - omega
        dphi -= 2 * np.pi * np.round(dphi / (2 * np.pi))
        phase = phase + omega + dphi
    return istft(np.array(out), win, n, hop)


def pitch_shift(x, factor):
    """factor < 1 lowers pitch, duration preserved."""
    shortened = time_stretch(x, 1.0 / factor)    # duration * factor, same pitch
    return resample(shortened, int(len(shortened) / factor))  # stretch back -> pitch * factor


def envelope(x, ms=25):
    k = max(1, int(SR * ms / 1000))
    return np.convolve(np.abs(x), np.ones(k) / k, mode="same")


def process(x, emotion, strain, speaker, seed):
    rng = np.random.default_rng(seed)
    base = 0.70 if speaker == "casualty" else 0.74
    factor = base - 0.05 * strain
    y = pitch_shift(x, factor)
    t = np.arange(len(y)) / SR
    env = envelope(y)
    env = env / (env.max() + 1e-6)
    if strain > 0:
        trem_f = rng.uniform(6, 9)
        y *= 1 + 0.25 * strain * np.sin(2 * np.pi * trem_f * t)
        b, a = butter(2, [900 / (SR / 2), 5000 / (SR / 2)], btype="band")
        noise = lfilter(b, a, rng.standard_normal(len(y)))
        noise /= np.abs(noise).max() + 1e-6
        y = y + noise * env * 0.22 * strain
        drive = 1 + 3 * strain
        y = np.tanh(y * drive) / np.tanh(drive)
    if emotion in ("weak", "gasp"):
        b, a = butter(2, 3200 / (SR / 2))
        y = lfilter(b, a, y) * 0.75
        b, a = butter(2, [700 / (SR / 2), 4000 / (SR / 2)], btype="band")
        y += lfilter(b, a, rng.standard_normal(len(y))) * env * 0.08
    if emotion in ("panic", "scream", "gasp"):
        n = int(SR * 0.35)
        b, a = butter(2, [500 / (SR / 2), 3500 / (SR / 2)], btype="band")
        inhale = lfilter(b, a, rng.standard_normal(n)) * np.sin(np.linspace(0, np.pi, n)) ** 2 * 0.12
        y = np.concatenate([inhale, np.zeros(int(SR * 0.05)), y])
    fade = int(SR * 0.02)
    y[:fade] *= np.linspace(0, 1, fade)
    y[-fade:] *= np.linspace(1, 0, fade)
    # trim silence at the end
    e = envelope(y, 10)
    idx = np.where(e > 0.01 * e.max())[0]
    if len(idx):
        y = y[: min(len(y), idx[-1] + int(SR * 0.15))]
    return y / (np.abs(y).max() + 1e-6) * 0.89


def main():
    raw, out, table = sys.argv[1:4]
    os.makedirs(out, exist_ok=True)
    with open(table, encoding="utf-8") as f:
        rows = list(csv.DictReader(f, delimiter="\t"))
    for k, r in enumerate(rows):
        for lang in ("ru", "kz"):
            src = os.path.join(raw, f"{r['id']}_{lang}.wav")
            if not os.path.exists(src):
                print("missing", src)
                continue
            x, sr = read_wav(src)
            if sr != SR:
                x = resample(x, int(len(x) * SR / sr))
            y = process(x, r["emotion"], float(r["strain"]), r["speaker"], seed=k * 2 + (lang == "kz"))
            write_wav(os.path.join(out, f"{r['id']}_{lang}.wav"), y)
    print("[voice_post] wrote", len(rows) * 2, "lines to", out)


if __name__ == "__main__":
    main()
