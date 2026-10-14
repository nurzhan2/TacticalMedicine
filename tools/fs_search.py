"""Freesound web-search fetcher (no API key): searches freesound.org with a license filter, parses the result
cards (id, author, title, duration, preview mp3) and downloads the best N per category into
Assets/Audio/SFX/<cat>/<id>_<slug>.mp3, appending rows to Assets/Audio/SFX/attribution.tsv.
Idempotent (skips categories that already have files). Usage: python tools/fs_search.py [cat ...]
"""
import html, os, re, subprocess, sys, urllib.parse

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SFX = os.path.join(ROOT, "Assets", "Audio", "SFX")
ATTR = os.path.join(SFX, "attribution.tsv")

# cat: (queries, min_dur, max_dur, count, exclude words)
WANT = {
    "jet_flyby":        (["jet flyby", "fighter jet flyby", "jet fly over"], 3, 40, 3, ["airliner", "passenger", "toy", "synth"]),
    "heli_flyby":       (["helicopter flyby", "military helicopter", "helicopter fly over"], 5, 60, 3, ["toy", "rc ", "synth", "radio"]),
    "tank_cannon":      (["tank cannon", "tank gun shot", "cannon shot"], 0.8, 10, 3, ["synth", "game", "laser", "toy"]),
    "mg_burst":         (["machine gun burst", "machine gun fire", "pkm machine gun"], 0.8, 10, 4, ["synth", "laser", "toy", "nerf"]),
    "bullet_crack":     (["bullet crack", "supersonic bullet", "bullet passby crack"], 0.1, 3, 3, ["synth", "game"]),
    "impact_concrete":  (["bullet impact concrete", "bullet ricochet", "bullet hit wall"], 0.1, 3, 4, ["synth", "game", "cartoon"]),
    "rocket_launch":    (["rocket launch whoosh", "missile launch", "rpg launch"], 0.5, 8, 3, ["firework", "space", "synth", "nasa"]),
    "smoke_grenade":    (["smoke grenade", "smoke bomb hiss", "gas hiss"], 2, 40, 2, ["synth", "cartoon"]),
    "pin_pull":         (["grenade pin", "pin pull", "ring pull metal"], 0.1, 3, 2, ["synth", "bowling"]),
    "artillery_rumble": (["distant artillery", "artillery rumble", "distant bombardment"], 15, 240, 2, ["synth", "thunder", "fireworks"]),
    "tank_engine":      (["tank engine tracks", "armoured vehicle tracks", "tank driving"], 4, 60, 2, ["fish", "toy", "synth", "water"]),
    "battle_bed":       (["battle ambience", "war ambience gunfire", "warzone ambience"], 30, 240, 2, ["synth", "music", "game", "fireworks"]),
    "bomb_large":       (["huge explosion", "bomb explosion large", "big explosion"], 2, 12, 3, ["synth", "cartoon", "firework", "game"]),
}
# a title must contain one of these words to be accepted
NEED = {
    "jet_flyby": ["jet", "fighter", "f-16", "f16", "f-18", "mig", "plane"],
    "heli_flyby": ["heli", "chopper", "rotor"],
    "tank_cannon": ["tank", "cannon", "howitzer", "gun"],
    "mg_burst": ["machine", "gun", "mg", "burst", "pkm", "m60", "m249"],
    "bullet_crack": ["crack", "bullet", "whiz", "supersonic"],
    "impact_concrete": ["impact", "ricochet", "hit", "concrete"],
    "rocket_launch": ["rocket", "missile", "rpg", "launch"],
    "smoke_grenade": ["smoke", "hiss", "gas"],
    "pin_pull": ["pin", "ring", "grenade"],
    "artillery_rumble": ["artillery", "bombard", "rumble", "shell"],
    "tank_engine": ["tank", "track", "armour", "armor", "apc", "btr", "bmp"],
    "battle_bed": ["battle", "war", "combat", "gunfire"],
    "bomb_large": ["explosion", "bomb", "blast"],
}


