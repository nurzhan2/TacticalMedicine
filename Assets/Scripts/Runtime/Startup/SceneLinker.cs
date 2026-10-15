using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FieldMedic.Startup
{
    /// <summary>
    /// Restores serialized references that crossed scene borders when the editor split the battlefield into
    /// Main_Core + additive scenes (w0b, <c>SceneSplitter</c>). Each link names the target by hierarchy path
    /// ("OVRCameraRig/TrackingSpace/CenterEyeAnchor") + component type; it is resolved in Awake (runs before the
    /// other scripts of this scene) and written into the source field by reflection.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class SceneLinker : MonoBehaviour
    {
        [Serializable]
        public struct Link
        {
            public Component source;
            public string field;          // "head" or "points.Array.data[3]"
            public string targetPath;     // root/child/child in any loaded scene
            public string targetType;     // "GameObject", "Transform" or a component type's full name
        }

        public Link[] links = Array.Empty<Link>();
        public int Resolved { get; private set; }
        public int Failed { get; private set; }

        void Awake()
        {
            Resolved = Failed = 0;
            foreach (var l in links)
            {
                if (l.source == null) { Failed++; continue; }
                var target = Find(l.targetPath, l.targetType);
                if (target == null || !Assign(l.source, l.field, target))
                {
                    Failed++;
                    Debug.LogWarning($"[FMVR] SceneLinker {gameObject.scene.name}: {l.source.GetType().Name}.{l.field} → '{l.targetPath}' ({l.targetType}) not resolved");
                    continue;
                }
                Resolved++;
            }
            if (Failed == 0) Debug.Log($"[FMVR] SceneLinker {gameObject.scene.name}: {Resolved} cross-scene links");
        }

        public static Object Find(string path, string type)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int slash = path.IndexOf('/');
            string rootName = slash < 0 ? path : path.Substring(0, slash);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                foreach (var root in s.GetRootGameObjects())
                {
                    if (root.name != rootName) continue;
                    var t = slash < 0 ? root.transform : root.transform.Find(path.Substring(slash + 1));
                    if (t == null) continue;
                    if (type == "GameObject") return t.gameObject;
                    if (type == "Transform" || type == typeof(Transform).FullName) return t;
                    foreach (var c in t.GetComponents<Component>())
                        if (c != null && c.GetType().FullName == type) return c;
                }
            }
            return null;
        }

        public static bool Assign(object owner, string fieldPath, Object value)
        {
            // "a" | "a.Array.data[i]"
            string name = fieldPath;
            int index = -1;
            int arr = fieldPath.IndexOf(".Array.data[", StringComparison.Ordinal);
            if (arr >= 0)
            {
                name = fieldPath.Substring(0, arr);
                int a = fieldPath.IndexOf('[', arr) + 1, b = fieldPath.IndexOf(']', a);
                if (!int.TryParse(fieldPath.Substring(a, b - a), out index)) return false;
                if (b != fieldPath.Length - 1) return false; // nested paths are not supported
            }
            else if (fieldPath.Contains(".")) return false;
            var f = FindField(owner.GetType(), name);
            if (f == null) return false;
            if (index < 0)
            {
                if (!f.FieldType.IsInstanceOfType(value)) return false;
                f.SetValue(owner, value);
                return true;
            }
            var coll = f.GetValue(owner);
            if (coll is Array array)
            {
                if (index >= array.Length || !array.GetType().GetElementType().IsInstanceOfType(value)) return false;
                array.SetValue(value, index);
                return true;
            }
            if (coll is IList list)
            {
                if (index >= list.Count) return false;
                list[index] = value;
                return true;
            }
            return false;
        }

        static FieldInfo FindField(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }
    }
}
