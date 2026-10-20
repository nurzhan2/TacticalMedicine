# Stage w10c - the owner's soldier model as Erlan (and the other soldiers)

Branch `w10` (after w10-audit and w10b-fullgame). Same guard rails (XR guard, Always Included shaders,
Quest 72 fps, no FMVR_Hands / FMVR_Soldier_KZ from Astra - those are NOT used).

The owner sent his own soldier: `SourceAssets/Incoming/soldier/` = `Soldier_KZ.fbx` (+ .meta from his Unity,
FBX 7.4, 3ds Max Biped rig `Bip01 ...` incl. fingers and facial bones, 4 materials sm005_body / sm005_equipment /
sm005_head / sm005_helmet) and `Textures/Soldier_{Body,Equipment,Head,Helmet}_{Albedo,Normal,MetalSmooth}.png`
(2048, Unity metallic-smoothness packing). Pixel camo, Kazakhstan flag patch on the sleeve, visible face (the
owner chose this model - keep its face, it is NOT the rejected w8 sculpted face).

1. Import into Assets/Art/Characters/SoldierKZ/ (copy, do not edit the source): Humanoid rig (map the Biped
   bones, check the avatar has no errors, T-pose), URP Lit materials from the textures (Albedo, Normal,
   MetalSmooth as metallic+smoothness in alpha), Android ASTC max 2048 for body/head, 1024 for helmet/equipment
   if it keeps the budget. Measure tris/draw calls; if too heavy for Quest (>40k tris on screen for Erlan +
   bots) make LODs or a lighter LOD for bots.
2. Replace Erlan's current body with this model everywhere Erlan appears (all scenarios, lessons, self-aid
   rescuers, MedicBot/other soldiers, menu dugout scene if he is there). Keep ALL gameplay working: ragdoll /
   lying poses, drag by the evac handle (add a handle anchor on the back of the vest if the model has none),
   log roll, limb handles, pulse sites (carotid/radial/pedal), wound placement and blood decals, tourniquet band
   wrapping on thigh/arm, cutting clothes to expose a wound (the model has no separate cuttable pieces - use a
   material/shader mask or a local cutout decal + skin patch at the wound; it must look clean), chest seal on
   the chest, NPA in the nose (the face is visible now - check the NPA lines up with the nostril), blanket over
   the body, casualty card attach point, pain reactions/breathing.
3. Physics/colliders: capsules per bone (no mesh colliders), consistent with the old casualty so grab/drag
   distances still work.
4. Erlan's KZ flag patch orientation must be correct (the flag was upside down in the past - check it).
5. Screenshots docs/screens/w10c_*.png (lying, dragged, TQ on thigh, chest seal, NPA, blanket, rescuers).
6. Tests green, QuestSceneAudit OK, Android build with the XR guard line + libUnityOpenXR.so, APK to
   `C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W10.apk` (overwrite), device smoke test if connected (pull logs
   first), commit + push w10, update docs/W10_AUDIT.md (section "Солдат") and docs/PROGRESS.md, create
   logs/w10c-soldier.done.

## 7. Real voice-over (ElevenLabs, Russian only) - do this in the same stage
The supervisor already generated the real voices with ElevenLabs (owner's choice 21.10: two strong male voices,
RUSSIAN ONLY): raw files in `SourceAssets/Voice/eleven_raw/` (69 RU lines from tools/radio_lines.tsv, ONE voice
for both roles - "Egor Gadzhiyev", chosen by the owner; the operator gets the radio filter, the instructor stays
clean so they still sound different; ids in tools/eleven_voices.json). Do NOT call the API
(no key is available to you and none is needed).
- Run the post-processing step of tools/eleven_tts.py / radio_post.py on the raw files (radio band-pass + squelch
  for `radio`, clean processing for `instructor`) into `Assets/Resources/Radio/<id>_ru.wav`.
- Russian-only: the game must play these Russian recordings also when the UI language is KZ (KZ subtitles/texts
  stay Kazakh). Remove the old robotic KZ placeholder wavs from the build or make the voice lookup fall back to
  RU - pick the cleaner way, and make sure no robotic TTS voice is heard anywhere in the game.
- If any line id from radio_lines.tsv has no raw file (e.g. lines added after 21.10 00:30), list it in
  docs/W10_AUDIT.md under "Озвучка - не хватает" instead of generating it.
- Check volumes against ambience/battle FX (operator readable over explosions), and that the line timing still
  fits the hints (no overlapping lines).
- Credit: add "Voices: ElevenLabs" to ATTRIBUTION.md and the credits screen.

## TIME LIMIT (owner, 21.10 00:46): the whole project is due at 07:00 local time (UTC+5)
Order changed: this stage runs right AFTER w10-audit, BEFORE w10b-fullgame. Check the clock (Get-Date).
Do the voice-over (section 7) FIRST - it is quick and the owner wants it most. Then the soldier.
By 03:30 at the latest you must have committed a working APK (FieldMedicVR_W10.apk on the Desktop) with
whatever is done; if the soldier integration is not solid by then, ship the voice-over with the OLD Erlan body
(keep the soldier work behind a switch, document it) - never leave the game broken. Then create the done marker.
