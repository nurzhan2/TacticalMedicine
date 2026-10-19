# Art import — real models for the IFAK items (w9)

The medical items are built in code (`Assets/Scripts/Runtime/Treatment/*Item.cs`): the procedural look lives in a child
called **`Visual`**, the logic (grab points, tip, colliders, moving procedural parts such as the CAT band on a limb)
lives on the root and its other children. An imported model only replaces what is inside `Visual`, so new art never
touches the logic.

## 1. Drop folder
```
SourceAssets/Incoming/<item_id>/<anything>.fbx | .glb | .gltf   (+ textures next to it, png/jpg/tga)
```
One folder per item, one model file per folder. The folder is git-ignored (licensed / large sources); the imported
result under `Assets/Art/Items/<item_id>/` is what the game uses.

Then in Unity: **FieldMedic → Art → Import Incoming** (or batch:
`-executeMethod FieldMedic.EditorTools.ArtImporter.ImportIncomingBatch`). Report: `logs/art_import.txt`.

| item_id | item | real size (longest side) | required sub-parts (object names in the model) |
|---|---|---|---|
| `tourniquet` | CAT tourniquet (folded, as carried) | 0.17 m | `Strap`, `Windlass`, `WindlassClip`, `TimeTab`, `Buckle` |
| `shears` | trauma shears | 0.19 m | `BladeA`, `BladeB` (pivot of both at the screw) |
| `israeli_bandage` | Israeli bandage (packed) | 0.17 m | `Pad`, `Wrap`, `PressureBar`, `Clip` |
| `hemostatic_gauze` | hemostatic gauze (packed) | 0.14 m | `Pack` |
| `chest_seal` | vented chest seal (package) | 0.20 m | `Package` |
| `npa` | nasopharyngeal airway 28 Fr | 0.15 m | `Tube`, `Flange` |
| `needle` | 14 G decompression needle | 0.16 m | `Needle`, `Hub`, `Cap` |
| `gloves` | nitrile gloves pair (folded) | 0.15 m | `Gloves` |
| `marker` | permanent marker | 0.14 m | `Body`, `Cap` |
| `blanket` | thermal blanket (folded) | 0.13 m | `Pack` |
| `casualty_card` | TCCC casualty card | 0.14 m | `Card` |
| `lubricant` | lubricant sachet | 0.06 m | `Sachet` |
| `ifak` | the kit (main pouch) | 0.42 m | `Body`, `Lid` (or `Flap`), `QuickPouch`, `Slots` |

Names are matched case-insensitively and may carry a suffix (`Strap.001`, `Strap_LOD0` are fine).
If a required name is missing the importer **does not change anything in the game** and lists what is missing in
the report and the Unity console — rename the objects in Blender and import again.

## 2. Model rules
- **Units**: 1 unit = 1 m. Export from Blender with *Apply Unit* / scale 1.0 (FBX: "FBX Units Scale").
  The importer checks the size against the table; a model 100× too big/small (cm/m mix-up) is rescaled and noted
  in the report, anything else off by more than 2× is reported as a warning.
- **Axes**: Unity is Y-up, Z-forward. Blender FBX export: *Forward −Z*, *Up Y* (default "Apply Transform" on).
  The item lies flat as carried: longest side along **Z**, thinnest along **Y**.
- **Pivot**: the model origin is the centre of the item as carried (the kit: centre of the bottom of the tray).
  Moving parts have their own origin at the hinge (shear blades at the screw, CAT windlass at its centre, the kit
  lid at the hinge line).
- **Triangles**: items ≤ 3 000 tris each, the kit ≤ 8 000. Up to 2 materials per item.
- **Textures**: PBR metal/rough (albedo, normal, optional metallic-smoothness). Any size — the importer sets
  Android to ASTC, max **1024** for items and the kit, **2048** for characters.
- **No mesh colliders**: the importer removes any collider in the model and adds simple box colliders
  (one box around the item; for the kit one box per required part).

## 3. What the importer does
1. Copies the FBX (or converts glb/gltf with headless Blender, `tools/art_glb2fbx.py`, keeping object names) into
   `Assets/Art/Items/<item_id>/`, textures into `Textures/`.
2. Model import settings: scale 1, no cameras/lights/animation (unless the file has one), read/write off,
   mesh compression medium, normals imported; textures ASTC + size caps for Android.
3. Validates the sub-part names (table above) and the size.
4. Builds `Assets/Resources/Art/<item_id>.prefab`: the model centred like the procedural item, box colliders,
   no mesh colliders.
5. At runtime `ArtModels.Apply(item)` (called by every item when it is created) hides the procedural renderers in
   `Visual` and puts the model there. Logic-driven states that are still procedural (the CAT band wrapped around a
   limb, the opened chest seal, the unfolded blanket, the bandage pad being wrapped) keep their procedural meshes
   until those are rigged too.

The older drop folder `SourceAssets/Meshy/<fixed name>` (docs/MESHY_PROMPTS_V2.md) still works; a model from
`Incoming` wins over a Meshy one for the same item.
