# Stage w10f - first-person hands polish + sky text (owner, 21.10 10:34)

Branch `w10`. Same guard rails (XR guard, Always Included shaders, Quest 72 fps, no face from w8, no Astra hands).
Owner: "и Политех лучше всех сделай, а не Сатпаев лучше всех, и руки отредактируй".

1. Sky text: the supervisor already changed SkyText.Phrase/DisplayText to «ПОЛИТЕХ ЛУЧШЕ ВСЕХ» (runtime swap of the
   baked TMP text in SkyText.Start, QuestSceneAudit accepts the old baked phrase). Make it clean: re-bake the sky
   text in every gameplay scene with the new phrase (W9Patch or a small editor step), check all glyphs exist in
   the font atlas (О, И, Х ...) - no missing-character squares, and the audit only accepts the new phrase again.
   Screenshot docs/screens/w10f_sky.png.
2. First-person hands (made in w10b from the owner's Soldier_KZ gloves, 758 tris per hand): make them look and
   feel right in VR. Check with screenshots from the player's eye in all poses (open, grip, trigger, point,
   pinch, holding the CAT / marker / gauze, pulse check) and fix everything that looks wrong: correct size
   (adult male glove), wrist exactly at the controller grip with the right rotation for Quest Touch Plus, the
   sleeve cut cleanly (no open seams, no stretched texture), no inverted normals / holes, fingers curl naturally
   with grip and trigger (thumb on the thumbstick/face buttons at rest), items held IN the palm not floating,
   left/right not mirrored wrongly, good lighting/material (not too dark or shiny). Compare with the old hands
   and only switch if the new ones are better in every pose. Screenshots docs/screens/w10f_hands_*.png.
3. Tests that matter (EditMode, input/kit/hands suites), QuestSceneAudit, Android build with the XR guard line +
   libUnityOpenXR.so, copy over Desktop\FMVR_DEMO\FieldMedicVR_W10.apk (keep the previous as
   FieldMedicVR_W10_prev.apk), install on the headset with `adb install -r -g` ONLY - do NOT launch the app (a
   launch interrupted by the install put the game into SAFE mode this morning).
4. Commit, push w10 and fast-forward main to w10 and push it. Append to docs/PROGRESS.md, create logs/w10f-hands.done.
Time box: about 1.5 hours.
