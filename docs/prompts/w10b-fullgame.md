# Stage w10b - audit of the WHOLE game, not only the new changes

Branch `w10` (after w10-audit). Same guard rails. Owner, 20.10 17:21: "надо не только новые правки
перепроверить, надо всю игру проверить".

## 0. FIRST - the owner played W10 on the headset at 01:10-01:45 (21.10). His feedback, highest priority
Device logs of that session: logs/device/play_2110/ (logcat) and logs/device/w10_fmvr_log.txt - read them.
Verbatim (Russian) + what to do:
1) "еле-еле записал время, надо сделать большую площадь нажатия" - writing the time on the tourniquet tab with
   the marker is far too fiddly. Make the target zone large and forgiving (touching the time tab area with the
   marker tip anywhere within ~6-8 cm for ~0.5 s writes the time), with a glow on the tab and haptics.
2) "случайно нажимаешь на что-то другое - если требуется только одно определённое действие, ты можешь сделать
   только его" - outside the exam, when the current step needs one specific action, ONLY that action (and the
   items/zones it needs) is interactable; everything else is ignored (no accidental grabs of other items, no
   wrong zones triggering). In the exam the player is free (that is what is graded).
3) "не понравилась аптечка в развёрнутом виде, не надо прямоугольник - прошлый был с моделью, со всем, вот он
   топ" - remove the flat 5x4 rectangle tray. The summoned kit (X / touch / watch) must be the real FMVR_IFAK
   model opened in front of the player (hinged panels unfolded, items in their real Slot_* places, like in
   W9_ART) - just floating at a comfortable spot in front, with the needed item glowing and small labels.
4) "даже не справился с измерением пульса на стопе" - the pedal pulse check is too hard. Big, forgiving zone on
   the top of the foot/ankle (touch with 2 fingers or just the hand, or press grip near it), clear glow, the
   haptic pulse starts immediately, the step completes after ~2 s of contact. Same for radial/carotid.
5) "надо крики убрать, пусть только пули и взрывы на фоне" - remove ALL background screaming/shouting voices
   from the ambience (other wounded, soldiers yelling). Keep only gunfire, explosions, drones, wind etc.
   (Erlan's own pain sounds when touched can stay quiet and short.)
6) "надо Ерлану тоже голос нагенерировать" - Erlan needs real voice lines. The supervisor will generate them with
   ElevenLabs into SourceAssets/Voice/eleven_raw_erlan/ (ids from tools/voice_lines.tsv, RU only). DONE 21.10 02:00:
   29 mp3 (44.1 kHz) are there - Erlan = "Prince Nur" voice with emotion (eleven_v3), the 3 `player` lines = Egor.
   4 new lines were appended to voice_lines.tsv (hear_you, where_hurts_leg, where_hurts_chest, yes) for the AVPU /
   "где болит" questions - wire them. Erlan's voice is used in BOTH UI languages (owner wants Russian voice only).
   Prepare: make
   sure tools/voice_lines.tsv has the lines Erlan actually says (pain, answers to AVPU questions "ты меня
   слышишь?", "где болит?", thanks, "нога..." etc., short, natural, RU), and the casualty voice code plays them
   from Assets/Audio/Voice/<id>_ru.wav. If the raw files exist when you get here, post-process and wire them;
   if not, leave a note in docs/W10_AUDIT.md (the supervisor will add them).
7) "во второй миссии не смог ему что-то сделать - что-то в тазе, не получается в нужное место попасть" - S2
   «Снайпер»: the groin / junctional wound treatment (packing / pressure in the groin) is impossible to hit.
   Make the wound target big and obvious (glow + arrow), packing works when the gauze is anywhere near the
   wound, pressure works with the hand on/near it. Then play S2 to the end with real input to prove it.
8) "сделай таймер 3 минуты меньше - типа 10 секунд, но напиши, что в бою надо 3" - every long real-time wait
   (3 min pressure after packing, re-check timers, evac wait, etc.) is shortened to ~10 s in lessons and
   scenarios, with a clear on-screen line like "В бою держать 3 минуты. Здесь - 10 секунд." (KZ too). Only the
   EXAM keeps real times.
