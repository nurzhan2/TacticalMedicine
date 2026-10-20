# Art import — real models for the IFAK items (w9)

The medical items are built in code (`Assets/Scripts/Runtime/Treatment/*Item.cs`): the procedural look lives in a child
called **`Visual`**, the logic (grab points, tip, colliders, moving procedural parts such as the CAT band on a limb)
lives on the root and its other children. An imported model only replaces what is inside `Visual`, so new art never
touches the logic.

## 1. Drop folder (w9d: the Astra deliveries)
```
SourceAssets/Incoming/<folder>/<Name>.fbx | .glb | .gltf  +  Textures/*BaseColor*, *Normal*, *Roughness*, *Metallic*, *AmbientOcclusion*.png
```
One folder per delivery; a delivery may hold **several items** and **several states** of an item. The folder is git-ignored;
the imported result under `Assets/Art/Items/<folder>/` (FBX, URP materials, packed textures) and
`Assets/Resources/Art/<item_id>.prefab` is what the game uses. Folder → item mapping lives in code
(`ArtModels.ByKind`, field `source`):

| folder | item_id | game item | objects used (carried state **bold**, others kept inactive for the item's states) |
|---|---|---|---|
| `tq` | `tourniquet` | CAT | **TQ_Stowed**; TQ_Open/Windlass, WindlassClip, TimeTab, Buckle grafted onto the band on a limb |
| `shears` | `shears` | trauma shears | **BladeA** (turns about the screw), **BladeB, Screw, Carabiner** |
| `israeli` | `israeli_bandage` | Israeli bandage | **Pack**; in the hand Pack_Open (stacked onto the pack); Pad, Wrap, PressureBar, Clip kept |
| `gauze` | `hemostatic_gauze` | hemostatic gauze | **Pack_Closed**; in the hand Pack_OpenBottom + Gauze_Roll |
| `chestseal` | `chest_seal` | vented chest seal | **Pack**; Seal + Liner grafted onto the torn-out seal, liner peels |
| `npa` | `npa` | NPA 28 Fr | **Package + Tube** (package stacked around the tube); in the hand Tube |
| `npa` | `lubricant` | lube sachet | **LubeSachet** |
| `needle` | `needle` | 14 G needle | **Case + Cap + Needle**; in the hand Needle |
| `gloves` | `gloves` | nitrile gloves | **Pack_Closed**; in the hand Pack_Open |
| `marker` | `marker` | marker | **Body, Cap** (cap posted on the back end while held) |
| `blanket` | `blanket` | rescue blanket | **Blanket_Pack** (the sheet is procedural, draped on the body) |
| `small` | `casualty_card`, `tri_bandage`, `eye_shield`, `wipes`, `tape` | card, triangular bandage, eye shield (+pack), wipes, duct tape | **CasualtyCard**, **TriBandage_Pack**, **EyeShield_Pack** (+EyeShield), **Wipes_Pack**, **Tape_Roll** |
| `alpha` | `alpha_tq` | elastic "Alpha" tourniquet | **Alpha_Stowed**; Hook_1/2 grafted onto the fixed band |
| `esmarch` | `esmarch_tq` | Esmarch tourniquet | **Esmarch_Stowed**; Ball_1/2 grafted onto the fixed band |
| `splint` | `splint` | roll splint | **Splint_Rolled** (Splint_Flat kept) |
| `ifak` | `ifak` | the player's kit | Body, Flap, InnerPanel_Upper/Lower(+wings, extension), QuickPouch, BackPanel, PullTab, **Slot_*** empties = item seats |

Then in Unity: **FieldMedic → Art → Import Incoming** (or batch:
`-executeMethod FieldMedic.EditorTools.ArtImporter.ImportIncomingBatch`). Report: `logs/art_import.txt`.

Required names (`ArtModels.ByKind[...].parts`) are matched case-insensitively; an exact name wins, export suffixes are
accepted (`Strap.001`, `Strap_LOD0`), other states are not suffixes (`Pack_Open` is not `Pack`).
If a required name is missing the importer **does not change anything in the game** and lists what is missing in
the report and the Unity console — rename the objects in Blender and import again.

What the importer does per folder (w9d):
- **FBX re-export from the .blend** for folders in `ArtImporter.ReexportFromBlend` (`ifak`): Astra exported FMVR_IFAK.fbx with
  *Apply Transform*, which moved nested children (InnerPanel_Lower, LowerExtension and their Slot_* empties). A temporary
  COPY of the .blend is re-exported by `tools/art_reexport_fbx.py` (headless Blender); the delivery is untouched.
- **Textures**: `tools/art_pack_textures.py` writes `<folder>_BaseColor`, `<folder>_Normal` and `<folder>_Mask`
  (R metallic, G AO, A smoothness = 1 − roughness) at ≤ 1024 into `Assets/Art/Items/<folder>/Textures`; Android ASTC 6×6 ≤ 1024.
- **Materials**: one URP/Lit per material name of the FBX (mask used as metallic-smoothness AND occlusion map);
  alpha blend for `chestseal`, `needle`, `npa` (gel / clear plastic), cutout two-sided for `IFAK_Mesh`, two-sided blanket.
- **Axes**: Astra's front (Blender −Y) arrives as −Z in Unity; `Spec.euler` turns each model into the item frame
  (long side Z, printed front up +Y); `Spec.stack` re-poses states that were laid out side by side.
- **Kit**: `IfakArt` folds the delivered open pose closed (wings ±90° about Y, extension and flap 180° about X), the pull tab
  rips it open, the quick pouch rides on the side when closed; items sit on seats under the `Slot_*` empties
  (`IfakArt.Layout`). Settled (closed or open) the kit is drawn as one combined mesh per material (2 draws; the kit FBX is
  imported readable for this).
- **Budget** (`QuestSceneAudit.ArtRules`, run by every build): ≤ 4 000 tris per item (all states), ≤ 8 000 kit, ≤ 60 000
  total, ≤ 2 materials per renderer, textures ASTC ≤ 1024, no mesh colliders. Current: 20 prefabs, 31 198 tris, 16 materials.

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
