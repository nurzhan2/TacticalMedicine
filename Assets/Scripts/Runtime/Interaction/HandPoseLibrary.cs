using System.Collections.Generic;
using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>All poses by key (Resources/HandPoseLibrary; code defaults when the asset is missing).</summary>
    [CreateAssetMenu(menuName = "FieldMedic/Hand Pose Library")]
    public sealed class HandPoseLibrary : ScriptableObject
    {
        public List<HandPose> poses = new List<HandPose>();

        /// <summary>Keys every interaction needs (w8 list: CAT, bandage, gauze, shears, marker, needle, NPA, seal,
        /// blanket, card, radio, Erlan drag handle, wrist, ankle) + the procedure handles.</summary>
        public static readonly string[] RequiredKeys =
        {
            "cat", "bandage", "gauze", "shears", "marker", "needle", "npa", "seal", "blanket", "card", "radio",
            "drag_handle", "wrist", "ankle", "cat_strap", "cat_rod", "tab", "gloves", "lube", "shoulder", "knee", "grenade", "ring", "poke",
        };

        /// <summary>The default table (also written into the assets by W8Builder).</summary>
        public static List<HandPose> Defaults() => new List<HandPose>
        {
            HandPose.Make("cat", 0.62f, 0.72f, 0.75f, 0.78f, 0.55f, 0.7f, GrabStyle.Grip, "CAT body in the fist, thumb on the buckle"),
            HandPose.Make("bandage", 0.55f, 0.62f, 0.66f, 0.7f, 0.45f, 0.65f, GrabStyle.Grip, "Israeli bandage roll in the palm"),
            HandPose.Make("gauze", 0.38f, 0.42f, 0.58f, 0.64f, 0.42f, 0.8f, GrabStyle.Pinch, "gauze roll between thumb and two fingers"),
            HandPose.Make("shears", 0.45f, 0.78f, 0.82f, 0.85f, 0.35f, 0.3f, GrabStyle.Grip, "fingers in the lower ring, thumb in the upper ring"),
            HandPose.Make("marker", 0.55f, 0.68f, 0.8f, 0.85f, 0.55f, 0.85f, GrabStyle.Grip, "pen grip"),
            HandPose.Make("needle", 0.5f, 0.6f, 0.78f, 0.85f, 0.5f, 0.9f, GrabStyle.Pinch, "dart grip on the hub"),
            HandPose.Make("npa", 0.42f, 0.5f, 0.74f, 0.82f, 0.45f, 0.9f, GrabStyle.Pinch, "tube between thumb, index and middle"),
            HandPose.Make("seal", 0.22f, 0.25f, 0.4f, 0.5f, 0.2f, 0.95f, GrabStyle.Pinch, "flat package pinched by the edge"),
            HandPose.Make("blanket", 0.6f, 0.66f, 0.7f, 0.74f, 0.55f, 0.75f, GrabStyle.Grip, "foil gathered in the fist"),
            HandPose.Make("card", 0.18f, 0.22f, 0.45f, 0.55f, 0.18f, 1f, GrabStyle.Pinch, "card pinched flat"),
            HandPose.Make("radio", 0.05f, 0.8f, 0.86f, 0.9f, 0.6f, 0.6f, GrabStyle.Any, "press-to-talk: index out, the rest closed"),
            HandPose.Make("drag_handle", 0.85f, 0.9f, 0.92f, 0.92f, 0.8f, 0.55f, GrabStyle.Grip, "full fist through the drag strap"),
            HandPose.Make("wrist", 0.72f, 0.8f, 0.82f, 0.82f, 0.7f, 0.7f, GrabStyle.Grip, "ring grip around his wrist"),
            HandPose.Make("ankle", 0.68f, 0.76f, 0.8f, 0.8f, 0.66f, 0.65f, GrabStyle.Grip, "around the boot top"),
            HandPose.Make("cat_strap", 0.7f, 0.82f, 0.86f, 0.88f, 0.75f, 0.85f, GrabStyle.Grip, "strap pinched hard in the fist"),
            HandPose.Make("cat_rod", 0.66f, 0.74f, 0.78f, 0.8f, 0.58f, 0.6f, GrabStyle.Any, "windlass rod across the fingers"),
            HandPose.Make("tab", 0.4f, 0.45f, 0.7f, 0.78f, 0.45f, 0.95f, GrabStyle.Pinch, "tab between thumb and index"),
            HandPose.Make("gloves", 0.3f, 0.35f, 0.5f, 0.55f, 0.3f, 0.9f, GrabStyle.Pinch, "glove cuff pinched"),
            HandPose.Make("lube", 0.4f, 0.45f, 0.6f, 0.65f, 0.5f, 0.9f, GrabStyle.Grip, "packet squeezed"),
            HandPose.Make("shoulder", 0.6f, 0.66f, 0.7f, 0.72f, 0.6f, 0.7f, GrabStyle.Grip, "hand on his shoulder"),
            HandPose.Make("knee", 0.55f, 0.62f, 0.66f, 0.7f, 0.55f, 0.6f, GrabStyle.Grip, "behind the knee"),
            HandPose.Make("grenade", 0.7f, 0.76f, 0.8f, 0.82f, 0.7f, 0.7f, GrabStyle.Grip, "smoke grenade, lever under the fingers"),
            HandPose.Make("ring", 0.62f, 0.72f, 0.82f, 0.86f, 0.55f, 0.9f, GrabStyle.Pinch, "index hooked through the pull ring"),
            HandPose.Make("poke", 0.04f, 0.82f, 0.88f, 0.9f, 0.62f, 0.6f, GrabStyle.Any, "pointing finger for buttons"),
        };

        static HandPoseLibrary _current;
        static Dictionary<string, HandPose> _map;

        public static HandPoseLibrary Current
        {
            get
            {
                if (_current != null) return _current;
                _current = Resources.Load<HandPoseLibrary>("HandPoseLibrary");
                if (_current == null)
                {
                    _current = CreateInstance<HandPoseLibrary>();
                    _current.name = "HandPoseLibrary(default)";
                    _current.poses = Defaults();
                }
                _map = null;
                return _current;
            }
        }

        public static HandPose Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_map == null)
            {
                var lib = Current;   // may reset the map: resolve first
                _map = new Dictionary<string, HandPose>();
                foreach (var p in lib.poses) if (p != null && !string.IsNullOrEmpty(p.key)) _map[p.key] = p;
                // keys added in code after the asset was saved stay available
                foreach (var d in Defaults()) if (!_map.ContainsKey(d.key)) _map[d.key] = d;
            }
            return _map.TryGetValue(key, out var r) ? r : null;
        }
    }
}
