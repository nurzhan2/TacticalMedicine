"""w8: turn the Interact1Capture frames into FMVR_NEXT/interact1.mp4 (captions + sound).

Frames: %TEMP%/fmvr_w8_video/f00000.jpg ... (30 fps game clock) + moments.txt ("<frame> <segment>").
Captions (RU, burned in): one bar per segment. Audio: low battle bed, and per segment the sound the player would
hear/feel: breathing at his mouth, the heartbeat (the haptic pulse made audible), Erlan's voice lines, groans.
All sounds from Assets/Audio (CC0/CC-BY, ATTRIBUTION.md) and the project's own voice lines.

usage: python tools/make_interact_video.py [--frames DIR] [--out FILE]
"""
import argparse, glob, os, subprocess, sys, tempfile, wave

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SR = 44100
FPS = 30

CAPTIONS = {
    "tutorial": ("Обучение управлению · 60 секунд", "хват · рывок ремня · поворот воротка · щипок · волок двумя руками — на учебном ящике"),
    "pain": ("Боль: жгут затягивают", "Ерлан вздрагивает, кричит и хватает медика за запястье свободной рукой"),
    "voice": ("Сознание (AVPU): голос", "медик зовёт его — громкость микрофона; Ерлан открывает глаза, смотрит на медика, отвечает"),
    "pulse": ("Пульс: два пальца, ≥ 2 секунды", "шея и запястье · вибрация контроллера с его ЧСС, сила — по давлению · белая подсказка в обучении"),
    "breath": ("Дыхание: ухо у его рта, 3 секунды", "слышно дыхание (3D), видно движение груди · частота и глубина — из физиологии"),
    "refill": ("Капиллярное наполнение", "нажать на ноготь 1 с, отпустить: цвет возвращается через N секунд — по перфузии"),
    "shock": ("Шок виден", "кровопотеря растёт: кожа бледнеет, сереет, губы синеют, пот; обновление материалов ≤ 10 Гц"),
    "ghost": ("Физические руки", "перчатка останавливается на его груди, призрак показывает реальное положение руки"),
}


def find_ffmpeg():
    for p in glob.glob(os.path.join(tempfile.gettempdir(), "iff", "imageio_ffmpeg", "binaries", "ffmpeg*.exe")):
        return p
    return "ffmpeg"


def decode(ff, path):
    out = subprocess.run([ff, "-v", "error", "-i", path, "-f", "f32le", "-ac", "1", "-ar", str(SR), "-"],
                         stdout=subprocess.PIPE, check=True).stdout
    return np.frombuffer(out, dtype=np.float32).copy()


def put(mix, clip, t, gain):
    i = int(t * SR)
    if i >= len(mix) or i < 0 or len(clip) == 0:
        return
    n = min(len(clip), len(mix) - i)
    mix[i:i + n] += clip[:n] * gain


