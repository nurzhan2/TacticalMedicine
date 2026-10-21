# Stage w10e - prove every scenario is completable (until 06:20, owner deadline 07:00)

Branch `w10`. A good APK is already on the Desktop (FieldMedicVR_W10.apk, 04:35) and on the headset - it is the
fallback and must NOT get worse. Read docs/W10_AUDIT.md and logs/w10b-fullgame.done first.

Left open by w10b: item 10 "проверить все сценарии, потому что некоторые не выполнимы" - real-input
completability of S1-S8 + SA was not proven, and ~10 bot tests tuned to the old Erlan body fail.

1. For each scenario S1..S8 and SA (in this order: S1, S2, SA, S3..S8): drive it with the REAL input path
   (Input System XR controller test devices, coach hints on, skip-step NOT used) from start to the debrief.
   Where a real player gets stuck (target unreachable on the new soldier body, step never completes, evac
   impossible), fix the cause. Re-tune the failing bot tests to the new body only where the bot is wrong.
2. Check the clock (Get-Date) after each scenario. At 05:50 stop fixing.
3. 05:50-06:20: run EditMode + the targeted PlayMode suites. ONLY if they pass and QuestSceneAudit is OK: build,
   check the XR guard line + libUnityOpenXR.so, copy over Desktop\FMVR_DEMO\FieldMedicVR_W10.apk and install on
   the headset (adb install -r -g). Otherwise keep the 04:35 APK untouched.
4. Update docs/W10_AUDIT.md: a table "Сценарии - проходимость" (scenario, completes yes/no, what was fixed, what
   is left) + a short plain Russian summary on top. Commit, push w10, then fast-forward main to w10
   (`git branch -f main w10` while on w10, `git push origin main`) so the owner's friend gets it on main.
5. Create logs/w10e-scenarios.done with a one-paragraph summary by 06:30 at the latest.
