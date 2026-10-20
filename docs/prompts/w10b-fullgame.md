# Stage w10b - audit of the WHOLE game, not only the new changes

Branch `w10` (after w10-audit). Same guard rails. Owner, 20.10 17:21: "надо не только новые правки
перепроверить, надо всю игру проверить".

w10-audit fixed grabbing, the kit tray, the radio on Y and guidance. Now go through EVERYTHING that exists in
the game, from app start to app exit, as a real player with controllers would (real Input System XR test
devices, not bot APIs), and fix what is broken, unclear or ugly. Build the inventory from the code and scenes
first (docs/GDD*.md, Scenarios.cs, Training.cs, menu code, scene list) so nothing is skipped:

1. Boot / launch guard / title / main menu: every button and sub-menu (scenarios, training, settings,
   language RU/KZ, comfort options, credits/exit), laser pointer works with both hands, text readable, no
   overlapping UI, KZ texts present everywhere (no missing translations / fallback keys).
2. Every training lesson (tourniquet, packing, chest, NPA, drag, self TQ, controls tutorial ...): can a player
   finish it with real input, are the hints right, does it end and return to the menu.
3. Every scenario (all of them in the scenario list: the battle modes from w4/w7 - drone drop, sniper, mine,
   mortar, FPV, KAB etc., the main casualty scenarios, self-aid A/B): reach the casualty, every treatment step,
   radio report, evac by dragging to the helicopter, debrief, back to menu, start the next one without
   restarting the app (memory/state leaks).
4. Systems: locomotion and snap/smooth turn, comfort vignette, height calibration (sitting/standing), the wrist
   watch, the kit tray, drag/log roll, vitals checks + pulse haptics, physiology timers, blood decals, audio
   (radio lines, ambience, volumes), battle FX, drones, sky text, helicopter, pause / headset off-on, Guardian
   boundary recentre.
5. Performance on Quest: frame time per scene and per scenario peak (explosions, helicopter, many items),
   GC allocations, draw calls; fix any scene that drops below 72 fps.
6. World: things inside walls or under the ground, holes, z-fighting, wrong scale, flag/insignia orientation,
   the Satpaev text readable, nothing floating.

Record everything in docs/W10_AUDIT.md (section "Вся игра"): per area - what was checked, bugs found, cause,
fix, test. Short plain Russian summary at the top for the owner: what was broken, what is fixed, what is left.
Fix every bug you can; for anything left, say why.

Done: tests green or documented, QuestSceneAudit OK, Android build with the XR guard line + libUnityOpenXR.so,
APK copied to `C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W10.apk` (overwrite), device smoke test if the
headset is connected (pull its logs into logs/device/ BEFORE launching), commit on w10, push w10, append to
docs/PROGRESS.md, create logs/w10b-fullgame.done.
