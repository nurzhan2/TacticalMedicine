# Meshy AI prompts (owner generates, drops files into SourceAssets/Meshy/)

Settings: Text to 3D, Realistic, PBR on, target 10k polys (5k for small items), export GLB (or FBX).
File names are FIXED - the importer (Editor/MeshyImporter) matches them:

| File name (glb/fbx) | Item |
| cat_tourniquet | CAT Gen 7 tourniquet |
| israeli_bandage | Israeli 6" bandage |
| combat_gauze | QuikClot Combat Gauze |
| chest_seal | HyFin Vent chest seal |
| needle_decompression | 14G 3.25" decompression needle |
| npa | Nasopharyngeal airway 28Fr |
| trauma_shears | Trauma shears |
| gloves | Black nitrile gloves |
| mylar_blanket | Emergency thermal blanket |
| sam_splint | SAM splint |
| marker_card | Marker + TCCC casualty card |
| ifak_pouch | IFAK rip-away pouch |
| smoke_grenade | Smoke grenade |

## Prompts
cat_tourniquet: Combat Application Tourniquet CAT Gen 7, black nylon strap with velcro, black plastic windlass rod, red windlass tip, triangular windlass clip with white time strap, metal friction buckle, rolled ready-to-use configuration, realistic military medical equipment, PBR textures, real-world scale 95 cm strap, game-ready

israeli_bandage: Israeli emergency trauma bandage 6 inch, olive green vacuum-sealed plastic packet with printed instructions, plus a second unpacked version: white non-adherent pad attached to beige elastic bandage with plastic pressure bar and closure clip, realistic medical equipment, PBR textures, game-ready

combat_gauze: QuikClot Combat Gauze hemostatic dressing, sealed olive drab foil pouch with tear notch and printed label, plus opened Z-folded white gauze strip, realistic military medical supply, PBR textures, real-world scale 12x8 cm pouch, game-ready

chest_seal: HyFin Vent chest seal twin pack, flat olive foil package, plus unpacked round clear vented chest seal with three-channel valve and red pull tab backing, realistic military medical equipment, PBR textures, game-ready

needle_decompression: Needle decompression kit ARS 14 gauge 3.25 inch, orange rigid plastic protective tube with cap, plus exposed IV catheter needle with clear hub, realistic military medical equipment, PBR textures, game-ready

npa: Nasopharyngeal airway 28 Fr, soft flexible translucent amber rubber tube with flared trumpet end and beveled tip, with small lubricant sachet, realistic medical equipment, PBR textures, real-world scale 15 cm, game-ready

trauma_shears: Trauma shears, black angled blades with blunt tip, black plastic handles, stainless steel rivet, realistic paramedic tool, PBR textures, real-world scale 19 cm, game-ready

gloves: Pair of black nitrile medical gloves folded inside a small clear zip bag, realistic tactical medical supply, PBR textures, game-ready

mylar_blanket: Emergency mylar thermal blanket, folded silver-gold reflective foil rectangle in clear plastic wrap, plus unfolded crinkled version, realistic, PBR textures, game-ready

sam_splint: SAM splint 36 inch, rolled orange and blue foam-covered aluminum splint, realistic military medical equipment, PBR textures, game-ready

marker_card: Black permanent marker with cap and a TCCC casualty card DD Form 1380, tan waterproof card with printed body diagram and fields, realistic, PBR textures, game-ready

ifak_pouch: Military IFAK rip-away medical pouch, coyote brown nylon, MOLLE webbing on back, red cross patch and pull handle, zipper open showing elastic loops inside, realistic tactical gear, PBR textures, game-ready

smoke_grenade: Military smoke grenade cylinder, olive drab metal canister with yellow markings, safety pin and spoon lever, realistic, PBR textures, real-world scale 15 cm, game-ready

## Extra prompts for assets not found on BlenderKit (agents append below)

### Added by w1-assets (14.10.2026)
BlenderKit (free plan) had no usable sandbags, Soviet-type armour (BMP/BTR/T-72), Su-25, Mi-24, Mi-8/17, shell craters
or rigged modern soldiers with plate carriers. The scene uses the stand-ins listed in docs/ASSETS_V2.md until these
files exist. Drop them into `SourceAssets/Meshy/` with these exact names (glb or fbx); Editor/MeshyImporter converts
them (real size, LODs, colliders, Cover tag) into `Assets/Art/Meshy/<category>/<name>/<name>.prefab`.
Settings for scenery: Text to 3D, Realistic, PBR on, target 20-30k polys (vehicles/aircraft), 10k (props), GLB.
For soldiers: enable Meshy auto-rigging (humanoid), A-pose or T-pose, export FBX with the rig.

