"""Hint-card pictograms for the white Training panel (own work, CC0).

  python tools/make_hint_icons.py  -> Assets/Resources/HintIcons/<name>.png (128x128 RGBA)

Simple flat pictograms in dark slate with a red accent, drawn at 4x and downsampled (anti-aliased). Also writes
the green check, the amber warning, a radial glow (body-target highlight) and the UI chime sounds
(Assets/Resources/UI/hint_done.wav, hint_wrong.wav, hint_show.wav).
"""
import math, os, wave
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "HintIcons")
UI = os.path.join(ROOT, "Assets", "Resources", "UI")
S = 512           # drawing size
N = 128           # output size
INK = (43, 49, 56, 255)
RED = (196, 38, 32, 255)
OLIVE = (92, 104, 58, 255)
SKIN = (214, 170, 140, 255)
WHITE = (255, 255, 255, 255)
GREEN = (46, 170, 82, 255)
AMBER = (240, 168, 30, 255)
BLUE = (52, 110, 190, 255)


def canvas():
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)


def save(im, name):
    os.makedirs(OUT, exist_ok=True)
    im.resize((N, N), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


def line(d, pts, w=34, fill=INK):
    d.line(pts, fill=fill, width=w, joint="curve")
    for p in (pts[0], pts[-1]):
        d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=fill)


def person(d, x, y, s=1.0, fill=INK, run=False):
    """stick figure, head at (x, y)"""
    r = 34 * s
    d.ellipse([x - r, y - r, x + r, y + r], fill=fill)
    if run:
        line(d, [(x, y + r), (x - 30 * s, y + 150 * s)], int(36 * s), fill)                  # torso leaning
        line(d, [(x - 30 * s, y + 150 * s), (x + 40 * s, y + 220 * s), (x + 20 * s, y + 300 * s)], int(32 * s), fill)
        line(d, [(x - 30 * s, y + 150 * s), (x - 110 * s, y + 220 * s), (x - 170 * s, y + 200 * s)], int(32 * s), fill)
        line(d, [(x - 10 * s, y + 70 * s), (x + 70 * s, y + 120 * s), (x + 110 * s, y + 70 * s)], int(28 * s), fill)
        line(d, [(x - 10 * s, y + 70 * s), (x - 90 * s, y + 90 * s)], int(28 * s), fill)
    else:
        line(d, [(x, y + r), (x, y + 160 * s)], int(36 * s), fill)


def lying(d, y=330, fill=INK, wound=None):
    """casualty lying on his back, head left"""
    d.ellipse([60, y - 40, 140, y + 40], fill=fill)
    line(d, [(150, y), (330, y)], 46, fill)
    line(d, [(330, y), (450, y + 6)], 36, fill)
    line(d, [(330, y + 10), (452, y + 40)], 34, fill)
    line(d, [(190, y + 10), (260, y + 48)], 26, fill)
    if wound:
        d.ellipse([wound[0] - 22, wound[1] - 22, wound[0] + 22, wound[1] + 22], fill=RED)


def icon_run():
    im, d = canvas()
    person(d, 300, 90, 1.0, INK, run=True)
    for i, yy in enumerate((200, 260, 320)):
        line(d, [(60, yy), (140 - i * 20, yy)], 18, (120, 128, 136, 255))
    save(im, "run")


def icon_tq():
    im, d = canvas()
    # thigh
    d.rounded_rectangle([170, 40, 340, 480], 70, fill=SKIN)
    # band
    d.rounded_rectangle([140, 150, 370, 215], 20, fill=INK)
    # windlass rod
    line(d, [(200, 120), (310, 245)], 30, INK)
    d.rounded_rectangle([330, 160, 420, 205], 12, fill=RED)  # red tab
    # wound below
    d.ellipse([228, 330, 282, 384], fill=RED)
    line(d, [(255, 384), (255, 440)], 16, RED)
    # arrow "high"
    line(d, [(450, 330), (450, 200)], 22, GREEN)
    d.polygon([(410, 220), (490, 220), (450, 160)], fill=GREEN)
    save(im, "tq")


