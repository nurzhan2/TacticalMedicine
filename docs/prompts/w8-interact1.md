# Stage w8-interact1 — Interactivity phase 1: living Erlan + hand feel (owner 16.10 05:34)

Context: w0b (black screen) and w7 (demo) are done; read docs/BLACKSCREEN_FIX.md, the w7 part of PROGRESS.md and
docs/SUPERVISOR_NOTES.md. The owner presents `C:\Users\user\Desktop\FMVR_DEMO\` this morning.
**Never touch FMVR_DEMO or FMVR_TEST.** Do this stage on a new git branch `w8-interact` created from main (commit there;
merge to main only at the end, after all tests are green). Keep every w0b/w7 guard rail: audits, APK budget, scene
streaming, no passthrough, upright KZ flag, DemoFlowTests. The demo flow must keep working.

Principle: the patient and the items must physically respond to HOW the player acts: where, how hard, how long.
Nothing completes by a button or by a simple touch.

## 1. Living Erlan (CasualtyController / PatientPhysiology / CasualtyVoice / CasualtyRig)

1. **Pulse check.**
   - Two fingertips held on the carotid (neck) or the radial pulse (wrist) for ≥ 2 s, using the index and middle fingertips of a hand pose. The controller grip/trigger pose counts as "fingers" too.
   - Haptic pulses at the physiology heart rate, amplitude from blood pressure.
   - Weak, fast pulse in shock; radial pulse absent when systolic < ~80.
   - A small white hint label shows the result only in Training mode.
2. **Breathing check.**
   - The player puts their head (HMD) within 25 cm of Erlan's mouth for ≥ 3 s. They hear breathing (3D audio) and see chest rise.
   - Rate and depth follow the physiology: tension pneumothorax gives fast and shallow breathing; sealed and decompressed breathing improves.
3. **Voice / AVPU.**
   - Microphone loudness above a threshold near Erlan's head counts as "talking to him". No speech recognition yet.
   - Grab-and-shake on the shoulder is a pain stimulus.
   - Erlan answers by his AVPU state: talks (RU/KZ lines from VoiceBank), groans, or nothing.
   - His head turns and his eyes look at the player's head when conscious. Eyelids open/closed by state.
4. **Pain reactions.**
   - Tourniquet tightening, wound packing, needle and NPA trigger flinch animations (procedural, on the CasualtyRig bones) and a voice line.
   - When conscious, he grabs the player's wrist with his free hand for a moment: an IK hand target on the player's hand.
5. **Visible shock.**
   - Skin and lips tint by blood loss: pale, then grey, lips blue (material property blocks on face/hands).
   - Sweat sheen.
   - Capillary refill: press a fingernail ≥ 1 s, release, and the colour returns in N s depending on perfusion.

## 2. Hand feel (Interaction / Input)

1. **Grab poses per item:** fingers wrap correctly.
   - Data-driven `HandPose` assets: CAT, bandage, gauze, shears, marker, needle, NPA, seal, blanket, card, radio, Erlan drag handle, wrist, ankle.
   - Applied to OVR hands and the glove model.
2. **Physical hands.** Hands do not pass through Erlan, walls, ground or the IFAK. The visual hand stops at the surface while the tracked hand goes further; the ghost outline shows the tracked position if the distance is > 5 cm.
3. **Haptics by action.** One `HapticsProfile`:
   - strap pull = continuous resistance proportional to tension;
   - windlass = one click per half turn;
   - packing = soft pulses;
   - seal press = firm pulse;
   - pulse = heartbeat;
   - drag = heavy low rumble by load.
4. **Hand-tracking mode (no controllers).** Pinch = small items, full grip = drag handles and the strap, poke = buttons. Make sure every demo step is doable with hands only. Auto-switch between controllers and hands.
5. **Weight.** Dragging Erlan with one hand is slow; with two hands it is faster; the vest handle is best. The player's hand is pulled down a little (visual offset) when holding heavy things.

## 3. Training feedback for these

- Hint table entries (RU+KZ) for: pulse check, breathing check, AVPU, capillary refill.
- Debrief lines: "pulse checked at 0:42 — 128, weak".
- A 60-second **tutorial** at the start of Training: grab, pull, twist, pinch, two-hand drag, on a practice dummy box. It can be skipped, and the menu remembers when it is done.

## 4. Tests (scripted hands, PlayMode) — all must be green, plus every old test

- `VitalsChecksTests`: pulse by finger hold (neck + wrist), breathing by head proximity, AVPU by voice level / shake, capillary refill.
- `PainReactionTests`: a tourniquet tighten → flinch + voice; the grab-wrist IK targets the player's hand.
- `HandPoseTests`: every item has a pose, and hands do not penetrate Erlan / walls (> 5 cm).
- `HandTrackingDemoTests`: the full DemoFlow with pinch / grip input instead of the controller grip.
- `TutorialTests`.
- Performance: Quest budget unchanged (no new per-frame allocations; physiology-driven material updates ≤ 10 Hz).

## 5. Deliverables

- `C:\Users\user\Desktop\FMVR_NEXT\FieldMedicVR_v2.apk`: full game, all audits OK, ≤ 200 MB.
- Short video `FMVR_NEXT\interact1.mp4` (1–2 min, editor capture with scripted hands), showing:
  - pulse check;
  - breathing check;
  - Erlan reacting to the voice;
  - the pain flinch and the wrist grab;
  - shock colours;
  - the tutorial.
- Screenshots in `FMVR_NEXT\screens\`.
- `FMVR_NEXT\README.txt` in Russian: what is new and how to try each feature in the headset.
- `docs/INTERACT1.md` (feature → how it works → test). PROGRESS.md updated.
- When all is done, create `logs/w8-interact1.done`.
