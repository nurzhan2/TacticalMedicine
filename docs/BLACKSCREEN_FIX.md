# BLACKSCREEN_FIX — "the APK shows only black on the Quest 3S" (stage w0b, 16.10.2026)

The owner reported on 15.10 (23:32) that the V1 APK started on the headset but showed only black. No headset was
connected during this stage. Everything below comes from the project, the editor, the build reports and new
tests. The last step is `tools/quest_bisect.ps1`, which runs once the headset is plugged in. See also
`docs/LAUNCH_FIX.md` (w0).

## 1. The most likely cause: a passthrough fade with no passthrough behind it

`OVRCameraRig` in the V1 trench and the V2 battlefield carried `ProximityPassthrough` (V1 comfort feature). It had a
quad 8 cm in front of the eyes with `FieldMedic/PassthroughFade`, which multiplies the scene colour **and alpha** by
`1 − opacity`, so that the compositor shows the room underneath. Opacity goes to 1 when the head or a hand is within
0.25 m of the play-area boundary, **or outside it**. Two problems on the headset:

* The boundary comes from `OVRManager.boundary` (a legacy API under OpenXR). If it is not configured or not returned
  (stationary boundary, some Horizon OS versions, OpenXR v207), the script falls back to a **2.5 × 2.5 m square
  around the tracking-space origin**. Any player standing more than 1.25 m from the origin, which is common in
  room-scale, counts as "outside" → opacity 1 → the scene is multiplied to black with alpha 0.
* Passthrough itself never runs: the project config has `insightPassthroughEnabled: 0`, and the OS gets no
  passthrough layer. So alpha 0 is composited over **black** instead of the room.

In addition, `OVRManager.isInsightPassthroughEnabled` was `1` in the battlefield rig, and the OS splash was
`com.oculus.ossplash.background = passthrough-contextual`.

**Fix:**
* `ProximityPassthrough`, its fade quad and `OVRPassthroughLayer` are removed from every scene. This is done by
  `SceneSplitter.Sanitize` (also applied to Boot and Hello) and by `AtmosphereBuilder`, so a rebuild cannot bring
  them back.
* `OVRManager` passthrough is off. `OVRProjectConfig` passthrough support is `None`, and the OS loading screen is
  `Black`. Both are enforced on every build by `QuestLaunchSetup`.
* Horizon OS's own guardian remains the safety net.
* `QuestSceneAudit` fails the build if any passthrough layer or `ProximityPassthrough` appears again.

## 2. Cause list → evidence → fix → test

