using System.IO;
using System.Linq;
using FieldMedic.EditorTools;
using FieldMedic.EditorTools.SceneBuilders;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w7 (owner 15.10 23:51: "the Kazakhstan flag is UPSIDE DOWN"). The flag texture is sampled: the sun (a solid
    /// disc) must be the upper half of the yellow emblem with the thin eagle under it, and the ornament band must be on
    /// the hoist side (u ≈ 0, next to the pole). The cloth mesh maps u = 0 at the pole and v = 1 at the top, the pole
    /// flag's material uses that texture, and the sleeve/shoulder patch textures read the same way.
    /// </summary>
    public class FlagOrientationTests
    {
        static bool Yellow(Color c) => c.r > 0.66f && c.g > 0.58f && c.b < 0.47f;
        static bool Blue(Color c) => c.b > 0.55f && c.r < 0.35f;

        /// <summary>Loads a PNG straight from disk (no import settings involved), row 0 = bottom like UV v.</summary>
        static Texture2D Load(string path)
        {
            if (!File.Exists(path)) Assert.Ignore(path + " not present (licensed asset, not in the public repo)");
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(t.LoadImage(File.ReadAllBytes(path)), "decode " + path);
            return t;
        }

        /// <summary>Asserts "sun above the eagle" and "ornament on the hoist side (left in texture space)".</summary>
        static void AssertUpright(Texture2D t, string what)
        {
            int w = t.width, h = t.height;
            var px = t.GetPixels(); // row-major, y = 0 at the bottom
            int x0 = (int)(w * 0.3f), x1 = (int)(w * 0.75f);
            int top = -1, bottom = h;
            for (int y = 0; y < h; y++)
            for (int x = x0; x < x1; x++)
                if (Yellow(px[y * w + x])) { top = Mathf.Max(top, y); bottom = Mathf.Min(bottom, y); }
            Assert.Greater(top, bottom, what + ": no yellow emblem in the centre");
            int mid = (top + bottom) / 2, upper = 0, lower = 0;
            for (int y = bottom; y <= top; y++)
            for (int x = x0; x < x1; x++)
                if (Yellow(px[y * w + x])) { if (y > mid) upper++; else lower++; }
            float ratio = upper / (float)Mathf.Max(1, lower);
            Debug.Log($"[FMVR] flag test {what}: emblem v {bottom / (float)h:F2}..{top / (float)h:F2}, upper/lower yellow {ratio:F2}");
            Assert.Greater(ratio, 1.2f, what + ": the solid sun must be the UPPER part of the emblem (eagle below it) — flag upside down?");
            float cy = (top + bottom) * 0.5f / h;
            Assert.That(cy, Is.InRange(0.35f, 0.65f), what + ": emblem vertically centred");

            float Band(float u0, float u1)
            {
                int n = 0, yel = 0;
                for (int y = (int)(h * 0.1f); y < h * 0.9f; y += 2)
                for (int x = (int)(w * u0); x < w * u1; x++) { n++; if (Yellow(px[y * w + x])) yel++; }
                return yel / (float)Mathf.Max(1, n);
            }
            float hoist = Band(0f, 0.15f), fly = Band(0.85f, 1f);
            Debug.Log($"[FMVR] flag test {what}: ornament yellow hoist {hoist:F3} fly {fly:F3}");
            Assert.Greater(hoist, 0.06f, what + ": ornament band on the hoist side (u≈0, at the pole)");
            Assert.Less(fly, hoist * 0.25f, what + ": no ornament on the fly side");
            Assert.IsTrue(Blue(px[(h / 2) * w + (int)(w * 0.9f)]), what + ": sky-blue field");
        }

        [Test]
        public void FlagTexture_SunAboveEagle_OrnamentAtHoist()
        {
            AssertUpright(Load(W7Demo.FlagTexturePath), "pole flag");
        }

        [Test]
        public void PatchTexture_SunAboveEagle_OrnamentAtHoist()
        {
            AssertUpright(Load("Assets/_Licensed/_Insignia/Resources/Insignia/kz_flag_patch.png"), "sleeve patch");
        }

        [Test]
        public void ClothMesh_HoistAtPole_TopIsTop()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(W7Demo.FlagMeshPath);
            Assert.IsNotNull(mesh, W7Demo.FlagMeshPath);
            var v = mesh.vertices; var uv = mesh.uv;
            for (int i = 0; i < v.Length; i++)
            {
                Assert.AreEqual(v[i].x / W7Demo.FlagLength, uv[i].x, 0.01f, "u grows from the pole (x = 0) to the fly end");
                Assert.AreEqual(v[i].y / W7Demo.FlagHeight, uv[i].y, 0.01f, "v grows from the bottom to the top");
            }
            Assert.Less(BattlefieldFx.FlagUV(0f, 0f).x, 0.01f);
            Assert.Greater(BattlefieldFx.FlagUV(0f, 1f).y, 0.99f);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(W7Demo.FlagMaterialPath);
            Assert.IsNotNull(mat);
            Assert.AreEqual(W7Demo.FlagTexturePath, AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), "pole flag uses the corrected texture");
            Assert.AreEqual(Vector2.one, mat.GetTextureScale("_BaseMap"));
        }

        [Test]
        public void SatbayevPatch_TextAtTop_NotMirrored()
        {
            var t = Load("Assets/Art/Insignia/Resources/Insignia/satbayev_patch.png");
            // the shield is wider at the top and ends in a point at the bottom; the gold text rows are in the upper third
            int w = t.width, h = t.height;
            var px = t.GetPixels();
            int Gold(int y0, int y1) { int n = 0; for (int y = y0; y < y1; y++) for (int x = 0; x < w; x++) { var c = px[y * w + x]; if (c.r > 0.7f && c.g > 0.6f && c.b < 0.5f) n++; } return n; }
            Assert.Greater(Gold((int)(h * 0.7f), (int)(h * 0.9f)), Gold((int)(h * 0.1f), (int)(h * 0.3f)), "text ӘСКЕРИ КАФЕДРА at the top");
            Color bl = px[8 * w + 8], tl = px[(h - 9) * w + 8];
            Assert.Less(bl.r, 0.4f, "bottom corners are outside the pointed shield");
            Assert.Greater(tl.r, 0.6f, "top corners are the gold border");
        }
    }
}
