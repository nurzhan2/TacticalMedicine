using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldMedic.Medical
{
    /// One step of a lesson; `check` is evaluated by the Unity layer (it knows the hands and items).
    [Serializable]
    public sealed class LessonStep
    {
        public string id;       // also the check key in the runtime
        public string ru, kz;   // checklist text
        public string voice;    // instructor line id (Resources/Radio/<voice>_<lang>)
        public LessonStep(string id, string ru, string kz, string voice = null) { this.id = id; this.ru = ru; this.kz = kz; this.voice = voice; }
        public string Text(bool k) => k ? kz : ru;
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
        public List<LessonStep> steps = new();
        public string Title(bool kz) => kz ? titleKz : titleRu;
    }

    /// <summary>Progress through a lesson's checklist (pure; the runtime feeds it the satisfied step ids).</summary>
    public sealed class LessonProgress
    {
        public readonly TrainingLesson lesson;
        public int Current { get; private set; }
        public readonly List<float> doneAt = new();
        public bool Complete => Current >= lesson.steps.Count;
        public LessonStep Step => Complete ? null : lesson.steps[Current];
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
    }

    public static class TrainingCatalog
    {
        static List<TrainingLesson> _all;
        public static IReadOnlyList<TrainingLesson> All => _all ??= Build();
        public static TrainingLesson Get(string id) => All.FirstOrDefault(l => l.id == id);

        static List<TrainingLesson> Build() => new()
        {
            new TrainingLesson
            {
                id = "tq", titleRu = "Жгут (CAT)", titleKz = "Жгут (CAT)", woundType = WoundType.ArterialLimb, region = BodyRegion.ThighR,
                steps =
                {
                    new("tq_grab", "Возьми турникет (CAT) из подсумка", "Подсумкадан CAT турникетін ал", "t_tq_1"),
                    new("tq_on_limb", "Петлю — через стопу на ногу", "Ілмекті табан арқылы аяққа", "t_tq_2"),
                    new("tq_high", "Высоко на бедро, выше раны (не на колено)", "Санның жоғарысына, жарадан жоғары", null),
                    new("tq_strap", "Выбери слабину: потяни ленту", "Таспаны тартып, бос жерін жоқ қыл", "t_tq_3"),
                    new("tq_twist", "Крути вороток до остановки кровотечения", "Қан тоқтағанша бұрағышты бұра", "t_tq_4"),
                    new("tq_lock", "Вороток в фиксатор, лента времени", "Бұрағыш бекіткіште, уақыт таспасы", "t_tq_5"),
                    new("tq_time", "Запиши время маркером", "Маркермен уақытты жаз", "t_tq_6"),
                    new("pulse_foot", "Проверь пульс на стопе (его нет)", "Табандағы пульсті тексер", null),
                },
            },
            new TrainingLesson
            {
                id = "pack", titleRu = "Тампонада и давление", titleKz = "Тампонада және басу", woundType = WoundType.Junctional, region = BodyRegion.GroinR,
                steps =
                {
                    new("gloves", "Надень перчатки", "Қолғап ки", "t_pack_1"),
                    new("exposed", "Разрежь штанину ножницами", "Қайшымен шалбарды кес", null),
                    new("packed", "Затампонируй рану до дна", "Жараны түбіне дейін тығында", "t_pack_2"),
                    new("held", "Дави 3 минуты (в обучении — быстрее)", "3 минут бас (оқытуда — тезірек)", "t_pack_3"),
                    new("bandage", "Израильский бинт с прижимной планкой", "Израиль бинті", "t_pack_4"),
                },
            },
            new TrainingLesson
            {
                id = "chest", titleRu = "Наклейка и игла", titleKz = "Жапсырма және ине", woundType = WoundType.PenetratingChest, region = BodyRegion.ChestR, visual = "ChestEntry",
                steps =
                {
                    new("seal_front", "Окклюзионная наклейка на рану на выдохе", "Жараға окклюзиялық жапсырма", "t_chest_1"),
                    new("rolled", "Переверни: осмотри спину", "Аунатып, арқасын қара", "t_chest_3"),
                    new("seal_back", "Заклей выходное отверстие", "Шығу тесігін жап", null),
                    new("tension", "Дождись признаков напряжённого пневмоторакса", "Кернеулі пневмоторакс белгілерін күт", "t_chest_4"),
                    new("needle", "Игла: 5 м/р по передней подмышечной линии", "Ине: 5-қабырға аралығы", null),
                },
            },
            new TrainingLesson
            {
                id = "drag", titleRu = "Волок и перекат", titleKz = "Сүйреу және аунату", woundType = WoundType.Venous, region = BodyRegion.ForearmL, visual = "Fragment",
                steps =
                {
                    new("drag_grab", "Возьмись за эвакуационную стропу", "Эвакуациялық бауды ұста", "t_drag_1"),
                    new("drag_cover", "Тащи в блиндаж, голова ниже бруствера", "Блиндажға сүйре", "t_drag_2"),
                    new("rolled", "Перекат «бревном» на бок", "Бүйіріне аунат", "t_drag_3"),
                    new("sweep", "Осмотр «прочёсом»: найди рану", "Толық тексеріп, жараны тап", null),
                },
            },
            new TrainingLesson
            {
                id = "npa", titleRu = "Дыхательные пути / НФВ", titleKz = "Тыныс жолы / НФВ", woundType = WoundType.Capillary, region = BodyRegion.Head, visual = "Fragment", unconscious = true,
                steps =
                {
                    new("npa_grab", "Возьми назофарингеальный воздуховод", "Мұрын түтігін ал", "t_npa_1"),
                    new("npa_in", "Срез к перегородке, вводи «к земле»", "Кесігі қалқаға, жерге қарай енгіз", "t_npa_2"),
                    new("recovery", "Устойчивое боковое положение", "Бүйірге жатқызу", "t_npa_3"),
                },
            },
        };
    }
}
