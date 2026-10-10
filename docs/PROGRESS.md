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

## 10.10 06:40 — S2 casualty: core done (in progress: visual polish)
- Art: `tools/blender/prepare_casualty.py` (Swat Guy → `Assets/Art/Characters/Erlan/Erlan.fbx`, 38.5k tris, 1.7 MB; textures retinted by `retint_textures.py` to olive/khaki digital camo, 2K albedo JPG + 1K normal/smoothness). Gear FBX in `Assets/Art/Gear/` (DragHandle, IFAK_Pouch, CAT_Tourniquet with separate Band/Windlass/Clip/Buckle/RedTip/TimeStrap children, Helmet, AmputationStump with bone).
- **Assumption:** Swat Guy has no visible face (balaclava + NVG helmet). Kept as a balaclava; pallor/cyanosis/sweat are implemented in the shader behind `_SkinMask` (black now) — plug a face model/mask later. The model's own vest (retinted) is the plate carrier; my procedural PC shell floated off the back in bent poses, so it is not exported (`INCLUDE_PC=False`).
- Unity: `MixamoImport` (Humanoid, clip names/loops, root XZ/rot baked, height *based on feet* — "original" floated lying clips 1 m up), `CasualtyAssetBuilder.BuildAll` (anim copies, materials + FBX remaps, `Erlan.controller` 15 states driven by int `State`, VoiceBank, `Resources/BloodSettings`, `Assets/Prefabs/Erlan.prefab`). No root motion (falls must not carry him through walls).
- Runtime: `Medical/PainModel`, `Medical/CasualtyPose` (+mapper, EditMode-tested); `Runtime/Casualty`: CasualtyRig (bone map, BodyRegion trigger colliders, two-bone IK API, breathing, flinch, look-at, ground clamp, skinned-surface wound placement), CasualtyController (owns physiology/protocol/pain, fall→lying by AVPU/loss/pain, wounds, tourniquet API, handling pain), CasualtyVoice; `Blood/`: WoundSite (HR-synced arterial spurts, drips), BloodPoolManager (area ∝ volume, drag trail), BloodSoak (shader spheres), BloodOnHands. Shaders `FieldMedic/CasualtyLit` (soak + skin params) and `FieldMedic/BloodDecal` (URP Lit transparency keywords don't stick from script → own shader).
- Audio: 114 Freesound clips (`tools/fetch_sounds.ps1`, attributed), 50 TTS voice lines RU/KZ (`tools/tts_voice.ps1` + `voice_post.py`: Irina pitched to ~120 Hz + strain), `docs/VOICE_SCRIPT.md`.
- Tests: EditMode 22/22, PlayMode 16/16.
- Workflow: `Invoke-UnityBatch -Method FieldMedic.EditorTools.CasualtyAssetBuilder.BuildAll` → `MainSceneBuilder.Build` → `CasualtyDemo.CaptureShots` (edit-mode simulation, screenshots s2_*).

## 10.10 07:30 — S2 casualty DONE
- Screenshots: `docs/screens/s2_erlan_idle.png` (crouched at the parapet), `s2_erlan_fall.png` (VOG blast, arterial spray hits the wall), `s2_erlan_wounded.png` (+ `_top`: soaked glossy trouser leg, ragged wound, pulsing jet, pool), `s2_erlan_wide.png`.
- Gotchas found: (1) in edit-mode batch rendering skinned meshes lag the stepped bone pose → `forceMatrixRecalculationPerRender` in the demo; (2) the Swat mesh has inner layers + crossed legs → wound placement = outermost vertex rigidly skinned (≥80%) to the limb bone inside a narrow cylinder; thigh anterior from knee bend; (3) `GetComponent() ?? Add` fake-null trap.
- Known issues / next: no face (balaclava) so pallor/cyanosis are not visible yet (shader ready via `_SkinMask`); TTS KZ lines are phonetic placeholders (see docs/VOICE_SCRIPT.md); drag handle/IFAK/CAT are rigid on bones (S3 will make CAT grabbable — CAT children are separate objects); breath loop clips are not seamless loops; lying clip transitions can pop slightly (no root motion by design).
- S3 hooks: `CasualtyController.ApplyTourniquet/Handle/AddWound/Hit`, `CasualtyRig.SetLimbIK/Part(region)/Flinch`, `BloodOnHands` on both hands, `WoundSite.Active`.

## 10.10 07:30 — S3 treatment: core implemented (in progress: bot tests, screenshots)
- Medical (pure C#, EditMode-tested, 45/45): `Tourniquet.cs` (`TourniquetState` stages InPouch→…→TimeMarked, slack/half-turns/lock/secure/time, occlusion model: tight strap alone = venous congestion → wound bleeds MORE; full occlusion stops bleed + distal pulse; joint = capped occlusion; slip on rough handling; `LimbGeometry` segment units 0 hip..1 knee..2 ankle), `Procedures.cs` (packing strokes, Israeli bandage, chest seal steps + burp, needle rules, NPA), physiology: physical tourniquet list, `PulseStrength(site)`, entry+exit chest wounds must both be sealed, vented seal delays tension (420 s), `BurpSeal`. Protocol: `TourniquetApplied/TimeMarked/Discover/NeedleDecompression/ChestSealApplied/NasalAirwayInserted/PressureReleasedEarly` + penalties (tq_loose, tq_over_clothes, elevation, not_exposed, needle_wrong_site...). Quick check without Unity: `cd tools/medtest; dotnet run`.
- Runtime: `Interaction/ProxyHandle` (grab spot that never moves; owner reads the hand). `Casualty/`: `LimbHandle` (ankle/knee/wrist → IK with lag, sag, joint limits, pain, gravity fall-back, elevation mistake), `CasualtyDrag` (handle behind neck; friction/mass lag, head-first yaw, floor follow/drop/pitch, TQ slip on jolts, thumbstick pull-to-crawl), `LogRoll` (shoulder+hip two-hand, propped on side, recovery), `CasualtyInteraction` (built at runtime by CasualtyController.Start: handles, WoundCare per wound, his own IFAK with his CAT, "grabs your arm" reflex, cut-clothing exposure).
- `Treatment/`: `Kit` (runtime primitive items, merged meshes, ring mesh), `MedItem`, `IfakPouch` (player pouch body-locked at the belt), `TourniquetCAT`, `WoundCare`, `HandTreatment` (sweep/pulse haptics/pressure/nitrile), items Gauze, Bandage, ChestSeal, Needle, Npa, Shears, Gloves, Marker, Blanket, Card. `Scenarios/`: ScenarioClock (01:40 start), CoverZone, `Bots/MedicBot`.
- Desktop sim: G = lock grip (hand keeps holding while you switch with Tab), F/MMB = trigger, X = crawl back.
- Supervisor notes: soaked-fabric smoothness 0.78 → 0.42 (no glossy green highlight); chemlight now URP/Unlit green (was magenta).
- Assumptions: CAT windlass locks when released within ±35° of the clip (rod along the limb); either twist direction tightens; Israeli bandage pressure bar = reverse wrapping direction (or 120° wrist twist); the plate carrier is ignored for needle landmarks (S5 may add opening the PC).
