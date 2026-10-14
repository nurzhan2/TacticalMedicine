# Bauyr - Field Medic VR - V2 "Open battlefield" (owner request 14.10.2026)

Owner feedback after V1: the game "did not open" on a Quest (sideloaded APK). The trench at night does not
convey war. V2 goal: maximum war atmosphere on an OPEN FLAT battlefield, continuous combat, real cover,
realistic IFAK, three game modes with a white hint panel in Training, national/unit insignia.
V1 code (casualty physiology, treatments, CAT steps, drag, debrief, RU/KZ) is KEPT and reused.

## 0. Launch must work (top priority)
- The APK must start in VR on Quest 3 / 3S. Audit AndroidManifest (VR intent category
  com.oculus.intent.category.VR + android.intent.category.LAUNCHER, OpenXR loader, Meta Quest feature,
  supported devices incl. quest3/quest3s, hand tracking optional), graphics API, IL2CPP ARM64, first scene,
  startup exceptions. QuestManifestCleanup from V1 is a suspect - verify it does not strip required entries.
- tools/quest_check.ps1: if `adb devices` shows a headset -> install APK, launch, capture 40 s logcat
  (Unity, AndroidRuntime, OpenXR, crash) to logs/quest_*.txt, grab `adb exec-out screencap -p`.
  Run it after every build. A build is not "done" on device until this passes, but never block the
  night on a missing headset - just record "device not connected".

## 1. Map: open steppe
- Flat steppe ~400x400 m, overcast day with smoke columns and haze (no night). Distance fog hides edges.
- Player position in the centre, ~40x40 m: ruins of 2-3 brick/concrete houses (walls with holes, slabs,
  rebar), burnt APC, concrete blocks, sandbags, craters, czech hedgehogs, barbed wire.
- Every cover piece has a collider and is tagged Cover (bullets stop on it; player can crouch behind).
- Erlan goes down IN THE OPEN 10-15 m from the ruins. TCCC flow:
  Care Under Fire (fast high-and-tight CAT + drag to ruins) -> Tactical Field Care in cover
  (packing, pressure bandage, chest seal, NPA, needle, hypothermia) -> TACEVAC: Mi-8/Mi-17 type
  helicopter lands at a marked LZ at the end.
- Kazakhstan flag on a pole/stretched on a wall at our position.

## 2. Continuous battle (never stops)
Layered for Quest 3S performance:
| Layer | Content | Tech |
| Near (<60 m) | 8-12 animated soldiers, 2-3 tanks/IFVs | real rigged models, pooled, simple FSM (run/cover/shoot/die) |
| Mid (60-200 m) | 30-60 soldiers, vehicles | low LOD, GPU instancing, crowd FSM |
| Horizon | advancing lines | animated billboard impostors in haze |
| Sky | Su-25 type jets, Mi-24 type helicopters, FPV drones | spline fly-bys, bomb drops |
- BattleDirector: event every 5-20 s (wave, artillery barrage, air strike, MG bursts with tracers,
  drone dive). Intensity curve: quieter for 3-5 s while the player does a fine motor step.
- Enemy fire is real: shooters raycast toward the player area; if the player head is exposed (no Cover
  between) -> whizz/crack sounds, dust impacts, after N seconds exposed -> player hit = scenario fail
  (Combat/Exam only). Cover truly protects.
- Audio: spatial, bullet whizz/crack, distant artillery rumble, tinnitus + muffled mix after close
  blast, controller haptics on near explosions.
- Smoke grenade on the vest: throw -> thick smoke screen to cover the drag.
- Enemy side: neutral uniform, NO real country insignia.

## 3. Modes
| Mode | Battle | Hints | Failure |
| Training | in background far away, nobody shoots at the player | white hint panel | none, repeat step |
| Combat | full | none | Erlan can die, player can be hit |
| Exam | full | none | TCCC score + debrief |
Training can also be started per topic: tourniquet only, packing only, drag only, chest seal only.

### White hint panel (Training)
- When the player comes within 2 m of Erlan a WHITE rounded panel fades/scales in above him,
  billboarded to the player, readable at 1.2 m (world-space, large font).
- Content: "Step 2 of 6", step title, 2-3 short actions, small picture/animation, checkboxes.
- Ghost hands show the motion; the needed IFAK item and the body target glow.
- Voice line duplicates the hint (RU/KZ).
- Step done -> green check + sound -> next step. Wrong action -> panel turns yellow and says why
  ("Tourniquet below the wound - bleeding will not stop").

## 4. Insignia
- Kazakhstan flag patch on player sleeve (visible on forearm/glove area) and on Erlan.
- Military department of Satbayev University patch on the other shoulder. Real emblem file will be put
  by the owner into SourceAssets/Insignia/ (any png/jpg). Until then use a neutral placeholder shield
  with text "ӘСКЕРИ КАФЕДРА / SATBAYEV UNIVERSITY" - do NOT invent/redraw the real logo.
- Name/blood tape on Erlan: "ЕРЛАН  O(I) Rh+".
- Emblem + "Бауыр" on splash and main menu.

## 5. Assets policy
- Real models only: BlenderKit (tools/bk_fetch.py) first; Poly Haven CC0 for HDRI/textures/rocks.
  No primitive/home-made placeholder art in final scenes.
- If something cannot be found at acceptable quality -> add a Meshy AI prompt to docs/MESHY_PROMPTS_V2.md
  and use the best available stand-in until the owner provides the model.
- IFAK models come from Meshy (owner): drop folder SourceAssets/Meshy/ with fixed file names
  (see docs/MESHY_PROMPTS_V2.md). An importer must auto-swap them in when present.
- BlenderKit Royalty-Free assets must NOT be pushed to the public repo: keep them under
  Assets/_Licensed/ (git-ignored). CC0 assets may be committed. Keep ATTRIBUTION.md updated.

## 6. Extras
- Wrist display on the player's left hand: Erlan pulse + golden-hour timer.
- Erlan voice weakens with his state.
- Debrief replays mistakes as markers on the battlefield map.

## 7. Performance budget (Quest 3S, 72 Hz)
<= 500k triangles in view, <= 150 draw calls, <= 4 real-time shadow casters (prefer baked/blob),
textures <= 2K (most 1K, ASTC), particle pools, LODs on every imported model, occlusion where possible.
