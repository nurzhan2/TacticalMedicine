using System.Collections.Generic;
using FieldMedic.Environment;
using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>
    /// An enemy tank / IFV crawling along a closed spline 55–140 m out: hull follows the ground slope, the turret
    /// (split from the model by the battle asset builder) slews toward its target, the gun fires with a muzzle
    /// blast, dust ring, recoil and a shell that detonates on our side. Tracks kick up dust; the engine is a 3D
    /// loop. Driven by <see cref="VehicleLayer"/>; no per-frame allocations.
    /// </summary>
    public sealed class VehicleAgent : MonoBehaviour
    {
        public Transform turret;
        [Tooltip("Barrel tip in turret space.")]
        public Vector3 muzzleLocal = new Vector3(0f, 0.3f, 5f);
        public Vector3[] path = new Vector3[0];
        public float speed = 4f;
        public bool autocannon;           // IFV: bursts of 30 mm instead of a single 125 mm shot
        public float hullLength = 7f;
        public AudioSource engine;
        public ParticleSystem dust;
        public float turretSlew = 22f;    // deg/s

        public enum Mode { Drive, Aim, Fire, Resume }
        public Mode State { get; private set; } = Mode.Drive;
        public int ShotsFired { get; private set; }
        public float PathU => _u;
        public Vector3 Target => _target;

        float _u, _len, _stateUntil, _recoil, _speedNow;
        Vector3 _target;
        int _burst;
        float _nextRound;
        Quaternion _turretRest;
        Vector3 _turretRestPos;
        float _turretYaw; // relative to hull

        void Awake()
        {
            if (turret == null) turret = transform.Find("Model/Part");
            if (turret != null) { _turretRest = turret.localRotation; _turretRestPos = turret.localPosition; }
            _len = Mathf.Max(1f, Spline.LoopLength(path));
            _u = Random.Range(0f, path.Length);
            _speedNow = speed;
        }

        public bool Busy => State != Mode.Drive;

        /// <summary>Stop, aim at <paramref name="target"/>, fire. False if busy.</summary>
        public bool RequestShot(Vector3 target)
        {
            if (Busy || path.Length < 2) return false;
            _target = target;
            State = Mode.Aim;
            _stateUntil = Time.time + 6f; // give up aiming after 6 s
            return true;
        }

        public Vector3 MuzzleWorld => turret != null ? turret.TransformPoint(muzzleLocal) : transform.position + transform.forward * 4f + Vector3.up * 2f;

        public void Step(float now, float dt)
        {
            if (path.Length < 2) return;
            float want = State == Mode.Drive || State == Mode.Resume ? speed : 0f;
            _speedNow = Mathf.MoveTowards(_speedNow, want, dt * 1.6f);
            // constant ground speed along the loop: du = ds * n / L
            _u = Mathf.Repeat(_u + _speedNow * dt * path.Length / _len, path.Length);
            Vector3 p = Spline.Loop(path, _u);
            Vector3 ahead = Spline.Loop(path, _u + 0.05f);
            Vector3 fwd = ahead - p; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = transform.forward;
            fwd.Normalize();
            float half = hullLength * 0.5f;
            Vector3 front = p + fwd * half, back = p - fwd * half;
            float yf = BattlefieldLayout.GroundHeight(front.x, front.z), yb = BattlefieldLayout.GroundHeight(back.x, back.z);
            p.y = (yf + yb) * 0.5f;
            Vector3 slope = new Vector3(fwd.x * hullLength, yf - yb, fwd.z * hullLength);
            transform.SetPositionAndRotation(p, Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(slope, Vector3.up), Mathf.Clamp01(dt * 3f)));

            // turret
            if (turret != null)
            {
                float wantYaw = 0f;
                if (State == Mode.Aim || State == Mode.Fire)
                {
                    Vector3 local = transform.InverseTransformPoint(_target);
                    wantYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                }
                _turretYaw = Mathf.MoveTowardsAngle(_turretYaw, wantYaw, turretSlew * dt);
                _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 0.6f);
                turret.localRotation = Quaternion.Euler(0f, _turretYaw, 0f) * _turretRest;
                // recoil: jerk back along the barrel, spring back over ~0.4 s
                turret.localPosition = _turretRestPos - (Quaternion.Euler(0f, _turretYaw, 0f) * Vector3.forward) * _recoil;
            }

            switch (State)
            {
                case Mode.Aim:
                    if (_speedNow < 0.2f)
                    {
                        Vector3 local = transform.InverseTransformPoint(_target);
                        float aimYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                        if (Mathf.Abs(Mathf.DeltaAngle(_turretYaw, aimYaw)) < 1.5f || now > _stateUntil)
                        {
                            State = Mode.Fire;
                            _burst = autocannon ? Random.Range(4, 8) : 1;
                            _nextRound = now;
                        }
                    }
                    break;
                case Mode.Fire:
                    if (now >= _nextRound && _burst > 0)
                    {
                        FireRound();
                        _burst--;
                        _nextRound = now + (autocannon ? 0.3f : 0f);
                        if (_burst == 0) { State = Mode.Resume; _stateUntil = now + (autocannon ? 1.5f : 3.5f); }
                    }
                    break;
                case Mode.Resume:
                    if (now >= _stateUntil) State = Mode.Drive;
                    break;
            }

            if (dust != null)
            {
                var em = dust.emission;
                em.rateOverTime = _speedNow > 0.5f ? 6f + _speedNow * 2f : 0f;
            }
            if (engine != null) engine.pitch = 0.8f + 0.06f * _speedNow;
        }

        void FireRound()
        {
            ShotsFired++;
            Vector3 m = MuzzleWorld;
            var fx = BattleFx.Instance;
            var ba = BattleAudio.Instance;
            if (autocannon)
            {
                fx?.MuzzleFlash(m, 0.9f);
                if (ba != null) ba.Gunfire(m, true, 1f);
                Vector3 tgt = _target + Random.insideUnitSphere * 2.5f;
                fx?.Launch(ProjectileKind.TankShell, m, new Vector3(tgt.x, Mathf.Max(tgt.y, BattlefieldLayout.GroundHeight(tgt.x, tgt.z)), tgt.z), 450f, 0.35f);
                return;
            }
            fx?.MuzzleFlash(m, 3f);
            CombatVfx.Instance?.Flash(m, 1.1f, 0.12f, 30f);
            if (ba != null) ba.Play(BattleAudio.Pick(ba.tankCannon), m, 1f, 25f, 900f, Random.Range(0.92f, 1.04f), ba.listener != null ? EnvMath.SoundDelay(Vector3.Distance(m, ba.listener.position)) : 0f);
            fx?.Launch(ProjectileKind.TankShell, m, _target, 650f, 1.3f);
            // muzzle blast kicks dust around the hull
            if (fx != null) fx.GroundDust(transform.position + transform.forward * 3f, 1.2f);
            _recoil = 0.25f; // recoil jerk (springs back in Step)
        }
    }
}
