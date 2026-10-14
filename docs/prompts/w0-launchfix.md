# Stage w0 - make the APK launch in VR on Quest 3/3S

Context: docs/GDD_V2.md section 0. Owner reports the V1 APK "did not open" on a headset.
No headset may be connected during the night; work from evidence in the project.

Tasks
1. Audit the Android/XR setup end to end and FIX every problem found:
   - XR Plug-in Management: OpenXR enabled for Android; Meta Quest Support feature on; target devices
     include Quest 3 and Quest 3S; interaction profiles (Oculus Touch / Meta Quest Touch Plus) present;
     hand tracking optional (not required).
   - Final merged AndroidManifest (export the Gradle project or read the merged manifest from the last build):
     must contain MAIN + LAUNCHER + com.oculus.intent.category.VR on the Unity activity,
     com.oculus.supportedDevices including quest3|quest3s, no permissions/features that make install or
     launch fail (e.g. required=true hardware that Quest lacks). Check what V1 QuestManifestCleanup removes.
   - Graphics: Vulkan first (GLES3 fallback ok), IL2CPP ARM64 only, minSdk 32, first scene in Build Settings
     is the boot/menu scene and loads without exceptions.
   - Startup code: no exceptions in Awake/Start when no desktop simulator/keyboard exists (guard DesktopSim
     with #if UNITY_EDITOR || !UNITY_ANDROID), no missing references, no blocking loads > 5 s on main thread.
2. Write tools/quest_check.ps1 (ASCII only): finds adb (C:\Android\platform-tools\adb.exe); if a real Quest
   is attached (ignore emulator-*): adb install -r -g Builds\FieldMedicVR.apk; launch via
   `adb shell monkey -p kz.qorsa.fieldmedic 1`; after 40 s save filtered `adb logcat -d` (Unity, AndroidRuntime,
   OpenXR, libc, DEBUG, CRASH) to logs/quest_<time>.txt and `adb exec-out screencap -p` to logs/quest_<time>.png;
   print PASS if the Unity player started and no FATAL EXCEPTION/SIGSEGV. If no device: print NO DEVICE, exit 0.
3. Build APK -> Builds/FieldMedicVR.apk and copy to C:\Users\user\Desktop\FieldMedicVR.apk. Run quest_check.
4. Document findings + fixes in docs/LAUNCH_FIX.md (what was wrong, what changed).

Done: build succeeds, manifest verified, LAUNCH_FIX.md written, quest_check run (PASS or NO DEVICE), committed.
