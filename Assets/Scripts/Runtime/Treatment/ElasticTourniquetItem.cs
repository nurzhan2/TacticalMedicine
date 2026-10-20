using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// w9d: elastic tourniquets from the kit — the orange woven "Alpha" (two hooks) and the pink rubber Esmarch band
    /// (two balls). Simpler than the CAT but still done with the hands:
    /// <list type="number">
    /// <item>Hold the rolled band against the limb ABOVE the wound for a moment → the free end is pinned there.</item>
    /// <item>Circle the limb with the hand holding the roll while pulling it away from the skin: every full circle lays
    /// one turn; only stretched turns compress (hand ≥ ~10 cm off the skin). Thigh 4, upper arm / shin 3, forearm 2.</item>
    /// <item>Bring the roll back onto the turns and squeeze (or let go there) → hooks / balls fix the end.
    /// Letting go anywhere else before that → the elastic springs back off the limb.</item>
    /// <item>Write the time on the band with the marker.</item>
    /// </list>
    /// Occlusion and the rules (partial = venous congestion, joint, below the wound, thigh → CAT preferred) live in
    /// <see cref="TourniquetState"/> (type Elastic) and <see cref="CareProtocol"/>.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class ElasticTourniquetItem : MedItem
    {
        public TourniquetState State = new TourniquetState { type = TourniquetType.Elastic };
        public const float PinSeconds = 0.35f;
        public const float FullStretch = 0.10f;   // hand this far off the skin = band fully stretched
        public const int MaxShownTurns = 7;

        public CasualtyController Casualty { get; private set; }
        public Limb Limb { get; private set; }
        public Feedback LastFeedback { get; private set; }
        public bool IsAlpha => kind == MedItemKind.AlphaTourniquet;
        public bool Fixed => State.Locked;
        /// <summary>Turns laid so far (stretched or not).</summary>
        public int Turns => State.elasticTurnsLaid;
        public Transform TimeLabel { get; private set; }

        Transform _onLimb, _strand, _fixings;
        TextMesh _timeText;
        readonly MeshFilter[] _rings = new MeshFilter[MaxShownTurns];
        Material _bandMat;
        float _pinTimer, _angleAcc, _prevAngle, _maxStretch;
        bool _prevValid;
        Limb _pinLimb;
        float _shownR = -1f;
        int _shownTurns = -1;

        public string Label => IsAlpha ? "Альфа" : "Эсмарх";

        // ------------------------------------------------------------------ visual
        public override void BuildVisual()
        {
            Body.mass = IsAlpha ? 0.07f : 0.09f;
            _bandMat = IsAlpha ? Kit.Mat("AlphaOrange", new Color(0.78f, 0.33f, 0.1f), 0.25f) : Kit.Mat("EsmarchPink", new Color(0.78f, 0.42f, 0.48f), 0.45f);
            var dark = Kit.Mat("HookBlack", new Color(0.05f, 0.05f, 0.05f), 0.4f);
            // as carried: a coiled roll (procedural stand-in for Alpha_Stowed / Esmarch_Stowed)
            var coil = new GameObject("Coil").transform;
            coil.SetParent(transform, false);
            for (int i = 0; i < 5; i++)
                Kit.MeshPart(coil, "Turn" + i, Kit.Ring(0.012f + i * 0.0055f, 0.025f, 0.002f, 18), _bandMat, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
            Kit.Part(coil, PrimitiveType.Cube, IsAlpha ? "Hooks" : "Balls", new Vector3(0f, -0.04f, 0f), new Vector3(0.012f, 0.008f, 0.02f), dark);
            Kit.Merge(coil);

            // on the limb: up to 7 turns (rings rebuilt with the limb radius), the stretched strand to the hand, fixings
            _onLimb = new GameObject("OnLimb").transform;
            _onLimb.SetParent(null, false);
            _onLimb.name = "ElasticOnLimb_" + kind;
            for (int i = 0; i < MaxShownTurns; i++)
            {
                _rings[i] = Kit.MeshPart(_onLimb, "Turn" + i, Kit.Ring(0.06f, 0.025f, 0.002f, 24), _bandMat, Vector3.zero, Quaternion.identity, Vector3.one).GetComponent<MeshFilter>();
                _rings[i].sharedMesh = Instantiate(_rings[i].sharedMesh);
                _rings[i].gameObject.SetActive(false);
            }
            _strand = Kit.Part(_onLimb, PrimitiveType.Cube, "Strand", Vector3.zero, Vector3.one, _bandMat).transform;
            _strand.gameObject.SetActive(false);
            _fixings = new GameObject("Fixings").transform;
            _fixings.SetParent(_onLimb, false);
            Kit.Part(_fixings, PrimitiveType.Cube, "FixA", new Vector3(-0.004f, 0f, 0f), new Vector3(0.006f, 0.004f, 0.012f), dark);
            Kit.Part(_fixings, PrimitiveType.Cube, "FixB", new Vector3(0.006f, 0f, 0f), new Vector3(0.006f, 0.004f, 0.012f), dark);
            _fixings.gameObject.SetActive(false);
            TimeLabel = new GameObject("TimeLabel").transform;
            TimeLabel.SetParent(_onLimb, false);
            _timeText = Kit.Text(TimeLabel, "Text", "", 0.003f, new Color(0.05f, 0.05f, 0.08f), Vector3.zero, Quaternion.Euler(90f, 0f, 90f));
            _onLimb.gameObject.SetActive(false);
            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
        }

        public override void OnArtApplied(Transform art)
        {
            // the delivered hooks / balls go onto the fixed end of the band on the limb
            string a = IsAlpha ? "Hook_1" : "Ball_1", b = IsAlpha ? "Hook_2" : "Ball_2";
            var ga = ArtModels.Graft(art, a, _fixings, Quaternion.Euler(90f, 0f, 0f), new Vector3(-0.007f, 0.003f, 0f));
            var gb = ArtModels.Graft(art, b, _fixings, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.007f, 0.003f, 0f), false);
            if (ga == null && gb == null) foreach (var r in _fixings.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        }

        void OnDestroy()
        {
            if (_onLimb != null) Destroy(_onLimb.gameObject);
        }

        // ------------------------------------------------------------------ grabbing
        protected override void OnItemGrabbed(HandGrabber hand) => State.Pickup();

        public override bool CanBeGrabbedBy(HandGrabber hand) => !Fixed && base.CanBeGrabbedBy(hand);

        protected override void OnItemReleased(HandGrabber hand)
        {
            if (!State.OnLimb || Fixed) return;
            if (State.elasticTurnsLaid >= 1 && NearTurns(transform.position)) Fix(hand);
            else SpringOff();
        }

        bool NearTurns(Vector3 p)
        {
            if (Casualty == null) return false;
            float d = LimbMath.Project(Casualty.rig, Limb, p, out float pos, out _);
            float r = LimbMath.RadiusAt(Limb, State.limbPosition);
            return d < r + 0.07f && Mathf.Abs(LimbGeometry.Distance(State.limbId, State.limbPosition, pos)) < 0.07f;
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (Fixed || !IsHeld) return;
            if (!State.OnLimb) { TryPin(); return; }
            Wind();
            if (State.elasticTurnsLaid >= 1 && Trigger > 0.6f && NearTurns(transform.position)) Fix(Holder);
        }

        void TryPin()
        {
            var c = NearestCasualty(transform.position, 2.2f);
            if (c == null || c.rig == null || !c.IsDown) { _pinTimer = 0f; return; }
            if (!LimbMath.Nearest(c.rig, transform.position, out var limb, out float pos, out float skin) || skin > 0.05f || pos > 2.3f)
            { _pinTimer = 0f; return; }
            if (limb != _pinLimb) { _pinLimb = limb; _pinTimer = 0f; }
            _pinTimer += Time.deltaTime;
            if (_pinTimer < PinSeconds) return;
            if (!State.WrapAround(LimbMath.Id(limb), pos) || !State.ConfirmPosition()) return;
            Casualty = c;
            Limb = limb;
            State.overClothing = !(c.Interaction != null && c.Interaction.IsExposed(State.Region));
            c.Patient.AddTourniquet(State);
            _prevValid = false;
            _angleAcc = 0f;
            _maxStretch = 0f;
            _onLimb.gameObject.SetActive(true);
            Haptics.Play(HolderInput, Haptics.Grab);
            Debug.Log($"[FMVR] {Label}: end pinned on {State.limbId} at {State.limbPosition:F2}");
        }

        /// <summary>Hand circling the limb axis at the band site: full circle = one turn, weighted by the stretch.</summary>
        void Wind()
        {
            var rig = Casualty.rig;
            Vector3 c = LimbMath.PointAt(rig, Limb, State.limbPosition);
            Vector3 axis = LimbMath.AxisAt(rig, Limb, State.limbPosition);
            Vector3 up = LimbMath.UpAround(rig, Limb, State.limbPosition);
            Vector3 rel = Vector3.ProjectOnPlane(transform.position - c, axis);
            float r = LimbMath.RadiusAt(Limb, State.limbPosition);
            if (rel.magnitude < r * 0.5f) { _prevValid = false; return; }
            float ang = Vector3.SignedAngle(up, rel, axis);
            if (_prevValid)
            {
                float d = Mathf.DeltaAngle(_prevAngle, ang);
                if (Mathf.Abs(d) < 90f) _angleAcc += d;
            }
            _prevAngle = ang;
            _prevValid = true;
            float stretch = Mathf.Clamp01((rel.magnitude - r - 0.015f) / FullStretch);
            _maxStretch = Mathf.Max(_maxStretch, stretch);
            Haptics.Tension(HolderInput, 0.15f + 0.7f * stretch);
            if (Mathf.Abs(_angleAcc) >= 360f)
            {
                _angleAcc -= Mathf.Sign(_angleAcc) * 360f;
                float w = State.ElasticTurn(_maxStretch);
                _maxStretch = stretch;
                Casualty.Patient.RefreshTourniquets();
                if (w > 0.3f)
                {
                    Casualty.Pain.TourniquetTwist(State.limbId, State.HalfTurns);
                    if (Casualty.voice != null) Casualty.voice.OnPainSpike(Casualty, true);
                    if (Casualty.rig != null) Casualty.rig.Flinch(0.7f);
                }
                Haptics.Play(HolderInput, Haptics.PressClick, 0.6f + w * 0.6f);
                Debug.Log($"[FMVR] {Label}: turn {State.elasticTurnsLaid} stretch {w:F2} → occlusion {State.Occlusion:F2}");
            }
        }

        void Fix(HandGrabber hand)
        {
            if (!State.ElasticFix()) return;
            Attached = true;
            if (IsGrabbed) ForceRelease();
            Pin();
            foreach (var col in GetComponents<Collider>()) col.isTrigger = true;
            if (Visual != null) Visual.gameObject.SetActive(false);
            _fixings.gameObject.SetActive(true);
            Casualty.Patient.RefreshTourniquets();
            LastFeedback = Casualty.Protocol.TourniquetApplied(State);
            if (hand != null) Haptics.Play(hand.Input, Haptics.PressClick, 1.2f);
            TreatmentAudio.Velcro(transform.position, 0.5f);
            Debug.Log($"[FMVR] {Label} fixed: {State.elasticTurnsLaid} turns, occlusion {State.Occlusion:F2}, {LastFeedback.code}");
            PlaceOnLimb();
        }

        /// <summary>Let go before fixing: the band springs off, the turns are lost, the roll drops.</summary>
        void SpringOff()
        {
            State.ElasticRelease();
            if (Casualty != null) Casualty.Patient.RemoveTourniquet(State);
            State = new TourniquetState { type = TourniquetType.Elastic };
            State.Pickup();
            Casualty = null;
            _onLimb.gameObject.SetActive(false);
            _strand.gameObject.SetActive(false);
            _shownTurns = -1;
            Debug.Log($"[FMVR] {Label}: let go before fixing — the band sprang off");
        }

        /// <summary>Scenario/test setup: band already on the limb with `turns` fully stretched turns and fixed.</summary>
        public void PreApply(CasualtyController c, Limb limb, float pos, int turns)
        {
            TakeOutOfPouch();
            State.Pickup();
            if (!State.WrapAround(LimbMath.Id(limb), pos) || !State.ConfirmPosition()) return;
            Casualty = c; Limb = limb;
            c.Patient.AddTourniquet(State);
            for (int i = 0; i < turns; i++) State.ElasticTurn(1f);
            _onLimb.gameObject.SetActive(true);
            Fix(null);
        }

        /// <summary>Marker stroke on the band: writes the scenario clock.</summary>
        public bool WriteTime()
        {
            if (!Fixed || State.TimeMarked || Casualty == null) return false;
            float t = Casualty.Patient.secondsSinceStart;
            if (!State.MarkTime(t)) return false;
            _timeText.text = "T " + ScenarioClock.Format(t);
            LastFeedback = Casualty.Protocol.TourniquetTimeMarked(State);
            return true;
        }

        public bool CanWriteTime => Fixed && !State.TimeMarked;
        public Vector3 TimeLabelPoint => TimeLabel != null ? TimeLabel.position : transform.position;

        void LateUpdate()
        {
            if (State.OnLimb && Casualty != null) PlaceOnLimb();
        }

        void PlaceOnLimb()
        {
            var rig = Casualty.rig;
            float pos = State.limbPosition;
            Vector3 c = LimbMath.PointAt(rig, Limb, pos);
            Vector3 axis = LimbMath.AxisAt(rig, Limb, pos);
            Vector3 up = LimbMath.UpAround(rig, Limb, pos);
            _onLimb.SetPositionAndRotation(c, Quaternion.LookRotation(axis, up));
            float r = LimbMath.RadiusAt(Limb, pos) + 0.002f;
            int turns = Mathf.Min(MaxShownTurns, State.elasticTurnsLaid);
            if (turns != _shownTurns || Mathf.Abs(r - _shownR) > 0.001f)
            {
                _shownTurns = turns; _shownR = r;
                for (int i = 0; i < MaxShownTurns; i++)
                {
                    bool on = i < turns;
                    _rings[i].gameObject.SetActive(on);
                    if (!on) continue;
                    // stretched turns lie tight and overlap (spiral), each a little over the previous one
                    Kit.Ring(r + 0.0018f * i, 0.025f, 0.0018f, 24, _rings[i].sharedMesh);
                    _rings[i].transform.localPosition = new Vector3(0f, 0f, ((i % 3) - 1) * 0.009f);
                    _rings[i].transform.localRotation = Quaternion.Euler(0f, (i % 2 == 0 ? 4f : -4f), 0f);
                }
            }
            float top = r + 0.0018f * Mathf.Max(0, turns - 1) + 0.002f;
            // the stretched part of the band from the last turn to the roll in the hand
            bool strand = !Fixed && IsHeld;
            _strand.gameObject.SetActive(strand);
            if (strand)
            {
                Vector3 hand = transform.position;
                Vector3 rel = Vector3.ProjectOnPlane(hand - c, axis);
                Vector3 from = c + (rel.sqrMagnitude > 1e-6f ? rel.normalized : up) * (top - 0.002f);
                float len = Vector3.Distance(from, hand);
                _strand.SetPositionAndRotation((from + hand) * 0.5f, Quaternion.LookRotation(hand - from, axis));
                float thin = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((len - 0.02f) / FullStretch)); // elastic narrows when stretched
                _strand.localScale = new Vector3(0.0018f, 0.025f * thin, Mathf.Max(0.005f, len));
            }
            _fixings.localPosition = new Vector3(0f, top + 0.002f, 0.004f);
            _fixings.localRotation = Quaternion.identity;
            TimeLabel.localPosition = new Vector3(0f, top + 0.001f, -0.016f);
            TimeLabel.gameObject.SetActive(Fixed);
            if (Fixed) transform.SetPositionAndRotation(_onLimb.position + _onLimb.up * top, _onLimb.rotation);
        }
    }
}