def curl(url, out=None, timeout=120):
    args = ["curl.exe" if os.name == "nt" else "curl", "-s", "-L", "--max-time", str(timeout), "-A", "Mozilla/5.0"]
    if out:
        args += ["-o", out]
    args.append(url)
    r = subprocess.run(args, capture_output=True)
    return r.stdout.decode("utf-8", "replace") if not out else r.returncode == 0


def search(q, lic):
    f = urllib.parse.quote(f'license:"{lic}"')
    url = f"https://freesound.org/search/?q={urllib.parse.quote_plus(q)}&f={f}&s=Downloads+(most+first)"
    page = curl(url, timeout=60)
    out = []
    for block in page.split('bw-search__result')[1:]:
        m = re.search(r'href="/people/([^/"]+)/sounds/(\d+)/"', block)
        mp3 = re.search(r'data-mp3="([^"]+)"', block)
        dur = re.search(r'data-duration="([\d.]+)"', block)
        t = re.search(r'<a [^>]*href="/people/[^/"]+/sounds/\d+/"[^>]*title="([^"]+)"', block)
        if not (m and mp3 and dur):
            continue
        title = html.unescape(t.group(1)) if t else m.group(2)
        out.append(dict(user=m.group(1), id=int(m.group(2)), mp3=mp3.group(1).replace("-lq.mp3", "-hq.mp3"),
                        dur=float(dur.group(1)), title=title,
                        lic="CC0 1.0" if lic == "Creative Commons 0" else "CC BY 4.0"))
    return out


def slug(s):
    s = re.sub(r"\.(wav|mp3|aif|aiff|ogg|flac)$", "", s.lower())
    s = re.sub(r"[^a-z0-9]+", "_", s).strip("_")
    return (s[:32].strip("_")) or "sound"


def main(cats):
    have_ids = set()
    if os.path.exists(ATTR):
        for line in open(ATTR, encoding="utf-8").read().splitlines()[1:]:
            parts = line.split("\t")
            if len(parts) > 1 and parts[1].isdigit():
                have_ids.add(int(parts[1]))
    rows = []
    for cat in cats:
        queries, dmin, dmax, count, excl = WANT[cat]
        d = os.path.join(SFX, cat)
        if os.path.isdir(d) and any(f.endswith(".mp3") for f in os.listdir(d)):
            print("skip (exists)", cat)
            continue
        os.makedirs(d, exist_ok=True)
        picked, seen = [], set()
        for lic in ("Creative Commons 0", "Attribution"):
            for q in queries:
                if len(picked) >= count:
                    break
                for r in search(q, lic):
                    if r["id"] in seen or r["id"] in have_ids:
                        continue
                    seen.add(r["id"])
                    low = r["title"].lower()
                    if not (dmin <= r["dur"] <= dmax) or any(x in low for x in excl) or not any(x in low for x in NEED.get(cat, [""])):
                        continue
                    picked.append(r)
                    if len(picked) >= count:
                        break
            if len(picked) >= count:
                break
        for r in picked:
            fn = f"{r['id']}_{slug(r['title'])}.mp3"
            dest = os.path.join(d, fn)
            ok = curl(r["mp3"], out=dest, timeout=240) and os.path.exists(dest) and os.path.getsize(dest) > 1000
            if not ok:
                print("FAIL", cat, r["id"])
                if os.path.exists(dest):
                    os.remove(dest)
                continue
            print(f"ok {cat} {r['id']} {r['dur']:.1f}s {r['lic']} {r['user']}: {r['title']}")
            rows.append("\t".join([cat, str(r["id"]), r["lic"], r["user"], r["title"][:40].replace("\t", " "),
                                   f"https://freesound.org/s/{r['id']}/", f"{cat}/{fn}"]))
    if rows:
        with open(ATTR, "a", encoding="utf-8") as f:
            for r in rows:
                f.write(r + "\n")
    print("added", len(rows))


if __name__ == "__main__":
    main(sys.argv[1:] or list(WANT))
