using System.Collections.Generic;
using FieldMedic.Environment;
using UnityEngine;

namespace FieldMedic.Battle
{
    public enum Team { Enemy = 0, Friendly = 1 }
    public enum SoldierState { Inactive, Run, Kneel, Stand, Dying, Dead }

    /// <summary>
    /// Near layer (&lt; 60 m, GDD_V2 §2): pooled enemy riflemen and machine gunners rushing from cover to cover
    /// toward our position, kneeling to fire bursts (muzzle flash, tracer every 3rd round, 3D sound), getting hit
    /// by our fire or by a blast, falling (Mixamo death clips), then respawning off-screen in the haze; plus a few
    /// friendlies along our sandbag line returning fire. One Update drives every agent (no per-agent scripts).
    /// Bullet impacts near the player use a single raycast per round and the shared <see cref="BattleFx"/> pools.
    /// </summary>
    public sealed class NearInfantry : MonoBehaviour
    {
        public static NearInfantry Instance { get; private set; }

        public BattleSettings settings;
        public Transform head;
        public List<SoldierAgent> enemies = new List<SoldierAgent>();
        public List<SoldierAgent> friendlies = new List<SoldierAgent>();
        [Tooltip("Enemy firing positions (crater lips, wrecks, folds) 35–70 m in front of our position.")]
        public Vector3[] enemySpots = new Vector3[0];
        [Tooltip("Friendly firing positions behind our cover line (standing aim over 1.5 m sandbags).")]
        public Vector3[] friendlySpots = new Vector3[0];
        [Tooltip("Points on our side the enemy shoots at when not shooting at the player.")]
        public Vector3[] ourLine = new Vector3[0];
        public float spawnDistance = 95f;
        public float spawnHalfWidth = 60f;

        readonly bool[] _spotTaken = new bool[64];
        readonly Dictionary<int, bool> _hardCache = new Dictionary<int, bool>(256);
        int _rayMask;

        /// <summary>Set by <see cref="EnemyFire"/>: the enemy may aim at the player this frame.</summary>
        public System.Func<SoldierAgent, Vector3?> playerAimProvider;
        public int AliveEnemies { get; private set; }
        public int TotalShots { get; private set; }
        public int TotalDeaths { get; private set; }
        public int Respawns { get; private set; }
        public float NearIntensity { get; set; } = 1f;

        void Awake()
        {
            Instance = this;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            int cover = CoverMath.CoverMask;
            _rayMask = cover | 1; // cover + Default (ground, props)
            // agents are prefab instances under this object: discover them (serialized references to prefab-instance
            // components can be lost when the prefab is re-saved by the builder)
            enemies.RemoveAll(a => a == null);
            friendlies.RemoveAll(a => a == null);
            if (enemies.Count == 0 && friendlies.Count == 0)
                foreach (var a in GetComponentsInChildren<SoldierAgent>(true))
                    (a.team == Team.Friendly || a.name.StartsWith("Friendly") ? friendlies : enemies).Add(a);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                var a = enemies[i];
                a.team = Team.Enemy;
                a.mg = i % 4 == 3;
                // half the line starts already in position (the assault is under way when you arrive)
                if (i % 2 == 0 && enemySpots.Length > 0) PlaceAtSpot(a, ClaimSpot(a));
                else Respawn(a, Time.time + i * 0.7f);
            }
            for (int i = 0; i < friendlies.Count; i++)
            {
                var f = friendlies[i];
                f.team = Team.Friendly;
                f.mg = i == 0;
                if (friendlySpots.Length == 0) { f.Enter(SoldierState.Inactive); continue; }
                f.spotIndex = i % friendlySpots.Length;
                var p = Ground(friendlySpots[f.spotIndex]);
                f.transform.SetPositionAndRotation(p, Quaternion.LookRotation(BattlefieldLayout.EnemyDirection));
                f.Enter(SoldierState.Stand, 0f);
                f.stateUntil = Time.time + Random.Range(12f, 30f);
                f.nextBurst = Time.time + Random.Range(0.5f, 3f);
            }
        }

        static Vector3 Ground(Vector3 p) => new Vector3(p.x, BattlefieldLayout.GroundHeight(p.x, p.z), p.z);

