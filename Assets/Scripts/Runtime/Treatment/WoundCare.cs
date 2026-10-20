using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Hands-on care of one wound: palm/fist pressure (pressure value, 3-minute hold after packing with an
    /// optional training speed-up), hemostatic gauze fed in by fingertip push strokes (visible gauze in the
    /// wound), the Israeli bandage state, wiping the area before a chest seal, and the cut-open clothing.
    /// Lives on the <see cref="WoundSite"/> (+Z = outward normal).
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class WoundCare : MonoBehaviour
    {
        public const float PressureThreshold = 0.35f;
        /// <summary>w10d: outside the exam the palm presses / the gauze starts packing anywhere within ~12 cm of the wound.</summary>
        public const float ForgivingPressureRadius = 0.12f;
        public const float ForgivingPackRadius = 0.12f;
        /// <summary>w10d: fingertip packing strokes count within this lateral distance outside the exam.</summary>
        public const float ForgivingStrokeRadius = 0.10f;

        public CasualtyController casualty;
        public WoundSite site;
        public PackingProcedure Packing { get; private set; }
        public BandageProcedure Bandage { get; private set; } = new BandageProcedure();
        public ChestSealItem Seal { get; set; }
        public int Wipes { get; private set; }
        public float Pressure { get; private set; }
        public float HoldSeconds => Wound != null ? Wound.packingHoldSeconds : 0f;
        public Wound Wound => site != null ? site.Wound : null;
        public bool CanPack => Wound != null && !Wound.IsChest && Wound.type != WoundType.Capillary;
        public bool SkinAccessible => Wound != null && (Wound.exposed || !Wound.hiddenUnderClothing);
        public Vector3 Normal => transform.forward;
        public Feedback LastFeedback { get; private set; }

        float _framePressure, _releasedFor;
        bool _holding;
        readonly float[] _strokeDepth = new float[2];
        readonly bool[] _inStroke = new bool[2];
        readonly float[] _wipePath = new float[2];
        readonly Vector3[] _lastPalm = new Vector3[2];
        Transform _gauzeVis, _bandageVis, _exposedSkin;
        TextMesh _hint;
        int _wrapsShown = -1;

        public void Bind(CasualtyController c, WoundSite s)
        {
            casualty = c;
            site = s;
            Packing = new PackingProcedure(s.Wound != null ? s.Wound.type : WoundType.Venous);
            if (s.Wound != null && !s.Wound.hiddenUnderClothing) s.Wound.exposed = true;
        }

        /// <summary>Called by hands each frame they press here (palm, fist, knee proxy). 0..1.</summary>
        public void AddPressure(float pressure01) => _framePressure = Mathf.Max(_framePressure, Mathf.Clamp01(pressure01));

        /// <summary>Signed distance of a point above the wound surface along the normal (negative = pressed in).</summary>
        public float Height(Vector3 p) => Vector3.Dot(p - transform.position, Normal);
        public float Lateral(Vector3 p) => Vector3.ProjectOnPlane(p - transform.position, Normal).magnitude;

        void Update()
        {
            if (Wound == null || casualty == null) return;
            float dt = Time.deltaTime;
            float sdt = dt * Mathf.Max(0f, casualty.simulationSpeed);
            Pressure = _framePressure;
            _framePressure = 0f;

            // ---- direct pressure / hold after packing
            if (Pressure >= PressureThreshold)
            {
                _holding = true;
                _releasedFor = 0f;
                if (casualty.Protocol.HoldPressure(Wound, sdt, GameSettings.HoldTimeScale))
                    LastFeedback = new Feedback(Severity.Ok, "hold_done", "Давление удержано 3 минуты. Закрепите давящей повязкой.");
                casualty.Pain.AddPressure(Pressure * 0.5f, dt);
            }
            else if (_holding)
            {
                _releasedFor += dt;
                if (_releasedFor > 1f)
                {
                    _holding = false;
                    if (Wound.control == BleedControl.Packed && Wound.packingHoldSeconds < Wound.PackingHoldRequired)
                        casualty.Protocol.PressureReleasedEarly(Wound);
                }
            }

            if (Forgiving.On) ForgivingAssist();
            DetectStrokesAndWipes();
            UpdateVisuals();
        }

        // ------------------------------------------------------------------ w10d forgiving zones
        GauzeItem _assistGauze;
        int _glowFrame = -1;
        float _glowStrength;

        /// <summary>Glow on the wound this frame (hands / gauze near it). Called by <see cref="HandTreatment"/> too.</summary>
        public void ShowTargetGlow(float strength)
        {
            if (!Forgiving.On || site == null) return;
            if (_glowFrame == Time.frameCount && strength <= _glowStrength) return;
            _glowFrame = Time.frameCount;
            _glowStrength = strength;
            float size = Mathf.Max(0.1f, site.size * 2.2f);
            ZoneGlow.Show(this, transform.position + Normal * 0.01f, size, strength >= 0.6f ? ZoneGlow.Green : ZoneGlow.Red, strength);
        }

        bool NeedsHands => Wound != null && Wound.discovered && (Wound.posterior == false || casualty.rolledOnSide) &&
                           (!Wound.IsControlled || (Wound.control == BleedControl.Packed && Wound.packingHoldSeconds < Wound.PackingHoldRequired));

        /// <summary>Outside the exam: a held gauze anywhere near the wound starts the packing (the item's own check
        /// needs its tip within 5 cm), and the bleeding wound glows while a hand / the gauze approaches it.</summary>
        void ForgivingAssist()
        {
            bool needsHands = NeedsHands;
            for (int i = 0; i < HandGrabber.All.Count && i < 2; i++)
            {
                var h = HandGrabber.All[i];
                if (h == null || h.Input == null) continue;
                Vector3 palm = h.PalmPosition;
                if (h.Held is GauzeItem g)
                {
                    if (!CanPack || Packing.Complete) continue;
                    Vector3 tipP = g.TipPosition;
                    float lat = Mathf.Min(Lateral(tipP), Lateral(palm));
                    float hgt = Mathf.Min(Mathf.Abs(Height(tipP)), Mathf.Abs(Height(palm)));
                    if (lat < 0.3f && SkinAccessible) ShowTargetGlow(lat < ForgivingPackRadius ? 0.8f : 0.45f);
                    if (g.PackingInto != null || !SkinAccessible) continue;
                    if (lat < ForgivingPackRadius && hgt < 0.08f && !Packing.started && StartPacking())
                    {
                        _assistGauze = g;
                        Haptics.Play(h.Input, Haptics.Grab);
                    }
                }
                else if (h.Held == null && needsHands && Lateral(palm) < 0.3f && Mathf.Abs(Height(palm)) < 0.2f)
                    ShowTargetGlow(0.45f);
            }
        }

        // ------------------------------------------------------------------ packing
        /// <summary>The gauze end is placed into the wound (from <see cref="GauzeItem"/>).</summary>
        public bool StartPacking()
        {
            if (!CanPack || Packing.Complete) return false;
            if (!SkinAccessible) return false; // gauze cannot go through trousers: cut them first
            if (Packing.started) return true;
            Packing.Start();
            return true;
        }

        void DetectStrokesAndWipes()
        {
            for (int i = 0; i < HandGrabber.All.Count && i < 2; i++)
            {
                var h = HandGrabber.All[i];
                if (h == null || h.Input == null) continue;
                // packing: fingertip pushes gauze into the channel; the hand may hold the gauze roll, nothing else
                bool free = h.Held == null || h.Held is GauzeItem;
                Vector3 f = h.FingertipPosition;
                float depth = -Height(f);
                bool easy = Forgiving.On;
                bool over = Lateral(f) < Mathf.Max(easy ? ForgivingStrokeRadius : 0.045f, site.size * 0.8f);
                if (free && over && Packing.started && !Packing.Complete && depth > (easy ? 0.008f : 0.012f))
                {
                    _inStroke[i] = true;
                    _strokeDepth[i] = Mathf.Max(_strokeDepth[i], depth);
                }
                else if (_inStroke[i] && (depth < 0.004f || !over))
                {
                    _inStroke[i] = false;
                    // w10d: outside the exam any clear push into the wound counts as a full stroke
                    float used = Packing.Stroke(easy ? Mathf.Max(_strokeDepth[i] * 100f, 3f) : _strokeDepth[i] * 100f);
                    _strokeDepth[i] = 0f;
                    if (used > 0f)
                    {
                        Haptics.Play(h.Input, Haptics.Contact, 2f);
                        casualty.Pain.AddSpike(0.12f); // packing an open wound hurts a lot
                        if (h.Held is GauzeItem g) g.Consume(used);
                        else
                        {
                            GauzeItem into = null;
                            foreach (var gi in FindObjectsByType<GauzeItem>(FindObjectsSortMode.None)) if (gi.PackingInto == this) { into = gi; break; }
                            if (into == null && _assistGauze != null && _assistGauze.PackingInto == null && !_assistGauze.Consumed) into = _assistGauze;
                            if (into != null) into.Consume(used);
                        }
                        if (Packing.Complete) OnPacked();
                    }
                }

                // wiping: open palm swept across the area (before a chest seal) — or the antiseptic wipes from the kit (w9d)
                bool open = (h.Held == null && h.Input.Grip < 0.5f) || (h.Held is PropItem wipe && wipe.kind == MedItemKind.AntisepticWipes);
                Vector3 palm = h.PalmPosition;
                bool near = open && Lateral(palm) < 0.1f && Mathf.Abs(Height(palm)) < 0.06f;
                if (near) _wipePath[i] += Vector3.ProjectOnPlane(palm - _lastPalm[i], Normal).magnitude;
                _lastPalm[i] = palm;
                if (_wipePath[i] >= 0.08f) { _wipePath[i] = 0f; Wipes++; Haptics.Play(h.Input, Haptics.Contact); }
            }
        }

        void OnPacked()
        {
            LastFeedback = casualty.Protocol.Perform(MedAction.HemostaticPacking, Wound);
            foreach (var gi in FindObjectsByType<GauzeItem>(FindObjectsSortMode.None))
                if (gi.PackingInto == this) gi.FinishPacking();
            // w10d: the roll that started the packing from the forgiving zone (never bound by its own 5 cm check)
            if (_assistGauze != null && _assistGauze.PackingInto == null && !_assistGauze.Consumed) _assistGauze.FinishPacking();
            _assistGauze = null;
        }

        // ------------------------------------------------------------------ bandage
        /// <summary>Israeli bandage finished: dressing over the wound (from <see cref="BandageItem"/>).</summary>
        public Feedback FinishBandage()
        {
            LastFeedback = casualty.Protocol.Perform(MedAction.PressureDressing, Wound);
            return LastFeedback;
        }

        /// <summary>Limb axis through the wound (for wrapping); body axis for torso wounds.</summary>
        public Vector3 WrapAxis(out Vector3 center, out float radius)
        {
            var rig = casualty.rig;
            string id = Wound.LimbId;
            if (id != null && rig != null)
            {
                var limb = LimbMath.FromId(id);
                float pos = Mathf.Max(0.08f, Wound.LimbPos);
                center = LimbMath.PointAt(rig, limb, pos);
                radius = LimbMath.RadiusAt(limb, pos) + 0.01f;
                return LimbMath.AxisAt(rig, limb, pos);
            }
            var hips = rig != null ? rig.Bone(HumanBodyBones.Hips) : transform;
            center = Vector3.Project(transform.position - hips.position, rig != null ? rig.BodyUp : Vector3.up) + hips.position;
            radius = 0.17f;
            return rig != null ? rig.BodyUp : Vector3.up;
        }

        // ------------------------------------------------------------------ visuals
        public void ShowExposedSkin()
        {
            if (_exposedSkin != null || site == null) return;
            var go = new GameObject("ExposedSkin");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            float s = Mathf.Max(0.12f, site.size * 2.4f);
            go.transform.localScale = new Vector3(s, s * 1.6f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = BloodPoolManager.Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ProceduralTex.ExposedSkinMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _exposedSkin = go.transform;
            // the wound decal draws above the skin patch
            if (site.decal != null) site.decal.transform.localPosition = new Vector3(0f, 0f, 0.0045f);
        }

        void UpdateVisuals()
        {
            // gauze visibly filling the wound channel, the tail left outside
            if (Packing.started && _gauzeVis == null)
            {
                _gauzeVis = new GameObject("PackedGauze").transform;
                _gauzeVis.SetParent(transform, false);
                var white = Kit.Mat("GauzeWhite", Kit.Gauze, 0.15f);
                var soaked = Kit.Mat("GauzeSoaked", new Color(0.36f, 0.03f, 0.03f), 0.55f);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 1.05f;
                    var p = new Vector3(Mathf.Cos(a) * 0.012f * (i % 3), Mathf.Sin(a) * 0.012f * (i % 3), 0.004f);
                    Kit.Part(_gauzeVis, PrimitiveType.Sphere, "Fold" + i, p, new Vector3(0.022f, 0.02f, 0.012f), i % 2 == 0 ? soaked : white,
                        Quaternion.Euler(0f, 0f, a * 57f));
                }
                Kit.Part(_gauzeVis, PrimitiveType.Cube, "Tail", new Vector3(0.0f, -0.02f, 0.012f), new Vector3(0.022f, 0.04f, 0.004f), white,
                    Quaternion.Euler(35f, 0f, 0f));
            }
            if (_gauzeVis != null) _gauzeVis.localScale = Vector3.one * Mathf.Lerp(0.35f, 1.15f, Packing.Fill);

            // Israeli bandage: pad + wraps around the limb, pressure bar, closure
            if (Bandage.step != BandageProcedure.Step.None)
            {
                if (_bandageVis == null) BuildBandageVisual();
                int wraps = Mathf.FloorToInt(Bandage.Wraps);
                if (wraps != _wrapsShown) { _wrapsShown = wraps; RebuildWraps(wraps); }
                PlaceWraps();
            }

            // optional training hint: hold timer next to the wound
            bool showHint = GameSettings.Hints && Wound.control == BleedControl.Packed && (Pressure > 0f || _holding);
            if (showHint && _hint == null)
            {
                _hint = Kit.Text(transform, "HoldHint", "", 0.004f, new Color(1f, 0.85f, 0.4f), new Vector3(0f, 0.06f, 0.06f), Quaternion.identity);
            }
            if (_hint != null)
            {
                _hint.gameObject.SetActive(showHint);
                if (showHint)
                {
                    float t = Wound.packingHoldSeconds;
                    _hint.text = $"{(int)(t / 60)}:{(int)(t % 60):00} / 3:00";
                    if (Camera.main != null) _hint.transform.rotation = Quaternion.LookRotation(_hint.transform.position - Camera.main.transform.position);
                }
            }
        }

        void BuildBandageVisual()
        {
            _bandageVis = new GameObject("IsraeliBandage").transform;
            _bandageVis.SetParent(transform, false);
            var pad = Kit.Mat("BandagePad", new Color(0.85f, 0.84f, 0.78f), 0.1f);
            var bar = Kit.Mat("BandageBar", new Color(0.12f, 0.12f, 0.12f), 0.3f);
            Kit.Part(_bandageVis, PrimitiveType.Cube, "Pad", new Vector3(0f, 0f, 0.008f), new Vector3(0.09f, 0.1f, 0.014f), pad);
            Kit.Part(_bandageVis, PrimitiveType.Cube, "PressureBar", new Vector3(0f, 0f, 0.018f), new Vector3(0.05f, 0.016f, 0.008f), bar).name = "PressureBar";
            var wraps = new GameObject("Wraps").transform;
            wraps.SetParent(_bandageVis.parent, false);
            wraps.name = "BandageWraps";
            wraps.SetParent(transform, false);
        }

        void RebuildWraps(int count)
        {
            var root = transform.Find("BandageWraps");
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            var cream = Kit.Mat("BandageWrap", new Color(0.78f, 0.76f, 0.66f), 0.12f);
            WrapAxis(out _, out float r);
            for (int i = 0; i < count; i++)
                Kit.MeshPart(root, "Wrap" + i, Kit.Ring(r + 0.003f * i, 0.1f, 0.003f, 24), cream,
                    new Vector3(0f, 0f, (i - count * 0.5f) * 0.012f), Quaternion.identity, Vector3.one);
            var barT = _bandageVis.Find("PressureBar");
            if (barT != null) barT.gameObject.SetActive(Bandage.step >= BandageProcedure.Step.PressureBar);
        }

        void PlaceWraps()
        {
            var root = transform.Find("BandageWraps");
            if (root == null) return;
            Vector3 axis = WrapAxis(out Vector3 c, out _);
            root.SetPositionAndRotation(c, Quaternion.LookRotation(axis, Normal));
            root.localScale = Vector3.one;
            var barT = _bandageVis != null ? _bandageVis.Find("PressureBar") : null;
            if (barT != null) barT.gameObject.SetActive(Bandage.step >= BandageProcedure.Step.PressureBar);
        }
    }
}
