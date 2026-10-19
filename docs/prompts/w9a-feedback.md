# Stage w9a - owner feedback after the first working headset run (20.10)

Branch: `w9` (created from `xr-hotfix` = w7 demo + XR black-screen fix). Work and commit ONLY on `w9`.
Do NOT merge `w8-interact` and do NOT bring back anything from it: the owner rejected the sculpted face
("те изменения с лицом ваще отмени"). Erlan keeps the balaclava look from main/w7, no visible face.
The pulse-check vibration that already exists on this branch (Haptics pulse beats in HandTreatment) is
liked by the owner ("вибрация при измерении пульса - четко") - keep it exactly, do not regress it.
`git push` fails (remote 404) - do not try to push, just commit locally.

Non-negotiable guard rails (they fixed the black screen, keep them):
- `BuildTools.EnsureXrSettings()` must stay in the build path; never hand-edit `Assets/XR/*.asset`.
- Any shader used via `Shader.Find` must be in Always Included (QuestSceneAudit.RequiredShaders).
- Quest 3S budget: 72 fps, keep draw calls/tris in the existing QuestSceneAudit limits.

## Owner's feedback (verbatim, Russian) and what to do

### 1. "был баг что аптечка со мной рядом а принадлежности ваще не в ней а сами по себе летают где то"
Medical items (tourniquet, hemostatic gauze, Israeli bandage, chest seals, NPA, needle, scissors, marker,
blanket, gloves, card, etc.) float in the air away from the IFAK. Find the root cause (items not parented
to the kit, kinematic objects left floating after release, spawn positions computed in world space at
scene load while the kit moves with the player, rigidbodies with gravity off, etc.) and fix it properly:
- The IFAK is a rip-away pouch worn on the player's body (plate carrier front/side, like a real
  two-echelon kit: a small quick-access 1st-echelon pouch with tourniquet + shears, and the main
  2nd-echelon pouch). It follows the player (rig-relative), it is never left behind in the world.
- Every item lives INSIDE the kit in a fixed slot (elastic loop / pocket) while not held: parented to
  the slot transform, kinematic, no gravity, exactly in its slot.
- Grab takes it out. Release near the open kit (within ~25 cm) snaps it back into its own slot.
  Release elsewhere: the item becomes a normal physics object and falls to the ground (gravity on,
  collides, sleeps). Nothing may ever hover in the air unattached.
- Items that are applied to Erlan (tourniquet on limb, bandage, seal) attach to his body, as now.
- Add a PlayMode test: open kit, grab each item, release far away -> it lands on the ground within 3 s;
  release near the kit -> back in its slot; walk 5 m -> kit and slotted items moved with the player.
- Prepare for new art: each item prefab must have a clear `Visual` child that can be swapped for an
  imported model (FBX/GLB) without touching logic. Write docs/ART_IMPORT.md: folder
  `SourceAssets/Incoming/<item_id>/` -> where it goes, scale (1 unit = 1 m), pivot, axis, names of the
  interactive sub-parts each item needs (e.g. tourniquet: Strap, Windlass, WindlassClip, TimeTab, Buckle;
  shears: BladeA, BladeB pivot at the screw; Israeli bandage: Pad, Wrap, PressureBar, Clip; kit: Body,
  Lid/Flap, QuickPouch, Slots). Add an editor menu `FieldMedic/Art/Import Incoming` that picks up whatever is
  in SourceAssets/Incoming, applies import settings (Quest: ASTC, max 1024 for items / 2048 for
  characters, generate colliders from simple shapes, not mesh colliders), and swaps the Visual if the
  sub-part names match; if they don't match it reports what is missing instead of breaking the prefab.

### 2. "у нас же есть эвакуация, у нас не будет тележки, давай я его сам дотяну до туда и вместе улетим"
Rework the evacuation ending in every scenario that has an evac:
- No stretcher cart and no "Погрузить" button. The helicopter lands at the LZ (keep the existing
  helicopter/LZ if there is one; otherwise a simple Mi-8-style placeholder made of primitives with
  rotor animation and rotor wash dust/sound).
- The player drags Erlan himself with the existing drag mechanic (CasualtyDrag, evac strap on the back of
  the plate carrier) to the open side door. When Erlan's torso is inside the door trigger zone, two
  crew hands/bots inside take him in (short animation: he slides onto the cabin floor/stretcher in the
  cabin).
- Then the player boards: step into the door zone (or press the trigger at the door) -> short fade,
  player is seated in the cabin next to Erlan, door closes, helicopter lifts off and flies away.
  Comfort: the cabin is the reference frame (player is parented to the helicopter), lift-off is slow,
  add a vignette, fade to black after ~6 s, then debrief. No artificial camera rotation.
