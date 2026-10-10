using System;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Medical;
using FieldMedic.Scenarios;
using UnityEngine;

namespace FieldMedic.UI
{
    /// <summary>
    /// The board in the dugout (GDD §9): main menu (Training / Scenarios / Exam / Settings), scenario cards with
    /// best scores, training lessons, settings (language RU/KZ, hints, gore level, seated, dominant hand, snap turn),
    /// the scenario briefing and the debrief (score, survival, limb outcome, mistakes with consequences, timeline).
    /// Everything is built at runtime from primitives + TextMesh; all buttons are <see cref="RoundButton"/>s.
    /// The panel faces -Z (viewer side); size 1.5 × 0.95 m.
    /// </summary>
    public sealed class MenuBoard : MonoBehaviour
    {
        public const float W = 1.5f, H = 0.95f;
        public string Page { get; private set; } = "";
        public DebriefReport ShownReport { get; private set; }
        /// <summary>Set by tests: buttons only record, no scene reload.</summary>
        public Action<string> launchOverride;

        Transform _content;
        Vector3 _homePos;
        Quaternion _homeRot;
        bool _homeSet;

        static readonly Color Ink = new Color(0.93f, 0.91f, 0.84f);
        static readonly Color Dim = new Color(0.66f, 0.66f, 0.6f);
        static readonly Color Amber = new Color(1f, 0.78f, 0.38f);
        static readonly Color Olive = new Color(0.36f, 0.42f, 0.2f);
        static readonly Color Rust = new Color(0.55f, 0.2f, 0.12f);
        static readonly Color Steel = new Color(0.24f, 0.32f, 0.4f);
        static readonly Color Good = new Color(0.55f, 0.95f, 0.5f);
        static readonly Color Bad = new Color(1f, 0.45f, 0.35f);

        void Awake()
        {
            BuildFrame();
            BoardPointer.Ensure();
        }

        void BuildFrame()
        {
            var wood = WorldUI.Lit(new Color(0.2f, 0.15f, 0.1f), 0.15f);
            WorldUI.Box(transform, "Backing", new Vector3(0f, 0f, 0.03f), new Vector3(W + 0.1f, H + 0.1f, 0.04f), wood);
            WorldUI.Quad(transform, "Slate", new Vector3(0f, 0f, 0.008f), new Vector2(W, H), WorldUI.Unlit(new Color(0.045f, 0.055f, 0.045f)));
            // a small shaded lamp over the board: makes it readable in the dark dugout without lighting the trench
            var lamp = new GameObject("BoardLamp");
            lamp.transform.SetParent(transform, false);
            lamp.transform.localPosition = new Vector3(0f, H * 0.5f + 0.12f, -0.35f);
            var l = lamp.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.82f, 0.6f);
            l.intensity = 0.5f;
            l.range = 1.6f;
            l.shadows = LightShadows.None;
            WorldUI.Box(lamp.transform, "Shade", Vector3.zero, new Vector3(0.12f, 0.04f, 0.08f), WorldUI.Lit(new Color(0.15f, 0.16f, 0.12f), 0.4f));
        }

        void Clear()
        {
            if (_content != null) Destroy(_content.gameObject);
            _content = new GameObject("Content").transform;
            _content.SetParent(transform, false);
        }

        void RememberHome()
        {
            if (_homeSet) return;
            _homeSet = true;
            _homePos = transform.position;
            _homeRot = transform.rotation;
        }

        /// <summary>Back to the dugout wall (after a debrief was shown in front of the player).</summary>
        public void ReturnHome()
        {
            if (_homeSet) transform.SetPositionAndRotation(_homePos, _homeRot);
        }

        TextMesh Txt(string text, float x, float y, float h, Color c, TextAnchor a = TextAnchor.UpperLeft, TextAlignment al = TextAlignment.Left) =>
            WorldUI.Text(_content, "T", text, new Vector3(x, y, 0f), h, c, a, al);

