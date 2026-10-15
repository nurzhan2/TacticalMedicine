# Stage w7-demo: presentation build for the MORNING of 16.10.2026 (hard deadline)

The owner presents the game on the morning of 16.10. Runs after w0b-blackscreen; read docs/BLACKSCREEN_FIX.md and
keep every guard rail from it. Check the clock with `Get-Date`:

- **By 06:00 local:** stop new features and go to the final build.
- **By 07:30 local:** the final APK + backup video must be in `C:\Users\user\Desktop\FMVR_DEMO\`.

A working, stable, fully interactive demo is worth more than more content.

## 1. Owner demands (15.10 23:51), all must be true on the headset input path (OVR controllers AND hands)

1. **Fully interactive, nothing done by a menu button.**
2. **Dragging Erlan:**
   - The player can GRAB Erlan with one or two hands: drag handle on the plate-carrier shoulder strap / collar, plus
     wrist and ankle handles. Use the existing CasualtyDrag / LimbHandle / ProxyHandle / HandGrabber code.
   - Drag him over the ground into cover; his body follows with weight (no teleport, no sliding through walls).
   - The player can roll him (LogRoll) and lift and move limbs.
3. **Treatment by hand:**
   - Open the IFAK pouch by pulling its handle; take each item out by hand.
   - Apply each item by hand:
     - **CAT tourniquet:** put the loop on the limb high and tight, pull the strap, turn the windlass, lock it in the clip, write the time.
     - **Gauze:** pack the wound by pushing it in.
     - **Israeli bandage:** wrap it around the limb.
     - **Chest seal:** peel the backing and press it on.
     - **NPA:** insert it.
     - **Needle decompression:** at the correct landmark.
     - **Mylar blanket.**
     - **Marker + card.**
   - Each item gives haptic and visual feedback, and the casualty physiology reacts (bleeding stops, etc.).
   - No item may "auto-complete" just by touching.
4. **Kazakhstan flag is UPSIDE DOWN — fix everywhere** (flags on poles, sleeve patches, wall flags, menu/briefing icons, loading screen).
   - Correct flag: sky-blue field; golden sun with rays ABOVE the soaring golden steppe eagle (berkut) in the centre; vertical golden national ornament band along the HOIST side, next to the pole. Seen from the front with the pole on the left, the ornament is on the left.
   - Check UV/texture orientation and the mesh/cloth rotation, in all instances.
   - Also check the Satbayev University military department patch (not mirrored, text readable, right way up).
   - Add an EditMode test that samples the flag texture: the sun/eagle region is in the upper-centre and the ornament band is on the hoist side. Add a screenshot check too.

## 2. Demo flow (what the owner will show, 5–7 minutes)

- Menu has a big white **"ДЕМО / DEMO"** button. It starts **Training mode with the white hint panel**.
- Scene: steppe battlefield with live battle. Erlan is wounded in the open (bleeding thigh). The player drags him into cover and treats him: tourniquet → wound packing → chest seal → blanket → card.
- Then TACEVAC helicopter, then debrief.
- Hints in RU (+KZ), no dead ends. If the player does something wrong, the hint explains it and they can retry.
- Run the whole flow with ScriptedHandInput in a PlayMode test (`DemoFlowTests`): grab Erlan → drag to cover → every treatment step done by simulated hand motion → TACEVAC → debrief. No errors.

## 3. Backup for the presentation (in case the headset fails on stage)

- Record a 2–3 min 1080p MP4 of the demo flow in the editor with Unity Recorder: scripted hands, steady camera, battle audio. Save it as `FMVR_DEMO\FieldMedicVR_demo.mp4`.
- 10 best screenshots: `FMVR_DEMO\screens\`.

## 4. Final build

- `FMVR_DEMO\FieldMedicVR_DEMO.apk`: full game, Demo button, all w0b guards.
- `FMVR_DEMO\FieldMedicVR_SAFE.apk`: Training only, no battle, as a fallback.
- Both must pass QuestManifestAudit + QuestSceneAudit.
- `FMVR_DEMO\README.txt` in Russian: install via laptop cable (`tools\quest_bisect.ps1` / `quest_check.ps1 -Apk`), where the app is (Library → Unknown sources), demo steps, and what to do if the headset shows black (use SAFE, or the video).

## Done criteria

- Section 1 is verified by tests and screenshots (flag screenshot from front and back).
- DemoFlowTests are green and all old tests are green.
- The APKs, video and README are in FMVR_DEMO.
- PROGRESS.md is updated and commits are made (prefix `w7:`).
- Then create `logs/w7-demo.done`.
