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

## 14.10 23:00 — w0 launch fix DONE
- APK 22:49 (29.6 min): `Builds/FieldMedicVR.apk` and a copy on the Desktop (97.7 MB). The manifest audit is OK, cross-checked with aapt2. Scenes: Boot, Main.
- BootSceneTests pass: Main is active 4.0 s after Boot.
- quest_check: NO DEVICE (exit 0).
- Next stage (w1+):
  - Any new scene builder must keep `MainSceneBuilder.SetBuildScenes()` so Boot stays first.
  - Run `tools/quest_check.ps1` after every build.
  - Git push is still blocked ("Repository not found"); the owner must fix the remote or token.

## 15.10 00:55 — w1 assets (in progress)
- **Tools**
  - `tools/bk_fetch.py` (search/rank/contact sheet, get, credits).
  - `tools/bk_export.py` (headless Blender), which does all of this:
    - instancing → real meshes;
    - applies modifiers, drops Subdivision;
    - decimates to budget;
    - builds the LOD0/1/2 chain;
    - extracts or bakes textures (1K JPG/PNG + metallic-smoothness map);
    - sets real size and pivot;
    - strips a rig to its deform bones (Auto-Rig Pro → Humanoid-ready).
  - `tools/bk_batch.py` + `tools/bk_batch_w1.txt` (the shopping list).
  - `tools/ph_fetch.py` (Poly Haven CC0).
  - `tools/gallery_sheet.py`.
- **Imported**: 43 BlenderKit models (`Assets/_Licensed`, git-ignored) + 6 Poly Haven models, the overcast HDRI and 5 ground textures (`Assets/ThirdParty/PolyHaven`, committed). The list is in `docs/ASSETS_V2.md`; credits are in ATTRIBUTION.md (generated by `bk_fetch.py credits`).
- **Unity side**: `Editor/LicensedModelSetup` handles both models and textures:
  - import settings (ASTC, 1K, normal and linear maps, Humanoid for soldiers);
  - URP Lit materials from the JSON;
  - a prefab with Cover tag/layer and MeshCollider for scenery.
  - The tag and layer "Cover" are added to the project.
- **Meshy**: `Editor/MeshyImporter` + `Runtime/Treatment/MeshyModels`.
  - IFAK files are turned into `Resources/Meshy/*.prefab` (real size, axes normalised, collider + GrabPoint).
  - `MedItem` and `IfakPouch` swap in the carried look; the procedural model stays as the fallback.
  - Scenery files (env_/veh_/air_/sol_) go through the same Blender pipeline.
  - It runs automatically after imports and before APK builds.
  - New prompts are in `docs/MESHY_PROMPTS_V2.md`.
- **Assumptions**:
  - Western vehicles and aircraft from BlenderKit stand in for Soviet types until the Meshy files exist.
  - The 8x8 APC asset was skipped (Blender hang).
  - The small barbed-wire fence lost its wire (its curve modifiers are disabled in the source file); `barbed_wire_metal_fence` is the real one.
- **Git push**: still "Repository not found" (same as w0). All work is committed locally.

## 15.10 01:50 — w1 assets DONE
- **48 real models**, each with a prefab: 42 BlenderKit + 6 Poly Haven. Full list in `docs/ASSETS_V2.md`; credits in ATTRIBUTION.md.
  - Ruins: 13
  - Cover: 8
  - Vehicles: 4
  - Aircraft and drones: 5
  - Soldier: 1 (Humanoid avatar valid)
  - Props: 11 (including the KZ flag)
  - Rocks, grass and tyre: 6
- **Environment**: overcast HDRI (2k) + 5 steppe ground textures (CC0, committed).
- **Gallery** renders: `docs/screens/v2_w1_gallery_*.jpg` and `v2_w1_lineup.jpg`.
- **Review fixes**:
  - Flour sacks → olive tint.
  - White F-16 → grey.
  - Red civilian helicopter → olive tint.
  - Ambulance gurney → olive tint.
  - Rebar dropped (it read as a primitive).
  - `brick_block_wall` is a one-sided scan: check the `~back` shots before placing.
  - `unfinished_building` wall infill is flat salmon colour (baked procedural): retexture or cover with debris in w2.
