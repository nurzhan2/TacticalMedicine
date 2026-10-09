# Stage S1 — Foundation (rig, input, grab system, project settings, build pipeline)

Read CLAUDE.md, docs/GDD.md first. Time budget ~75 min. Work in small committed steps.

## Tasks
1. **Project settings via editor script** `Assets/Editor/ProjectSetup.cs` (`FieldMedic.EditorTools.ProjectSetup.Apply`), run in batchmode:
   - Android build target, IL2CPP, ARM64 only, min API 32, target API 34 (or Meta-recommended), Linear color space, Vulkan (or GLES3 if Meta recommends), package `kz.qorsa.fieldmedic`, product name "Бауыр — Field Medic VR" (ASCII-safe fallback if needed), company "Qorsa Studio".
   - XR Plug-in Management: OpenXR for Android with **Meta Quest** feature group + hand tracking feature enabled (the repo already has OpenXR settings — verify and fix with the Meta Project Setup Tool API `OVRProjectSetup` / `OVRProjectSetupCommandLine` if available: try to call its "fix all" for Android programmatically; log what was applied).
   - URP: use `Assets/Settings/Mobile_RPAsset.asset` for Android and set it as default + quality level; MSAA 4x, HDR off, render scale 1, shadows: main light only, soft shadows off, distance 15 m.
   - OVRManager: tracking origin FloorLevel, hand tracking support "ControllersAndHands", passthrough support "Supported" (needed for proximity passthrough later).
2. **Runtime assembly** `Assets/Scripts/Runtime/FieldMedic.Runtime.asmdef` (refs: FieldMedic.Medical, Oculus.VR, Unity.InputSystem, Unity.TextMeshPro if used).
3. **Input layer** (`Runtime/Input`): `IHandInput` (Handedness, Pose position/rotation, Velocity, AngularVelocity, Grip 0..1, Trigger 0..1, PrimaryButton, IsTracked, Haptic(amplitude, duration)); `OVRHandInput` (controllers via OVRInput; hand tracking via OVRHand pinch/grab strength; auto-select active source); `DesktopHandInput` (editor: right mouse = move hand in a plane in front of camera, scroll = depth, LMB = grip, Shift+LMB = trigger, Tab = switch hand, Q/E rotate wrist (for windlass), WASD + mouse look camera, C = crouch toggle); `ScriptedHandInput` (set by tests/bots); `HandRig` component that picks the implementation (OVR in player builds with XR active, Desktop in editor without XR, Scripted in tests).
4. **Interaction layer** (`Runtime/Interaction`): `HandGrabber` (sphere overlap, priority, hover highlight, grab/release events, physics-hand style follow with configurable spring so heavy objects lag), `Grabbable` base (+ `GrabPoint` with snap pose, two-hand support, mass feel), `Pokeable`/`Pressable` (finger/palm contact with depth), haptics feedback helper. Keep it generic — treatment-specific logic comes in S3.
5. **Main scene builder** `Assets/Editor/SceneBuilders/MainSceneBuilder.cs` → `Assets/Scenes/Main.unity`: OVRCameraRig (prefab from the Meta core package), hand anchors with `HandRig`+`HandGrabber` per hand, simple placeholder ground/trench box, a few grabbable test objects, a directional moon light. Add it to Build Settings as scene 0 (keep the other scenes after it or remove them from the build list).
6. **Tools**: `tools/unity.ps1` (functions: `Invoke-UnityBatch -Method X`, `Invoke-UnityTests -Platform EditMode|PlayMode`, returns pass/fail and prints errors), `Assets/Editor/ScreenshotTool.cs` (`FieldMedic.EditorTools.ScreenshotTool.Capture` loads a scene, renders from named camera bookmarks to `docs/screens/<name>.png` 1600x900).
7. **Tests**: PlayMode test: ScriptedHandInput moves to a grabbable cube, grips, moves, releases → cube moved; EditMode tests still green.
8. **Build pipeline** `Assets/Editor/BuildTools.cs` `BuildAndroid` → `Builds/FieldMedicVR.apk` (Development build off, compression LZ4HC). Run it once now (time-box 50 min; if the Gradle/IL2CPP build fails, capture the error in PROGRESS.md, try one fix, then move on — S6 will finish it). Delete `Library/Bee` intermediate junk only if disk < 4 GB.

## Done criteria
- Project compiles in batchmode with zero `error CS`; EditMode + PlayMode tests pass.
- `Assets/Scenes/Main.unity` exists with the rig and grab system; a screenshot `docs/screens/s1_main.png` exists.
- Build attempt result recorded in PROGRESS.md.
- Everything committed and pushed. Create `logs/s1-foundation.done` with a one-paragraph summary.
