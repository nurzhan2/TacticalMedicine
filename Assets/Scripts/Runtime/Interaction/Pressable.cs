using UnityEngine;
using UnityEngine.Events;

namespace FieldMedic.Interaction
{
    /// <summary>
    /// A physical push button on top of <see cref="Pokeable"/>: click past pressThreshold, re-arm below
    /// releaseThreshold, optional cap visual that sinks with the finger.
    /// </summary>
    public class Pressable : Pokeable
    {
        [Range(0f, 1f)] public float pressThreshold = 0.75f;
        [Range(0f, 1f)] public float releaseThreshold = 0.35f;
        [Tooltip("Moves down along -up with the press depth.")]
        public Transform cap;

        public UnityEvent onPressed = new UnityEvent();
        public UnityEvent onReleased = new UnityEvent();

        public bool IsPressed { get; private set; }
        public int PressCount { get; private set; }

        Vector3 _capRest;

        protected virtual void Awake()
        {
            if (cap != null) _capRest = cap.localPosition;
        }

        protected override void OnDepth(float previous, float current)
        {
            float d = Depth01;
            if (!IsPressed && d >= pressThreshold)
            {
                IsPressed = true;
                PressCount++;
                Haptics.Play(Toucher != null ? Toucher.Input : null, Haptics.PressClick);
                onPressed.Invoke();
            }
            else if (IsPressed && d <= releaseThreshold)
            {
                IsPressed = false;
                onReleased.Invoke();
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!IsTouching && IsPressed)
            {
                IsPressed = false;
                onReleased.Invoke();
            }
            if (cap != null)
            {
                Vector3 down = cap.parent != null ? cap.parent.InverseTransformDirection(-transform.up) : -transform.up;
                Vector3 target = _capRest + down * Depth;
                cap.localPosition = Vector3.Lerp(cap.localPosition, target, IsTouching ? 1f : 12f * Time.deltaTime);
            }
        }
    }
}