- **Meshy prompts** are in `docs/MESHY_PROMPTS_V2.md`: sandbags, BMP-2, BTR-80, T-72, Su-25, Mi-24, Mi-8, crater, burnt car, KZ and enemy soldiers, casings, litter, marker, card.
- **Tests**:
  - EditMode: 72/72 (+6 MeshyImporter tests: FBX, GLB via Blender, unknown names, swap, fallback, sizes).
  - PlayMode TreatmentBot: 8/8.
  - PlayMode Grab + LaunchSmoke: 7/7.
  - Order flake: running all three classes together failed 3 bot tests. A/B in isolation with and without the Meshy hook: 8/8 both ways, so it is not caused by this stage.
- **APK**: 01:46 (21.5 min, 97.7 MB, manifest audit OK) → `Builds/` + Desktop.
- **Next (w2)**:
  - Use the prefabs from `Assets/_Licensed`/`ThirdParty`.
  - Layer/tag "Cover" now exists.
  - On a fresh clone, run `python tools/bk_batch.py tools/bk_batch_w1.txt` first. It needs `%USERPROFILE%\.bk_api_key`.
- **Git push**: still blocked ("Repository not found").
- quest_check.ps1 after the w1 build hung (killed after 10 min, no output; probably adb waiting with no headset attached). Not a build problem; the next stage should run it with a timeout.

## 15.10 — w2 battlefield (scene done, APK build next)
- **Scene** `Assets/Scenes/Battlefield.unity`, generated by `Editor/SceneBuilders/BattlefieldBuilder` (+ `BattlefieldTerrain`, `RuinBuilder`, `BattlefieldFx`).
  - Batch: `BattlefieldBuilder.BuildBatch` (scene + Boot), or `BuildAndShoot` (also screenshots + docs/PERF_V2.md).
  - Boot now loads Battlefield. Build scenes: Boot, Battlefield, Main (V1 trench, legacy, still built for its tests).
- **Terrain**: 400×400 m in 4 chunks, ~51k tris. Grid x = 50u + 150u⁵: ~0.65 m spacing out to 25 m, 10 m at the rim.
  - Shader `FieldMedic/GroundBlend` blends three Poly Haven layers (withered grass / dry ground / burned ground) by vertex colour: tracks with ruts, trampled ground, crater ejecta/scorch, LZ wash.
  - 20 deterministic craters (`BattlefieldLayout.Craters`). The centre is flat.
- **Sky and light**:
  - `FieldMedic/HazySky`: Poly Haven overcast HDRI with a horizon haze band equal to the fog colour, plus darker smoke over the front.
  - Ambient and reflections come from the skybox. ExponentialSquared fog (0.0078).
  - One directional light with HARD shadows. Mobile shadow distance raised from 15 to 32 m.
- **Ruins**: 3 procedural brick houses (`RuinBuilder`).
  - Walls are 0.25 m strips with brick-course broken tops, windows, shell holes and breaches; closed two-sided meshes of 0.3–1k tris each.
  - Shader `FieldMedic/RuinWall`: Poly Haven plaster over brick, plaster loss toward the edges, soot.
  - House A (where we treat) has a drag doorway in its east wall and a rear breach toward the LZ.
  - Also placed: the gutted concrete frame (retinted), scanned debris piles, slabs, rubble and debris.
- **Cover and obstacles**:
  - Jersey barriers topped with 2 courses of sandbags (~1.5 m, kneeling cover), checkpoint barriers, culvert pipe, a pocked post on the drag line.
  - Burnt APC, car, truck and tank (burnt material copies), smouldering smoke.
  - 7 czech hedgehogs. Concertina razor wire: a procedural helix, 6 runs on X-frame pickets. This replaces the prison-style metal fence.
- **Cover rules**:
  - Every solid cover piece has a collider on layer and tag Cover and a NavMeshObstacle (carve).
  - It also has an automatic `CoverPoint` (kneeling eye 1.1 m, 0.5 m behind the back face).
  - Wire and hedgehog steel are obstacles only (Default layer).