        RoundButton Btn(string label, float x, float y, float d, Color c, Action onClick, float labelH = 0.03f, string caption = null, float capH = 0.02f)
        {
            var b = RoundButton.Create(_content, "Btn_" + label, new Vector3(x, y, 0f), d, c, label, labelH, caption, capH);
            b.Clicked += onClick;
            return b;
        }

        void Header(string sub)
        {
            Txt("БАУЫР", -W / 2 + 0.06f, H / 2 - 0.04f, 0.05f, Amber);
            Txt(GameSettings.T("Тренажёр боевого медика", "Жауынгер-медик тренажёры") + (sub != null ? "  ·  " + sub : ""), -W / 2 + 0.36f, H / 2 - 0.065f, 0.02f, Dim);
            Btn("RU", W / 2 - 0.16f, H / 2 - 0.08f, 0.07f, GameSettings.KZ ? Steel * 0.6f : Steel, () => { GameSettings.Language = GameLanguage.RU; Refresh(); }, 0.018f);
            Btn("KZ", W / 2 - 0.07f, H / 2 - 0.08f, 0.07f, GameSettings.KZ ? Steel : Steel * 0.6f, () => { GameSettings.Language = GameLanguage.KZ; Refresh(); }, 0.018f);
        }

        Action _refresh;
        void Refresh() => _refresh?.Invoke();

        void Back(Action to) => Btn("←", -W / 2 + 0.09f, -H / 2 + 0.09f, 0.09f, Rust * 0.8f, to, 0.035f, GameSettings.T("Назад", "Артқа"), 0.016f);

        // ------------------------------------------------------------------ pages
        public void ShowMain()
        {
            RememberHome();
            ReturnHome();
            Clear(); Page = "main"; _refresh = ShowMain;
            Header(null);
            Txt(GameSettings.T("Ночь. Окоп первой линии. Ерлан — твой брат по оружию.\nЕго жизнь — в твоих руках.",
                    "Түн. Бірінші шептегі окоп. Ерлан — сенің қаруластың.\nОның өмірі — сенің қолыңда."),
                -W / 2 + 0.06f, H / 2 - 0.16f, 0.022f, Ink);
            float y = -0.06f, d = 0.2f;
            Btn("1", -0.54f, y, d, Olive, ShowTraining, 0.06f, GameSettings.T("ОБУЧЕНИЕ", "ОҚЫТУ"), 0.026f);
            Btn("2", -0.18f, y, d, Rust, ShowScenarios, 0.06f, GameSettings.T("СЦЕНАРИИ", "СЦЕНАРИЙЛЕР"), 0.026f);
            Btn("3", 0.18f, y, d, new Color(0.45f, 0.12f, 0.1f), () => Go("exam", () => ScenarioLaunch.Exam(System.Environment.TickCount)), 0.06f, GameSettings.T("ЭКЗАМЕН", "ЕМТИХАН"), 0.026f);
            Btn("4", 0.54f, y, d, Steel, ShowSettings, 0.06f, GameSettings.T("НАСТРОЙКИ", "БАПТАУЛАР"), 0.026f);
            Txt(GameSettings.T("Нажми кнопку пальцем или наведи луч и нажми курок.  ПК: наведи и щёлкни / Enter.",
                    "Түймені саусақпен бас немесе сәулемен көрсетіп, шүріппені бас."),
                0f, -H / 2 + 0.06f, 0.014f, Dim, TextAnchor.LowerCenter, TextAlignment.Center);
        }

