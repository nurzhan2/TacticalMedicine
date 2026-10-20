using System;
using System.Collections.Generic;
using FieldMedic.Medical;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Scenarios.SelfAid
{
    /// <summary>
    /// Radio report as a short list menu (w9b): after a press on the PTT button a small dark card appears in front of
    /// the chest (free-hand side) with the current line of the report — "Кто / Где / Что / Что сделано / Запрос" — and
    /// two or three round buttons with the possible lines. Poke one with the free hand (or point + trigger): it is
    /// transmitted (radio screen + click) and the next line appears. Body-locked, faces the eyes.
    /// </summary>
    public sealed class SelfAidReportMenu : MonoBehaviour
    {
        public SelfAidRun run;
        public Transform head;
        public float side = 1f; // +1 = right of the chest (free hand right)
        public event Action<SelfAidReportOption> Chosen;

        public bool Open { get; private set; }
        public readonly List<RoundButton> Buttons = new List<RoundButton>();
        public List<SelfAidReportOption> Options { get; private set; } = new List<SelfAidReportOption>();

        Transform _content;
        float _yaw, _lockUntil;
        bool _yawInit;
        const float W = 0.3f;

        public static SelfAidReportMenu Create(SelfAidRun run, Transform head, float side)
        {
            var go = new GameObject("SelfAidReportMenu");
            var m = go.AddComponent<SelfAidReportMenu>();
            m.run = run; m.head = head; m.side = side;
            go.SetActive(false);
            return m;
        }

        public void Show()
        {
            if (run == null || run.ReportSent) { Hide(); return; }
            gameObject.SetActive(true);
            Open = true;
            _yawInit = false;
            Follow(true);
            Rebuild();
        }

        public void Hide()
        {
            Open = false;
            gameObject.SetActive(false);
        }

        void Rebuild()
        {
            if (_content != null) Destroy(_content.gameObject);
            Buttons.Clear();
            _content = new GameObject("Content").transform;
            _content.SetParent(transform, false);
            bool kz = GameSettings.KZ;
            Options = run.ReportOptions();
            _lockUntil = Time.time + 0.5f;
            float h = 0.07f + Options.Count * 0.05f;
            WorldUI.Quad(_content, "Card", new Vector3(0f, -h / 2f + 0.035f, 0.004f), new Vector2(W, h), WorldUI.Unlit(new Color(0.05f, 0.06f, 0.05f)));
            WorldUI.Text(_content, "Title", (kz ? "ДОКЛАД  " : "ДОКЛАД  ") + SelfAidRun.ReportStepTitle(run.ReportStep, kz), new Vector3(-W / 2 + 0.012f, 0.025f, 0f), 0.014f, new Color(1f, 0.78f, 0.38f));
            for (int i = 0; i < Options.Count; i++)
            {
                var o = Options[i];
                float y = -0.015f - i * 0.05f;
                var b = RoundButton.Create(_content, "Opt" + i, new Vector3(-W / 2 + 0.03f, y, 0f), 0.034f, new Color(0.24f, 0.32f, 0.4f), (i + 1).ToString(), 0.012f);
                int k = i;
                b.Clicked += () => Choose(k);
                // the finger that pressed the previous line is still there: the new buttons wait until it is pulled back
                b.interactable = false;
                Buttons.Add(b);
                WorldUI.Text(_content, "OptText" + i, WorldUI.Wrap(o.Text(kz), 34), new Vector3(-W / 2 + 0.055f, y + 0.011f, 0f), 0.0105f, new Color(0.93f, 0.91f, 0.84f));
            }
        }

        public void Choose(int index)
        {
            if (!Open || index < 0 || index >= Options.Count) return;
            var o = Options[index];
            run.ChooseReport(o);
            Chosen?.Invoke(o);
            if (run.ReportSent) Hide();
            else Rebuild();
        }

        /// <summary>Index of the right line (bots).</summary>
        public int CorrectIndex => Options.FindIndex(o => o.correct);

        void LateUpdate()
        {
            if (!Open) return;
            Follow(false);
            if (Time.time >= _lockUntil) foreach (var b in Buttons) if (b != null && !b.interactable) b.interactable = true;
        }

        void Follow(bool snap)
        {
            if (head == null) return;
            float hy = head.eulerAngles.y;
            if (!_yawInit || snap) { _yaw = hy; _yawInit = true; }
            float diff = Mathf.DeltaAngle(_yaw, hy);
            if (Mathf.Abs(diff) > 25f) _yaw += (Mathf.Abs(diff) - 25f) * Mathf.Sign(diff);
            var rot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 p = head.position + rot * new Vector3(0.12f * side, -0.19f, 0.42f);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(p - head.position));
        }
    }
}