def icon_drag():
    im, d = canvas()
    lying(d, 360)
    # medic pulling the strap at the head end
    person(d, 120, 110, 0.85, OLIVE)
    line(d, [(120, 190), (110, 300)], 18, OLIVE)
    line(d, [(110, 300), (100, 330)], 14, (90, 90, 90, 255))
    # arrow toward cover
    line(d, [(470, 120), (300, 120)], 24, GREEN)
    d.polygon([(310, 80), (310, 160), (240, 120)], fill=GREEN)
    d.rectangle([20, 40, 60, 480], fill=(150, 120, 100, 255))  # wall
    save(im, "drag")


def hand(d, x, y, s=1.0, fill=INK, glove=True):
    col = fill
    d.rounded_rectangle([x - 90 * s, y - 70 * s, x + 90 * s, y + 110 * s], int(50 * s), fill=col)
    for i in range(4):
        fx = x - 70 * s + i * 47 * s
        d.rounded_rectangle([fx - 20 * s, y - 200 * s + (i in (0, 3)) * 30 * s, fx + 20 * s, y - 40 * s], int(20 * s), fill=col)
    d.rounded_rectangle([x + 70 * s, y - 40 * s, x + 170 * s, y + 10 * s], int(25 * s), fill=col)
    if glove:
        d.rectangle([x - 95 * s, y + 90 * s, x + 95 * s, y + 160 * s], fill=(70, 78, 86, 255))


def icon_gloves():
    im, d = canvas()
    hand(d, 230, 280, 1.0, (40, 44, 50, 255))
    d.ellipse([330, 330, 470, 470], fill=GREEN)
    line(d, [(360, 400), (392, 432), (444, 370)], 24, WHITE)
    save(im, "gloves")


def icon_pulse():
    im, d = canvas()
    # foot
    d.rounded_rectangle([90, 120, 220, 330], 60, fill=SKIN)
    d.rounded_rectangle([90, 260, 440, 380], 60, fill=SKIN)
    # two fingers
    line(d, [(300, 120), (300, 280)], 30, INK)
    line(d, [(350, 130), (350, 285)], 30, INK)
    # pulse line crossed
    pts = [(40, 450), (150, 450), (180, 410), (210, 490), (240, 450), (470, 450)]
    d.line(pts, fill=RED, width=16, joint="curve")
    save(im, "pulse")


def icon_marker():
    im, d = canvas()
    d.polygon([(130, 400), (360, 170), (420, 230), (190, 460)], fill=INK)
    d.polygon([(130, 400), (190, 460), (100, 490)], fill=(20, 20, 20, 255))
    d.rounded_rectangle([60, 60, 300, 140], 16, outline=INK, width=14)
    d.text((90, 75), "T 14:32", fill=INK)
    for i in range(3):
        line(d, [(90 + i * 60, 100), (120 + i * 60, 100)], 14, RED)
    save(im, "marker")


def icon_sweep():
    im, d = canvas()
    lying(d, 300, wound=(395, 340))
    hand(d, 240, 210, 0.45, OLIVE, glove=False)
    for i in range(3):
        line(d, [(150 + i * 70, 120), (200 + i * 70, 120)], 16, (120, 128, 136, 255))
    line(d, [(330, 120), (450, 120)], 20, GREEN)
    d.polygon([(440, 95), (440, 145), (490, 120)], fill=GREEN)
    save(im, "sweep")


def icon_bandage():
    im, d = canvas()
    d.rounded_rectangle([60, 180, 460, 340], 60, fill=SKIN)
    for i in range(4):
        d.rounded_rectangle([150 + i * 50, 160, 190 + i * 50, 360], 14, fill=(236, 236, 230, 255), outline=(170, 170, 160, 255), width=6)
    d.rounded_rectangle([210, 230, 300, 290], 10, fill=OLIVE)
    d.ellipse([350, 380, 470, 500], fill=(236, 236, 230, 255), outline=(170, 170, 160, 255), width=10)
    d.ellipse([390, 420, 430, 460], fill=(170, 170, 160, 255))
    save(im, "bandage")


