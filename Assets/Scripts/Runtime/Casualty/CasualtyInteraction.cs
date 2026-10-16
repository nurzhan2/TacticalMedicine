using System.Collections.Generic;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// Everything the player's hands can do to the casualty's body: limb handles (ankle, knee, wrist), the
    /// plate-carrier drag handle, log-roll handles (shoulder + hip each side), per-wound care components,
    /// and his own IFAK (CAT + dressing) on the plate carrier. Built at runtime from the rig, so the prefab
    /// stays a pure character asset. Also drives his reflex of grabbing the rescuer's arm when it hurts.
    /// </summary>
    [DefaultExecutionOrder(52)]
    public sealed class CasualtyInteraction : MonoBehaviour
    {
        public CasualtyController casualty;
        [Tooltip("Contents of his IFAK (TCCC: use the casualty's tourniquet, not yours).")]
        public MedItemKind[] ifakContents = { MedItemKind.Tourniquet, MedItemKind.IsraeliBandage, MedItemKind.HemostaticGauze };
        public bool buildIfak = true;

        public readonly List<LimbHandle> LimbHandles = new List<LimbHandle>();
        public readonly List<TourniquetCAT> Tourniquets = new List<TourniquetCAT>();
        public CasualtyDrag Drag { get; private set; }
        public LogRoll Roll { get; private set; }
        public IfakPouch Ifak { get; private set; }
        public bool Built { get; private set; }
        /// <summary>Body regions whose clothing has been cut open with shears.</summary>
        public readonly HashSet<BodyRegion> Exposed = new HashSet<BodyRegion>();
        public bool IsExposed(BodyRegion r) => Exposed.Contains(r);

        /// <summary>Clothing cut over a region: its wounds are now visible and reachable at skin level.</summary>
        public void Expose(BodyRegion r)
        {
            if (!Exposed.Add(r)) return;
            foreach (var s in casualty.WoundSites)
            {
                if (s == null || s.Wound == null) continue;
                var wr = s.Wound.region;
                bool same = wr == r || (r is BodyRegion.ThighL && wr == BodyRegion.GroinL) || (r is BodyRegion.ThighR && wr == BodyRegion.GroinR)
                            || (r is BodyRegion.GroinL && wr == BodyRegion.ThighL) || (r is BodyRegion.GroinR && wr == BodyRegion.ThighR);
                if (!same) continue;
                s.Wound.exposed = true;
                casualty.Protocol.Discover(s.Wound);
                var care = s.GetComponent<WoundCare>();
                if (care != null) care.ShowExposedSkin();
            }
        }

        float _armGrabUntil;
        Transform _armGrabTarget;
        Limb _armGrabLimb;

        void Awake() { if (casualty == null) casualty = GetComponent<CasualtyController>(); }

        void Start() => Build();

        public void Build()
        {
            if (Built || casualty == null || casualty.rig == null) return;
            var rig = casualty.rig;
            rig.Init();
            if (rig.Bone(HumanBodyBones.Hips) == null) return;
            Built = true;

            AddLimbHandle(Limb.RightLeg, LimbHandle.Grip.Ankle, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.85f, 0.075f);
            AddLimbHandle(Limb.LeftLeg, LimbHandle.Grip.Ankle, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.85f, 0.075f);
            AddLimbHandle(Limb.RightLeg, LimbHandle.Grip.Knee, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.92f, 0.07f);
            AddLimbHandle(Limb.LeftLeg, LimbHandle.Grip.Knee, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.92f, 0.07f);
            AddLimbHandle(Limb.RightArm, LimbHandle.Grip.Wrist, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.85f, 0.06f);
            AddLimbHandle(Limb.LeftArm, LimbHandle.Grip.Wrist, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.85f, 0.06f);

            // drag handle on the back of the plate carrier
            var handleT = FindDeep(transform, "DragHandle_GrabPoint") ?? FindDeep(transform, "DragHandle");
            Transform chest = rig.ChestBone;
            Vector3 hp = handleT != null ? handleT.position : chest.position - rig.BodyForward * 0.17f + rig.BodyUp * 0.12f;
            Drag = ProxyHandle.Create<CasualtyDrag>("Grab_DragHandle", handleT != null ? handleT : chest, hp, 0.07f, 3);
            Drag.casualty = casualty;
            // w7: both shoulder straps of the plate carrier are grips too (second hand / two-hand drag)
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 sp = chest.position + rig.BodyUp * 0.17f + rig.BodyRight * side * 0.11f + rig.BodyForward * 0.07f;
                var strap = ProxyHandle.Create<ProxyHandle>(side < 0 ? "Grab_StrapL" : "Grab_StrapR", chest, sp, 0.055f, 2);
                strap.loadWhileHeld = 0.6f;
                Drag.strapHandles.Add(strap);
            }

            // log roll: shoulder + hip on each side
            Roll = gameObject.AddComponent<LogRoll>();
            Roll.casualty = casualty;
            Roll.shoulderL = RollHandle("Roll_ShoulderL", HumanBodyBones.LeftUpperArm, -1f);
            Roll.shoulderR = RollHandle("Roll_ShoulderR", HumanBodyBones.RightUpperArm, 1f);
            Roll.hipL = HipHandle("Roll_HipL", HumanBodyBones.LeftUpperLeg, -1f);
            Roll.hipR = HipHandle("Roll_HipR", HumanBodyBones.RightUpperLeg, 1f);

            if (buildIfak) BuildIfak();
            if (buildPatches) AddPatches();
            SyncWoundCare();
        }

        [Tooltip("GDD_V2 §4: KZ flag on the left shoulder, Satbayev military department patch on the right.")]
        public bool buildPatches = true;
        [Tooltip("Distance of the shoulder patches from the upper-arm bone axis (sleeve surface).")]
        public float patchRadius = 0.062f;

        void AddPatches()
        {
            ShoulderPatch(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, -1f, FieldMedic.UI.Insignia.KzFlag, new Vector2(0.08f, 0.04f));
            ShoulderPatch(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 1f, FieldMedic.UI.Insignia.Satbayev, new Vector2(0.052f, 0.065f));
        }

        void ShoulderPatch(HumanBodyBones bone, HumanBodyBones child, float side, string id, Vector2 size)
        {
            var rig = casualty.rig;
            var b = rig.Bone(bone); var c = rig.Bone(child);
            if (b == null || c == null) return;
            Vector3 dir = (c.position - b.position).normalized;
            Vector3 outward = Vector3.ProjectOnPlane(rig.BodyRight * side, dir).normalized;
            if (outward.sqrMagnitude < 0.5f) return;
            Vector3 pos = Vector3.Lerp(b.position, c.position, 0.3f) + outward * patchRadius;
            FieldMedic.UI.Insignia.Patch(b, id, pos, outward, -dir, size); // top of the patch toward the shoulder
        }

        void AddLimbHandle(Limb limb, LimbHandle.Grip grip, HumanBodyBones bone, HumanBodyBones child, float along, float radius)
        {
            var b = casualty.rig.Bone(bone); var c = casualty.rig.Bone(child);
            if (b == null || c == null) return;
            var h = ProxyHandle.Create<LimbHandle>("Grab_" + limb + "_" + grip, b, Vector3.Lerp(b.position, c.position, along), radius, 1);
            h.casualty = casualty;
            h.limb = limb;
            h.grip = grip;
            h.loadWhileHeld = grip == LimbHandle.Grip.Knee ? 0.3f : 0.5f;
            LimbHandles.Add(h);
        }

        ProxyHandle RollHandle(string name, HumanBodyBones bone, float side)
        {
            var b = casualty.rig.Bone(bone);
            if (b == null) return null;
            // top of the shoulder, on the outside
            Vector3 p = b.position + casualty.rig.BodyRight * side * 0.06f;
            return ProxyHandle.Create<ProxyHandle>(name, b, p, 0.08f, 1);
        }

        ProxyHandle HipHandle(string name, HumanBodyBones bone, float side)
        {
            var b = casualty.rig.Bone(bone);
            var hips = casualty.rig.Bone(HumanBodyBones.Hips);
            if (b == null || hips == null) return null;
            Vector3 p = b.position + casualty.rig.BodyRight * side * 0.09f;
            return ProxyHandle.Create<ProxyHandle>(name, hips, p, 0.08f, 1);
        }

        void BuildIfak()
        {
            var ifakT = FindDeep(transform, "IFAK");
            if (ifakT == null) return;
            // the static CAT model of the prefab is replaced by a real, grabbable one
            var old = FindDeep(ifakT, "CAT_Erlan");
            Vector3 catPos = old != null ? old.position : ifakT.position;
            Quaternion catRot = old != null ? old.rotation : ifakT.rotation;
            if (old != null) old.gameObject.SetActive(false);
            var pouchGo = new GameObject("IFAK_Contents");
            pouchGo.transform.SetParent(ifakT, false);
            var ls = ifakT.lossyScale; // FBX scale must not scale the items
            pouchGo.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, ls.x), 1f / Mathf.Max(1e-4f, ls.y), 1f / Mathf.Max(1e-4f, ls.z));
            Ifak = pouchGo.AddComponent<IfakPouch>();
            Ifak.buildVisual = false;
            Ifak.contents = ifakContents;
            Ifak.slotSpacing = 0.045f;
            Ifak.slotOrigin = pouchGo.transform.InverseTransformPoint(ifakT.position + casualty.rig.BodyUp * 0.05f);
            Ifak.Build();
            foreach (var it in Ifak.Items)
            {
                if (it is TourniquetCAT cat)
                {
                    cat.transform.SetPositionAndRotation(catPos, catRot);
                    cat.homeSlot.SetPositionAndRotation(catPos, catRot);
                    cat.owner = this;
                }
            }
        }

        /// <summary>Every wound site gets a care component (pressure, packing, dressing, seal...).</summary>
        public void SyncWoundCare()
        {
            foreach (var s in casualty.WoundSites)
            {
                if (s == null || s.GetComponent<WoundCare>() != null) continue;
                var care = s.gameObject.AddComponent<WoundCare>();
                care.Bind(casualty, s);
            }
        }

        public WoundCare Care(Wound w)
        {
            foreach (var s in casualty.WoundSites)
                if (s != null && s.Wound == w) return s.GetComponent<WoundCare>();
            return null;
        }

        public LimbHandle Handle(Limb limb, LimbHandle.Grip grip) => LimbHandles.Find(h => h.limb == limb && h.grip == grip);

        public void Register(TourniquetCAT cat) { if (!Tourniquets.Contains(cat)) Tourniquets.Add(cat); }

        /// <summary>Pain reflex: he grabs the rescuer's forearm with his nearest free hand.</summary>
        public void GrabPlayerArm(Transform hand, float seconds)
        {
            if (hand == null || casualty.Patient.AVPU > Consciousness.Voice) return;
            var rig = casualty.rig;
            float dl = Vector3.Distance(rig.Bone(HumanBodyBones.LeftHand).position, hand.position);
            float dr = Vector3.Distance(rig.Bone(HumanBodyBones.RightHand).position, hand.position);
            var limb = dl < dr ? Limb.LeftArm : Limb.RightArm;
            var other = limb == Limb.LeftArm ? Limb.RightArm : Limb.LeftArm;
            if (!ArmFree(limb)) limb = other;
            if (!ArmFree(limb)) return; // both arms wounded or held: no grab
            _armGrabLimb = limb;
            _armGrabTarget = hand;
            _armGrabUntil = Time.time + seconds;
        }

        /// <summary>An arm that can reach for the medic: not held by the player and not wounded (a wounded arm stays still).</summary>
        bool ArmFree(Limb limb)
        {
            var held = Handle(limb, LimbHandle.Grip.Wrist);
            if (held != null && held.Held) return false;
            string id = limb == Limb.LeftArm ? "armL" : "armR";
            var wounds = casualty.Patient.wounds;
            for (int i = 0; i < wounds.Count; i++)
                if (BodyRegions.LimbId(wounds[i].region) == id) return false;
            return true;
        }

        public bool GrabbingPlayer => _armGrabTarget != null && Time.time < _armGrabUntil;
        /// <summary>Which of his arms reaches for the medic, and where his hand aims (the medic's wrist).</summary>
        public Limb ArmGrabLimb => _armGrabLimb;
        public Transform ArmGrabTarget => _armGrabTarget;
        public Vector3 ArmGrabPoint => _armGrabTarget != null ? _armGrabTarget.position - _armGrabTarget.forward * 0.05f : Vector3.zero;

        void Update()
        {
            if (!Built) { Build(); return; }
            if (casualty.WoundSites.Count > 0) SyncWoundCare();
            UpdateArmGrab();
        }

        float _armWeight;

        void UpdateArmGrab()
        {
            var rig = casualty.rig;
            bool active = GrabbingPlayer;
            var handle = Handle(_armGrabLimb, LimbHandle.Grip.Wrist);
            if (handle != null && handle.Held) return; // the player holds that arm: LimbHandle owns the IK
            _armWeight = Mathf.MoveTowards(_armWeight, active ? 0.85f : 0f, Time.deltaTime * (active ? 4f : 1.5f));
            if (_armWeight <= 0f)
            {
                if (_armGrabTarget != null) { rig.ClearLimbIK(_armGrabLimb); _armGrabTarget = null; }
                return;
            }
            if (_armGrabTarget == null) return;
            // his hand closes around the rescuer's wrist, just behind the glove
            Vector3 target = ArmGrabPoint;
            rig.SetLimbIK(_armGrabLimb, target, _armWeight);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
