# Stage w3 - continuous battle (BattleDirector) + real enemy fire

Context: docs/GDD_V2.md section 2 and 7. The battle must NEVER stop and must feel like a frontal assault
everywhere: soldiers, tanks, aircraft, bombs, bullets - while staying at 72 fps on Quest 3S.

Tasks
1. Layers:
   - Near (<60 m): 8-12 pooled soldiers (rigged models from w1 + existing humanoid clips): run between
     cover, kneel, shoot (muzzle flash + tracer), get hit, die (ragdoll-lite or death clip), respawn off-screen.
     2-3 tanks/IFVs moving on splines, turret aiming, main gun fire with blast + dust.
   - Mid (60-200 m): 30-60 low-LOD soldiers with GPU instancing / simple crowd FSM, vehicles.
   - Horizon: animated billboard impostors of advancing lines in the haze (generate impostor sheets in editor).
   - Sky: jets (Su-25 style) and attack helicopters on splines with rocket/bomb runs; FPV drones diving.
2. BattleDirector (ScriptableObject-tuned): event every 5-20 s from a weighted table (wave, artillery barrage,
   air strike, MG burst with tracers, drone dive, tank shot). Intensity curve API: medical steps can request
   "focus" -> 3-5 s lower intensity near the player but background never silent.
3. Enemy fire at the player: shooters raycast toward the player head/torso; if not blocked by Cover layer ->
   bullet whizz/crack 3D sounds, dust/concrete impacts near the player, suppression effect (slight vignette,
   breathing). In Combat/Exam: exposed > N s (tunable, ~4 s cumulative in 10 s) -> player hit -> scenario fail
   with clear message. In Training: enemy never targets the player.
4. Explosions: pooled VFX (flash, debris, smoke, scorch decal), camera-safe shake (very small, comfort-safe),
   controller haptics by distance, close blast -> tinnitus ring + low-pass muffled mix for 3-6 s.
5. Smoke grenade on the vest: grab, pull pin, throw -> dense smoke screen 20 s that blocks enemy raycasts.
6. Audio mix: distant artillery rumble bed, near/far gunfire layers, aircraft flyby doppler, use existing or
   CC0 sounds (freesound CC0 / Sonniss GDC bundles if present); credit in ATTRIBUTION.md.
7. Enemy side uses neutral uniforms without real country insignia.
8. Perf: pool everything, no per-frame allocations, profile in editor; document numbers in docs/PROGRESS.md.

Done: in Battlefield scene the battle runs continuously for 10 minutes in a PlayMode soak test without errors
or GC spikes, enemy-fire exposure logic covered by tests, screenshots docs/screens/v2_w3_*.jpg reviewed
(must look like war, not like test boxes), committed.