| # | Cause | Evidence (before) | Fix | Test / guard |
|---|---|---|---|---|
| 1a | No frame while loading the battlefield (main-thread stall) | Boot loaded the whole `Battlefield` (1.1k battle objects, ~1.9 M triangles, 308 MB of source dependencies) as one scene; activating it is one long main-thread stall | Battlefield split by `SceneSplitter` into **Main_Core** (terrain, rig, Erlan, IFAK/gear, HUD/menu, scenarios, TACEVAC) plus additive **Main_Cover** (ruins, sandbag line, wrecks, cover points), **Main_Scenery** (far field, props, grass, smoke columns) and **Main_Battle** (BattleDirector + all layers). `GameplayScenes` streams them one after another with `LoadSceneAsync` after the core is visible: low background priority, a bigger async-upload buffer, 3 frames between scenes. Scenario launch and restart now use an async reload instead of a blocking `LoadScene` (the old one froze the headset on every run start). | `SceneStreamingTests.Boot_Core_Then_All_Additive_Scenes_Stream_Without_Errors` (timings below) |
| 1b | Boot must render at once and stay light | Boot dependencies | Boot = rig + 3 TextMesh lines + loader. It warms shaders **and** loads Main_Core in parallel, shows a progress bar, activates only at 100 %, and the panel follows the head at 2 m | `QuestSceneAudit` Boot budget 5 MB with no battlefield folders (EditMode `Boot_*` + `Real_Scenes_Pass_The_Audit`) |
| 1c | Cross-scene references after the split | 25 cross-group references (W0bDiag); 8 cross scene borders (battle → head/listener/suppression FX) | `SceneLinker` (execution order −10000) resolves them by hierarchy path in Awake. Additive scenes may only reference the core; the splitter fails otherwise. | EditMode link targets exist; PlayMode `Failed == 0`, BattleDirector.head set |
| 1d | Occlusion culling of a scene set | Occlusion was baked for one scene | Multi-scene bake of Core + additives. Culling is off while scenes stream or in SAFE mode (missing occluders would hide visible objects). | PlayMode: occlusion back on after streaming |
| 2 | Shader / Vulkan pipeline compile stall on the first frames | No warm-up | `Assets/Data/Shaders/Battlefield.shadervariants` (edit-mode render of 8 viewpoints, then the variants tracked in the PlayMode streaming run with the battle on) is warmed in Boot with `WarmUpProgressively` (6 per frame) while Main_Core loads. On Vulkan this pre-creates the shader programs; full PSO pre-creation would need a device-recorded `GraphicsStateCollection` (next step once a headset is available). | Boot log line `shader variants warmed` |
| 3 | Out of memory / silent crash, APK size | V1 → w3: 98 → 256 MB APK. 177 MB textures (many 2–4K BlenderKit maps without Android overrides), audio partly PCM/decompress-on-load, legacy V1 trench (`Main`) and the authored `Battlefield` both in the build | `AndroidAssetOptimizer`: ASTC 6×6 (UI 4×4), max 2048 for ground/Erlan, 1024 for props/ruins/vehicles, 512 for small items/aircraft/far scenery, mipmaps on, Read/Write off on textures and licensed environment models (runtime-read meshes kept), mesh compression, prebaked collision meshes. Audio: Vorbis q0.45, > 5 s streaming, 1.5–5 s compressed in memory. `Main` and `Battlefield` are no longer build scenes. Meta SDK reduced to `com.meta.xr.sdk.core` (the unused Voice/Platform/MRUK/Interaction/Audio SDKs ship resources and init code). | `Texture_Budgets` test, build report (below), `ApkRules` (warning > 150 MB, build fails > 200 MB) |
| 4a | Rendering black although the app runs: cameras | 3 cameras under the rig (2 disabled eye cameras, one wrongly tagged MainCamera), near 0.03, far 420 | Exactly one display camera, CenterEyeAnchor, MainCamera, opaque clear (alpha 1), near 0.05, far 1500, culling mask everything; additive scenes have no cameras | `CameraRules` tests + real scenes |
| 4b | Passthrough / alpha-0 background | See §1 | See §1 | `PassthroughRules` |
| 4c | URP | Mobile_RPAsset: HDR off, scale 1, MSAA 4, no depth/opaque texture, no renderer features | unchanged, now audited | `UrpRules` |
| 4d | Dynamic resolution | QuestPerformance min 0.7 / max 1.2 | unchanged (≥ 0.7) | — |
| 4e | TMP / UI shaders stripped | TMP + GUI/Text + BloodDecal + UiCard were not Always Included (some are created from code) | `EnsureAlwaysIncluded` on every audit/build | `ShaderRules` |
| 5 | Player in the wrong place | Rig y came from the scene | `RigGrounding` on the rig: raycasts down from the spawn point, puts the rig on the first solid surface, logs if the head volume starts inside a collider. Tracking origin forced to Floor. | PlayMode streaming test (rig on the ground, one camera) |
| 6a | Exceptions / early crash | Nothing persisted on the device | `FmvrDiagnostics`: every log line from every thread goes to `persistentDataPath/fmvr_log.txt` (the previous launch is kept as `fmvr_log_prev.txt`). Also logged: first frame time, scene load/activation, the longest frame per second for 60 s, `STALL` if > 1 s, pause/focus. Unity isolates exceptions per component (a failing Awake/Start disables only that script's callback); the new startup systems wrap their own work in try/catch and log. | quest_bisect pulls the log |
| 6b | Crash flag → SAFE mode | — | `LaunchGuard`: `fmvr_state.txt` started → core → ready. If the previous launch never reached "ready", the next launch is **SAFE** (Main_Core only, white note «БЕЗОПАСНЫЙ РЕЖИМ»). The next good launch is full again. `fmvr_force_full.txt` / `fmvr_force_safe.txt` override it (adb push). | `SafeMode_*` EditMode tests, `Safe_Mode_Loads_The_Core_Only` PlayMode |
| 7 | XR start failure | No information | Active loader, OpenXR runtime name/version, display subsystem, eye texture size logged at 1 s and 5 s. `[FMVR] XR FAILED …` if no display is running after 5 s. | — (device) |
| 8 | Unity splash | Splash off, Unity logo flag still on | Logo off, splash off (enforced). OVR/OpenXR settings from w0 kept (`QuestLaunchSetup`, `QuestManifestAudit`). | Manifest audit after every build |

## 3. Measured numbers

(filled in below from `logs/w0b_timings.txt` and `logs/apk_report_*.txt`)
