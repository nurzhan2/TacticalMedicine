using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>Small runtime-generated textures/materials (no texture assets, no download).</summary>
    public static class ProceduralTex
    {
        static Material _skin;

        /// <summary>
        /// Skin showing through cut-open clothing: bloodied skin in the middle, frayed cloth edge, transparent
        /// outside. Uses the blood decal shader (alpha-blended, wet highlight).
        /// </summary>
        public static Material ExposedSkinMaterial()
        {
            if (_skin != null) return _skin;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "ExposedSkin", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            var rng = new System.Random(7);
            var skin = new Color(0.62f, 0.43f, 0.34f);
            var blood = new Color(0.32f, 0.03f, 0.025f);
            var cloth = new Color(0.17f, 0.16f, 0.1f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (n - 1f) * 2f - 1f, v = y / (n - 1f) * 2f - 1f;
                float ang = Mathf.Atan2(v, u);
                float fray = 0.06f * Mathf.Sin(ang * 23f) + 0.04f * Mathf.Sin(ang * 51f + 1.3f) + 0.03f * (float)rng.NextDouble();
                float r = Mathf.Sqrt(u * u + v * v);
                float edge = 0.78f + fray;
                Color c; float a;
                if (r < edge - 0.08f)
                {
                    // smeared blood toward the centre (where the wound is), mottled
                    float bl = Mathf.Clamp01(1.1f - r * 1.3f + 0.25f * Mathf.PerlinNoise(x * 0.09f, y * 0.09f));
                    c = Color.Lerp(skin, blood, bl * 0.85f);
                    a = 1f;
                }
                else if (r < edge)
                {
                    c = Color.Lerp(cloth, blood, 0.35f) * (0.8f + 0.2f * (float)rng.NextDouble()); // frayed, soaked cut edge
                    a = 1f;
                }
                else { c = cloth; a = 0f; }
                c.a = a;
                px[y * n + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            var sh = Shader.Find("FieldMedic/BloodDecal") ?? Shader.Find("Universal Render Pipeline/Unlit");
            _skin = new Material(sh) { name = "ExposedSkin", renderQueue = 3001 };
            _skin.SetTexture("_BaseMap", tex);
            _skin.SetColor("_BaseColor", Color.white);
            _skin.SetFloat("_Gloss", 0.45f);
            _skin.SetFloat("_Alpha", 1f);
            return _skin;
        }
    }

    /// <summary>One-shot procedural sounds for treatment feedback (air hiss of a decompression / burp).</summary>
    public static class TreatmentAudio
    {
        static AudioClip _hiss;
        public static bool Muted;

        public static AudioClip HissClip
        {
            get
            {
                if (_hiss != null) return _hiss;
                const int rate = 22050;
                int len = (int)(rate * 1.6f);
                var data = new float[len];
                var rng = new System.Random(3);
                float lp = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)rate;
                    float env = Mathf.Min(1f, t / 0.03f) * Mathf.Exp(-t * 2.2f);
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += (w - lp) * 0.55f; // soft high-pass-ish hiss after subtracting the low-passed part
                    data[i] = (w - lp * 0.6f) * env * 0.5f;
                }
                _hiss = AudioClip.Create("Hiss", len, 1, rate, false);
                _hiss.SetData(data, 0);
                return _hiss;
            }
        }

        static AudioClip _velcro;

        /// <summary>Rip-away velcro: dense random crackle (hook-and-loop letting go) over ~0.35 s.</summary>
        public static AudioClip VelcroClip
        {
            get
            {
                if (_velcro != null) return _velcro;
                const int rate = 22050;
                int len = (int)(rate * 0.45f);
                var data = new float[len];
                var rng = new System.Random(11);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)rate;
                    float env = Mathf.Min(1f, t / 0.015f) * (t < 0.32f ? 1f : Mathf.Exp(-(t - 0.32f) * 30f));
                    float crackle = rng.NextDouble() < 0.18 ? (float)(rng.NextDouble() * 2.0 - 1.0) : 0f;
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.25f;
                    data[i] = (crackle + w) * env * 0.6f;
                }
                _velcro = AudioClip.Create("Velcro", len, 1, rate, false);
                _velcro.SetData(data, 0);
                return _velcro;
            }
        }

        public static void Velcro(Vector3 at, float volume)
        {
            if (Muted) return;
            AudioSource.PlayClipAtPoint(VelcroClip, at, volume);
        }

        public static void Hiss(Vector3 at, float volume)
        {
            if (Muted) return;
            AudioSource.PlayClipAtPoint(HissClip, at, volume);
        }
    }
}
