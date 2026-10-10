using System.Collections.Generic;
using FieldMedic.Environment;
using FieldMedic.Input;
using UnityEngine;

namespace FieldMedic.Comfort
{
    /// <summary>
    /// Shows the real room only when needed: passthrough opacity ramps 0→1 as the head or a hand
    /// gets from 0.6 m to 0.25 m of the play-area boundary (OVRBoundary play area, tracking space). Fully immersive
    /// otherwise; the system guardian stays as the final fallback. In the editor (no headset) a virtual 2.5×2.5 m
    /// play area around the rig origin is used and distances are logged once per second.
    /// The Meta SDK passthrough layer is an underlay, so the fade is done by <see cref="fadeQuad"/> in front of the
    /// eyes (FieldMedic/PassthroughFade: scales scene colour and alpha by 1-opacity → the compositor shows the room).
    /// </summary>
    public sealed class ProximityPassthrough : MonoBehaviour
    {
        public Transform trackingSpace;
        public Transform head;
        public Transform leftHand, rightHand;
        public OVRPassthroughLayer layer;
        public Renderer fadeQuad;
        public float far = 0.6f;
        public float near = 0.25f;
        public Vector2 editorPlayArea = new Vector2(2.5f, 2.5f);
        public bool logInEditor = true;

        readonly List<Vector3> _boundary = new List<Vector3>(); // tracking-space points
        float _nextFetch, _nextLog;
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        public float Opacity { get; private set; }
        public float MinDistance { get; private set; } = float.PositiveInfinity;

        void Start()
        {
            if (layer != null)
            {
                layer.textureOpacity = 1f;
                layer.hidden = true;
            }
        }

        public void SetBoundary(IList<Vector3> pts)
        {
            _boundary.Clear();
            if (pts != null) _boundary.AddRange(pts);
        }

        void FetchBoundary()
        {
            _nextFetch = Time.unscaledTime + 5f;
            if (DesktopSimulator.XRActive)
            {
                try
                {
                    if (OVRManager.boundary != null && OVRManager.boundary.GetConfigured())
                    {
                        var g = OVRManager.boundary.GetGeometry(OVRBoundary.BoundaryType.PlayArea);
                        if (g != null && g.Length >= 3) { SetBoundary(g); return; }
                    }
                }
                catch (System.Exception e) { Debug.LogWarning("[FMVR] boundary unavailable: " + e.Message); }
            }
            if (_boundary.Count == 0)
            {
                float x = editorPlayArea.x * 0.5f, z = editorPlayArea.y * 0.5f;
                SetBoundary(new[] { new Vector3(-x, 0, -z), new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(x, 0, -z) });
            }
        }

        /// <summary>Distance of a world point to the boundary (tracking-space XZ). Outside = 0.</summary>
        public float DistanceToBoundary(Vector3 world)
        {
            if (_boundary.Count < 3) return float.PositiveInfinity;
            Vector3 p = trackingSpace != null ? trackingSpace.InverseTransformPoint(world) : world;
            if (!EnvMath.InsidePolygonXZ(p, _boundary)) return 0f;
            return EnvMath.DistanceToPolygonXZ(p, _boundary);
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextFetch) FetchBoundary();
            float d = float.PositiveInfinity;
            if (head != null) d = Mathf.Min(d, DistanceToBoundary(head.position));
            if (leftHand != null) d = Mathf.Min(d, DistanceToBoundary(leftHand.position));
            if (rightHand != null) d = Mathf.Min(d, DistanceToBoundary(rightHand.position));
            MinDistance = d;
            Opacity = EnvMath.ProximityOpacity(d, far, near);
            bool show = Opacity > 0.001f;
            if (layer != null) layer.hidden = !show;
            if (fadeQuad != null)
            {
                fadeQuad.enabled = show && DesktopSimulator.XRActive;
                if (show) fadeQuad.sharedMaterial.SetFloat(OpacityId, Opacity);
            }
            if (!DesktopSimulator.XRActive && logInEditor && Application.isEditor && Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + 1f;
                if (Opacity > 0f) Debug.Log($"[FMVR] proximity passthrough: min distance {d:F2} m → opacity {Opacity:F2}");
            }
        }
    }
}