def loop_to(clip, n):
    if len(clip) == 0:
        return np.zeros(n, np.float32)
    return np.tile(clip, n // len(clip) + 1)[:n]


def font(sz, bold=True):
    for f in (("C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf"), "arial.ttf"):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            pass
    return ImageFont.load_default()


def title_card(path, w=1920, h=1080):
    im = Image.new("RGB", (w, h), (12, 12, 10))
    d = ImageDraw.Draw(im)
    def ctext(y, t, f, c):
        d.text(((w - d.textlength(t, font=f)) / 2, y), t, font=f, fill=c)
    ctext(300, "БАУЫР", font(150), (255, 199, 97))
    ctext(490, "Живой Ерлан и ощущение рук (этап w8)", font(54, False), (236, 232, 214))
    ctext(580, "пульс · дыхание · голос и AVPU · боль · капилляры · шок · позы рук · физические руки · обучение", font(30, False), (170, 170, 160))
    ctext(900, "Запись из редактора Unity: руки ведёт скрипт (ScriptedHandInput) — каждое действие выполняется руками, как в шлеме.", font(26, False), (130, 130, 122))
    im.save(path, quality=92)


def caption(src, dst, title, sub, alpha):
    im = Image.open(src).convert("RGB")
    w, h = im.size
    bar = Image.new("RGBA", (w, 132), (8, 8, 8, int(170 * alpha)))
    d = ImageDraw.Draw(bar)
    d.text((60, 18), title, font=font(46), fill=(255, 210, 120, int(255 * alpha)))
    d.text((60, 78), sub, font=font(28, False), fill=(235, 235, 225, int(255 * alpha)))
    im.paste(bar, (0, h - 150), bar)
    im.save(dst, quality=90)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--frames", default=os.path.join(tempfile.gettempdir(), "fmvr_w8_video"))
    ap.add_argument("--out", default="C:/Users/user/Desktop/FMVR_NEXT/interact1.mp4")
    a = ap.parse_args()
    ff = find_ffmpeg()
    frames = sorted(glob.glob(os.path.join(a.frames, "f*.jpg")))
    if not frames:
        sys.exit("no frames in " + a.frames)
    segs = []
    mp = os.path.join(a.frames, "moments.txt")
    for line in open(mp, encoding="utf-8"):
        p = line.split()
        if len(p) == 2:
            segs.append((int(p[0]), p[1]))
    segs.sort()
    work = tempfile.mkdtemp(prefix="fmvr_w8_")
    # captions burned into each frame (fade in over 0.4 s at each segment start)
    out_frames = []
    for i, fr in enumerate(frames):
        cur = None
        for f0, key in segs:
            if f0 <= i:
                cur = (f0, key)
        dst = os.path.join(work, "c%05d.jpg" % i)
        if cur and cur[1] in CAPTIONS:
            t, s = CAPTIONS[cur[1]]
            caption(fr, dst, t, s, min(1.0, (i - cur[0]) / 12.0 + 0.05))
        else:
            dst = fr
        out_frames.append(dst)
    title = os.path.join(work, "title.jpg")
    title_card(title)
    T0 = 3.5
    lst = os.path.join(work, "frames.txt")
    with open(lst, "w", encoding="utf-8") as f:
        f.write("file '%s'\nduration %.3f\n" % (title.replace("\\", "/"), T0))
        for fr in out_frames:
            f.write("file '%s'\nduration %.6f\n" % (fr.replace("\\", "/"), 1.0 / FPS))
        f.write("file '%s'\n" % out_frames[-1].replace("\\", "/"))
    dur = T0 + len(frames) / FPS
    n = int(dur * SR) + SR
    mix = np.zeros(n, np.float32)
    sfx = os.path.join(ROOT, "Assets", "Audio", "SFX")
    voice = os.path.join(ROOT, "Assets", "Audio", "Voice")
    S = lambda rel: decode(ff, os.path.join(sfx, rel))
    V = lambda rel: decode(ff, os.path.join(voice, rel))
    env = np.clip((np.arange(n) / SR - T0 + 0.5) / 2.0, 0.0, 1.0).astype(np.float32)
    mix += loop_to(S("battle_bed/385895_battlefield_ambience_loop.mp3"), n) * 0.22 * env
    mix += loop_to(S("artillery_rumble/675609_s16_31_bombardment_with_distant.mp3"), n) * 0.14 * env
    at = {k: T0 + f0 / FPS for f0, k in segs}
    nxt = {}
    for j, (f0, k) in enumerate(segs):
        nxt[k] = T0 + (segs[j + 1][0] if j + 1 < len(segs) else len(frames)) / FPS
    if "tutorial" in at:
        put(mix, S("velcro/536228_velcro_pull_short.mp3"), at["tutorial"] + 6.0, 0.5)
        put(mix, S("ratchet_click/151778_clip_in.mp3"), at["tutorial"] + 9.5, 0.6)
    if "pain" in at:
        t = at["pain"] + 7.0
        put(mix, S("ratchet_click/151778_clip_in.mp3"), t, 0.7)
        put(mix, V("hurts_ru.wav"), t + 0.3, 1.0)
        put(mix, S("pain_scream/179325_male_scream_7.mp3"), t + 2.3, 0.6)
        put(mix, V("take_off_kz.wav"), t + 4.0, 1.0)
    if "voice" in at:
        put(mix, V("look_at_me_ru.wav"), at["voice"] + 1.3, 0.9)   # the medic calls him
        put(mix, V("dizzy_ru.wav"), at["voice"] + 2.6, 1.0)        # Erlan mumbles
    if "pulse" in at:
        hb = S("heartbeat/216221_heartbeat_03.mp3")
        t, end = at["pulse"] + 1.5, nxt["pulse"]
        beat = hb[: int(0.45 * SR)]
        while t < end - 0.5:
            put(mix, beat, t, 0.55)
            t += 60.0 / 122.0
    if "breath" in at:
        br = S("breath_heavy/319103_heavy_breathing.mp3")
        put(mix, br[: int((nxt["breath"] - at["breath"]) * SR)], at["breath"] + 0.8, 0.8)
    if "shock" in at:
        put(mix, S("pain_groan/257706_groaning.mp3"), at["shock"] + 3.0, 0.45)
        put(mix, V("cold_ru.wav"), at["shock"] + 6.5, 0.8)
    out = np.tanh(mix * 0.95).astype(np.float32)
    fade = int(1.5 * SR)
    out[-fade:] *= np.linspace(1, 0, fade, dtype=np.float32)
    wav = os.path.join(work, "audio.wav")
    with wave.open(wav, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(out, -1, 1) * 32767).astype(np.int16).tobytes())
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    cmd = [ff, "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", lst, "-i", wav,
           "-vf", "scale=1920:1080,fps=30,format=yuv420p", "-c:v", "libx264", "-preset", "medium", "-crf", "22",
           "-c:a", "aac", "-b:a", "160k", "-shortest", "-movflags", "+faststart", a.out]
    subprocess.run(cmd, check=True)
    print("video: %s  (%.1f s, %d frames, %.1f MB)" % (a.out, dur, len(frames), os.path.getsize(a.out) / 1e6))


if __name__ == "__main__":
    main()
