using System;
using FieldMedic.Battle;
using FieldMedic.Casualty;
using FieldMedic.Comfort;
using FieldMedic.Environment;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// TACEVAC (GDD_V2 §1, reworked w9 from owner feedback "no cart: I drag him there myself and we fly off together"):
    /// after the MEDEVAC call a transport helicopter comes in low over the steppe, flares and lands at the marked LZ
    /// (green smoke, rotor wash dust). The player drags Erlan with the normal drag grips (plate-carrier handle,
    /// straps, wrists, ankles) to the open side door; once his chest is inside the door zone two crew members reach
    /// out and slide him onto the cabin floor. Then the player boards (steps into the door zone or presses the
    /// trigger at the door): short fade, seated in the cabin next to Erlan, the door slides shut, a slow lift-off
    /// with the cabin as the reference frame (player parented to the helicopter, vignette, no artificial camera
    /// rotation), fade to black after ~6 s of flight, back on the ground → debrief. No stretcher cart, no button.
    /// </summary>
    public sealed class TacevacController : MonoBehaviour
    {
        public enum Stage { Idle, Inbound, Landed, TakingAboard, WaitingForPlayer, Boarding, Departing, Gone }

        public static TacevacController Instance { get; private set; }

        public Transform heli;
        public Transform rotor;
        public AudioSource rotorAudio;
        [Tooltip("w4 litter (legacy, kept hidden — the casualty is dragged to the door now).")]
        public Transform stretcher;
        public float approachSeconds = 24f;
        public float rotorRpm = 280f;
        public Vector3 approachFrom = new Vector3(-90f, 55f, -230f);
        [Header("Door / boarding (w9)")]
        [Tooltip("Erlan's chest closer than this (horizontal, m) to the door point → the crew takes him in.")]
        public float doorRadius = 1.35f;
        [Tooltip("Player head closer than this (horizontal, m) to the door point → boards.")]
        public float boardRadius = 1.3f;
        [Tooltip("Trigger at the door boards from this far (m).")]
        public float triggerBoardRadius = 3.2f;
        public float takeAboardSeconds = 2.4f;
        public float doorCloseSeconds = 1.6f;
        [Tooltip("Flight before the fade to black (s).")]
        public float flightSeconds = 6f;

        public Stage State { get; private set; } = Stage.Idle;
        public bool Called => State != Stage.Idle;
        public bool Landed => State >= Stage.Landed;
        /// <summary>Erlan was dragged to the door and is on the cabin floor.</summary>
        public bool CasualtyAboard { get; private set; }
        public bool PlayerAboard { get; private set; }
        /// <summary>The whole evacuation is over (flown off, faded out): the run may end.</summary>
        public bool Loaded { get; private set; }
        /// <summary>Ground point in front of the open side door (drag target, hint arrow, ghost hands).</summary>
        public Vector3 DoorPoint => heli != null && _geoValid ? heli.TransformPoint(_doorLocal) : LandingPoint + Vector3.left * 2.5f;
        public Vector3 CabinPoint => heli != null && _geoValid ? heli.TransformPoint(_cabinLocal) : LandingPoint;
        public Vector3 LandingPoint => LzMarker.Instance != null ? LzMarker.Instance.TouchdownPoint : BattlefieldLayout.LandingZone;
        /// <summary>Legacy name used by hints: the drag target is now the door.</summary>
        public Vector3 StretcherPoint => DoorPoint + Vector3.up * 0.3f;
        public event Action CasualtyAboardEvent;
        public event Action PlayerBoardedEvent;
        public event Action LoadedEvent;

        float _t, _depart, _dustAt, _stageT;
        Vector3 _p0, _p1, _p2, _p3;
        Quaternion _landRot;
        bool _geoValid, _awaySaid;
        Vector3 _doorLocal, _cabinLocal, _seatLocal, _doorPanelOpen, _doorPanelClosed;
        float _floorLocalY, _doorHalfWidth;
        Transform _crew, _armA, _armB, _doorPanel;
        CasualtyController _c;
        Vector3 _cStartPos, _cEndPos;
        Quaternion _cStartRot, _cEndRot;
        Transform _rig;
        Transform _rigParent;
        float _rigY0;
        readonly System.Collections.Generic.List<Behaviour> _paused = new System.Collections.Generic.List<Behaviour>();

        void Awake()
        {
            Instance = this;
            if (heli != null) heli.gameObject.SetActive(false);
            if (stretcher != null) stretcher.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestorePlayer();
        }

        public void ResetForRun()
        {
            State = Stage.Idle;
            CasualtyAboard = PlayerAboard = Loaded = false;
            _awaySaid = false;
            if (heli != null) heli.gameObject.SetActive(false);
            if (stretcher != null) stretcher.gameObject.SetActive(false);
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
            if (heli != null) heli.SetPositionAndRotation(GroundAt(LandingPoint), _landRot);
            MeasureCabin();
            BuildCrewAndDoor();
            Debug.Log($"[FMVR] tacevac: landed at the LZ, side door at {DoorPoint:F1}");
        }

        /// <summary>The steppe is not flat out at the LZ: put the skids on the real ground (the helicopter has no colliders).</summary>
        static Vector3 GroundAt(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            return p;
        }

        // ------------------------------------------------------------------ geometry
        /// <summary>Fuselage box in heli space → the port-side door (front third), the cabin floor and a seat.</summary>
        [Header("Cabin geometry (baked in the editor by W9Patch: mesh data is not readable on the device)")]
        public bool cabinBaked;
        public float bakedMinY, bakedZMin, bakedZMax, bakedHalfWidth;

        /// <summary>Fuselage of the helicopter from its body mesh (editor: read-only mesh data works on any mesh).</summary>
        public bool BakeCabin()
        {
            if (!MeasureFromMeshes(out bakedMinY, out bakedZMin, out bakedZMax, out bakedHalfWidth, out int n)) return false;
            cabinBaked = true;
            Debug.Log($"[FMVR] tacevac: cabin baked from {n} vertices: y0 {bakedMinY:F2}, z {bakedZMin:F2}..{bakedZMax:F2}, half width {bakedHalfWidth:F2}");
            return true;
        }

        /// <summary>Vertices below 55 % of the height (drooping blade tips sit higher), robust percentiles; nose at +z.</summary>
        bool MeasureFromMeshes(out float minY, out float zMin, out float zMax, out float half, out int count)
        {
            minY = 0f; zMin = -8f; zMax = 8f; half = 1.2f; count = 0;
            if (heli == null) return false;
            var inv = heli.worldToLocalMatrix;
            var pts = new System.Collections.Generic.List<Vector3>(8192);
            foreach (var mf in heli.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.transform == rotor || (rotor != null && mf.transform.IsChildOf(rotor))) continue;
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var m = inv * mf.transform.localToWorldMatrix;
                using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    if (data.Length == 0) continue;
                    var v = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetVertices(v);
                    int step = Mathf.Max(1, v.Length / 8000);
                    for (int i = 0; i < v.Length; i += step) pts.Add(m.MultiplyPoint3x4(v[i]));
                    v.Dispose();
                }
            }
            count = pts.Count;
            if (pts.Count < 100) return false;
            pts.Sort((a, c) => a.y.CompareTo(c.y));
            minY = pts[0].y;
            float maxY = pts[pts.Count - 1].y;
            float yCut = minY + (maxY - minY) * 0.55f;
            var low = pts.FindAll(q => q.y < yCut);
            if (low.Count < 50) return false;
            low.Sort((a, c) => a.z.CompareTo(c.z));
            zMin = low[(int)(low.Count * 0.01f)].z;
            zMax = low[(int)(low.Count * 0.99f)].z;
            float zCabin = zMax - (zMax - zMin) * 0.45f;
            float y0 = minY;
            var cab = low.FindAll(q => q.z > zCabin && q.y > y0 + 0.4f);
            if (cab.Count > 20)
            {
                cab.Sort((a, c) => Mathf.Abs(a.x).CompareTo(Mathf.Abs(c.x)));
                half = Mathf.Abs(cab[(int)(cab.Count * 0.95f)].x);
            }
            return true;
        }

        void MeasureCabin()
        {
            _geoValid = false;
            if (heli == null) return;
            float minY, zMin, zMax, half;
            string src = "baked";
            if (cabinBaked) { minY = bakedMinY; zMin = bakedZMin; zMax = bakedZMax; half = bakedHalfWidth; }
            else if (!MeasureFromMeshes(out minY, out zMin, out zMax, out half, out _)) { src = "default"; minY = 0f; zMin = -8f; zMax = 8f; half = 1.2f; }
            else src = "measured";
            half = Mathf.Clamp(half, 0.8f, 1.6f);
            float len = Mathf.Max(4f, zMax - zMin);
            float zDoor = zMax - len * 0.3f;   // the cabin door sits just behind the cockpit
            _floorLocalY = minY + 0.75f;
            _doorHalfWidth = 0.65f;
            _doorLocal = new Vector3(-half - 0.55f, minY, zDoor);
            _cabinLocal = new Vector3(-half * 0.25f, _floorLocalY, zDoor - 0.3f);
            _seatLocal = new Vector3(half - 0.3f, _floorLocalY, zDoor + 0.1f);   // on the troop bench
            _doorPanelClosed = new Vector3(-half - 0.02f, _floorLocalY + 0.65f, zDoor);
            _doorPanelOpen = _doorPanelClosed + new Vector3(-0.06f, 0f, -1.3f);
            _geoValid = true;
            Debug.Log($"[FMVR] tacevac: cabin ({src}) y0 {minY:F2}, z {zMin:F2}..{zMax:F2}, half width {half:F2}, door {_doorLocal:F2}, floor {_floorLocalY:F2}");
        }

        /// <summary>Two crew members' arms in the doorway (sleeve + glove) and a sliding door panel.</summary>
        void BuildCrewAndDoor()
        {
            if (heli == null) return;
            if (_crew != null) { _doorPanel.localPosition = _doorPanelOpen; SetArms(0f); return; }
            var olive = FieldMedic.Treatment.Kit.Mat("CrewSleeve", new Color(0.26f, 0.27f, 0.18f), 0.15f);
            var glove = FieldMedic.Treatment.Kit.Mat("CrewGlove", new Color(0.1f, 0.09f, 0.08f), 0.25f);
            var hull = FieldMedic.Treatment.Kit.Mat("HeliDoor", new Color(0.36f, 0.4f, 0.3f), 0.3f, 0.2f);
            _crew = new GameObject("EvacCrew").transform;
            _crew.SetParent(heli, false);
            _crew.localPosition = new Vector3(_doorLocal.x + 0.75f, _floorLocalY, _doorLocal.z);
            _armA = Arm("CrewArm_A", new Vector3(0f, 0.55f, 0.28f), olive, glove);
            _armB = Arm("CrewArm_B", new Vector3(0f, 0.6f, -0.28f), olive, glove);
            _doorPanel = FieldMedic.Treatment.Kit.Part(heli, PrimitiveType.Cube, "SideDoor", _doorPanelOpen,
                new Vector3(0.05f, 1.25f, 1.25f), hull).transform;
            _doorPanel.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // the imported hull is single-sided: from inside you would look through its floor at the ground.
            // A cabin floor (ribbed aluminium, olive), the troop bench the medic sits on and a back wall.
            var floorM = FieldMedic.Treatment.Kit.Mat("CabinFloor", new Color(0.2f, 0.21f, 0.17f), 0.25f, 0.3f);
            var canvas = FieldMedic.Treatment.Kit.Mat("CabinBench", new Color(0.3f, 0.3f, 0.2f), 0.1f);
            float half = -_doorLocal.x - 0.55f, z = _doorLocal.z;
            var cabin = new GameObject("EvacCabin").transform;
            cabin.SetParent(heli, false);
            FieldMedic.Treatment.Kit.Part(cabin, PrimitiveType.Cube, "Floor", new Vector3(0f, _floorLocalY - 0.03f, z - 0.2f), new Vector3(half * 2f - 0.06f, 0.05f, 2.8f), floorM);
            FieldMedic.Treatment.Kit.Part(cabin, PrimitiveType.Cube, "Bench", new Vector3(half - 0.3f, _floorLocalY + 0.42f, z + 0.1f), new Vector3(0.42f, 0.06f, 1.9f), canvas);
            FieldMedic.Treatment.Kit.Part(cabin, PrimitiveType.Cube, "BenchBack", new Vector3(half - 0.08f, _floorLocalY + 0.85f, z + 0.1f), new Vector3(0.04f, 0.75f, 1.9f), canvas);
            FieldMedic.Treatment.Kit.Part(cabin, PrimitiveType.Cube, "BackWall", new Vector3(0f, _floorLocalY + 0.7f, z - 1.6f), new Vector3(half * 2f - 0.06f, 1.4f, 0.04f), floorM);
            FieldMedic.Treatment.Kit.Merge(cabin);
            SetArms(0f);
        }

        Transform Arm(string name, Vector3 shoulder, Material sleeve, Material glove)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_crew, false);
            root.localPosition = shoulder;
            FieldMedic.Treatment.Kit.Part(root, PrimitiveType.Capsule, "Sleeve", new Vector3(-0.25f, 0f, 0f), new Vector3(0.1f, 0.27f, 0.1f), sleeve, Quaternion.Euler(0f, 0f, 90f));
            FieldMedic.Treatment.Kit.Part(root, PrimitiveType.Sphere, "Glove", new Vector3(-0.53f, 0f, 0f), new Vector3(0.1f, 0.06f, 0.12f), glove);
            return root;
        }

        /// <summary>0 = arms resting inside, 1 = reaching out of the door and down to the casualty.</summary>
        void SetArms(float reach)
        {
            if (_armA == null) return;
            float pitch = Mathf.Lerp(-25f, 38f, reach);
            _armA.localRotation = Quaternion.Euler(0f, 0f, pitch);
            _armB.localRotation = Quaternion.Euler(0f, 0f, pitch + 6f);
            _armA.localPosition = new Vector3(Mathf.Lerp(0.15f, -0.25f, reach), 0.55f, 0.28f);
            _armB.localPosition = new Vector3(Mathf.Lerp(0.15f, -0.25f, reach), 0.6f, -0.28f);
        }

        // ------------------------------------------------------------------ casualty → door
        CasualtyController Casualty()
        {
            if (_c == null) _c = ScenarioDirector.Instance != null && ScenarioDirector.Instance.casualty != null
                ? ScenarioDirector.Instance.casualty : FindAnyObjectByType<CasualtyController>();
            return _c;
        }

        /// <summary>Horizontal distance of Erlan's chest from the door point (∞ if unknown).</summary>
        public float CasualtyDoorDistance()
        {
            var c = Casualty();
            if (c == null || c.rig == null || c.rig.ChestBone == null) return float.PositiveInfinity;
            Vector3 d = c.rig.ChestBone.position - DoorPoint; d.y = 0f;
            return d.magnitude;
        }

        void TakeAboard()
        {
            var c = Casualty();
            if (c == null) return;
            State = Stage.TakingAboard;
            _stageT = 0f;
            var ci = c.Interaction;
            if (ci != null)
            {
                if (ci.Drag != null) { foreach (var h in ci.Drag.strapHandles) if (h != null) h.ForceRelease(); ci.Drag.enabled = false; }
                foreach (var h in ci.LimbHandles) if (h != null) h.ForceRelease();
            }
            // he slides in head first along the cabin floor, lying across toward the far side
            var root = c.transform;
            var hips = c.rig.Bone(HumanBodyBones.Hips);
            var headB = c.rig.Bone(HumanBodyBones.Head);
            Vector3 hp = hips != null ? hips.position : root.position;
            Vector3 along = (headB != null ? headB.position : hp + root.forward) - hp; along.y = 0f;
            Vector3 want = heli.forward; want.y = 0f; // along the cabin floor, head toward the cockpit
            float yaw = Vector3.SignedAngle(along.sqrMagnitude > 1e-4f ? along : root.forward, want, Vector3.up);
            _cStartPos = root.position;
            _cStartRot = root.rotation;
            _cEndRot = Quaternion.AngleAxis(yaw, Vector3.up) * root.rotation;
            Vector3 hipOffset = _cEndRot * (Quaternion.Inverse(root.rotation) * (hp - root.position));
            Vector3 cabin = CabinPoint;
            _cEndPos = new Vector3(cabin.x, cabin.y + (root.position.y - hp.y) + 0.12f, cabin.z) - new Vector3(hipOffset.x, 0f, hipOffset.z);
            Debug.Log("[FMVR] tacevac: casualty at the door — crew takes him in");
        }

        void FinishTakeAboard()
        {
            var c = Casualty();
            CasualtyAboard = true;
            State = Stage.WaitingForPlayer;
            _stageT = 0f;
            if (c != null)
            {
                c.transform.SetPositionAndRotation(_cEndPos, _cEndRot);
                c.transform.SetParent(heli, true);
                if (c.Protocol != null) c.Protocol.Perform(MedAction.EvacLoad);
            }
            if (FieldMedic.Scenarios.RadioSet.Instance != null) FieldMedic.Scenarios.RadioSet.Instance.Say("evac_board");
            Debug.Log("[FMVR] tacevac: casualty aboard (dragged to the door)");
            CasualtyAboardEvent?.Invoke();
        }

        // ------------------------------------------------------------------ player boards
        Transform Head()
        {
            var d = ScenarioDirector.Instance;
            if (d != null && d.head != null) return d.head;
            return Camera.main != null ? Camera.main.transform : null;
        }

        Transform Rig()
        {
            var d = ScenarioDirector.Instance;
            if (d != null && d.playerRig != null) return d.playerRig;
            var cam = Camera.main;
            return cam != null ? cam.transform.root : null;
        }

        /// <summary>Head near the door, or a trigger squeeze within a few metres of it.</summary>
        public bool WantsToBoard()
        {
            var head = Head();
            if (head == null) return false;
            Vector3 d = head.position - DoorPoint; d.y = 0f;
            if (d.magnitude < boardRadius) return true;
            if (d.magnitude > triggerBoardRadius) return false;
            foreach (var h in HandGrabber.All)
                if (h != null && h.Input != null && h.Input.Trigger > 0.8f && h.Held == null) return true;
            return false;
        }

        /// <summary>Board now (also tests / desktop).</summary>
        public void Board()
        {
            if (State != Stage.WaitingForPlayer) return;
            State = Stage.Boarding;
            _stageT = 0f;
            if (ComfortFade.Instance != null) ComfortFade.Instance.Fade = 1f;
            Debug.Log("[FMVR] tacevac: player boarding");
        }

        void SeatPlayer()
        {
            _rig = Rig();
            PlayerAboard = true;
            if (_rig == null || heli == null) return;
            // freeze ground-bound comfort systems: the cabin is the reference frame now
            _paused.Clear();
            foreach (var b in _rig.GetComponentsInChildren<Locomotion>(true)) if (b.enabled) { b.enabled = false; _paused.Add(b); }
            // the eye that belongs to this rig (tests may drive a detached head transform)
            var head = Head();
            if (head == null || !head.IsChildOf(_rig)) head = Camera.main != null && Camera.main.transform.IsChildOf(_rig) ? Camera.main.transform : null;
            Vector3 seat = heli.TransformPoint(_seatLocal);
            Vector3 headOff = head != null ? head.position - _rig.position : Vector3.up * 1.2f;
            Vector3 face = CabinPoint - seat; face.y = 0f;
            float yaw = face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face.normalized).eulerAngles.y : heli.eulerAngles.y;
            float headYawInRig = head != null ? Mathf.DeltaAngle(_rig.eulerAngles.y, head.eulerAngles.y) : 0f;
            var rot = Quaternion.Euler(0f, yaw - headYawInRig, 0f);
            Vector3 headOffRot = rot * Quaternion.Inverse(_rig.rotation) * headOff;
            _rigParent = _rig.parent;
            _rigY0 = _rig.position.y;
            // seated on the bench: eyes ~1.15 m above the cabin floor whatever the real head height
            _rig.SetPositionAndRotation(new Vector3(seat.x - headOffRot.x, seat.y + 1.15f - headOffRot.y, seat.z - headOffRot.z), rot);
            _rig.SetParent(heli, true);
            var sim = _rig.GetComponent<DesktopSimulator>();
            if (sim != null) sim.SnapYaw(rot.eulerAngles.y);
            Debug.Log("[FMVR] tacevac: player seated in the cabin");
            PlayerBoardedEvent?.Invoke();
        }

        void RestorePlayer()
        {
            if (_rig == null) return;
            if (_rig.parent == heli) _rig.SetParent(_rigParent, true);
            foreach (var b in _paused) if (b != null) b.enabled = true;
            _paused.Clear();
        }

        void Finish()
        {
            State = Stage.Gone;
            var c = Casualty();
            if (c != null) c.gameObject.SetActive(false); // handed over
            // back on the ground at the menu spot (or the LZ) for the debrief board
            RestorePlayer();
            var d = ScenarioDirector.Instance;
            if (_rig != null)
            {
                _rig.position = new Vector3(_rig.position.x, _rigY0, _rig.position.z);
                if (d != null && d.menuPlayerSpot != null) d.PlacePlayerAt(d.menuPlayerSpot.position, d.menuPlayerSpot.eulerAngles.y);
                else { var p = LandingPoint + Vector3.right * 6f; _rig.position = new Vector3(p.x, _rigY0, p.z); }
                FieldMedic.Startup.RigGrounding.Ground(_rig);
            }
            if (heli != null) heli.gameObject.SetActive(false);
            if (rotorAudio != null) rotorAudio.Stop();
            Loaded = true;
            Debug.Log("[FMVR] tacevac: flown off, evacuation complete");
            LoadedEvent?.Invoke();
            if (ComfortFade.Instance != null) { ComfortFade.Instance.Vignette = 0f; ComfortFade.Instance.Fade = 0f; }
        }

        // ------------------------------------------------------------------ per frame
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
                    RotorWash(0.9f);
                    SetArms(Mathf.Clamp01(1.6f - CasualtyDoorDistance() / 3f) * 0.5f); // crew leans out when he is close
                    if (CasualtyDoorDistance() < doorRadius) TakeAboard();
                    break;
                case Stage.TakingAboard:
                {
                    _stageT += dt;
                    float k = Mathf.Clamp01(_stageT / takeAboardSeconds);
                    SetArms(k < 0.25f ? Mathf.Lerp(0.5f, 1f, k / 0.25f) : Mathf.Lerp(1f, 0f, (k - 0.25f) / 0.75f));
                    var c = Casualty();
                    if (c != null)
                    {
                        float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.2f) / 0.8f));
                        c.transform.SetPositionAndRotation(Vector3.Lerp(_cStartPos, _cEndPos, s) + Vector3.up * (Mathf.Sin(s * Mathf.PI) * 0.25f),
                            Quaternion.Slerp(_cStartRot, _cEndRot, s));
                    }
                    if (k >= 1f) FinishTakeAboard();
                    break;
                }
                case Stage.WaitingForPlayer:
                    RotorWash(0.9f);
                    _stageT += dt;
                    if (WantsToBoard()) Board();
                    break;
                case Stage.Boarding:
                    _stageT += dt;
                    if (!PlayerAboard && (ComfortFade.Instance == null || ComfortFade.Instance.ShownFade > 0.99f || _stageT > 1.2f))
                    {
                        SeatPlayer();
                        _stageT = 0f;
                        if (ComfortFade.Instance != null) ComfortFade.Instance.Fade = 0f;
                    }
                    else if (PlayerAboard)
                    {
                        float k = Mathf.Clamp01(_stageT / doorCloseSeconds);
                        if (_doorPanel != null) _doorPanel.localPosition = Vector3.Lerp(_doorPanelOpen, _doorPanelClosed, Mathf.SmoothStep(0f, 1f, k));
                        if (k >= 1f && _stageT > doorCloseSeconds + 0.6f) { State = Stage.Departing; _depart = 0f; }
                    }
                    break;
                case Stage.Departing:
                {
                    _depart += dt;
                    // slow vertical lift, then a gentle forward transition (no rotation forced on the head)
                    float climb = Mathf.Min(2.2f, 0.25f + _depart * 0.55f);
                    float fwd = _depart > 2.5f ? Mathf.Min(14f, (_depart - 2.5f) * (_depart - 2.5f) * 1.2f) : 0f;
                    if (heli != null)
                    {
                        heli.position += (Vector3.up * climb + _landRot * Vector3.forward * fwd) * dt;
                        heli.rotation = Quaternion.Slerp(heli.rotation, _landRot * Quaternion.Euler(fwd > 0.5f ? 4f : 0f, 0f, 0f), dt * 0.6f);
                    }
                    if (_depart < 3f) RotorWash(Mathf.Lerp(1.2f, 0.3f, _depart / 3f));
                    if (ComfortFade.Instance != null) ComfortFade.Instance.Vignette = Mathf.Clamp01(_depart / 1.5f) * 0.75f;
                    if (!_awaySaid && _depart > 2f)
                    {
                        _awaySaid = true;
                        if (FieldMedic.Scenarios.RadioSet.Instance != null) FieldMedic.Scenarios.RadioSet.Instance.Say("evac_away");
                    }
                    if (_depart > flightSeconds && ComfortFade.Instance != null) ComfortFade.Instance.Fade = 1f;
                    bool black = ComfortFade.Instance == null || ComfortFade.Instance.ShownFade > 0.99f;
                    if (_depart > flightSeconds && (black || _depart > flightSeconds + 1.5f)) Finish();
                    break;
                }
            }
        }

        void RotorWash(float amount)
        {
            if (Time.time <= _dustAt || BattleFx.Instance == null) return;
            _dustAt = Time.time + 0.9f;
            BattleFx.Instance.GroundDust(LandingPoint, 0.5f * amount);
        }

        Vector3 Bezier(float t)
        {
            float u = 1f - t;
            return u * u * u * _p0 + 3f * u * u * t * _p1 + 3f * u * t * t * _p2 + t * t * t * _p3;
        }
    }
}
