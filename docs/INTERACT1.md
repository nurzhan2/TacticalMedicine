# INTERACT1 — living Erlan and hand feel (stage w8-interact1)

Principle: the patient and the items respond to how the player acts: where, how hard, how long. Nothing
completes on a button press or a single touch. Every check below is done with the hands (or the head, or the
voice) and is covered by a PlayMode test that uses scripted hands only (`ScriptedHandInput` via `MedicBot`).

## 1. Living Erlan

### Face, eyes, eyelids, lips
- **How it works.** The Mixamo "Swat" model has no exposed skin: a closed balaclava with NVG goggles.
  - `CasualtyFace` builds a face at runtime, in the head bone's bind space (pose independent).
  - It removes the goggles and the balaclava's face patch from a runtime copy of the mesh.
  - It sculpts a skin face into the opening. The base is the minimum of an analytic face and the measured mask surface, so Erlan keeps the mask's narrow V-chin. On top come the nose, brows, eye sockets, cheekbones and chin.
  - It adds lip overlays, a balaclava fabric rim, eyeballs with a procedural iris, and upper/lower eyelid caps with a lash line. The skin texture is painted procedurally: brows, stubble, eye bags, dust.
- **Eyes and eyelids by AVPU.**
  - A: open, blinking. V: drowsy (30 %). P: closed. U: almost closed. Dead: half open.
  - A stimulus opens them for a few seconds, and pain squeezes them.
  - The eyes turn toward the player's head (clamped to 32°) when he is conscious.
- **Head turn.** `CasualtyRig.LookAt`, with `CasualtyController.Attend()` after a stimulus.
- **Test.** `VitalsChecksTests.Avpu_ByVoiceLevel_And_ShoulderShake` checks the eyes open, `LookingAtPlayer` and `Attending`.

### Pulse check
- **How it works.**
  - `HandTreatment.Pulse`: the **index and middle fingertips** (`HandGrabber.IndexTipPosition` / `MiddleTipPosition`) must both be within 3 cm of the carotid / radial / pedal / femoral point.
  - A tracked fist does not palpate. With controllers any grip pose counts.
  - Haptic beats come at the physiology heart rate (`HapticsProfile.Heartbeat`), with amplitude from the blood pressure at that site.
  - After **≥ 2 s** it reports to `VitalsChecks.PulseFelt`. That reading goes to the protocol (debrief) and, in Training, to a small white label: «Пульс на шее: 122, слабый, частый».
  - The radial pulse is absent below SBP ~80; the carotid stays.
- **Test.** `VitalsChecksTests.Pulse_TwoFingers_Neck_And_Wrist_Beats_By_Pressure`:
  - 1 s on the neck does not count;
  - neck and wrist are recorded, and the rate matches his heart rate;
  - there are ≥ 3 haptic beats;
  - beats are stronger at a normal blood pressure;
  - the radial pulse is gone at 40 % loss.

### Breathing check
- **How it works.**
  - The player's head (HMD) within **25 cm** of his mouth (`CasualtyFace.MouthPosition`) for **≥ 3 s** records «Дыхание: 32/мин, частое, поверхностное».
  - The breath is procedural 3D audio (`BreathSynth`, `OnAudioFilterRead`). Its loudness is the airflow of the procedural chest (the derivative of `CasualtyRig.BreathSignal`), so the sound follows the physiology exactly. A snore buzz is added when the airway is blocked. It is quiet at a distance and clear at the mouth.
  - Depth comes from `PatientPhysiology.BreathDepth` (new):
    - early shock: deeper (air hunger);
    - open chest: shallower, and better once sealed;
    - tension: fast and shallow, recovering after decompression;
    - blocked airway: shallow, snoring.
- **Tests.**
  - `VitalsChecksTests.Breathing_ByHeadNearMouth_RateAndDepth_FollowPhysiology`: nothing at 1.2 m. Recorded after 3 s near the mouth. In tension, fast and shallow, with the chest counted at the physiology rate.
  - EditMode `VitalsAssessmentTests.Breathing_*`.

### Voice / AVPU
- **How it works.**
  - **Voice.** `PlayerVoice` measures microphone RMS, mapped −52…−16 dBFS → 0..1. Talking above 0.35 for 0.35 s, with the head within 1.7 m of his head, is the voice stimulus. There is no speech recognition. Android asks for RECORD_AUDIO once; if it is refused, the shake still works.
  - **Pain.** Grab his shoulder (log-roll handle) or a plate-carrier strap and shake it (3 reversals), or squeeze it with a full grip for 2 s.
  - **Answer.** `VitalsAssessment.Respond`:
    - A talks (VoiceBank lines, RU/KZ mix);
    - V mumbles to the voice and groans to pain;
    - P gives nothing to the voice, groans and withdraws to pain;
    - U gives nothing.
  - The result is recorded: «AVPU: реагирует на голос (V)».
