using System.Collections.Generic;
using System.IO;
using System.Linq;
using FieldMedic.Battle;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace FieldMedic.EditorTools.SceneBuilders
{
    /// <summary>
    /// Builds the w3 battle assets from the w1 models (idempotent, re-run safe):
    ///  - soldier prefabs (enemy / friendly tint) with the Animator controller (Mixamo kneel + falls) and the
    ///    procedural <see cref="SoldierPoser"/> whose rifle axis/muzzle are calibrated from the weapon vertices;
    ///  - vehicles with a separately rotating turret (tank, IFV) or main rotor (helicopter) split from the
    ///    single imported mesh by height, forward axis detected from the geometry;
    ///  - crowd meshes: the 1k-triangle soldier baked at 8 run frames + kneel + fallen, merged to one vertex-coloured
    ///    submesh each (instanced at 60–200 m);
    ///  - the horizon impostor run sheet (8 frames rendered from the near soldier);
    ///  - own textures (scorch) and materials.
    /// Anything derived from licensed models goes to Assets/_Licensed/_Battle (git-ignored).
    /// </summary>
    public static class BattleAssets
    {
        public const string Dir = "Assets/Art/Battle";
        public const string LDir = "Assets/_Licensed/_Battle";
        const string BK = "Assets/_Licensed/BlenderKit";
        const string Anims = "Assets/Art/Characters/Erlan/Anims";

        public static string Fbx(string cat, string slug) => $"{BK}/{cat}/{slug}/{slug}.fbx";
        public static string ModelPrefabPath(string cat, string slug) => $"{BK}/{cat}/{slug}/{slug}.prefab";

        static void Folders()
        {
            foreach (var d in new[] { Dir, Dir + "/Textures", Dir + "/Materials", Dir + "/Meshes", LDir, LDir + "/Meshes", LDir + "/Materials" })
                Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
        }

        public static GameObject ModelPrefab(string cat, string slug)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath(cat, slug));
            if (go == null && File.Exists(Fbx(cat, slug)))
            {
                AssetDatabase.ImportAsset(Fbx(cat, slug));
                go = LicensedModelSetup.SetupModel(Fbx(cat, slug));
            }
            if (go == null) Debug.LogWarning($"[FMVR] battle: model {cat}/{slug} missing");
            return go;
        }

        // ================================================================== animator
        static AnimationClip MixamoClip(string name)
        {
            string p = $"{Anims}/mixamo_{name}.fbx";
            return AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
        }

        public static AnimatorController SoldierController()
        {
            string path = Dir + "/SoldierAgent.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            var baseIdle = MixamoClip("Rifle_Crouch_Idle");
            AnimatorState Add(string n, AnimationClip c)
            {
                var s = sm.AddState(n);
                s.motion = c;
                s.writeDefaultValues = true;
                return s;
            }
            var kneel = Add("Kneel", MixamoClip("Rifle_Kneel_Idle"));
            Add("Run", baseIdle);   // overridden by the pose layer
            Add("Stand", baseIdle); // overridden by the pose layer
            Add("Die0", MixamoClip("Shot_Chest_Fall_Back"));
            Add("Die1", MixamoClip("Shoulder_Hit_Fall"));
            Add("Die2", MixamoClip("Knocked_Out_Fall_Back"));
            sm.defaultState = kneel;
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        // ================================================================== rifle calibration
        /// <summary>Finds the weapon vertices (material "Weapon") in the bind pose and expresses the grip→muzzle axis
        /// and the muzzle point in right-hand space.</summary>
        public static bool CalibrateRifle(GameObject inst, out Vector3 axisLocal, out Vector3 muzzleLocal) => CalibrateRifle(inst, out axisLocal, out muzzleLocal, out _);

        public static bool CalibrateRifle(GameObject inst, out Vector3 axisLocal, out Vector3 muzzleLocal, out bool leftHand)
        {
            axisLocal = Vector3.forward; muzzleLocal = new Vector3(0f, 0f, 0.8f); leftHand = false;
            var anim = inst.GetComponentInChildren<Animator>();
            var rightHand = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            var leftHandT = anim != null ? anim.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            if (rightHand == null || leftHandT == null) return false;
            var world = new List<Vector3>();
            // exact: skin the weapon vertices by hand (Σ w·bone·bindpose·v) in the current pose, then express them in
            // right-hand space (the rifle is rigid with the hand whatever bones it is weighted to)
            var weights = new Dictionary<string, int>();
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var bones = smr.bones;
                var bps = mesh.bindposes;
                var bw = mesh.boneWeights;
                var mats = smr.sharedMaterials;
                var v = mesh.vertices;
                var l2w = new Matrix4x4[bones.Length];
                for (int b = 0; b < bones.Length; b++) l2w[b] = bones[b] != null ? bones[b].localToWorldMatrix * bps[b] : Matrix4x4.identity;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    if (mats[s] == null || !mats[s].name.ToLowerInvariant().Contains("weapon")) continue;
                    foreach (int i in mesh.GetIndices(s).Distinct())
                    {
                        var w = bw[i];
                        Vector3 p = l2w[w.boneIndex0].MultiplyPoint3x4(v[i]) * w.weight0 + l2w[w.boneIndex1].MultiplyPoint3x4(v[i]) * w.weight1
                                  + l2w[w.boneIndex2].MultiplyPoint3x4(v[i]) * w.weight2 + l2w[w.boneIndex3].MultiplyPoint3x4(v[i]) * w.weight3;
                        world.Add(p);
                        string bn = bones[w.boneIndex0] != null ? bones[w.boneIndex0].name : "?";
                        weights[bn] = weights.TryGetValue(bn, out int c) ? c + 1 : 1;
                    }
                }
            }
            Debug.Log("[FMVR] battle: weapon weighted to " + string.Join(", ", weights.Select(kv => kv.Key + ":" + kv.Value)));
            // the weapon hand = whichever hand bone carries the rifle (by name of the dominant bone)
            string top = weights.Count > 0 ? weights.OrderByDescending(kv => kv.Value).First().Key : "";
            leftHand = top == leftHandT.name || (top != rightHand.name && (top.EndsWith(".l") || top.ToLowerInvariant().Contains("left")));
            var hand = leftHand ? leftHandT : rightHand;
            var pts = world.Select(w => hand.InverseTransformPoint(w)).ToList();
            if (pts.Count < 10) { Debug.LogWarning("[FMVR] battle: no weapon vertices found"); return false; }
            // muzzle = farthest from the hand; butt = farthest from the muzzle
            Vector3 muzzle = pts.OrderByDescending(p => p.sqrMagnitude).First();
            Vector3 butt = pts.OrderByDescending(p => (p - muzzle).sqrMagnitude).First();
            axisLocal = (muzzle - butt).normalized;
            muzzleLocal = muzzle;
            Debug.Log($"[FMVR] battle: rifle length {hand.TransformVector(muzzle - butt).magnitude:F2} m (world), muzzle {muzzle} (hand space), axis {axisLocal}, hand scale {hand.lossyScale}");
            return true;
        }

        // ================================================================== soldiers
        static Material Tinted(Material src, string suffix, Color mul, float smooth = 0.6f)
        {
            string path = $"{LDir}/Materials/{src.name}_{suffix}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else m.CopyPropertiesFromMaterial(src);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor") * mul);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", src.GetFloat("_Smoothness") * smooth);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Enemy: neutral dark grey-olive, no insignia. Friendly: warmer olive/khaki.</summary>
        public static Color TeamTint(Team t) => t == Team.Enemy ? new Color(0.62f, 0.64f, 0.6f) : new Color(0.86f, 0.82f, 0.62f);

        public static GameObject SoldierPrefab(Team team, AnimatorController ctrl)
        {
            var model = ModelPrefab("soldiers", "soldier_agent");
            if (model == null) return null;
            // w10c: our side (friendly line, self-aid rescuers) wears the owner's KZ soldier, carrying the agent's rifle
            if (team == Team.Friendly && SoldierKZImport.LodReady) return KzFriendlyPrefab(model, ctrl);
            var inst = (GameObject)Object.Instantiate(model);
            try
            {
                inst.name = team == Team.Enemy ? "EnemySoldier" : "FriendlySoldier";
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var lg in inst.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lg);
                GameObjectUtility.SetStaticEditorFlags(inst, 0);
                foreach (Transform t in inst.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                var anim = inst.GetComponentInChildren<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.quality = SkinQuality.Bone2;
                    smr.updateWhenOffscreen = false;
                    smr.shadowCastingMode = ShadowCastingMode.Off;
                    smr.receiveShadows = true;
                    smr.sharedMaterials = smr.sharedMaterials.Select(m => m == null ? null : m.name.ToLowerInvariant().Contains("weapon") ? m : Tinted(m, team.ToString(), TeamTint(team))).ToArray();
                }
                CalibrateRifle(inst, out var axis, out var muzzle, out bool left);
                var poser = inst.AddComponent<SoldierPoser>();
                poser.animator = anim;
                poser.rifleAxisLocal = axis;
                poser.muzzleLocal = muzzle;
                poser.rifleInLeftHand = left;
                var agent = inst.AddComponent<SoldierAgent>();
                agent.team = team;
                agent.animator = anim;
                agent.poser = poser;
                string path = $"{LDir}/{inst.name}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(inst, path);
            }
            finally { Object.DestroyImmediate(inst); }
        }

        /// <summary>w10c: the KZ soldier (LOD1) with the BlenderKit agent's rifle as a rigid mesh in the grip hand.</summary>
        static GameObject KzFriendlyPrefab(GameObject agentModel, AnimatorController ctrl)
        {
            var agent = (GameObject)Object.Instantiate(agentModel);
            var inst = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SoldierKZImport.LodFbx));
            try
            {
                inst.name = "FriendlySoldier";
                var aAnim = agent.GetComponentInChildren<Animator>();
                var kAnim = inst.GetComponentInChildren<Animator>();
                // the rifle vertices of the agent in its bind pose (world) + which hand holds it
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
                Material weaponMat = null;
                var weights = new Dictionary<Transform, int>();
                foreach (var smr in agent.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh;
                    if (mesh == null) continue;
                    var bones = smr.bones; var bps = mesh.bindposes; var bw = mesh.boneWeights; var mats = smr.sharedMaterials;
                    var v = mesh.vertices; var uv = mesh.uv;
                    var l2w = new Matrix4x4[bones.Length];
                    for (int b = 0; b < bones.Length; b++) l2w[b] = bones[b] != null ? bones[b].localToWorldMatrix * bps[b] : Matrix4x4.identity;
                    for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                    {
                        if (mats[s] == null || !mats[s].name.ToLowerInvariant().Contains("weapon")) continue;
                        weaponMat = mats[s];
                        var map = new Dictionary<int, int>();
                        foreach (int i in mesh.GetIndices(s))
                        {
                            if (!map.TryGetValue(i, out int ni))
                            {
                                var w = bw[i];
                                Vector3 p = l2w[w.boneIndex0].MultiplyPoint3x4(v[i]) * w.weight0 + l2w[w.boneIndex1].MultiplyPoint3x4(v[i]) * w.weight1
                                          + l2w[w.boneIndex2].MultiplyPoint3x4(v[i]) * w.weight2 + l2w[w.boneIndex3].MultiplyPoint3x4(v[i]) * w.weight3;
                                ni = verts.Count; map[i] = ni;
                                verts.Add(p); uvs.Add(uv != null && uv.Length > i ? uv[i] : Vector2.zero);
                                var bt = bones[w.boneIndex0];
                                if (bt != null) weights[bt] = weights.TryGetValue(bt, out int c) ? c + 1 : 1;
                            }
                            tris.Add(ni);
                        }
                    }
                }
                if (verts.Count < 10) Debug.LogWarning("[FMVR] battle: KZ friendly - no rifle found on the agent");
                var top = weights.Count > 0 ? weights.OrderByDescending(kv => kv.Value).First().Key : null;
                bool left = top != null && top == aAnim.GetBoneTransform(HumanBodyBones.LeftHand);
                var aHand = aAnim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var kHand = kAnim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                // same grip: move the rifle from the agent's hand to the soldier's hand (both models face +Z)
                Vector3 shift = kHand.position - aHand.position;
                var local = verts.Select(p => kHand.InverseTransformPoint(p + shift)).ToArray();
                var rifleMesh = new Mesh { name = "SoldierKZ_Rifle" };
                rifleMesh.vertices = local; rifleMesh.uv = uvs.ToArray(); rifleMesh.SetTriangles(tris, 0);
                rifleMesh.RecalculateNormals(); rifleMesh.RecalculateBounds();
                string meshPath = LDir + "/Meshes/SoldierKZ_Rifle.asset";
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(rifleMesh, meshPath);
                var rifle = new GameObject("Rifle");
                rifle.transform.SetParent(kHand, false);
                rifle.AddComponent<MeshFilter>().sharedMesh = rifleMesh;
                var rr = rifle.AddComponent<MeshRenderer>();
                rr.sharedMaterial = weaponMat;
                rr.shadowCastingMode = ShadowCastingMode.Off;
                // muzzle = farthest from the hand; butt = farthest from the muzzle (same rule as CalibrateRifle)
                Vector3 axis = Vector3.forward, muzzle = new Vector3(0f, 0f, 0.8f);
                if (local.Length > 10)
                {
                    muzzle = local.OrderByDescending(p => p.sqrMagnitude).First();
                    Vector3 butt = local.OrderByDescending(p => (p - muzzle).sqrMagnitude).First();
                    axis = (muzzle - butt).normalized;
                }
                kAnim.runtimeAnimatorController = ctrl;
                kAnim.applyRootMotion = false;
                kAnim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.quality = SkinQuality.Bone2;
                    smr.updateWhenOffscreen = false;
                    smr.shadowCastingMode = ShadowCastingMode.Off;
                    smr.receiveShadows = true;
                }
                var poser = inst.AddComponent<SoldierPoser>();
                poser.animator = kAnim;
                poser.rifleAxisLocal = axis;
                poser.muzzleLocal = muzzle;
                poser.rifleInLeftHand = left;
                var sa = inst.AddComponent<SoldierAgent>();
                sa.team = Team.Friendly;
                sa.animator = kAnim;
                sa.poser = poser;
                Debug.Log($"[FMVR] battle: KZ friendly, rifle {verts.Count} verts in the {(left ? "left" : "right")} hand, axis {axis:F2}, muzzle {muzzle:F2}");
                return PrefabUtility.SaveAsPrefabAsset(inst, LDir + "/" + inst.name + ".prefab");
            }
            finally { Object.DestroyImmediate(inst); Object.DestroyImmediate(agent); }
        }

        // ================================================================== vehicle splitting
        public sealed class SplitResult
        {
            public GameObject prefab;
            public Vector3 muzzleLocal;
            public float length;
        }

        static MeshFilter BestLod(GameObject model, int want)
        {
            var mfs = model.GetComponentsInChildren<MeshFilter>(true);
            return mfs.FirstOrDefault(m => m.name.EndsWith("_LOD" + want)) ?? mfs.FirstOrDefault(m => m.name.EndsWith("_LOD0")) ?? mfs.FirstOrDefault();
        }

        /// <summary>Splits a vehicle mesh into a body and a part above <paramref name="splitFrac"/> of its height (turret
        /// or rotor; <paramref name="radialLimit"/> keeps only the part within that horizontal radius of the top's
        /// centre). Builds a prefab: root (forward = +Z) → Model → Body, Part (pivot at the part's centre).</summary>
        public static SplitResult SplitVehicle(string cat, string slug, string name, int lod, float splitFrac, Color tint, string forwardHint, float radialLimit = 99f)
        {
            var model = ModelPrefab(cat, slug);
            if (model == null) return null;
            var mf = BestLod(model, lod);
            var mr = mf.GetComponent<MeshRenderer>();
            var src = mf.sharedMesh;
            var v = src.vertices; var n = src.normals; var uv = src.uv;
            // object space of the LOD mesh → model root space
            Matrix4x4 toRoot = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var pv = v.Select(p => toRoot.MultiplyPoint3x4(p)).ToArray();
            var pn = n.Length == v.Length ? n.Select(p => toRoot.MultiplyVector(p).normalized).ToArray() : null;
            var b = new Bounds(pv[0], Vector3.zero);
            foreach (var p in pv) b.Encapsulate(p);
            float cut = b.min.y + splitFrac * b.size.y;
            {
                var hist = new int[10];
                for (int s0 = 0; s0 < src.subMeshCount; s0++)
                {
                    var idx0 = src.GetTriangles(s0);
                    for (int t = 0; t < idx0.Length; t += 3)
                    {
                        float cy = (pv[idx0[t]].y + pv[idx0[t + 1]].y + pv[idx0[t + 2]].y) / 3f;
                        hist[Mathf.Clamp((int)((cy - b.min.y) / b.size.y * 10f), 0, 9)]++;
                    }
                }
                Debug.Log($"[FMVR] battle: {slug} height histogram (10 bins, {b.size.y:F2} m): {string.Join(",", hist)}; size {b.size}");
            }

            // first pass: centre of the top part (for the radial limit)
            Vector3 topC = Vector3.zero; int topN = 0;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var idx = src.GetTriangles(s);
                for (int t = 0; t < idx.Length; t += 3)
                {
                    Vector3 c = (pv[idx[t]] + pv[idx[t + 1]] + pv[idx[t + 2]]) / 3f;
                    if (c.y > cut) { topC += c; topN++; }
                }
            }
            if (topN == 0) { Debug.LogWarning($"[FMVR] battle: split {slug}: nothing above {cut}"); return null; }
            topC /= topN;

            var bodyTris = new List<int>[src.subMeshCount];
            var partTris = new List<int>[src.subMeshCount];
            var partVerts = new HashSet<int>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                bodyTris[s] = new List<int>(); partTris[s] = new List<int>();
                var idx = src.GetTriangles(s);
                for (int t = 0; t < idx.Length; t += 3)
                {
                    Vector3 c = (pv[idx[t]] + pv[idx[t + 1]] + pv[idx[t + 2]]) / 3f;
                    float rad = new Vector2(c.x - topC.x, c.z - topC.z).magnitude;
                    bool part = c.y > cut && rad < radialLimit;
                    var list = part ? partTris[s] : bodyTris[s];
                    list.Add(idx[t]); list.Add(idx[t + 1]); list.Add(idx[t + 2]);
                    if (part) { partVerts.Add(idx[t]); partVerts.Add(idx[t + 1]); partVerts.Add(idx[t + 2]); }
                }
            }
            // part pivot: horizontal centre of its vertices at the cut height
            var pb = new Bounds(pv[partVerts.First()], Vector3.zero);
            foreach (int i in partVerts) pb.Encapsulate(pv[i]);
            // median of the part's vertices: the turret ring / rotor mast, not pulled forward by a long barrel
            var xs = partVerts.Select(i => pv[i].x).OrderBy(x => x).ToList();
            var zs = partVerts.Select(i => pv[i].z).OrderBy(z => z).ToList();
            Vector3 pivot = new Vector3(xs[xs.Count / 2], cut, zs[zs.Count / 2]);

            // forward: the barrel sticks out of the turret's front; the tail fin is the jet's highest point at the
            // back; a helicopter's mast sits ahead of the bounds centre (the tail boom is long)
            float sign = 1f;
            if (forwardHint == "barrel") sign = (pb.max.z - pivot.z) >= (pivot.z - pb.min.z) ? 1f : -1f;
            else if (forwardHint == "mast") sign = pivot.z >= b.center.z ? 1f : -1f;

            Mesh Build(List<int>[] tris, Vector3 offset, string suffix)
            {
                var m = new Mesh { name = $"{name}_{suffix}", indexFormat = IndexFormat.UInt32 };
                m.vertices = pv.Select(p => p - offset).ToArray();
                if (pn != null) m.normals = pn;
                if (uv.Length == v.Length) m.uv = uv;
                m.subMeshCount = tris.Length;
                for (int s = 0; s < tris.Length; s++) m.SetTriangles(tris[s], s);
                m.RecalculateBounds();
                // drop unused vertices to keep memory down
                MeshUtility.Optimize(m);
                string path = $"{LDir}/Meshes/{m.name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(m, path);
                return m;
            }
            var bodyMesh = Build(bodyTris, Vector3.zero, "body");
            var partMesh = Build(partTris, pivot, "part");

            var mats = mr.sharedMaterials.Select(m => m == null ? null : Tinted(m, name, tint, 0.5f)).ToArray();
            var root = new GameObject(name);
            try
            {
                var modelT = new GameObject("Model").transform;
                modelT.SetParent(root.transform, false);
                modelT.localRotation = Quaternion.Euler(0f, sign > 0f ? 0f : 180f, 0f);
                var body = new GameObject("Body");
                body.transform.SetParent(modelT, false);
                body.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
                var br = body.AddComponent<MeshRenderer>();
                br.sharedMaterials = mats;
                br.shadowCastingMode = ShadowCastingMode.Off;
                var part = new GameObject("Part");
                part.transform.SetParent(modelT, false);
                part.transform.localPosition = pivot;
                part.AddComponent<MeshFilter>().sharedMesh = partMesh;
                var prr = part.AddComponent<MeshRenderer>();
                prr.sharedMaterials = mats;
                prr.shadowCastingMode = ShadowCastingMode.Off;
                // muzzle: the part vertex farthest along the vehicle's forward, in part space
                Vector3 fwdModel = new Vector3(0f, 0f, sign);
                Vector3 best = Vector3.zero; float bestD = float.MinValue;
                foreach (int i in partVerts)
                {
                    float d = Vector3.Dot(pv[i] - pivot, fwdModel);
                    if (d > bestD) { bestD = d; best = pv[i] - pivot; }
                }
                var res = new SplitResult { muzzleLocal = best, length = b.size.z };
                string path = $"{LDir}/{name}.prefab";
                res.prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[FMVR] battle: split {slug} → {name}: body {bodyTris.Sum(t => t.Count) / 3} tris, part {partTris.Sum(t => t.Count) / 3} tris, cut {cut:F2} m, forward {(sign > 0 ? "+Z" : "-Z")}, muzzle {best}");
                return res;
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>A single-mesh aircraft prefab (LOD1) with forward = +Z (jet: the tail fin is the highest point).</summary>
        public static GameObject Aircraft(string slug, string name, Color tint)
        {
            var model = ModelPrefab("aircraft", slug);
            if (model == null) return null;
            var mf = BestLod(model, 1);
            Matrix4x4 toRoot = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var pv = mf.sharedMesh.vertices.Select(p => toRoot.MultiplyPoint3x4(p)).ToArray();
            var b = new Bounds(pv[0], Vector3.zero);
            foreach (var p in pv) b.Encapsulate(p);
            Vector3 top = pv.OrderByDescending(p => p.y).First();
            float sign = top.z < b.center.z ? 1f : -1f; // fin at the back
            var root = new GameObject(name);
            try
            {
                var m = new GameObject("Model");
                m.transform.SetParent(root.transform, false);
                m.transform.localRotation = Quaternion.Euler(0f, sign > 0f ? 0f : 180f, 0f);
                var g = new GameObject("Mesh");
                g.transform.SetParent(m.transform, false);
                g.transform.localPosition = -b.center;
                var mesh = Object.Instantiate(mf.sharedMesh);
                mesh.name = name + "_mesh";
                mesh.vertices = pv;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                string mp = $"{LDir}/Meshes/{mesh.name}.asset";
                AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(mesh, mp);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterials = mf.GetComponent<MeshRenderer>().sharedMaterials.Select(x => x == null ? null : Tinted(x, name, tint, 0.6f)).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
                return PrefabUtility.SaveAsPrefabAsset(root, $"{LDir}/{name}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ================================================================== crowd + impostors
        static Color AverageColor(Material m)
        {
            Color c = m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.gray;
            var tex = m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") as Texture2D : null;
            if (tex == null) return c;
            var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t2 = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            t2.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
            t2.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color sum = Color.black; float w = 0f;
            foreach (var p in t2.GetPixels()) { if (p.a < 0.3f) continue; sum += p; w += 1f; }
            Object.DestroyImmediate(t2);
            if (w > 0f) sum /= w;
            return new Color(sum.r * c.r, sum.g * c.g, sum.b * c.b, 1f);
        }

        /// <summary>Bakes every skinned renderer of <paramref name="inst"/> into one vertex-coloured mesh (root space).</summary>
        static Mesh BakeColoured(GameObject inst, string name, Color tint, Dictionary<Material, Color> colorCache)
        {
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            var toRoot = inst.transform.worldToLocalMatrix;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                smr.BakeMesh(baked, false);
                var v = baked.vertices; var n = baked.normals;
                var l2w = smr.transform.localToWorldMatrix;
                var mats = smr.sharedMaterials;
                for (int s = 0; s < baked.subMeshCount; s++)
                {
                    var mat = s < mats.Length ? mats[s] : null;
                    // fixed palette by part (texture averaging is unreliable on the editor GPU): dark, neutral uniform
                    string mn = mat != null ? mat.name.ToLowerInvariant() : "";
                    Color col = mn.Contains("weapon") ? new Color(0.12f, 0.12f, 0.12f)
                              : mn.Contains("upper") ? new Color(0.36f, 0.36f, 0.32f)
                              : mn.Contains("bottom") ? new Color(0.3f, 0.3f, 0.27f)
                              : new Color(0.26f, 0.26f, 0.24f);
                    bool weapon = mat != null && mat.name.ToLowerInvariant().Contains("weapon");
                    // palette is in sRGB; vertex colours are used as linear values by the shader
                    Color vc = (weapon ? col * 0.8f : new Color(col.r * tint.r, col.g * tint.g, col.b * tint.b, 1f)).linear;
                    var idx = baked.GetIndices(s);
                    var map = new Dictionary<int, int>();
                    foreach (int i in idx)
                    {
                        if (!map.TryGetValue(i, out int ni))
                        {
                            ni = verts.Count;
                            map[i] = ni;
                            Vector3 w = l2w.MultiplyPoint3x4(v[i]);
                            verts.Add(toRoot.MultiplyPoint3x4(w));
                            norms.Add(toRoot.MultiplyVector(l2w.MultiplyVector(n.Length > i ? n[i] : Vector3.up)).normalized);
                            cols.Add(vc);
                        }
                        tris.Add(ni);
                    }
                }
                Object.DestroyImmediate(baked);
            }
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetNormals(norms); m.SetColors(cols); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static void SampleClip(GameObject inst, string clipName, float normalized)
        {
            var clip = MixamoClip(clipName);
            if (clip == null) return;
            clip.SampleAnimation(inst, clip.length * normalized);
        }

        /// <summary>Crowd meshes: 8 run frames, kneel aim, fallen. Saved under _Licensed/_Battle/Meshes.</summary>
        public static Mesh[] CrowdMeshes()
        {
            var model = ModelPrefab("soldiers", "soldier_far");
            if (model == null) return new Mesh[0];
            var inst = (GameObject)Object.Instantiate(model);
            var result = new List<Mesh>();
            try
            {
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var anim = inst.GetComponentInChildren<Animator>();
                CalibrateRifle(inst, out var axis, out _, out bool left);
                var rig = SoldierPoser.Rig.Capture(anim, inst.transform);
                var cache = new Dictionary<Material, Color>();
                var tint = TeamTint(Team.Enemy);
                Mesh Save(Mesh m)
                {
                    string p = $"{LDir}/Meshes/{m.name}.asset";
                    AssetDatabase.DeleteAsset(p);
                    AssetDatabase.CreateAsset(m, p);
                    return m;
                }
                for (int f = 0; f < 8; f++)
                {
                    SoldierPoser.Apply(rig, SoldierPose.Run, f / 8f * Mathf.PI * 2f, inst.transform, axis, 0f, left);
                    result.Add(Save(BakeColoured(inst, $"Crowd_Run{f}", tint, cache)));
                }
                rig.ResetToBind(false);
                SampleClip(inst, "Rifle_Kneel_Idle", 0.3f);
                SoldierPoser.Apply(rig, SoldierPose.KneelAim, 0f, inst.transform, axis, 0f, left);
                result.Add(Save(BakeColoured(inst, "Crowd_Kneel", tint, cache)));
                rig.ResetToBind(false);
                SampleClip(inst, "Knocked_Out_Fall_Back", 1f);
                result.Add(Save(BakeColoured(inst, "Crowd_Dead", tint, cache)));
            }
            finally { Object.DestroyImmediate(inst); }
            return result.ToArray();
        }

        /// <summary>Renders the near soldier running toward the camera (slight 3/4) at 8 frames into a 1024×256 sheet.</summary>
        public static Texture2D ImpostorSheet()
        {
            string path = $"{LDir}/Impostor_Run.png";
            var model = ModelPrefab("soldiers", "soldier_agent");
            if (model == null) return null;
            const int W = 128, H = 256, Frames = 8;
            var inst = (GameObject)Object.Instantiate(model);
            var camGo = new GameObject("ImpostorCam");
            var lightGo = new GameObject("ImpostorLight");
            bool fog = RenderSettings.fog;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var sheet = new Texture2D(W * Frames, H, TextureFormat.RGBA32, false);
            try
            {
                Vector3 at = new Vector3(0f, -800f, 0f);
                inst.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 200f, 0f)); // toward the camera, a little turned
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.sharedMaterials = smr.sharedMaterials.Select(m => m == null ? null : m.name.ToLowerInvariant().Contains("weapon") ? m : Tinted(m, "Enemy", TeamTint(Team.Enemy))).ToArray();
                var anim = inst.GetComponentInChildren<Animator>();
                CalibrateRifle(inst, out var axis, out _, out bool left);
                var rig = SoldierPoser.Rig.Capture(anim, inst.transform);
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 1.0f;
                cam.aspect = (float)W / H;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 20f;
                cam.transform.SetPositionAndRotation(at + new Vector3(0f, 0.95f, -6f), Quaternion.identity);
                cam.targetTexture = rt;
                cam.enabled = false;
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 0.6f;
                l.transform.rotation = Quaternion.Euler(40f, 30f, 0f);
                RenderSettings.fog = false;
                for (int f = 0; f < Frames; f++)
                {
                    SoldierPoser.Apply(rig, SoldierPose.Run, f / (float)Frames * Mathf.PI * 2f, inst.transform, axis, 0f, left);
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, W, H), f * W, 0);
                    RenderTexture.active = prev;
                }
                sheet.Apply();
                File.WriteAllBytes(path, sheet.EncodeToPNG());
            }
            finally
            {
                RenderSettings.fog = fog;
                Object.DestroyImmediate(inst); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(sheet);
            }
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ================================================================== own textures / materials
        public static Texture2D ScorchTexture()
        {
            string path = Dir + "/Textures/scorch.png";
            if (!File.Exists(path))
            {
                const int N = 256;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    float n = 0f, amp = 0.5f, fr = 4f;
                    for (int o = 0; o < 5; o++) { n += amp * Mathf.PerlinNoise(x / (float)N * fr + o * 5.3f, y / (float)N * fr + o * 1.7f); amp *= 0.5f; fr *= 2f; }
                    // ragged rim + radial streaks of thrown soil
                    float rim = 0.75f + 0.25f * Mathf.PerlinNoise(Mathf.Cos(ang) * 3f + 10f, Mathf.Sin(ang) * 3f + 10f);
                    float streak = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 9f + n * 3f)), 6f) * 0.35f;
                    float a = Mathf.Clamp01((rim + streak - r) * 3.5f) * (0.55f + 0.6f * n);
                    float dark = Mathf.Lerp(0.05f, 0.2f, n);
                    t.SetPixel(x, y, new Color(dark * 1.05f, dark, dark * 0.9f, Mathf.Clamp01(a)));
                }
                t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Material Mat(string name, string shader, bool licensed = false)
        {
            string path = $"{(licensed ? LDir : Dir)}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Mesh GroundQuad()
        {
            string path = Dir + "/Meshes/GroundQuad.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m != null) return m;
            m = new Mesh { name = "GroundQuad" };
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static void Prepare() => Folders();
    }
}
