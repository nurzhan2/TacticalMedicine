# Supervisor review notes (night supervisor, read and address in the relevant stage)

## 06:30 review of docs/screens
- s2_erlan_wounded.png: casualty, gear and blood stream look good. The blood-soaked trouser area has bright GREEN specular highlights (probably the green chemlight/point light or a reflection probe reflecting on a very glossy blood material). Blood on cloth should be dark, mostly matte (smoothness ~0.3-0.45 when wet, lower when soaked into fabric); keep the fresh pool glossy but tint the light/reflection neutral. Fix in S2/S3 or S7.
- s1_trench.png: the trench is still a flat greybox (untextured planks, uniform grey sandbags, flat ground, empty dark sky). S4 must make it read as a real night trench: mud/soil texture with wet normal map, wood grain on revetment, sandbag cloth texture with variation, dirt on duckboards, debris, wires, puddles, sky with faint stars/cloud glow + distant flare/fire light on the horizon, fog/haze. This is what the user sees first - top priority for the WOW effect.
## 07:05 morning deadline
- Owner wakes ~09:30. S4 and S5 prompts now end with a "Morning checkpoint" (APK to Desktop + docs/MORNING_REPORT.md). Any stage running after 09:00 must make sure a working APK from the current state exists on the Desktop before continuing.
- Scenario S1 «Сброс» must be fully playable end-to-end in that APK (wound -> tourniquet hands-on -> time mark -> debrief) - that is the demo the owner will try first.
## 07:06 review of s2_erlan_wide.png
- Chemlight mesh renders MAGENTA/pink (missing or broken shader -> URP error colour) while its light is green. Fix the material (URP/Unlit or Lit emissive green) and check every material for pink after shader changes.
- Blood pool / drops on the duckboards look like flat saturated red discs (unlit-looking, too bright for night). Use a dark wet decal-like material, irregular shape, slightly glossy, darker red-brown, soaked into wood/mud; grow it over time.
- Casualty must rest ON the duckboards/ground (check no floating, feet/hips contact) and partly in mud, not hovering.
## 08:40 review of s3_* screenshots
- s3_05_drag.png: casualty lies FACE DOWN during the drag and the IFAK is an open crate with saturated primitive blocks (pure red/blue/white). Drag must be supine (on the back), pulled head-first by the plate-carrier drag handle, arms crossed/limp, heels dragging. IFAK must look like a real pouch kit (coyote/olive fabric pouch, rip-away panel, items in muted real colours: black CAT with red tab, white/olive packaged gauze, olive Israeli bandage) - no primary-colour boxes anywhere in the scene.
- s3_04_tq_locked_time.png: too dark and too close to read. Debug/presentation shots should be framed at ~0.6-0.8 m with a soft fill light so the CAT band, windlass, clip and time label are readable. The red slab top-right is unexplained geometry - check.
- Overall: everything readable in the headset matters more than screenshots, but screenshots are what the owner sees first in MORNING_REPORT - frame them well.
## 09:21 review
- s3_11_packing.png: blood "drops" render as long thin vertical red streaks hanging in the air above the body (stretched particles / velocity-stretched billboards). Drops should be short, fall and splat; arterial spurts pulse with heartbeat; no static streaks.
- APK safety build is on the Desktop (09:15, 76.5 MB). Disk is tight (~5.8 GB): delete Bee android intermediates only right before the final build if needed.
## 09:55 review of s4_* screenshots
- s4_01_trench_flare.png: big improvement (wood grain, starry sky, sandbags, mud). Remaining: left wall is almost black while right wall is lit - add a weak cool moon fill so both walls read; sandbags look like smooth pillows - add cloth normal/sag and colour variation.
- s4_08_headlamp_casualty.png: red headlamp saturates the casualty to pure orange-red, wound and blood become unreadable (red on red). Reduce red headlamp intensity ~50%, widen falloff, add a white-light mode (toggle) for treatment under cover - TCCC allows white light in cover; red/IR only under fire.
## 11:32 review of s5_* screenshots
- s5_menu_main.png: good (round buttons, RU/KZ, readable). Keep.
- s5_debrief.png: the 'ЕЩЁ' (retry) button has no round background - only text, unlike МЕНЮ/ДАЛЕЕ. Also the board overlaps the trench wall at the left edge (place it fully in free space).
- s5_s5_fpv.png: scenario shots all look like the same trench + casualty; the FPV scenario should show its signature (drone wreck / rotor pieces nearby, shrapnel wounds visible, smoke) and every scenario's first frame should make the wound obvious (exposed, bleeding). Make each scenario visually distinct at a glance.
- Disk: owner freed space, now ~37 GB free - no need to delete the Bee cache anymore; keep incremental Android builds fast.
## 14:24 review of s7_* screenshots
- s7_s1_drop.jpg: the thigh wound renders as a bright red diamond/rhombus OUTLINE (looks like a UI marker or an emissive decal edge), not an organic wound. Wound must read as torn fabric + dark wet blood soak spreading from an irregular hole, no emissive/outline, no geometric shape. If a highlight is needed for training mode, make it a soft pulse only in Training, never in Scenarios/Exam.
- s7_fpv_dive.jpg: drone is a few pixels - fine for realism, but add its buzzing audio ramp + a faint red/IR LED so the player can track it. Field beyond the wire looks flat/empty: add craters, shell holes, burnt vehicle silhouette on the horizon (tank/BMP wreck), smoke columns lit by the glow.