def icon_blanket():
    im, d = canvas()
    lying(d, 300)
    d.polygon([(150, 250), (470, 250), (480, 360), (140, 360)], fill=(214, 168, 60, 255))
    for i in range(5):
        line(d, [(170 + i * 60, 262), (160 + i * 62, 348)], 8, (240, 210, 120, 255))
    save(im, "blanket")


def icon_card():
    im, d = canvas()
    d.rounded_rectangle([110, 60, 400, 460], 24, fill=(236, 232, 214, 255), outline=INK, width=12)
    d.rectangle([110, 60, 400, 130], fill=RED)
    for i in range(5):
        line(d, [(150, 180 + i * 55), (360 - (i % 2) * 60, 180 + i * 55)], 14, INK)
    save(im, "card")


def icon_radio():
    im, d = canvas()
    d.rounded_rectangle([170, 150, 340, 470], 30, fill=OLIVE)
    line(d, [(300, 150), (300, 40)], 22, INK)
    d.rounded_rectangle([200, 190, 310, 260], 10, fill=(160, 210, 140, 255))
    d.ellipse([215, 300, 295, 380], fill=INK)
    for r in (70, 120, 170):
        d.arc([330 - r + 40, 130 - r, 330 + r + 40, 130 + r], -60, 30, fill=BLUE, width=16)
    save(im, "radio")


def icon_heli():
    im, d = canvas()
    d.ellipse([90, 200, 340, 330], fill=OLIVE)
    d.rectangle([300, 245, 470, 275], fill=OLIVE)
    d.polygon([(440, 200), (480, 200), (470, 275), (450, 275)], fill=OLIVE)
    line(d, [(40, 170), (400, 170)], 16, INK)
    line(d, [(215, 170), (215, 205)], 18, INK)
    line(d, [(140, 360), (310, 360)], 14, INK)
    line(d, [(170, 320), (160, 360)], 12, INK)
    line(d, [(280, 320), (290, 360)], 12, INK)
    d.ellipse([110, 225, 190, 290], fill=(150, 190, 210, 255))
    # LZ marker H
    d.rectangle([180, 410, 340, 480], fill=(230, 120, 30, 255))
    save(im, "heli")


def icon_shears():
    im, d = canvas()
    line(d, [(140, 120), (360, 380)], 30, (150, 158, 166, 255))
    line(d, [(360, 120), (140, 380)], 30, (150, 158, 166, 255))
    d.ellipse([60, 330, 200, 470], outline=INK, width=30)
    d.ellipse([300, 330, 440, 470], outline=INK, width=30)
    save(im, "shears")


def icon_gauze():
    im, d = canvas()
    d.ellipse([120, 120, 400, 400], fill=(236, 236, 230, 255), outline=(170, 170, 160, 255), width=12)
    d.ellipse([220, 220, 300, 300], fill=(190, 190, 180, 255))
    line(d, [(380, 300), (470, 470)], 50, (236, 236, 230, 255))
    d.ellipse([80, 380, 180, 480], fill=RED)
    save(im, "gauze")


def icon_press():
    im, d = canvas()
    d.rounded_rectangle([60, 330, 460, 470], 50, fill=SKIN)
    d.ellipse([220, 360, 300, 420], fill=RED)
    hand(d, 260, 260, 0.75, INK)
    line(d, [(260, 20), (260, 110)], 24, GREEN)
    d.polygon([(220, 100), (300, 100), (260, 150)], fill=GREEN)
    save(im, "press")


def icon_roll():
    im, d = canvas()
    d.ellipse([60, 200, 150, 290], fill=INK)
    d.rounded_rectangle([160, 190, 440, 300], 50, fill=INK)
    d.arc([140, 40, 460, 360], 200, 340, fill=GREEN, width=26)
    d.polygon([(420, 120), (470, 170), (400, 190)], fill=GREEN)
    save(im, "roll")


def icon_seal():
    im, d = canvas()
    d.rounded_rectangle([60, 80, 440, 460], 60, fill=SKIN)
    d.ellipse([140, 150, 360, 370], fill=(40, 44, 50, 230), outline=INK, width=10)
    d.ellipse([220, 230, 280, 290], fill=RED)
    d.rounded_rectangle([300, 330, 380, 380], 12, fill=(240, 240, 240, 255))
    save(im, "seal")


