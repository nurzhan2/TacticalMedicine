# Progress log (append, newest at bottom)

## 10.10 02:45 — setup (Claude in chat session)
- Project retargeted to Unity 6.3 LTS (6000.3.26f1); Meta XR SDK 207 (all) imported; opens in batchmode; 11 EditMode tests pass.
- Medical core done: `Assets/Scripts/Medical` (Wound, PatientPhysiology, CareProtocol) + tests.
- Assets downloaded: `SourceAssets/Mixamo/*.fbx` (Swat Guy skin 115 MB git-ignored + 19 animations), `SourceAssets/Audio/freesound_manifest.tsv` (~270 CC0/CC-BY candidates).
- Docs: CLAUDE.md, docs/GDD.md, docs/RESEARCH.md, docs/prompts/s1..s7.
- Night runner: run_all.ps1 + watchdog.ps1 (Claude Code CLI, stages s1..s7).
- Owner decisions: character = Mixamo Swat Guy; gore = maximum naturalistic; voice = Russian + Kazakh; power settings handled by owner.

## 10.10 04:40 — S1 foundation (resumed after interruption)
- Done: `ProjectSetup.Apply` (Android/IL2CPP/ARM64/API 32-34/Vulkan/Linear, URP Mobile asset MSAA4 HDR off hard main-light shadows 15 m, OVR config ControllersAndHands + passthrough Supported, OpenXR Android Meta Quest feature group, Meta UPST FixTasks via reflection — Meta XR Simulator installer task filtered out because it downloads an MSI).
- Runtime asmdef + `Input/` (IHandInput, OVRHandInput, DesktopHandInput + DesktopSimulator, ScriptedHandInput, HandRig) + `Interaction/` (HandGrabber, Grabbable, GrabPoint, GrabHighlight, Pokeable, Pressable, Haptics).
- Editor: `MainSceneBuilder` (night trench placeholder, OVRCameraRig floor origin, OVRHandPrefab per anchor, placeholder gloves, crate + cube/canteen/chemlight/heavy two-handle ammo box/push button, camera bookmarks), `ScreenshotTool`, `BuildTools.BuildAndroid`, `tools/unity.ps1`.
- PlayMode tests `Assets/Tests/PlayMode/GrabInteractionTests.cs` (grab/move/release, mass lag, two-hand, grab point, button press).
- Gotcha: after Meta UPST FixTasks the editor never honours `-quit` → all batch entry points now call `EditorApplication.Exit(0)` themselves.
- Assumption: placeholder gloves render even when OVR tracked hands are visible (S3/S2 will replace with real hand meshes).

## 10.10 05:35 — S1 foundation DONE
- Compile clean; EditMode 11/11, PlayMode 10/10 (grab/move/release, mass lag, two-hand, grab-point, button poke, layer-collision regression, velocity tracker, desktop hand, haptics).
- `Assets/Scenes/Main.unity` generated (only scene in Build Settings); screenshots `docs/screens/s1_main.png`, `s1_props.png`, `s1_trench.png`.
- **Android build: SUCCEEDED** — `Builds/FieldMedicVR.apk` 68 MB, IL2CPP ARM64, 21 min first build (Gradle 9.3.1 offline ok). Not tested on device (no headset tonight).
- Template-version traps fixed (files came from a newer Unity/URP than 6000.3.26f1):
  - `DynamicsManager` collision matrix unreadable → every layer ignored every other (objects fell through floors). `ProjectSetup.ApplyPhysics` re-enables all pairs; regression test added.
  - `UniversalRenderPipelineGlobalSettings` had `m_AssetVersion: 11` (URP 17.3 expects 10) → build preprocess refused; set to 10.
  - 3 missing-script OpenXR feature entries (Android + Standalone) NRE'd the OpenXR validator on every load → removed by `ProjectSetup.RemoveMissingFeatures`.
- Workflow: `. .\tools\unity.ps1; Invoke-UnityBatch -Method ...; Invoke-UnityTests -Platform PlayMode` (exit codes now reliable). Disk: ~8 GB free after the build (Library/Bee ~ big; delete it only if < 4 GB).
- Next (S2): environment art + casualty; the props in Main are test placeholders, keep `TestProps`/`Hands` naming or update the builder.
