using System.Collections.Generic;
using FieldMedic.Interaction;
using UnityEngine;

namespace FieldMedic.Treatment
{
    /// <summary>
    /// Rip-away IFAK lid (w7, owner: "open the IFAK pouch by pulling its handle"). The pouch starts closed: a coyote
    /// lid hinged at the back edge covers the tray and the items in it cannot be seen or grabbed. Grab the red pull
    /// tab on the front edge and pull it ≥ <see cref="pullToOpen"/> away: the velcro lets go (crackle + haptic), the
    /// lid flips back and the items can be taken out one by one. Touching the tab alone does nothing.
    /// </summary>
    [DefaultExecutionOrder(-35)]
    public sealed class PouchFlap : MonoBehaviour
    {
        public IfakPouch pouch;
        [Tooltip("Hand travel with the tab held that rips the velcro open (m).")]
        public float pullToOpen = 0.07f;
        public float openAngle = -118f;
        public Vector3 size = new Vector3(0.43f, 0.014f, 0.25f);
        public float hingeHeight = 0.062f;

        public bool IsOpen { get; private set; }
        public ProxyHandle Tab { get; private set; }
        public Transform Lid { get; private set; }
        /// <summary>Pull progress 0..1 while the tab is held (hint / tests).</summary>
        public float Pull { get; private set; }

        float _angle;
        readonly List<Renderer> _hidden = new List<Renderer>();

        public static PouchFlap Add(IfakPouch p)
        {
            var f = p.gameObject.GetComponent<PouchFlap>() ?? p.gameObject.AddComponent<PouchFlap>();
            f.pouch = p;
            f.Build();
            return f;
        }

        void Build()
        {
            if (Lid != null) return;
            var coyote = Kit.Mat("PouchCoyote", Kit.Coyote, 0.12f);
            var dark = Kit.Mat("PouchDark", new Color(0.16f, 0.13f, 0.09f), 0.1f);
            var red = Kit.Mat("PouchRed", new Color(0.5f, 0.06f, 0.05f), 0.2f);
            var tabRed = Kit.Mat("PouchTabRed", new Color(0.55f, 0.08f, 0.06f), 0.25f);
            // hinge along the back edge (local +z), lid extends toward the player (−z)
            var hinge = new GameObject("LidHinge").transform;
            hinge.SetParent(transform, false);
            hinge.localPosition = new Vector3(0f, hingeHeight, size.z * 0.5f);
            Lid = hinge;
            var lid = new GameObject("Lid").transform;
            lid.SetParent(hinge, false);
            Kit.Part(lid, PrimitiveType.Cube, "LidPanel", new Vector3(0f, 0f, -size.z * 0.5f), size, coyote);
            Kit.Part(lid, PrimitiveType.Cube, "LidLip", new Vector3(0f, -0.022f, -size.z + 0.004f), new Vector3(size.x, 0.045f, 0.01f), coyote);
            Kit.Part(lid, PrimitiveType.Cube, "CrossH", new Vector3(0f, 0.0075f, -size.z * 0.5f), new Vector3(0.07f, 0.002f, 0.022f), red);
            Kit.Part(lid, PrimitiveType.Cube, "CrossV", new Vector3(0f, 0.0075f, -size.z * 0.5f), new Vector3(0.022f, 0.002f, 0.07f), red);
            Kit.Part(lid, PrimitiveType.Cube, "Velcro", new Vector3(0f, -0.045f, -size.z + 0.002f), new Vector3(size.x * 0.6f, 0.006f, 0.012f), dark);
            // red pull tab: a webbing loop sticking out of the front lip
            Kit.Part(lid, PrimitiveType.Cube, "PullTab", new Vector3(0f, -0.03f, -size.z - 0.022f), new Vector3(0.05f, 0.006f, 0.045f), tabRed);
            Kit.Merge(lid);
            Tab = ProxyHandle.Create<ProxyHandle>("Grab_PouchTab", lid, lid.TransformPoint(new Vector3(0f, -0.03f, -size.z - 0.025f)), 0.045f, 4);
            Tab.Grabbed += (_, h) => Haptics.Play(h.Input, Haptics.Grab);
            SetItemsVisible(false);
        }

        void SetItemsVisible(bool v)
        {
            if (pouch == null) return;
            if (!v)
            {
                _hidden.Clear();
                foreach (var it in pouch.Items)
                {
                    if (it == null || !it.InPouch) continue;
                    foreach (var r in it.GetComponentsInChildren<Renderer>())
                        if (r.enabled) { r.enabled = false; _hidden.Add(r); }
                }
            }
            else
            {
                foreach (var r in _hidden) if (r != null) r.enabled = true;
                _hidden.Clear();
            }
        }

        void Update()
        {
            if (pouch != null && !IsOpen && _hidden.Count == 0 && pouch.Built) SetItemsVisible(false); // items built after the lid
            if (!IsOpen && Tab != null && Tab.Hand != null)
            {
                float d = Tab.PalmDelta.magnitude;
                Pull = Mathf.Clamp01(d / pullToOpen);
                Haptics.Tension(Tab.Input, Pull * 0.8f);
                if (d >= pullToOpen) Open(Tab.Hand);
            }
            else if (!IsOpen) Pull = 0f;
            float target = IsOpen ? openAngle : 0f;
            if (!Mathf.Approximately(_angle, target))
            {
                _angle = Mathf.MoveTowards(_angle, target, Time.deltaTime * 520f);
                Lid.localRotation = Quaternion.Euler(-_angle, 0f, 0f);
            }
        }

        /// <summary>Velcro lets go (only from a real pull of the tab, or scenario setup with `byHand` null).</summary>
        public void Open(HandGrabber byHand = null)
        {
            if (IsOpen) return;
            IsOpen = true;
            Pull = 1f;
            if (byHand != null) { Haptics.Play(byHand.Input, Haptics.PressClick, 1.3f); Tab.ForceRelease(); }
            TreatmentAudio.Velcro(Tab != null ? Tab.transform.position : transform.position, 0.9f);
            if (Tab != null) Tab.gameObject.SetActive(false);
            SetItemsVisible(true);
            Debug.Log("[FMVR] IFAK opened " + (byHand != null ? "by pulling the tab" : "(setup)"));
        }
    }
}