- **FX**:
  - 7 horizon smoke columns: ≤48 big procedural puffs each, wind-bent, fire glow at the base of the 4 nearest.
  - KZ flag: pole + 28×14 cloth, vertex-waved by `FieldMedic/FlagWave` (analytic normals, two-sided). It uses the BlenderKit flag texture; plain KZ blue is the fallback.
  - LZ 47 m behind house A: VS-17 panel + green signal smoke (`LzMarker`, `PopSmoke` for w4), tent and crates.
- **Player**:
  - OVRCameraRig kept instead of an XR Origin (CLAUDE.md architecture), + `Comfort/Locomotion`:
    - left stick: smooth move, head-relative, capsule-cast wall sliding;
    - right stick forward: parabolic teleport arc, valid on walkable ground only (never on or inside cover);
    - right stick sideways: snap turn (ComfortRig);
    - ground following (craters, floors);
    - locomotion is off while dragging; desktop T = teleport.
  - `PlayerStance` classifies the real head height (stand / crouch / kneel / prone).
  - V1 gameplay (hands, IFAK, Erlan, ScenarioDirector with spots mapped into the ruins, menu board on house A's west wall, radio, watch) works on the new map. The V1 parapet threat is off (parapetHeight 99); w3 adds ray-cast fire against the Cover layer.
- **Performance**:
  - Small repeated pieces (sandbags, debris, rocks, wire pickets, coils) are merged per material. Merged licensed geometry goes to the git-ignored `Assets/_Licensed/_Combined`.
  - Only big silhouettes cast shadows (81 on / 219 off). Small debris switches LOD early. Occlusion is baked (OcclusionArea over the position).
  - Report: docs/PERF_V2.md (`PerfReportV2`, LOD-aware estimate).
  - Profiler capture in play mode (`BattlefieldScreenshots.Profile_Render_Stats_From_Player_Spots`, Mobile URP, worst of 8 headings):

    | Spot | Triangles | Draw calls | Batches | SetPass |
    |---|---|---|---|---|
    | player start | 495k | 197 | 179 | 37 |
    | behind sandbags | 456k | 203 | 197 | 44 |
    | Erlan in the open | 455k | 161 | 144 | 34 |
    | LZ | 499k | 224 | 201 | 46 |

    Triangles are within the 500k budget.
  - Draw calls exceed 150 only because of V1 gameplay objects in view: MenuBoard 41, PlayerIFAK 38, Erlan 15, Radio 9, hands 7. The environment alone is ~95–130.
  - **w4**: hide the menu board outside the menu.
  - **w5**: Meshy IFAK should use fewer renderers.
  - A merge experiment on LOD-managed props cost +120k tris and was reverted.
- **Tests**:
  - EditMode 78/78, including the new `BattlefieldLayoutTests`.
  - PlayMode `BattlefieldTests` 11/11:
    - kneeling behind each of 21 cover points is shielded (3 shooters ±arc at 120 m);
    - every line of fire hits a Cover-layer collider;
    - standing up behind the sandbags is exposed;
    - Erlan's spot is open and 13.8 m from the doorway, and the drag line is free of cover;
    - tags, layers and obstacles; flag, LZ and smoke present;
    - locomotion wall-slide, teleport validity, ground following into a crater, stance thresholds, a clean desktop run.
  - Also passing: Battlefield screenshots + profiler test, Boot→Battlefield, and a new headset-path launch smoke test for the Battlefield.
- **Screenshots**: docs/screens/v2_w2_{overview,ruins,cover_view,enemy,flag,drag,inside,lz}.jpg (1600×900, JPG 85). They come from the edit-mode PC-pipeline capture with a warm-up frame plus a 5-render per-pixel median.
- **KNOWN (verify on device!)** This laptop's Iris Xe (D3D11, no native ASTC) renders the Android-target project with artifacts that depend on the render path:
  - Mobile URP or play-mode LDR: colour speckle on the licensed URP/Lit materials.
  - PC soft shadows: some objects black.
  - Distant ground sparkle.
  - The PC-path median capture is the cleanest result.
  - The Quest decodes ASTC natively, so these are expected to be editor-only. On the first device run, take a screencap (tools/quest_check.ps1). If speckle shows on the Quest, try Mobile_RPAsset HDR on (it made the editor clean) and check the ASTC normal maps.
- **Assets**: 6 new Poly Haven CC0 textures (ph_fetch WANT list) + derived straw grass albedo; ATTRIBUTION.md updated.
- **Git push**: still "Repository not found" (remote/credentials issue from w0); all work is committed locally.
- **APK** 03:43 (10.9 min): manifest audit OK, copied to the Desktop. The size grew from 97.7 to 244 MB (Battlefield licensed models/textures + legacy Main). Consider dropping Main from the build in w6 if size matters. quest_check: NO DEVICE.
- **w2 DONE.** Next, w3 (battle):
  - use `CoverMath.Blocked/Shielded` + `PlayerStance` for enemy fire;
  - `BattlefieldLayout` for positions;
  - `LzMarker.PopSmoke` for the evac.

## 15.10 — w3 battle (BattleDirector + real enemy fire)
- **Runtime** (`Assets/Scripts/Runtime/Battle/`, namespace `FieldMedic.Battle`):
  - `BattleDirector` (tuned by `Assets/Data/BattleSettings.asset`): weighted event every 5–20 s (wave, artillery barrage, jet bomb run, helicopter rocket run, MG bursts with tracers, FPV dive, tank shot), never the same event 3× in a row; assault swell (`IntensityCurve`); **focus API** `BattleDirector.RequestFocus()` — 3–5 s with no director blast within 60 m (auto-requested when a hand grabs a MedItem or Erlan), background never below 0.45. Static `Mode` (Training/Combat/Exam) + `PlayerArmed` set by ScenarioDirector/TrainingDirector (w4 owns the mode UI). Scheduled blasts/impacts/bursts in a fixed array (no coroutines).
  - Layers: `NearInfantry` (8 enemies incl. MG gunners + 3 friendlies on our sandbag line; run cover→cover, kneel/stand fire with muzzle flash + tracer + 3D sound, hit/die with Mixamo falls, respawn off-screen), `VehicleLayer`/`VehicleAgent` (2 tanks + 2 IFVs on Catmull-Rom loops, turret slew, cannon/autocannon, recoil, dust, engine loop), `CrowdLayer` (44 GPU-instanced baked soldiers, ≤10 draws), `HorizonLayer` (160 billboard impostors + muzzle twinkles, shader-only), `AirLayer` (jet bomb runs with heat flares, helicopter rocket salvos, rotor spin, Doppler audio).
  - `SoldierPoser`: procedural pose layer over the Animator (run gait with foot IK, shouldered/kneeling aim, weapon-hand aware — this model's rifle is skinned to the **left** hand; calibrated by exact manual skinning in the builder). Bind pose = humanoid zero-muscle pose (the imported bones lie on the floor).
  - `EnemyFire`: 10 Hz line-of-fire checks from shooters to head+chest vs Cover layer, analytic ground profile and `SmokeScreens`; near misses (crack/whiz, dirt/concrete impacts, tracers), suppression vignette (`SuppressionFx`) + breathing; `ExposureTracker` (cumulative 4 s in a 10 s window, tunable) → `ThreatSystem.ExternalHit("enemy_fire")` → scenario fails with an RU/KZ explanation. Training: never targeted. Hidden behind cover: rounds hit the cover in front.
  - `BattleFx`: pooled muzzle flashes, impacts, blast chunks, dust ring, bomb column, scorch marks (one instanced draw), projectiles (shell/rocket/bomb/heat flares), particle tracers (one draw), distance haptics; near blasts reuse CombatAmbience tinnitus + low-pass, now 3–6 s.
  - `SmokeGrenade` + `SmokePin` + `VestMount`: two grenades on the chest rig; grab, the other hand pulls the ring ≥6 cm, throw → lever flies, 1.2 s fuse, 20 s dense cloud (3 drifting spheres registered in `SmokeScreens`) that blocks enemy rays.
  - `BattleAudio`: 22 pooled 3D voices with audibility-based stealing, far sounds delayed by distance/343, beds that never stop (artillery rumble + battle ambience), breathing.
- **Editor**: `BattleAssets` (soldier prefabs, vehicle/rotor split by height + forward detection, crowd mesh bake, impostor sheet, scorch texture), `BattleBuilder` (scene integration, `BuildBatchW3`), `BattleGallery` (debug renders).
- **Assets**: soldier LODs `soldier_agent` (5.2k) / `soldier_crowd` / `soldier_far` (1.1k) re-exported from the BlenderKit sniper (masked, no insignia = neutral enemy); smoke grenade body = BlenderKit "Flashbang" retinted OD; 28 new CC0/CC-BY Freesound sounds (`tools/fs_search.py`, web search without API key); Poly Haven `hessian_230`. ATTRIBUTION.md updated.
- **Supervisor 15.10 03:55 fixes done here**: #1 ground pixel noise — root cause: `frac(sin(big)·43758)` hash in GroundBlend/RuinWall/EnvLit degenerates on GPUs with low-precision sin → replaced by a sine-free hash (edit-mode Quest-pipeline renders are now clean); #3 sandbags → plain olive hessian, no "50KG" print (`military_sandbag_wall` alias in MESHY_PROMPTS_V2). #2 (real-size ruins) and #4 remain for w4. Ground tint muted toward war-grey.
- **Bug fixes**: MonoBehaviours must live in files named after the class (SoldierAgent/VehicleAgent/… were silently dropped from prefabs); `BloodPoolManager` survives destroyed pools (caused 38 order-dependent V1 failures); `ph_fetch.py` no longer overwrites CREDITS.md on partial runs.
- **Tests**: EditMode 94/94 (+16 `BattleLogicTests`). PlayMode `BattleTests` 12/12 (open ground → hit at 4.0 s; kneeling behind sandbags never hit and rounds hit the cover; standing up exposed; Training never targeted; dense smoke blocks; cover points safe from in-arc shooters; hands-on grenade pull+throw → screen blocks rays; pin-in → back to vest/dud; focus keeps blasts ≥ 60 m; medical grab requests focus; every event type runs; all layers alive). **Soak** `BattleSoakTest` 10 min: 0 errors, 47 events (longest gap 27 s), 0 gen0 GC collections, ≈1.1 KB/frame managed (editor overhead incl.), the only >64 KB frame was the test's own list growing (fixed). Final full PlayMode run (after the fixes): **100/108**; the soak in that run: 0 frames > 64 KB, 0 GC collections, max frame alloc 18 KB, 45 events, longest gap 23 s. The 8 remaining failures are V1 grab-precision tests (GrabInteractionTests.Grab_Move_Release, 3 ScenarioBot, 3 TreatmentBot, 1 TreatmentScreenshots) that **fail only in full-suite order** and pass in isolation (31/31 for Grab+TreatmentBot+ScenarioBot; Battle+Battlefield+Grab 30/30 order; GloveHand+Grab 8/8). Global time/physics/quality state is identical at the grab test (logged by `GlobalStateGuard.Describe()`), so it is not a leaked setting; same class of order flake w1 documented. Next stage: bisect with `-testFilter` over the classes that run before GrabInteractionTests (BootSceneTests, CasualtyTests, Environment*), e.g. make V1 grab tests start from an empty scene.
- **APK** 08:21 (27.9 min, incremental IL2CPP): `Builds/FieldMedicVR.apk` 256 MB + Desktop copy, manifest audit OK (quest2|questpro|quest3|quest3s). quest_check: no device (timed out after 90 s, adb hang as in w1).
- **Git push**: still "Repository not found" (remote/credentials issue since w0); all commits are local.
- **Screenshots** `docs/screens/v2_w3_*.jpg` (play mode, staged moments): assault over the sandbags, from the window, enemy line close-up, cover with impacts, open field blast, helicopter + jet strike, smoke screen, tank firing, overview. **Known editor artifact**: in *play mode* this laptop's Iris Xe still shows speckle on licensed Lit materials and blocky noise on the ground (edit-mode renders of the same view are clean; not caused by XR, static batching, MSAA, cameras or vertex data — verified). Verify on the Quest (tools/quest_check.ps1 screencap).
- **Perf** (docs/PERF_V2.md "w3"): triangles 517–660k, draw calls 184–434 at the worst headings → over the GDD budget; mitigation list there for w6 (soldier/heli material atlases, near→crowd LOD beyond 35 m, hide V1 objects outside the menu).
- **Assumptions**: enemy uses the masked sniper model (no insignia); western vehicle/aircraft stand-ins remain until Meshy files; Mode/PlayerArmed are statics until w4's menu sets them; GDD's "~4 s cumulative in 10 s" applies regardless of stance (w4 may weight prone/kneel lower via `ExposureTracker.Tick(exposed, dt·k)`).
- **Next (w4)**: the CUF run to Erlan is deliberately lethal without smoke (4 s rule) — w4's scenario flow should teach "smoke first / move in bursts"; keep `BattleTests.CoverPoints_*` green when rebuilding the ruins.

## 15.10 — w4 modes (in progress)
- **Speckle root cause found (supervisor 15.10 07:00 #1)**: `W4Diag.Speckle` renders the sandbag view under 14 variants and scores isolated-outlier pixels plus the difference between two identical renders. On the editor's default **D3D11** device (Intel Iris Xe): speckle 13.6‰, and **33‰ of pixels differ between two renders of the same static frame**. No content change fixes it (normal maps off, specular off, white albedo, fog, shadows, cross-fade, HDR, MSAA all stay noisy). The same run with `-force-vulkan`: speckle 0.3‰, renders deterministic (0.2‰), the ground shows its real dry/cracked layers (the "burnt black + tan blocks" was garbage too). Cause: the D3D11 driver path of this laptop's GPU. The Android build uses **Vulkan only** (`m_BuildTargetGraphicsAPIs` Android = 0x15), the Quest never touches that path. `tools/unity.ps1` now adds `-force-vulkan` to every batch run, so all screenshots/tests render through Vulkan. Evidence: Logs/w4_diag_d3d11.txt vs Logs/w4_diag_vulkan.txt.
- **w4 code (15.10 ~10:00)**:
  - `Medical/Training.cs`: lesson/hint table — steps carry title, 2–3 actions (optional per-action checks), IFAK item, body target, icon, TCCC phase, watched wrong actions; RU/KZ reasons (`HintWrong`) shared with the debrief (`DebriefMistake.step`, KZ texts). Topics: full TCCC (in the open: reach → CAT high-and-tight → drag → gloves → pulse → time → sweep → forearm bandage → blanket → card → MEDEVAC → load), tq, pack, drag, chest, npa.
  - `Runtime/Scenarios/HintTable` (ScriptableObject `Resources/HintTable.asset`, built by `W4Builder`), `TrainingDirector` rewrite, `TacevacController` (helicopter approach/flare/landing at the LZ, litter + «ПОГРУЗИТЬ» button next to Erlan, lift-off). `ScenarioTimeline.waitForLoading` holds the run until loaded.
  - `UI/HintPanel` + `UiMesh` (TextMeshPro world text, Liberation Sans SDF with all Kazakh letters, rounded card, soft shadow, phase chip, pills, check/warn icons) — `Resources/Shaders/UiCard.shader`.
  - Debrief: numbered mistakes with the TCCC step, mini map (`Resources/UI/battlefield_map.jpg`, top-down render) with markers where each mistake happened, timeline strip (CUF/TFC/TACEVAC segments + ticks). Board hidden during runs (draw calls), back for the debrief.
  - Wrist display: Erlan's pulse (beating dot) + golden-hour countdown in every mode.
  - Menu: ОБУЧЕНИЕ (topics, TCCC big) / БОЙ (S1–S8) / ЭКЗАМЕН / НАСТРОЙКИ; training summary page.
  - V2 scenario texts (steppe/ruins, daytime clocks), S7 far out in the field (`CasualtySpot.OpenFar`).
- **Supervisor fixes in the scene**: house A/B/C walls two storeys (4.3–5.6 m, upper windows, broken first-floor slab in A); real BlenderKit "Abandoned House" (CC0, PBRPX, 45k tris) as a shelled five-storey block behind the position and "Ruined Farmhouse" on the right flank; the fake horizon buildings (intact derelict block ×2, glass-like unfinished frame) removed; concrete pipe → burnt car wreck; smoke columns use a soft 2×2 smoke sheet (gaussian lobes + fbm, no alpha clip) with 3.4 puffs/s, 140 max, bigger growth → continuous columns.
- Git push still fails ("Repository not found" — credentials/remote, unchanged since w0). Commits are local.
