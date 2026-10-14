"""BlenderKit fetcher for Field Medic VR (python 3.12, stdlib + optional PIL for contact sheets).

  python tools/bk_fetch.py search "ruined house" [--n 40] [--sheet]
      -> ranked table; --sheet saves a labelled thumbnail grid to %TEMP%/bk/sheets/<query>.jpg
  python tools/bk_fetch.py get <assetBaseId|prefix from last search> --category ruins --slug ruin_house_a
      [--max-faces 20000] [--tex 1024] [--res 1K] [--rigged] [--size L=6] [--pivot bottom] [--drop plane,ground]
      -> downloads, converts with Blender (tools/bk_export.py), places into
         Assets/_Licensed/BlenderKit/<category>/<slug>/ and records a row in docs/ASSETS_V2.md
  python tools/bk_fetch.py list   -> rows of docs/ASSETS_V2.md

The API key is read from %USERPROFILE%/.bk_api_key and never printed. Free plan: every search adds
is_free:true (paid assets answer 402).
"""
import argparse, json, os, re, shutil, subprocess, sys, time, uuid, urllib.parse, urllib.request, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
API = "https://www.blenderkit.com/api/v1/"
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
WORK = os.path.join(os.environ.get("TEMP", "/tmp"), "bk")
DEST = os.path.join(ROOT, "Assets", "_Licensed", "BlenderKit")
TABLE = os.path.join(ROOT, "docs", "ASSETS_V2.md")
HEADER = ("# Imported V2 assets\n\nBlenderKit models live in `Assets/_Licensed/` (git-ignored: Royalty-Free, not "
          "redistributable). CC0 assets (Poly Haven) live in `Assets/ThirdParty/PolyHaven/` and are committed.\n"
          "Re-create the licensed folder with `python tools/bk_fetch.py get <assetBaseId> --category .. --slug ..` "
          "(the exact commands are in the `cmd` column of tools/bk_assets.json).\n\n"
          "| category | name | assetBaseId | author | license | faces | path |\n|---|---|---|---|---|---|---|\n")


def key():
    p = os.path.join(os.environ["USERPROFILE"], ".bk_api_key")
    with open(p) as f:
        return f.read().strip()


def req(url, auth=True, timeout=120):
    h = {"User-Agent": "FieldMedicVR-bk_fetch/1.0"}
    if auth:
        h["Authorization"] = "Bearer " + key()
    return urllib.request.urlopen(urllib.request.Request(url, headers=h), timeout=timeout)


def get_json(url, auth=True):
    for attempt in range(4):
        try:
            with req(url, auth) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code in (402, 403, 404):
                raise
            time.sleep(3 + attempt * 5)
        except Exception:
            time.sleep(3 + attempt * 5)
    raise RuntimeError("request failed: " + url.split("?")[0])


def download(url, path, auth=False):
    tmp = path + ".part"
    for attempt in range(4):
        try:
            with req(url, auth, timeout=300) as r, open(tmp, "wb") as f:
                total = int(r.headers.get("Content-Length") or 0)
                got, t0 = 0, time.time()
                while True:
                    b = r.read(1 << 16)
                    if not b:
                        break
                    f.write(b)
                    got += len(b)
                    if time.time() - t0 > 15:
                        print("  .. %.1f/%.1f MB" % (got / 1e6, total / 1e6), flush=True)
                        t0 = time.time()
            os.replace(tmp, path)
            return path
        except Exception as e:
            print("  download retry", attempt, type(e).__name__, flush=True)
            time.sleep(5)
    raise RuntimeError("download failed")


# ------------------------------------------------------------------ search + rank
def search(query, n=40):
    q = query + " asset_type:model is_free:true"
    url = API + "search/?query=" + urllib.parse.quote(q) + "&page_size=%d" % n
    d = get_json(url)
    return d.get("results", [])


def files_of(r):
    return {f["fileType"]: f for f in r.get("files", [])}


