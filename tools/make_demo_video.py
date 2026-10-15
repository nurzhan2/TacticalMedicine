"""w7: turn the DemoCapture frames into the presentation backup video.

Frames: %TEMP%/fmvr_video/f00000.jpg ... (30 fps game clock) + moments.txt ("<frame> <moment>").
Audio:  battle bed + distant artillery/gunfire (Assets/Audio/SFX, CC0/CC-BY, see ATTRIBUTION.md) + the instructor's
        Russian step lines (Assets/Resources/Radio) at the moments each step starts + helicopter at TACEVAC.
Output: H.264 1080p30 + AAC, default C:/Users/user/Desktop/FMVR_DEMO/FieldMedicVR_demo.mp4

usage: python tools/make_demo_video.py [--frames DIR] [--out FILE] [--ffmpeg EXE]
"""
import argparse, glob, os, random, subprocess, sys, tempfile, wave

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SR = 44100
FPS = 30


def find_ffmpeg(arg):
    if arg:
        return arg
    for p in glob.glob(os.path.join(tempfile.gettempdir(), "iff", "imageio_ffmpeg", "binaries", "ffmpeg*.exe")):
        return p
    return "ffmpeg"


def decode(ff, path):
    """Any audio file → mono float32 at SR."""
    out = subprocess.run([ff, "-v", "error", "-i", path, "-f", "f32le", "-ac", "1", "-ar", str(SR), "-"],
                         stdout=subprocess.PIPE, check=True).stdout
    return np.frombuffer(out, dtype=np.float32).copy()


def put(mix, clip, t, gain):
    i = int(t * SR)
    if i >= len(mix) or len(clip) == 0:
        return
    n = min(len(clip), len(mix) - i)
    mix[i:i + n] += clip[:n] * gain


def loop_to(clip, n):
    if len(clip) == 0:
        return np.zeros(n, np.float32)
    reps = n // len(clip) + 1
    return np.tile(clip, reps)[:n]


