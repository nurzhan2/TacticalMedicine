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
            if (_homeSet) { transform.SetPositionAndRotation(_homePos, _homeRot); transform.localScale = Vector3.one; }
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
            // Kazakhstan flag (corrected texture: sun above the eagle, ornament on the hoist = left) + Satbayev patch
            WorldUI.Quad(_content, "Flag_KZ", new Vector3(W / 2 - 0.36f, H / 2 - 0.08f, -0.002f), new Vector2(0.12f, 0.06f), Insignia.UnlitMaterial(Insignia.KzFlag));
            WorldUI.Quad(_content, "Patch_Satbayev", new Vector3(W / 2 - 0.255f, H / 2 - 0.08f, -0.002f), new Vector2(0.052f, 0.065f), Insignia.UnlitMaterial(Insignia.Satbayev));
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
            Txt(GameSettings.T("Степь. Разрушенный посёлок. Бой не стихает.\nЕрлан — твой брат по оружию. Его жизнь — в твоих руках.",
                    "Дала. Қираған кент. Шайқас тоқтамайды.\nЕрлан — сенің қаруластың. Оның өмірі — сенің қолыңда."),
                -W / 2 + 0.06f, H / 2 - 0.16f, 0.022f, Ink);
            // w7: the presentation run — big white button, Training mode with the white hint panel
            var demo = Btn("ДЕМО\nDEMO", 0.56f, 0.205f, 0.2f, new Color(0.95f, 0.95f, 0.93f), () => Go("lesson:" + TrainingCatalog.DemoId, () => ScenarioLaunch.Training(TrainingCatalog.DemoId)), 0.03f);
            demo.name = "Btn_DEMO";
            if (demo.Label != null) demo.Label.color = new Color(0.1f, 0.1f, 0.09f);
            Txt(GameSettings.T("ДЕМО · 5–7 минут, белая подсказка:\nжгут · в укрытие · тампонада\nнаклейка · одеяло · карточка · вертолёт",
                    "ДЕМО · 5–7 минут, ақ кеңес:\nжгут · баспанаға · тампонада\nжапсырма · көрпе · карточка · тікұшақ"),
                0.43f, 0.2f, 0.0135f, Dim, TextAnchor.UpperRight, TextAlignment.Right);
            float y = -0.04f, d = 0.2f;
            Btn("1", -0.54f, y, d, Olive, ShowTraining, 0.06f, GameSettings.T("ОБУЧЕНИЕ", "ОҚЫТУ"), 0.026f);
            Btn("2", -0.18f, y, d, Rust, ShowScenarios, 0.06f, GameSettings.T("БОЙ", "ШАЙҚАС"), 0.026f);
            Btn("3", 0.18f, y, d, new Color(0.45f, 0.12f, 0.1f), () => Go("exam", () => ScenarioLaunch.Exam(System.Environment.TickCount)), 0.06f, GameSettings.T("ЭКЗАМЕН", "ЕМТИХАН"), 0.026f);
            Btn("4", 0.54f, y, d, Steel, ShowSettings, 0.06f, GameSettings.T("НАСТРОЙКИ", "БАПТАУЛАР"), 0.026f);
            float cy = y - 0.205f;
            Txt(GameSettings.T("белая подсказка,\nбой вдали, без провала", "ақ кеңес, шайқас алыста,\nсәтсіздік жоқ"), -0.54f, cy, 0.0135f, Dim, TextAnchor.UpperCenter, TextAlignment.Center);
            Txt(GameSettings.T("сценарии S1–S8 + самопомощь,\nпо тебе стреляют", "S1–S8 + өзіне көмек,\nсаған оқ атылады"), -0.18f, cy, 0.0135f, Dim, TextAnchor.UpperCenter, TextAlignment.Center);
            Txt(GameSettings.T("случайный сценарий,\nоценка TCCC", "кездейсоқ сценарий,\nTCCC бағасы"), 0.18f, cy, 0.0135f, Dim, TextAnchor.UpperCenter, TextAlignment.Center);
            Txt(GameSettings.T("Нажми кнопку пальцем или наведи луч и нажми курок.  ПК: наведи и щёлкни / Enter.",
                    "Түймені саусақпен бас немесе сәулемен көрсетіп, шүріппені бас."),
                0f, -H / 2 + 0.06f, 0.014f, Dim, TextAnchor.LowerCenter, TextAlignment.Center);
        }

        public void ShowScenarios()
        {
            Clear(); Page = "scenarios"; _refresh = ShowScenarios;
            Header(GameSettings.T("Бой", "Шайқас"));
            var all = ScenarioCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                // w9b: 9 scenarios (8 + self-aid) → 5 columns
                int col = i % 5, row = i / 5;
                float x = -0.6f + col * 0.3f, y = 0.17f - row * 0.36f;
                int best = GameSettings.BestScore(def.id);
                var color = def.polished ? Rust : Olive;
                Btn(def.number.ToString(), x, y, 0.13f, color, () => ShowBriefing(def, ScenarioVariant.Default, true), 0.045f);
                var title = Txt(WorldUI.Wrap(def.Title(GameSettings.KZ), 20), x, y - 0.085f, def.selfAid ? 0.018f : 0.022f, Amber, TextAnchor.UpperCenter, TextAlignment.Center);
                float ty = y - 0.12f - (title.text.Split('\n').Length - 1) * 0.024f;
                Txt(WorldUI.Wrap(def.Tagline(GameSettings.KZ), 26), x, ty, 0.0118f, Ink, TextAnchor.UpperCenter, TextAlignment.Center);
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
            var brief = Txt(WorldUI.Wrap(def.Briefing(kz), 70), -W / 2 + 0.06f, H / 2 - 0.23f, 0.019f, Ink);
            float sy = H / 2 - 0.23f - brief.text.Split('\n').Length * 0.03f - 0.04f;
            string sk = string.Join("\n", (kz ? def.skillsKz : def.skillsRu).Select(s => "•  " + s));
            Txt(GameSettings.T("Навыки:", "Дағдылар:"), -W / 2 + 0.06f, sy, 0.02f, Dim);
            Txt(sk, -W / 2 + 0.06f, sy - 0.04f, 0.018f, new Color(0.7f, 0.9f, 0.62f));
            if (def.selfAid)
                Txt(GameSettings.T("Ты один. Помощь выдвинется после твоего доклада по рации (кнопка на левом плече).",
                        "Сен жалғызсың. Көмек рациямен баяндағаннан кейін шығады (сол иықтағы түйме)."), -W / 2 + 0.06f, -H / 2 + 0.1f, 0.015f, Dim);
            else
            Txt(GameSettings.T("Эвакуация: ", "Эвакуация: ") + def.evacMinutes.ToString("0") + GameSettings.T(" мин после запроса по радио", " мин") +
                (def.timeSkipMinutes.Length > 0 ? GameSettings.T("   ·   часы на руке: «+» — следующая веха", "   ·   сағат: «+»") : ""),
                -W / 2 + 0.06f, -H / 2 + 0.1f, 0.015f, Dim);
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
            Txt(GameSettings.T("Белая карточка подсказывает каждый шаг, когда ты рядом с Ерланом. Бой идёт вдали, по тебе не стреляют.",
                    "Ерланның қасында ақ карточка әр қадамды көрсетеді. Шайқас алыста, саған оқ атылмайды."),
                -W / 2 + 0.06f, H / 2 - 0.15f, 0.018f, Ink);
            var topics = TrainingCatalog.Topics;
            for (int i = 0; i < topics.Length; i++)
            {
                var l = TrainingCatalog.Get(topics[i]);
                if (l == null) continue;
                bool full = l.id == "full";
                float x = full ? -0.5f : -0.2f + ((i - 1) % 3) * 0.3f;
                float y = full ? -0.02f : 0.1f - ((i - 1) / 3) * 0.3f;
                Btn(full ? "TCCC" : i.ToString(), x, y, full ? 0.26f : 0.15f, full ? Rust : Olive, () => Go("lesson:" + l.id, () => ScenarioLaunch.Training(l.id)),
                    full ? 0.045f : 0.05f, WorldUI.Wrap(l.Title(GameSettings.KZ), 18), full ? 0.024f : 0.018f);
                if (full) Txt(GameSettings.T("под огнём → укрытие → эвакуация", "оқ астында → баспана → эвакуация"), x, y - 0.2f, 0.013f, Dim, TextAnchor.UpperCenter, TextAlignment.Center);
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

        void ComeTo(Transform viewer)
        {
            if (viewer == null) return;
            // "in front of the eyes" is often a wall in the ruins: take the freest direction
            Vector3 eye = viewer.position + Vector3.down * 0.25f;
            Vector3 fwd = WorldUI.FreeDirection(eye, viewer.forward, 2f, out float clear);
            float dist = Mathf.Clamp(clear - 0.2f, 0.6f, 0.85f);
            transform.SetPositionAndRotation(eye + fwd * dist, Quaternion.LookRotation(fwd));
            transform.localScale = Vector3.one * 0.68f; // 1.0 m wide, same angular size
        }

        /// <summary>Training lesson finished: time, wrong actions, repeat / next topic / menu.</summary>
        public void ShowTrainingDone(TrainingLesson l, float seconds, int wrongs, Transform viewer = null)
        {
            RememberHome();
            ComeTo(viewer);
            Clear(); Page = "training_done"; _refresh = () => ShowTrainingDone(l, seconds, wrongs);
            bool kz = GameSettings.KZ;
            Header(GameSettings.T("Обучение", "Оқыту"));
            Txt(GameSettings.T("Урок выполнен", "Сабақ орындалды"), -W / 2 + 0.06f, H / 2 - 0.15f, 0.05f, Good);
            Txt(l.Title(kz), -W / 2 + 0.06f, H / 2 - 0.25f, 0.03f, Amber);
            Txt(GameSettings.T("Время: ", "Уақыт: ") + Mathf.FloorToInt(seconds / 60f) + ":" + Mathf.FloorToInt(seconds % 60f).ToString("00") +
                "     " + GameSettings.T("Шагов: ", "Қадамдар: ") + l.steps.Count +
                "     " + GameSettings.T("Ошибочных действий: ", "Қате әрекеттер: ") + wrongs, -W / 2 + 0.06f, H / 2 - 0.33f, 0.02f, Ink);
            string steps = string.Join("\n", l.steps.Select((st, i) => $"{i + 1}. {st.Text(kz)}"));
            Txt(steps, 0.1f, H / 2 - 0.15f, 0.015f, new Color(0.7f, 0.9f, 0.62f));
            var topics = TrainingCatalog.Topics;
            int idx = System.Array.IndexOf(topics, l.id);
            string next = idx >= 0 && idx + 1 < topics.Length ? topics[idx + 1] : null;
            float lx = -W / 2 + 0.06f;
            Btn(GameSettings.T("ЕЩЁ", "ТАҒЫ"), lx + 0.09f, -H / 2 + 0.13f, 0.13f, Rust, () => Go("lesson:" + l.id, () => ScenarioLaunch.Training(l.id)), 0.022f, GameSettings.T("повторить", "қайталау"), 0.014f);
            Btn(GameSettings.T("МЕНЮ", "МӘЗІР"), lx + 0.29f, -H / 2 + 0.13f, 0.13f, Steel, () => Go("menu", () => ScenarioLaunch.Menu()), 0.022f, GameSettings.T("в руины", "қирандыға"), 0.014f);
            if (next != null)
            {
                var nl = TrainingCatalog.Get(next);
                Btn(GameSettings.T("ДАЛЕЕ", "КЕЛЕСІ"), lx + 0.49f, -H / 2 + 0.13f, 0.13f, Olive, () => Go("lesson:" + next, () => ScenarioLaunch.Training(next)), 0.02f, nl.Title(kz), 0.014f);
            }
        }

        // ------------------------------------------------------------------ debrief
        /// <summary>The after-action review. If `viewer` is given, the board comes to the player (0.6–0.85 m in front).</summary>
        public void ShowDebrief(DebriefReport r, Transform viewer = null)
        {
            RememberHome();
            ComeTo(viewer);
            ShownReport = r;
            Clear(); Page = "debrief"; _refresh = () => ShowDebrief(r);
            bool kz = GameSettings.KZ;
            Header(GameSettings.T("Разбор", "Талдау") + (r.title != null ? "  ·  " + r.title : ""));

            // left column: the verdict
            float lx = -W / 2 + 0.06f;
            Txt(r.score.ToString(), lx, H / 2 - 0.13f, 0.11f, r.survived ? (r.score >= 85 ? Good : Amber) : Bad);
            float gx = lx + (r.score >= 100 ? 0.29f : 0.22f); // three digits are wider
            Txt(r.Grade(kz), gx, H / 2 - 0.15f, 0.028f, r.survived ? Amber : Bad);
            Txt(GameSettings.T("лучший: ", "үздік: ") + r.bestScore + (r.newBest ? GameSettings.T("  НОВЫЙ РЕКОРД", "  ЖАҢА РЕКОРД") : ""), gx, H / 2 - 0.2f, 0.016f, Dim);
            Txt(WorldUI.Wrap(r.outcomeText, 44), lx, H / 2 - 0.3f, 0.018f, r.survived ? Ink : Bad);
            Txt(WorldUI.Wrap(GameSettings.T("Конечности: ", "Аяқ-қол: ") + r.limbOutcome, 44), lx, H / 2 - 0.39f, 0.016f, Ink);
            string ctl = r.bleedControlSeconds >= 0f ? $"{r.bleedControlSeconds:0} с" : "—";
            Txt(GameSettings.T("Остановка кровотечения: ", "Қан тоқтатылды: ") + ctl + "\n" + GameSettings.T("Кровопотеря: ", "Қан жоғалту: ") + $"{r.lossPercent:0}%",
                lx, H / 2 - 0.47f, 0.016f, Dim);

            // middle column: numbered mistakes (same numbers as on the map and the timeline) with the TCCC step
            float rx = -0.2f;
            Txt(GameSettings.T("Ошибки", "Қателер") + (r.mistakes.Count == 0 ? GameSettings.T(" — нет. Чисто сработано.", " — жоқ.") : ""), rx, H / 2 - 0.13f, r.selfAid ? 0.02f : 0.024f, r.mistakes.Count == 0 ? Good : Bad);
            float y = H / 2 - 0.17f;
            foreach (var m in r.mistakes.OrderBy(m => m.number).Take(5))
            {
                Marker(m.number, rx + 0.012f, y - 0.011f, MistakeColor(m), 0.026f);
                var t = Txt($"−{m.points}  " + WorldUI.Wrap(m.text, 44), rx + 0.034f, y, 0.0125f, new Color(1f, 0.7f, 0.6f));
                int lines = t.text.Split('\n').Length;
                y -= 0.02f * lines + 0.002f;
                if (!string.IsNullOrEmpty(m.step))
                {
                    Txt(GameSettings.T("шаг: ", "қадам: ") + m.step, rx + 0.034f, y, 0.011f, Amber);
                    y -= 0.018f;
                }
                if (!string.IsNullOrEmpty(m.consequence))
                {
                    var c = Txt("→ " + WorldUI.Wrap(m.consequence, 46), rx + 0.034f, y, 0.011f, Dim);
                    y -= 0.017f * c.text.Split('\n').Length + 0.008f;
                }
                if (y < -0.18f) break;
            }
            if (r.selfAid && r.checks.Count > 0) Checklist(r, 0.36f, H / 2 - 0.19f);
            else MiniMap(r, new Vector2(0.505f, 0.075f), new Vector2(0.36f, 0.48f));
            TimelineStrip(r, -0.2f, 0.71f, -0.255f);

            // actions
            Btn(GameSettings.T("ЕЩЁ", "ТАҒЫ"), lx + 0.09f, -H / 2 + 0.13f, 0.13f, Rust, () => Go("retry:" + r.scenarioId, () => ScenarioLaunch.Scenario(r.scenarioId, ScenarioVariant.Random(System.Environment.TickCount))), 0.022f, GameSettings.T("повторить", "қайталау"), 0.014f);
            Btn(GameSettings.T("МЕНЮ", "МӘЗІР"), lx + 0.29f, -H / 2 + 0.13f, 0.13f, Steel, () => Go("menu", () => ScenarioLaunch.Menu()), 0.022f, GameSettings.T("в руины", "қирандыға"), 0.014f);
            var next = ScenarioCatalog.All.SkipWhile(d => d.id != r.scenarioId).Skip(1).FirstOrDefault();
            if (next != null)
                Btn(GameSettings.T("ДАЛЕЕ", "КЕЛЕСІ"), lx + 0.49f, -H / 2 + 0.13f, 0.13f, Olive, () => Go("next:" + next.id, () => ScenarioLaunch.Scenario(next.id, ScenarioVariant.Default)), 0.02f, next.Title(kz), 0.014f);
        }

        /// <summary>w9b self-aid checklist (one row per TCCC self-aid step, green / red) in the right column.</summary>
        void Checklist(DebriefReport r, float x, float y)
        {
            Txt(GameSettings.T("Самопомощь", "Өзіне көмек"), x, y, 0.024f, Amber);
            y -= 0.05f;
            foreach (var c in r.checks)
            {
                var col = c.ok ? new Color(0.3f, 0.62f, 0.28f) : new Color(0.75f, 0.2f, 0.15f);
                WorldUI.Disc(_content, "Chk", new Vector3(x + 0.012f, y - 0.01f, -0.001f), 0.022f, 0.003f, WorldUI.Unlit(col));
                Txt(c.ok ? "+" : "−", x + 0.012f, y - 0.01f, 0.014f, Color.white, TextAnchor.MiddleCenter, TextAlignment.Center).transform.localPosition = new Vector3(x + 0.012f, y - 0.01f, -0.004f);
                Txt(WorldUI.Wrap(c.text, 30), x + 0.032f, y, 0.0135f, c.ok ? Ink : new Color(1f, 0.7f, 0.6f));
                y -= 0.042f;
            }
        }

        static Color MistakeColor(DebriefMistake m) => m.points >= 15 ? new Color(0.85f, 0.2f, 0.15f) : m.points >= 10 ? new Color(0.9f, 0.5f, 0.12f) : new Color(0.85f, 0.75f, 0.2f);

        void Marker(int number, float x, float y, Color c, float d)
        {
            WorldUI.Disc(_content, "Mark" + number, new Vector3(x, y, -0.001f), d, 0.003f, WorldUI.Unlit(c));
            Txt(number.ToString(), x, y, d * 0.42f, Color.white, TextAnchor.MiddleCenter, TextAlignment.Center).transform.localPosition = new Vector3(x, y, -0.004f);
        }

        /// <summary>Battlefield map (top-down render, Resources/UI/battlefield_map) with the run's events.</summary>
        void MiniMap(DebriefReport r, Vector2 centre, Vector2 size)
        {
            Txt(GameSettings.T("Карта", "Карта"), centre.x - size.x / 2, centre.y + size.y / 2 + 0.035f, 0.018f, Amber);
            var tex = Resources.Load<Texture2D>("UI/battlefield_map");
            var mat = WorldUI.Unlit(Color.white);
            if (tex != null) { mat.SetTexture("_BaseMap", tex); mat.mainTexture = tex; }
            else mat.SetColor("_BaseColor", new Color(0.25f, 0.24f, 0.2f));
            WorldUI.Quad(_content, "Map", new Vector3(centre.x, centre.y, 0.001f), size, mat);
            WorldUI.Quad(_content, "MapFrame", new Vector3(centre.x, centre.y, 0.003f), size + new Vector2(0.012f, 0.012f), WorldUI.Unlit(new Color(0.55f, 0.5f, 0.38f)));
            Vector3 P(float x, float z) => new Vector3(centre.x + (x - MapMin.x) / (MapMax.x - MapMin.x) * size.x - size.x / 2,
                centre.y + (z - MapMin.y) / (MapMax.y - MapMin.y) * size.y - size.y / 2, 0f);
            // LZ and the fall point
            var lz = FieldMedic.Environment.BattlefieldLayout.LandingZone;
            var lp = P(lz.x, lz.z);
            Txt("H", lp.x, lp.y, 0.02f, new Color(0.4f, 0.75f, 1f), TextAnchor.MiddleCenter, TextAlignment.Center).transform.localPosition = lp + new Vector3(0f, 0f, -0.003f);
            if (r.hasCasualtyPos)
            {
                var cp = P(r.casualtyX, r.casualtyZ);
                Txt("+", cp.x, cp.y, 0.022f, new Color(1f, 0.25f, 0.2f), TextAnchor.MiddleCenter, TextAlignment.Center).transform.localPosition = cp + new Vector3(0f, 0f, -0.003f);
            }
            var placed = new List<Vector3>();
            foreach (var m in r.mistakes.Where(m => m.hasPos).OrderBy(m => m.number))
            {
                var mp = P(Mathf.Clamp(m.x, MapMin.x, MapMax.x), Mathf.Clamp(m.z, MapMin.y, MapMax.y));
                // fan out markers on the same spot
                int same = placed.Count(q => Vector3.Distance(q, mp) < 0.02f);
                placed.Add(mp);
                mp += new Vector3(0.024f * Mathf.Cos(same * 1.3f), 0.024f * Mathf.Sin(same * 1.3f), 0f) * (same > 0 ? 1f : 0f);
                Marker(m.number, mp.x, mp.y, MistakeColor(m), 0.024f);
            }
            Txt(GameSettings.T("+ ранение   H площадка   ● ошибка", "+ жара   H алаң   ● қате"), centre.x - size.x / 2, centre.y - size.y / 2 - 0.012f, 0.011f, Dim);
        }

        /// <summary>Map window of the battlefield render (x, z metres): our position, the open ground and the LZ.</summary>
        public static readonly Vector2 MapMin = new Vector2(-30f, -50f), MapMax = new Vector2(30f, 30f);

        /// <summary>Phase segments (under fire / in cover / evacuation) with numbered mistake ticks.</summary>
        void TimelineStrip(DebriefReport r, float x0, float x1, float y)
        {
            float w = x1 - x0;
            Txt(GameSettings.T("Хронология", "Хронология"), x0, y + 0.06f, 0.016f, Amber);
            WorldUI.Quad(_content, "Strip", new Vector3(x0 + w / 2, y, 0.001f), new Vector2(w, 0.016f), WorldUI.Unlit(new Color(0.18f, 0.2f, 0.18f)));
            foreach (var ph in r.phases)
            {
                float a = r.TimelineX(ph.start), b = r.TimelineX(ph.end);
                if (b - a < 0.002f) continue;
                var col = ph.id == "CUF" ? new Color(0.75f, 0.2f, 0.15f) : ph.id == "TFC" ? new Color(0.3f, 0.55f, 0.28f) : new Color(0.25f, 0.45f, 0.75f);
                WorldUI.Quad(_content, "Phase_" + ph.id, new Vector3(x0 + (a + b) / 2 * w, y, -0.0005f), new Vector2((b - a) * w, 0.016f), WorldUI.Unlit(col));
                string label = ph.id == "CUF" ? GameSettings.T("под огнём", "оқ астында") : ph.id == "TFC" ? GameSettings.T("в укрытии", "баспанада") : GameSettings.T("эвакуация", "эвакуация");
                if ((b - a) * w > 0.08f) Txt(label, x0 + a * w + 0.004f, y - 0.014f, 0.0105f, Dim);
            }
            int k = 0;
            foreach (var m in r.mistakes.Where(m => m.time >= 0f).OrderBy(m => m.number))
            {
                float mx = x0 + r.TimelineX(m.time) * w;
                WorldUI.Quad(_content, "Tick" + m.number, new Vector3(mx, y + 0.012f, -0.001f), new Vector2(0.003f, 0.03f), WorldUI.Unlit(MistakeColor(m)));
                Marker(m.number, mx, y + 0.036f + (k++ % 2) * 0.0f, MistakeColor(m), 0.022f);
            }
            Txt(ScenarioClock.Format(r.startTime), x0, y - 0.03f, 0.011f, Dim);
            Txt(ScenarioClock.Format(r.endTime), x1, y - 0.03f, 0.011f, Dim, TextAnchor.UpperRight, TextAlignment.Right);
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
