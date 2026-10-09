# Бауыр — Field Medic VR · Game Design Document

Realistic VR combat-medicine trainer for **Meta Quest 3S**. Player = a soldier of the Armed Forces of the Republic of Kazakhstan (ВС РК) trained in TCCC (CLS level). Night, front-line trench, warfare of 2025–2026 (drones everywhere, artillery, glide bombs, mines). The battle buddy **Ерлан** (рядовой Ерлан Сейтқали, 24 y.o.) keeps getting wounded in different ways; the player saves him **with his own hands**, step by step, as in reality. The enemy is unnamed ("противник"); no real countries.

Owner priorities (verbatim intent): maximum realism and immersion; the buddy's leg can be lifted and he screams in pain while you hold it; the tourniquet is applied physically (no "press a button"); learn to lift/drag him correctly into the trench without effort; explosions and gunfire around you at night, a metre away; different scenario per wound type (grenade → thigh, bullet → chest, etc.); real walls appear (passthrough) only when you get close to them, otherwise full immersion; RU + KZ voice; maximum naturalistic gore.

## 1. Pillars
1. **Hands do the medicine.** Every action = physical gestures with controllers or tracked hands (grip, pull, twist, press, wrap, peel, insert). Wrong technique produces wrong results, not error popups.
2. **The casualty is alive.** He breathes, bleeds in rhythm with his pulse, pales, his lips go blue, he grabs your arm, screams «Сними, больно!», goes quiet when he crashes. Vital signs are *felt* (radial/carotid pulse under your fingers, chest rise, breathing sounds), never shown on a HUD (an optional instructor monitor exists only in Training mode).
3. **The war never pauses.** Drones buzz, flares drift, shells land 5–40 m away, "Воздух!" on the radio. Raising your head above the parapet under fire is dangerous. Light discipline: only a red headlamp.
4. **Truthful feedback.** After every run, a debrief: timeline of actions, mistakes with explanation, what would have happened in reality (e.g. "жгут на колене → кровотечение продолжилось, смерть через 4 мин"), score.

## 2. Player experience (a typical run, Scenario 1)
1. Trench at night, 01:40. Ерлан crouches 2 m away watching the field. Radio chatter. A quiet buzz above — a recon/drop quadcopter.
2. Click of a release, 1.5 s whistle, explosion 1–2 m behind the parapet: flash, dust, camera shake, ear ringing (tinnitus, muffled audio for 4 s).
3. Ерлан falls, screams «Аяғым! Нога!». Bright blood pulsates from his inner right thigh, pants soak dark, a pool grows on the trench floor.
4. **Care under fire:** the drone is still overhead. Player stays low, grabs **Ерлан's tourniquet** from his plate-carrier pouch, wraps it **high and tight** over the trousers on the thigh, pulls the strap, twists the windlass until the spurting stops, locks it in the clip. Ерлан screams while you twist.
5. Grab the **drag handle** on the back of his plate carrier and pull him 4 m along the trench to the covered dugout corner, keeping your head below the parapet.
6. **Tactical field care (MARCH):** gloves, cut the trouser leg open with shears, see the wound channel, re-check the TQ (no distal pulse at the foot — feel it), mark time "T 01:43" with a marker on the TQ strap and his forehead, blood sweep (hands over the body: find a small fragment wound on the left forearm), pressure dressing on it, check breathing, thermal blanket, casualty card. Talk to him ("Смотри на меня!" voice button).
7. Radio: evacuation in 40 min. Debrief.

## 3. Game modes
- **Обучение (Training):** guided lessons per skill (tourniquet, packing, chest seal, drag, NPA, needle) with a voice instructor, ghost hands showing the motion, unlimited time, instructor vitals monitor visible.
- **Сценарии (Scenarios):** the 8 scenarios below, hints optional.
- **Экзамен (Exam):** random scenario, random wound side/variant, no hints, strict time.
- **Разбор (Debrief):** board in the dugout: timeline, mistakes, survival, limb outcome, score 0–100, best results.
- Settings: language RU/KZ, hints on/off, dominant hand, seated/standing, height calibration, gore level (owner default: max), comfort (snap turn angle).

