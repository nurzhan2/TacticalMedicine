# Stage w9c - build and check on the Quest 3S

Branch `w9` after w9a and w9b. Goal: a build the owner can play right away.
1. Run all tests, QuestSceneAudit, then BuildTools.BuildAndroid (tools/unity.ps1). Require the log line
   `[FMVR] XR settings registered: android loaders=1 initOnStart=True` and `lib/arm64-v8a/libUnityOpenXR.so`
   inside the APK (zip check). Copy the APK to `C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W9.apk`.
2. If `adb devices` (adb from the Unity Android SDK, see tools/quest_run.ps1 / tools/quest_diag.ps1) shows
   the headset as `device`: install with `adb install -r -g`, clear logcat, launch
   kz.qorsa.fieldmedic, wait 40 s, pull logcat + /sdcard/Android/data/kz.qorsa.fieldmedic/files/fmvr_log.txt
   into logs/device/w9_*. Screenshot ONLY via `adb shell screencap -p /sdcard/fmvr.png` + `adb pull`
   (PowerShell redirection corrupts PNGs). Check: OpenXR loader active, display running, no exceptions,
   fps. Note: if the headset is not on a head, Quest pauses the app after a few seconds - that is not an
   error; the 5 s XR self-check must not report "XR FAILED" in that case (fix it if it does: ignore the
   check while the app is paused / focus lost).
   If no device is connected, skip the device part and say so in the summary.
3. Write a short Russian note docs/W9_WHATS_NEW.md for the owner: what changed, how to try each item
   (kit, evac by dragging to the helicopter, sky text, self-aid scenario, training lesson), what still
   waits for him (ElevenLabs key + voice ids, models from GPT-6 Astra into SourceAssets/Incoming).
4. Commit, append to docs/PROGRESS.md, create logs/w9c-device.done.
