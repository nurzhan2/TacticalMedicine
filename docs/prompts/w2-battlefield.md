# Stage w2 - open steppe battlefield scene

Context: docs/GDD_V2.md sections 1, 4, 7. Use only the real assets imported in w1 (docs/ASSETS_V2.md).

Tasks
1. New scene Assets/Scenes/Battlefield.unity (keep the V1 trench scene as legacy, not in the main flow).
   - Flat steppe terrain/mesh ~400x400 m, dry grass + dirt PBR (Poly Haven), subtle height noise only.
   - Overcast day lighting: Poly Haven overcast HDRI for ambient/reflection, one directional light,
     exponential fog + distance haze, 5-8 smoke columns on the horizon (pooled particles, cheap).
   - Player position in the centre ~40x40 m: 2-3 ruined houses, burnt APC, concrete blocks, czech
     hedgehogs, barbed wire, craters, debris. Composition: real cover lines, a doorway/gap to drag Erlan
     through, an open 10-15 m stretch between where Erlan falls and the ruins.
   - Every cover object: collider on layer "Cover", tag Cover, NavMesh obstacle where relevant.
   - Kazakhstan flag on a pole at our position (cloth animation via vertex shader, cheap).
   - Helicopter LZ marker (smoke + panel) 40 m behind the ruins for the evacuation phase.
2. Player rig: XR Origin with locomotion (smooth + snap turn + teleport), crouch works physically
   (real head height) - cover must make sense at kneeling height ~1.0-1.2 m.
3. Static batching/LODGroups on everything imported, baked lighting or mixed with few shadow casters,
   occlusion culling baked. Keep within the budget in GDD_V2 section 7; log tris/draw calls from an editor
   profiler capture in docs/PROGRESS.md.
4. Screenshots (JPG 85%, <= 1600 px) to docs/screens/v2_w2_*.jpg: overview, ruins close-up, view from cover,
   view toward the enemy, flag. Look at them yourself and fix anything that looks fake or primitive.

Done: Battlefield scene playable in editor desktop simulator, cover colliders verified by a PlayMode test
(raycast from enemy side to a player crouched behind each cover piece is blocked), screenshots reviewed,
committed.
