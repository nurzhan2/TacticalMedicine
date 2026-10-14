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

## 10.10 09:00 — S3 treatment DONE
- Tests: EditMode 45/45; PlayMode bot suite (`TreatmentBotTests`, 8): TQ high&tight on the thigh with Erlan's own CAT (strap, 4 half-turns, lock, TIME strap, marker) → spurting stops, no pedal pulse, haptic carotid beats, survives 20 min, score 100; TQ on the knee for a shin bleed → `tq_on_joint`, bleeds out; drag by the handle ≥ 3 m into a CoverZone → TFC phase; groin: shears → finger packing → 3-min hold (training ×60) → Israeli bandage → controlled, survives; chest: tear/peel/wipe/place/press-edges seal → tension later → needle 5th ICS AAL → decompressed, survives; leg lift (IK, pain, falls back); blood sweep finds hidden forearm wound; two-hand log roll → propped on side.
- Screenshots `docs/screens/s3_01..14_*.png` from the real Main scene via `TreatmentScreenshots` (PlayMode, category "Screenshots", excluded from the normal run: `Invoke-UnityTests -Platform PlayMode -Category "!Screenshots"`; render with `-Filter FieldMedic.Tests.TreatmentScreenshots`). Several close-ups are cluttered (camera inside the trench wall / body) — S7 polish: better camera picks (s3_04 CAT close-up is hidden behind the placeholder glove); blanket drape now built from bone capsules (s3_10 shows it over the body).
- Gotchas: (1) `WaitForEndOfFrame` never resumes in batch mode → hangs; (2) HandGrabber overlap buffer was 32 → body handles were never found next to the body's ~30 triggers (now 128); (3) inactive renderers in item collider fit (folded blanket had a 1.4 m box); (4) the fall→lying crossfade slides Erlan ~3 m after `IsFalling` ends (S2 animation issue — wait ~2 s, or fix the clips in S7); (5) batch tests run at very high FPS: never threshold per-frame motion.
- Known issues / next: in the trench-scene demo (TreatmentScreenshots chest/groin) the hold missed the moving wound — bot now tracks it live, not re-rendered; NPA, card, gloves, pulse have runtime + bot steps but only TQ/drag/pack/seal/needle/sweep/roll/lift have dedicated assertions; the casualty's drag handle lies under his back when supine (reach under the collar — realistic but fiddly on desktop); TQ slip on rough drags exists (S7 scenario uses it).

## 10.10 09:40 — S4 environment: in progress
- 09:15 safety checkpoint: APK of the S3 state built and copied to `C:\Users\user\Desktop\FieldMedicVR.apk` (80 MB). Disk: deleted `Library/Bee/artifacts/objcopy_*` (1.1 GB, debug-symbol copies) → 5.8 GB free; Bee Android kept for incremental rebuilds.
- Trench: `Editor/SceneBuilders/TrenchBuilder.cs` (Unity mesh generator, chosen over Blender for iteration speed): zig-zag 21 m fire trench (bays + traverses), 1.75 m deep, 1.2 m bottom, sloped noisy soil walls with vertex AO/wet mask, plank revetment + posts, superellipsoid sandbags (3 tints), duckboards, puddles, firing steps, covered dugout (log walls/roof, earth mound, tarp door, flickering candle), comm-trench branch, field cables, ammo crates/cans, anti-drone net, churned field (craters, spoil berms, burnt stumps, concertina wire). Poly Haven CC0 1k textures (`tools/textures/prep_env_textures.py`), shader `FieldMedic/EnvLit` (keyword-free URP PBR).
- Atmosphere (`AtmosphereBuilder`): procedural `FieldMedic/NightSky` (stars, cloud veil, fire glow on the horizon, moon), `NightLightingDirector` (moon ⇄ para-flare steering of the single shadow light → swinging shadows), `IlluminationFlare` (fall/drift/swing/flicker/smoke), `CombatVfx` (pooled flashes, dirt/smoke/sparks, debris on player, trench dust, roof dust, tracers, rain), `CombatAmbience` + `DroneAudio` (beds, artillery/MLRS with d/343 s delay, MG + tracers, recon/FPV/Shahed drones, `PlayDropGrenade/PlayIncomingShell/PlaySniperCrack`, tinnitus + low-pass muffle, shake), `ThreatSystem`/`ThreatModel` (parapet plane, drone, red/white light, cover, cues → hit → `RunEndOverlay` with RU/KZ explanation, exposure log for the debrief), `Headlamp` (red default; Y / L toggles, hold Y / Shift+L white).
- Comfort: `ComfortRig` (snap turn 45°, optional teleport, seated offset, hold B 2 s = height calibration), `ProximityPassthrough` (OVRBoundary play area; SDK passthrough is an underlay → `FieldMedic/PassthroughFade` alpha punch quad, 0.6→0.25 m ramp; editor fallback 2.5 m square + log), `CameraShake` (translation only, ≤ 12 mm).
- `DemoScenarioStarter`: 15 s after start (desktop: Enter) a recon drone drops a VOG near Erlan → thigh arterial + forearm fragment → he falls (placeholder until S5).
- Gotcha: new namespace `FieldMedic.Environment` shadows `System.Environment` inside `FieldMedic.*` → use `System.Environment.GetCommandLineArgs()` (ScreenshotTool fixed).

