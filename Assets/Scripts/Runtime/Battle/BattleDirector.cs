using System.Collections.Generic;
using FieldMedic.Audio;
using FieldMedic.Environment;
using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>
    /// The battle that never stops (GDD_V2 §2). Every 5–20 s (faster when the assault swells) it fires a weighted
    /// event — a wave, an artillery barrage, a jet bomb run, a helicopter rocket run, machine-gun bursts with tracers,
    /// an FPV dive, a tank shot — on top of the always-running layers (near infantry, vehicles, mid crowd, horizon
    /// impostors, the ambience's distant artillery/gunfire/drones and the audio beds). Fine medical steps request
    /// "focus": 3–5 s with fewer and farther blasts near the player, while the background never goes silent.
    /// Training mode keeps every blast far away and the enemy never targets the player.
    /// </summary>
    public sealed class BattleDirector : MonoBehaviour
    {
        public static BattleDirector Instance { get; private set; }

        /// <summary>Game mode (set by the scenario/menu flow, w4). Default: Combat.</summary>
        public static BattleMode Mode = BattleMode.Combat;
        /// <summary>Whether a run is active that allows the player to be hit; null = not managed (sandbox/tests).</summary>
        public static bool? PlayerArmed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Mode = BattleMode.Combat; PlayerArmed = null; }

        public BattleSettings settings;
        public Transform head;
        [Tooltip("Points on our side the enemy shells/tanks aim at (ruins, cover line, wrecks).")]
        public Vector3[] ourTargets = new Vector3[0];
        [Tooltip("Enemy MG nests (fire with tracers during MG events).")]
        public Vector3[] mgNests = new Vector3[0];
        public bool autoRun = true;

        public BattleEventPicker Picker { get; private set; }
        public IntensityCurve Curve { get; private set; }
        public int EventsFired { get; private set; }
        public readonly int[] EventCounts = new int[7];
        public BattleEventType LastEvent { get; private set; }
        public float NextEventAt => _nextEvent;

        float _nextEvent;
        readonly HashSet<HandGrabber> _hooked = new HashSet<HandGrabber>();
        float _hookScan;

        // scheduled one-shots (no coroutines → no allocations)
        enum Job : byte { None, Blast, FieldBlast, Whistle, Impact, Burst }
        struct Pending { public float t; public Job job; public Vector3 a, b; public float charge; public int n; }
        const int Queue = 96;
        readonly Pending[] _q = new Pending[Queue];

        void Awake()
        {
            Instance = this;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            var s = settings;
            Picker = new BattleEventPicker(s != null ? s.eventWeights : null, s != null ? s.seed : 1234);
            if (s != null) { Picker.minInterval = s.eventInterval.x; Picker.maxInterval = s.eventInterval.y; }
            Curve = new IntensityCurve();
            if (s != null)
            {
                Curve.swellAmplitude = s.swellAmplitude; Curve.swellPeriod = s.swellPeriod;
                Curve.focusLevel = s.focusLevel; Curve.backgroundFloor = s.backgroundFloor;
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start() => _nextEvent = Time.time + 3f;

        /// <summary>Medical fine-motor step: quieter near the player for a few seconds.</summary>
        public static void RequestFocus(float seconds = -1f)
        {
            var d = Instance;
            if (d == null) return;
            if (seconds < 0f) seconds = d.settings != null ? d.settings.focusSeconds : 4f;
            d.Curve.RequestFocus(Time.time, seconds);
        }

        public bool InFocus => Curve.InFocus(Time.time);
        Vector3 HeadPos => head != null ? head.position : Vector3.up * 1.6f;

        float MinNearDistance
        {
            get
            {
                if (Mode == BattleMode.Training) return 70f;
                if (InFocus) return settings != null ? settings.focusSafeRadius : 60f;
                return settings != null ? settings.nearBlastMin : 16f;
            }
        }

        void Update()
        {
            float now = Time.time;
            HookGrabbers(now);
            float bg = Curve.Background(now), near = Curve.Near(now);
            var amb = CombatAmbience.Instance;
            if (amb != null) amb.intensity = bg;
            var inf = NearInfantry.Instance;
            if (inf != null) inf.NearIntensity = near;
            RunQueue(now);
            if (!autoRun) return;
            if (now >= _nextEvent)
            {
                Fire(Picker.Next());
                _nextEvent = now + Picker.NextInterval(bg);
            }
        }

        // ------------------------------------------------------------------ focus hooks
        void HookGrabbers(float now)
        {
            if (now < _hookScan) return;
            _hookScan = now + 2f;
            var all = HandGrabber.All;
            for (int i = 0; i < all.Count; i++)
            {
                var h = all[i];
                if (h == null || _hooked.Contains(h)) continue;
                _hooked.Add(h);
                h.GrabStarted += OnGrab;
            }
        }

        void OnGrab(HandGrabber hand, Grabbable g)
        {
            if (g == null) return;
            // treatment items and the casualty's limbs/handles are fine-motor work
            if (g is FieldMedic.Treatment.MedItem || g.GetComponentInParent<FieldMedic.Casualty.CasualtyController>() != null)
                RequestFocus();
        }

        // ------------------------------------------------------------------ events
        /// <summary>Runs one event now (public for tests and the debug menu).</summary>
        public void Fire(BattleEventType e)
        {
            EventsFired++;
            EventCounts[(int)e]++;
            LastEvent = e;
            switch (e)
            {
                case BattleEventType.Wave: Wave(); break;
                case BattleEventType.ArtilleryBarrage: Barrage(); break;
                case BattleEventType.AirStrike:
                    if (AirLayer.Instance == null || !AirLayer.Instance.JetRun(EnemyField(90f, 140f))) Barrage();
                    break;
                case BattleEventType.HelicopterRun:
                    if (AirLayer.Instance == null || !AirLayer.Instance.HeliRun(EnemyField(95f, 150f))) MgBurst();
                    break;
                case BattleEventType.MgBurst: MgBurst(); break;
                case BattleEventType.DroneDive: DroneDive(); break;
                case BattleEventType.TankShot: TankShot(); break;
            }
        }

        Vector3 EnemyField(float zMin, float zMax)
        {
            var p = new Vector3(Picker.Range(-70f, 70f), 0f, Picker.Range(zMin, zMax));
            p.y = BattlefieldLayout.GroundHeight(p.x, p.z);
            return p;
        }

        /// <summary>A point on our side at least <see cref="MinNearDistance"/> from the player.</summary>
        bool OurSide(out Vector3 p)
        {
            float min = MinNearDistance;
            Vector3 h = HeadPos; h.y = 0f;
            for (int k = 0; k < 8; k++)
            {
                Vector3 c = ourTargets.Length > 0 ? ourTargets[Random.Range(0, ourTargets.Length)] : new Vector3(Picker.Range(-30f, 30f), 0f, Picker.Range(-10f, 12f));
                c += new Vector3(Picker.Range(-4f, 4f), 0f, Picker.Range(-3f, 3f));
                Vector3 flat = new Vector3(c.x, 0f, c.z);
                if ((flat - h).magnitude < min)
                {
                    // push it out to the safe ring, keeping the direction
                    Vector3 dir = flat - h;
                    if (dir.sqrMagnitude < 0.01f) dir = new Vector3(Picker.Range(-1f, 1f), 0f, 1f);
                    flat = h + dir.normalized * Picker.Range(min, min + 15f);
                }
                p = new Vector3(flat.x, BattlefieldLayout.GroundHeight(flat.x, flat.z), flat.z);
                return true;
            }
            p = default;
            return false;
        }

        void Wave()
        {
            CrowdLayer.Instance?.StartSurge(Picker.Range(10f, 16f));
            var inf = NearInfantry.Instance;
            if (inf != null)
                for (int i = 0; i < inf.enemies.Count; i++)
                {
                    var a = inf.enemies[i];
                    if (a != null && a.State == SoldierState.Inactive) a.stateUntil = 0f; // respawn now
                }
            // their machine guns cover the rush
            for (int k = 0; k < 3; k++) ScheduleBurst(Time.time + k * Picker.Range(1.2f, 2.5f));
        }

        void Barrage()
        {
            int n = Random.Range(4, 10);
            float t = Time.time;
            bool incoming = Mode != BattleMode.Training && Random.value < 0.45f;
            for (int i = 0; i < n; i++)
            {
                t += Picker.Range(0.35f, 1.1f);
                if (incoming && i % 2 == 0 && OurSide(out var ours))
                {
                    // enemy shells on our position: whistle ~2 s before the blast
                    Schedule(t, Job.Whistle, Vector3.Lerp(HeadPos, ours, 0.6f) + Vector3.up * 14f, default, 0f);
                    Schedule(t + 2f, Job.Blast, ours, default, Picker.Range(0.8f, 1.2f));
                }
                else Schedule(t, Job.FieldBlast, EnemyField(55f, 170f), default, Picker.Range(0.9f, 1.6f));
            }
        }

        void MgBurst()
        {
            float t = Time.time;
            ScheduleBurst(t);
            if (Random.value < 0.5f) ScheduleBurst(t + Picker.Range(0.8f, 2f));
        }

        void ScheduleBurst(float t)
        {
            Vector3 from = mgNests.Length > 0 ? mgNests[Random.Range(0, mgNests.Length)] : EnemyField(90f, 130f) + Vector3.up;
            Vector3 to = ourTargets.Length > 0 ? ourTargets[Random.Range(0, ourTargets.Length)] + Vector3.up * Picker.Range(0.8f, 2.5f) : new Vector3(0f, 1.5f, 6f);
            // friendly MG answers sometimes (tracers outbound)
            if (Random.value < 0.35f) { var tmp = from; from = to + Vector3.up * 0.3f; to = tmp; }
            Schedule(t, Job.Burst, from, to, 0f, Random.Range(8, 18));
        }

        void DroneDive()
        {
            var amb = CombatAmbience.Instance;
            if (amb == null) return;
            Vector3 target;
            var vl = VehicleLayer.Instance;
            // our FPVs hunt their armour; theirs hunt our positions (never closer than the safe ring)
            if (vl != null && vl.vehicles.Count > 0 && Random.value < 0.55f)
            {
                var v = vl.vehicles[Random.Range(0, vl.vehicles.Count)];
                target = v != null ? v.transform.position + Vector3.up * 1.5f : EnemyField(60f, 100f);
            }
            else if (Mode != BattleMode.Training && OurSide(out var ours)) target = ours + Vector3.up * 0.5f;
            else target = EnemyField(60f, 110f);
            amb.PlayFpvDive(target);
        }

        void TankShot()
        {
            var vl = VehicleLayer.Instance;
            if (vl == null) { Barrage(); return; }
            Vector3 target;
            if (Mode == BattleMode.Training || !OurSide(out target)) target = EnemyField(40f, 60f) + Vector3.back * 30f;
            target.y = BattlefieldLayout.GroundHeight(target.x, target.z) + Picker.Range(0.3f, 2f);
            vl.RequestShot(target);
        }

        // ------------------------------------------------------------------ queue
        void Schedule(float t, Job job, Vector3 a, Vector3 b, float charge, int n = 0)
        {
            for (int i = 0; i < Queue; i++)
            {
                if (_q[i].job != Job.None) continue;
                _q[i] = new Pending { t = t, job = job, a = a, b = b, charge = charge, n = n };
                return;
            }
        }

        public int PendingJobs { get { int n = 0; for (int i = 0; i < Queue; i++) if (_q[i].job != Job.None) n++; return n; } }

        void RunQueue(float now)
        {
            for (int i = 0; i < Queue; i++)
            {
                if (_q[i].job == Job.None || now < _q[i].t) continue;
                var j = _q[i];
                _q[i].job = Job.None;
                switch (j.job)
                {
                    case Job.Blast:
                        // re-check the safe ring at impact time (focus may have started meanwhile)
                        if (Vector3.Distance(new Vector3(j.a.x, 0f, j.a.z), new Vector3(HeadPos.x, 0f, HeadPos.z)) < MinNearDistance * 0.9f)
                        {
                            Vector3 dir = j.a - HeadPos; dir.y = 0f;
                            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                            j.a = HeadPos + dir.normalized * MinNearDistance;
                            j.a.y = BattlefieldLayout.GroundHeight(j.a.x, j.a.z);
                        }
                        BattleFx.Instance?.Blast(j.a, j.charge);
                        break;
                    case Job.FieldBlast:
                        BattleFx.Instance?.Blast(j.a, j.charge, j.charge > 1.4f);
                        NearInfantry.Instance?.BlastAt(j.a, 7f);
                        CrowdLayer.Instance?.BlastAt(j.a, 9f);
                        break;
                    case Job.Whistle:
                    {
                        var amb = CombatAmbience.Instance;
                        if (amb != null && amb.incomingWhistle != null && amb.incomingWhistle.Length > 0)
                            amb.PlayAt(amb.incomingWhistle[Random.Range(0, amb.incomingWhistle.Length)], j.a, 0.8f, 10f, 400f);
                        break;
                    }
                    case Job.Burst:
                    {
                        Vector3 dir = (j.b - j.a).normalized;
                        CombatVfx.Instance?.TracerBurst(j.a + dir * 2f, dir, Mathf.Max(3, j.n / 3));
                        BattleAudio.Instance?.Gunfire(j.a, true, 1f);
                        // a few rounds land around the aim point (impacts visible from our position)
                        int hits = Mathf.Min(6, j.n / 2);
                        for (int k = 0; k < hits; k++)
                            Schedule(now + Vector3.Distance(j.a, j.b) / 700f + k * 0.09f, Job.Impact, j.a, j.b + Random.insideUnitSphere * 1.6f, 0f);
                        break;
                    }
                    case Job.Impact:
                    {
                        Vector3 d = j.b - j.a;
                        float len = d.magnitude;
                        if (len < 1f) break;
                        if (head != null && (j.b - head.position).sqrMagnitude > 60f * 60f) break;
                        if (Physics.Raycast(j.a, d / len, out var hit, len + 5f, CoverMath.CoverMask | 1, QueryTriggerInteraction.Ignore))
                        {
                            bool hard = NearInfantry.Instance != null && NearInfantry.Instance.IsHard(hit.collider);
                            BattleFx.Instance?.Impact(hit.point, hit.normal, hard);
                            var ba = BattleAudio.Instance;
                            if (ba != null && Random.value < 0.4f) ba.Play(BattleAudio.Pick(hard ? ba.impactHard : ba.impactDirt), hit.point, 0.6f, 2f, 50f);
                        }
                        break;
                    }
                }
            }
        }
    }
}
