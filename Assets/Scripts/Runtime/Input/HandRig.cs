using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Input
{
    /// <summary>
    /// One per hand. Picks the input implementation (OVR when an XR device runs, desktop simulator in the
    /// editor, scripted in tests), ticks it once per frame and moves this transform to the hand pose.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class HandRig : MonoBehaviour
    {
        public enum Mode { Auto, OVR, Desktop, Scripted }

        public Handedness handedness = Handedness.Right;
        public Mode mode = Mode.Auto;
        [Tooltip("OVRCameraRig Left/RightHandAnchor.")]
        public Transform ovrAnchor;
        [Tooltip("Optional OVRHand under the anchor for hand tracking.")]
        public OVRHand ovrHand;
        public DesktopSimulator desktop;
        [Tooltip("Move this transform to the input pose every frame.")]
        public bool driveTransform = true;

        public static readonly List<HandRig> All = new List<HandRig>();

        public IHandInput Input { get; private set; }
        public Mode ActiveMode { get; private set; }

        void Awake() { if (Input == null) Resolve(); }
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Resolve()
        {
            var m = mode;
            if (m == Mode.Auto)
            {
                if (DesktopSimulator.XRActive && ovrAnchor != null) m = Mode.OVR;
                else if (desktop != null || (desktop = FindAnyObjectByType<DesktopSimulator>()) != null) m = Mode.Desktop;
                else m = Mode.Scripted;
            }
            switch (m)
            {
                case Mode.OVR:
                    SetInput(new OVRHandInput(handedness, ovrAnchor, ovrHand), m);
                    break;
                case Mode.Desktop:
                    if (desktop == null) desktop = FindAnyObjectByType<DesktopSimulator>();
                    SetInput(desktop != null ? (IHandInput)desktop.Get(handedness)
                                             : new ScriptedHandInput(handedness, transform.position), m);
                    break;
                default:
                    SetInput(new ScriptedHandInput(handedness, transform.position), Mode.Scripted);
                    break;
            }
        }

        /// <summary>Switch to code-driven input (tests/bots). Returns the scripted input to drive.</summary>
        public ScriptedHandInput UseScripted(Vector3? startPosition = null)
        {
            var s = new ScriptedHandInput(handedness, startPosition ?? transform.position) { Rotation = transform.rotation };
            SetInput(s, Mode.Scripted);
            return s;
        }

        public void SetInput(IHandInput input, Mode activeMode)
        {
            Input = input;
            ActiveMode = activeMode;
        }

        void Update()
        {
            if (Input == null) return;
            Input.Tick(Time.deltaTime);
            if (driveTransform) transform.SetPositionAndRotation(Input.Position, Input.Rotation);
        }
    }
}
