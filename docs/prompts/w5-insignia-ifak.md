# Stage w5 - insignia + realistic IFAK

Context: docs/GDD_V2.md sections 4 and 5, docs/MESHY_PROMPTS_V2.md.

Tasks
1. Insignia (decals/texture patches on existing uniforms, no new geometry needed except patch quads):
   - Kazakhstan flag patch (draw it correctly: sky-blue field, golden sun with 32 rays and steppe eagle,
     national ornament stripe at the hoist) - on the player's sleeve/forearm area visible in VR and on Erlan.
   - Military department of Satbayev University patch on the other shoulder: if any image exists in
     SourceAssets/Insignia/ use it; otherwise a neutral shield placeholder with text
     "ӘСКЕРИ КАФЕДРА / SATBAYEV UNIVERSITY". Do NOT invent or redraw the real logo.
   - Name/blood tape on Erlan: "ЕРЛАН  O(I) Rh+".
   - Emblem + "Бауыр" on the splash screen and main menu.
   - Kazakhstan flag on the pole (from w2) uses the same texture.
2. IFAK:
   - Run MeshyImporter on SourceAssets/Meshy/. For every file present, swap the model into the item prefab
     (scale in real cm, colliders, grab points, packaging open/close states where the prompt has two versions).
   - Items without a Meshy file: keep the best existing model, list them in docs/PROGRESS.md as
     "waiting for owner's Meshy model".
   - Pouch: rip-away IFAK on the player's vest (left hip or chest), opens with a pull, items in elastic loops.
3. Screenshots docs/screens/v2_w5_*.jpg: patches on sleeve (VR view), Erlan's tape, open IFAK with all items.

Done: insignia visible in VR view, IFAK pouch works, importer swapped all available Meshy models, committed.
