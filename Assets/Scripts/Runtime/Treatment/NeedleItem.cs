using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// 14G 3.25" decompression needle-catheter. Landmarks are felt with the free hand's fingertips (rib edges
    /// tick in the controller as the fingers move down from the clavicle). Insert perpendicular to the chest
    /// wall at the 2nd intercostal space (mid-clavicular line) or 5th (anterior axillary line), all the way:
    /// hiss of air under tension, the catheter stays in. Medial to the nipple line / through a seal = error.
    /// </summary>
    public sealed class NeedleItem : MedItem
    {
        public NeedleSite LastSite { get; private set; } = NeedleSite.Other;
        public float LastAngle { get; private set; }
        public float DepthCm { get; private set; }
        public Feedback LastFeedback { get; private set; }
        public bool Inserted { get; private set; }

        Transform _needle;
        bool _engaged, _evaluated;
        Vector3 _entry, _normal;
        float _catheterTimer = -1f;
        CasualtyController _c;
        // palpation (rib clicks) by the other hand
        float _lastRibCoord = float.NaN;

        public override void BuildVisual()
        {
            Body.mass = 0.015f;
            var hub = Kit.Mat("NeedleHub", new Color(0.95f, 0.45f, 0.08f), 0.5f); // 14G = orange
            var steel = Kit.Mat("Steel", new Color(0.75f, 0.76f, 0.78f), 0.85f, 0.9f);
            var cath = Kit.Mat("Catheter", new Color(0.85f, 0.8f, 0.65f), 0.7f);
            Kit.Part(transform, PrimitiveType.Cylinder, "Hub", new Vector3(0f, 0f, -0.035f), new Vector3(0.013f, 0.014f, 0.013f), hub, Quaternion.Euler(90f, 0f, 0f));
            Kit.Part(transform, PrimitiveType.Cylinder, "Flash", new Vector3(0f, 0f, -0.06f), new Vector3(0.01f, 0.012f, 0.01f), cath, Quaternion.Euler(90f, 0f, 0f));
            Kit.Part(transform, PrimitiveType.Cylinder, "Catheter", new Vector3(0f, 0f, 0.016f), new Vector3(0.0035f, 0.038f, 0.0035f), cath, Quaternion.Euler(90f, 0f, 0f));
            Kit.Merge(transform);
            _needle = Kit.Part(transform, PrimitiveType.Cylinder, "Needle", new Vector3(0f, 0f, 0.02f), new Vector3(0.0018f, 0.042f, 0.0018f), steel, Quaternion.Euler(90f, 0f, 0f)).transform;
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
            tip.localPosition = new Vector3(0f, 0f, 0.062f);
        }

        public override bool CanBeGrabbedBy(HandGrabber hand) => !Attached && base.CanBeGrabbedBy(hand);

        void Update()
        {
            if (_catheterTimer >= 0f)
            {
                _catheterTimer -= Time.deltaTime;
                if (_catheterTimer < 0f) LeaveCatheter();
                return;
            }
            if (Attached || !IsHeld) { _engaged = false; return; }
            _c = NearestCasualty(TipPosition, 1.2f);
            if (_c == null || _c.rig == null) return;
            Palpate();
            Insert();
        }

        // ------------------------------------------------------------------ landmarks
        /// <summary>Skin (vest) points of the decompression sites and the sternum, from the current pose.</summary>
        public static bool Landmark(CasualtyRig rig, NeedleSite site, bool right, out Vector3 p, out Vector3 n)
        {
            Vector3 fwd = rig.BodyForward, r = rig.BodyRight * (right ? 1f : -1f);
            switch (site)
            {
                case NeedleSite.SecondIcsMcl:
                    if (rig.SurfacePoint(HumanBodyBones.Chest, HumanBodyBones.Neck, 0.8f, (fwd + r * 0.45f).normalized, out p, out n)) return true;
                    break;
                case NeedleSite.FifthIcsAal:
                    if (rig.SurfacePoint(HumanBodyBones.Chest, HumanBodyBones.Neck, 0.15f, (r + fwd * 0.35f).normalized, out p, out n)) return true;
                    break;
                default:
                    if (rig.SurfacePoint(HumanBodyBones.Chest, HumanBodyBones.Neck, 0.5f, fwd, out p, out n)) return true;
                    break;
            }
            var chest = rig.ChestBone;
            Vector3 dir = site == NeedleSite.SecondIcsMcl ? (fwd + r * 0.45f).normalized : site == NeedleSite.FifthIcsAal ? (r + fwd * 0.35f).normalized : fwd;
            float up = site == NeedleSite.SecondIcsMcl ? 0.12f : site == NeedleSite.FifthIcsAal ? -0.02f : 0.05f;
            p = chest.position + rig.BodyUp * up + dir * 0.13f;
            n = dir;
            return false;
        }

        float _cacheTime = -1f;
        readonly Vector3[] _sitePos = new Vector3[5];
        readonly Vector3[] _siteNrm = new Vector3[5];
        static readonly (NeedleSite site, bool right)[] Sites =
        {
            (NeedleSite.SecondIcsMcl, true), (NeedleSite.SecondIcsMcl, false),
            (NeedleSite.FifthIcsAal, true), (NeedleSite.FifthIcsAal, false), (NeedleSite.MedialToNippleLine, true),
        };

        void RefreshLandmarks()
        {
            if (Time.time - _cacheTime < 1f && _cacheTime >= 0f) return; // mesh baking is not free
            _cacheTime = Time.time;
            for (int i = 0; i < Sites.Length; i++) Landmark(_c.rig, Sites[i].site, Sites[i].right, out _sitePos[i], out _siteNrm[i]);
        }

        void Insert()
        {
            var chest = _c.rig.ChestBone;
            if (chest == null || Vector3.Distance(TipPosition, chest.position) > 0.35f) { _engaged = false; return; }
            RefreshLandmarks();
            Vector3 t = TipPosition;
            if (!_engaged)
            {
                // the tip touches the chest wall: find which landmark it is on
                int best = -1; float bd = 0.035f;
                for (int i = 0; i < Sites.Length; i++)
                {
                    float lat = Vector3.ProjectOnPlane(t - _sitePos[i], _siteNrm[i]).magnitude;
                    float h = Vector3.Dot(t - _sitePos[i], _siteNrm[i]);
                    if (h < 0.01f && h > -0.02f && lat < bd) { bd = lat; best = i; }
                }
                Vector3 sternum = _sitePos[4];
                if (best < 0)
                {
                    // touching the chest somewhere else
                    float hs = Vector3.Dot(t - sternum, _siteNrm[4]);
                    float ls = Vector3.ProjectOnPlane(t - sternum, _siteNrm[4]).magnitude;
                    if (hs < 0.01f && hs > -0.02f && ls < 0.14f)
                    {
                        best = 4;
                        LastSite = Mathf.Abs(Vector3.Dot(t - sternum, _c.rig.BodyRight)) < 0.06f ? NeedleSite.MedialToNippleLine : NeedleSite.Other;
                    }
                }
                else LastSite = Sites[best].site;
                if (best < 0) return;
                _engaged = true;
                _evaluated = false;
                _entry = best == 4 ? t : _sitePos[best];
                _normal = _siteNrm[best];
                Haptics.Play(HolderInput, Haptics.Contact, 2f);
            }
            DepthCm = Mathf.Max(0f, -Vector3.Dot(t - _entry, _normal) * 100f);
            LastAngle = Vector3.Angle(transform.forward, -_normal);
            if (DepthCm < -1f || Vector3.ProjectOnPlane(t - _entry, _normal).magnitude > 0.05f) { _engaged = false; return; }
            Haptics.Tension(HolderInput, Mathf.Clamp01(DepthCm / 6f) * 0.5f);
            if (_evaluated || DepthCm < NeedleRules.MinDepthCm) return;
            _evaluated = true;
            bool throughSeal = false;
            foreach (var s in _c.WoundSites)
            {
                var care = s != null ? s.GetComponent<WoundCare>() : null;
                if (care != null && care.Seal != null && care.Seal.Attached && Vector3.Distance(care.transform.position, _entry) < 0.07f) throughSeal = true;
            }
            bool tension = _c.Patient.pneumo == PneumoState.Tension;
            LastFeedback = _c.Protocol.NeedleDecompression(LastSite, LastAngle, DepthCm, throughSeal);
            _c.Pain.AddSpike(0.5f);
            if (LastFeedback.code == "decompressed")
            {
                Inserted = true;
                if (tension) TreatmentAudio.Hiss(_entry, 1f);
                Haptics.Play(HolderInput, Haptics.PressClick);
                _catheterTimer = 2.5f; // hold 5–10 s in reality; then the needle comes out, the catheter stays
            }
        }

        void LeaveCatheter()
        {
            var chest = _c != null && _c.rig != null ? _c.rig.ChestBone : null;
            ForceRelease();
            if (chest == null) return;
            AttachTo(chest);
            transform.position = _entry - _normal * 0.025f;
            transform.rotation = Quaternion.LookRotation(-_normal);
            if (_needle != null) _needle.gameObject.SetActive(false);
        }

        /// <summary>Fingertips of the other hand sliding down the chest: a tick per rib (clavicle → 2nd ICS).</summary>
        void Palpate()
        {
            foreach (var h in HandGrabber.All)
            {
                if (h == null || h.Held != null || h == Holder || h.Input == null) continue;
                var chest = _c.rig.ChestBone;
                Vector3 f = h.FingertipPosition;
                if (Vector3.Distance(f, chest.position) > 0.3f) continue;
                RefreshLandmarks();
                Vector3 sp = _sitePos[4], sn = _siteNrm[4];
                if (Vector3.Dot(f - sp, sn) > 0.025f) continue; // not touching
                float coord = Vector3.Dot(f - chest.position, _c.rig.BodyUp);
                float rib = Mathf.Floor(coord / 0.025f);
                if (!float.IsNaN(_lastRibCoord) && rib != _lastRibCoord) Haptics.Play(h.Input, Haptics.Contact, 2.5f);
                _lastRibCoord = rib;
            }
        }
    }
}