- **Tests.** `VitalsChecksTests.Avpu_ByVoiceLevel_And_ShoulderShake` checks: quiet means no stimulus; loud gives V and opens the eyes; at P the voice gets nothing; the shake gives a groan and a flinch; when alert the eyes are on the medic.

### Pain reactions
- **How it works.** `CasualtyController.PainReaction(strength, playerHand, source)` is called by:
  - every CAT windlass half-turn,
  - every packing stroke,
  - the needle,
  - the NPA entering the nose,
  - the shoulder shake.

  The reaction:
  - a procedural flinch on the rig bones;
  - a voice line, or only a groan at P;
  - a grimace (eyes squeezed);
  - if conscious, **he grabs the player's wrist** with his free hand: a two-bone IK target on the player's wrist, about 1.8 s, with a 5 s cooldown.

  Unresponsive: nothing.
- **Tests.**
  - `PainReactionTests.Tourniquet_Tighten_Flinch_Voice_And_WristGrab_On_PlayerHand`: tq reactions, flinch, a voice line, and the IK target is the twisting hand (his hand gets within 4 cm of the medic's wrist).
  - `Unresponsive_NoReaction_Conscious_GrabCooldown`.

### Visible shock and capillary refill
- **Shock colours.**
  - **How it works.** Face skin, eyelids and nail-bed fingertips are tinted by `Pallor` / `Cyanosis` / `Sweat`: pale, then grey, lips blue, sweat sheen through smoothness. This uses MaterialPropertyBlocks at **≤ 10 Hz** (`CasualtyFace.tintRateHz`).
  - **Test.** `VitalsChecksTests.Shock_Colours_Pale_Grey_BlueLips_Sweat_AtMost10Hz`.
- **Capillary refill.**
  - **How it works.**
    - Fingerless-glove fingertips with nails are added on index, middle and ring of both hands.
    - Pressing a nail with a fingertip turns it white.
    - Held ≥ 1 s and released, the colour returns in `VitalsAssessment.CapillaryRefillSeconds`: about 1.4 s when healthy, more than 3 s in shock, longer in the cold.
    - The time the medic actually watched is recorded: «Капиллярное наполнение: 3,8 с — шок».
  - **Test.** `VitalsChecksTests.CapillaryRefill_NailPressed_ColourReturns_ByPerfusion`: healthy 1.4 s, shock 3.8 s.

## 2. Hand feel

### Grab poses per item
- **How it works.**
  - Each `HandPose` asset (`Assets/Data/HandPoses/HandPose_*.asset`, 24 of them) holds a curl per finger, thumb opposition, and the hand-tracking gesture.
  - They are collected in `Resources/HandPoseLibrary`.
  - Items covered: CAT, bandage, gauze, shears, marker, needle, NPA, seal, blanket, card, radio (press-to-talk), the drag handle, wrist, ankle, CAT strap, windlass, tabs, gloves, lube, shoulder, knee, smoke grenade and pin ring, and a poke pose near buttons.
  - `Grabbable.Pose` resolves the pose by item kind, handle type or name, once.
  - `GloveHand` takes the pose per finger while holding. A fist on top closes it further.
- **OVR hands.** With controllers the glove is the visible hand, so the poses apply. With hand tracking, Meta's real hand mesh shows the player's own fingers (a hard-coded pose would fight the tracking).
- **Tests.**
  - `HandPoseTests.Every_Item_And_Handle_Has_A_Pose_And_The_Glove_Takes_It`.
  - EditMode `W8AssetTests`: every MedItemKind and required key exists as an asset.

### Physical hands
- **How it works.** `HandPhysicalVisual` on the player's glove:
  - The visual palm sweeps (SphereCast) from where it was toward the tracked hand and stops and slides at solid colliders (walls, ground, solid props).
  - It stops at the first contact with Erlan's body capsules along the path.
  - The tracked hand may go further, and all logic uses the tracked pose, so pressure on a wound still works.
  - A faint ghost glove (the baked glove mesh, UiCard material) shows the tracked position when the two are more than **5 cm** apart.
  - It is disabled while holding something (the item follows the tracked hand).
- **Test.** `HandPoseTests.Hands_DoNotPenetrate_Walls_Or_Erlan_Ghost_Shows_Tracked`:
  - **Wall:** tracked 15 cm inside, the visual sphere 0 cm inside, ghost on.
  - **Chest:** tracked 12 cm in, the visual rests on the surface.

### Haptics by action
- **How it works.** One `HapticsProfile` (`Resources/HapticsProfile.asset`, tunable) defines:
  - **Strap pull:** continuous resistance ∝ tension.
  - **Windlass:** one click per half turn.
  - **Packing:** soft pulses at 7 Hz while the finger pushes gauze.
  - **Seal edge press:** one firm pulse.
  - **Pulse:** the heartbeat.
  - **Drag:** low-frequency rumble ∝ load.

  `IHandInput.Haptic(amp, dur, frequency)` is new, with a default method. `OVRHandInput` passes the frequency to `SetControllerVibration`.
- **Tests.**
  - EditMode `W8AssetTests.HapticsProfile_*`.
  - The pulse test counts `HapticsProfile.Played` heartbeat events.

### Hand-tracking mode
- **How it works.** With tracked hands (`HandSource.TrackedHand`):
  - an **index pinch** grabs pinch-style items (gauze, seal, NPA, needle, card, tabs, pin ring);
  - a **full grip** grabs anything, and is the only way to take handles and the strap (drag handle, CAT strap, wrist, ankle);
  - buttons are poked with the fingertip, as before.

  A pinched item stays while the pinch or a fist holds. Controllers and hands auto-switch per frame (`OVRHandInput`).
- **Tests.**
  - `HandTrackingDemoTests.Demo_FullFlow_WithPinchAndGrip_HandTracking`: the whole DEMO with pinch/grip only.
  - `Pinch_DoesNotTake_DragHandle_Fist_Does`.

### Weight
- **How it works.**
  - `CasualtyDrag` speed limits by grip: plate-carrier handle 1.5 m/s, shoulder strap ×0.8, ankle ×0.68, wrist ×0.58. Two hands add +35 %.
  - Heavy loads pull the visual hand down by up to 4 cm (`Grabbable.VisualLoad`; the drag handle with one hand = 1).
- **Test.** `HandPoseTests.Drag_Speed_ByGrip_Wrist_Slow_Handle_Best_TwoHands_Faster`: wrist 0.87, handle 1.50, handle + strap 1.82 m/s.

## 3. Training feedback
- **Hint table.** A new topic «Осмотр: пульс, дыхание, сознание» (`assess`). Steps:
  1. AVPU
  2. pulse on the neck
  3. pulse at the wrist
  4. breathing
  5. capillary refill

  RU + KZ, with ghost-hand guides and glowing targets (Head, Neck, Wrist, Mouth, Nail). Erlan starts at 28 % blood loss: he answers the voice, and his radial pulse is weak.
- **Debrief.** The «Осмотр» lines on the board: «Пульс проверен (на шее) в 0:42 — 122, слабый, частый». EditMode test: `VitalsAssessmentTests.Protocol_Records_Assessments_And_Debrief_Lines`.
- **Tutorial.** `TutorialDirector`: 60 seconds on a practice dummy in front of the player.
  - The dummy is a real BlenderKit rifle crate with rope handles plus an ammo can, and a training tourniquet strapped round the crate.
  - Motions: GRAB (lift the can 15 cm), PULL (the strap 12 cm), TWIST (the windlass half a turn, click), PINCH (the tag), TWO-HAND DRAG (both rope handles, 1 m).
  - It runs before the first Training lesson, never before the demo. «ПРОПУСТИТЬ» is poked with a finger. It is remembered in PlayerPrefs `fm_tutorial_done`, and the Training page has «РУКИ / Управление» to replay it.
  - Tests: `TutorialTests`.

## 4. Performance
- No per-frame managed allocations from the new components. `W8PerfTests` runs an A/B of "GC Allocated In Frame" with face, checks, breath and physical hands on and off.
- Material updates run at ≤ 10 Hz.
- New geometry: face ≈ 3.4k tris, lips, rim, 2 eyes, 4 lids, 12 fingertip/nail parts. That is about 12 extra draw calls near Erlan.

## 5. Files
- **Medical:** `Medical/VitalsAssessment.cs`, `PatientPhysiology.BreathDepth`, `CareProtocol.RecordAssessment`, `Debrief.checks`, `Training` (`assess`).
- **Runtime:**
  - `Casualty/CasualtyFace.cs`
  - `Casualty/VitalsChecks.cs`
  - `Audio/PlayerVoice.cs`
  - `Audio/BreathSynth.cs`
  - `UI/CheckLabel.cs`
  - `Interaction/HandPose*.cs`
  - `Interaction/HapticsProfile.cs`
  - `Input/HandPhysicalVisual.cs`
  - `Scenarios/TutorialDirector.cs`
  - `Scenarios/TutorialProps.cs`
- **Editor:** `Editor/W8Tools.cs` (BuildAssets, head probe, face preview).
- **Tests:**
  - EditMode: `VitalsAssessmentTests`, `W8AssetTests`
  - PlayMode: `VitalsChecksTests`, `PainReactionTests`, `HandPoseTests`, `HandTrackingDemoTests`, `TutorialTests`, `W8PerfTests`, `Interact1Capture` (video)
