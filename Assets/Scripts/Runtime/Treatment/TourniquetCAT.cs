using FieldMedic.Casualty;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Combat Application Tourniquet, applied with the hands exactly as GDD §5:
    /// <list type="number">
    /// <item>Held → bring the open loop over the foot/hand and slide it up the limb, or pass it under a lifted
    /// limb from one side to the other (BandAroundLimb).</item>
    /// <item>Slide along the limb while holding the band; let go to position it (joints are invalid zones).</item>
    /// <item>Grab the red-tipped strap tail and pull (≥ 15 cm takes out the slack).</item>
    /// <item>Grab the windlass rod and twist the wrist: every 180° is a half-turn; bleeding falls with each, the
    /// distal pulse disappears at full occlusion; he screams and grabs your arm.</item>
    /// <item>Let go of the rod near the clip orientation → it snaps into the clip (locked); otherwise it spins back.</item>
    /// <item>Pull the white TIME strap over the clip (secured), then write the time on it with the marker.</item>
    /// </list>
    /// All rules live in <see cref="TourniquetState"/> (pure C#, EditMode-tested); this class turns hand motion
    /// into those calls and shows the result.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class TourniquetCAT : MedItem
    {
        public TourniquetState State = new TourniquetState();
        [Tooltip("Set when this is the casualty's own CAT (from his IFAK).")]
        public CasualtyInteraction owner;

        public CasualtyController Casualty { get; private set; }
        public Limb Limb { get; private set; }
        public ProxyHandle StrapHandle { get; private set; }
        public ProxyHandle RodHandle { get; private set; }
        public ProxyHandle TimeStrapHandle { get; private set; }
        public Transform TimeLabel { get; private set; }
        public Feedback LastFeedback { get; private set; }
        /// <summary>S8: a locked band may be unwound (rod twisted back ≥ 270°) to convert it.</summary>
        public bool allowConversion;
        public bool ConversionAttempted { get; private set; }
        public bool Retightened { get; private set; }
        public bool Slipped { get; private set; }
        bool _everLocked;
        float _untwist;
        public float SlideTarget => State.limbPosition;

        Transform _folded, _onLimb, _rodPivot, _strapTail, _timeFlap, _timeCover;
        MeshFilter _ring;
        Mesh _ringMesh;
        TextMesh _timeText;
        float _shownRadius = -1f;

        // wrap detection
        readonly float[] _nearEnd = { -10f, -10f, -10f, -10f };
        readonly float[] _side = new float[4];
        readonly bool[] _sideValid = new bool[4];
        // strap / rod / time strap tracking
        float _strapMax, _timeMax, _rodVisual, _crankPrev;
        Quaternion _rodPrevRot;
        bool _rodWasHeld, _strapWasHeld, _timeWasHeld, _crankValid;
        int _lastHalfTurns;
        HandGrabber _slideHand;

        // ------------------------------------------------------------------ visual
        public override void BuildVisual()
        {
            Body.mass = 0.08f;
            var black = Kit.Mat("CAT_Black", Kit.CatBlack, 0.35f);
            var red = Kit.Mat("CAT_Red", Kit.CatRed, 0.4f);
            var grey = Kit.Mat("CAT_Grey", new Color(0.13f, 0.13f, 0.13f), 0.3f);
            var white = Kit.Mat("CAT_White", new Color(0.86f, 0.86f, 0.82f), 0.3f);

            // as carried: band folded into a flat loop, windlass on top, red tip sticking out
            _folded = new GameObject("Folded").transform;
            _folded.SetParent(transform, false);
            Kit.MeshPart(_folded, "Loop", Kit.Ring(0.034f, 0.038f, 0.004f, 20), black, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), new Vector3(1f, 0.42f, 1f));
            Kit.Part(_folded, PrimitiveType.Cube, "Housing", new Vector3(0f, 0.02f, 0f), new Vector3(0.04f, 0.012f, 0.055f), black);
            Kit.Part(_folded, PrimitiveType.Cylinder, "Rod", new Vector3(0f, 0.031f, 0f), new Vector3(0.011f, 0.05f, 0.011f), black, Quaternion.Euler(90f, 0f, 0f));
            Kit.Part(_folded, PrimitiveType.Cube, "Clip", new Vector3(0f, 0.026f, 0.034f), new Vector3(0.03f, 0.016f, 0.008f), grey);
            Kit.Part(_folded, PrimitiveType.Cube, "Tail", new Vector3(0.0f, -0.008f, -0.05f), new Vector3(0.038f, 0.004f, 0.05f), black);
            Kit.Part(_folded, PrimitiveType.Cube, "RedTip", new Vector3(0.0f, -0.008f, -0.08f), new Vector3(0.039f, 0.005f, 0.014f), red);
            Kit.Part(_folded, PrimitiveType.Cube, "TimeStrap", new Vector3(0f, 0.016f, -0.012f), new Vector3(0.028f, 0.003f, 0.03f), white);
            Kit.Merge(_folded);

            // on a limb: ring (rebuilt with the current radius), housing + rod on top, buckle, tail, TIME strap
            _onLimb = new GameObject("OnLimb").transform;
            _onLimb.SetParent(transform, false);
            _ringMesh = Kit.Ring(0.08f, 0.038f, 0.004f, 28);
            _ring = Kit.MeshPart(_onLimb, "Band", _ringMesh, black, Vector3.zero, Quaternion.identity, Vector3.one).GetComponent<MeshFilter>();
            var housing = Kit.Part(_onLimb, PrimitiveType.Cube, "Housing", Vector3.zero, new Vector3(0.04f, 0.012f, 0.06f), black).transform;
            housing.name = "Housing";
            _rodPivot = new GameObject("RodPivot").transform;
            _rodPivot.SetParent(_onLimb, false);
            Kit.Part(_rodPivot, PrimitiveType.Cylinder, "Rod", Vector3.zero, new Vector3(0.012f, 0.05f, 0.012f), black, Quaternion.Euler(90f, 0f, 0f));
            Kit.Part(_rodPivot, PrimitiveType.Cylinder, "RodEnd", new Vector3(0f, 0f, 0.046f), new Vector3(0.0135f, 0.004f, 0.0135f), red, Quaternion.Euler(90f, 0f, 0f));
            Kit.Part(_onLimb, PrimitiveType.Cube, "Clip", Vector3.zero, new Vector3(0.036f, 0.02f, 0.008f), grey).name = "Clip";
            Kit.Part(_onLimb, PrimitiveType.Cube, "Buckle", Vector3.zero, new Vector3(0.012f, 0.02f, 0.045f), grey).name = "Buckle";
            _strapTail = new GameObject("StrapTail").transform;
            _strapTail.SetParent(_onLimb, false);
            Kit.Part(_strapTail, PrimitiveType.Cube, "Tail", new Vector3(0f, 0f, 0.5f), new Vector3(0.038f, 0.004f, 1f), black);
            Kit.Part(_strapTail, PrimitiveType.Cube, "RedTip", new Vector3(0f, 0f, 1f), new Vector3(0.039f, 0.005f, 0.02f), red);
            _timeFlap = Kit.Part(_onLimb, PrimitiveType.Cube, "TimeFlap", Vector3.zero, new Vector3(0.03f, 0.003f, 0.05f), white).transform;
            _timeCover = Kit.Part(_onLimb, PrimitiveType.Cube, "TimeCover", Vector3.zero, new Vector3(0.032f, 0.003f, 0.075f), white).transform;
            TimeLabel = new GameObject("TimeLabel").transform;
            TimeLabel.SetParent(_onLimb, false);
            _timeText = Kit.Text(TimeLabel, "Text", "", 0.0035f, new Color(0.05f, 0.05f, 0.08f), new Vector3(0f, 0.002f, 0f), Quaternion.Euler(90f, 0f, 90f));
            _onLimb.gameObject.SetActive(false);

            StrapHandle = ProxyHandle.Create<ProxyHandle>("Grab_Strap", _onLimb, transform.position, 0.05f, 6);
            RodHandle = ProxyHandle.Create<ProxyHandle>("Grab_Windlass", _onLimb, transform.position, 0.045f, 6);
            TimeStrapHandle = ProxyHandle.Create<ProxyHandle>("Grab_TimeStrap", _onLimb, transform.position, 0.04f, 6);

            tip = new GameObject("Tip").transform;
            tip.SetParent(transform, false);
            State.StageChanged += OnStage;
        }

        // ------------------------------------------------------------------ grabbing the band itself
        protected override void OnItemGrabbed(HandGrabber hand)
        {
            State.Pickup();
            if (State.OnLimb) _slideHand = hand;
        }

        protected override void OnItemReleased(HandGrabber hand)
        {
            if (!State.OnLimb) return;
            _slideHand = null;
            if (State.stage == TourniquetStage.BandAroundLimb && State.ConfirmPosition())
            {
                Haptics.Play(hand.Input, Haptics.PressClick);
                State.overClothing = !(Casualty.Interaction != null && Casualty.Interaction.IsExposed(State.Region));
                Casualty.Patient.AddTourniquet(State);
                if (Casualty.Interaction != null) Casualty.Interaction.Register(this);
            }
        }

        public override bool CanBeGrabbedBy(HandGrabber hand)
        {
            // once the strap is pulled the band cannot slide any more: only its parts are grabbed
            if (State.OnLimb && State.stage > TourniquetStage.Positioned) return false;
            return base.CanBeGrabbedBy(hand);
        }

        protected override void FixedUpdate()
        {
            if (State.OnLimb) return; // positioned by the limb in LateUpdate, not by physics
            base.FixedUpdate();
            if (IsGrabbed) DetectWrap();
        }

        void DetectWrap()
        {
            var c = NearestCasualty(transform.position, 2.2f);
            if (c == null || c.rig == null || !c.IsDown) return;
            Vector3 p = transform.position;
            foreach (var limb in LimbMath.All)
            {
                int i = (int)limb;
                float dist = LimbMath.Project(c.rig, limb, p, out float pos, out Vector3 axisPt);
                if (dist == float.MaxValue) continue;
                float r = LimbMath.RadiusAt(limb, pos);
                // 1) over the foot / hand: the loop passes the end of the limb, then travels up it
                if (pos >= 2.3f && dist < r + 0.1f) _nearEnd[i] = Time.time;
                if (Time.time - _nearEnd[i] < 3f && pos < 2.25f && dist < r + 0.06f)
                {
                    Wrap(c, limb, pos);
                    return;
                }
                // 2) routed under a lifted limb from one side to the other
                if (dist < r + 0.14f && LimbLifted(c, limb, pos, axisPt))
                {
                    Vector3 axis = LimbMath.AxisAt(c.rig, limb, pos);
                    Vector3 sideDir = Vector3.Cross(axis, Vector3.up).normalized;
                    float s = Vector3.Dot(p - axisPt, sideDir);
                    bool below = p.y < axisPt.y;
                    if (_sideValid[i] && below && Mathf.Sign(s) != Mathf.Sign(_side[i]) && Mathf.Abs(s - _side[i]) > 0.02f)
                    {
                        Wrap(c, limb, pos);
                        return;
                    }
                    _side[i] = s;
                    _sideValid[i] = true;
                }
                else _sideValid[i] = false;
            }
        }

        static bool LimbLifted(CasualtyController c, Limb limb, float pos, Vector3 axisPt)
        {
            if (c.Interaction != null)
                foreach (var h in c.Interaction.LimbHandles)
                    if (h.limb == limb && h.Held && h.IsLifted) return true;
            CasualtyDrag.FloorAt(axisPt, out float floor);
            return axisPt.y - floor - LimbMath.RadiusAt(limb, pos) > 0.05f;
        }

        void Wrap(CasualtyController c, Limb limb, float pos)
        {
            if (!State.WrapAround(LimbMath.Id(limb), Mathf.Min(pos, 2.2f))) return;
            Casualty = c;
            Limb = limb;
            Attached = true;
            GetComponent<BoxCollider>().isTrigger = true; // the marker must reach the TIME strap
            Body.isKinematic = true;
            Body.useGravity = false;
            _slideHand = PrimaryHolder;
            Haptics.Play(HolderInput, Haptics.Grab);
            _folded.gameObject.SetActive(false);
            _onLimb.gameObject.SetActive(true);
            GetComponent<BoxCollider>().size = new Vector3(0.12f, 0.08f, 0.06f);
            if (c.Interaction != null) c.Interaction.Register(this);
            PlaceOnLimb();
        }

        // ------------------------------------------------------------------ per frame on the limb
        void Update()
        {
            if (!State.OnLimb || Casualty == null) return;
            float dt = Time.deltaTime;
            Slide();
            Strap();
            Windlass(dt);
            TimeStrap();
            StrapHandle.gameObject.SetActive(State.stage >= TourniquetStage.Positioned && !State.Locked);
            RodHandle.gameObject.SetActive((State.stage >= TourniquetStage.Positioned && !State.Locked) || (State.Locked && allowConversion && !ConversionAttempted));
            TimeStrapHandle.gameObject.SetActive(State.stage == TourniquetStage.WindlassLocked);
        }

        void Slide()
        {
            if (_slideHand == null || !IsGrabbed) return;
            LimbMath.Project(Casualty.rig, Limb, _slideHand.PalmPosition, out float pos, out _);
            float before = State.limbPosition;
            if (State.SlideTo(Mathf.MoveTowards(before, pos, Time.deltaTime * 2.5f)))
            {
                float moved = Mathf.Abs(State.limbPosition - before);
                if (moved > 0f) Casualty.Handle(transform.position, moved * 6f / Mathf.Max(1e-3f, Time.deltaTime) * 0.1f, Time.deltaTime);
                bool joint = LimbGeometry.OnJoint(State.limbPosition);
                if (joint) Haptics.Play(HolderInput, Haptics.Contact); // bumps over the knee/elbow
            }
        }

        void Strap()
        {
            bool held = StrapHandle.isActiveAndEnabled && StrapHandle.Hand != null;
            if (held && !_strapWasHeld) _strapMax = 0f;
            _strapWasHeld = held;
            if (!held) return;
            float pull = StrapHandle.PalmDelta.magnitude;
            if (pull <= _strapMax) return;
            float cm = (pull - _strapMax) * 100f;
            _strapMax = pull;
            float before = State.slackCm;
            State.PullStrap(cm);
            float removed = before - State.slackCm;
            if (removed > 0f)
            {
                Casualty.Pain.AddSpike(removed * 0.012f);
                Casualty.Patient.RefreshTourniquets();
            }
            Haptics.Tension(StrapHandle.Input, State.StrapTightness);
        }

        void Windlass(float dt)
        {
            bool held = RodHandle.isActiveAndEnabled && RodHandle.Hand != null;
            var hand = RodHandle.Hand;
            Vector3 up = _onLimb.up;
            if (held && !_rodWasHeld)
            {
                _rodPrevRot = hand.transform.rotation;
                _crankValid = false;
            }
            if (held)
            {
                // wrist roll about the rod's spin axis (= surface normal)
                Quaternion hr = hand.transform.rotation;
                (hr * Quaternion.Inverse(_rodPrevRot)).ToAngleAxis(out float a, out Vector3 ax);
                if (a > 180f) a -= 360f;
                float twist = float.IsNaN(ax.x) ? 0f : a * Vector3.Dot(ax, up);
                _rodPrevRot = hr;
                // or cranking the hand around the pivot
                Vector3 rel = Vector3.ProjectOnPlane(hand.PalmPosition - _rodPivot.position, up);
                float crank = 0f;
                if (rel.magnitude > 0.035f)
                {
                    float ang = Mathf.Atan2(Vector3.Dot(Vector3.Cross(_onLimb.forward, rel), up), Vector3.Dot(_onLimb.forward, rel)) * Mathf.Rad2Deg;
                    if (_crankValid) crank = Mathf.DeltaAngle(_crankPrev, ang);
                    _crankPrev = ang;
                    _crankValid = true;
                }
                else _crankValid = false;
                float applied = Mathf.Abs(crank) > Mathf.Abs(twist) ? crank : twist;
                if (State.Locked)
                {
                    // conversion: unclip and unwind a locked band
                    if (allowConversion && !ConversionAttempted)
                    {
                        _untwist += applied;
                        _rodVisual = State.windlassDegrees + _untwist;
                        if (Mathf.Abs(_untwist) >= 270f) Convert();
                    }
                }
                else if (Mathf.Abs(applied) > 0.01f)
                {
                    State.Twist(applied);
                    Casualty.Patient.RefreshTourniquets();
                    Haptics.Tension(RodHandle.Input, 0.25f + 0.6f * State.Occlusion);
                }
                if (State.HalfTurns > _lastHalfTurns) OnHalfTurn(hand);
                _lastHalfTurns = State.HalfTurns;
            }
            else if (_rodWasHeld && !State.Locked)
            {
                if (State.TryLock()) OnLocked();
                else
                {
                    State.ReleaseWindlass();
                    Casualty.Patient.RefreshTourniquets();
                    _lastHalfTurns = State.HalfTurns;
                }
            }
            _rodWasHeld = held;
            // the rod visibly spins back when let go outside the clip
            _rodVisual = Mathf.MoveTowards(_rodVisual, State.windlassDegrees, (held ? 2000f : 900f) * dt);
        }

        void OnHalfTurn(HandGrabber hand)
        {
            Casualty.Pain.TourniquetTwist(State.limbId, State.HalfTurns);
            if (Casualty.voice != null) Casualty.voice.OnPainSpike(Casualty, true);
            if (Casualty.rig != null) Casualty.rig.Flinch(0.9f);
            if (Casualty.Interaction != null) Casualty.Interaction.GrabPlayerArm(hand.transform, 2.5f);
            Haptics.Play(hand.Input, Haptics.PressClick);
        }

        void Convert()
        {
            ConversionAttempted = true;
            LastFeedback = Casualty.Protocol.ConvertTourniquet(State);
            _untwist = 0f;
            _rodVisual = State.windlassDegrees;
            _lastHalfTurns = State.HalfTurns;
            if (RodHandle.Hand != null) Haptics.Play(RodHandle.Input, Haptics.PressClick);
            Debug.Log("[FMVR] TQ conversion: " + LastFeedback.code);
        }

        /// <summary>Scenario setup: this CAT was applied before the scenario starts (S7/S8).</summary>
        public void PreApply(CasualtyController c, Limb limb, float pos, int halfTurns, bool timeMarked, float appliedAt)
        {
            TakeOutOfPouch();
            State.Pickup();
            Wrap(c, limb, pos);
            if (!State.OnLimb) return;
            _slideHand = null;
            State.PreApply(LimbMath.Id(limb), pos, halfTurns, timeMarked, appliedAt);
            _rodVisual = State.windlassDegrees;
            _lastHalfTurns = State.HalfTurns;
            _everLocked = true;
            c.Patient.AddTourniquet(State);
            if (timeMarked && _timeText != null) _timeText.text = "T " + ScenarioClock.Format(appliedAt);
            PlaceOnLimb();
        }

        /// <summary>S7: the band slipped during the drag — the rod is out of the clip, partly unwound.</summary>
        public void SlipLoose()
        {
            if (!State.Locked) return;
            Slipped = true;
            State.SlipLoose(2, 2.5f);
            _rodVisual = State.windlassDegrees;
            _lastHalfTurns = State.HalfTurns;
            Casualty.Patient.RefreshTourniquets();
        }

        void OnLocked()
        {
            _rodVisual = State.windlassDegrees;
            if (State.HalfTurns > _lastHalfTurns) { Casualty.Pain.TourniquetTwist(State.limbId, State.HalfTurns); _lastHalfTurns = State.HalfTurns; }
            Casualty.Patient.RefreshTourniquets();
            if (_everLocked) { Retightened = true; LastFeedback = Casualty.Protocol.TourniquetRetightened(State); }
            else LastFeedback = Casualty.Protocol.TourniquetApplied(State);
            _everLocked = true;
            Haptics.Play(RodHandle.Input, Haptics.PressClick);
        }

        void TimeStrap()
        {
            bool held = TimeStrapHandle.isActiveAndEnabled && TimeStrapHandle.Hand != null;
            if (held && !_timeWasHeld) _timeMax = 0f;
            _timeWasHeld = held;
            if (!held) return;
            _timeMax = Mathf.Max(_timeMax, TimeStrapHandle.PalmDelta.magnitude);
            if (_timeMax >= 0.06f && State.SecureStrap()) Haptics.Play(TimeStrapHandle.Input, Haptics.PressClick);
        }

        /// <summary>Marker stroke on the TIME strap: writes the scenario clock (needs the windlass locked).</summary>
        public bool WriteTime()
        {
            if (!State.Locked || State.TimeMarked) return false;
            float t = Casualty.Patient.secondsSinceStart;
            if (!State.MarkTime(t)) return false;
            _timeText.text = "T " + ScenarioClock.Format(t);
            LastFeedback = Casualty.Protocol.TourniquetTimeMarked(State);
            return true;
        }

        void OnStage(TourniquetState s, TourniquetStage stage) { }

        void LateUpdate()
        {
            if (State.OnLimb && Casualty != null) PlaceOnLimb();
        }

        /// <summary>Pose on the limb (after IK) and update the band radius, rod angle, strap and labels.</summary>
        void PlaceOnLimb()
        {
            var rig = Casualty.rig;
            float pos = State.limbPosition;
            Vector3 c = LimbMath.PointAt(rig, Limb, pos);
            Vector3 axis = LimbMath.AxisAt(rig, Limb, pos);
            Vector3 up = LimbMath.UpAround(rig, Limb, pos);
            transform.SetPositionAndRotation(c, Quaternion.LookRotation(axis, up));

            // band circumference = limb + remaining slack (shown as a loose, larger loop)
            float r = LimbMath.RadiusAt(Limb, pos) + 0.004f + State.slackCm / 100f / (2f * Mathf.PI) * 1.4f;
            if (Mathf.Abs(r - _shownRadius) > 0.0008f)
            {
                _shownRadius = r;
                Kit.Ring(r, 0.038f, 0.004f, 28, _ringMesh);
            }
            float top = r + 0.008f;
            var housing = _onLimb.Find("Housing");
            housing.localPosition = new Vector3(0f, top, 0f);
            _rodPivot.localPosition = new Vector3(0f, top + 0.012f, 0f);
            _rodPivot.localRotation = Quaternion.AngleAxis(_rodVisual, Vector3.up);
            _onLimb.Find("Clip").localPosition = new Vector3(0f, top + 0.008f, 0.04f);
            _onLimb.Find("Buckle").localPosition = new Vector3(r * 0.72f + 0.006f, r * 0.72f, 0f);
            Vector3 buckle = new Vector3(r * 0.72f + 0.012f, r * 0.72f, 0f);

            // strap tail: hangs from the buckle, stretched to the hand while pulled, wrapped when done
            Vector3 tailDir; float tailLen;
            if (StrapHandle.Hand != null)
            {
                Vector3 hand = _onLimb.InverseTransformPoint(StrapHandle.Hand.PalmPosition);
                tailDir = (hand - buckle).normalized; tailLen = Mathf.Max(0.03f, (hand - buckle).magnitude);
            }
            else if (State.stage >= TourniquetStage.StrapPulled)
            {
                tailDir = new Vector3(-0.7f, 0.7f, 0f).normalized; tailLen = 0.07f; // laid back over the band
            }
            else
            {
                tailDir = new Vector3(0.75f, -0.4f, 0.1f).normalized; tailLen = 0.11f + State.slackCm * 0.004f;
            }
            _strapTail.localPosition = buckle;
            _strapTail.localRotation = Quaternion.LookRotation(tailDir, Vector3.Cross(tailDir, Vector3.forward).sqrMagnitude > 1e-4f ? Vector3.Cross(tailDir, Vector3.forward) : Vector3.up);
            _strapTail.localScale = new Vector3(1f, 1f, tailLen);
            StrapHandle.transform.localPosition = buckle + tailDir * tailLen;
            RodHandle.transform.localPosition = _rodPivot.localPosition;

            bool secured = State.Secured;
            _timeFlap.gameObject.SetActive(!secured);
            _timeCover.gameObject.SetActive(secured);
            _timeFlap.localPosition = new Vector3(-0.03f, top - 0.004f, -0.035f);
            _timeFlap.localRotation = Quaternion.Euler(0f, 0f, 25f);
            _timeCover.localPosition = new Vector3(0f, top + 0.026f, 0f);
            TimeStrapHandle.transform.localPosition = _timeFlap.localPosition;
            TimeLabel.localPosition = new Vector3(0f, top + 0.0285f, 0f);
            TimeLabel.gameObject.SetActive(secured);
        }

        /// <summary>World point on the TIME strap a marker writes on.</summary>
        public Vector3 TimeLabelPoint => TimeLabel != null ? TimeLabel.position : transform.position;
    }
}
