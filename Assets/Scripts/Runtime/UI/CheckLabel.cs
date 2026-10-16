using TMPro;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>
    /// Small white result card next to the medic's hands (w8, Training mode only): "Пульс на шее: 128 — слабый".
    /// Same look as the hint panel (rounded white card, dark text), billboarded to the viewer, fades after a while.
    /// </summary>
    public sealed class CheckLabel : MonoBehaviour
    {
        public float showSeconds = 4.5f;
        public string Text => _text != null ? _text.text : null;
        public bool Visible => gameObject.activeSelf && _alpha > 0.01f;

        TextMeshPro _text;
        Transform _card;
        Transform _viewer;
        Vector3 _anchor;
        float _until, _alpha;

        public static CheckLabel Create(string name = "CheckLabel")
        {
            var go = new GameObject(name);
            var l = go.AddComponent<CheckLabel>();
            l._card = new GameObject("Card").transform;
            l._card.SetParent(go.transform, false);
            UiMesh.Add(l._card, "Bg", UiMesh.RoundedRect(0.27f, 0.062f, 0.014f, Color.white, new Color(0.94f, 0.94f, 0.93f)), UiMesh.Mat(Color.white, 3100), Vector3.zero);
            l._text = UiMesh.Text(l._card, "Text", "", new Vector3(-0.124f, 0.022f, -0.001f), 0.0095f, new Color(0.12f, 0.13f, 0.15f), 0.248f, 3101, TextAlignmentOptions.TopLeft, true);
            go.SetActive(false);
            return l;
        }

        public void Show(string text, Vector3 worldAnchor, Transform viewer)
        {
            _text.text = text;
            _anchor = worldAnchor;
            _viewer = viewer;
            _until = Time.time + showSeconds;
            gameObject.SetActive(true);
            _alpha = 1f;
            Place();
        }

        void Place()
        {
            Vector3 view = _viewer != null ? _viewer.position : (Camera.main != null ? Camera.main.transform.position : _anchor + Vector3.back);
            Vector3 to = _anchor - view;
            // a little toward the viewer and up, so it does not hide the hands
            Vector3 p = _anchor - to.normalized * 0.08f + Vector3.up * 0.09f;
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(p - view, Vector3.up));
        }

        void LateUpdate()
        {
            Place();
            float target = Time.time < _until ? 1f : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime * 3f);
            float s = Mathf.Lerp(0.85f, 1f, _alpha);
            _card.localScale = new Vector3(s, s, s);
            if (_alpha <= 0f) gameObject.SetActive(false);
        }
    }
}
