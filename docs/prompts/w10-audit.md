# Stage w10 - full audit after the owner's first real play of W9_ART (20.10 16:21)

Branch: create `w10` from `w9` (= main). Commit only there. Guard rails as before (XR guard in the build,
Always Included shaders, Quest 72 fps budget, no face from w8, do not touch FMVR_Hands / FMVR_Soldier_KZ).
Push is possible now: remote origin = github.com/nurzhan2/TacticalMedicine (private). Push `w10` at the end
(not main) - the owner's friend will review it.

## Owner's words (verbatim)
"я поиграл, она вся забагована. Сам проверь всё, не понятно вообще что куда. Я хватаю как показывает, а он
не берётся. Аптечка пусть будет в сторонке, чтобы не мешалась вообще, и если нужна будет - я мог нажать на
неё и она перед глазами чтобы была. И эта рация с кнопкой. Короче полный аудит сделай, найди баги и сделай всё."

The owner's play session logs may be in `logs/device/play_2010/` (fmvr_log.txt + logcat) - the supervisor
pulls them when the headset is connected; check that folder at the start AND again before you finish. If
the headset is connected (adb `device`) you may pull `/sdcard/Android/data/kz.qorsa.fieldmedic/files/` yourself
BEFORE launching the app (launching overwrites the log). Read them first: which inputs arrived (controllers or
hand tracking?), what was hovered/grabbed, warnings, exceptions, where the player got stuck.

## 1. Grabbing does not work "as the hint shows" (highest priority)
Bot tests passed, so the bug is between the real device input and the grab logic - bots bypass it.
- Trace the full path on the device: OpenXR interaction profile (Meta Quest Touch Plus / Touch Pro / hand
  tracking), Input System actions and bindings (`RuntimeActionBindings.json` x2 - why are there two copies? which
  one is used on the device?), the hand interactor, the item's grab colliders/layers, kinematic in-slot items
  (w9a/w9d slot logic, combined-mesh kit snapshot from w9d - did the combine remove/disable item colliders?),
  closed-kit hiding, physics layers matrix.
- Make grabbing forgiving and identical everywhere: grab with GRIP or TRIGGER (both), hover radius ~8-10 cm
  from the controller grip point, hover highlight (outline/glow) + light haptic tick on hover, the nearest item
  wins, release = button up. Hand tracking (if enabled): pinch or fist grabs the same way.
- The hints must show exactly the input that works (button icon on the controller model: grip / trigger / X /
  Y / A / B, as Quest 3S Touch Plus controllers have them). No hint may describe a gesture the game does not
  accept.
- Add PlayMode tests that drive the REAL input path with Input System test devices (XR controller device
  with devicePosition/grip/trigger), not bot shortcuts: hover -> grip -> item in hand; trigger -> item in hand;
  release -> falls / returns to slot; for every item kind and from the kit tray (section 2).

## 2. Kit: out of the way, summoned in front of the eyes
- The kit is no longer worn on the chest. It sits out of the way: hanging on the player's left hip/back
  (visible when looking down, never in front of the hands, no colliders that block grabbing Erlan).
- Summon: press a dedicated controller button (left X) OR touch the kit with either hand OR a "АПТЕЧКА"
  button on the wrist watch -> the kit opens as a TRAY floating in front of the player (~40 cm ahead, chest
  height, tilted toward the eyes, follows the head lazily only when the player turns more than ~50 deg).
  Items are laid out flat in a clear grid, each with a small RU/KZ label under it (e.g. "Жгут", "Ножницы",
  "Гемостатик", "Израильский бинт", "Наклейка", "Воздуховод", "Игла", "Перчатки", "Маркер", "Одеяло",
  "Карточка", ...). The item the current step needs glows.
- Press X again / grab nothing and press X -> the tray folds back to the hip. Items released within ~25 cm of
  the open tray snap back to their place; elsewhere they fall to the ground (keep w9a rules).
- Keep the IFAK model (FMVR_IFAK) as the visual of the closed kit on the hip; the tray uses its open state or a
  clean panel with the same textures - whatever reads clearer in VR.

## 3. Radio
The shoulder radio button is confusing. Make the radio a controller button: hold left Y = push-to-talk.
While held: a small radio panel appears in front (same style as the tray) with the report options as big
buttons
(select by pointing + trigger, or by thumbstick up/down + trigger); release Y = send. The shoulder radio can stay as
a visual and also work by touch, but every hint must say "зажми Y" (KZ equivalent). The watch shows the radio
icon when a report is expected.

## 4. "Не понятно что куда" - guidance pass over EVERY scenario and lesson
- One consistent step panel: the current step in short words + the controller button icons, always readable
  (on the left wrist when looking at it AND a small floating line at the bottom of view for the first seconds
  of each new step), RU/KZ.
- Target highlighting: the item to take glows in the tray; the place on Erlan where it goes glows (e.g. band
  zone high on the thigh) - reuse/extend the existing hint system.
- First launch: a 60-90 s controls tutorial (move/turn, grab with grip/trigger, kit on X, radio on Y, watch)
  that can be skipped and replayed from the menu.
- Walk through every scenario/lesson as a player would (simulate real input, not bot APIs) and list in
  docs/W10_AUDIT.md every step where it is unclear what to do, where to go, or what button to press - then fix.

## 5. Full bug audit
- Read all w9a-w9d code paths and the device logs; run the full test suites; fix the known failures from
  w9c/w9d (order-dependent tests, the groin packing bot accuracy, BattleSoak allocation) where they are real.
- Check on device-like settings: menu -> each scenario -> debrief -> back to menu without restarting the app;
  evac by dragging to the helicopter (can a real player drag Erlan there in reasonable time? is the path
  clear? is the door zone obvious?); self-aid (one-handed TQ reachable with the tray? radio via Y);
  text readability; nothing spawns inside walls or under the ground; no item ever floats.
- docs/W10_AUDIT.md: table of every bug found (where, cause, fix, test). Russian summary at the top for the
  owner, short and plain.

## Done criteria
All above implemented, tests green (or documented as truly flaky with reason), QuestSceneAudit OK, Android
build with the XR guard line + libUnityOpenXR.so, APK copied to `C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W10.apk`.
If the headset is connected: pull logs first, then install + launch smoke test (screencap via adb shell + pull).
Commit on w10, push w10 to origin, append to docs/PROGRESS.md, create logs/w10-audit.done.