| File name (glb/fbx) | Item | Stand-in now |
|---|---|---|
| env_sandbag_wall | sandbag wall section | burlap_sacks |
| env_sandbag_pile | loose sandbag pile | burlap_sacks |
| env_shell_crater | artillery shell crater | none (w2: terrain dent + decal) |
| env_burnt_car | burnt-out civilian car | car_wreck_beetle |
| veh_bmp2_wreck | destroyed BMP-2 IFV | arx_apc |
| veh_btr80_burnt | burnt BTR-80 APC | apc_8x8 |
| veh_t72 | T-72B3 main battle tank | m1a2_tank |
| air_su25 | Su-25 attack jet | f16_jet |
| air_mi24 | Mi-24 attack helicopter | utility_helicopter |
| air_mi8 | Mi-8/Mi-17 transport helicopter (evac) | ch47_helicopter |
| sol_kz_soldier | Kazakh soldier, rigged | sniper_soldier / Erlan (Mixamo) |
| sol_enemy_soldier | enemy soldier, neutral uniform, rigged | sniper_soldier recoloured |
| prop_spent_casings | scatter of spent 7.62x39 casings | ammo_556 |
| prop_litter | folding military litter (stretcher) | stretcher (ambulance gurney, tinted) |
| marker | black permanent marker (if marker_card is one mesh) | procedural |
| casualty_card | TCCC casualty card alone | procedural |

env_sandbag_wall: Military sandbag wall section, 3 meters long and 1.2 meters high, five staggered rows of filled woven polypropylene sandbags in faded olive and khaki, sagging realistic shapes with folded tied ends, dusty and dirty with dried mud stains, some bags slightly torn with sand spilling, realistic fortification, PBR textures, game-ready, flat bottom

env_sandbag_pile: Loose pile of 8 filled military sandbags, faded olive and tan woven fabric, irregular sagging shapes, dusty with mud stains, one bag torn and leaking sand, realistic battlefield cover, PBR textures, game-ready, flat bottom

env_shell_crater: Fresh artillery shell crater in dry steppe soil, 6 meters wide and 1 meter deep, raised rim of thrown dirt and clods, scorched black center, scattered small rocks and torn dry grass on the rim, realistic terrain piece with flat outer edge blending to ground, PBR textures, game-ready

env_burnt_car: Burnt-out Soviet sedan car Lada 2107, completely burned, rusty orange and black scorched metal, no paint left, no glass, melted tires on bare steel rims, collapsed seats springs visible, doors slightly open, realistic war wreck, PBR textures, game-ready

veh_bmp2_wreck: Destroyed BMP-2 infantry fighting vehicle, tracked, burnt dark grey-brown hull with scorched paint, turret with 30mm autocannon tilted, one track broken and lying on the ground, open rear doors, soot stains and rust, debris around, no national markings, realistic war wreck, PBR textures, game-ready

veh_btr80_burnt: Burnt BTR-80 armored personnel carrier, 8 wheels with burnt tires, scorched olive paint turned rusty brown and black, small turret with heavy machine gun, open hatches, soot and rust, no national markings, realistic war wreck, PBR textures, game-ready

veh_t72: T-72B3 main battle tank, olive green with dust and mud, explosive reactive armor blocks on hull and turret, 125mm gun, tracks and road wheels, smoke grenade launchers, no national markings, realistic military vehicle, PBR textures, game-ready, separate turret if possible

air_su25: Sukhoi Su-25 ground attack jet aircraft, grey-green camouflage, straight high wings with rocket pods and bombs under wings, twin engines along fuselage, cockpit canopy, landing gear retracted, no national markings, realistic military aircraft, PBR textures, game-ready

air_mi24: Mil Mi-24 attack helicopter, green-brown camouflage, tandem stepped cockpits, stub wings with rocket pods, five-blade main rotor and tail rotor, retractable landing gear, no national markings, realistic military helicopter, PBR textures, game-ready, rotors as separate parts if possible

air_mi8: Mil Mi-8 / Mi-17 transport helicopter, olive green military paint, large cabin with round windows, rear clamshell doors, five-blade main rotor and tail rotor, fixed tricycle landing gear, external fuel tanks, red cross panel on the side, no national markings, realistic, PBR textures, game-ready, rotors as separate parts if possible

sol_kz_soldier: Modern Kazakh army soldier, full body, multicam-style olive digital camouflage uniform, olive plate carrier with magazine pouches and IFAK pouch, ballistic helmet with cover, tactical gloves, knee pads, combat boots, Central Asian face, holding nothing (hands open), A-pose, realistic, PBR textures, game-ready, rigged humanoid

sol_enemy_soldier: Modern soldier, full body, plain grey-green uniform without any flags or insignia, dark plate carrier with pouches, dark helmet, balaclava, gloves, combat boots, holding nothing (hands open), A-pose, realistic, PBR textures, game-ready, rigged humanoid

prop_spent_casings: Small scatter of about 20 spent brass rifle casings 7.62x39, some steel lacquered green casings, lying on dirt, realistic, PBR textures, game-ready

marker: Black permanent marker with cap, realistic, PBR textures, real-world scale 14 cm, game-ready

casualty_card: TCCC casualty card DD Form 1380, tan waterproof card with printed body diagram and fields, with grommet and string, realistic, PBR textures, real-world scale 14x9 cm, game-ready

prop_litter: Folding military field litter stretcher, olive drab canvas bed stretched between two aluminium poles with rubber grip ends, four short metal stirrup legs, folding spreader bars, worn and dusty with dried blood stains, realistic, PBR textures, real-world scale 2.2 m long, game-ready