## 14.10 18:50 - V2 START (stages w0..w6). Notes above this line are about the V1 night trench - treat as legacy.
- Owner priorities in order: (1) the APK must start on a real Quest, (2) war atmosphere on an open steppe,
  continuous battle, (3) real cover + drag Erlan from the open into ruins, (4) Training mode with the WHITE hint
  panel, (5) insignia (KZ flag, Satbayev military department), (6) real IFAK from owner's Meshy files.
- Real models only (BlenderKit free plan + Poly Haven CC0). Anything that looks like grey boxes / primitives in
  a screenshot is a failure - fix it or replace it before closing a stage.
- Every stage that changes runtime code ends with a quick APK build so a working APK always exists.
- Keep V1 features working (CAT steps, packing, drag, RU/KZ, debrief); V1 tests may be updated, not deleted.


## 15.10 03:55 - supervisor review of w2 screenshots (MUST be fixed in w3/w4, before w6)
1. GROUND IS BROKEN: v2_w2_overview / cover_view show a black-and-tan pixel noise ("straw grass" alpha dither / clip
   artifact) over the whole steppe. It reads as a rendering bug. Replace with a clean Poly Haven dry-ground/dirt PBR
   (proper tiling + detail blend, no dither) and only sparse, soft grass clumps (cross-quad with proper alpha-to-coverage
   or cutout without dither noise). Check the screenshot at 1600 px - no pixel noise anywhere.
2. RUINS TOO SMALL / HOME-MADE: the three ruins are knee-to-chest high shoebox walls; "procedural house B" (grey frame
   with roof on the right) looks fake. Owner rule: real models only. Use the real BlenderKit ruins (Brick Ruins,
   Ruined Farmhouse, Rocks & Ruins etc.) as the main buildings, scaled to real 2-storey house size (walls 3-6 m),
   remove the procedural house or hide it under real assets. Ruins must give standing AND crouching cover.
3. SANDBAGS look like cement bags with "50KG" print. Military sandbags: plain olive/khaki burlap/polypropylene, no
   print, stacked in a staggered wall. Retint/re-texture (remove print) now; a Meshy prompt "military_sandbag_wall"
   must exist in MESHY_PROMPTS_V2.md.
4. A grey floating box building on the horizon (center) - remove or ground it into the haze properly.
5. Overall look target: grey overcast war zone, burnt and muddy, craters, debris, scorched ground near wrecks.
