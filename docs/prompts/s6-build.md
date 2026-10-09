# Stage S6 — Quest build, performance, docs, morning report

Read CLAUDE.md, docs/PROGRESS.md. Time budget ~75 min.

## Tasks
1. **Android APK**: `BuildTools.BuildAndroid` → `Builds/FieldMedicVR.apk`. Fix every build error (Gradle, manifest, OpenXR features, permissions: hand tracking, passthrough). Verify the APK: size, `aapt dump badging` if available (package, permissions, `com.oculus.intent.category.VR`), min/target SDK. Keep the APK out of git (it is .gitignored); copy it also to `C:\Users\user\Desktop\FieldMedicVR.apk` for the owner.
2. **Performance**: run the perf report script; ensure budgets (CLAUDE.md). Apply: static batching / GPU instancing for sandbags & boards, LODs for Erlan if needed, texture ASTC compression, mixer & particle limits, Fixed Foveated Rendering high + dynamic resolution if supported via OpenXR Meta settings.
3. **README.md** (Russian): what the game is, how to open in Unity 6.3, how to play in the editor (desktop simulator controls), how to install the APK on Quest 3S via Meta Quest Developer Hub (drag & drop) or `adb install -r`, scenario list, how to record real voice (point to docs/VOICE_SCRIPT.md, file naming), known limitations, next steps. Keep `ATTRIBUTION.md` complete.
4. **docs/MORNING_REPORT.md** (Russian, for the owner): what was built, screenshots list with short captions, test results (numbers), build status/APK path, what's left / known issues, recommended next steps. Short and honest.
5. Final screenshot set `docs/screens/final_*.png` (best 6–8 shots).

## Done criteria
APK built (or a precise blocker documented with what was tried), README + MORNING_REPORT written, pushed; `logs/s6-build.done`.
