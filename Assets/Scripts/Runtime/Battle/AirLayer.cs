using FieldMedic.Environment;
using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>
    /// Sky layer: a ground-attack jet (Su-25 stand-in) making low bomb runs over the enemy lines with heat-decoy
    /// flares on the pull-out, and an attack helicopter (Mi-24 stand-in) that comes up behind our position, pitches
    /// nose-down and ripples rocket salvos into the enemy field before turning away. Both fly simple analytic
    /// paths (no physics), carry a 3D engine/rotor loop with Doppler, and are hidden between runs.
    /// </summary>
    public sealed class AirLayer : MonoBehaviour
    {
        public static AirLayer Instance { get; private set; }

        public Transform jet;
        public AudioSource jetAudio;
        public Transform heli;
        public Transform heliRotor;
        public AudioSource heliAudio;
        public Transform head;

        public bool JetBusy => _jetActive;
        public bool HeliBusy => _heliActive;
        public int JetRuns { get; private set; }
        public int HeliRuns { get; private set; }
        public int BombsDropped { get; private set; }
        public int RocketsFired { get; private set; }

        // jet run
        bool _jetActive;
        Vector3 _jetStart, _jetDir, _jetTarget;
        float _jetT, _jetSpeed = 170f, _jetAlt = 85f, _jetLen;
        int _bombsLeft;
        float _releaseAt, _flaresAt;
        int _flaresLeft;
        float _bank;

        // heli run
        bool _heliActive;
        float _heliT;
        Vector3 _heliFrom, _heliHold, _heliExit, _heliTarget;
        int _salvo, _rocketsLeft;
        float _nextRocket;

        void Awake()
        {
            Instance = this;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (jet != null) jet.gameObject.SetActive(false);
            if (heli != null) heli.gameObject.SetActive(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ jet
        /// <summary>Bomb run on <paramref name="target"/> (enemy field). Comes in from behind our lines.</summary>
        public bool JetRun(Vector3 target)
        {
            if (jet == null || _jetActive) return false;
            _jetActive = true;
            JetRuns++;
            _jetTarget = target;
            // heading roughly along +Z (from our rear toward the enemy) with a random crossing angle
            float yaw = Random.Range(-35f, 35f);
            _jetDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            _jetAlt = Random.Range(70f, 95f);
            float fall = Mathf.Sqrt(2f * _jetAlt / 9.81f);
            float releaseDist = _jetSpeed * fall;             // horizontal throw of a bomb dropped at speed
            Vector3 release = target - _jetDir * releaseDist;
            _jetStart = release - _jetDir * 900f;
            _jetStart.y = _jetAlt;
            _jetLen = 2600f;
            _jetT = 0f;
            _releaseAt = 900f / _jetSpeed;
            _bombsLeft = Random.Range(2, 5);
            _flaresAt = _releaseAt + 1.2f;
            _flaresLeft = 8;
            jet.gameObject.SetActive(true);
            PlaceJet(0f);
            if (jetAudio != null)
            {
                jetAudio.clip = BattleAudio.Instance != null ? BattleAudio.Pick(BattleAudio.Instance.jetFlyby) : jetAudio.clip;
                if (jetAudio.clip != null)
                {
                    // line the flyby's loudest point (≈ middle of the clip) up with the pass over our position
                    jetAudio.time = 0f;
                    jetAudio.Play();
                }
            }
            return true;
        }

        void PlaceJet(float t)
        {
            float s = t * _jetSpeed;
            Vector3 p = _jetStart + _jetDir * s;
            float pull = Mathf.Clamp01((t - _releaseAt - 0.3f) / 2.5f);
            p.y = _jetAlt + pull * pull * 120f - Mathf.Clamp01(t / _releaseAt) * 10f; // shallow dive, then climb out
            _bank = Mathf.Lerp(_bank, pull > 0f ? 55f : 0f, Time.deltaTime * 1.5f);
            Vector3 fwd = _jetDir + Vector3.up * (pull > 0f ? 0.35f * pull : -0.02f);
            jet.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, -_bank));
        }

        void StepJet(float dt)
        {
            if (!_jetActive) return;
            _jetT += dt;
            PlaceJet(_jetT);
            var fx = BattleFx.Instance;
            if (_bombsLeft > 0 && _jetT >= _releaseAt)
            {
                _releaseAt += 0.18f; // stick of bombs
                _bombsLeft--;
                BombsDropped++;
                Vector3 offset = _jetDir * ((3 - _bombsLeft) * 14f) + Vector3.Cross(Vector3.up, _jetDir) * Random.Range(-5f, 5f);
                Vector3 tgt = _jetTarget + offset;
                tgt.y = BattlefieldLayout.GroundHeight(tgt.x, tgt.z);
                Vector3 from = jet.position + Vector3.down * 1f;
                float fall = Mathf.Sqrt(2f * Mathf.Max(5f, from.y - tgt.y) / 9.81f);
                fx?.Launch(ProjectileKind.Bomb, from, tgt, Vector3.Distance(from, tgt) / fall, 2.2f);
            }
            if (_flaresLeft > 0 && _jetT >= _flaresAt)
            {
                _flaresAt += 0.12f;
                _flaresLeft--;
                Vector3 side = Vector3.Cross(Vector3.up, _jetDir) * (_flaresLeft % 2 == 0 ? 1f : -1f);
                fx?.Decoy(jet.position - _jetDir * 6f, _jetDir * 60f + side * Random.Range(15f, 30f) + Vector3.down * 8f);
            }
            if (_jetT * _jetSpeed > _jetLen)
            {
                _jetActive = false;
                jet.gameObject.SetActive(false);
                if (jetAudio != null) jetAudio.Stop();
            }
        }

        // ------------------------------------------------------------------ helicopter
        /// <summary>Rocket run: the helicopter climbs in behind us, hovers nose-down, fires two salvos at
        /// <paramref name="target"/> and leaves.</summary>
        public bool HeliRun(Vector3 target)
        {
            if (heli == null || _heliActive) return false;
            _heliActive = true;
            HeliRuns++;
            _heliTarget = target;
            float side = Random.value < 0.5f ? -1f : 1f;
            _heliFrom = new Vector3(side * 220f, 45f, -260f);
            _heliHold = new Vector3(side * Random.Range(25f, 45f), Random.Range(30f, 40f), Random.Range(-40f, -20f));
            _heliExit = new Vector3(-side * 260f, 60f, -200f);
            _heliT = 0f;
            _salvo = 0;
            _rocketsLeft = 0;
            heli.gameObject.SetActive(true);
            heli.position = _heliFrom;
            if (heliAudio != null)
            {
                if (BattleAudio.Instance != null) heliAudio.clip = BattleAudio.Pick(BattleAudio.Instance.heliLoop);
                if (heliAudio.clip != null) { heliAudio.time = Random.Range(0f, heliAudio.clip.length * 0.5f); heliAudio.Play(); }
            }
            return true;
        }

        void StepHeli(float dt)
        {
            if (!_heliActive) return;
            _heliT += dt;
            const float inT = 14f, holdT = 9f, outT = 14f;
            Vector3 p, look;
            float pitch = 0f;
            if (_heliT < inT)
            {
                float k = Mathf.SmoothStep(0f, 1f, _heliT / inT);
                p = Vector3.Lerp(_heliFrom, _heliHold, k);
                look = _heliHold - _heliFrom;
                pitch = 12f * (1f - k);
            }
            else if (_heliT < inT + holdT)
            {
                p = _heliHold + new Vector3(Mathf.Sin(_heliT * 0.7f) * 0.8f, Mathf.Sin(_heliT * 1.1f) * 0.4f, 0f);
                look = _heliTarget - p;
                pitch = 8f;
                float tl = _heliT - inT;
                if (_salvo < 2 && tl > 2.5f + _salvo * 4f && _rocketsLeft == 0) { _salvo++; _rocketsLeft = Random.Range(4, 7); _nextRocket = Time.time; }
                if (_rocketsLeft > 0 && Time.time >= _nextRocket)
                {
                    _rocketsLeft--;
                    _nextRocket = Time.time + 0.16f;
                    RocketsFired++;
                    Vector3 side = heli.right * (_rocketsLeft % 2 == 0 ? 1.6f : -1.6f);
                    Vector3 from = heli.position + side + heli.forward * 1.5f + Vector3.down * 0.6f;
                    Vector3 tgt = _heliTarget + new Vector3(Random.Range(-14f, 14f), 0f, Random.Range(-10f, 10f));
                    tgt.y = BattlefieldLayout.GroundHeight(tgt.x, tgt.z);
                    BattleFx.Instance?.Launch(ProjectileKind.Rocket, from, tgt, 260f, 0.9f);
                    BattleFx.Instance?.MuzzleFlash(from, 1.2f);
                    var ba = BattleAudio.Instance;
                    if (ba != null && (_rocketsLeft % 2 == 0)) ba.Play(BattleAudio.Pick(ba.rocketLaunch), from, 0.9f, 15f, 500f, Random.Range(0.95f, 1.1f));
                }
            }
            else if (_heliT < inT + holdT + outT)
            {
                float k = Mathf.SmoothStep(0f, 1f, (_heliT - inT - holdT) / outT);
                p = Vector3.Lerp(_heliHold, _heliExit, k);
                look = _heliExit - _heliHold;
                pitch = 14f * Mathf.Clamp01(k * 3f);
            }
            else
            {
                _heliActive = false;
                heli.gameObject.SetActive(false);
                if (heliAudio != null) heliAudio.Stop();
                return;
            }
            look.y = 0f;
            if (look.sqrMagnitude < 1e-3f) look = heli.forward;
            heli.SetPositionAndRotation(p, Quaternion.Slerp(heli.rotation, Quaternion.LookRotation(look) * Quaternion.Euler(pitch, 0f, 0f), Mathf.Clamp01(dt * 1.5f)));
            if (heliRotor != null) heliRotor.Rotate(0f, 1150f * dt, 0f, Space.Self);
            // rotor wash when low over the ground
            if (p.y < 45f && Time.frameCount % 6 == 0) BattleFx.Instance?.GroundDust(new Vector3(p.x, 0f, p.z) + Random.insideUnitSphere * 6f, 0.3f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            StepJet(dt);
            StepHeli(dt);
        }
    }
}
