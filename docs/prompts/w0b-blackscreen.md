# Stage w0b-blackscreen — "the APK shows only BLACK on the Quest 3S" (owner report 15.10 23:32)

Owner confirmed: on the headset the app started but showed only a black screen (V1 APK). Since then the APK grew
from 98 MB to 256 MB (w2/w3/w4 content), so the risk is now bigger. No headset is connected tonight. Goal: remove
EVERY plausible cause of a black screen / silent early crash, add guard rails + diagnostics, and ship APKs that
let us bisect the problem in minutes once the headset is plugged in. Read docs/LAUNCH_FIX.md first (w0 work).

w4-modes is paused mid-stage (WIP commit 6b98f16). Do NOT continue w4 features. Keep the project compiling and
the w4 WIP intact; make your changes in separate commits prefixed `w0b:`. git push may fail (remote 404) — ignore.

## A. Cause list — check each, fix it, write the evidence in docs/BLACKSCREEN_FIX.md

1. **No frame during loading (main-thread stall).** Horizon OS shows black (and may kill the app) when the app
   stops submitting frames. Measure every blocking step from process start to first gameplay frame.
   - Boot scene must render its first frame in < 1 s and keep rendering every frame while Main loads. Check Boot
     dependencies with AssetDatabase.GetDependencies (budget < 5 MB, no references to battlefield assets).
   - Scene *activation* of a huge Main is itself a long stall. Split Main: `Main_Core` (terrain, player rig, Erlan,
     medic gear, HUD) is loaded first; battle/vehicles/air/crowd/props/audio go into additive scenes loaded with
     LoadSceneAsync one after another AFTER the player already sees the world. Activation of each additive scene
     must stay < 200 ms in editor measurement; spread heavy Awake/Start work over frames (coroutines, budgets).
   - No synchronous Resources.LoadAll / big procedural generation at runtime — bake in the editor.
2. **Shader / Vulkan pipeline compile stall on first frames.** Collect a ShaderVariantCollection (or Unity 6
   GraphicsStateCollection) for Main_Core + battle and warm it up inside Boot with the progress bar.
3. **Out of memory / silent crash.** Android texture overrides: ASTC, max 2048 (props 1024, small items 512),
   mipmaps on, Read/Write off on meshes and textures, audio Vorbis, clips > 5 s Streaming, no uncompressed WAV in
   build. Produce a build-report top-50 largest assets. **APK budget ≤ 150 MB** (build fails > 200 MB). Fix the
   offenders (BlenderKit 4K textures, scans, big audio) rather than raising the budget.
4. **Rendering black although the app runs.**
   - Exactly one active camera, under OVRCameraRig CenterEyeAnchor, tagged MainCamera; no other camera rendering
     to the display.
   - Opaque background: clear colour alpha = 1. A passthrough setup (alpha 0 background + OVRPassthroughLayer)
     shows black when passthrough is not running — remove passthrough from Boot and Main, and set OS splash
     `com.oculus.ossplash.background` to `black` (not passthrough-contextual) or a static splash image.
   - Culling mask = everything needed, near 0.05, far >= 1500, fog not black-out.
   - URP: render scale 1, HDR off, no renderer feature needing depth/opaque texture, post-process exposure sane.
   - Dynamic resolution min scale >= 0.7.
   - TMP font assets with RU+KZ glyphs are included, and TMP/UI shaders survive stripping (Always Included).
5. **Player in the wrong place.** Tracking origin = floor. At start, raycast down from the spawn point and put the
   rig on the terrain. Never inside a collider or mesh; Boot UI 2 m in front of the head.
6. **Exceptions / early crash.**
   - Every startup system is wrapped so that one failing system disables itself and logs, and the rest still runs.
   - Application.logMessageReceived + a crash flag write `persistentDataPath/fmvr_log.txt`.
   - If the previous launch did not reach "gameplay ready", the next launch goes to SAFE mode (Main_Core only)
     and shows a small white note.
7. **XR start failure.** Log the active loader, OpenXR runtime name and display subsystem state at 1 s and 5 s.
   If XR is not running after 5 s, log a clear `[FMVR] XR FAILED` line.
8. **Unity splash.** Disable the Unity splash if the license allows; otherwise make it short.
   Keep OVR/OpenXR settings from w0 (QuestLaunchSetup / QuestManifestAudit).

## B. Guard rails (editor, run on every build and in tests)

- `QuestSceneAudit`: fails the build on two display cameras, alpha-0 background, passthrough layer, missing TMP
  shader, a Boot dependency over budget, or an APK over budget.
- EditMode tests for each rule.
- PlayMode test: Boot → Main_Core → all additive scenes, no error logs, max frame time logged.

## C. Deliverables (copy to C:\Users\user\Desktop\FMVR_TEST\)

1. `1_HELLO.apk`: same player/XR settings, one scene with a grey floor, a cube and white text "HELLO VR". This
   proves the toolchain/manifest.
2. `2_SAFE.apk`: Boot + Main_Core only (training at Erlan, no battle).
3. `3_FULL.apk`: everything.
4. `3_FULL_DEV.apk`: Development Build of FULL with logcat output.
5. `tools/quest_bisect.ps1`: installs 1→2→3 with `adb install -r -g`, launches each, waits, takes a screenshot,
   pulls `fmvr_log.txt` + filtered logcat, prints PASS/FAIL per APK and the first failing one with the log lines.
   Also extend `tools/quest_check.ps1` with an `-Apk` parameter.
6. A short Russian `README.txt` in that folder: how to install with the laptop cable, where the app is in the
   headset (Library → Unknown sources), and the order of testing.
7. `docs/BLACKSCREEN_FIX.md` with a table: cause → evidence → fix → test, and the measured numbers:
   - Boot first frame;
   - Main_Core activation;
   - each additive scene;
   - APK sizes;
   - top assets.

## Done criteria

- All tests green.
- QuestSceneAudit and QuestManifestAudit OK.
- The 4 APKs are on the Desktop.
- FULL ≤ 150 MB (or explain exactly why not, with the largest assets list).
- `docs/BLACKSCREEN_FIX.md` written; PROGRESS.md updated; commits made.
- Then create `logs/w0b-blackscreen.done`.
