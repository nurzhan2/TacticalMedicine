# Stage w9d - put the new GPT-6 Astra item models into the game

Branch `w9` (after w9a/w9b/w9c). Same guard rails as w9a (XR guard in the build, Always Included shaders,
Quest budget 72 fps, no face from w8, do not push). Owner, 20.10 03:10: "на рабочем столе все нужные зипки,
сам раскидай, доделай игру за ночь. Модель солдата и руки НЕ используй, я их переделаю".
=> Do NOT import or use FMVR_Hands / FMVR_Soldier_KZ (zips on the Desktop) in any way.

## What arrived
`SourceAssets/Incoming/<folder>/` (already unpacked by the supervisor, one folder per zip). Each folder has
`<Name>.fbx`, `.glb`, `.blend`, `Textures/` (BaseColor, Normal OpenGL, Roughness, Metallic, AmbientOcclusion,
sometimes a Unity metallic-smoothness map), `Previews/*.png` (LOOK at them), `Report.txt` and
`validation.json` from the generator (READ them: real object names, sizes, triangle counts, deviations).
The prompts they were generated from: docs/ASTRA_PROMPTS (the artifact text is mirrored below in short).

| folder | file | what | objects the prompt asked for |
|---|---|---|---|
| tq | FMVR_TQ_Windlass | windlass tourniquet | TQ_Stowed (folded, for the kit) + TQ_Open parts Strap (edge loop every 1 cm), Buckle, Windlass (origin centre), WindlassClip, TimeTab ("ВРЕМЯ:") |
| shears | FMVR_Shears | trauma shears | BladeA, BladeB (origins on the screw axis), Screw, Carabiner |
| israeli | FMVR_IsraeliBandage | Israeli bandage 6" | Pack (closed), Pack_Open, Pad, Wrap (+40 cm tail), PressureBar, Clip |
| gauze | FMVR_HemoGauze | hemostatic gauze | Pack_Closed, Pack_OpenTop, Pack_OpenBottom, Gauze_Roll, Gauze_Strip (60 cm, 1 cm loops) |
| chestseal | FMVR_ChestSeal | vented chest seal | Pack, Seal (round, valve), Liner |
| npa | FMVR_NPA | NPA 28 Fr | Tube (origin flange, axis +Y), Package, LubeSachet |
| needle | FMVR_DecompNeedle | 14G needle | Case, Cap, Needle (origin hub, axis +Y) |
| gloves | FMVR_GlovesPack | nitrile gloves pack | Pack_Closed, Pack_Open |
| marker | FMVR_Marker | marker | Body (origin tip), Cap |
| blanket | FMVR_RescueBlanket | rescue blanket | Blanket_Pack, Blanket_Sheet (210x160, 3 cm grid, gold/silver) |
| ifak | FMVR_IFAK | rip-away 2-echelon kit | Body, Flap (hinge origin), InnerPanel_Upper/Lower (hinge origins), QuickPouch, BackPanel, PullTab, empties Slot_* (Slot_TQ, Slot_Shears, Slot_Gauze, Slot_Israeli, Slot_Splint, Slot_ChestSeal_1/2, Slot_NPA, Slot_Needle, Slot_Marker, Slot_Gloves, Slot_Blanket, Slot_Tape, Slot_AlphaTQ, Slot_Esmarch, Slot_Card) |
| alpha | FMVR_TQ_Alpha | orange elastic "Альфа" tourniquet | Alpha_Stowed, Alpha_Open: Band (1 cm loops), Hook_1, Hook_2 |
| esmarch | FMVR_TQ_Esmarch | Esmarch rubber tourniquet | Esmarch_Stowed, Esmarch_Open: Band, Ball_1, Ball_2 |
| splint | FMVR_Splint | roll splint | Splint_Rolled, Splint_Flat (2 cm loops) |
| small | FMVR_SmallItems | small items | TriBandage_Pack, EyeShield_Pack, EyeShield, Wipes_Pack, Tape_Roll, CasualtyCard |

## Tasks
1. Map these deliveries onto the w9a art pipeline (ArtImporter / ArtModels / docs/ART_IMPORT.md). The importer
   specs (item ids, required sub-part names, "one model per folder") were written BEFORE the models existed:
   adapt the CODE to the delivered names (aliases per item, multi-state files: pick the stowed/closed object
   for the in-kit look, keep the other objects available for states), do not edit the models in Blender
   unless something is really broken (wrong scale/axis, missing object) - if you must, use headless Blender
   on a COPY and note it. Folder->item id mapping lives in code (e.g. `tq`->tourniquet, `israeli`->israeli_bandage,
   `gauze`->hemostatic_gauze, `chestseal`->chest_seal, `small/CasualtyCard`->casualty_card,
   `npa/LubeSachet`->lubricant).
2. Use the real models in the game for everything that already exists: tourniquet (stowed in the quick pouch;
   if feasible use the delivered Windlass/WindlassClip/TimeTab/Buckle meshes on the procedural strap while it is
   applied), shears (blades open/close around the screw when cutting), Israeli bandage, hemostatic gauze, chest
   seal (+ Seal and Liner for the applied state if feasible), NPA, needle, gloves pack, marker (cap comes off),
   rescue blanket (pack; the unfolded sheet may use Blanket_Sheet as the cloth mesh if it performs), casualty
   card, lubricant sachet. Procedural fallbacks stay for anything the import cannot satisfy.
3. The kit: replace the w9a two-echelon kit visual with FMVR_IFAK (Body/Flap/InnerPanels open by hinge,
   QuickPouch holds TQ + shears, BackPanel stays on the vest, PullTab rips it off). Use the Slot_* empties as
   the item slots (w9a slot logic: in-slot = parented + kinematic; release near -> back to slot; elsewhere ->
   falls to the ground). Keep the w9a tests green.
4. New items that the game did not have (the owner wants every real kit item modelled and in the kit):
   - Alpha elastic tourniquet and Esmarch tourniquet: real usable items. Put them in their slots. Allow applying
     them on a limb above the wound as an alternative tourniquet (wrap 3+ turns while pulling, then fix with
     hooks / balls) - simpler than the CAT but must actually stop the bleeding in the physiology model; debrief
     accepts them as a tourniquet, with the note that the windlass TQ is preferred for a thigh.
   - Splint (rolled): grabbable, in its slot; unrolling is not needed now (no fracture scenario yet) - a short
     hint when picked: "Шина - при переломе. В этом сценарии не нужна." / KZ equivalent.
   - Triangular bandage pack, eye shield (+pack), antiseptic wipes, duct tape roll: grabbable props in their
     slots (wipes can be used on the chest-seal "wipe the blood" step if that step exists).
   - No fake functionality beyond this.
5. Performance check on Quest: total kit+items tris and materials within QuestSceneAudit limits, textures ASTC
   <= 1024, no mesh colliders. Fix anything over budget (LOD / texture size).
6. Screenshots (Editor capture) into docs/screens/w9_art_*.png: open kit with all items in slots, each tourniquet
   type applied, shears cutting, chest seal applied.
7. Tests green, QuestSceneAudit passes, Android build with the XR guard line and libUnityOpenXR.so in the APK;
   copy to `C:\Users\user\Desktop\FMVR_DEMO\FieldMedicVR_W9_ART.apk`; if the headset is connected (adb
   `device`), install and smoke-test as in w9c (screencap only via adb shell + pull).
8. Update docs/W9_WHATS_NEW.md (Russian, for the owner) with what the new models changed and anything the
   owner must fix in Astra (list per model: what is wrong, what to ask Astra). Commit, append to
   docs/PROGRESS.md, create logs/w9d-art.done.