## 4. Scenarios (from RESEARCH.md §7, keep medical details from there)
| # | Name | Mechanism / cue | Wounds | Key skills | Complications |
|---|---|---|---|---|---|
| S1 | «Сброс» | Drone drops VOG grenade, whistle, blast 1–2 m | R thigh arterial (fragment, inner thigh) + small L forearm fragment | CUF tourniquet high&tight, drag, TFC re-check, mark time, blood sweep | Drone may drop again; shock in 2–3 min |
| S2 | «Снайпер» | Bullet crack on parapet, buddy slumps, wheezing | Penetrating R chest (entry below clavicle) + **exit wound on back** | Hand over wound, vented chest seal on exhale, roll to find exit, seal it, position | Tension pneumothorax 5–10 min later → burp seal → needle 2nd ICS MCL / 5th ICS AAL |
| S3 | «Мина» | Moving along communication trench, muffled bang | Traumatic amputation R foot + fragments L leg & groin, ruptured eardrums | Walk only in own footsteps, TQ high on thigh, 2nd TQ if needed, stump dressing, check groin | Bleeding resumes after vasospasm; panic, tries to crawl |
| S4 | «Миномёт» | Long incoming whistle, blast on parapet | R groin junctional bleed | Knee/fist pressure, hemostatic packing with fingers, **3 min pressure (count)**, Israeli bandage figure-8 | TQ impossible; releasing early → rebleed |
| S5 | «FPV» | Rising whine, explosion at the foxhole entrance | Visible: shin, arm, face; **hidden: buttock/back under plate carrier** | Full blood sweep, open/remove plate carrier, log-roll, pack hidden wound | Deteriorates while "visible" wounds treated; second FPV |
| S6 | «КАБ» | Huge blast nearby, tinnitus, dust | Blast/TBI, unconscious, blood from ears, vomit, snoring | M check, clear mouth, NPA (bevel to septum, insert toward the ground), recovery position | Airway obstruction kills in 3–4 min if left supine |
| S7 | «Под огнём» | While dragging a casualty with TQ, artillery barrage | Existing thigh TQ | Stay below parapet, drag in bursts between impacts, re-check TQ on arrival (it slipped) | Standing up = hit; TQ slipped → rebleed |
| S8 | «До рассвета» | Radio: no evac until dawn, drones over road | TQ applied at 22:10 on a non-arterial wound | Prolonged care: time-skip milestones (30 min, 1 h, 2 h, 4 h), TQ reassessment & conversion at 2 h, warmth, keep awake | Conversion forbidden in shock/amputation; after 6 h forbidden |

Each scenario has variants (side L/R, timing, second hit) so Exam mode is not memorisable.

