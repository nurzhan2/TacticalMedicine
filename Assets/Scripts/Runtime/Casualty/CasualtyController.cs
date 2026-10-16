using System;
using System.Collections.Generic;
using FieldMedic.Casualty.Blood;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Casualty
{
    /// <summary>
    /// The living casualty. Owns the medical model (<see cref="PatientPhysiology"/> + <see cref="CareProtocol"/>
    /// + <see cref="PainModel"/>), ticks it, and turns it into body language: animator state (fall → lying
    /// state by AVPU/blood loss/pain), breathing rate & depth, look-at, flinches, skin pallor/cyanosis/sweat,
    /// bleeding visuals and voice. Scenarios talk to this class; the hands-on treatment (S3) talks to the
    /// protocol through it.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class CasualtyController : MonoBehaviour
    {
        public static readonly int StateParam = Animator.StringToHash("State");

        public Animator animator;
        public CasualtyRig rig;
        public CasualtyVoice voice;
        public BloodSoak soak;
        public BloodPoolManager PoolManager;
        [Tooltip("Physiology time multiplier (1 = real time; training mode may accelerate).")]
        public float simulationSpeed = 1f;
        public bool autoTick = true;
        public Phase startPhase = Phase.CareUnderFire;
        [Tooltip("Pose while not injured (scenario start).")]
        public CasualtyPose standingPose = CasualtyPose.CrouchIdle;
        [Tooltip("Minimum seconds in a lying state before the body language may change (avoid flicker).")]
        public float poseHold = 4f;
        public float maxFallSeconds = 5f;

        public PatientPhysiology Patient { get; private set; }
        public CareProtocol Protocol { get; private set; }
        public PainModel Pain { get; private set; }
        public readonly List<WoundSite> WoundSites = new List<WoundSite>();
        public bool IsDown { get; private set; }
        public bool IsFalling { get; private set; }
        public bool recoveryPosition;
        public bool carried;
        /// <summary>Propped on his side by a log roll (procedural): body language stays a lying clip.</summary>
        public bool rolledOnSide;
        public static readonly List<CasualtyController> All = new List<CasualtyController>();
        public CasualtyInteraction Interaction { get; private set; }
        public CasualtyPose CurrentPose { get; private set; } = (CasualtyPose)(-1);
        public float Pallor { get; private set; }
        public float Cyanosis { get; private set; }
        public float Sweat { get; private set; }

        public event Action<CasualtyPose> PoseChanged;
        public event Action<string> PatientEvent;
        /// <summary>w8: a painful procedure made him react (source: tq, pack, needle, npa, shake...; strength 0..1).</summary>
        public event Action<string, float> PainReacted;
        public string LastPainSource { get; private set; }
        public int PainReactionCount { get; private set; }
        [Tooltip("Seconds between two grabs of the medic's wrist (pain reflex).")]
        public float armGrabCooldown = 5f;
        float _lastArmGrab = -99f;
        /// <summary>Face, eyes, eyelids, skin tint, nail beds (w8), built at runtime.</summary>
        public CasualtyFace Face { get; private set; }
        /// <summary>Hands-on checks: pulse, breathing, AVPU, capillary refill (w8).</summary>
        public VitalsChecks Checks { get; private set; }

        float _poseTimer, _fallTimer;
        bool _sawFallState;
        float _lastSpikeFlinch;

        void Awake()
        {
            EnsurePatient();
            if (rig == null) rig = GetComponent<CasualtyRig>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (voice == null) voice = GetComponent<CasualtyVoice>();
            if (soak == null) soak = GetComponent<BloodSoak>();
            if (PoolManager == null) PoolManager = GetComponent<BloodPoolManager>();
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        void Start()
        {
            if (rig != null) rig.Init();
            if (!IsDown) SetPose(standingPose, true);
            EnsureInteraction();
            EnsureLiving();
        }

        /// <summary>w8 "living Erlan": face + checks components (runtime, so the prefab stays a pure character asset).</summary>
        public void EnsureLiving()
        {
            if (rig == null || rig.Bone(HumanBodyBones.Head) == null) return;
            if (Face == null) Face = GetComponent<CasualtyFace>() ?? gameObject.AddComponent<CasualtyFace>();
            Face.casualty = this;
            if (Checks == null) Checks = GetComponent<VitalsChecks>() ?? gameObject.AddComponent<VitalsChecks>();
            Checks.casualty = this;
        }

        /// <summary>
        /// A painful procedure (tourniquet twist, packing stroke, needle, NPA, shake): procedural flinch on the rig, a
        /// voice line or a groan by his AVPU state, a grimace, and — conscious only — he grabs the medic's wrist with
        /// his free hand for a moment (IK hand target on the player's hand). Unresponsive: nothing.
        /// </summary>
        public void PainReaction(float strength, Transform playerHand, string source)
        {
            EnsurePatient();
            var avpu = Patient.AVPU;
            if (Patient.outcome == Outcome.Died || avpu == Consciousness.Unresponsive) return;
            strength = Mathf.Clamp01(strength);
            if (rig != null) rig.Flinch(strength * (avpu == Consciousness.Pain ? 0.6f : 1f));
            if (voice != null) voice.OnPainSpike(this, source == "tq");
            if (Face != null) Face.Grimace(strength);
            if (playerHand != null && avpu <= Consciousness.Voice && Interaction != null && Time.time - _lastArmGrab > armGrabCooldown)
            {
                _lastArmGrab = Time.time;
                Interaction.GrabPlayerArm(playerHand, 1.8f);
            }
            LastPainSource = source;
            PainReactionCount++;
            PainReacted?.Invoke(source, strength);
        }

        /// <summary>Hands-on handles (limbs, drag, roll, wounds, his IFAK) — built at runtime from the rig.</summary>
        public CasualtyInteraction EnsureInteraction()
        {
            if (Interaction == null) Interaction = GetComponent<CasualtyInteraction>();
            if (Interaction == null)
            {
                Interaction = gameObject.AddComponent<CasualtyInteraction>();
                Interaction.casualty = this;
            }
            Interaction.Build();
            return Interaction;
        }

        /// <summary>Nearest casualty to a point (within maxDistance of his hips).</summary>
        public static CasualtyController Nearest(Vector3 p, float maxDistance = 2.5f)
        {
            CasualtyController best = null;
            float bd = maxDistance;
            foreach (var c in All)
            {
                if (c == null) continue;
                var hips = c.rig != null ? c.rig.Bone(HumanBodyBones.Hips) : null;
                float d = Vector3.Distance(p, hips != null ? hips.position : c.transform.position);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        public void EnsurePatient()
        {
            if (Patient != null) return;
            Patient = new PatientPhysiology();
            Protocol = new CareProtocol(Patient, startPhase);
            Pain = new PainModel();
            Patient.OnEvent += e => PatientEvent?.Invoke(e);
            Pain.OnPainSpike += _ => { if (voice != null) voice.OnPainSpike(this, false); };
        }

        /// <summary>Reset to an uninjured casualty (new scenario).</summary>
        public void ResetPatient()
        {
            foreach (var w in WoundSites) if (w != null) DestroySafe(w.gameObject);
            WoundSites.Clear();
            Patient = null;
            EnsurePatient();
            IsDown = IsFalling = false;
            recoveryPosition = carried = rolledOnSide = false;
            SetPose(standingPose, true);
        }

        // ------------------------------------------------------------------ wounds
        struct Placement
        {
            public HumanBodyBones bone, child;
            public float along;
            public Func<CasualtyRig, Vector3> dir;
        }

        static Placement Place(BodyRegion r)
        {
            Placement P(HumanBodyBones b, HumanBodyBones c, float a, Func<CasualtyRig, Vector3> d) =>
                new Placement { bone = b, child = c, along = a, dir = d };
            switch (r)
            {
                // inner (medial, slightly anterior) thigh: femoral artery territory
                case BodyRegion.ThighR: return P(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.45f, k => (-k.BodyRight * 0.8f + LegAnterior(k, false)).normalized);
                case BodyRegion.ThighL: return P(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.45f, k => (k.BodyRight * 0.8f + LegAnterior(k, true)).normalized);
                case BodyRegion.ShinR: return P(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.45f, k => LegAnterior(k, false));
                case BodyRegion.ShinL: return P(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.45f, k => LegAnterior(k, true));
                case BodyRegion.ForearmL: return P(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.5f, k => k.BodyUp);
                case BodyRegion.ForearmR: return P(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.5f, k => k.BodyUp);
                case BodyRegion.UpperArmL: return P(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.5f, k => -k.BodyRight);
                case BodyRegion.UpperArmR: return P(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.5f, k => k.BodyRight);
                case BodyRegion.ChestR: return P(HumanBodyBones.Chest, HumanBodyBones.Neck, 0.75f, k => (k.BodyForward + k.BodyRight * 0.45f).normalized);
                case BodyRegion.ChestL: return P(HumanBodyBones.Chest, HumanBodyBones.Neck, 0.75f, k => (k.BodyForward - k.BodyRight * 0.45f).normalized);
                case BodyRegion.GroinR: return P(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.06f, k => (k.BodyForward - k.BodyRight * 0.3f).normalized);
                case BodyRegion.GroinL: return P(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.06f, k => (k.BodyForward + k.BodyRight * 0.3f).normalized);
                case BodyRegion.Abdomen: return P(HumanBodyBones.Spine, HumanBodyBones.Chest, 0.5f, k => k.BodyForward);
                case BodyRegion.Pelvis: return P(HumanBodyBones.Hips, HumanBodyBones.Spine, 0.2f, k => -k.BodyForward); // buttock
                case BodyRegion.Neck: return P(HumanBodyBones.Neck, HumanBodyBones.Head, 0.5f, k => k.BodyRight);
                case BodyRegion.Head: return P(HumanBodyBones.Head, HumanBodyBones.LastBone, 0.6f, k => k.BodyRight);
                default: return P(HumanBodyBones.Hips, HumanBodyBones.Spine, 0.5f, k => k.BodyForward);
            }
        }

        /// <summary>Front of the thigh in the current pose: opposite to where the knee bends the shin
        /// (falls back to the body front for a straight leg).</summary>
        static Vector3 LegAnterior(CasualtyRig k, bool left)
        {
            var hip = k.Bone(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var knee = k.Bone(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var foot = k.Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            if (hip == null || knee == null || foot == null) return k.BodyForward;
            Vector3 axis = (knee.position - hip.position).normalized;
            Vector3 shin = Vector3.ProjectOnPlane(foot.position - knee.position, axis);
            if (shin.magnitude < 0.12f) return Vector3.ProjectOnPlane(k.BodyForward, axis).normalized;
            return -shin.normalized;
        }

        /// <summary>Adds a wound to the medical model and its visible site on the body.</summary>
        public WoundSite AddWound(WoundType type, BodyRegion region, WoundVisual visual, string id = null, bool exitSide = false)
        {
            EnsurePatient();
            var w = new Wound { id = id ?? $"{type}_{region}", type = type, region = region };
            if (type == WoundType.PenetratingChest && Patient.pneumo == PneumoState.None) Patient.pneumo = PneumoState.Open;
            Patient.wounds.Add(w);
            return AttachSite(w, visual, exitSide);
        }

        WoundSite AttachSite(Wound w, WoundVisual visual, bool exitSide)
        {
            if (rig != null) rig.Init();
            var go = new GameObject("Wound_" + w.id);
            var site = go.AddComponent<WoundSite>();
            var s = BloodSettings.Load();
            Transform parent = transform;
            Vector3 pos = transform.position + Vector3.up * 0.5f, normal = transform.forward;

            if (visual == WoundVisual.AmputationStump && rig != null && rig.Bone(HumanBodyBones.RightFoot) != null)
            {
                bool left = BodyRegions.IsLeftSide(w.region);
                var foot = rig.Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                var shin = rig.Bone(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                parent = shin;
                pos = foot.position;
                normal = (foot.position - shin.position).normalized; // blood leaves along the limb axis
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(normal, rig.BodyForward));
                if (s != null && s.stumpPrefab != null)
                {
                    var stump = Instantiate(s.stumpPrefab, go.transform);
                    stump.name = "Stump";
                    // model: +Y = towards the knee
                    stump.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, -normal) * Quaternion.identity);
                }
                foot.localScale = Vector3.one * 0.001f; // the foot is gone
                site.size = 0.1f;
            }
            else
            {
                if (rig != null)
                {
                    var pl = Place(w.region);
                    Vector3 dir = pl.dir(rig);
                    if (exitSide) dir = -dir;
                    if (rig.SurfacePoint(pl.bone, pl.child, pl.along, dir, out var p, out var n)) { pos = p; normal = n; }
                    parent = rig.Bone(pl.bone) ?? transform;
                }
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(normal));
                site.size = visual switch
                {
                    WoundVisual.RaggedLaceration => 0.085f,
                    WoundVisual.ExitWound => 0.08f,
                    WoundVisual.ChestEntry => 0.045f,
                    _ => 0.05f,
                };
                Material m = s == null ? null : visual switch
                {
                    WoundVisual.RaggedLaceration => s.woundRagged,
                    WoundVisual.ChestEntry => s.woundChestEntry,
                    WoundVisual.ExitWound => s.woundExit,
                    _ => s.woundFragment,
                };
                var decal = new GameObject("Decal");
                decal.transform.SetParent(go.transform, false);
                decal.transform.localPosition = new Vector3(0f, 0f, 0.004f);
                decal.transform.localRotation = Quaternion.Euler(0f, 180f, UnityEngine.Random.Range(0f, 360f));
                decal.transform.localScale = new Vector3(site.size * (visual == WoundVisual.RaggedLaceration ? 1.3f : 1f), site.size, 1f);
                decal.AddComponent<MeshFilter>().sharedMesh = BloodPoolManager.Quad();
                var r = decal.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                site.decal = r;
            }
            site.Bind(this, w, visual);
            WoundSites.Add(site);
            if (rig != null && w.limbPosition < 0f && BodyRegions.IsLimb(w.region))
                w.limbPosition = LimbMath.WoundLimbPosition(rig, w.region, site.transform.position);
            if (Interaction != null) Interaction.SyncWoundCare();
            return site;
        }

        // ------------------------------------------------------------------ events
        /// <summary>He gets hit: fall animation, then lying. Wounds are added separately (scenario).</summary>
        public void Hit(HitKind kind)
        {
            EnsurePatient();
            IsDown = true;
            IsFalling = true;
            _fallTimer = 0f;
            _sawFallState = false;
            SetPose(CasualtyPoseMapper.FallFor(kind), true);
            if (voice != null) voice.OnHit(this);
            if (rig != null) rig.Flinch(1f);
        }

        /// <summary>Scenario start with the casualty already down (S7/S8): lying, no fall animation.</summary>
        public void StartDown()
        {
            EnsurePatient();
            IsDown = true;
            IsFalling = false;
            SetPose(CasualtyPoseMapper.LyingPose(Patient, Pain.level, recoveryPosition), true);
        }

        /// <summary>Tourniquet through the protocol (S3 drives this from the physical CAT state machine).</summary>
        public Feedback ApplyTourniquet(BodyRegion site, int halfTurns = 2)
        {
            var fb = Protocol.Perform(MedAction.Tourniquet, site: site);
            var limb = BodyRegions.LimbId(site);
            if (limb != null && Patient.tourniquetSeconds.ContainsKey(limb))
            {
                Pain.TourniquetTwist(limb, halfTurns);
                PainReaction(0.8f, null, "tq");
            }
            return fb;
        }

        /// <summary>Limb handled at speed near a wound (grab/lift). Called by S3 interactions each frame.</summary>
        public void Handle(Vector3 worldPoint, float speed, float dt)
        {
            float best = float.MaxValue;
            foreach (var w in WoundSites) if (w != null) best = Mathf.Min(best, Vector3.Distance(worldPoint, w.transform.position));
            float before = Pain.spike;
            Pain.AddHandling(best, speed, dt);
            if (rig != null && Pain.spike - before > 0.02f && Time.time - _lastSpikeFlinch > 0.4f)
            {
                rig.Flinch(Mathf.Clamp01((Pain.spike - before) * 8f));
                _lastSpikeFlinch = Time.time;
            }
        }

        // ------------------------------------------------------------------ per-frame
        void Update()
        {
            if (autoTick) Simulate(Time.deltaTime);
        }

        public void Simulate(float dt)
        {
            if (dt <= 0f) return;
            EnsurePatient();
            float sdt = dt * Mathf.Max(0f, simulationSpeed);
            if (IsDown) { Patient.recoveryPosition = rolledOnSide || recoveryPosition; Patient.Tick(sdt); }
            Pain.Tick(Patient, sdt);
            UpdatePose(dt);
            UpdateBody(dt);
            foreach (var w in WoundSites) if (w != null) w.Simulate(dt, IsDown ? sdt : 0f);
            if (soak != null) soak.Apply(this);
            if (voice != null) voice.Tick(this, dt);
        }

        void UpdatePose(float dt)
        {
            _poseTimer += dt;
            if (!IsDown) { SetPose(carried ? CasualtyPose.Carried : standingPose); return; }
            if (IsFalling)
            {
                _fallTimer += dt;
                bool done = _fallTimer > maxFallSeconds;
                if (animator != null && animator.runtimeAnimatorController != null && animator.isInitialized)
                {
                    var st = animator.GetCurrentAnimatorStateInfo(0);
                    bool inFall = st.IsName(CurrentPose.ToString());
                    if (inFall) _sawFallState = true;
                    if (_sawFallState && inFall && !animator.IsInTransition(0) && st.normalizedTime >= 0.93f) done = true;
                }
                if (!done) return;
                IsFalling = false;
                SetPose(CasualtyPoseMapper.LyingPose(Patient, Pain.level, recoveryPosition), true);
                return;
            }
            if (carried) { SetPose(CasualtyPose.Carried); return; }
            var want = CasualtyPoseMapper.LyingPose(Patient, Pain.level, recoveryPosition && !rolledOnSide);
            if (rolledOnSide && want == CasualtyPose.LyingWrithing) want = CasualtyPose.LyingMoaning; // no thrashing on the side
            bool urgent = want == CasualtyPose.LyingDying || want == CasualtyPose.Unconscious || want == CasualtyPose.RecoverySide;
            if (want != CurrentPose && (urgent || _poseTimer >= poseHold)) SetPose(want);
        }

        void UpdateBody(float dt)
        {
            var p = Patient;
            float loss = p.LossFraction;
            Pallor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.4f, loss));
            Cyanosis = Mathf.Clamp01(Mathf.InverseLerp(94f, 70f, p.SpO2) + Mathf.InverseLerp(0.35f, 0.5f, loss) * 0.5f);
            Sweat = p.HemorrhageClass >= 2 || p.tensionSeverity > 0.3f ? Mathf.Clamp01(Sweat + dt * 0.05f) : Mathf.Max(0f, Sweat - dt * 0.02f);
            if (rig == null) return;
            bool dead = p.outcome == Outcome.Died;
            rig.breathRate = dead ? 0f : p.RespRate;
            rig.breathDepth = dead ? 0f : Mathf.Clamp01(0.35f + 0.5f * p.LossFraction + 0.4f * p.tensionSeverity - (p.pneumo == PneumoState.Tension ? 0.2f : 0f));
            rig.agonal = !dead && (loss > 0.42f || (p.airwayObstructed && !p.airwayAdjunct));
            var avpu = p.AVPU;
            rig.lookWeight = dead || IsFalling ? 0f : avpu == Consciousness.Alert ? 1f : avpu == Consciousness.Voice ? 0.45f : 0f;
        }

        public void SetPose(CasualtyPose pose, bool force = false)
        {
            if (pose == CurrentPose && !force) return;
            CurrentPose = pose;
            _poseTimer = 0f;
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetInteger(StateParam, (int)pose);
            PoseChanged?.Invoke(pose);
        }

        static void DestroySafe(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
