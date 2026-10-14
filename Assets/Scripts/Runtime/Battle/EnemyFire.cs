using System;
using FieldMedic.Environment;
using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>
    /// Real enemy fire at the player (GDD_V2 §2, w3 task 3). Shooters = near enemies in a firing position + dug-in
    /// machine-gun nests. Several times a second every shooter ray-casts to the player's head and chest against the
    /// Cover layer, the ground profile and active smoke screens. Any open line = exposed:
    ///  - near misses: supersonic cracks past the head, dirt/concrete impacts next to the player, tracers;
    ///  - suppression: slight vignette + heavy breathing;
    ///  - Combat / Exam: cumulative exposure &gt; limit (≈4 s in 10 s) → the player is hit → scenario fails with an
    ///    explanation (through <see cref="ThreatSystem"/> so the scenario director and debrief get it).
    /// Hidden behind cover the rounds smack into the cover in front of him — proof that cover works.
    /// Training: the enemy never targets the player.
    /// </summary>
    public sealed class EnemyFire : MonoBehaviour
    {
        public static EnemyFire Instance { get; private set; }

        public BattleSettings settings;
        public Transform head;
        [Tooltip("Dug-in enemy machine-gun positions (always manned).")]
        public Vector3[] mgNests = new Vector3[0];
        [Tooltip("When no scenario director exists: whether the player can be targeted (tests, sandbox).")]
        public bool armedWithoutScenario = true;
        public SuppressionFx suppressionFx;

        public ExposureTracker Tracker { get; private set; }
        public bool Exposed { get; private set; }
        public bool PlayerTargetable { get; private set; }
        public float Suppression { get; private set; }
        public int NearMisses { get; private set; }
        public int CoverHits { get; private set; }
        public int Checks { get; private set; }
        public bool PlayerWasHit { get; private set; }
        /// <summary>Optional override (tests): when set, decides whether fire at the player is allowed.</summary>
        public Func<bool> targetableOverride;

        public event Action<string> PlayerHit;

        float _checkAcc, _nextNearMiss, _nextCoverHit;
        Vector3 _lastShooter;
        bool _hasShooter;
        int _rayMask;

        void Awake()
        {
            Instance = this;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            Tracker = new ExposureTracker(settings != null ? settings.exposureWindow : 10f, settings != null ? settings.exposureLimit : 4f);
            _rayMask = CoverMath.CoverMask;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEnable()
        {
            if (NearInfantry.Instance != null) NearInfantry.Instance.playerAimProvider = AimAtPlayer;
        }

        void Start()
        {
            if (NearInfantry.Instance != null) NearInfantry.Instance.playerAimProvider = AimAtPlayer;
        }

        public void ResetExposure()
        {
            Tracker.Reset();
            PlayerWasHit = false;
            Exposed = false;
            Suppression = 0f;
        }

        bool ComputeTargetable()
        {
            if (targetableOverride != null) return targetableOverride();
            if (BattleDirector.Mode == BattleMode.Training) return false;
            return BattleDirector.PlayerArmed ?? armedWithoutScenario;
        }

        /// <summary>Chest point (the torso is hit even when only the shoulders show).</summary>
        public Vector3 Torso => head.position + Vector3.down * 0.42f;

        void Update()
        {
            if (head == null) return;
            float dt = Time.deltaTime;
            PlayerTargetable = ComputeTargetable() && !PlayerWasHit;
            float rate = settings != null ? settings.checkRate : 10f;
            _checkAcc += dt;
            if (_checkAcc >= 1f / rate)
            {
                float step = _checkAcc;
                _checkAcc = 0f;
                Exposed = PlayerTargetable && CheckExposure();
                Checks++;
                if (PlayerTargetable && Tracker.Tick(Exposed, step)) OnHit();
                else if (!PlayerTargetable) Tracker.Tick(false, step);
            }
            if (PlayerTargetable) FireEffects(Time.time);
            float decay = settings != null ? settings.suppressionDecay : 0.3f;
            Suppression = Mathf.Max(0f, Suppression - decay * dt);
            if (suppressionFx != null) suppressionFx.Amount = Mathf.Max(Suppression, Tracker.Exposure01 * 0.6f);
            BattleAudio.Instance?.SetBreathing(Mathf.Max(Suppression, Tracker.Exposure01));
        }

        /// <summary>True when at least one shooter has an open line to the head or chest.</summary>
        public bool CheckExposure()
        {
            _hasShooter = false;
            Vector3 h = head.position, t = Torso;
            bool exposed = false;
            var inf = NearInfantry.Instance;
            if (inf != null)
            {
                for (int i = 0; i < inf.enemies.Count; i++)
                {
                    var a = inf.enemies[i];
                    if (a == null) continue;
                    a.seesPlayer = false;
                    if (!a.InFiringPosition) continue;
                    Vector3 m = a.Muzzle;
                    if (LineOpen(m, h) || LineOpen(m, t))
                    {
                        a.seesPlayer = true;
                        exposed = true;
                        if (!_hasShooter || UnityEngine.Random.value < 0.4f) { _lastShooter = m; _hasShooter = true; }
                    }
                }
            }
            for (int i = 0; i < mgNests.Length; i++)
            {
                Vector3 m = mgNests[i];
                if (LineOpen(m, h) || LineOpen(m, t))
                {
                    exposed = true;
                    if (!_hasShooter || UnityEngine.Random.value < 0.3f) { _lastShooter = m; _hasShooter = true; }
                }
            }
            if (!exposed) PickCoverShooter();
            return exposed;
        }

        void PickCoverShooter()
        {
            // the nearest active shooter keeps firing at the player's cover
            var inf = NearInfantry.Instance;
            if (inf != null)
                for (int i = 0; i < inf.enemies.Count; i++)
                {
                    var a = inf.enemies[i];
                    if (a != null && a.InFiringPosition) { _lastShooter = a.Muzzle; _hasShooter = true; return; }
                }
            if (mgNests.Length > 0) { _lastShooter = mgNests[0]; _hasShooter = true; }
        }

        /// <summary>Open line of fire: no Cover-layer collider, no ground swell/crater lip, no dense smoke.</summary>
        public bool LineOpen(Vector3 from, Vector3 to)
        {
            if (SmokeScreens.Blocks(from, to)) return false;
            if (GroundBlocks(from, to)) return false;
            return !CoverMath.Blocked(from, to, out _, _rayMask);
        }

        /// <summary>Samples the analytic terrain along the line (crater rims, swells hide a prone player).</summary>
        public static bool GroundBlocks(Vector3 from, Vector3 to)
        {
            const int N = 12;
            for (int i = 1; i < N; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)N);
                if (p.y < BattlefieldLayout.GroundHeight(p.x, p.z) - 0.05f) return true;
            }
            return false;
        }

        Vector3? AimAtPlayer(SoldierAgent a)
        {
            if (!PlayerTargetable || head == null) return null;
            if (a.seesPlayer)
                return head.position + new Vector3(UnityEngine.Random.Range(-1.2f, 1.2f), UnityEngine.Random.Range(-0.9f, 0.6f), UnityEngine.Random.Range(-1.2f, 1.2f));
            // hidden: they still pin the player's cover
            if (UnityEngine.Random.value < 0.35f) return head.position + Vector3.down * 0.3f;
            return null;
        }

        void FireEffects(float now)
        {
            if (!_hasShooter) return;
            var ba = BattleAudio.Instance;
            var fx = BattleFx.Instance;
            if (Exposed)
            {
                if (now < _nextNearMiss) return;
                Vector2 r = settings != null ? settings.nearMissRate : new Vector2(1.5f, 4f);
                float perSec = Mathf.Lerp(r.x, r.y, Tracker.Exposure01);
                _nextNearMiss = now + UnityEngine.Random.Range(0.5f, 1.5f) / perSec;
                NearMisses++;
                Suppression = Mathf.Min(1f, Suppression + 0.22f);
                Vector3 h = head.position;
                // crack passes within a metre of the head
                Vector3 side = Vector3.Cross(Vector3.up, (h - _lastShooter).normalized);
                Vector3 pass = h + side * UnityEngine.Random.Range(-0.9f, 0.9f) + Vector3.up * UnityEngine.Random.Range(0.1f, 0.6f);
                if (ba != null) ba.Play(BattleAudio.Pick(UnityEngine.Random.value < 0.7f ? ba.bulletCrack : ba.bulletWhiz), pass, 0.9f, 1.5f, 60f, UnityEngine.Random.Range(0.9f, 1.15f));
                // the round lands next to the player
                Vector3 miss = h + side * UnityEngine.Random.Range(-1.6f, 1.6f) + Vector3.down * UnityEngine.Random.Range(0.4f, 1.6f) + (h - _lastShooter).normalized * UnityEngine.Random.Range(0.5f, 3f);
                Vector3 dir = (miss - _lastShooter).normalized;
                if (Physics.Raycast(_lastShooter, dir, out var hit, Vector3.Distance(_lastShooter, miss) + 8f, _rayMask | 1, QueryTriggerInteraction.Ignore))
                {
                    bool hard = NearInfantry.Instance != null && NearInfantry.Instance.IsHard(hit.collider);
                    fx?.Impact(hit.point, hit.normal, hard);
                    if (ba != null) ba.Play(BattleAudio.Pick(hard ? ba.impactHard : ba.impactDirt), hit.point, 0.8f, 2f, 50f, UnityEngine.Random.Range(0.9f, 1.1f));
                }
                if (NearMisses % 3 == 0) CombatVfx.Instance?.TracerBurst(_lastShooter + dir * 3f, (pass - _lastShooter).normalized, 1);
            }
            else
            {
                float rate = settings != null ? settings.coverHitRate : 0.6f;
                if (rate <= 0f || now < _nextCoverHit) return;
                _nextCoverHit = now + UnityEngine.Random.Range(0.4f, 1.6f) / rate;
                Vector3 target = head.position + new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), UnityEngine.Random.Range(-0.7f, 0.1f), 0f);
                if (CoverMath.Blocked(_lastShooter, target, out var hit, _rayMask) && Vector3.Distance(hit.point, head.position) < 8f)
                {
                    CoverHits++;
                    Suppression = Mathf.Min(1f, Suppression + 0.06f);
                    bool hard = NearInfantry.Instance != null && NearInfantry.Instance.IsHard(hit.collider);
                    fx?.Impact(hit.point, hit.normal, hard);
                    if (ba != null) ba.Play(BattleAudio.Pick(hard ? ba.impactHard : ba.impactDirt), hit.point, 0.75f, 2f, 50f, UnityEngine.Random.Range(0.9f, 1.1f));
                }
            }
        }

        void OnHit()
        {
            PlayerWasHit = true;
            Debug.Log($"[FMVR] enemy fire: player hit after {Tracker.Exposed:F1} s exposed in {Tracker.Window:F0} s");
            PlayerHit?.Invoke("enemy_fire");
            var ts = ThreatSystem.Instance;
            if (ts != null) ts.ExternalHit("enemy_fire");
        }
    }
}
