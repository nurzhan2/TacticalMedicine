using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>
    /// Horizon layer: hundreds of advancing soldiers as camera-facing impostors in the haze — one static mesh of
    /// quads animated entirely in the vertex shader (FieldMedic/Impostor: billboard, flipbook from the editor-baked
    /// run sheet, forward drift with wrap-around), plus muzzle twinkles. Zero CPU per frame.
    /// </summary>
    public sealed class HorizonLayer : MonoBehaviour
    {
        public MeshRenderer figures;
        public MeshRenderer twinkles;
    }
}
