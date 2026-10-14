# Stage w1 - real assets from BlenderKit (+ Poly Haven CC0)

Context: docs/GDD_V2.md sections 1, 2, 5. Owner rule: only REAL models, no home-made placeholder art.

BlenderKit access
- API key (user's account) is in %USERPROFILE%\.bk_api_key - never print it, never commit it.
- Search: GET https://www.blenderkit.com/api/v1/search/?query=<words>+asset_type:model&page_size=40
  with header Authorization: Bearer <key>. Results have name, assetBaseId, isFree, dictParameters
  (faceCount etc.), files[] with fileType (blend, gltf, resolution_1K...) and downloadUrl.
- Download: GET <file.downloadUrl>?scene_uuid=<any uuid4> with the same header -> JSON {filePath: <signed url>};
  then download that url. Prefer fileType "gltf" (to Unity via glTFast) else "blend" (open headless:
  "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b file.blend --python tools/bk_export.py ->
  decimate to budget, apply transforms, pack/limit textures to <= 2K, export FBX or GLB).
- The account is on the FREE plan: always add +is_free:true to the query (paid ones return 402). Verified free hits exist for ruin, tank (M1A2), helicopter (CH-47), soldier (rigged sniper, low poly soldier), concrete barrier, czech hedgehog. No free sandbags -> Meshy prompt.
- Write tools/bk_fetch.py (python 3.12 on the laptop): search, rank (isFree, rating, faceCount fit), download,
  convert, place into Assets/_Licensed/BlenderKit/<category>/<slug>/ and add a line to docs/ASSETS_V2.md:
  category | name | assetBaseId | author | license | faces | path.
- Add Assets/_Licensed/ (and its .meta) to .gitignore: Royalty-Free assets must not be redistributed in the
  public repo. Poly Haven CC0 assets go to Assets/ThirdParty/PolyHaven/ and may be committed. Update ATTRIBUTION.md.

Shopping list (best 1-3 of each; respect the Quest budget, decimate if needed)
- Ruins: destroyed house/building, ruined brick wall with holes, broken concrete slabs, rebar, rubble piles.
- Cover: sandbags (wall + pile), concrete blocks/jersey barriers, czech hedgehog, barbed wire, shell crater,
  wrecked/burnt car, burnt APC/BTR.
- Vehicles: main battle tank, IFV/BMP, attack jet (Su-25 style), attack helicopter (Mi-24 style), transport
  helicopter (Mi-8 style) for evac, FPV quadcopter drone.
- Soldiers: 2-3 rigged modern soldiers (camo, helmet, plate carrier) compatible with Unity Humanoid so the
  existing Mixamo clips in the project work (check Assets for run, rifle shoot, crouch, death, crawl, drag).
  If unusable, reuse Swat Guy with different materials and add a Meshy prompt.
- Props: ammo crates, jerry cans, radio, flag pole, debris, spent casings, stretcher.
- Environment: Poly Haven overcast HDRI, dirt/dry grass ground textures, rocks.

If an item is not available at acceptable quality: append a Meshy prompt (same style as IFAK ones) under
"Extra prompts" in docs/MESHY_PROMPTS_V2.md with the fixed file name the importer will use.

Also implement Assets/Editor/MeshyImporter.cs: scans SourceAssets/Meshy/*.glb|fbx with the fixed names from
MESHY_PROMPTS_V2.md, sets real scale, generates colliders + grab points and swaps them into the IFAK item
prefabs (keep current models as fallback when a file is missing). Menu item + auto-run on asset import.

Done: bk_fetch.py + bk_export.py work, >= 20 real models imported and listed in ASSETS_V2.md, missing ones
have Meshy prompts, MeshyImporter exists and is tested with a dummy file, committed (without _Licensed).