9) "даже миссия снайпер - сделать легче, я не понимаю что да как, только вышел - уже сразу пиздец. Сделай
   подсказки даже в сценарии, и только в экзамене без подсказок" - ALL scenarios get full step-by-step hints
   (like lessons), easier pacing at the start (a few seconds of calm + a short briefing of what to do first,
   enemy fire less punishing - the player should not be overwhelmed instantly). Only the EXAM mode has no hints.
10) "проверить все сценарии, потому что некоторые не выполнимы" - every scenario S1-S8 + SA must be completable
   with real input by a normal player. Prove each with a real-input PlayMode run (not bot shortcuts) and list
   results in docs/W10_AUDIT.md.
11) He asked "в каком сценарии есть вертолёт?" - make it obvious: the briefing/watch of every scenario that ends
   with TACEVAC says so ("в конце - эвакуация вертолётом: вызови по рации (Y) и дотащи Ерлана к вертолёту").
--- Second round, owner played the w10c build (soldier + Egor voice) at 02:10-02:39 ---
12) "крик не убрал" - background screaming is still there (item 5) - find EVERY source (ambience loops, battle
   audio, casualty pools, other soldiers' VoiceBank pools, 3D one-shots) and remove the screams. Verify by
   listing every AudioClip that plays in a 5-minute S1 run (log it) - no scream/shout clip may be in the list.
13) "до сих пор маленькая зона взаимодействия" - item 2/General is the top priority of this stage.
14) "сделай кнопку, чтобы пропустить шаг, чтобы всё само сделалось" - add SKIP STEP outside the exam: hold A
   for 1.5 s (with a fill ring on the step panel) or a "Пропустить шаг" button on the watch -> the current step
   completes by itself (the item flies to the right place / is applied automatically with its animation, the
   physiology updates as if done correctly), the debrief marks the step "пропущен". Not available in the exam.
15) "почему аптечка не изменилась - надо X нажал, потом нормальная, как в прошлый раз, аптечка вышла" - item 3:
   X must bring out the real FMVR_IFAK model opened in front (like W9_ART), not the rectangle tray.
16) "аптечку с Ерлана убери, эту некрасивую самоделку" - remove the self-made/procedural pouch (kit) that hangs
   on Erlan's body (the "IFAK on the hip" added in w10c and any older procedural pouch on the casualty). If a
   scenario needs Erlan's own tourniquet/kit, use the real FMVR_IFAK model scaled on his vest, or drop it.
17) "руки ещё не проработал - всё ещё старая модель" - the player's first-person hands are still the old ones.
   Make new first-person hands from the owner's soldier model (SourceAssets/Incoming/soldier/Soldier_KZ.fbx: its
   gloved hands + ~14 cm of camo sleeve, Bip01 L/R Hand + Finger bones): extract/separate the hand+forearm part of
   the mesh (Blender headless on a copy, or a Unity mesh split by bone weights), keep the same textures, drive the
   finger bones from the existing hand pose system (grip/trigger/point/pinch poses, hand tracking if enabled).
   Do NOT use the GPT-6 Astra FMVR_Hands. If the result is not clean by 04:30, keep the old hands and say so.

General: "надо сделать именно площадь нажатия и взаимодействия больше" - go through EVERY interaction (grab
radius, zones on the body, buttons, tray/kit items, radio panel, door ring, marker, pulse sites, seals, NPA,
needle site) and make the targets bigger and more forgiving outside the exam.

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

## TIME LIMIT (owner, 21.10 00:46): the project is due at 07:00 local time (UTC+5)
This stage now runs AFTER w10c-soldier (soldier + ElevenLabs voices). Check the clock (Get-Date) often.
Prioritise what a player hits first: menu -> controls tutorial -> main scenario with Erlan -> evac -> debrief,
then self-aid, then the other battle modes, then lessons. Fix the worst blockers first.
At 05:30 STOP fixing, run the tests that matter, build the final APK, copy it to
`C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W10.apk` (overwrite), install it on the headset if connected,
commit + push w10, write the Russian summary at the top of docs/W10_AUDIT.md (what was fixed, what is left),
and create logs/w10b-fullgame.done - everything must be finished by 06:30.
