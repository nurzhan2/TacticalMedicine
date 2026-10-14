# SUPERVISOR FIRST: before anything else in this stage, fix every item of the 15.10 03:55 section in docs/SUPERVISOR_NOTES.md (ground pixel noise, real-size real ruins instead of procedural house, military sandbags, floating horizon box) and re-shoot docs/screens/v2_w4_battlefield_*.jpg to prove it.

# Stage w4 - Training / Combat / Exam modes, white hint panel, TCCC flow in the open

Context: docs/GDD_V2.md sections 1 and 3. Reuse V1 casualty physiology, treatments, CAT steps, drag, debrief.

Tasks
1. Scenario flow on the Battlefield scene: Erlan is hit and falls IN THE OPEN 10-15 m from the ruins.
   Care Under Fire: player must run out, apply CAT high-and-tight fast, drag Erlan into the ruins (V1 drag
   adapted to open ground + doorway). Tactical Field Care in cover: assessment (MARCH), packing, pressure
   bandage, chest seal, NPA, needle decompression, hypothermia (mylar), casualty card. TACEVAC: radio call
   -> transport helicopter lands at the LZ, player helps load Erlan (or presses a button at the stretcher),
   scenario ends -> debrief.
   Port the V1 8 scenarios' injury sets to this flow (different wound/position/time pressure each).
2. Modes in the main menu (RU/KZ): Training (with topic selector: full / tourniquet / packing / drag /
   chest seal), Combat, Exam. Training: background battle far, no enemy targeting, no fail.
3. White hint panel (Training only):
   - Appears when the player head is within 2 m of Erlan: white rounded card, soft shadow, smooth
     fade+scale-in animation, billboards to the player, positioned above Erlan without hiding the wound,
     large readable text at 1.2 m (TextMeshPro, RU/KZ).
   - Shows "Step X of N", title, 2-3 short actions, small illustration/animated icon, checkboxes.
   - Ghost hands animate the required motion; the required IFAK item and the body target glow.
   - Voice line (existing TTS pipeline, RU/KZ) reads the step.
   - Step complete -> green check, sound, auto-advance. Wrong action -> card turns yellow with the reason.
   - Steps come from a data table (ScriptableObject) shared with the debrief so text stays consistent.
4. Wrist display on the left hand: Erlan pulse + golden-hour timer (all modes).
5. Debrief: show mistakes as markers on a mini map of the battlefield + timeline.

Done: all three modes playable end-to-end in the desktop simulator, the hint panel appears/advances correctly
(PlayMode tests for proximity, advance, wrong-action), bot medic completes Training and Combat runs,
screenshots docs/screens/v2_w4_*.jpg of the hint panel reviewed, committed.

