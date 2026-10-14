"""Run tools/bk_fetch.py get for every line of a batch file (id category slug max_faces [extra...])."""
import subprocess, sys, os, shlex
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
only = set(sys.argv[2:])
for line in open(sys.argv[1], encoding="utf-8"):
    line = line.strip()
    if not line or line.startswith("#"):
        continue
    p = shlex.split(line)
    ident, cat, slug, faces, extra = p[0], p[1], p[2], p[3], p[4:]
    if only and slug not in only:
        continue
    if not only and os.path.exists(os.path.join(root, "Assets", "_Licensed", "BlenderKit", cat, slug, slug + ".fbx")):
        print("skip", slug, flush=True)
        continue
    cmd = [sys.executable, os.path.join(root, "tools", "bk_fetch.py"), "get", ident, "--category", cat, "--slug", slug,
           "--max-faces", faces] + extra
    print(">>", slug, flush=True)
    r = subprocess.run(cmd, cwd=root)
    print("<<", slug, "exit", r.returncode, flush=True)