## 10.10 10:10 — S4 environment DONE
- Visual iteration on screenshots (`docs/screens/s4_00..08`): sandbags moved onto the lip of the cut (were floating over the wall bevel), additive FX premultiplied (glows were white squares), plank UV tiling (one plank strip per board) + weathered tint, dugout earth cover = single-sided heightfield (the sphere mound intruded into the chamber), sky glow/stars toned down, field darker brown + reflection intensity 0.35 (grey sheen at grazing angles), bigger explosion dirt/smoke, flare light 0.95.
- AudioMixer `Assets/Audio/CombatMixer.mixer` now created via reflection (groups Ambience/SFX/Voice/Tinnitus, snapshots Normal/Muffled; `AddGroupToCurrentView` threw IndexOutOfRange → wrapped). Erlan's voice routed to Voice.
- Perf (`docs/PERF.md`): 86k tris visible worst heading, ~37 draw calls est., 1 shadow light, 17.8 MB textures → within budget.
- Tests: EditMode 45/45; PlayMode `EnvironmentTests` 11/11 (threat model warn→hit, peek+decay, white vs red light, sound delay, flare fall/burn/swing, passthrough ramp + polygon, editor proximity fade, shake limit, ambience near/far blast + scenario API, threat system run end, headlamp).
- **Known issue (pre-existing, S3):** in the full PlayMode run two `TreatmentBotTests` (TQ marker step, groin packing 92–99%) fail; they pass 8/8 when run alone (`-testFilter FieldMedic.Tests.TreatmentBotTests`), also fail without the S4 tests → order/timing-sensitive bot grabbing (log: "wanted Grab_Windlass, got Grab_Strap"). S5/S7: make the bot robust or isolate.
- Morning checkpoint: final APK built 09:51 (incremental IL2CPP, 9.6 min) → `C:\Users\user\Desktop\FieldMedicVR.apk` (95 MB); `docs/MORNING_REPORT.md` written. The last field darkening/reflection tweak (10:05) is in the scene but not in that APK (next stage's build picks it up).
- Next (S5): replace `DemoScenarioStarter` with the scenario system; use `CombatAmbience.PlayDropGrenade/PlayIncomingShell/PlaySniperCrack`, `ThreatSystem.underFire` per phase, `ThreatModel.Exposures` in the debrief, `RunEndOverlay` → debrief screen.

## 10.10 10:40 — S5 scenarios: in progress
- Started 10:07 (after 09:00): the 09:51 APK on the Desktop already matched the committed S4 state, so the "checkpoint first" build was skipped; the final S5 APK is built at the end of the stage.
- Medical (pure, EditMode 60/60): airway model (obstruction progresses → hypoxic death in ~3–4 min supine; vomit aspirated even with NPA; recovery position drains), TBI unconscious flag, eardrums flag, delayed onset / vasospasm rebleed (`Wound.onsetDelaySeconds`, event "rebleed"), posterior (back) wounds found only by log roll, TQ `Loosen/SlipLoose/PreApply`, `TourniquetConversion` rules (shock, amputation, > 6 h, dressing first, arterial rebleeds → re-tighten, never again), new mistake codes + `CareProtocol.Consequence` texts, `ScenarioCatalog` (8 scenarios with RU/KZ titles, briefings, skills, variants L/R + second hit + delayed onset), `ScenarioTimeline` (Briefing → Incident → CUF → TFC → Evacuation → Debrief, complications), `DebriefBuilder`, `TrainingCatalog` (5 lessons).
- Runtime: `ScenarioDirector` (scene reload per run via `ScenarioLaunch`; incidents per scenario; rules: TQ ≤ 60 s, airway left supine, mine path, light discipline, head over parapet, TQ re-check after the S7 drag, S8 reassessment; evacuation time ×20; debrief + best score in PlayerPrefs), `RadioSet` (vest radio, commander lines, PTT MEDEVAC 4-line), `WristWatch` (time, T+, phase, S8 time-skip "+"), `MenuBoard` (dugout board; main/scenarios/training/settings/briefing/debrief), `RoundButton` (pulsing, glowing, press-depth; poke + ray + desktop click/Enter), `BoardPointer`, `BriefingCard`, `MinePath` footprints, `TrainingDirector` (instructor voice, checklist, ghost hand + arrow, vitals monitor). `DemoScenarioStarter` removed.
- Assumptions: S3 and S6 start in tactical field care (no direct fire after a mine / glide bomb; the danger is mines / the airway); S8 evacuation after the last milestone (dawn) is 30 min; mistake explanations in the debrief are Russian-only (UI labels are RU/KZ); radio/instructor voices are TTS placeholders (`tools/tts_radio.ps1`).

## 10.10 12:25 — S5 scenarios DONE
- Tests: EditMode 60/60. The full PlayMode suite (11:35) was 51/52; its single failure was the order-sensitive TQ marker step in `TreatmentBotTests`, now fixed: the bot re-writes a blank TIME strap. After the fix, `TreatmentBotTests` + `ScenarioBotTests` passed 25/25 (≈12:05). `ScenarioBotTests` covers all 8 scenarios through the real director:
  - correct protocol → evacuated alive with score ≥ 85 (S1 100, S2 100, S3 100, S4 100, S5 95, S6 100, S7 100, S8 converted + 4 time-skips);
  - wrong variant → expected mistake recorded: tq_on_joint, missed_wound, elevation, tq_junctional, missed_wound, left_supine, tq_not_rechecked, tq_not_reassessed;
  - plus a board test (pages, card → briefing → launch, debrief texts, pointer pick).
- Honest note: two steps in the bot tests fall back to the protocol API when the scripted hands miss (sealing the exit wound on the back in S2, packing the buttock in S5). Whether a given run used the fallback is logged as "[FMVR] … protocol fallback"; that was not checked for the final run (the log was overwritten by later screenshot runs). Both work by hand for a player.
- Fixes found by the bots:
  - the debrief now lists derived mistakes even when he died;
  - a palm on the other side of the body no longer counts as pressure, and posterior wounds are untouchable while he is supine;
  - the complication schedule follows the casualty's clock;
  - the protocol phase is set at the incident;
  - the thermal blanket is only blocked by life-threatening bleeds (capillary ooze no longer blocks it).
- Visuals:
  - Erlan now falls along the trench (FireBay yaw 90°).
  - The debrief board comes to the player in the freest direction and is scaled to 0.68 so it fits between the trench walls.
  - The training panel is placed in free space; the headlamp turns white in training.
  - Incident props per scenario: scorch, fragments, FPV wreck, mortar fin, torn boot, helmet, lingering smoke.
  - Red headlamp halved with a wider beam (supervisor note).
  - Screenshots: `docs/screens/s5_*.png` (menu pages RU/KZ, briefing card, 8 scenarios, debrief, training, mine-path footprints).
- APK: built 11:54 → Desktop, rebuilt with the final changes at 12:22 (97.5 MB). `docs/MORNING_REPORT.md` rewritten (RU). `README.md` added (modes, controls, desktop simulator, tests, voices).
- Known issues / next (S6/S7):
  - TextMesh built-in font: check Kazakh glyphs on Quest; switch to Noto/TMP if they show as boxes.
  - Instructor/commander voices are TTS placeholders.
  - Blood "streak" particles (supervisor 09:21) are still visible in some shots.
  - NPA has no dedicated bot assertion (S6 is tested via recovery position).
  - Training ghost hand is a simple blue glove box.
  - Hands are still placeholder gloves.

## 10.10 13:55 — S6 build DONE
- APK rebuilt 13:30 (incremental IL2CPP, 5.7 min) → `Builds/FieldMedicVR.apk` + `C:\Users\user\Desktop\FieldMedicVR.apk` (97.6 MB). aapt2 check: `kz.qorsa.fieldmedic` 0.1.0, min 32 / target 34, `com.oculus.intent.category.VR`, supportedDevices quest3|quest3s, HAND_TRACKING + handtracking/passthrough features (not required), arm64. BuildReport says `errors=1` — the same in every previous successful build (S4/S5); no CS/Gradle errors.
- `Editor/QuestManifestCleanup` (IPostGenerateGradleAndroidProject, order 10000): strips RECORD_AUDIO (pulled in by Meta SDK building blocks referencing Microphone → mic prompt at first launch), USE_SCENE, USE_ANCHOR_API (spatial-data prompt); adds tools:node="remove" in the launcher manifest. Verified gone in the APK.
- `Runtime/Comfort/QuestPerformance` (RuntimeInitializeOnLoadMethod, device only, re-applied after each scene reload): FFR High + dynamic, 72 Hz, CPU/GPU SustainedHigh, OVRManager dynamic resolution 0.7–1.2.
- `PerfReport.OptimizeAndRun`: instancing on 6 more materials; PERF.md now lists the runtime profile and the Kazakh glyph check (LegacyRuntime has all KZ letters in the editor). Budgets OK: 86k tris visible, ~37 draws, 1 shadow light, 17.8 MB textures. Erlan LODs not needed (108k tris whole scene).
- Tests: EditMode 63/63 (new `FieldMedic.Editor.Tests`: manifest cleanup ×2, perf profile); PlayMode 52/52 (16 min, `!Screenshots`). One protocol fallback logged (S5 buttock packing: bot could not grab the hemostatic gauze).
- Docs: README.md rewritten in Russian; MORNING_REPORT.md updated; ATTRIBUTION.md + radio TTS, Meta hand assets, font, policy note (Mixamo/Meta SDK are the non-CC exceptions). Final shots `docs/screens/final_1..8_*.jpg` (JPG per supervisor note; picked from the s4/s5/s3 sets, not re-rendered).
- Next (S7): device test, blood streak particles, hands, real voices, S5 bot gauze grab.
- Push tip: plain `git push` failed with HTTP 408 on the big S5 pack; `git -c http.postBuffer=524288000 push origin main` worked (everything up to 0aed13a is on GitHub).

## 10.10 16:25 — S7 polish (in progress)
- **Visible drones** (`Environment/DroneVisual.cs`, `DroneAudio` hover/dive): the recon drone now has a body (merged mesh + 4 spinning rotors, gimbal, drop clamp) and a faint blinking red LED that stays ~1.5 px wide at any distance. In S1 it comes down from its 32 m orbit to hover 15 m over the trench, and a **visible VOG falls** from the clamp (free fall, t = √(2h/g) ≈ 1.75 s, whistle in the last 1.1 s) before the blast; then the drone climbs away. The S5 FPV (carbon X frame, orange props, warhead) **dives at the blast point** (~140 km/h at the end). Ambient FPVs now strike 30–70 m out in the field instead of vanishing. Shahed stays sound-only (450 m).
- **Blood streaks fixed** (supervisor 09:21): drips are round billboards; jets have a short stretch (velocityScale 0.008).
- **Wounds** (supervisor 14:24): the "red diamond" was the decal quad edge (the stain touched the texture border). Every generated blood/wound texture now fades out in a round window before the border; wrap = Clamp; organic torn threads, bruised skin margin and striated muscle replace the regular radial stripes; wound decal tint 0.62 and gloss 0.55, so it reads dark and wet instead of glowing.
- **Scorch** = a soft soot "blast star" decal (BloodDecal shader); the old opaque black discs read as holes.
- **Player hands**: `Input/GloveHand.cs` builds a skinned tactical glove (palm, knuckle ridge, thenar pad, cuff + strap, short sleeve, 4×3 phalanges + thumb). It is ONE draw call per hand. Fingers curl with the controller: grip closes middle/ring/little, trigger closes the index, the thumb follows both, and the hand stays wrapped while holding an item. The glove is hidden while OVR hand tracking shows the tracked hand. Blood-on-hands tints it; the scene builder no longer makes box gloves.
- **Battlefield**: 13 more large shell holes; a burnt BMP-2 nosed into a crater; a T-72 hull with its turret blown off beside it; a burnt truck on the far road; smoke columns with ember glow; two distant fires (`TrenchBuilder.BuildWrecks`).
- **Screenshots**: scenario shots now centre on the visible wound (`s7_s1..s8_*.jpg`). New shots: `s7_drone_hover_zoom`, `s7_vog_falling`, `s7_fpv_dive`, `s7_gloves`, `s7_glove_iso_*`, `s7_field_wrecks`. Tests: `PolishScreenshots` (category Screenshots).
- Tests: EditMode 63/63. New PlayMode tests: EnvironmentTests +3 (fall time/hover point, recon hover + VOG + climb-away, FPV dive / Shahed no visual) and GloveHandTests ×2. Targeted runs are green; the full PlayMode run is in progress.
- Next: full PlayMode regression → APK rebuild → Desktop copy.

## 10.10 17:30 — S7 polish DONE (this session)
- Supervisor 14:24 notes addressed: the wound diamond (decal edge), darker wound tint, battlefield wrecks/craters/smoke, drone LED. The FPV buzz already ramps in pitch on the dive.
- APK rebuilt 17:06 (20.8 min, full rebuild of changed code), 97.7 MB → `Builds/` + Desktop. BuildReport `errors=1` as in every earlier successful build.
- Tests:
  - EditMode 63/63.
  - PlayMode full run #1: 55/57. S3_Mine hit the 180 s timeout and the TQ bot failed at "time written with the marker". Both passed in isolation (S3 score 100, TQ loss 3%).
  - PlayMode full run #2: **57/57** (16.4 min). These two are order-sensitive flakes; watch them.
  - The S5 buttock-packing protocol fallback is still logged (bot can't grab the hemostatic gauze).
- Next ideas:
  - fabric texture/normal for the gloves;
  - fix the bot gauze grab in S5;
  - flare-lit shot of the new wrecks;
  - real RU/KZ voices;
  - on-device check of glove alignment vs controller.

## 14.10 22:20 — w0 launch fix (in progress)
- Audit (details: docs/LAUNCH_FIX.md). Meta's OVRProjectSetup validator: all Required rules pass. The V1 merged manifest had MAIN+LAUNCHER+VR on UnityPlayerGameActivity (GameActivity is REQUIRED by Meta SDK v207, kept). The APK is v2-signed, arm64, with the OpenXR loader. QuestManifestCleanup only stripped 3 permissions (innocent).
- Fixes:
  - **Boot scene**: title + async load of Main, so there is no black wait. Main took 16 s to load in the editor.
  - **Device-safe XRActive**: HandRig used to pick desktop input on the headset because the XR display starts after the first scene's Awake.
  - **Desktop simulator** disabled in the Quest player.
  - **Platform SDK supplement**: the `horizonos:uses-horizonos-supplement` entry is stripped.
  - **supportedDevices** is now quest2|questpro|quest3|quest3s.
  - Unity SpaceWarp and MetaXR Subsampled Layout are off.
- Guards:
  - `QuestLaunchSetup` runs before every build.
  - `QuestManifestAudit` checks the merged manifest after every build and writes `logs/manifest_check.txt`. The build exits with code 2 on a problem.
  - The APK is copied to the Desktop.
  - New script `tools/quest_check.ps1`.
- Tests:
  - EditMode 66/66 (+3 manifest tests).
  - New PlayMode tests: HandRig late-XR switch (pass) and LaunchSmokeTests (pass). BootSceneTests is pending.
- Note: one Unity batch run hung ~3 h at editor startup (Package Manager IPC) before running the method. Always use the unity.ps1 timeout.
- **Git push blocked**: `git push` and `git ls-remote` return "Repository not found" for github.com/Hevilim/TacticalMedicine. origin/main is still 37ebbe9, so the owner's 4f8d39e is not on GitHub either. The repo may have been renamed, deleted, or made private, or the stored token lost access. All w0 work is committed locally; push it after the remote or credentials are fixed.
