using System.Collections.Generic;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using UnityEngine;

namespace FieldMedic.Scenarios.SelfAid
{
    /// <summary>
    /// CAT on yourself, one hand (w9b). The free hand takes the CAT from the quick-access pouch and brings the open
    /// loop over the wounded hand (wrap), keeps holding the band and slides it up to the shoulder, lets go
    /// (positioned), grabs the red strap tail and pulls, grabs the windlass and twists (wrist roll or cranking) until
    /// the bleeding stops, lets go with the rod at the clip (locked), pulls the white TIME strap over the clip
    /// (secured) and writes the time with the marker (<see cref="WriteTime"/>). The rules are the pure
    /// <see cref="TourniquetState"/> of the run (same as on Erlan); the wounded hand cannot help.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class SelfTourniquet : MonoBehaviour
    {
        public static readonly List<SelfTourniquet> All = new List<SelfTourniquet>();

        public SelfAidRun run;
        public SelfLimb limb;
        /// The kit's CAT (quick pouch): taken out, it becomes this band once the loop is over the hand.
        public MedItem kitCat;
        public HandGrabber woundedHand;
        public float wrapDistance = 0.11f;

        public TourniquetState State => run.tq;
        public ProxyHandle BandHandle { get; private set; }
        public ProxyHandle StrapHandle { get; private set; }
        public ProxyHandle RodHandle { get; private set; }
        public ProxyHandle TimeStrapHandle { get; private set; }
        public Transform TimeLabel { get; private set; }
        public bool Wrapped => State.OnLimb;
        public float WrapPoint => run.cfg.limb == SelfAidLimb.Arm ? 2.1f : 2.1f;

        Transform _onLimb, _rodPivot, _strapTail, _timeFlap, _timeCover, _housing, _clip, _buckle;
        Mesh _ringMesh;
        TextMesh _timeText;
        float _shownRadius = -1f, _strapMax, _timeMax, _rodVisual, _crankPrev;
        Quaternion _rodPrevRot;
        bool _rodWasHeld, _strapWasHeld, _timeWasHeld, _crankValid, _bandWasHeld;
        int _lastHalfTurns;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        void Awake() => Build();

        void Build()
        {
            if (_onLimb != null) return;
            var black = Kit.Mat("CAT_Black", Kit.CatBlack, 0.35f);
            var red = Kit.Mat("CAT_Red", Kit.CatRed, 0.4f);
            var grey = Kit.Mat("CAT_Grey", new Color(0.13f, 0.13f, 0.13f), 0.3f);
            var white = Kit.Mat("CAT_White", new Color(0.86f, 0.86f, 0.82f), 0.3f);
            _onLimb = new GameObject("SelfCAT").transform;
            _onLimb.SetParent(transform, false);
            _ringMesh = Kit.Ring(0.06f, 0.038f, 0.004f, 28);
            Kit.MeshPart(_onLimb, "Band", _ringMesh, black, Vector3.zero, Quaternion.identity, Vector3.one);
            _housing = Kit.Part(_onLimb, PrimitiveType.Cube, "Housing", Vector3.zero, new Vector3(0.04f, 0.012f, 0.06f), black).transform;
            _rodPivot = new GameObject("RodPivot").transform;
            _rodPivot.SetParent(_onLimb, false);
            Kit.Part(_rodPivot, PrimitiveType.Cylinder, "Rod", Vector3.zero, new Vector3(0.012f, 0.05f, 0.012f), black, Quaternion.Euler(90f, 0f, 0f));
            Kit.Part(_rodPivot, PrimitiveType.Cylinder, "RodEnd", new Vector3(0f, 0f, 0.046f), new Vector3(0.0135f, 0.004f, 0.0135f), red, Quaternion.Euler(90f, 0f, 0f));
            _clip = Kit.Part(_onLimb, PrimitiveType.Cube, "Clip", Vector3.zero, new Vector3(0.036f, 0.02f, 0.008f), grey).transform;
            _buckle = Kit.Part(_onLimb, PrimitiveType.Cube, "Buckle", Vector3.zero, new Vector3(0.012f, 0.02f, 0.045f), grey).transform;
            _strapTail = new GameObject("StrapTail").transform;
            _strapTail.SetParent(_onLimb, false);
            Kit.Part(_strapTail, PrimitiveType.Cube, "Tail", new Vector3(0f, 0f, 0.5f), new Vector3(0.038f, 0.004f, 1f), black);
            Kit.Part(_strapTail, PrimitiveType.Cube, "RedTip", new Vector3(0f, 0f, 1f), new Vector3(0.039f, 0.005f, 0.02f), red);
            _timeFlap = Kit.Part(_onLimb, PrimitiveType.Cube, "TimeFlap", Vector3.zero, new Vector3(0.03f, 0.003f, 0.05f), white).transform;
            _timeCover = Kit.Part(_onLimb, PrimitiveType.Cube, "TimeCover", Vector3.zero, new Vector3(0.032f, 0.003f, 0.075f), white).transform;
            TimeLabel = new GameObject("TimeLabel").transform;
            TimeLabel.SetParent(_onLimb, false);
            _timeText = Kit.Text(TimeLabel, "Text", "", 0.0035f, new Color(0.05f, 0.05f, 0.08f), new Vector3(0f, 0.002f, 0f), Quaternion.Euler(90f, 0f, 90f));
            BandHandle = ProxyHandle.Create<ProxyHandle>("Grab_SelfBand", _onLimb, transform.position, 0.06f, 5);
            StrapHandle = ProxyHandle.Create<ProxyHandle>("Grab_SelfStrap", _onLimb, transform.position, 0.05f, 6);
            RodHandle = ProxyHandle.Create<ProxyHandle>("Grab_SelfWindlass", _onLimb, transform.position, 0.045f, 7);
            TimeStrapHandle = ProxyHandle.Create<ProxyHandle>("Grab_SelfTimeStrap", _onLimb, transform.position, 0.04f, 6);
            _onLimb.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ wrap: loop over the wounded hand
        void Update()
        {
            if (run == null || limb == null) return;
            if (!Wrapped) { DetectWrap(); return; }
            float dt = Time.deltaTime;
            Slide(dt);
            Strap();
            Windlass(dt);
            TimeStrap();
            bool slidable = State.stage <= TourniquetStage.Positioned && State.strapPulledCm <= 3f;
            BandHandle.gameObject.SetActive(slidable);
            StrapHandle.gameObject.SetActive(State.stage >= TourniquetStage.Positioned && !State.Locked);
            RodHandle.gameObject.SetActive(State.stage >= TourniquetStage.Positioned && !State.Locked);
            TimeStrapHandle.gameObject.SetActive(State.stage == TourniquetStage.WindlassLocked);
        }

        /// <summary>Held CAT close to the wrist/hand of the wounded side = the loop goes over the hand.</summary>
        void DetectWrap()
        {
            if (kitCat == null || !kitCat.IsHeld) return;
            var hand = kitCat.Holder;
            if (hand == null || hand == woundedHand) return;
            Vector3 p = kitCat.transform.position;
            float d = Mathf.Min(Vector3.Distance(p, limb.PointAt(2.2f)), Vector3.Distance(p, limb.Tip), Vector3.Distance(p, limb.Distal));
            if (d > wrapDistance) return;
            WrapNow(hand);
        }

        /// <summary>The loop is over the hand: the kit CAT becomes the band on the limb, still held by the free hand.</summary>
        public bool WrapNow(HandGrabber hand)
        {
            State.Pickup();
            if (!run.Wrap(WrapPoint)) return false;
            if (kitCat != null)
            {
                kitCat.ForceRelease();
                kitCat.gameObject.SetActive(false);
            }
            _onLimb.gameObject.SetActive(true);
            Place();
            Physics.SyncTransforms();
            if (hand != null)
            {
                hand.TryGrab(BandHandle);
                Haptics.Play(hand.Input, Haptics.Grab);
            }
            Debug.Log("[FMVR] self-aid: loop over the wounded hand");
            return true;
        }

        // ------------------------------------------------------------------ slide up the limb, let go = positioned
        void Slide(float dt)
        {
            bool held = BandHandle.isActiveAndEnabled && BandHandle.Hand != null;
            if (held)
            {
                limb.Project(BandHandle.Hand.PalmPosition, out float pos);
                float before = State.limbPosition;
                if (State.SlideTo(Mathf.MoveTowards(before, Mathf.Min(pos, 2.2f), dt * 3f)) && LimbGeometry.OnJoint(State.limbPosition) && !LimbGeometry.OnJoint(before))
                    Haptics.Play(BandHandle.Input, Haptics.Contact); // bump over the elbow/knee
            }
            else if (_bandWasHeld && State.stage == TourniquetStage.BandAroundLimb)
            {
                State.ConfirmPosition();
                if (woundedHand != null) Haptics.Play(woundedHand.Input, Haptics.PressClick);
                Debug.Log($"[FMVR] self-aid: band positioned at {State.limbPosition:0.00}");
            }
            _bandWasHeld = held;
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
            State.PullStrap(cm);
            run.patient.RefreshTourniquets();
            Haptics.Tension(StrapHandle.Input, State.StrapTightness);
        }

        void Windlass(float dt)
        {
            bool held = RodHandle.isActiveAndEnabled && RodHandle.Hand != null;
            var hand = RodHandle.Hand;
            Vector3 up = _onLimb.up;
            if (held && !_rodWasHeld) { _rodPrevRot = hand.transform.rotation; _crankValid = false; }
            if (held)
            {
                // wrist roll about the rod's spin axis, or cranking the hand around the pivot (same as the CAT on Erlan)
                Quaternion hr = hand.transform.rotation;
                (hr * Quaternion.Inverse(_rodPrevRot)).ToAngleAxis(out float a, out Vector3 ax);
                if (a > 180f) a -= 360f;
                float twist = float.IsNaN(ax.x) ? 0f : a * Vector3.Dot(ax, up);
                _rodPrevRot = hr;
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
                if (!State.Locked && Mathf.Abs(applied) > 0.01f)
                {
                    State.Twist(applied);
                    run.patient.RefreshTourniquets();
                    Haptics.Tension(RodHandle.Input, 0.25f + 0.6f * State.Occlusion);
                }
                if (State.HalfTurns > _lastHalfTurns)
                {
                    Haptics.Play(hand.Input, Haptics.PressClick);
                    if (woundedHand != null) Haptics.Play(woundedHand.Input, Haptics.Grab, 0.8f); // it hurts
                }
                _lastHalfTurns = State.HalfTurns;
            }
            else if (_rodWasHeld && !State.Locked)
            {
                if (State.TryLock())
                {
                    _rodVisual = State.windlassDegrees;
                    run.patient.RefreshTourniquets();
                    Debug.Log($"[FMVR] self-aid: windlass locked, {State.HalfTurns} half-turns, occlusion {State.Occlusion:0.00}");
                }
                else { State.ReleaseWindlass(); run.patient.RefreshTourniquets(); }
                _lastHalfTurns = State.HalfTurns;
            }
            _rodWasHeld = held;
            _rodVisual = Mathf.MoveTowards(_rodVisual, State.windlassDegrees, (held ? 2000f : 900f) * dt);
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

        /// <summary>Marker on the TIME strap (needs the strap secured over the clip).</summary>
        public bool CanWrite => Wrapped && State.Secured && !State.TimeMarked;
        public Vector3 TimeLabelPoint => TimeLabel != null ? TimeLabel.position : transform.position;

        public bool WriteTime()
        {
            if (!CanWrite) return false;
            float t = run.Clock;
            if (!State.MarkTime(t)) return false;
            _timeText.text = "T " + ScenarioClock.Format(t);
            Debug.Log("[FMVR] self-aid: time written " + _timeText.text);
            return true;
        }

        // ------------------------------------------------------------------ pose
        void LateUpdate()
        {
            if (run != null && limb != null && Wrapped) Place();
        }

        void Place()
        {
            float pos = State.limbPosition;
            Vector3 c = limb.PointAt(pos);
            Vector3 axis = limb.AxisAt(pos);
            Vector3 up = limb.TopAt(pos);
            _onLimb.SetPositionAndRotation(c, Quaternion.LookRotation(axis, up));
            float r = limb.RadiusAt(pos) + 0.006f + State.slackCm / 100f / (2f * Mathf.PI) * 1.4f;
            if (Mathf.Abs(r - _shownRadius) > 0.0008f) { _shownRadius = r; Kit.Ring(r, 0.038f, 0.004f, 28, _ringMesh); }
            float top = r + 0.008f;
            _housing.localPosition = new Vector3(0f, top, 0f);
            _rodPivot.localPosition = new Vector3(0f, top + 0.012f, 0f);
            _rodPivot.localRotation = Quaternion.AngleAxis(_rodVisual, Vector3.up);
            _clip.localPosition = new Vector3(0f, top + 0.008f, 0.04f);
            Vector3 buckle = new Vector3(r * 0.72f + 0.012f, r * 0.72f, 0f);
            _buckle.localPosition = buckle - new Vector3(0.006f, 0f, 0f);
            BandHandle.transform.localPosition = new Vector3(0f, top, 0f);

            Vector3 tailDir; float tailLen;
            if (StrapHandle.Hand != null)
            {
                Vector3 hand = _onLimb.InverseTransformPoint(StrapHandle.Hand.PalmPosition);
                tailDir = (hand - buckle).normalized; tailLen = Mathf.Max(0.03f, (hand - buckle).magnitude);
            }
            else if (State.stage >= TourniquetStage.StrapPulled) { tailDir = new Vector3(-0.7f, 0.7f, 0f).normalized; tailLen = 0.07f; }
            else { tailDir = new Vector3(0.75f, -0.4f, 0.1f).normalized; tailLen = 0.11f + State.slackCm * 0.004f; }
            _strapTail.localPosition = buckle;
            var side = Vector3.Cross(tailDir, Vector3.forward);
            _strapTail.localRotation = Quaternion.LookRotation(tailDir, side.sqrMagnitude > 1e-4f ? side : Vector3.up);
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

        /// <summary>World positions of the grab points (bots, hints).</summary>
        public Vector3 BandPoint => BandHandle.transform.position;
        public Vector3 StrapPoint => StrapHandle.transform.position;
        public Vector3 RodPoint => RodHandle.transform.position;
        public Vector3 TimeStrapPoint => TimeStrapHandle.transform.position;
        public Vector3 Up => _onLimb.up;
        public Vector3 Axis => _onLimb.forward;
    }
}
