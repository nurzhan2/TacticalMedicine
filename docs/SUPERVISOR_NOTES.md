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