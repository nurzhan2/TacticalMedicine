# Stage w10g - "Автопрохождение": the bot plays a scenario while the owner watches in VR (time box 40 min!)

Branch `w10`. Owner, 21.10 11:17: "я не могу пройти, пусть бот пройдёт". He is waiting - be fast and safe.

1. Add an AUTOPLAY mode reachable from the scenario list: a big button "Смотреть, как проходит бот" /
   "Боттың өтуін көру" next to each scenario (or one toggle "Автопрохождение" in the scenario menu).
2. In autoplay the existing ScenarioBot logic (the one used by the PlayMode ScenarioBot tests, which passes
   S1-S8 + SA) drives the scenario at normal speed: it moves the medic's virtual hands / items, drags Erlan,
   applies every treatment, calls MEDEVAC, drags Erlan to the helicopter. The player's own controllers do nothing
   except: B (hold) = exit to menu. The player's rig follows smoothly: it stays ~1.5 m from the action, facing it
   (teleport with a short fade when the action moves more than ~4 m - no smooth artificial rotation, comfort).
   The step panel shows what the bot is doing right now in plain words ("Тащит Ерлана в укрытие", "Переворачивает
   на бок - ищет скрытую рану", ...) so the owner learns the order.
3. The debrief at the end works as usual (marked "автопрохождение").
4. Do not change anything else. Run EditMode + the ScenarioBot/S5 PlayMode test, QuestSceneAudit, then build
   (tools/final_build.ps1 does build + XR check + copy to Desktop\FMVR_DEMO\FieldMedicVR_W10.apk + adb install,
   no launch). Commit + push w10 (+ fast-forward main). Create logs/w10g-autoplay.done.
If the bot-driven visible hands are too hard in the time box, a simpler acceptable version: autoplay = the game
auto-skips each step after ~3 s (the existing skip-step path) with the step text shown, and teleports the player
next to Erlan when needed.
