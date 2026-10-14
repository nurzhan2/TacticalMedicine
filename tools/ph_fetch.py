"""Poly Haven (CC0) fetcher: HDRIs, PBR textures and models into Assets/ThirdParty/PolyHaven/ (committed).

  python tools/ph_fetch.py            -> fetch everything in WANT (skips what exists)
  python tools/ph_fetch.py rock_07    -> only these ids

Textures: <id>_diff.jpg, <id>_nor_gl.jpg, <id>_rough.jpg (1k). HDRIs: <id>_<res>.hdr.
Models: gltf 1k + includes, converted by Blender with tools/bk_export.py (FBX + LODs + materials JSON) so they
go through the same Unity setup as the BlenderKit models (Editor/LicensedModelSetup).
"""
import json, os, subprocess, sys, urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "ThirdParty", "PolyHaven")
WORK = os.path.join(os.environ.get("TEMP", "/tmp"), "ph")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
API = "https://api.polyhaven.com/"

WANT = {
    "hdris": {"overcast_soil_puresky": "2k"},
    "textures": ["withered_grass", "sparse_grass", "dry_ground_01", "dry_ground_rocks", "brown_mud_dry"],
    # id: (category folder, max faces)
    "models": {
        "rock_07": ("rocks", 3000), "rock_09": ("rocks", 3000), "namaqualand_boulder_02": ("rocks", 4000),
        "sand_rocks_small_01": ("rocks", 4000), "grass_medium_01": ("vegetation", 3000), "old_tyre": ("props", 2000),
    },
}


def get_json(url):
    req = urllib.request.Request(url, headers={"User-Agent": "FieldMedicVR-ph_fetch/1.0"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.load(r)


def download(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    req = urllib.request.Request(url, headers={"User-Agent": "FieldMedicVR-ph_fetch/1.0"})
    with urllib.request.urlopen(req, timeout=300) as r, open(path + ".part", "wb") as f:
        while True:
            b = r.read(1 << 16)
            if not b:
                break
            f.write(b)
    os.replace(path + ".part", path)
    print("  got", os.path.relpath(path, ROOT if path.startswith(ROOT) else WORK), "%.1f MB" % (os.path.getsize(path) / 1e6), flush=True)
    return path


def info(pid):
    return get_json(API + "info/" + pid)


def credit(pid, kind):
    i = info(pid)
    authors = ", ".join(i.get("authors", {}).keys())
    return "| %s | %s (%s) | %s | https://polyhaven.com/a/%s | CC0 1.0 |" % (kind, i.get("name", pid), pid, authors, pid)


def fetch_hdri(pid, res):
    files = get_json(API + "files/" + pid)
    url = files["hdri"][res]["hdr"]["url"]
    download(url, os.path.join(OUT, "HDRI", "%s_%s.hdr" % (pid, res)))


def fetch_texture(pid):
    files = get_json(API + "files/" + pid)
    for key, suffix in (("Diffuse", "diff"), ("nor_gl", "nor_gl"), ("Rough", "rough")):
        if key in files and "1k" in files[key]:
            url = files[key]["1k"]["jpg"]["url"]
            download(url, os.path.join(OUT, "Textures", pid, "%s_%s.jpg" % (pid, suffix)))


def fetch_model(pid, cat, faces):
    out = os.path.join(OUT, cat, pid)
    if os.path.exists(os.path.join(out, pid + ".fbx")):
        print("  exists", pid)
        return
    files = get_json(API + "files/" + pid)
    g = files["gltf"]["1k"]["gltf"]
    d = os.path.join(WORK, pid)
    main = download(g["url"], os.path.join(d, os.path.basename(g["url"])))
    for rel, inc in (g.get("include") or {}).items():
        download(inc["url"], os.path.join(d, rel))
    os.makedirs(out, exist_ok=True)
    cmd = [BLENDER, "-b", "--factory-startup", "--disable-autoexec", "--python", os.path.join(ROOT, "tools", "bk_export.py"),
           "--", "--in", main, "--out", out, "--name", pid, "--max-faces", str(faces), "--tex", "1024"]
    r = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
    for l in r.stdout.splitlines():
        if "[bk_export]" in l:
            print("  " + l)
    if r.returncode != 0:
        print(r.stdout[-2000:], r.stderr[-1000:])
        raise SystemExit("blender failed for " + pid)
    with open(os.path.join(out, pid + ".source.json"), "w") as f:
        json.dump({"name": pid, "source": "https://polyhaven.com/a/" + pid, "license": "CC0", "category": cat}, f, indent=1)


def main():
    only = set(sys.argv[1:])
    rows = []
    for pid, res in WANT["hdris"].items():
        if only and pid not in only:
            continue
        print("HDRI", pid); fetch_hdri(pid, res); rows.append(credit(pid, "HDRI " + res))
    for pid in WANT["textures"]:
        if only and pid not in only:
            continue
        print("TEX", pid); fetch_texture(pid); rows.append(credit(pid, "texture 1k"))
    for pid, (cat, faces) in WANT["models"].items():
        if only and pid not in only:
            continue
        print("MODEL", pid); fetch_model(pid, cat, faces); rows.append(credit(pid, "model (%s)" % cat))
    with open(os.path.join(OUT, "CREDITS.md"), "w", encoding="utf-8") as f:
        f.write("# Poly Haven assets (CC0 1.0)\n\n| type | asset | authors | url | license |\n|---|---|---|---|---|\n")
        f.write("\n".join(rows) + "\n")
    print("done", len(rows))


if __name__ == "__main__":
    main()