def icon_lungs():
    im, d = canvas()
    line(d, [(256, 60), (256, 200)], 30, INK)
    d.ellipse([90, 140, 245, 440], fill=(230, 140, 140, 255))
    d.ellipse([267, 140, 422, 440], fill=(230, 140, 140, 255))
    line(d, [(256, 200), (190, 260)], 22, INK)
    line(d, [(256, 200), (322, 260)], 22, INK)
    d.ellipse([290, 280, 350, 340], fill=RED)
    save(im, "lungs")


def icon_needle():
    im, d = canvas()
    for i in range(4):
        d.arc([60, 80 + i * 90, 452, 300 + i * 90], 200, 340, fill=(200, 180, 160, 255), width=18)
    line(d, [(400, 60), (220, 300)], 22, (150, 158, 166, 255))
    d.rounded_rectangle([370, 30, 450, 110], 14, fill=BLUE)
    d.ellipse([200, 290, 240, 330], fill=RED)
    save(im, "needle")


def icon_npa():
    im, d = canvas()
    # face profile
    d.polygon([(330, 60), (330, 470), (210, 470), (200, 330), (140, 300), (190, 260), (170, 180), (230, 60)], fill=SKIN)
    d.arc([140, 160, 360, 420], 120, 230, fill=(230, 120, 40, 255), width=26)
    d.ellipse([150, 240, 210, 290], fill=(230, 120, 40, 255))
    save(im, "npa")


def icon_check():
    im, d = canvas()
    d.ellipse([16, 16, S - 16, S - 16], fill=GREEN)
    line(d, [(140, 270), (220, 350), (380, 170)], 56, WHITE)
    save(im, "check")


def icon_warn():
    im, d = canvas()
    d.polygon([(256, 30), (490, 460), (22, 460)], fill=AMBER)
    line(d, [(256, 170), (256, 320)], 46, INK)
    d.ellipse([228, 365, 284, 421], fill=INK)
    save(im, "warn")


def icon_glow():
    im = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    px = im.load()
    for y in range(N):
        for x in range(N):
            r = math.hypot(x - N / 2 + 0.5, y - N / 2 + 0.5) / (N / 2)
            ring = math.exp(-((r - 0.62) / 0.16) ** 2)
            core = max(0.0, 1.0 - r) ** 2 * 0.45
            a = min(1.0, ring + core) * (1.0 if r < 1 else 0.0)
            px[x, y] = (255, 255, 255, int(255 * a))
    os.makedirs(OUT, exist_ok=True)
    im.save(os.path.join(OUT, "glow.png"))


def chime(path, freqs, dur=0.42, vol=0.35):
    sr = 44100
    t = np.arange(int(sr * dur)) / sr
    sig = np.zeros_like(t)
    step = dur / len(freqs)
    for i, f in enumerate(freqs):
        t0 = i * step * 0.6
        env = np.where(t >= t0, np.exp(-(t - t0) * 7.0) * np.clip((t - t0) * 400, 0, 1), 0.0)
        sig += env * (np.sin(2 * np.pi * f * t) + 0.25 * np.sin(2 * np.pi * 2 * f * t))
    sig = sig / max(1e-6, np.abs(sig).max()) * vol
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
        w.writeframes((sig * 32767).astype(np.int16).tobytes())


if __name__ == "__main__":
    for f in [icon_run, icon_tq, icon_drag, icon_gloves, icon_pulse, icon_marker, icon_sweep, icon_bandage, icon_blanket,
              icon_card, icon_radio, icon_heli, icon_shears, icon_gauze, icon_press, icon_roll, icon_seal, icon_lungs,
              icon_needle, icon_npa, icon_check, icon_warn]:
        f()
    icon_glow()
    chime(os.path.join(UI, "hint_done.wav"), [784.0, 1046.5])
    chime(os.path.join(UI, "hint_wrong.wav"), [392.0, 311.1], 0.5, 0.3)
    chime(os.path.join(UI, "hint_show.wav"), [880.0], 0.25, 0.18)
    print("icons ->", OUT)