        public void ShowScenarios()
        {
            Clear(); Page = "scenarios"; _refresh = ShowScenarios;
            Header(GameSettings.T("Сценарии", "Сценарийлер"));
            var all = ScenarioCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                int col = i % 4, row = i / 4;
                float x = -0.54f + col * 0.36f, y = 0.17f - row * 0.36f;
                int best = GameSettings.BestScore(def.id);
                var color = def.polished ? Rust : Olive;
                Btn(def.number.ToString(), x, y, 0.13f, color, () => ShowBriefing(def, ScenarioVariant.Default, true), 0.045f);
                Txt(def.Title(GameSettings.KZ), x, y - 0.085f, 0.022f, Amber, TextAnchor.UpperCenter, TextAlignment.Center);
                Txt(WorldUI.Wrap(def.Tagline(GameSettings.KZ), 30), x, y - 0.12f, 0.0125f, Ink, TextAnchor.UpperCenter, TextAlignment.Center);
                if (best >= 0) Txt(GameSettings.T("лучший: ", "үздік: ") + best, x + 0.075f, y + 0.07f, 0.013f, Good, TextAnchor.UpperLeft);
            }
            Back(ShowMain);
        }

        public void ShowBriefing(ScenarioDefinition def, ScenarioVariant v, bool interactive)
        {
            RememberHome();
            Clear(); Page = "briefing"; _refresh = () => ShowBriefing(def, v, interactive);
            Header(GameSettings.T("Брифинг", "Брифинг"));
            bool kz = GameSettings.KZ;
            Txt($"S{def.number}  {def.Title(kz)}", -W / 2 + 0.06f, H / 2 - 0.15f, 0.04f, Amber);
            Txt(WorldUI.Wrap(def.Briefing(kz), 70), -W / 2 + 0.06f, H / 2 - 0.23f, 0.019f, Ink);
            string sk = string.Join("\n", (kz ? def.skillsKz : def.skillsRu).Select(s => "•  " + s));
            Txt(GameSettings.T("Навыки:", "Дағдылар:"), -W / 2 + 0.06f, -0.02f, 0.02f, Dim);
            Txt(sk, -W / 2 + 0.06f, -0.06f, 0.018f, new Color(0.7f, 0.9f, 0.62f));
            if (!interactive) return;
            Btn(GameSettings.T("ПУСК", "БАСТА"), W / 2 - 0.2f, -0.2f, 0.2f, new Color(0.62f, 0.16f, 0.1f),
                () => Go("scenario:" + def.id, () => ScenarioLaunch.Scenario(def.id, ScenarioRandom())),
                0.03f, GameSettings.T("вариант случайный", "кездейсоқ нұсқа"), 0.014f);
            Back(ShowScenarios);
        }

        public void ShowTraining()
        {
            Clear(); Page = "training"; _refresh = ShowTraining;
            Header(GameSettings.T("Обучение", "Оқыту"));
            Txt(GameSettings.T("Пошаговые уроки с инструктором. Без обстрела, время не ограничено, монитор показателей включён.",
                    "Нұсқаушымен қадамдық сабақтар. Атқылаусыз, уақыт шектелмеген."),
                -W / 2 + 0.06f, H / 2 - 0.15f, 0.018f, Ink);
            var lessons = TrainingCatalog.All;
            for (int i = 0; i < lessons.Count; i++)
            {
                var l = lessons[i];
                float x = -0.56f + i * 0.28f;
                Btn((i + 1).ToString(), x, 0.0f, 0.16f, Olive, () => Go("lesson:" + l.id, () => ScenarioLaunch.Training(l.id)), 0.05f,
                    WorldUI.Wrap(l.Title(GameSettings.KZ), 16), 0.019f);
            }
            Back(ShowMain);
        }

        public void ShowSettings()
        {
            Clear(); Page = "settings"; _refresh = ShowSettings;
            Header(GameSettings.T("Настройки", "Баптаулар"));
            float y = 0.22f, dy = 0.115f, lx = -W / 2 + 0.08f;
            void Row(string label, string[] opts, int sel, Action<int> set)
            {
                Txt(label, lx, y + 0.015f, 0.022f, Ink);
                for (int i = 0; i < opts.Length; i++)
                {
                    int k = i;
                    Btn(opts[i], 0.02f + i * 0.2f, y, 0.085f, i == sel ? Amber * 0.75f : Steel * 0.55f, () => { set(k); ShowSettings(); }, 0.016f);
                }
                y -= dy;
            }
            Row(GameSettings.T("Язык", "Тіл"), new[] { "РУС", "ҚАЗ" }, GameSettings.KZ ? 1 : 0, i => GameSettings.Language = (GameLanguage)i);
            Row(GameSettings.T("Подсказки", "Кеңестер"), new[] { GameSettings.T("ВКЛ", "ҚОСУ"), GameSettings.T("ВЫКЛ", "ӨШІРУ") }, GameSettings.Hints ? 0 : 1, i => GameSettings.Hints = i == 0);
            Row(GameSettings.T("Кровь", "Қан"), new[] { GameSettings.T("МИН", "АЗ"), GameSettings.T("СРЕД", "ОРТА"), GameSettings.T("МАКС", "МАКС") }, (int)GameSettings.Gore, i => GameSettings.Gore = (GoreLevel)i);
            Row(GameSettings.T("Режим", "Режим"), new[] { GameSettings.T("СТОЯ", "ТҰРЫП"), GameSettings.T("СИДЯ", "ОТЫРЫП") }, GameSettings.Seated ? 1 : 0, i => GameSettings.Seated = i == 1);
            Row(GameSettings.T("Ведущая рука", "Негізгі қол"), new[] { GameSettings.T("ПРАВАЯ", "ОҢ"), GameSettings.T("ЛЕВАЯ", "СОЛ") }, GameSettings.LeftHanded ? 1 : 0, i => GameSettings.LeftHanded = i == 1);
            Row(GameSettings.T("Поворот", "Бұрылу"), new[] { "30°", "45°" }, GameSettings.SnapTurnDegrees < 40f ? 0 : 1, i => GameSettings.SnapTurnDegrees = i == 0 ? 30f : 45f);
            Back(ShowMain);
        }

        // ------------------------------------------------------------------ debrief
        /// <summary>The after-action review. If `viewer` is given, the board comes to the player (1.1 m in front).</summary>
        public void ShowDebrief(DebriefReport r, Transform viewer = null)
        {
            RememberHome();
            if (viewer != null)
            {
                Vector3 fwd = viewer.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                transform.SetPositionAndRotation(viewer.position + fwd * 1.15f + Vector3.down * 0.25f, Quaternion.LookRotation(fwd));
            }
            ShownReport = r;
            Clear(); Page = "debrief"; _refresh = () => ShowDebrief(r);
            bool kz = GameSettings.KZ;
            Header(GameSettings.T("Разбор", "Талдау") + (r.title != null ? "  ·  " + r.title : ""));

            // left column: the verdict
            float lx = -W / 2 + 0.06f;
            Txt(r.score.ToString(), lx, H / 2 - 0.13f, 0.11f, r.survived ? (r.score >= 85 ? Good : Amber) : Bad);
            Txt(r.Grade(kz), lx + 0.22f, H / 2 - 0.15f, 0.028f, r.survived ? Amber : Bad);
            Txt(GameSettings.T("лучший: ", "үздік: ") + r.bestScore + (r.newBest ? GameSettings.T("  НОВЫЙ РЕКОРД", "  ЖАҢА РЕКОРД") : ""), lx + 0.22f, H / 2 - 0.2f, 0.016f, Dim);
            Txt(WorldUI.Wrap(r.outcomeText, 44), lx, H / 2 - 0.3f, 0.018f, r.survived ? Ink : Bad);
            Txt(WorldUI.Wrap(GameSettings.T("Конечности: ", "Аяқ-қол: ") + r.limbOutcome, 44), lx, H / 2 - 0.39f, 0.016f, Ink);
            string ctl = r.bleedControlSeconds >= 0f ? $"{r.bleedControlSeconds:0} с" : "—";
            Txt(GameSettings.T("Остановка кровотечения: ", "Қан тоқтатылды: ") + ctl + "\n" + GameSettings.T("Кровопотеря: ", "Қан жоғалту: ") + $"{r.lossPercent:0}%",
                lx, H / 2 - 0.47f, 0.016f, Dim);

            // right column: mistakes with consequence
            float rx = -0.06f;
            Txt(GameSettings.T("Ошибки", "Қателер") + (r.mistakes.Count == 0 ? GameSettings.T(" — нет. Чисто сработано.", " — жоқ.") : ""), rx, H / 2 - 0.13f, 0.024f, r.mistakes.Count == 0 ? Good : Bad);
            float y = H / 2 - 0.17f;
            foreach (var m in r.mistakes.Take(5))
            {
                var t = Txt($"−{m.points}  " + WorldUI.Wrap(m.text, 62), rx, y, 0.0135f, new Color(1f, 0.7f, 0.6f));
                int lines = t.text.Split('\n').Length;
                y -= 0.021f * lines + 0.004f;
                if (!string.IsNullOrEmpty(m.consequence))
                {
                    var c = Txt("→ " + WorldUI.Wrap(m.consequence, 64), rx + 0.03f, y, 0.012f, Dim);
                    y -= 0.019f * c.text.Split('\n').Length + 0.01f;
                }
            }

            // timeline (bottom right)
            var tl = r.timeline.Where(e => e.severity != Severity.Info || e.text.Length < 60).ToList();
            var shown = tl.Count > 9 ? tl.Skip(tl.Count - 9).ToList() : tl;
            y = Mathf.Min(y - 0.02f, -0.05f);
            Txt(GameSettings.T("Хронология", "Хронология"), rx, y, 0.018f, Amber);
            y -= 0.032f;
            foreach (var e in shown)
            {
                var col = e.severity == Severity.Error ? Bad : e.severity == Severity.Warning ? Amber : e.severity == Severity.Ok ? Good : Dim;
                string line = e.text.Length > 78 ? e.text.Substring(0, 76) + "…" : e.text;
                Txt($"{e.clock}  {line}", rx, y, 0.011f, col);
                y -= 0.019f;
                if (y < -H / 2 + 0.03f) break;
            }

            // actions
            Btn(GameSettings.T("ЕЩЁ", "ТАҒЫ"), lx + 0.09f, -H / 2 + 0.13f, 0.13f, Rust, () => Go("retry:" + r.scenarioId, () => ScenarioLaunch.Scenario(r.scenarioId, ScenarioVariant.Random(System.Environment.TickCount))), 0.022f, GameSettings.T("повторить", "қайталау"), 0.014f);
            Btn(GameSettings.T("МЕНЮ", "МӘЗІР"), lx + 0.29f, -H / 2 + 0.13f, 0.13f, Steel, () => Go("menu", () => ScenarioLaunch.Menu()), 0.022f, GameSettings.T("в блиндаж", "блиндажға"), 0.014f);
            var next = ScenarioCatalog.All.SkipWhile(d => d.id != r.scenarioId).Skip(1).FirstOrDefault();
            if (next != null)
                Btn(GameSettings.T("ДАЛЕЕ", "КЕЛЕСІ"), lx + 0.49f, -H / 2 + 0.13f, 0.13f, Olive, () => Go("next:" + next.id, () => ScenarioLaunch.Scenario(next.id, ScenarioVariant.Default)), 0.02f, next.Title(kz), 0.014f);
        }

        /// <summary>Scenario mode: side and second hit vary, delayed onset only in the exam.</summary>
        static ScenarioVariant ScenarioRandom()
        {
            var v = ScenarioVariant.Random(System.Environment.TickCount);
            v.delayedOnset = false;
            return v;
        }

        void Go(string what, Action request)
        {
            Debug.Log("[FMVR] board: " + what);
            if (launchOverride != null) { launchOverride(what); return; }
            ScenarioDirector.Launch(request);
        }
    }
}
