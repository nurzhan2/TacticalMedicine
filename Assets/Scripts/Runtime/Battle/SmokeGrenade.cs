using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>
    /// Smoke grenade (RDG-2 / M18 class) — all by hand: grab it from the vest pouch, hook the ring with the other
    /// hand and pull the pin (≥ 6 cm), throw; the lever flies off on release and after a 1.2 s fuse a dense white
    /// cloud pours out for 20 s, drifting with the wind. The cloud registers <see cref="SmokeScreens"/> spheres
    /// that block enemy lines of fire (GDD_V2: cover the drag). Released with the pin in near the pouch → it goes
    /// back; elsewhere it just drops (a dud you can pick up again).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SmokeGrenade : Grabbable
    {
        public enum GState { OnVest, Held, Dropped, Armed, Smoking, Spent }

        public Transform slot;
        public SmokePin pin;
        public ParticleSystem smoke;
        public AudioSource hiss;
        public float fuse = 1.2f;
        public float burnTime = 20f;
        public float returnDistance = 0.16f;
        public Vector3 wind = new Vector3(0.6f, 0f, -0.2f);

        public GState State { get; private set; } = GState.OnVest;
        public bool PinPulled => pin == null || pin.Pulled;
        public float SmokeAge => State == GState.Smoking || State == GState.Spent ? Time.time - _smokeStart : 0f;
        public float CloudRadius { get; private set; }
        public Vector3 CloudCentre { get; private set; }

        const int Clouds = 3;
        readonly int[] _cloudIds = { -1, -1, -1 };
        float _armedAt, _smokeStart;
        Vector3 _origin;

        protected override void Awake()
        {
            base.Awake();
            priority = 3;
            allowTwoHands = false;
            if (Body != null) { Body.isKinematic = true; Body.mass = 0.45f; }
            if (smoke != null) { var em = smoke.emission; em.enabled = false; }
            if (pin != null) pin.grenade = this;
        }

        public override bool CanBeGrabbedBy(HandGrabber hand)
        {
            if (State == GState.Smoking) return false; // hot, pouring smoke
            return base.CanBeGrabbedBy(hand);
        }

        protected override void OnGrabbed(HandGrabber hand, GrabPoint point)
        {
            if (State == GState.OnVest || State == GState.Dropped) State = GState.Held;
            if (Body != null) Body.isKinematic = false;
        }

        protected override void OnReleased(HandGrabber hand)
        {
            if (State != GState.Held) return;
            if (PinPulled) { Arm(); return; }
            if (slot != null && Vector3.Distance(transform.position, slot.position) < returnDistance) { ReturnToVest(); return; }
            State = GState.Dropped;
        }

        public void ReturnToVest()
        {
            State = GState.OnVest;
            if (Body != null) { Body.isKinematic = true; Body.linearVelocity = Vector3.zero; }
        }

        void Arm()
        {
            State = GState.Armed;
            _armedAt = Time.time;
            // the spoon flies off: a metallic ping
            var ba = BattleAudio.Instance;
            if (ba != null) ba.Play(BattleAudio.Pick(ba.pinPull), transform.position, 0.5f, 0.5f, 15f, 1.35f);
        }

        /// <summary>Test/bot helper: same as the pin pulled + throw from the current pose with a velocity.</summary>
        public void ThrowNow(Vector3 velocity)
        {
            if (pin != null && !pin.Pulled) pin.Pull();
            ForceRelease();
            if (Body != null) { Body.isKinematic = false; Body.linearVelocity = velocity; }
            State = GState.Held;
            OnReleased(null);
        }

        void StartSmoke()
        {
            State = GState.Smoking;
            _smokeStart = Time.time;
            _origin = transform.position;
            if (smoke != null)
            {
                smoke.transform.SetParent(null, true); // world-space emitter stays upright on the ground
                var em = smoke.emission; em.enabled = true;
                smoke.Play();
            }
            if (hiss != null) { var ba = BattleAudio.Instance; if (ba != null && hiss.clip == null) hiss.clip = BattleAudio.Pick(ba.smokeHiss); if (hiss.clip != null) hiss.Play(); }
            for (int i = 0; i < Clouds; i++) _cloudIds[i] = SmokeScreens.Add(_origin, 0.1f, 0f);
        }

        void LateUpdate()
        {
            if (State == GState.OnVest && slot != null)
            {
                transform.SetPositionAndRotation(slot.position, slot.rotation);
                if (Body != null && !Body.isKinematic) Body.isKinematic = true;
            }
        }

        void Update()
        {
            float now = Time.time;
            if (State == GState.Armed && now - _armedAt >= fuse) StartSmoke();
            if (State != GState.Smoking && State != GState.Spent) return;

            float age = now - _smokeStart;
            if (smoke != null)
            {
                smoke.transform.position = transform.position + Vector3.up * 0.1f;
                var em = smoke.emission;
                em.enabled = age < burnTime;
            }
            if (hiss != null && age >= burnTime && hiss.isPlaying) hiss.volume = Mathf.MoveTowards(hiss.volume, 0f, Time.deltaTime);
            if (age >= burnTime && State == GState.Smoking) State = GState.Spent;

            // three overlapping spheres drifting downwind: young (at the canister) → old (furthest)
            float fadeOut = Mathf.Clamp01(1f - (age - burnTime) / 9f);
            Vector3 src = transform.position;
            CloudCentre = src;
            CloudRadius = 0f;
            for (int i = 0; i < Clouds; i++)
            {
                if (_cloudIds[i] < 0) continue;
                float lagAge = Mathf.Max(0f, age - i * 3.5f);
                float grow = Mathf.Clamp01(lagAge / 5f);
                float radius = Mathf.Lerp(0.6f, 4.2f + i * 0.6f, grow);
                float density = Mathf.Clamp01(lagAge / 2.5f) * fadeOut * (1.25f - i * 0.15f);
                Vector3 c = src + wind * Mathf.Min(lagAge, 12f) * (0.25f + i * 0.35f) + Vector3.up * Mathf.Min(radius * 0.55f, 2.2f);
                SmokeScreens.Set(_cloudIds[i], c, radius, density);
                if (i == 0) { CloudCentre = c; CloudRadius = radius; }
                if (fadeOut <= 0f) { SmokeScreens.Remove(_cloudIds[i]); _cloudIds[i] = -1; }
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            for (int i = 0; i < Clouds; i++) if (_cloudIds[i] >= 0) { SmokeScreens.Remove(_cloudIds[i]); _cloudIds[i] = -1; }
        }

        void OnDestroy()
        {
            if (smoke != null && smoke.transform.parent == null) Destroy(smoke.gameObject);
        }
    }
}