def rank(r, budget=60000):
    dp = r.get("dictParameters") or {}
    faces = dp.get("faceCount") or 0
    q = (r.get("ratingsAverage") or {}).get("quality") or 5.0
    s = q * 10
    if not r.get("isFree"):
        s -= 1000
    if dp.get("procedural"):
        s -= 8
    if dp.get("modelStyle") not in (None, "realistic"):
        s -= 15
    if faces:
        s -= 10 * max(0.0, (faces - budget) / budget)  # decimation hurts beyond ~2x budget
    fs = files_of(r)
    if "resolution_1K" in fs or "resolution_0_5K" in fs or "gltf" in fs:
        s += 5
    return s


def size_mb(f):
    return (f.get("fileUploadSize") or 0) / 1e6


def cache_path():
    os.makedirs(WORK, exist_ok=True)
    return os.path.join(WORK, "last_search.json")


def cmd_search(a):
    res = search(a.query, a.n)
    res.sort(key=rank, reverse=True)
    with open(cache_path(), "w", encoding="utf-8") as f:
        json.dump(res, f)
    allp = os.path.join(WORK, "all_assets.json")
    allc = json.load(open(allp, encoding="utf-8")) if os.path.exists(allp) else {}
    for r in res:
        allc[r["assetBaseId"]] = r
    with open(allp, "w", encoding="utf-8") as f:
        json.dump(allc, f)
    for i, r in enumerate(res):
        dp = r.get("dictParameters") or {}
        fs = files_of(r)
        best = next((t for t in ("resolution_1K", "resolution_0_5K", "gltf", "blend") if t in fs), "-")
        print("%2d %s %-42s f=%-7s q=%-4.1f %s %5.1fMB %s%s by %s" % (
            i, r["assetBaseId"][:8], r["name"][:42], dp.get("faceCount"),
            (r.get("ratingsAverage") or {}).get("quality") or 0, best, size_mb(fs.get(best, {})),
            "PROC " if dp.get("procedural") else "", "RIG " if dp.get("rig") else "",
            (r.get("author") or {}).get("fullName", "?").strip()))
    if a.sheet:
        sheet(a.query, res[: a.sheet_n])


