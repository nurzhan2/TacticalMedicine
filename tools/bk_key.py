import bpy, os
key = ""
for name, ad in bpy.context.preferences.addons.items():
    if "blenderkit" in name:
        p = ad.preferences
        key = getattr(p, "api_key", "") or ""
        print("ADDON", name, "has_key", bool(key), "len", len(key))
if key:
    out = os.path.join(os.environ["USERPROFILE"], ".bk_api_key")
    with open(out, "w") as f:
        f.write(key)
    print("WROTE", out)
