# Stage w6 - performance, build, device check, report

Tasks
1. Performance pass for Quest 3S at 72 Hz (GDD_V2 section 7): LODs, impostors, texture sizes/ASTC,
   shadow casters, particle counts, draw calls, GC. Use the existing QuestPerformance settings (FFR, dynamic
   resolution). Record before/after numbers in docs/PROGRESS.md.
2. EditMode + PlayMode tests all green (update V1 tests that changed). Bot medic completes all scenarios in
   Training and Combat.
3. Build APK -> Builds/FieldMedicVR.apk, copy to C:\Users\user\Desktop\FieldMedicVR.apk.
   Run tools/quest_check.ps1 (PASS or NO DEVICE).
4. Write docs/MORNING_REPORT_V2.md in Russian, short: what was done, screenshots list, what is waiting for
   the owner (Meshy files with exact names, Satbayev emblem file, headset connection for launch check),
   known issues. Push (background push rule from CLAUDE.md; never block on push).

Done: APK on Desktop, tests green, report written, committed and push started.


SUPERVISOR CHECK: re-verify all items in docs/SUPERVISOR_NOTES.md (15.10 sections) are fixed in the final screenshots; fix any that are not before building.