def sheet(query, res):
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        print("PIL missing, no sheet")
        return
    d = os.path.join(WORK, "thumbs")
    os.makedirs(d, exist_ok=True)
    if not res:
        return
    cell, cols = 256, 5
    rows = (len(res) + cols - 1) // cols
    im = Image.new("RGB", (cols * cell, rows * cell), (30, 30, 30))
    dr = ImageDraw.Draw(im)
    for i, r in enumerate(res):
        url = r.get("thumbnailSmallUrl")
        p = os.path.join(d, r["assetBaseId"] + ".jpg")
        try:
            if not os.path.exists(p):
                download(url, p)
            t = Image.open(p).convert("RGB").resize((cell, cell))
            im.paste(t, ((i % cols) * cell, (i // cols) * cell))
        except Exception as e:
            print("thumb fail", r["name"], e)
        x, y = (i % cols) * cell, (i // cols) * cell
        dr.rectangle([x, y, x + cell, y + 16], fill=(0, 0, 0))
        dr.text((x + 3, y + 2), "%d %s" % (i, r["name"][:34]), fill=(255, 255, 0))
    out = os.path.join(WORK, "sheets")
    os.makedirs(out, exist_ok=True)
    p = os.path.join(out, re.sub(r"[^a-z0-9]+", "_", query.lower()) + ".jpg")
    im.save(p, quality=80)
    print("SHEET", p)


# ------------------------------------------------------------------ get
def find_asset(ident):
    allp = os.path.join(WORK, "all_assets.json")
    if os.path.exists(allp):
        for bid, r in json.load(open(allp, encoding="utf-8")).items():
            if bid.startswith(ident) or r["id"].startswith(ident):
                return r
    if os.path.exists(cache_path()):
        with open(cache_path(), encoding="utf-8") as f:
            for r in json.load(f):
                if r["assetBaseId"].startswith(ident) or r["id"].startswith(ident):
                    return r
    d = get_json(API + "search/?query=" + urllib.parse.quote("asset_base_id:%s" % ident))
    if d.get("results"):
        return d["results"][0]
    raise SystemExit("asset not found: " + ident)


def fetch_file(r, prefer):
    fs = files_of(r)
    order = [prefer, "resolution_1K", "resolution_0_5K", "resolution_2K", "gltf", "blend"]
    for t in order:
        if t in fs:
            f = fs[t]
            break
    else:
        raise SystemExit("no downloadable file for " + r["name"])
    d = os.path.join(WORK, "dl", r["assetBaseId"])
    os.makedirs(d, exist_ok=True)
    marker = os.path.join(d, t + ".done")
    if os.path.exists(marker):
        with open(marker) as m:
            return m.read().strip(), t
    url = f["downloadUrl"] + "?scene_uuid=" + str(uuid.uuid4())
    info = get_json(url)
    signed = info.get("filePath")
    if not signed:
        raise SystemExit("no filePath in download answer")
    name = urllib.parse.unquote(os.path.basename(urllib.parse.urlparse(signed).path)) or (t + ".bin")
    path = os.path.join(d, name)
    print("download %s (%s, %.1f MB)" % (r["name"], t, size_mb(f)), flush=True)
    download(signed, path)
    if zipfile.is_zipfile(path):
        zd = os.path.join(d, t)
        with zipfile.ZipFile(path) as z:
            z.extractall(zd)
        cand = [os.path.join(dp, fn) for dp, _, fns in os.walk(zd) for fn in fns
                if fn.lower().endswith((".glb", ".gltf", ".blend", ".fbx"))]
        if not cand:
            raise SystemExit("zip without model")
        path = sorted(cand, key=lambda p: (not p.endswith(".glb"), not p.endswith(".gltf"), len(p)))[0]
    with open(marker, "w") as m:
        m.write(path)
    return path, t


def run_blender(src, out, a):
    cmd = [BLENDER, "-b", "--factory-startup", "--disable-autoexec", "--python",
           os.path.join(ROOT, "tools", "bk_export.py"), "--", "--in", src, "--out", out, "--name", a.slug,
           "--max-faces", str(a.max_faces), "--tex", str(a.tex), "--pivot", a.pivot]
    if a.rigged:
        cmd.append("--rigged")
    if a.size:
        cmd += ["--size", a.size]
    if a.drop:
        cmd += ["--drop", a.drop]
    if a.keep_collection:
        cmd += ["--keep-collection", a.keep_collection]
    logp = os.path.join(WORK, "blender_%s.log" % a.slug)
    with open(logp, "w", encoding="utf-8", errors="replace") as lf:
        p = subprocess.run(cmd, stdout=lf, stderr=subprocess.STDOUT, timeout=1800)
    with open(logp, encoding="utf-8", errors="replace") as lf:
        lines = [l.rstrip() for l in lf if "[bk_export]" in l or "Error" in l or "Traceback" in l]
    for l in lines[-25:]:
        print("  " + l)
    if p.returncode != 0 or not os.path.exists(os.path.join(out, a.slug + ".fbx")):
        raise SystemExit("blender export failed, log: " + logp)


def record(r, a, stats, rel):
    author = (r.get("author") or {}).get("fullName", "?").strip()
    lic = r.get("license", "royalty_free")
    row = "| %s | %s | %s | %s | %s | %s | %s |\n" % (
        a.category, r["name"].replace("|", "/"), r["assetBaseId"], author, lic, stats.get("tris"), rel)
    os.makedirs(os.path.dirname(TABLE), exist_ok=True)
    text = open(TABLE, encoding="utf-8").read() if os.path.exists(TABLE) else HEADER
    lines = [l for l in text.splitlines(True) if not l.rstrip().endswith("| " + rel + " |")]
    text = "".join(lines)
    if not text.endswith("\n"):
        text += "\n"
    # insert the row at the end of the BlenderKit table (before any later section)
    marker = "\n## "
    i = text.find(marker, text.find("| category |"))
    text = text + row if i < 0 else text[: i + 1].rstrip("\n") + "\n" + row + text[i:]
    with open(TABLE, "w", encoding="utf-8") as f:
        f.write(text)
    # machine-readable manifest (to re-fetch the git-ignored folder)
    mp = os.path.join(ROOT, "tools", "bk_assets.json")
    man = json.load(open(mp, encoding="utf-8")) if os.path.exists(mp) else {}
    man[a.slug] = {"category": a.category, "name": r["name"], "assetBaseId": r["assetBaseId"], "author": author,
                   "license": lic, "tris": stats.get("tris"), "size_m": stats.get("size_m"), "path": rel,
                   "cmd": "python tools/bk_fetch.py get %s --category %s --slug %s --max-faces %d --tex %d%s%s%s%s" % (
                       r["assetBaseId"], a.category, a.slug, a.max_faces, a.tex, " --rigged" if a.rigged else "",
                       (" --size " + a.size) if a.size else "", (" --drop " + a.drop) if a.drop else "",
                       (" --pivot " + a.pivot) if a.pivot != "bottom" else "")}
    with open(mp, "w", encoding="utf-8") as f:
        json.dump(man, f, indent=1, ensure_ascii=False)


def cmd_get(a):
    r = find_asset(a.ident)
    if not r.get("isFree"):
        print("WARNING: not marked free, download may fail (402)")
    src, t = fetch_file(r, "resolution_" + a.res if a.res else "resolution_1K")
    out = os.path.join(DEST, a.category, a.slug)
    if os.path.isdir(out):
        # keep Unity .meta files so GUIDs stay stable on re-export
        for fn in os.listdir(out):
            p = os.path.join(out, fn)
            if fn.endswith(".meta") or fn == "Materials":
                continue
            if os.path.isdir(p):
                for g in os.listdir(p):
                    if not g.endswith(".meta"):
                        os.remove(os.path.join(p, g))
            else:
                os.remove(p)
    os.makedirs(out, exist_ok=True)
    run_blender(src, out, a)
    stats = json.load(open(os.path.join(out, a.slug + ".export.json")))
    meta = {"name": r["name"], "assetBaseId": r["assetBaseId"], "author": (r.get("author") or {}).get("fullName"),
            "license": r.get("license"), "url": "https://www.blenderkit.com/asset-gallery-detail/%s/" % r["id"],
            "fileType": t, "rigged": a.rigged, "category": a.category}
    with open(os.path.join(out, a.slug + ".source.json"), "w") as f:
        json.dump(meta, f, indent=1)
    rel = os.path.relpath(out, ROOT).replace("\\", "/")
    record(r, a, stats, rel)
    print("OK %s tris=%s size=%s -> %s" % (a.slug, stats.get("tris"), stats.get("size_m"), rel))
    if stats.get("warnings"):
        print("  warnings:", "; ".join(stats["warnings"][:8]))


def cmd_list(a):
    if os.path.exists(TABLE):
        print(open(TABLE, encoding="utf-8").read())


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser()
    sp = ap.add_subparsers(dest="cmd", required=True)
    s = sp.add_parser("search")
    s.add_argument("query")
    s.add_argument("--n", type=int, default=40)
    s.add_argument("--sheet", action="store_true")
    s.add_argument("--sheet-n", type=int, default=20)
    g = sp.add_parser("get")
    g.add_argument("ident")
    g.add_argument("--category", required=True)
    g.add_argument("--slug", required=True)
    g.add_argument("--max-faces", type=int, default=20000)
    g.add_argument("--tex", type=int, default=1024)
    g.add_argument("--res", default="1K")
    g.add_argument("--rigged", action="store_true")
    g.add_argument("--size", default="")
    g.add_argument("--pivot", default="bottom")
    g.add_argument("--drop", default="")
    g.add_argument("--keep-collection", default="")
    sp.add_parser("list")
    a = ap.parse_args()
    {"search": cmd_search, "get": cmd_get, "list": cmd_list}[a.cmd](a)


if __name__ == "__main__":
    main()
