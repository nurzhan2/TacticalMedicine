using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldMedic.Medical
{
    /// One short action line on the hint card; `check` (optional) ticks its checkbox before the whole step is done.
    [Serializable]
    public sealed class LessonAction
    {
        public string ru, kz;
        public string check;
        public LessonAction() { }
        public LessonAction(string ru, string kz, string check = null) { this.ru = ru; this.kz = kz; this.check = check; }
        public string Text(bool k) => k && !string.IsNullOrEmpty(kz) ? kz : ru;
    }

    /// <summary>
    /// One step of a lesson (GDD_V2 §3 white hint panel): title, 2–3 short actions with checkboxes, the IFAK item and
    /// the body target that glow, an icon, the TCCC phase, the voice line and the wrong actions watched while the step
    /// is current. `id` is also the completion check evaluated by the Unity layer (it knows the hands and items).
    /// The same table feeds the debrief, so step titles and mistake reasons read the same in both places.
    /// </summary>
    [Serializable]
    public sealed class LessonStep
    {
        public string id;       // completion check key in the runtime
        public string ru, kz;   // step title
        public string voice;    // instructor line id (Resources/Radio/<voice>_<lang>)
        public string phase = "TFC";   // CUF | TFC | TACEVAC
        public string item;     // IFAK item to glow: CAT, Gauze, Bandage, ChestSeal, Needle, Npa, Gloves, Shears, Marker, Blanket, Card, Radio, Smoke
        public string target;   // body/world target: Erlan, ThighHigh, Wound, Wound2, Foot, TqLabel, DragHandle, Cover, Chest, ChestBack, Nose, Body, Stretcher
        public string icon;     // icon key (Resources/HintIcons/<icon>)
        public List<LessonAction> actions = new();
        public List<string> watch = new();   // wrong-action ids (HintCatalog.Wrongs) checked during this step

        public LessonStep() { }
        public LessonStep(string id, string ru, string kz, string voice = null) { this.id = id; this.ru = ru; this.kz = kz; this.voice = voice; }
        public string Text(bool k) => k && !string.IsNullOrEmpty(kz) ? kz : ru;

        public LessonStep Act(string ru, string kz, string check = null) { actions.Add(new LessonAction(ru, kz, check)); return this; }
        public LessonStep Show(string item, string target, string icon) { this.item = item; this.target = target; this.icon = icon; return this; }
        public LessonStep In(string phase) { this.phase = phase; return this; }
        public LessonStep Watch(params string[] ids) { watch.AddRange(ids); return this; }
    }

    [Serializable]
    public sealed class TrainingLesson
    {
        public string id, titleRu, titleKz;
        public WoundType woundType;
        public BodyRegion region;
        public string visual = "Ragged";
        public bool posterior;
        public bool unconscious;
        /// Second, minor wound (found by the blood sweep in the full TCCC run).
        public bool secondWound;
        public WoundType secondType = WoundType.Venous;
        public BodyRegion secondRegion = BodyRegion.ForearmL;
        /// The second wound is under the clothing (blood sweep) — false: torn open by the blast, visible at once.
        public bool secondHidden = true;
        /// Third wound (DEMO: penetrating chest wound for the chest seal).
        public bool thirdWound;
        public WoundType thirdType = WoundType.PenetratingChest;
        public BodyRegion thirdRegion = BodyRegion.ChestR;
        public string thirdVisual = "ChestEntry";
        /// Ends with the real debrief (score, mistakes, map) instead of the training summary.
        public bool debrief;
        /// Erlan lies in the open (care under fire + drag) instead of inside the ruins.
        public bool inTheOpen;
        /// Blood already lost when the lesson starts (w8 assessment: visible shock, weak radial pulse).
        public float startLossFraction;
        public List<LessonStep> steps = new();
        public string Title(bool kz) => kz ? titleKz : titleRu;
        public int IndexOf(string stepId) => steps.FindIndex(s => s.id == stepId);
    }

    /// Why an action is wrong (yellow hint card) — also the debrief's mistake text in both languages.
    [Serializable]
    public sealed class HintWrong
    {
        public string id, ru, kz;
        public HintWrong() { }
        public HintWrong(string id, string ru, string kz) { this.id = id; this.ru = ru; this.kz = kz; }
        public string Text(bool k) => k && !string.IsNullOrEmpty(kz) ? kz : ru;
    }

    /// <summary>Progress through a lesson's checklist (pure; the runtime feeds it the satisfied step ids).</summary>
    public sealed class LessonProgress
    {
        public readonly TrainingLesson lesson;
        public int Current { get; private set; }
        public readonly List<float> doneAt = new();
        public bool Complete => Current >= lesson.steps.Count;
        public LessonStep Step => Complete ? null : lesson.steps[Current];
        public int StepNumber => Math.Min(Current + 1, lesson.steps.Count);
        public int StepCount => lesson.steps.Count;
        public event Action<LessonStep> Advanced;

        public LessonProgress(TrainingLesson l) { lesson = l; }

        /// Advance while the current step is satisfied (steps are strictly ordered, like the instructor teaches).
        public int Update(Func<string, bool> satisfied, float time)
        {
            int n = 0;
            while (!Complete && satisfied(lesson.steps[Current].id))
            {
                var s = lesson.steps[Current];
                doneAt.Add(time);
                Current++;
                n++;
                Advanced?.Invoke(s);
            }
            return n;
        }

        /// Advance exactly one step if it is satisfied (the hint card shows the green check before moving on).
        public bool TryAdvanceOne(Func<string, bool> satisfied, float time)
        {
            if (Complete || !satisfied(lesson.steps[Current].id)) return false;
            var s = lesson.steps[Current];
            doneAt.Add(time);
            Current++;
            Advanced?.Invoke(s);
            return true;
        }

        /// Checkbox states of the current step's actions: an action with its own check ticks as soon as it holds
        /// (and stays ticked); actions without a check tick only with the step.
        public bool[] ActionStates(Func<string, bool> satisfied, bool[] sticky = null)
        {
            var s = Step;
            if (s == null) return Array.Empty<bool>();
            var r = new bool[s.actions.Count];
            for (int i = 0; i < r.Length; i++)
            {
                var a = s.actions[i];
                r[i] = (sticky != null && i < sticky.Length && sticky[i]) || (!string.IsNullOrEmpty(a.check) && satisfied(a.check));
            }
            return r;
        }
    }

    /// <summary>
    /// The lesson and hint table (GDD_V2 §3): topics full TCCC / tourniquet / packing / drag / chest seal (+ airway).
    /// Default content lives here (pure, testable); the Unity layer can override it with the HintTable asset so the
    /// texts can be edited without code. Mistake reasons are shared with the debrief.
    /// </summary>
    public static class TrainingCatalog
    {
        static List<TrainingLesson> _all;
        static Dictionary<string, HintWrong> _wrongs;
        public static IReadOnlyList<TrainingLesson> All => _all ??= Build();
        public static IReadOnlyDictionary<string, HintWrong> Wrongs => _wrongs ??= BuildWrongs().ToDictionary(w => w.id);
        public static TrainingLesson Get(string id) => All.FirstOrDefault(l => l.id == id);

        /// Topics on the Training page, in menu order.
        public static readonly string[] Topics = { "full", "tq", "pack", "drag", "chest", "npa", "assess" };

        /// Use an edited table (HintTable asset). Null/empty arguments keep the defaults.
        public static void Use(List<TrainingLesson> lessons, List<HintWrong> wrongs)
        {
            if (lessons != null && lessons.Count > 0)
            {
                // lessons added in code after the asset was saved (w7 DEMO) stay available
                _all = new List<TrainingLesson>(lessons);
                foreach (var d in Build()) if (!_all.Any(l => l.id == d.id)) _all.Add(d);
            }
            if (wrongs != null && wrongs.Count > 0) _wrongs = wrongs.Where(w => !string.IsNullOrEmpty(w.id)).GroupBy(w => w.id).ToDictionary(g => g.Key, g => g.First());
        }

        public static void ResetDefaults() { _all = null; _wrongs = null; }

        public static List<TrainingLesson> Defaults() => Build();
        public static List<HintWrong> DefaultWrongs() => BuildWrongs();

        /// Reason text for a wrong action / protocol penalty (null when the table has none).
        public static string Reason(string code, bool kz) => code != null && Wrongs.TryGetValue(code, out var w) ? w.Text(kz) : null;

        /// The step of the full TCCC lesson a mistake belongs to (debrief: "Шаг 2 · Жгут «высоко и туго»").
        public static LessonStep StepForMistake(string code)
        {
            var full = Get("full");
            if (full == null || code == null) return null;
            return full.steps.FirstOrDefault(s => s.watch.Contains(code)) ?? (MistakeStep.TryGetValue(code, out var id) ? full.steps.FirstOrDefault(s => s.id == id) : null);
        }

        /// Protocol penalties that are not watched live but still belong to a step of the full run.
        static readonly Dictionary<string, string> MistakeStep = new()
        {
            ["tq_slow"] = "tq_lock", ["tq_not_marked"] = "tq_time", ["missed_wound"] = "sweep", ["hypothermia"] = "blanket",
            ["no_medevac"] = "medevac", ["player_exposed"] = "drag_cover", ["big_blood_loss"] = "tq_lock", ["tq_loose"] = "tq_lock",
            ["tq_not_rechecked"] = "pulse_foot", ["no_gloves"] = "gloves",
        };

        /// The presentation run behind the big white ДЕМО button (w7).
        public const string DemoId = "demo";

        static List<TrainingLesson> Build() => new()
        {
            // ------------------------------------------------------------------ DEMO (presentation, 5–7 min)
            new TrainingLesson
            {
                id = DemoId, titleRu = "ДЕМО: Ерлан ранен в поле", titleKz = "ДЕМО: Ерлан далада жараланды", woundType = WoundType.ArterialLimb, region = BodyRegion.ThighR,
                inTheOpen = true, secondWound = true, secondType = WoundType.Venous, secondRegion = BodyRegion.ThighL, secondHidden = false,
                thirdWound = true, thirdType = WoundType.PenetratingChest, thirdRegion = BodyRegion.ChestR, thirdVisual = "ChestEntry", debrief = true,
                steps =
                {
                    new LessonStep("reach", "К раненому", "Жаралыға", "h_reach").In("CUF").Show(null, "Erlan", "run")
                        .Act("Ерлан ранен в бедро — артериальное кровотечение", "Ерлан санынан жараланды — артериялық қан кету")
                        .Act("Подойди к нему, пригнувшись", "Еңкейіп, оған жақында", "reach"),
                    new LessonStep("tq_lock", "Жгут «высоко и туго»", "Жгут «жоғары әрі қатты»", "h_tq_cuf").In("CUF").Show("CAT", "ThighHigh", "tq")
                        .Act("Аптечка на поясе: потяни красный язычок", "Белдегі дәрі қобдишасы: қызыл тілшені тарт", "ifak_open")
                        .Act("Достань CAT, петлю — высоко на бедро", "CAT ал, ілмекті санның жоғарысына сал", "tq_high")
                        .Act("Затяни ленту, крути вороток, защёлкни", "Таспаны тарт, бұра, бекіт", "tq_twist")
                        .Watch("tq_below_wound", "tq_on_joint", "tq_wrong_limb"),
                    new LessonStep("drag_cover", "В укрытие", "Баспанаға", "h_drag").In("CUF").Show(null, "DragHandle", "drag")
                        .Act("Хватай за стропу разгрузки (можно двумя руками)", "Разгрузка бауынан ұста (екі қолмен де болады)", "drag_grab")
                        .Act("Тащи головой вперёд в руины", "Басымен алға қирандыға сүйре")
                        .Watch("drag_released"),
                    new LessonStep("tq_time", "Время на жгуте", "Жгут уақыты", "h_time").Show("Marker", "TqLabel", "marker")
                        .Act("Возьми маркер, зажми курок", "Маркерді ал, шүріппені бас", "marker_grab")
                        .Act("Запиши время на белой ленте жгута", "Жгуттың ақ таспасына уақытты жаз"),
                    new LessonStep("packed2", "Тампонада раны", "Жараны тампонадалау", "t_pack_2").Show("Gauze", "Wound2", "gauze")
                        .Act("Вторая рана — левое бедро. Возьми гемостатик", "Екінші жара — сол сан. Гемостатикті ал", "gauze_grab")
                        .Act("Пальцами вталкивай бинт в рану до заполнения", "Саусақпен бинтті жараға толғанша ит"),
                    new LessonStep("seal3", "Окклюзионная наклейка", "Окклюзиялық жапсырма", "t_chest_1").Show("ChestSeal", "Wound3", "seal")
                        .Act("Ранение груди: вскрой пакет, сними подложку", "Кеуде жарасы: пакетті аш, қабатын алып таста", "seal_open")
                        .Act("Ладонью на рану, прижми края по кругу", "Алақанмен жараға, шеттерін басып шық")
                        .Watch("wrong_tool"),
                    new LessonStep("blanket", "Гипотермия", "Гипотермия", "h_blanket").Show("Blanket", "Body", "blanket")
                        .Act("Достань спасательное одеяло, разверни двумя руками", "Құтқару көрпесін ал, екі қолмен жай")
                        .Act("Укрой Ерлана целиком", "Ерланды толық жап"),
                    new LessonStep("card", "Карточка раненого", "Жаралы карточкасы", "h_card").Show("Card", "Body", "card")
                        .Act("Заполни карточку маркером", "Карточканы маркермен толтыр")
                        .Act("Положи её Ерлану на грудь", "Оны Ерланның кеудесіне қой"),
                    new LessonStep("medevac", "Вызов эвакуации", "Эвакуацияны шақыру", "h_radio").In("TACEVAC").Show("Radio", null, "radio")
                        .Act("Рация на левом плече — нажми кнопку пальцем", "Сол иықтағы рация — түймені саусақпен бас"),
                    new LessonStep("loaded", "Погрузка в вертолёт", "Тікұшаққа тиеу", "h_load").In("TACEVAC").Show(null, "Stretcher", "heli")
                        .Act("Дождись посадки вертолёта", "Тікұшақтың қонуын күт", "heli_landed")
                        .Act("Нажми «ПОГРУЗИТЬ» у носилок", "Зембіл жанындағы «ТИЕУ» түймесін бас"),
                },
            },
            // ------------------------------------------------------------------ full TCCC run in the open
            new TrainingLesson
            {
                id = "full", titleRu = "Полный цикл TCCC", titleKz = "Толық TCCC циклі", woundType = WoundType.ArterialLimb, region = BodyRegion.ThighR,
                inTheOpen = true, secondWound = true, secondType = WoundType.Venous, secondRegion = BodyRegion.ForearmL,
                steps =
                {
                    new LessonStep("reach", "К раненому", "Жаралыға", "h_reach").In("CUF").Show("Smoke", "Erlan", "run")
                        .Act("Дым — между Ерланом и противником", "Түтін — Ерлан мен жаудың арасына")
                        .Act("Пригнись и беги к Ерлану", "Еңкейіп, Ерланға жүгір", "reach"),
                    new LessonStep("tq_lock", "Жгут «высоко и туго»", "Жгут «жоғары әрі қатты»", "h_tq_cuf").In("CUF").Show("CAT", "ThighHigh", "tq")
                        .Act("Достань CAT из подсумка", "Подсумкадан CAT ал", "tq_grab")
                        .Act("Петля высоко на бедро, у паха", "Ілмек санның жоғарысына", "tq_high")
                        .Act("Затяни ленту, крути вороток, зафиксируй", "Таспаны тарт, бұра, бекіт", "tq_twist")
                        .Watch("tq_below_wound", "tq_on_joint", "tq_wrong_limb", "fire_non_tq"),
                    new LessonStep("drag_cover", "В укрытие", "Баспанаға", "h_drag").In("CUF").Show(null, "DragHandle", "drag")
                        .Act("Возьмись за стропу на разгрузке", "Разгрузкадағы бауды ұста", "drag_grab")
                        .Act("Тащи головой вперёд в руины", "Басымен алға қирандыға сүйре")
                        .Watch("drag_released"),
                    new LessonStep("gloves", "Перчатки", "Қолғап", "h_gloves").Show("Gloves", null, "gloves")
                        .Act("В укрытии: надень перчатки из аптечки", "Баспанада: қолғап ки"),
                    new LessonStep("pulse_foot", "M: проверь жгут", "M: жгутты тексер", "h_pulse").Show(null, "Foot", "pulse")
                        .Act("Кровь из раны больше не идёт?", "Жарадан қан тоқтады ма?")
                        .Act("Пульса на стопе нет — жгут работает", "Табанда пульс жоқ — жгут жұмыс істейді"),
                    new LessonStep("tq_time", "Время на жгуте", "Жгут уақыты", "h_time").Show("Marker", "TqLabel", "marker")
                        .Act("Возьми маркер", "Маркерді ал", "marker_grab")
                        .Act("Запиши время на ленте жгута", "Жгут таспасына уақытты жаз"),
                    new LessonStep("sweep", "Осмотр «прочёсом»", "Толық тексеру", "h_sweep").Show(null, "Body", "sweep")
                        .Act("Проведи ладонями по телу сверху вниз", "Алақанмен денені жоғарыдан төмен сыла")
                        .Act("Найди вторую рану (предплечье)", "Екінші жараны тап (білек)")
                        .Watch("march_order"),
                    new LessonStep("bandage2", "Давящая повязка", "Қысатын таңғыш", "h_bandage").Show("Bandage", "Wound2", "bandage")
                        .Act("Израильский бинт: подушка на рану", "Израиль бинті: жастықша жараға", "bandage2_on")
                        .Act("Оберни, проведи через планку, закрепи", "Орап, планкадан өткізіп, бекіт")
                        .Watch("wrong_tool", "tq_unneeded"),
                    new LessonStep("blanket", "Гипотермия", "Гипотермия", "h_blanket").Show("Blanket", "Body", "blanket")
                        .Act("Достань спасательное одеяло", "Құтқару көрпесін ал")
                        .Act("Укрой Ерлана целиком", "Ерланды толық жап")
                        .Watch("blanket_early"),
                    new LessonStep("card", "Карточка раненого", "Жаралы карточкасы", "h_card").Show("Card", "Body", "card")
                        .Act("Возьми карточку TCCC", "TCCC карточкасын ал")
                        .Act("Заполни маркером и закрепи на Ерлане", "Маркермен толтырып, Ерланға бекіт"),
                    new LessonStep("medevac", "Вызов эвакуации", "Эвакуацияны шақыру", "h_radio").In("TACEVAC").Show("Radio", null, "radio")
                        .Act("Рация на левом плече — нажимай кнопку", "Сол иықтағы рация — түймені бас")
                        .Act("4 строки: место, раненый, ранения, площадка", "4 жол: орын, жаралы, жаралар, алаң"),
                    new LessonStep("loaded", "Погрузка в вертолёт", "Тікұшаққа тиеу", "h_load").In("TACEVAC").Show(null, "Stretcher", "heli")
                        .Act("Дождись посадки вертолёта на площадке", "Тікұшақтың қонуын күт", "heli_landed")
                        .Act("Нажми «ПОГРУЗИТЬ» у носилок", "Зембіл жанындағы «ТИЕУ» түймесін бас"),
                },
            },
            // ------------------------------------------------------------------ tourniquet only
            new TrainingLesson
            {
                id = "tq", titleRu = "Жгут (CAT)", titleKz = "Жгут (CAT)", woundType = WoundType.ArterialLimb, region = BodyRegion.ThighR,
                steps =
                {
                    new LessonStep("tq_grab", "Возьми турникет", "Турникетті ал", "t_tq_1").Show("CAT", null, "tq")
                        .Act("Открой подсумок аптечки", "Аптечка подсумкасын аш")
                        .Act("Возьми CAT за корпус", "CAT-ты корпусынан ұста"),
                    new LessonStep("tq_high", "Наложи высоко", "Жоғары сал", "t_tq_2").Show("CAT", "ThighHigh", "tq")
                        .Act("Петлю — через стопу на ногу", "Ілмекті табан арқылы аяққа", "tq_on_limb")
                        .Act("Выше раны, высоко у паха, не на колено", "Жарадан жоғары, тізеге емес")
                        .Watch("tq_below_wound", "tq_on_joint", "tq_wrong_limb"),
                    new LessonStep("tq_twist", "Затяни", "Тарт", "t_tq_3").Show("CAT", "ThighHigh", "tq")
                        .Act("Вытяни ленту до упора", "Таспаны соңына дейін тарт", "tq_strap")
                        .Act("Крути вороток, пока кровь не остановится", "Қан тоқтағанша бұрағышты бұра")
                        .Watch("tq_below_wound", "tq_on_joint"),
                    new LessonStep("tq_lock", "Зафиксируй", "Бекіт", "t_tq_5").Show("CAT", "ThighHigh", "tq")
                        .Act("Вороток — в фиксатор", "Бұрағыш — бекіткішке")
                        .Act("Закрой ленту времени", "Уақыт таспасын жап"),
                    new LessonStep("tq_time", "Время", "Уақыт", "t_tq_6").Show("Marker", "TqLabel", "marker")
                        .Act("Возьми маркер", "Маркерді ал", "marker_grab")
                        .Act("Запиши время на ленте", "Таспаға уақытты жаз"),
                    new LessonStep("pulse_foot", "Проверь пульс", "Пульсті тексер", "h_pulse").Show(null, "Foot", "pulse")
                        .Act("Два пальца на тыл стопы", "Екі саусақ табан үстіне")
                        .Act("Пульса нет — жгут работает", "Пульс жоқ — жгут жұмыс істейді"),
                },
            },
            // ------------------------------------------------------------------ packing + pressure
            new TrainingLesson
            {
                id = "pack", titleRu = "Тампонада и давление", titleKz = "Тампонада және басу", woundType = WoundType.Junctional, region = BodyRegion.GroinR,
                steps =
                {
                    new LessonStep("gloves", "Перчатки", "Қолғап", "t_pack_1").Show("Gloves", null, "gloves")
                        .Act("Надень перчатки из аптечки", "Аптечкадан қолғап ки"),
                    new LessonStep("exposed", "Открой рану", "Жараны аш", "h_expose").Show("Shears", "Wound", "shears")
                        .Act("Возьми ножницы", "Қайшыны ал")
                        .Act("Разрежь штанину над раной", "Жараның үстінен шалбарды кес"),
                    new LessonStep("packed", "Тампонада", "Тампонада", "t_pack_2").Show("Gauze", "Wound", "gauze")
                        .Act("Возьми гемостатический бинт", "Гемостатикалық бинтті ал")
                        .Act("Пальцами плотно до дна раны", "Саусақпен жараның түбіне дейін тығыз")
                        .Watch("not_exposed", "tq_junctional", "wrong_tool"),
                    new LessonStep("held", "Прямое давление", "Тікелей басу", "t_pack_3").Show(null, "Wound", "press")
                        .Act("Дави обеими руками на рану", "Екі қолмен жараны бас")
                        .Act("3 минуты (в обучении — быстрее)", "3 минут (оқытуда — тезірек)")
                        .Watch("pressure_released"),
                    new LessonStep("bandage", "Давящая повязка", "Қысатын таңғыш", "t_pack_4").Show("Bandage", "Wound", "bandage")
                        .Act("Израильский бинт: подушка на рану", "Израиль бинті: жастықша жараға")
                        .Act("Оберни, через планку, закрепи", "Орап, планкадан өткізіп, бекіт"),
                },
            },
            // ------------------------------------------------------------------ drag
            new TrainingLesson
            {
                id = "drag", titleRu = "Волок в укрытие", titleKz = "Баспанаға сүйреу", woundType = WoundType.Venous, region = BodyRegion.ForearmL, visual = "Fragment",
                inTheOpen = true,
                steps =
                {
                    new LessonStep("reach", "К раненому", "Жаралыға", "h_reach").In("CUF").Show("Smoke", "Erlan", "run")
                        .Act("Дым — между вами и противником", "Түтін — сен мен жаудың арасына")
                        .Act("Пригнись и беги к Ерлану", "Еңкейіп, Ерланға жүгір", "reach"),
                    new LessonStep("drag_grab", "Возьмись за стропу", "Бауды ұста", "t_drag_1").In("CUF").Show(null, "DragHandle", "drag")
                        .Act("Петля эвакуационной стропы — на разгрузке у шеи", "Эвакуациялық бау — разгрузкада, мойын тұсында")
                        .Act("Сожми кулак (хват)", "Жұдырықты қыс (ұстау)"),
                    new LessonStep("drag_cover", "Тащи в руины", "Қирандыға сүйре", "t_drag_2").In("CUF").Show(null, "Cover", "drag")
                        .Act("Головой вперёд, спиной к укрытию", "Басымен алға, арқаңмен баспанаға")
                        .Act("Не вставай в рост", "Тік тұрма")
                        .Watch("drag_released"),
                    new LessonStep("rolled", "Перекат «бревном»", "«Бөрене» аунату", "t_drag_3").Show(null, "Body", "roll")
                        .Act("Рука на плечо, вторая на таз", "Бір қол иыққа, екіншісі жамбасқа")
                        .Act("Переверни на бок, к себе", "Өзіңе қарай бүйіріне аунат"),
                    new LessonStep("sweep", "Осмотр «прочёсом»", "Толық тексеру", "h_sweep").Show(null, "Body", "sweep")
                        .Act("Ладонями сверху вниз, спину тоже", "Алақанмен жоғарыдан төмен, арқасын да")
                        .Act("Найди рану", "Жараны тап"),
                },
            },
            // ------------------------------------------------------------------ chest seal + needle
            new TrainingLesson
            {
                id = "chest", titleRu = "Наклейка и игла", titleKz = "Жапсырма және ине", woundType = WoundType.PenetratingChest, region = BodyRegion.ChestR, visual = "ChestEntry",
                steps =
                {
                    new LessonStep("seal_front", "Окклюзионная наклейка", "Окклюзиялық жапсырма", "t_chest_1").Show("ChestSeal", "Wound", "seal")
                        .Act("Ладонь на рану, вытри кровь", "Алақанмен жараны жап, қанды сүрт")
                        .Act("Наклейка клапаном на рану, на выдохе", "Жапсырманы дем шығарғанда жапсыр")
                        .Watch("seal_no_wipe", "wrong_tool"),
                    new LessonStep("rolled", "Осмотри спину", "Арқасын қара", "t_chest_3").Show(null, "Body", "roll")
                        .Act("Перекат «бревном» на бок", "«Бөрене» аунату")
                        .Act("Ищи выходное отверстие", "Шығу тесігін ізде"),
                    new LessonStep("seal_back", "Заклей выходное", "Шығу тесігін жап", "h_seal_back").Show("ChestSeal", "ChestBack", "seal")
                        .Act("Вторая наклейка на спину", "Екінші жапсырма арқаға"),
                    new LessonStep("tension", "Следи за дыханием", "Тынысты бақыла", "t_chest_4").Show(null, "Chest", "lungs")
                        .Act("Растёт одышка, падает SpO2?", "Ентігу күшейді ме, SpO2 төмендеді ме?")
                        .Act("Это напряжённый пневмоторакс", "Бұл — кернеулі пневмоторакс"),
                    new LessonStep("needle", "Игольная декомпрессия", "Инемен декомпрессия", "h_needle").Show("Needle", "Chest", "needle")
                        .Act("5-е межреберье, передняя подмышечная линия", "5-қабырға аралығы, алдыңғы қолтық сызығы")
                        .Act("Перпендикулярно, над верхним краем ребра", "Перпендикуляр, қабырғаның үстіңгі жиегімен")
                        .Watch("needle_wrong_site", "needle_through_seal", "needle_angle", "needle_no_signs"),
                },
            },
            // ------------------------------------------------------------------ airway
            new TrainingLesson
            {
                id = "npa", titleRu = "Дыхательные пути / НФВ", titleKz = "Тыныс жолы / НФВ", woundType = WoundType.Capillary, region = BodyRegion.Head, visual = "Fragment", unconscious = true,
                steps =
                {
                    new LessonStep("npa_grab", "Возьми воздуховод", "Түтікті ал", "t_npa_1").Show("Npa", null, "npa")
                        .Act("Назофарингеальный воздуховод из аптечки", "Аптечкадан мұрын түтігі"),
                    new LessonStep("npa_in", "Введи НФВ", "НФВ енгіз", "t_npa_2").Show("Npa", "Nose", "npa")
                        .Act("Срез к перегородке", "Кесігі қалқаға")
                        .Act("Вводи перпендикулярно лицу, «к земле»", "Бетке перпендикуляр, «жерге қарай»")
                        .Watch("npa_technique"),
                    new LessonStep("recovery", "Боковое положение", "Бүйірге жатқызу", "t_npa_3").Show(null, "Body", "roll")
                        .Act("Переверни на бок", "Бүйіріне аунат")
                        .Act("Голова чуть запрокинута, рот вниз", "Басы сәл шалқайған, аузы төмен")
                        .Watch("left_supine"),
                },
            },
            // ------------------------------------------------------------------ w8: hands-on assessment
            new TrainingLesson
            {
                id = "assess", titleRu = "Осмотр: пульс, дыхание, сознание", titleKz = "Тексеру: пульс, тыныс, сана",
                woundType = WoundType.Capillary, region = BodyRegion.ForearmL, visual = "Fragment", startLossFraction = 0.27f,
                steps =
                {
                    new LessonStep("avpu", "Сознание (AVPU)", "Сана (AVPU)", "h_avpu").Show(null, "Head", "voice")
                        .Act("Наклонись к голове и громко позови: «Ерлан, слышишь меня?»", "Басына еңкейіп қатты шақыр: «Ерлан, естисің бе?»", "avpu_voice")
                        .Act("Не отвечает — сожми и потряси его за плечо", "Жауап бермесе — иығынан қысып сілкіле")
                        .Act("A — говорит, V — на голос, P — на боль, U — нет", "A — сөйлейді, V — дауысқа, P — ауырсынуға, U — жоқ"),
                    new LessonStep("pulse_neck", "Пульс на шее", "Мойын пульсі", "h_pulse_neck").Show(null, "Neck", "pulse")
                        .Act("Два пальца — указательный и средний — сбоку от кадыка", "Екі саусақ — сұқ және ортаңғы — алқымның жанына")
                        .Act("Держи 2 секунды, считай удары в вибрации", "2 секунд ұста, дірілдегі соққыларды сана"),
                    new LessonStep("pulse_wrist", "Пульс на запястье", "Білек пульсі", "h_pulse_wrist").Show(null, "Wrist", "pulse")
                        .Act("Два пальца на запястье со стороны большого пальца", "Екі саусақ білекке, бас бармақ жағынан")
                        .Act("Нет пульса на запястье — давление ниже 80: шок", "Білекте пульс жоқ — қысым 80-нен төмен: шок"),
                    new LessonStep("breath", "Дыхание", "Тыныс", "h_breath").Show(null, "Mouth", "lungs")
                        .Act("Наклонись ухом к его рту (ближе 25 см)", "Құлағыңды аузына жақында (25 см-ден жақын)")
                        .Act("Слушай и смотри на грудь 3 секунды", "3 секунд тыңда және кеудесіне қара")
                        .Act("Частое и поверхностное — подозревай пневмоторакс", "Жиі әрі үстірт — пневмотораксқа күдіктен"),
                    new LessonStep("cap_refill", "Капиллярное наполнение", "Капиллярлық толу", "h_refill").Show(null, "Nail", "press")
                        .Act("Нажми на ноготь его пальца и держи 1 секунду", "Саусағының тырнағын басып, 1 секунд ұста")
                        .Act("Отпусти: цвет вернулся дольше 2 с — плохая перфузия", "Жібер: түсі 2 с-тан ұзақ қайтса — перфузия нашар"),
                },
            },
        };

        static List<HintWrong> BuildWrongs() => new()
        {
            new("tq_below_wound", "Жгут ниже раны — кровотечение не остановится. Переложи выше раны.", "Жгут жарадан төмен — қан тоқтамайды. Жарадан жоғары сал."),
            new("tq_on_joint", "Жгут на колене/локте не пережимает артерию. Сдвинь выше сустава.", "Жгут буында артерияны қыспайды. Буыннан жоғары жылжыт."),
            new("tq_wrong_limb", "Жгут не на раненой конечности.", "Жгут жараланған аяқ-қолда емес."),
            new("fire_non_tq", "Под огнём — только жгут «высоко и туго». Остальное — в укрытии.", "Оқ астында — тек жгут. Қалғаны — баспанада."),
            new("drag_released", "Стропа отпущена на открытом месте. Тащи до укрытия без остановок.", "Ашық жерде бау жіберілді. Баспанаға дейін тоқтамай сүйре."),
            new("march_order", "Порядок MARCH: сначала массивное кровотечение, потом остальное.", "MARCH реті: алдымен қатты қан кету, сосын қалғаны."),
            new("wrong_tool", "Это средство не подходит для такой раны.", "Бұл құрал мұндай жараға келмейді."),
            new("tq_unneeded", "На венозную рану предплечья жгут не нужен — давящая повязка.", "Білектің веналық жарасына жгут керек емес — қысатын таңғыш."),
            new("blanket_early", "Одеяло — после остановки кровотечения.", "Көрпе — қан тоқтағаннан кейін."),
            new("not_exposed", "Сначала разрежь одежду: тампон — в рану, а не в ткань.", "Алдымен киімді кес: тампон — жараға, матаға емес."),
            new("tq_junctional", "Пах — узловая зона, жгут невозможен. Тампонада и давление.", "Шап — түйін аймағы, жгут салынбайды. Тампонада және басу."),
            new("pressure_released", "Давление отпущено рано — дави до конца отсчёта.", "Басу ерте тоқтады — уақыт біткенше бас."),
            new("seal_no_wipe", "Вытри кровь вокруг раны — иначе наклейка не держится.", "Жараның айналасындағы қанды сүрт — әйтпесе жапсырма тұрмайды."),
            new("needle_wrong_site", "Не та точка: 5-е межреберье по передней подмышечной линии.", "Дұрыс емес нүкте: 5-қабырға аралығы, алдыңғы қолтық сызығы."),
            new("needle_through_seal", "Нельзя колоть через наклейку.", "Жапсырма арқылы шаншуға болмайды."),
            new("needle_angle", "Игла — перпендикулярно грудной стенке.", "Ине — кеуде қабырғасына перпендикуляр."),
            new("needle_no_signs", "Нет признаков напряжённого пневмоторакса — игла не нужна.", "Кернеулі пневмоторакс белгісі жоқ — ине керек емес."),
            new("npa_technique", "НФВ: срез к перегородке, вводить «к земле», не вверх.", "НФВ: кесігі қалқаға, «жерге қарай» енгіз."),
            new("left_supine", "Без сознания на спине — язык перекроет дыхание. На бок!", "Есі жоқ, шалқасынан — тілі тынысты жабады. Бүйіріне!"),
            new("tq_slow", "Жгут под огнём наложен позже 60 секунд.", "Оқ астында жгут 60 секундтан кеш салынды."),
            new("tq_not_marked", "Не записано время наложения жгута.", "Жгут салынған уақыт жазылмады."),
            new("missed_wound", "Пропущена рана — нужен полный осмотр.", "Жара байқалмады — толық тексеру керек."),
            new("hypothermia", "Раненый переохлаждён — не укрыт одеялом.", "Жаралы тоңып қалды — көрпемен жабылмады."),
            new("no_medevac", "Эвакуация не запрошена по рации.", "Эвакуация рациямен сұралмады."),
            new("player_exposed", "В полный рост под огнём — медик становится второй целью.", "Оқ астында тік тұру — медик екінші нысанаға айналады."),
            new("no_gloves", "Работа без перчаток.", "Қолғапсыз жұмыс."),
            new("tq_loose", "Жгут затянут слабо — докрути вороток.", "Жгут әлсіз тартылған — бұрағышты бұра."),
            new("big_blood_loss", "Потеряно больше 30% крови — действуй быстрее.", "Қанның 30%-дан астамы жоғалды — тезірек әрекет ет."),
            new("tq_not_rechecked", "После волока жгут не перепроверен.", "Сүйреуден кейін жгут қайта тексерілмеді."),
        };
    }
}
