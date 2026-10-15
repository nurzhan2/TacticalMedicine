using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>
    /// Sewn-on patches (GDD_V2 §4): the Kazakhstan flag (Resources/Insignia/kz_flag_patch, the corrected flag:
    /// sun above the eagle, ornament on the hoist side = texture u 0) and the Satbayev University military department
    /// placeholder (Resources/Insignia/satbayev_patch — neutral shield with text, not the real logo). A patch is a
    /// quad whose texture "up" is the given up direction and whose front faces `outward`, so it is never mirrored.
    /// </summary>
    public static class Insignia
    {
        public const string KzFlag = "Insignia/kz_flag_patch";
        public const string Satbayev = "Insignia/satbayev_patch";

        static readonly Dictionary<string, Material> _lit = new Dictionary<string, Material>();
        static readonly Dictionary<string, Material> _unlit = new Dictionary<string, Material>();

        public static Texture2D Texture(string id) => Resources.Load<Texture2D>(id);

        public static Material LitMaterial(string id)
        {
            if (_lit.TryGetValue(id, out var m) && m != null) return m;
            var tex = Texture(id);
            m = WorldUI.Lit(tex != null ? Color.white : new Color(0f, 0.68f, 0.79f), 0.12f);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            m.name = "Patch_" + id;
            return _lit[id] = m;
        }

        public static Material UnlitMaterial(string id)
        {
            if (_unlit.TryGetValue(id, out var m) && m != null) return m;
            var tex = Texture(id);
            m = WorldUI.Unlit(tex != null ? Color.white : new Color(0f, 0.68f, 0.79f));
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            m.name = "PatchUnlit_" + id;
            return _unlit[id] = m;
        }

        /// <summary>
        /// A patch on `parent` at world `pos`, front facing `outward`, texture top toward `up` (world directions,
        /// taken at creation; the patch then rides on the parent). `size` = width × height in metres.
        /// </summary>
        public static GameObject Patch(Transform parent, string id, Vector3 pos, Vector3 outward, Vector3 up, Vector2 size, bool lit = true)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Patch_" + id.Substring(id.LastIndexOf('/') + 1);
            var col = q.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
            // the built-in quad shows its texture un-mirrored when seen from −Z (its normal is −Z, texture up +Y)
            up = Vector3.ProjectOnPlane(up, outward).normalized;
            q.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-outward, up));
            q.transform.SetParent(parent, true);
            var ls = parent != null ? parent.lossyScale : Vector3.one;
            q.transform.localScale = new Vector3(size.x / Mathf.Max(1e-4f, Mathf.Abs(ls.x)), size.y / Mathf.Max(1e-4f, Mathf.Abs(ls.y)), 1f);
            // non-uniform parent scale would shear the quad; patches go on bones/rigs with ~uniform scale
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = lit ? LitMaterial(id) : UnlitMaterial(id);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }
    }
}