def title_card(path, w=1920, h=1080):
    im = Image.new("RGB", (w, h), (12, 12, 10))
    d = ImageDraw.Draw(im)
    def font(sz, bold=True):
        for f in ("C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf", "arial.ttf"):
            try:
                return ImageFont.truetype(f, sz)
            except OSError:
                pass
        return ImageFont.load_default()
    def ctext(y, t, f, c):
        tw = d.textlength(t, font=f)
        d.text(((w - tw) / 2, y), t, font=f, fill=c)
    ctext(330, "БАУЫР", font(150), (255, 199, 97))
    ctext(520, "Field Medic VR — тренажёр боевого медика (Meta Quest 3S)", font(46, False), (236, 232, 214))
    ctext(600, "ДЕМО: Ерлан ранен в поле · жгут · эвакуация в укрытие · тампонада · наклейка · одеяло · карточка · TACEVAC", font(30, False), (170, 170, 160))
    ctext(900, "Запись из редактора Unity: руки ведёт скрипт (ScriptedHandInput), каждое действие выполняется руками, как в шлеме.", font(26, False), (130, 130, 122))
    im.save(path, quality=92)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--frames", default=os.path.join(tempfile.gettempdir(), "fmvr_video"))
    ap.add_argument("--out", default="C:/Users/user/Desktop/FMVR_DEMO/FieldMedicVR_demo.mp4")
    ap.add_argument("--ffmpeg", default=None)
    ap.add_argument("--title-seconds", type=float, default=3.5)
    a = ap.parse_args()
    ff = find_ffmpeg(a.ffmpeg)
    frames = sorted(glob.glob(os.path.join(a.frames, "f*.jpg")))
    if not frames:
        sys.exit("no frames in " + a.frames)
    moments = {}
    mp = os.path.join(a.frames, "moments.txt")
    if os.path.exists(mp):
        for line in open(mp, encoding="utf-8"):
            p = line.split()
            if len(p) == 2:
                moments.setdefault(p[1], int(p[0]))
    work = tempfile.mkdtemp(prefix="fmvr_vid_")
    title = os.path.join(work, "title.jpg")
    title_card(title)
    lst = os.path.join(work, "frames.txt")
    with open(lst, "w", encoding="utf-8") as f:
        f.write("file '%s'\nduration %.3f\n" % (title.replace("\\", "/"), a.title_seconds))
        for fr in frames:
            f.write("file '%s'\nduration %.6f\n" % (fr.replace("\\", "/"), 1.0 / FPS))
        f.write("file '%s'\n" % frames[-1].replace("\\", "/"))
    T0 = a.title_seconds
    dur = T0 + len(frames) / FPS
    n = int(dur * SR) + SR
    mix = np.zeros(n, np.float32)

    sfx = os.path.join(ROOT, "Assets", "Audio", "SFX")
    radio = os.path.join(ROOT, "Assets", "Resources", "Radio")
    def S(rel):
        return decode(ff, os.path.join(sfx, rel))
    # beds (start after the title, fade in)
    bed = loop_to(S("battle_bed/385895_battlefield_ambience_loop.mp3"), n) * 0.55
    rumble = loop_to(S("artillery_rumble/675609_s16_31_bombardment_with_distant.mp3"), n) * 0.32
    wind = loop_to(S("wind_night/773075_wind_in_corn_field_on_20m_0305_p.mp3"), n) * 0.25
    env = np.clip((np.arange(n) / SR - T0 + 0.5) / 2.0, 0.0, 1.0).astype(np.float32)
    mix += (bed + rumble + wind) * env
    # distant events
    ev = ["explosion_far/108640_distant_explosion.mp3", "explosion_far/209769_explosion_distant.mp3",
          "gunfire_distant/158977_distant_gunfire_03.mp3", "gunfire_distant/158978_distant_gunfire_02.mp3",
          "artillery_outgoing/565794_distant_cannon_fire_simulated.mp3", "bomb_large/149966_muffled_distant_explosion.mp3",
          "mg_burst/165393_machine_gun_shots_fast.mp3", "artillery_outgoing/186945_cannon_boom5.mp3"]
    clips = [S(e) for e in ev]
    rng = random.Random(7)
    t = T0 + 2.0
    while t < dur - 2:
        put(mix, clips[rng.randrange(len(clips))], t, rng.uniform(0.25, 0.6))
        t += rng.uniform(4.0, 11.0)
    # instructor lines (RU) when each step starts; frame → seconds
    voice = [("open", "t_welcome", 0.3), ("hint", "h_tq_cuf", 0.0), ("tq", "h_drag", 1.2), ("drag", "h_time", 1.2),
             ("time", "t_pack_2", 1.2), ("pack", "t_chest_1", 1.2), ("seal", "h_blanket", 1.2), ("blanket", "h_card", 1.2),
             ("card", "h_radio", 1.2), ("heli_call", "h_load", 0.5), ("debrief", "t_done", 0.5)]
    for m, vid, off in voice:
        if m not in moments:
            continue
        p = os.path.join(radio, vid + "_ru.wav")
        if os.path.exists(p):
            put(mix, decode(ff, p), T0 + moments[m] / FPS + off, 1.0)
    if "heli_call" in moments:
        put(mix, S("heli_flyby/534855_helicopter_flyby.mp3"), T0 + moments["heli_call"] / FPS, 0.75)
    # master: soft limiter, fade out
    out = np.tanh(mix * 0.9).astype(np.float32)
    fade = int(1.5 * SR)
    out[-fade:] *= np.linspace(1, 0, fade, dtype=np.float32)
    wav = os.path.join(work, "audio.wav")
    with wave.open(wav, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(out, -1, 1) * 32767).astype(np.int16).tobytes())
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    cmd = [ff, "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", lst, "-i", wav,
           "-vf", "scale=1920:1080,fps=30,format=yuv420p", "-c:v", "libx264", "-preset", "medium", "-crf", "21",
           "-c:a", "aac", "-b:a", "160k", "-shortest", "-movflags", "+faststart", a.out]
    subprocess.run(cmd, check=True)
    print("video: %s  (%.1f s, %d frames, %.1f MB)" % (a.out, dur, len(frames), os.path.getsize(a.out) / 1e6))


if __name__ == "__main__":
    main()