        int ClaimSpot(SoldierAgent a)
        {
            if (enemySpots.Length == 0) return -1;
            if (a.spotIndex >= 0 && a.spotIndex < _spotTaken.Length) _spotTaken[a.spotIndex] = false;
            int start = Random.Range(0, enemySpots.Length);
            for (int k = 0; k < enemySpots.Length; k++)
            {
                int i = (start + k) % enemySpots.Length;
                if (i < _spotTaken.Length && !_spotTaken[i]) { _spotTaken[i] = true; a.spotIndex = i; return i; }
            }
            a.spotIndex = start;
            return start;
        }

        void PlaceAtSpot(SoldierAgent a, int spot)
        {
            if (spot < 0) return;
            var p = Ground(enemySpots[spot] + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-0.5f, 0.5f)));
            a.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-BattlefieldLayout.EnemyDirection));
            KneelAndFire(a);
        }

        void KneelAndFire(SoldierAgent a)
        {
            a.Enter(SoldierState.Kneel);
            a.stateUntil = Time.time + Random.Range(7f, 18f);
            a.nextBurst = Time.time + Random.Range(0.6f, 2.5f);
            a.roundsLeft = 0;
        }

        /// <summary>Puts a dead/inactive enemy back at the far edge (off-screen when possible) running in.</summary>
        void Respawn(SoldierAgent a, float notBefore)
        {
            a.Enter(SoldierState.Inactive);
            a.stateUntil = notBefore;
        }

        bool TrySpawn(SoldierAgent a)
        {
            Vector3 hp = head != null ? head.position : Vector3.zero;
            Vector3 hf = head != null ? head.forward : Vector3.forward;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var p = Ground(new Vector3(Random.Range(-spawnHalfWidth, spawnHalfWidth), 0f, spawnDistance + Random.Range(0f, 25f)));
                // off-screen: outside the view cone, or far enough to be lost in the haze
                if (BattleMath.InView(hp, hf, p, 50f) && Vector3.Distance(p, hp) < 105f) continue;
                int spot = ClaimSpot(a);
                a.goal = spot >= 0 ? Ground(enemySpots[spot] + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-0.5f, 0.5f))) : Ground(p + Vector3.back * 40f);
                a.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Flat(a.goal - p)));
                a.speed = Random.Range(3.2f, 4.3f);
                a.Enter(SoldierState.Run, 0f);
                Respawns++;
                return true;
            }
            return false;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v; }

        void Update()
        {
            float now = Time.time, dt = Time.deltaTime;
            int alive = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var a = enemies[i];
                if (a == null) continue;
                StepEnemy(a, now, dt);
                if (a.Alive) alive++;
            }
            AliveEnemies = alive;
            for (int i = 0; i < friendlies.Count; i++)
            {
                var f = friendlies[i];
                if (f == null || f.State == SoldierState.Inactive) continue;
                StepFriendly(f, now, dt);
            }
        }

        void StepEnemy(SoldierAgent a, float now, float dt)
        {
            switch (a.State)
            {
                case SoldierState.Inactive:
                    if (now >= a.stateUntil && !TrySpawn(a)) a.stateUntil = now + 1f;
                    break;
                case SoldierState.Run:
                {
                    Vector3 p = a.transform.position;
                    Vector3 to = Flat(a.goal - p);
                    float d = to.magnitude;
                    if (d < 0.4f) { a.transform.rotation = Quaternion.LookRotation(-BattlefieldLayout.EnemyDirection); KneelAndFire(a); break; }
                    Vector3 step = to / d * Mathf.Min(d, a.speed * dt);
                    p += step;
                    p.y = BattlefieldLayout.GroundHeight(p.x, p.z);
                    a.transform.SetPositionAndRotation(p, Quaternion.Slerp(a.transform.rotation, Quaternion.LookRotation(to), Mathf.Clamp01(dt * 8f)));
                    MaybeHit(a, dt, 1.6f);
                    break;
                }
                case SoldierState.Kneel:
                case SoldierState.Stand:
                    Fire(a, now);
                    MaybeHit(a, dt, 1f);
                    if (a.Alive && now >= a.stateUntil && a.roundsLeft <= 0)
                    {
                        // rush to the next cover (closer when possible)
                        int spot = ClaimSpot(a);
                        if (spot >= 0)
                        {
                            a.goal = Ground(enemySpots[spot] + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-0.5f, 0.5f)));
                            a.speed = Random.Range(3.2f, 4.3f);
                            a.Enter(SoldierState.Run);
                        }
                        else a.stateUntil = now + 5f;
                    }
                    break;
                case SoldierState.Dying:
                    if (now >= a.stateUntil) { a.Enter(SoldierState.Dead); a.stateUntil = now + (settings != null ? settings.soldierRespawnDelay : 7f) + Random.Range(0f, 6f); }
                    break;
                case SoldierState.Dead:
                    if (now >= a.stateUntil)
                    {
                        // only vanish when the player is not looking at the body
                        Vector3 hp = head != null ? head.position : Vector3.zero;
                        Vector3 hf = head != null ? head.forward : Vector3.forward;
                        if (!BattleMath.InView(hp, hf, a.transform.position, 55f)) Respawn(a, now + Random.Range(0.5f, 3f));
                        else a.stateUntil = now + 2f;
                    }
                    break;
            }
        }

        void StepFriendly(SoldierAgent f, float now, float dt)
        {
            if (f.State == SoldierState.Run)
            {
                Vector3 p = f.transform.position;
                Vector3 to = Flat(f.goal - p);
                float d = to.magnitude;
                if (d < 0.3f)
                {
                    f.transform.rotation = Quaternion.LookRotation(BattlefieldLayout.EnemyDirection);
                    f.Enter(SoldierState.Stand);
                    f.stateUntil = now + Random.Range(15f, 35f);
                    f.nextBurst = now + Random.Range(0.8f, 2f);
                    return;
                }
                p += to / d * Mathf.Min(d, f.speed * dt);
                p.y = BattlefieldLayout.GroundHeight(p.x, p.z);
                f.transform.SetPositionAndRotation(p, Quaternion.Slerp(f.transform.rotation, Quaternion.LookRotation(to), Mathf.Clamp01(dt * 8f)));
                return;
            }
            Fire(f, now);
            if (now >= f.stateUntil && f.roundsLeft <= 0 && friendlySpots.Length > friendlies.Count)
            {
                // reposition to a free friendly spot
                int start = Random.Range(0, friendlySpots.Length);
                for (int k = 0; k < friendlySpots.Length; k++)
                {
                    int s = (start + k) % friendlySpots.Length;
                    bool taken = false;
                    foreach (var o in friendlies) if (o != f && o.spotIndex == s) { taken = true; break; }
                    if (taken) continue;
                    f.spotIndex = s;
                    f.goal = Ground(friendlySpots[s]);
                    f.speed = 3.2f;
                    f.Enter(SoldierState.Run);
                    return;
                }
                f.stateUntil = now + 10f;
            }
        }

        void MaybeHit(SoldierAgent a, float dt, float exposure)
        {
            float rate = (settings != null ? settings.enemyCasualtyRate : 0.025f) * exposure;
            if (Random.value < rate * dt) Kill(a);
        }

        /// <summary>Kills an enemy (our fire / a blast): death clip, body stays, respawn later off-screen.</summary>
        public void Kill(SoldierAgent a)
        {
            if (!a.Alive) return;
            if (a.spotIndex >= 0 && a.spotIndex < _spotTaken.Length) _spotTaken[a.spotIndex] = false;
            a.roundsLeft = 0;
            a.Enter(SoldierState.Dying, 0.12f);
            a.stateUntil = Time.time + 2.6f;
            a.Deaths++;
            TotalDeaths++;
        }

        /// <summary>Blast damage: enemies within the lethal radius fall.</summary>
        public void BlastAt(Vector3 p, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < enemies.Count; i++)
            {
                var a = enemies[i];
                if (a != null && a.Alive && (a.transform.position - p).sqrMagnitude < r2) Kill(a);
            }
        }

        // ------------------------------------------------------------------ firing
        /// <summary>Turns a firing soldier toward his aim point (yaw) and pitches the rifle.</summary>
        static void FaceAim(SoldierAgent a, float dt)
        {
            Vector3 to = a.aim - a.transform.position;
            float h = new Vector2(to.x, to.z).magnitude;
            if (h < 0.5f) return;
            var want = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z));
            a.transform.rotation = Quaternion.RotateTowards(a.transform.rotation, want, 140f * dt);
            if (a.poser != null) a.poser.aimPitch = Mathf.Clamp(Mathf.Atan2(to.y - 1.2f, h) * Mathf.Rad2Deg, -25f, 25f);
        }

        void Fire(SoldierAgent a, float now)
        {
            FaceAim(a, Time.deltaTime);
            if (a.roundsLeft <= 0)
            {
                if (now < a.nextBurst) return;
                float k = Mathf.Max(0.25f, NearIntensity);
                a.roundsLeft = a.mg ? Random.Range(6, 14) : Random.Range(2, 6);
                a.nextRound = now;
                a.aim = ChooseAim(a);
                var ba = BattleAudio.Instance;
                if (ba != null) ba.Gunfire(a.Muzzle, a.mg, a.team == Team.Friendly ? 1f : 0.9f);
                a.nextBurst = now + (a.mg ? Random.Range(2.5f, 5f) : Random.Range(1.4f, 4f)) / k;
            }
            if (now < a.nextRound) return;
            a.nextRound = now + (a.mg ? 0.085f : 0.11f);
            a.roundsLeft--;
            a.Shots++;
            TotalShots++;
            Vector3 muzzle = a.Muzzle;
            var fx = BattleFx.Instance;
            if (fx != null) fx.MuzzleFlash(muzzle, a.mg ? 0.45f : 0.32f);
            Vector3 dir = a.aim - muzzle;
            float dist = dir.magnitude;
            if (dist < 0.5f) return;
            dir /= dist;
            // spread ~1.2° (MG 1.8°)
            float spread = a.mg ? 0.031f : 0.021f;
            dir = (dir + new Vector3(Random.Range(-spread, spread), Random.Range(-spread * 0.6f, spread), Random.Range(-spread, spread))).normalized;
            if (a.Shots % 3 == 0) CombatVfx.Instance?.TracerBurst(muzzle + dir * 1.5f, dir, 1);
            // impact (only worth simulating where the player can see it)
            if (head != null && (a.aim - head.position).sqrMagnitude < 45f * 45f && fx != null)
            {
                if (Physics.Raycast(muzzle + dir * 0.8f, dir, out var hit, dist + 6f, _rayMask, QueryTriggerInteraction.Ignore))
                    fx.Impact(hit.point, hit.normal, IsHard(hit.collider));
            }
        }

        Vector3 ChooseAim(SoldierAgent a)
        {
            if (a.team == Team.Friendly)
            {
                // a live enemy if any, else a point in the enemy field
                int n = enemies.Count;
                int start = Random.Range(0, Mathf.Max(1, n));
                for (int k = 0; k < n; k++)
                {
                    var e = enemies[(start + k) % n];
                    if (e != null && e.Alive) return e.Head + Random.insideUnitSphere * 1.5f;
                }
                return Ground(new Vector3(Random.Range(-40f, 40f), 0f, Random.Range(50f, 90f))) + Vector3.up;
            }
            if (playerAimProvider != null)
            {
                var p = playerAimProvider(a);
                if (p.HasValue) return p.Value;
            }
            if (ourLine.Length > 0) return ourLine[Random.Range(0, ourLine.Length)] + Random.insideUnitSphere * 0.8f;
            return new Vector3(Random.Range(-10f, 10f), 1.3f, 5f);
        }

        /// <summary>Concrete/brick/steel chips + sparks; sandbags and soil puff dirt (cached per collider).</summary>
        public bool IsHard(Collider c)
        {
            if (c == null) return false;
            int id = c.GetInstanceID();
            if (_hardCache.TryGetValue(id, out bool hard)) return hard;
            string n = c.gameObject.name.ToLowerInvariant();
            var t = c.transform;
            while (t.parent != null && t.parent.name != "CoverLine" && t.parent.name != "Ruins") t = t.parent;
            string root = t.name.ToLowerInvariant();
            hard = c.gameObject.layer == LayerMask.NameToLayer(CoverMath.CoverLayerName)
                   && !(n.Contains("sack") || n.Contains("burlap") || n.Contains("sand") || root.Contains("sack"));
            _hardCache[id] = hard;
            return hard;
        }
    }
}
