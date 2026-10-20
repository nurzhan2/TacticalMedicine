using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldMedic.Medical
{
    /// <summary>w10b: one coached step of a scenario (what the player must do next with the hands).</summary>
    public sealed class CoachStep
    {
        /// <summary>Stable id — shares the lesson vocabulary (tq_lock, drag_cover, gloves, pulse_foot, tq_time, packed,
        /// held, bandage, seal_front, needle, npa_in, sweep, rolled, blanket, card, medevac, loaded…) so the same
        /// runtime "do it for me" code serves lessons and scenarios.</summary>
        public string id;
        public string ru, kz;
        /// <summary>IFAK item key (lesson vocabulary: CAT, Gauze, Bandage, ChestSeal, Needle, Npa, Gloves, Shears, Marker,
        /// Blanket, Card, Radio) or null.</summary>
        public string item;
        /// <summary>Body/world target key (lesson vocabulary: ThighHigh, Wound, Foot, TqLabel, DragHandle, Chest, Nose, Body,
        /// HeliDoor) or null.</summary>
        public string target;
        /// <summary>The wound the step is about (null = none).</summary>
        public Wound wound;
        /// <summary>False for "wait" steps (nothing to do by hand → nothing to skip).</summary>
        public bool skippable = true;
        public string Text(bool kz) => kz && !string.IsNullOrEmpty(this.kz) ? this.kz : ru;
        public override string ToString() => id + ": " + ru;
    }

    /// <summary>Where the TACEVAC is (runtime facts the protocol does not know).</summary>
    public enum CoachEvac { None, Waiting, Landed, Aboard }

    /// <summary>
    /// w10b (owner: "сделай подсказки даже в сценарии, и только в экзамене без подсказок"): the next MARCH step for a
    /// scenario casualty, derived only from the protocol state (what the debrief would grade next). Engine-agnostic so it
    /// is covered by EditMode tests; the scenario director shows it on the step line and glows the item.
    /// </summary>
    public static class ScenarioCoach
    {
        public static CoachStep Next(CareProtocol proto, CoachEvac evac = CoachEvac.None, bool tacevac = true)
        {
            if (proto == null) return null;
            var p = proto.patient;
            if (p.outcome != Outcome.Ongoing) return null;

            // ---------------------------------------------------------------- care under fire
            if (proto.phase == Phase.CareUnderFire)
            {
                var limb = p.wounds.FirstOrDefault(w => w.IsLifeThreatening && w.IsLimb && !w.IsJunctionalRegion && !w.IsControlled);
                if (limb != null)
                {
                    var tq = BandOn(p, limb.LimbId);
                    if (tq == null)
                        return S("tq_lock", "Жгут «высоко и туго»: X — аптечка, жгут высоко на " + HighRu(limb) + ", затяни вороток",
                            "Жгут «жоғары әрі қатты»: X — қобдиша, жгутты " + HighKz(limb) + " жоғары сал, бұрауышты бұра", "CAT", "ThighHigh", limb);
                    return S("tq_twist", "Докрути вороток, пока кровь не остановится, и защёлкни его", "Қан тоқтағанша бұрауышты бұра да, бекіт", null, "ThighHigh", limb);
                }
                bool junction = p.wounds.Any(w => w.IsLifeThreatening && w.IsJunctionalRegion && !w.IsControlled);
                return S("drag_cover",
                    (junction ? "Пах жгутом не пережать — " : "") + "тащи Ерлана в укрытие: GRIP за ручку на спине, стик назад",
                    (junction ? "Шапқа жгут салынбайды — " : "") + "Ерланды баспанаға сүйре: арқадағы тұтқадан GRIP, стикті артқа",
                    null, "DragHandle", null);
            }

            // ---------------------------------------------------------------- tactical field care (MARCH)
            if (!proto.glovesOn)
                return S("gloves", "Надень перчатки: X — аптечка, возьми перчатки", "Қолғап киі: X — қобдиша, қолғапты ал", "Gloves", null, null);

            // M — massive hemorrhage (the worst first)
            foreach (var w in p.wounds.Where(w => w.IsLifeThreatening && Visible(w) && !w.IsControlled && !w.IsChest)
                                      .OrderBy(w => w.IsJunctionalRegion ? 1 : 0))
            {
                if (w.IsLimb && !w.IsJunctionalRegion && w.control is not (BleedControl.Packed or BleedControl.PackedAndHeld))
                {
                    var tq = BandOn(p, w.LimbId);
                    if (tq == null)
                        return S("tq_high", "Наложи жгут высоко на " + HighRu(w) + " и затяни вороток",
                            "Жгутты " + HighKz(w) + " жоғары салып, бұрауышты бұра", "CAT", "ThighHigh", w);
                    return S("tq_twist", "Жгут не держит: докрути вороток и защёлкни", "Жгут ұстамайды: бұрауышты бұрап, бекіт", null, "ThighHigh", w);
                }
                var step = PackSteps(w);
                if (step != null) return step;
            }
            // a held packing still needs its dressing even when it barely bleeds
            foreach (var w in p.wounds.Where(w => w.control == BleedControl.PackedAndHeld && w.IsLifeThreatening))
                return S("bandage", "Закрепи тампонаду давящей повязкой (" + RegionRu(w.region) + ")",
                    "Тампонаданы қысатын таңғышпен бекіт (" + RegionKz(w.region) + ")", "Bandage", "Wound", w);

            // tourniquet: pulse below the band, then the time on the strap
            foreach (var tq in p.tourniquets.Where(t => t.Locked && t.limbId != null))
            {
                if (!PulseCheckedBelow(proto, tq))
                {
                    bool leg = LimbGeometry.IsLeg(tq.limbId);
                    return S("pulse_foot",
                        leg ? "Проверь жгут: два пальца на подъём стопы — пульса быть не должно" : "Проверь жгут: два пальца на запястье — пульса быть не должно",
                        leg ? "Жгутты тексер: екі саусақ табанның үстіне — тамыр соқпауы керек" : "Жгутты тексер: екі саусақ білезікке — тамыр соқпауы керек",
                        null, "Foot", null);
                }
            }
            if (p.tourniquets.Any(t => t.Locked) && !proto.tourniquetTimeMarked)
                return S("tq_time", "Запиши время на жгуте: X — маркер, коснись ленты TIME", "Жгутқа уақытты жаз: X — маркер, TIME таспасына тигіз", "Marker", "TqLabel", null);

            // full exam: hidden wounds
            if (p.wounds.Any(w => !w.discovered && !w.posterior) || (p.wounds.Any(w => !w.discovered) && !Logged(proto, "sweep", "sweep_front")))
                return S("sweep", "Осмотри Ерлана руками с головы до ног: проведи ладонью по телу", "Ерланды бастан аяққа қолмен тексер: алақанмен денесін сипа", null, "Body", null);
            if (p.wounds.Any(w => !w.discovered && w.posterior))
                return S("rolled", "Переверни Ерлана на бок (GRIP за плечо и бедро) и осмотри спину", "Ерланды бүйіріне аунат (иық пен саннан GRIP), арқасын қара", null, "Body", null);

            // A — airway
            if (p.vomitInMouth)
                return S("mouth", "Рвота во рту: поверни голову набок и очисти рот пальцами", "Аузында құсық: басын бұрып, аузын саусақпен тазала", null, "Nose", null);
            if (p.airwayObstructed && !p.airwayAdjunct && !p.recoveryPosition)
                return S("npa_in", "Дыхательные пути: X — воздуховод (НФВ), введи в ноздрю перпендикулярно лицу",
                    "Тыныс жолы: X — ауа өткізгіш (НФВ), танауға бетке перпендикуляр енгіз", "Npa", "Nose", null);

            // R — respiration
            if (p.pneumo == PneumoState.Tension)
                return S("needle", "Напряжённый пневмоторакс: игла во 2-е межреберье по среднеключичной линии",
                    "Кернеулі пневмоторакс: инені 2-ші қабырғааралыққа, бұғананың ортаңғы сызығы бойынша", "Needle", "Chest", null);
            foreach (var w in p.wounds.Where(w => w.IsChest && Visible(w) && w.control != BleedControl.OcclusiveSeal))
            {
                if (w.hiddenUnderClothing && !w.exposed)
                    return S("exposed", "Разрежь одежду над раной груди: X — ножницы", "Кеуде жарасының үстіндегі киімді қи: X — қайшы", "Shears", "Wound", w);
                return S(w.posterior ? "seal_back" : "seal_front",
                    w.posterior ? "Наклейка на спину: окклюзионная наклейка на выходное отверстие" : "Окклюзионная наклейка на рану груди — на выдохе",
                    w.posterior ? "Арқаға жапсырма: шығу тесігіне окклюзиялық жапсырма" : "Кеуде жарасына окклюзиялық жапсырма — дем шығарғанда",
                    "ChestSeal", w.posterior ? "ChestBack" : "Wound", w);
            }

            // C — the remaining bleeding (venous / capillary)
            foreach (var w in p.wounds.Where(w => !w.IsLifeThreatening && !w.IsChest && Visible(w) && !w.IsControlled))
            {
                if (w.hiddenUnderClothing && !w.exposed)
                    return S("exposed", "Разрежь одежду над раной (" + RegionRu(w.region) + "): X — ножницы",
                        "Жараның үстіндегі киімді қи (" + RegionKz(w.region) + "): X — қайшы", "Shears", "Wound", w);
                return S("bandage2", "Наложи давящую повязку на рану (" + RegionRu(w.region) + ")",
                    "Жараға қысатын таңғыш сал (" + RegionKz(w.region) + ")", "Bandage", "Wound", w);
            }

            // unconscious: on the side
            if (p.tbiUnconscious && !p.recoveryPosition)
                return S("recovery", "Без сознания: уложи в устойчивое боковое положение (GRIP за плечо и бедро, переверни)",
                    "Есі жоқ: тұрақты бүйір қалыпқа жатқыз (иық пен саннан GRIP, аунат)", null, "Body", null);

            // H — hypothermia, the card
            if (!p.insulated)
                return S("blanket", "Укрой термоодеялом: X — одеяло, накинь на Ерлана", "Термокөрпемен жап: X — көрпе, Ерланға жап", "Blanket", "Body", null);
            if (!Logged(proto, "card"))
                return S("card", "Заполни карточку раненого и прикрепи на грудь", "Жаралы карточкасын толтырып, кеудесіне бекіт", "Card", "Body", null);

            // TACEVAC
            if (!proto.medevacRequested)
                return S("medevac", "Доложи по рации: зажми Y и вызови эвакуацию", "Рациямен баянда: Y ұстап, эвакуацияны шақыр", "Radio", null, null);
            switch (evac)
            {
                case CoachEvac.Landed:
                    return S("loaded", "Вертолёт сел: дотащи Ерлана к двери (GRIP за ручку, стик назад)", "Тікұшақ қонды: Ерланды есікке сүйре (тұтқадан GRIP, стикті артқа)", null, "HeliDoor", null);
                case CoachEvac.Aboard:
                    return S("board", "Ерлан на борту — подойди к двери и садись в вертолёт", "Ерлан бортта — есікке барып, тікұшаққа отыр", null, "HeliDoor", null);
            }
            var wait = S("wait", tacevac ? "Ждём вертолёт: держись рядом, следи за жгутом и дыханием" : "Следи за Ерланом: жгут, дыхание, пульс",
                tacevac ? "Тікұшақты күтеміз: қасында бол, жгут пен тынысты бақыла" : "Ерланды бақыла: жгут, тыныс, тамыр", null, null, null);
            wait.skippable = false;
            return wait;
        }

        static CoachStep PackSteps(Wound w)
        {
            string ru = RegionRu(w.region), kz = RegionKz(w.region);
            if (w.hiddenUnderClothing && !w.exposed)
                return S("exposed", "Разрежь одежду над раной (" + ru + "): X — ножницы", "Жараның үстіндегі киімді қи (" + kz + "): X — қайшы", "Shears", "Wound", w);
            if (!w.packed)
                return S("packed", "Затампонируй рану (" + ru + "): X — гемостатик, набивай пальцами в рану", "Жараны тампонада (" + kz + "): X — гемостатик, саусақпен жараға толтыр", "Gauze", "Wound", w);
            if (w.control == BleedControl.Packed)
                return S("held", "Дави на рану обеими руками. " + HoldTimes.LineRu, "Жараны екі қолмен бас. " + HoldTimes.LineKz, null, "Wound", w);
            if (w.control is BleedControl.PackedAndHeld or BleedControl.None or BleedControl.DirectPressure)
                return S("bandage", "Закрепи давящей повязкой (" + ru + ")", "Қысатын таңғышпен бекіт (" + kz + ")", "Bandage", "Wound", w);
            return null;
        }

        static CoachStep S(string id, string ru, string kz, string item, string target, Wound w) =>
            new CoachStep { id = id, ru = ru, kz = kz, item = item, target = target, wound = w };

        static bool Visible(Wound w) => w.discovered || !w.hiddenUnderClothing;

        static TourniquetState BandOn(PatientPhysiology p, string limbId) =>
            p.tourniquets.FirstOrDefault(t => t.OnLimb && t.limbId == limbId);

        static bool Logged(CareProtocol proto, params string[] codes) => proto.log.Any(e => codes.Contains(e.code));

        /// <summary>A pulse check distal to the band after it went on (pedal for a leg, radial for an arm).</summary>
        public static bool PulseCheckedBelow(CareProtocol proto, TourniquetState tq)
        {
            if (tq == null || tq.limbId == null) return true;
            bool leg = LimbGeometry.IsLeg(tq.limbId);
            bool left = tq.limbId.EndsWith("L");
            string site = leg ? (left ? "FootL" : "FootR") : (left ? "ForearmL" : "ForearmR");
            float since = Math.Max(0f, tq.appliedAtSeconds);
            return proto.log.Any(e => e.code == "pulse" && e.target == site && e.time >= since - 0.01f);
        }

        static string HighRu(Wound w) => LimbGeometry.IsLeg(w.LimbId) ? "бедро" : "плечо";
        static string HighKz(Wound w) => LimbGeometry.IsLeg(w.LimbId) ? "санға" : "иыққа";

        public static string RegionRu(BodyRegion r) => r switch
        {
            BodyRegion.GroinL or BodyRegion.GroinR => "пах",
            BodyRegion.AxillaL or BodyRegion.AxillaR => "подмышка",
            BodyRegion.Neck => "шея",
            BodyRegion.ThighL or BodyRegion.ThighR => "бедро",
            BodyRegion.ShinL or BodyRegion.ShinR => "голень",
            BodyRegion.FootL or BodyRegion.FootR => "стопа",
            BodyRegion.UpperArmL or BodyRegion.UpperArmR => "плечо",
            BodyRegion.ForearmL or BodyRegion.ForearmR => "предплечье",
            BodyRegion.HandL or BodyRegion.HandR => "кисть",
            BodyRegion.ChestL or BodyRegion.ChestR => "грудь",
            BodyRegion.Abdomen => "живот",
            BodyRegion.Pelvis => "таз",
            _ => "голова",
        };

        public static string RegionKz(BodyRegion r) => r switch
        {
            BodyRegion.GroinL or BodyRegion.GroinR => "шап",
            BodyRegion.AxillaL or BodyRegion.AxillaR => "қолтық",
            BodyRegion.Neck => "мойын",
            BodyRegion.ThighL or BodyRegion.ThighR => "сан",
            BodyRegion.ShinL or BodyRegion.ShinR => "сирақ",
            BodyRegion.FootL or BodyRegion.FootR => "табан",
            BodyRegion.UpperArmL or BodyRegion.UpperArmR => "иық",
            BodyRegion.ForearmL or BodyRegion.ForearmR => "білек",
            BodyRegion.HandL or BodyRegion.HandR => "қол",
            BodyRegion.ChestL or BodyRegion.ChestR => "кеуде",
            BodyRegion.Abdomen => "іш",
            BodyRegion.Pelvis => "жамбас",
            _ => "бас",
        };
    }

    /// <summary>
    /// w10b (owner: "сделай таймер 3 минуты меньше — типа 10 секунд, но напиши, что в бою надо 3"): every long real-time
    /// hold runs ~10 s outside the exam, with a line telling the real time. The exam keeps real times.
    /// </summary>
    public static class HoldTimes
    {
        /// <summary>Real pressure hold after packing (s).</summary>
        public const float CombatSeconds = Wound.PackingHoldRequired;
        /// <summary>The same hold in lessons / scenarios (s).</summary>
        public const float ShortSeconds = 10f;
        /// <summary>Hold time scale outside the exam (180 s → 10 s).</summary>
        public const float TrainingScale = CombatSeconds / ShortSeconds;
        public const string LineRu = "В бою держать 3 минуты. Здесь — 10 секунд.";
        public const string LineKz = "Ұрыста 3 минут ұстау керек. Мұнда — 10 секунд.";
        public static string Line(bool kz) => kz ? LineKz : LineRu;
        /// <summary>The hold scale for a run: real time in the exam, 18x everywhere else.</summary>
        public static float ScaleFor(bool exam) => exam ? 1f : TrainingScale;
    }

    /// <summary>w10b: the TACEVAC line every scenario briefing ends with (item 11).</summary>
    public static class TacevacBrief
    {
        public const string Ru = "В конце — эвакуация вертолётом: вызови по рации (зажми Y) и дотащи Ерлана к вертолёту.";
        public const string Kz = "Соңында — тікұшақпен эвакуация: рациямен шақыр (Y ұста) да, Ерланды тікұшаққа сүйре.";
        public static string Line(bool kz) => kz ? Kz : Ru;
    }
}
