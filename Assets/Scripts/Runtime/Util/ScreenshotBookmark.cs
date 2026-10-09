using UnityEngine;

namespace FieldMedic.Util
{
    /// <summary>
    /// Named camera pose used by the editor ScreenshotTool (the GameObject name is the PNG name).
    /// Pure marker: no runtime cost, stripped of behaviour in builds.
    /// </summary>
    public sealed class ScreenshotBookmark : MonoBehaviour
    {
        public float fieldOfView = 60f;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawFrustum(Vector3.zero, fieldOfView, 0.3f, 0.02f, 16f / 9f);
        }
    }
}
