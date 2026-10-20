using System;
using System.Collections.Generic;
using FieldMedic.Input;
using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>Which button started the current grab.</summary>
    public enum GrabButton { None, Grip, Trigger }

    /// <summary>
    /// Proximity grab for one hand: sphere overlap at the palm, the nearest candidate wins (priority only breaks near
    /// ties), hover highlight + glow cue + haptic tick, grab on GRIP or TRIGGER press, release when the buttons are up.
    /// Also exposes palm/fingertip points used by <see cref="Pokeable"/>.
    /// w10 (owner: "I grab the way the hint shows and it does not take"): before w10 only the grip grabbed, inside 7 cm,
    /// with no visual hover feedback, while the ghost glove curled the index finger like a trigger squeeze. Now both
    /// buttons grab the same way everywhere; while an item is held the trigger is its fine action
    /// (<see cref="ActionTrigger"/>, re-armed after a trigger-started grab so the grab squeeze never cuts or writes).
    /// </summary>
    [RequireComponent(typeof(HandRig))]
    public sealed class HandGrabber : MonoBehaviour
    {
        public const float HoverRadius = 0.09f;
        /// <summary>Distance advantage per priority level (m): nearer wins unless the other is a "better" handle close by.</summary>
        public const float PriorityMetres = 0.02f;

        [Tooltip("Palm centre relative to the hand transform.")]
        public Vector3 palmOffset = new Vector3(0f, -0.01f, 0.04f);
        [Tooltip("Index fingertip relative to the hand transform (poking, pulse checks).")]
        public Vector3 fingertipOffset = new Vector3(0f, -0.015f, 0.12f);
        public float grabRadius = HoverRadius;
        [Range(0f, 1f)] public float gripOn = 0.6f;
        [Range(0f, 1f)] public float gripOff = 0.35f;
        public LayerMask grabMask = ~0;
        [Tooltip("w9b: the wounded hand of the self-aid drill cannot hold anything (bloody glove, no strength).")]
        public bool gripDisabled;
        [Tooltip("w10: the trigger grabs too (owner feedback). Off = grip only (legacy).")]
        public bool triggerGrabs = true;
        [Tooltip("Glowing ring at the hovered handle (w10).")]
        public bool hoverCue = true;
        [SerializeField, HideInInspector] int _w10;

        public static readonly List<HandGrabber> All = new List<HandGrabber>();
        /// <summary>Grab / miss log lines written so far this session (device diagnostics).</summary>
        public static int LoggedEvents;

        public HandRig Rig { get; private set; }
        public IHandInput Input => Rig != null ? Rig.Input : null;
        public Handedness Handedness => Rig != null ? Rig.handedness : Handedness.Right;
        public Grabbable Hovered { get; private set; }
        public GrabPoint HoveredPoint { get; private set; }
        public Grabbable Held { get; private set; }
        public GrabPoint HeldPoint { get; private set; }
        /// <summary>A grab button (grip, or trigger when it grabs) is held.</summary>
        public bool IsGripping { get; private set; }
        public bool GripDown { get; private set; }
        public bool TriggerDown { get; private set; }
        public GrabButton HeldWith { get; private set; }
        /// <summary>Last grab press with nothing in reach (UI hints, tests).</summary>
        public float LastMissAt { get; private set; } = -1f;

        /// <summary>Trigger value for the held item's fine action (cut, write, peel). 0 until the trigger that started the
        /// grab has been let go once (hold on with the grip, then squeeze again).</summary>
        public float ActionTrigger => Input == null ? 0f : Held == null || _triggerArmed ? Input.Trigger : 0f;

        public Vector3 PalmPosition => transform.TransformPoint(palmOffset);
        public Vector3 FingertipPosition => transform.TransformPoint(fingertipOffset);

        public event Action<HandGrabber, Grabbable> GrabStarted;
        public event Action<HandGrabber, Grabbable> GrabEnded;

        readonly Collider[] _buffer = new Collider[128]; // a body next to the hand alone has ~30 trigger parts
        readonly HashSet<Grabbable> _seen = new HashSet<Grabbable>();
        bool _triggerArmed = true;
        Collider _hoverCollider;
        Transform _cue;
        Material _cueMat;
        float _missLogAt;

        void Awake()
        {
            Rig = GetComponent<HandRig>();
            // scenes serialized before w10 carry the old 7 cm radius
            if (_w10 < 1) { grabRadius = Mathf.Max(grabRadius, HoverRadius); _w10 = 1; }
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }

        void OnDisable()
        {
            All.Remove(this);
            Release();
            SetHover(null, null, null);
            if (_cue != null) _cue.gameObject.SetActive(false);
        }

        void OnDestroy() { if (_cue != null) Destroy(_cue.gameObject); }

        void Update()
        {
            var input = Input;
            if (input == null) return;

            if (gripDisabled)
            {
                IsGripping = GripDown = TriggerDown = false;
                if (Held != null) Release();
                SetHover(null, null, null);
                UpdateCue();
                return;
            }
            bool wasGrip = GripDown, wasTrig = TriggerDown;
            GripDown = GripDown ? input.Grip > gripOff : input.Grip >= gripOn;
            TriggerDown = TriggerDown ? input.Trigger > gripOff : input.Trigger >= gripOn;
            bool gripPressed = GripDown && !wasGrip;
            bool trigPressed = triggerGrabs && TriggerDown && !wasTrig;
            IsGripping = GripDown || (triggerGrabs && TriggerDown);

            if (Held != null)
            {
                // w10 audit: a tool taken with КУРОК is moved to GRIP as soon as grip closes, so the trigger becomes its action
                // (marker/shears/seal) instead of dropping the item when the trigger is released
                if (HeldWith == GrabButton.Trigger && GripDown) HeldWith = GrabButton.Grip;
                if (!_triggerArmed && input.Trigger < 0.3f) _triggerArmed = true;
                bool holding = GripDown || (triggerGrabs && TriggerDown);
                if (!holding || !input.IsTracked) Release();
                UpdateCue();
                return;
            }

            FindBest(out var best, out var bestPoint, out var bestCol);
            SetHover(best, bestPoint, bestCol);
            if ((gripPressed || trigPressed) && input.IsTracked)
            {
                var with = gripPressed ? GrabButton.Grip : GrabButton.Trigger;
                if (best != null) TryGrab(best, bestPoint, with);
                else LogMiss(with);
            }
            UpdateCue();
        }

        /// <summary>Best grab candidate near the palm (null if none).</summary>
        public void FindBest(out Grabbable best, out GrabPoint bestPoint) => FindBest(out best, out bestPoint, out _);

        public void FindBest(out Grabbable best, out GrabPoint bestPoint, out Collider bestCollider)
        {
            best = null; bestPoint = null; bestCollider = null;
            Vector3 palm = PalmPosition;
            int n = Physics.OverlapSphereNonAlloc(palm, grabRadius, _buffer, grabMask, QueryTriggerInteraction.Collide);
            float bestScore = float.NegativeInfinity;
            _seen.Clear();
            for (int i = 0; i < n; i++)
            {
                var col = _buffer[i];
                if (col == null || !col.enabled) continue;
                var g = col.GetComponentInParent<Grabbable>();
                if (g == null || !g.CanBeGrabbedBy(this)) continue;

                // inside several handles at once: the one whose centre is nearest wins
                float colDist = Vector3.Distance(SafeClosestPoint(col, palm), palm) + 0.1f * Vector3.Distance(col.bounds.center, palm);
                bool first = _seen.Add(g);

                GrabPoint point = null;
                float pointDist = float.PositiveInfinity;
                if (first && g.GrabPoints != null)
                {
                    foreach (var p in g.GrabPoints)
                    {
                        if (p == null || !p.isActiveAndEnabled || !p.Allows(Handedness)) continue;
                        float d = p.Distance(palm);
                        if (d <= p.radius && d < pointDist) { pointDist = d; point = p; }
                    }
                }
                if (point == null && g.requireGrabPoint) continue;

                float dist = point != null ? pointDist : colDist;
                float score = (g.priority + (point != null ? point.priorityBonus + 1 : 0)) * PriorityMetres - dist;
                if (score > bestScore) { bestScore = score; best = g; bestPoint = point; bestCollider = col; }
            }
        }

        static Vector3 SafeClosestPoint(Collider c, Vector3 p)
        {
            if (c is MeshCollider mc && !mc.convex) return c.bounds.ClosestPoint(p);
            return c.ClosestPoint(p);
        }

        public bool TryGrab(Grabbable g, GrabPoint point = null) => TryGrab(g, point, GrabButton.Grip);

        public bool TryGrab(Grabbable g, GrabPoint point, GrabButton with)
        {
            if (g == null || gripDisabled) return false;
            if (Held != null) Release();
            if (!g.BeginGrab(this, point)) return false;
            SetHover(null, null, null);
            Held = g;
            HeldPoint = point;
            HeldWith = with;
            // a trigger-started grab: the squeeze that took the item is not its action (no cut / write until re-pressed)
            _triggerArmed = with != GrabButton.Trigger && (Input == null || Input.Trigger < 0.3f);
            Haptics.Play(Input, Haptics.Grab);
            GrabStarted?.Invoke(this, g);
            if (LoggedEvents < 400)
            {
                LoggedEvents++;
                Debug.Log($"[FMVR] grab {Side} ({with}): {g.name} at {Vector3.Distance(PalmPosition, g.transform.position) * 100f:0} cm");
            }
            return true;
        }

        string Side => Handedness == Handedness.Left ? "L" : "R";

        void LogMiss(GrabButton with)
        {
            LastMissAt = Time.time;
            if (LoggedEvents >= 400 || Time.unscaledTime < _missLogAt) return;
            _missLogAt = Time.unscaledTime + 1f;
            LoggedEvents++;
            // what was the player reaching for? nearest grabbable within 40 cm (and why it did not take)
            Vector3 palm = PalmPosition;
            int n = Physics.OverlapSphereNonAlloc(palm, 0.4f, _buffer, grabMask, QueryTriggerInteraction.Collide);
            Grabbable near = null; float nd = 9f;
            for (int i = 0; i < n; i++)
            {
                var g = _buffer[i] != null ? _buffer[i].GetComponentInParent<Grabbable>() : null;
                if (g == null) continue;
                float d = Vector3.Distance(SafeClosestPoint(_buffer[i], palm), palm);
                if (d < nd) { nd = d; near = g; }
            }
            string why = near == null ? "" : near.CanBeGrabbedBy(this) ? " (out of reach)" : " (not grabbable now)";
            Debug.Log($"[FMVR] grab miss {Side} ({with}): nothing within {grabRadius * 100f:0} cm" +
                      (near != null ? $"; nearest {near.name} at {nd * 100f:0} cm{why}" : ""));
        }

        public void Release()
        {
            if (Held == null) return;
            var g = Held;
            Held = null;
            HeldPoint = null;
            HeldWith = GrabButton.None;
            _triggerArmed = true;
            g.EndGrab(this);
            Haptics.Play(Input, Haptics.Release);
            GrabEnded?.Invoke(this, g);
        }

        void SetHover(Grabbable g, GrabPoint p, Collider c)
        {
            HoveredPoint = p;
            _hoverCollider = c;
            if (g == Hovered) return;
            if (Hovered != null) Hovered.HoverExit(this);
            Hovered = g;
            if (Hovered != null)
            {
                Hovered.HoverEnter(this);
                Haptics.Play(Input, Haptics.Hover);
            }
        }

        // ------------------------------------------------------------------ hover cue
        /// <summary>World point of the hover cue (null when nothing is hovered) — tests.</summary>
        public Vector3? CuePoint { get; private set; }

        void UpdateCue()
        {
            CuePoint = null;
            if (Held == null && Hovered != null)
            {
                Vector3 palm = PalmPosition;
                Vector3 at = HoveredPoint != null ? HoveredPoint.transform.position
                    : _hoverCollider != null ? SafeClosestPoint(_hoverCollider, palm) : Hovered.transform.position;
                CuePoint = at;
            }
            if (!hoverCue || !Application.isPlaying) return;
            if (CuePoint == null) { if (_cue != null && _cue.gameObject.activeSelf) _cue.gameObject.SetActive(false); return; }
            if (_cue == null) BuildCue();
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : CuePoint.Value + Vector3.back;
            Vector3 toEye = (eye - CuePoint.Value).normalized;
            _cue.gameObject.SetActive(true);
            _cue.position = CuePoint.Value + toEye * 0.015f;
            _cue.rotation = Quaternion.LookRotation(-toEye, Vector3.up);
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 9f);
            _cue.localScale = Vector3.one * (0.05f * pulse);
            _cueMat.SetFloat("_Alpha", 0.55f + 0.35f * pulse);
        }

        void BuildCue()
        {
            _cueMat = FieldMedic.UI.UiMesh.Mat(new Color(1f, 0.92f, 0.55f, 0.9f), 3070, FieldMedic.UI.UiMesh.Icon("glow"));
            _cue = FieldMedic.UI.UiMesh.Add(null, "HoverCue_" + Side, FieldMedic.UI.UiMesh.Quad, _cueMat, Vector3.zero).transform;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(PalmPosition, grabRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(FingertipPosition, 0.01f);
        }
    }
}
