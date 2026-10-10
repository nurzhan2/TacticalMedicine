using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldMedic.Medical
{
    /// Run phases of a scenario (GDD §3/§4). Debrief is terminal.
    public enum ScenarioState { Briefing, Incident, CareUnderFire, TacticalFieldCare, Evacuation, Debrief }

    /// What hurts Erlan (drives the scripted incident: sounds, VFX, fall animation).
    public enum IncidentKind { DroneDrop, SniperShot, Mine, Mortar, Fpv, GlideBomb, Barrage, None }

    /// Where the casualty is when the incident happens (resolved to world positions by the Unity layer).
    public enum CasualtySpot { FireBay, CommTrench, FarBay, Dugout }

    public enum ComplicationKind
    {
        SecondDrop,     // the drone comes back and drops again near the position
        SecondFpv,      // another FPV in the sky: get under cover
        Vomit,          // TBI casualty vomits
        TqSlip,         // the band slipped during the drag (S7): rebleed until re-tightened
        Barrage,        // artillery barrage: impacts walk along the trench
        CasualtyCrawl,  // panic: he tries to crawl away (S3)
        RadioLine,      // a radio call (text id in `arg`)
        Wake,           // S8: he dozes off; keep him awake
    }

    [Serializable]
    public sealed class WoundSpec
    {
        public string id;
        public WoundType type;
        public BodyRegion region;
        public string visual = "Fragment";   // Fragment | Ragged | ChestEntry | Exit | Stump
        public bool posterior;
        public bool exitSide;
        public float onsetDelay;
        public bool hidden = true;

        public WoundSpec Clone() => (WoundSpec)MemberwiseClone();

        public Wound ToWound() => new Wound
        {
            id = id, type = type, region = region, posterior = posterior, onsetDelaySeconds = onsetDelay,
            hiddenUnderClothing = hidden,
        };
    }

    [Serializable]
    public sealed class ComplicationSpec
    {
        public ComplicationKind kind;
        public float at;          // seconds after the incident
        public string arg;
        public bool optional;     // only with variant.secondHit
        public ComplicationSpec(ComplicationKind k, float t, string a = null, bool opt = false) { kind = k; at = t; arg = a; optional = opt; }
    }

    [Serializable]
    public sealed class TourniquetSpec
    {
        public string limb = "legR";
        public float position = 0.15f;
        public float minutesAgo = 5f;
        public bool timeMarked = true;
        public int halfTurns = 4;
    }

    /// <summary>One training scenario (GDD §4, RESEARCH §7). Pure data; the Unity ScenarioDirector runs it.</summary>
    [Serializable]
    public sealed class ScenarioDefinition
    {
        public string id;               // "S1"
        public int number;
        public string titleRu, titleKz;
        public string taglineRu, taglineKz;
        public string briefingRu, briefingKz;
        public IncidentKind incident;
        public HitKind hit = HitKind.Explosion;
        public CasualtySpot spot = CasualtySpot.FireBay;
        public List<WoundSpec> wounds = new();
        public Phase startPhase = Phase.CareUnderFire;
        public float incidentDelay = 12f;      // seconds of "quiet" after the briefing
        public float evacMinutes = 40f;
        public TourniquetSpec preTourniquet;
        public bool tbi, eardrums;
        public List<ComplicationSpec> complications = new();
        public string[] skillsRu = Array.Empty<string>();
        public string[] skillsKz = Array.Empty<string>();
        public int[] timeSkipMinutes = Array.Empty<int>();
        public int clockHour = 1, clockMinute = 40;
        public bool polished;                  // showcase scenarios (S1, S2)
        /// Seconds after the incident by which a tourniquet must be on under fire (0 = no rule).
        public float tqDeadline;

        public string Title(bool kz) => kz ? titleKz : titleRu;
        public string Tagline(bool kz) => kz ? taglineKz : taglineRu;
        public string Briefing(bool kz) => kz ? briefingKz : briefingRu;
    }

    /// Random-but-reproducible variation so Exam runs are not memorisable.
    [Serializable]
    public struct ScenarioVariant
    {
        public bool mirrored;      // wounds on the other side
        public bool secondHit;     // second drone drop / second FPV / ...
        public bool delayedOnset;  // a wound starts bleeding later (clot lets go)
        public int seed;

        public static ScenarioVariant Default => new ScenarioVariant();

        public static ScenarioVariant Random(int seed)
        {
            var r = new System.Random(seed);
            return new ScenarioVariant { mirrored = r.NextDouble() < 0.5, secondHit = r.NextDouble() < 0.5, delayedOnset = r.NextDouble() < 0.35, seed = seed };
        }

        public override string ToString() => $"{(mirrored ? "L" : "R")}{(secondHit ? "+2nd" : "")}{(delayedOnset ? "+delay" : "")}";
    }

    /// A definition with a variant applied: concrete wounds and the complication schedule.
    public sealed class ResolvedScenario
    {
        public ScenarioDefinition def;
        public ScenarioVariant variant;
        public List<WoundSpec> wounds;
        public List<ComplicationSpec> complications;
        public TourniquetSpec preTourniquet;
    }

    public static class ScenarioCatalog
    {
        static List<ScenarioDefinition> _all;
        public static IReadOnlyList<ScenarioDefinition> All => _all ??= Build();

        public static ScenarioDefinition Get(string id) => All.FirstOrDefault(d => d.id == id);

        public static BodyRegion Mirror(BodyRegion r)
        {
            string n = r.ToString();
            if (n.EndsWith("L")) return (BodyRegion)Enum.Parse(typeof(BodyRegion), n.Substring(0, n.Length - 1) + "R");
            if (n.EndsWith("R")) return (BodyRegion)Enum.Parse(typeof(BodyRegion), n.Substring(0, n.Length - 1) + "L");
            return r;
        }

        static string MirrorLimb(string limb) => limb == null ? null : limb.EndsWith("L") ? limb[..^1] + "R" : limb[..^1] + "L";

        public static ResolvedScenario Resolve(ScenarioDefinition d, ScenarioVariant v)
        {
            var wounds = d.wounds.Select(w =>
            {
                var c = w.Clone();
                if (v.mirrored) { c.region = Mirror(c.region); c.id = c.id + "_m"; }
                return c;
            }).ToList();
            if (v.delayedOnset)
            {
                // the most minor bleeding wound "opens up" later: forces a re-check
                var minor = wounds.Where(w => w.type is WoundType.Venous or WoundType.Capillary).FirstOrDefault();
                if (minor != null && minor.onsetDelay <= 0f) minor.onsetDelay = 240f;
            }
            var comps = d.complications.Where(c => !c.optional || v.secondHit).OrderBy(c => c.at).ToList();
            TourniquetSpec tq = null;
            if (d.preTourniquet != null)
            {
                tq = new TourniquetSpec
                {
                    limb = v.mirrored ? MirrorLimb(d.preTourniquet.limb) : d.preTourniquet.limb,
                    position = d.preTourniquet.position, minutesAgo = d.preTourniquet.minutesAgo,
                    timeMarked = d.preTourniquet.timeMarked, halfTurns = d.preTourniquet.halfTurns,
                };
            }
            return new ResolvedScenario { def = d, variant = v, wounds = wounds, complications = comps, preTourniquet = tq };
        }

        static WoundSpec W(string id, WoundType t, BodyRegion r, string visual, bool posterior = false, float onset = 0f, bool exit = false) =>
            new WoundSpec { id = id, type = t, region = r, visual = visual, posterior = posterior, onsetDelay = onset, exitSide = exit };

        static List<ScenarioDefinition> Build() => new()
        {
            new ScenarioDefinition
            {
                id = "S1", number = 1, polished = true,
                titleRu = "«Сброс»", titleKz = "«Тастау»",
                taglineRu = "Дрон сбросил ВОГ: артериальное кровотечение бедра", taglineKz = "Дрон ВОГ тастады: санның артериялық қан кетуі",
                briefingRu = "01:40. Окоп первой линии. Ерлан наблюдает за полем. Над позицией гудит разведывательный дрон-«сбросник». Будь готов: жгут — высоко и туго, затем в блиндаж.",
                briefingKz = "01:40. Бірінші шептегі окоп. Ерлан далаға қарап отыр. Үстімізде барлау дроны ызыңдайды. Дайын бол: жгут — жоғары әрі қатты, сосын блиндажға.",
                incident = IncidentKind.DroneDrop, hit = HitKind.Explosion, spot = CasualtySpot.FireBay,
                wounds = { W("thigh_artery", WoundType.ArterialLimb, BodyRegion.ThighR, "Ragged"), W("forearm_frag", WoundType.Capillary, BodyRegion.ForearmL, "Fragment") },
                complications = { new(ComplicationKind.SecondDrop, 75f, null, true), new(ComplicationKind.RadioLine, 20f, "air") },
                skillsRu = new[] { "Жгут «высоко и туго» под огнём ≤ 60 с", "Волок за эвакуационную стропу в укрытие", "Пульс на стопе, время на жгуте", "Осмотр «прочёсом»: рана предплечья" },
                skillsKz = new[] { "Оқ астында жгут ≤ 60 с", "Баспанаға сүйреу", "Табандағы пульс, жгут уақыты", "Толық тексеру: білектегі жара" },
                incidentDelay = 12f, evacMinutes = 40f, tqDeadline = 60f,
            },
            new ScenarioDefinition
            {
                id = "S2", number = 2, polished = true,
                titleRu = "«Снайпер»", titleKz = "«Мерген»",
                taglineRu = "Проникающее ранение груди, выходное отверстие на спине", taglineKz = "Кеуденің өтпелі жарасы, шығу тесігі арқада",
                briefingRu = "03:10. Ерлан поднимает голову над бруствером. Снайпер работает по позиции. Ранение груди — ладонь на рану, наклейка на выдохе, осмотри спину.",
                briefingKz = "03:10. Ерлан бруствердің үстіне басын көтерді. Мерген атып жатыр. Кеуде жарасы — алақанмен бас, демді шығарғанда жапсырма, арқасын тексер.",
                incident = IncidentKind.SniperShot, hit = HitKind.Shot, spot = CasualtySpot.FireBay,
                wounds = { W("chest_entry", WoundType.PenetratingChest, BodyRegion.ChestR, "ChestEntry"), W("chest_exit", WoundType.PenetratingChest, BodyRegion.ChestR, "Exit", posterior: true, exit: true) },
                complications = { new(ComplicationKind.RadioLine, 30f, "sniper") },
                skillsRu = new[] { "Окклюзионная наклейка с клапаном на выдохе", "Перекат «бревном» — выходное отверстие", "Напряжённый пневмоторакс: «отрыжка» наклейки, игла 5 м/р ППЛ", "Полусидячее положение" },
                skillsKz = new[] { "Клапанды жапсырма", "Аунату — шығу тесігі", "Кернеулі пневмоторакс: ине", "Жартылай отыру" },
                incidentDelay = 10f, evacMinutes = 30f, clockHour = 3, clockMinute = 10,
            },
            new ScenarioDefinition
            {
                id = "S3", number = 3,
                titleRu = "«Мина»", titleKz = "«Мина»",
                taglineRu = "Отрыв стопы в ходе сообщения", taglineKz = "Қатынас жолында табанның үзілуі",
                briefingRu = "02:20. Ход сообщения. Ерлан идёт впереди. Глухой хлопок — противопехотная мина. Иди только по своим следам. Жгут высоко на бедро, второй — если кровит.",
                briefingKz = "02:20. Қатынас жолы. Ерлан алда келе жатыр. Мина жарылды. Тек өз ізіңмен жүр. Жгут санның жоғарысына, қан тоқтамаса — екіншісі.",
                incident = IncidentKind.Mine, hit = HitKind.Explosion, spot = CasualtySpot.CommTrench, eardrums = true,
                wounds = { W("foot_amputation", WoundType.TraumaticAmputation, BodyRegion.FootR, "Stump", onset: 45f), W("shin_frag", WoundType.Venous, BodyRegion.ShinL, "Fragment"), W("groin_frag", WoundType.Capillary, BodyRegion.GroinL, "Fragment") },
                complications = { new(ComplicationKind.CasualtyCrawl, 25f), new(ComplicationKind.RadioLine, 15f, "mine") },
                skillsRu = new[] { "Только по своим следам", "Жгут высоко на бедро над культёй", "Кровотечение после спазма сосудов — второй жгут", "Осмотр паха и второй ноги" },
                skillsKz = new[] { "Тек өз ізіңмен", "Жгут санның жоғарысына", "Екінші жгут", "Шап пен екінші аяқты тексеру" },
                incidentDelay = 10f, evacMinutes = 40f, clockHour = 2, clockMinute = 20, tqDeadline = 90f,
            },
            new ScenarioDefinition
            {
                id = "S4", number = 4,
                titleRu = "«Миномёт»", titleKz = "«Миномёт»",
                taglineRu = "Узловое кровотечение из паха", taglineKz = "Шаптан қан кету",
                briefingRu = "04:05. Свист мины. Разрыв на бруствере. Рана в паху — жгут невозможен: колено/кулак, тампонада пальцами, 3 минуты давления, израильский бинт.",
                briefingKz = "04:05. Мина ысқырды. Бруствердегі жарылыс. Шаптағы жара — жгут салынбайды: тізе/жұдырық, тампонада, 3 минут басу, израиль бинті.",
                incident = IncidentKind.Mortar, hit = HitKind.Explosion, spot = CasualtySpot.FireBay,
                wounds = { W("groin_junctional", WoundType.Junctional, BodyRegion.GroinR, "Ragged") },
                complications = { new(ComplicationKind.RadioLine, 25f, "air"), new(ComplicationKind.SecondDrop, 90f, "mortar", true) },
                skillsRu = new[] { "Прямое давление коленом/кулаком", "Тампонада раны гемостатиком", "3 минуты давления без проверок", "Израильский бинт «восьмёркой»" },
                skillsKz = new[] { "Тіземен/жұдырықпен басу", "Гемостатикпен тампонада", "3 минут басу", "Израиль бинті" },
                incidentDelay = 10f, evacMinutes = 40f, clockHour = 4, clockMinute = 5,
            },
            new ScenarioDefinition
            {
                id = "S5", number = 5,
                titleRu = "«FPV»", titleKz = "«FPV»",
                taglineRu = "Множественные осколки, скрытая рана под бронежилетом", taglineKz = "Көп жарықшақ, бронежилет астындағы жасырын жара",
                briefingRu = "00:50. Нарастающий вой FPV. Разрыв у входа в окоп. Видимые раны — голень, плечо, лицо. Не верь глазам: полный осмотр, перекат, спина.",
                briefingKz = "00:50. FPV ызыңы күшейеді. Окоп кіреберісінде жарылыс. Көрінетін жаралар — сирақ, иық, бет. Толық тексер, аунат, арқасын қара.",
                incident = IncidentKind.Fpv, hit = HitKind.Explosion, spot = CasualtySpot.FireBay,
                wounds = { W("shin_frag", WoundType.Venous, BodyRegion.ShinL, "Fragment"), W("arm_frag", WoundType.Capillary, BodyRegion.UpperArmR, "Fragment"),
                           W("face_frag", WoundType.Capillary, BodyRegion.Head, "Fragment"), W("buttock_hidden", WoundType.Venous, BodyRegion.Pelvis, "Ragged", posterior: true) },
                complications = { new(ComplicationKind.SecondFpv, 70f, null, true), new(ComplicationKind.RadioLine, 20f, "fpv") },
                skillsRu = new[] { "Не отвлекаться на видимые мелкие раны", "Полный осмотр «прочёсом»", "Перекат «бревном»: ягодица/спина", "Тампонада скрытой раны" },
                skillsKz = new[] { "Ұсақ жараларға алаңдама", "Толық тексеру", "Аунату: арқа", "Жасырын жараны тампонада" },
                incidentDelay = 10f, evacMinutes = 40f, clockHour = 0, clockMinute = 50,
            },
            new ScenarioDefinition
            {
                id = "S6", number = 6,
                titleRu = "«КАБ»", titleKz = "«КАБ»",
                taglineRu = "Взрывная травма: без сознания, храп, рвота", taglineKz = "Жарылыс жарақаты: есінен танған, қорылдау, құсу",
                briefingRu = "05:20. Управляемая авиабомба в 60 метрах. Ерлана отбросило. Без сознания, кровь из ушей. Дыхательные пути: рот, НФВ, устойчивое боковое положение.",
                briefingKz = "05:20. 60 метр жерде басқарылатын авиабомба. Ерлан лақтырылды. Есі жоқ, құлағынан қан. Тыныс жолдары: ауыз, НФВ, бүйірге жатқызу.",
                incident = IncidentKind.GlideBomb, hit = HitKind.KnockedOut, spot = CasualtySpot.FireBay, tbi = true, eardrums = true,
                wounds = { W("scalp", WoundType.Capillary, BodyRegion.Head, "Fragment") },
                complications = { new(ComplicationKind.Vomit, 75f), new(ComplicationKind.Vomit, 200f, null, true), new(ComplicationKind.RadioLine, 15f, "kab") },
                skillsRu = new[] { "Проверка дыхания: храп = западение языка", "НФВ: срез к перегородке, «к земле»", "Устойчивое боковое положение", "Рвота: повернуть, очистить рот" },
                skillsKz = new[] { "Тынысты тексеру: қорылдау", "НФВ енгізу", "Бүйірге жатқызу", "Құсық: аузын тазалау" },
                incidentDelay = 10f, evacMinutes = 30f, clockHour = 5, clockMinute = 20, startPhase = Phase.CareUnderFire,
            },
            new ScenarioDefinition
            {
                id = "S7", number = 7,
                titleRu = "«Под огнём»", titleKz = "«Оқ астында»",
                taglineRu = "Эвакуация раненого с жгутом под артобстрелом", taglineKz = "Атқылау кезінде жгутпен жараланғанды шығару",
                briefingRu = "01:15. Жгут на бедре Ерлана уже наложен. Начинается обстрел. Тащи его в блиндаж рывками между разрывами, не поднимаясь. В укрытии — перепроверь жгут.",
                briefingKz = "01:15. Ерланның санында жгут бар. Атқылау басталды. Жарылыстар арасында блиндажға сүйре, басыңды көтерме. Баспанада жгутты қайта тексер.",
                incident = IncidentKind.Barrage, hit = HitKind.Explosion, spot = CasualtySpot.FarBay,
                wounds = { W("thigh_artery", WoundType.ArterialLimb, BodyRegion.ThighR, "Ragged") },
                preTourniquet = new TourniquetSpec { limb = "legR", position = 0.15f, minutesAgo = 4f, halfTurns = 4, timeMarked = true },
                complications = { new(ComplicationKind.Barrage, 0f), new(ComplicationKind.Barrage, 20f), new(ComplicationKind.Barrage, 45f), new(ComplicationKind.RadioLine, 3f, "barrage"), new(ComplicationKind.TqSlip, -1f) },
                skillsRu = new[] { "Ниже бруствера, рывками между разрывами", "Волок за стропу", "Жгут сполз: перепроверить пульс на стопе", "Докрутить вороток" },
                skillsKz = new[] { "Бруствердан төмен", "Сүйреу", "Жгут сырғыды: пульсті тексер", "Бұрағышты бұра" },
                incidentDelay = 6f, evacMinutes = 30f, clockHour = 1, clockMinute = 15,
            },
            new ScenarioDefinition
            {
                id = "S8", number = 8,
                titleRu = "«До рассвета»", titleKz = "«Таң атқанша»",
                taglineRu = "Продлённая помощь: переоценка и конверсия жгута", taglineKz = "Ұзақ көмек: жгутты қайта бағалау және ауыстыру",
                briefingRu = "22:25. Дорога под дронами — эвакуации до рассвета не будет. Жгут наложен в 22:10 на рану голени. Переоцени: если нет шока — тампон и давящая повязка, ослабь жгут до 2 часов.",
                briefingKz = "22:25. Жол дрондардың астында — таңға дейін эвакуация жоқ. Жгут 22:10-да салынған. Шок болмаса — тампон, қысатын таңғыш, 2 сағатқа дейін жгутты босат.",
                incident = IncidentKind.None, hit = HitKind.Explosion, spot = CasualtySpot.Dugout, startPhase = Phase.TacticalFieldCare,
                wounds = { W("shin_venous", WoundType.Venous, BodyRegion.ShinR, "Ragged") },
                preTourniquet = new TourniquetSpec { limb = "legR", position = 0.2f, minutesAgo = 15f, halfTurns = 4, timeMarked = true },
                complications = { new(ComplicationKind.RadioLine, 5f, "no_evac"), new(ComplicationKind.Wake, 60f) },
                timeSkipMinutes = new[] { 30, 60, 120, 240 },
                skillsRu = new[] { "Переоценка жгута ≤ 2 ч", "Условия конверсии: нет шока, нет ампутации, < 6 ч", "Тампон + давящая повязка, затем ослабить жгут", "Тепло, не давать спать, записи времени" },
                skillsKz = new[] { "Жгутты 2 сағатқа дейін бағалау", "Ауыстыру шарттары", "Таңғыш, сосын жгутты босату", "Жылу, ұйықтатпау" },
                incidentDelay = 2f, evacMinutes = 360f, clockHour = 22, clockMinute = 25,
            },
        };
    }

    /// <summary>
    /// Pure run state machine: Briefing → Incident → CareUnderFire → TacticalFieldCare → Evacuation → Debrief.
    /// Fires complications on schedule (seconds after the incident). The Unity director feeds it the protocol phase,
    /// the outcome and the MEDEVAC request, and executes whatever it returns.
    /// </summary>
    public sealed class ScenarioTimeline
    {
        public readonly ResolvedScenario run;
        public ScenarioState State { get; private set; } = ScenarioState.Briefing;
        public float Clock { get; private set; }              // seconds since Begin
        public float SinceIncident => _incidentAt < 0f ? 0f : Clock - _incidentAt;
        public float StateTime { get; private set; }
        public float EvacRemaining { get; private set; } = -1f;
        public string EndReason { get; private set; }
        public bool AutoEvac { get; private set; }
        /// Seconds of tactical field care after which the commander orders evacuation anyway.
        public float autoEvacAfter = 480f;
        /// Evacuation wait passes this many times faster than real time (the player can still act).
        public float evacTimeScale = 20f;

        public event Action<ScenarioState> StateChanged;
        readonly List<ComplicationSpec> _pending;
        readonly List<ComplicationSpec> _deferred = new(); // fired on an event (at < 0)
        float _incidentAt = -1f;

        public ScenarioTimeline(ResolvedScenario r)
        {
            run = r;
            _pending = r.complications.Where(c => c.at >= 0f).OrderBy(c => c.at).ToList();
            _deferred.AddRange(r.complications.Where(c => c.at < 0f));
        }

        public void Set(ScenarioState s)
        {
            if (State == s || State == ScenarioState.Debrief) return;
            State = s;
            StateTime = 0f;
            StateChanged?.Invoke(s);
        }

        /// The incident happened (blast/shot): care starts.
        public void Incident()
        {
            if (_incidentAt >= 0f) return;
            _incidentAt = Clock;
            Set(ScenarioState.Incident);
        }

        /// He is down: care under fire (or straight to field care for S8-like starts).
        public void CasualtyDown(Phase startPhase) =>
            Set(startPhase == Phase.CareUnderFire ? ScenarioState.CareUnderFire : ScenarioState.TacticalFieldCare);

        public void End(string reason)
        {
            if (State == ScenarioState.Debrief) return;
            EndReason = reason;
            Set(ScenarioState.Debrief);
        }

        /// Deferred complications (e.g. TQ slip on arrival in cover). Returns them once.
        public List<ComplicationSpec> TakeDeferred(ComplicationKind kind)
        {
            var list = _deferred.Where(c => c.kind == kind).ToList();
            foreach (var c in list) _deferred.Remove(c);
            return list;
        }

        /// <param name="evacScaledDt">game seconds of evacuation wait elapsed this frame (already scaled)</param>
        public List<ComplicationSpec> Tick(float dt, Phase protocolPhase, bool medevacRequested, Outcome outcome, bool playerHit, float evacScaledDt = -1f)
        {
            var due = new List<ComplicationSpec>();
            if (State == ScenarioState.Debrief) return due;
            Clock += dt;
            StateTime += dt;

            if (playerHit) { End("player_hit"); return due; }
            if (outcome == Outcome.Died) { End("died"); return due; }

            switch (State)
            {
                case ScenarioState.CareUnderFire:
                    if (protocolPhase != Phase.CareUnderFire) Set(ScenarioState.TacticalFieldCare);
                    break;
                case ScenarioState.TacticalFieldCare:
                    if (medevacRequested || StateTime >= autoEvacAfter)
                    {
                        AutoEvac = !medevacRequested;
                        EvacRemaining = run.def.evacMinutes * 60f;
                        Set(ScenarioState.Evacuation);
                    }
                    break;
                case ScenarioState.Evacuation:
                    EvacRemaining -= evacScaledDt >= 0f ? evacScaledDt : dt * evacTimeScale;
                    if (EvacRemaining <= 0f) { EvacRemaining = 0f; End("evacuated"); return due; }
                    break;
            }

            if (_incidentAt >= 0f)
                while (_pending.Count > 0 && _pending[0].at <= SinceIncident)
                {
                    due.Add(_pending[0]);
                    _pending.RemoveAt(0);
                }
            return due;
        }
    }
}
