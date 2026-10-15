using System;
using FieldMedic.Battle;
using FieldMedic.Casualty;
using FieldMedic.Environment;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// TACEVAC (GDD_V2 §1): after the MEDEVAC call and the (time-compressed) wait, a transport helicopter comes in low
    /// over the steppe, flares and lands at the marked LZ behind the ruins (green smoke pops, rotor wash kicks up dust).
    /// A litter appears next to Erlan with a round «ПОГРУЗИТЬ / ТИЕУ» button; pressing it hands him over (he is carried to
    /// the helicopter), the helicopter lifts off and the run ends → debrief. Scripted path, no physics.
    /// </summary>
    public sealed class TacevacController : MonoBehaviour
    {
        public enum Stage { Idle, Inbound, Landed, Departing, Gone }

        public static TacevacController Instance { get; private set; }

        public Transform heli;
        public Transform rotor;
        public AudioSource rotorAudio;
        public Transform stretcher;
        public float approachSeconds = 24f;
        public float rotorRpm = 280f;
        public Vector3 approachFrom = new Vector3(-90f, 55f, -230f);

        public Stage State { get; private set; } = Stage.Idle;
        public bool Called => State != Stage.Idle;
        public bool Landed => State >= Stage.Landed;
        public bool Loaded { get; private set; }
        public RoundButton LoadButton { get; private set; }
        public Vector3 StretcherPoint => stretcher != null && stretcher.gameObject.activeInHierarchy ? stretcher.position + Vector3.up * 0.3f : LandingPoint;
        public Vector3 ButtonPoint => LoadButton != null ? LoadButton.transform.position : StretcherPoint;
        public Vector3 LandingPoint => LzMarker.Instance != null ? LzMarker.Instance.TouchdownPoint : BattlefieldLayout.LandingZone;
        public event Action LoadedEvent;

        float _t, _depart, _dustAt;
        Vector3 _p0, _p1, _p2, _p3;
        Quaternion _landRot;

        void Awake()
        {
            Instance = this;
            if (heli != null) heli.gameObject.SetActive(false);
            if (stretcher != null) stretcher.gameObject.SetActive(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void ResetForRun()
        {
            State = Stage.Idle;
            Loaded = false;
            if (heli != null) heli.gameObject.SetActive(false);
            if (stretcher != null) stretcher.gameObject.SetActive(false);
            if (LoadButton != null) Destroy(LoadButton.gameObject);
        }

        /// <summary>Start the approach (instant = land now; tests).</summary>
        public void Call(bool instant = false)
        {
            if (State != Stage.Idle) return;
            State = Stage.Inbound;
            _t = 0f;
            Vector3 lz = LandingPoint;
            _p0 = lz + approachFrom;
            _p3 = lz;
            _p1 = Vector3.Lerp(_p0, lz, 0.55f) + Vector3.up * -18f;
            _p2 = lz + Vector3.up * 14f + (lz - _p0).normalized * -16f;
            Vector3 fwd = lz - _p0; fwd.y = 0f;
            _landRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            if (LzMarker.Instance != null) LzMarker.Instance.PopSmoke();
            if (heli != null)
            {
                heli.gameObject.SetActive(true);
                heli.SetPositionAndRotation(_p0, _landRot);
                if (rotorAudio != null && rotorAudio.clip != null && !rotorAudio.isPlaying) rotorAudio.Play();
            }
            Debug.Log("[FMVR] tacevac: inbound" + (instant ? " (instant)" : ""));
            if (instant) Land();
        }

        void Land()
        {
            State = Stage.Landed;
            if (heli != null) heli.SetPositionAndRotation(LandingPoint, _landRot);
            PlaceStretcher();
            Debug.Log("[FMVR] tacevac: landed at the LZ");
        }

        /// <summary>The litter goes down next to Erlan (wherever he lies) with the load button above it.</summary>
        void PlaceStretcher()
        {
            var c = ScenarioDirector.Instance != null ? ScenarioDirector.Instance.casualty : FindAnyObjectByType<CasualtyController>();
            Vector3 at = c != null ? c.transform.position : LandingPoint + Vector3.right * 3f;
            Vector3 side = c != null && c.rig != null ? c.rig.BodyRight : Vector3.right;
            side.y = 0f;
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            Vector3 hips = c != null && c.rig != null && c.rig.Bone(HumanBodyBones.Hips) != null ? c.rig.Bone(HumanBodyBones.Hips).position : at;
            Vector3 head = c != null && c.rig != null && c.rig.Bone(HumanBodyBones.Head) != null ? c.rig.Bone(HumanBodyBones.Head).position : at + Vector3.forward;
            Vector3 along = head - hips; along.y = 0f;
            if (along.sqrMagnitude < 1e-4f) along = Vector3.forward;
            // the litter on the side with more room
            Vector3 p = new Vector3(hips.x, 0f, hips.z) - side * 0.75f;
            if (Physics.Raycast(hips + Vector3.up * 0.4f, -side, 0.9f, ~0, QueryTriggerInteraction.Ignore)) p = new Vector3(hips.x, 0f, hips.z) + side * 0.75f;
            p.y = (c != null ? c.transform.position.y : 0f) + 0.02f;
            if (stretcher != null)
            {
                stretcher.gameObject.SetActive(true);
                stretcher.SetPositionAndRotation(p, Quaternion.LookRotation(along.normalized, Vector3.up));
            }
            if (LoadButton == null)
            {
                LoadButton = RoundButton.Create(null, "LoadButton", Vector3.zero, 0.14f, new Color(0.2f, 0.45f, 0.75f),
                    GameSettings.T("ПОГРУЗИТЬ", "ТИЕУ"), 0.022f, GameSettings.T("в вертолёт", "тікұшаққа"), 0.016f);
                LoadButton.Clicked += Load;
            }
            var cam = Camera.main;
            Vector3 bp = p + Vector3.up * 0.85f;
            LoadButton.transform.position = bp;
            Vector3 face = cam != null ? bp - cam.transform.position : -side;
            face.y = 0f;
            LoadButton.transform.rotation = Quaternion.LookRotation(face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward);
        }

        /// <summary>Hand Erlan over: he goes onto the litter and into the helicopter, which lifts off.</summary>
        public void Load()
        {
            if (State != Stage.Landed || Loaded) return;
            Loaded = true;
            UiAudio.Click(ButtonPoint);
            var c = ScenarioDirector.Instance != null ? ScenarioDirector.Instance.casualty : FindAnyObjectByType<CasualtyController>();
            if (c != null && stretcher != null) c.transform.SetPositionAndRotation(stretcher.position + Vector3.up * 0.25f, stretcher.rotation);
            if (LoadButton != null) LoadButton.gameObject.SetActive(false);
            _depart = 0f;
            State = Stage.Departing;
            Debug.Log("[FMVR] tacevac: casualty loaded");
            LoadedEvent?.Invoke();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (rotor != null && heli != null && heli.gameObject.activeSelf) rotor.Rotate(0f, rotorRpm * 6f * dt, 0f, Space.Self);
            switch (State)
            {
                case Stage.Inbound:
                {
                    _t += dt / approachSeconds;
                    float k = Mathf.Clamp01(_t);
                    // ease the last third (flare + descent)
                    float e = k < 0.7f ? k / 0.7f * 0.8f : 0.8f + 0.2f * Mathf.SmoothStep(0f, 1f, (k - 0.7f) / 0.3f);
                    Vector3 pos = Bezier(e);
                    Vector3 vel = Bezier(Mathf.Min(1f, e + 0.01f)) - pos;
                    if (heli != null)
                    {
                        heli.position = pos;
                        float pitch = k < 0.75f ? 10f : Mathf.Lerp(-12f, 0f, (k - 0.75f) / 0.25f); // nose down cruise, flare
                        Vector3 f = vel; f.y = 0f;
                        var rot = f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized) : _landRot;
                        heli.rotation = Quaternion.Slerp(rot, _landRot, Mathf.SmoothStep(0f, 1f, (k - 0.8f) / 0.2f)) * Quaternion.Euler(pitch, 0f, 0f);
                    }
                    float alt = pos.y - LandingPoint.y;
                    if (alt < 12f && Time.time > _dustAt && BattleFx.Instance != null)
                    {
                        _dustAt = Time.time + 0.35f;
                        BattleFx.Instance.GroundDust(LandingPoint, Mathf.Lerp(1.4f, 0.4f, alt / 12f));
                    }
                    if (k >= 1f) Land();
                    break;
                }
                case Stage.Landed:
                    if (Time.time > _dustAt && BattleFx.Instance != null) { _dustAt = Time.time + 0.9f; BattleFx.Instance.GroundDust(LandingPoint, 0.5f); }
                    if (LoadButton != null && Camera.main != null)
                    {
                        Vector3 face = LoadButton.transform.position - Camera.main.transform.position; face.y = 0f;
                        if (face.sqrMagnitude > 1e-4f) LoadButton.transform.rotation = Quaternion.Slerp(LoadButton.transform.rotation, Quaternion.LookRotation(face.normalized), dt * 4f);
                    }
                    break;
                case Stage.Departing:
                    _depart += dt;
                    if (_depart > 2.5f && heli != null)
                    {
                        float u = _depart - 2.5f;
                        heli.position += (Vector3.up * Mathf.Min(6f, 1f + u * 2f) + heli.forward * Mathf.Min(30f, u * u * 3f)) * dt;
                        heli.rotation = Quaternion.Slerp(heli.rotation, _landRot * Quaternion.Euler(12f, 0f, 0f), dt);
                        if (stretcher != null) stretcher.gameObject.SetActive(false);
                        var c = ScenarioDirector.Instance != null ? ScenarioDirector.Instance.casualty : null;
                        if (c != null && c.gameObject.activeSelf && u > 0.2f) c.gameObject.SetActive(false); // aboard
                    }
                    if (_depart > 30f) { State = Stage.Gone; if (heli != null) heli.gameObject.SetActive(false); }
                    break;
            }
        }

        Vector3 Bezier(float t)
        {
            float u = 1f - t;
            return u * u * u * _p0 + 3f * u * u * t * _p1 + 3f * u * t * t * _p2 + t * t * t * _p3;
        }
    }
}