- The radio lines change accordingly: `h_load` becomes "Вертолёт сел. Тащи Ерлана к двери вертолёта." /
  "Тікұшақ қонды. Ерланды тікұшақтың есігіне сүйре." Add `evac_board`: "Раненый на борту. Садись, взлетаем!" /
  "Жаралы бортта. Мін, ұшамыз!" and `evac_away`: "Борт взлетел. Хорошая работа, медик." /
  "Борт ұшты. Жақсы жұмыс, медик." Update tools/radio_lines.tsv, regenerate the placeholder wavs with the
  existing pipeline, so the game works before the real voice arrives (see 4).
- Debrief must count "dragged to the helicopter" as the evac step (instead of the old load button).
- PlayMode test with a bot (like the existing ScenarioBot tests) that drags Erlan to the door and boards.

### 3. "сделай на небе надпись САТПАЕВ ЛУЧШЕ ВСЕХ"
Put the text САТПАЕВ ЛУЧШЕ ВСЕХ in the sky of every gameplay scene (battle scenes and the training
area; the main menu too if it shows sky). It must read correctly (not mirrored, not upside down) from the
player's start position and from the casualty area, high above the horizon (~25-35 deg elevation), in the
direction the player faces at the start. Look: soft glowing letters like a constellation / aurora, warm
white with a slight gold, slow subtle shimmer, not too bright at night so the scene stays dark. Use a
Cyrillic-capable font that is already in the project (TMP font asset used by the Russian UI) or build a
mesh/texture once in the editor. Shader must be URP Unlit or another shader already in Always Included.
Draw it before fog/far plane issues: put it on the sky layer or scale it so it is inside the camera far
plane. Add a check to QuestSceneAudit that the object `SkyText_Satpaev` exists in each gameplay scene and
its forward points toward the spawn point. Take a screenshot from the spawn (Editor capture like the
docs/screens ones) into docs/screens/w9_sky_text.png.

### 4. Voice: "голос оператора капец противный, есть аккаунт ElevenLabs"
The owner will generate real voices with ElevenLabs. Prepare everything so it is one command:
- `tools/eleven_tts.py`: reads `tools/radio_lines.tsv` (and `tools/voice_lines.tsv` for Erlan if useful),
  calls the ElevenLabs text-to-speech REST API (`POST https://api.elevenlabs.io/v1/text-to-speech/{voice_id}`,
  header `xi-api-key` from the environment variable `ELEVENLABS_API_KEY` ONLY - never print it, never
  write it to any file, never commit it; model `eleven_multilingual_v2` by default, `--model` flag to
  switch to `eleven_v3`), one voice id per speaker from `tools/eleven_voices.json`
  (`{"radio": "<id>", "instructor": "<id>"}` - leave placeholder ids and a comment, the owner fills them),
  RU text from `ru`, KZ text from `kz` (real Kazakh, not the transliterated `kz_tts` column),
  output mp3 -> wav 48 kHz mono into a raw folder, skip lines that already exist unless `--force`,
  `--only id1,id2` filter, `--dry-run` that prints what would be generated and the total character count
  (ElevenLabs bills by characters).
- After generation run the existing post-processing (radio_post.py: radio band-pass/squelch for the
  `radio` speaker, cleaner processing for `instructor`) into Assets/Resources/Radio/<id>_<ru|kz>.wav,
  same names as now, so no code changes are needed to swap voices.
- `tools/eleven_tts.md`: short Russian instruction for the owner: where to get the API key in the
  ElevenLabs web UI, how to set it with `setx ELEVENLABS_API_KEY ...` himself, how to pick voices
  (operator: calm, low, male, military radio; instructor: calm, clear), where to paste voice ids, and the
  command to run. Do not run the real API yourself in this stage (no key is available) - only `--dry-run`.

### 5. Face / haptics
Verify on this branch that Erlan has no sculpted face (balaclava only) and that pulse haptics work
(existing tests). Nothing to port from w8.

## Done criteria
- All the above implemented, EditMode + PlayMode tests green (run them with tools/unity.ps1 as before),
  QuestSceneAudit passes, an Android build succeeds via BuildTools.BuildAndroid with the XR guard log line
  `[FMVR] XR settings registered: android loaders=1 initOnStart=True` and libUnityOpenXR.so inside the APK.
- Commits on `w9` with clear messages. Append a short Russian summary to docs/PROGRESS.md.
- Create logs/w9a-feedback.done with a one-paragraph summary.