## 5. Interactions spec (all physical)
Input: Quest controllers (grip = grab, trigger = fine action / squeeze) **and** hand tracking (pinch/grab). Haptics on every contact.
- **Grab model:** proximity grab of handles with priority; two-hand grabs supported; objects have weight (heavy things lag behind the hand with spring/damper — a casualty never teleports).
- **Tourniquet (CAT) state machine:** InPouch → Held → BandAroundLimb (bring the open loop over the foot/hand and slide it up the limb; or route around the limb when it's not reachable) → Positioned (slider along the limb bone; valid zone = 5–8 cm above wound or "high & tight" under fire; joints invalid) → StrapPulled (grab the strap tail and pull ≥ 15 cm; slack measured) → WindlassTwisted (grab the rod, rotate wrist; each 180° = one half-turn; bleeding decreases with turns; required 1–3 turns depending on limb; casualty screams, grabs your arm) → WindlassLocked (move rod into the clip) → StrapSecured (velcro strap over clip) → TimeMarked (pick marker, "write" on the time strap → auto writes current time).
- **Leg lift / limb handling:** grab ankle/calf/knee; the leg follows via two-bone IK with joint limits & weight; quick or rough lifts trigger pain screams (pain = angular velocity + wound proximity). Elevating a leg instead of a tourniquet is graded as a mistake; lifting to route a tourniquet or to examine is fine.
- **Drag:** grab the plate-carrier drag handle (behind the neck) or a drag strap clipped to it, pull while moving backwards in real space (or with thumbstick "crawl" for small play areas); body slides with friction, head controlled; descending into the trench edge feet-first.
- **Log roll:** two-hand grab on shoulder + hip, rotate toward you → he rolls on side; needed to check the back and for recovery position.
- **Direct pressure / packing:** palm or fingers into the wound collider; pressure = depth & time; packing = feed hemostatic gauze roll into the wound with repeated push strokes (each stroke consumes gauze length), then hold 3 min (training option: accelerate), then Israeli bandage (wrap around limb N times, twist the pressure bar, secure clip).
- **Shears:** grab shears, trigger to cut, move along the trouser seam → cloth splits (mesh swap/alpha), wound exposed.
- **Chest seal:** tear package, peel backing (grab tab, pull), wipe area, place with palm during exhale (watch chest), press edges; "burp" = lift edge with fingers.
- **Needle decompression:** palpate landmarks (clavicle → 2nd intercostal space, midclavicular line) with fingers, insert perpendicular over the rib top edge, hiss of air.
- **NPA:** measure (nostril → earlobe), lube, insert with bevel to septum, direction toward the ground (posterior), rotate if resistance.
- **Pulse check:** two fingers on radial (wrist) or carotid (neck) → controller haptic pulses at the casualty's heart rate & strength; foot pulse (posterior tibial / dorsalis pedis) to verify a TQ.
- **Thermal blanket, casualty card, marker, radio (call MEDEVAC: 9-line simplified), voice buttons** («Смотри на меня!», «Дыши!», «Держись, брат»).

## 6. Casualty system
- Model: Mixamo "Swat Guy" humanoid, recoloured to a Kazakh-army style multicam/olive uniform, plate carrier with drag handle, helmet, IFAK pouch (with his tourniquet), gloves. Face lit only by red light.
- Physiology: `FieldMedic.Medical.PatientPhysiology` (ATLS classes, AVPU, perfusion-scaled bleeding, hypothermia+coagulopathy, pneumothorax → tension, airway obstruction, tourniquet timers, death causes). Extend as needed (wound per scenario).
- Visual state: skin pallor and cyanosis via material params, sweat sheen, breathing amplitude & rate from physiology, eyes half-closed when V/P/U, agonal breathing before death.
- Blood: pulsating arterial jet particles synced to heart rate, venous ooze, soaking decal growth on clothing, ground pool decals, blood on player's gloves.
- Audio/voice: pain groans/screams scaled by pain level; breathing (wheeze, gurgle, snore); phrases RU+KZ (see RESEARCH §6). Voice lines tonight: TTS placeholders + Freesound vocalisations; owner will record real voice later — keep a `VoiceBank` keyed by line id so files can be swapped.
- Behaviour: grabs player's hand during TQ twist, tries to sit up in tension pneumo, tries to crawl after amputation, asks for water, cold, «Маме скажи…» when crashing.

## 7. Environment & atmosphere
- Trench segment ~16 m with a zig-zag, timber revetment, sandbags, a covered dugout corner (блиндаж) with a log roof, communication trench branch (S3 mines), firing step, ammo crates, an anti-drone net partially over the trench, cables. Ground mud with puddles.
- Night: dark sky with stars, distant glow on the horizon, moon low. Lighting: player's red headlamp (toggle), flares (drifting white light with swinging shadows), explosion flashes, tracer lines. Fog for depth.
- Combat ambience director: distant artillery, outgoing/incoming, MLRS rumble, machine-gun bursts, drone types by sound (FPV high whine, recon quad hum, "moped" Shahed far above), near-miss events scripted per scenario. Near blasts: flash + shake + dirt falling on the player + tinnitus + muffled mix.
- Threat system: player head height vs parapet; under fire exposure accumulates risk; drone overhead makes white-light/standing riskier. Getting "hit" ends the run with an explanation (training value).

## 8. Comfort & safety
- **Proximity passthrough:** when the player's head or hands get within ~0.6 m of the real boundary/walls (OVRBoundary geometry; MRUK room walls if available), fade in a passthrough overlay locally/globally (opacity ramps 0→1 from 0.6 m to 0.25 m), so real walls become visible only when close; otherwise full immersion. Keep the system guardian as a fallback.
- Snap turn 30/45°, optional short teleport along the trench floor, seated mode (raises virtual floor), height calibration, vignette on fast motion. Flash intensity limiter.

## 9. UI
- Diegetic: wrist watch on the left wrist (time, scenario timer), casualty card, radio. Main menu = a board in the dugout with large round buttons (owner likes round animated buttons) selectable by poke/ray. Debrief on the same board.
- Language RU (default) / KZ.

## 10. Technical
Unity 6.3 LTS, URP (Mobile renderer asset), OpenXR + Meta XR Core/Interaction SDK 207, Android ARM64 IL2CPP. Own interaction layer with `IHandInput` (see CLAUDE.md). Scenes generated by editor scripts. Tests: EditMode (medical model, protocol, state machines) + PlayMode (bots with `ScriptedHandInput` complete each scenario end-to-end and the casualty survives; wrong actions produce the expected mistakes). Desktop simulator lets you play in the editor with mouse/keyboard.

## 11. Definition of "WOW by morning"
- Opening `Assets/Scenes/Main.unity` and pressing Play in the editor shows the night trench with atmosphere and Ерлан; the desktop simulator lets you play Scenario 1 completely: blast → tourniquet steps by hand → drag → TFC → debrief.
- Screenshots in `docs/screens/` look like a real night trench, not grey boxes.
- PlayMode bot tests pass for every implemented scenario.
- `Builds/FieldMedicVR.apk` installs on Quest 3S (friend's headset) and runs at 72 fps.
- `README.md` explains how to run, controls, install the APK, record voice lines.
