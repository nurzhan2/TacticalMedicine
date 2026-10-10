using UnityEngine;

namespace FieldMedic.Casualty.Blood
{
    /// <summary>
    /// Feeds up to 4 wound soak spheres (world pos + radius) to the casualty shader (FieldMedic/CasualtyLit),
    /// which darkens and wets the uniform with an organic noisy edge. Blood runs downhill: the sphere centre
    /// sags toward world-down as it grows. Also pushes skin pallor/cyanosis/sweat. MaterialPropertyBlock only.
    /// </summary>
    public sealed class BloodSoak : MonoBehaviour
    {
        public Renderer[] targets;
        public static readonly int[] SphereIds =
        {
            Shader.PropertyToID("_SoakSphere0"), Shader.PropertyToID("_SoakSphere1"),
            Shader.PropertyToID("_SoakSphere2"), Shader.PropertyToID("_SoakSphere3"),
        };
        static readonly int PallorId = Shader.PropertyToID("_Pallor");
        static readonly int CyanosisId = Shader.PropertyToID("_Cyanosis");
        static readonly int SweatId = Shader.PropertyToID("_Sweat");
        MaterialPropertyBlock _mpb;
        public readonly Vector4[] Spheres = new Vector4[4];

        public void Apply(CasualtyController c)
        {
            if (targets == null) return;
            _mpb ??= new MaterialPropertyBlock();
            int n = 0;
            for (int i = 0; i < 4; i++) Spheres[i] = new Vector4(0, -1000, 0, 0);
            foreach (var w in c.WoundSites)
            {
                if (w == null || n >= 4) continue;
                float r = w.SoakRadius;
                Vector3 pos = w.transform.position + Vector3.down * r * 0.35f;
                Spheres[n++] = new Vector4(pos.x, pos.y, pos.z, r);
            }
            foreach (var t in targets)
            {
                if (t == null) continue;
                t.GetPropertyBlock(_mpb);
                for (int i = 0; i < 4; i++) _mpb.SetVector(SphereIds[i], Spheres[i]);
                _mpb.SetFloat(PallorId, c.Pallor);
                _mpb.SetFloat(CyanosisId, c.Cyanosis);
                _mpb.SetFloat(SweatId, c.Sweat);
                t.SetPropertyBlock(_mpb);
            }
        }
    }
}
