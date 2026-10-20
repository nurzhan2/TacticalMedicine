using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldMedic.Medical
{
    /// Which limb the player is hit in (w9b self-aid drill).
    public enum SelfAidLimb { Arm, Thigh }

    /// <summary>What the runtime should play/show this frame (lines, drone, effects). Returned by <see cref="SelfAidRun.Tick"/>.</summary>
    public enum SelfAidCue
    {
        DroneWarning,     // the drone turns back: "get out of the open!"
        DronePass,        // second pass: a drop/dive next to the player (danger, debrief error)
        Killed,           // second pass while still in the open: the run ends
        CoverReached,     // first time in cover: instructor "tourniquet, free hand, high and tight"
        TqLocked,         // windlass in the clip: "write the time"
        BleedingStopped,  // no spurts, the pulse in the wounded hand fades
        TimeMarked,       // "report on the radio"
        ReportSent,       // last line of the report transmitted
        Ack,              // operator: "group is moving to you"
        BlanketPrompt,    // instructor: "blanket"
        CheckIn,          // operator: "Medic, how are you? Answer."
        CheckInLoud,      // operator calls again, louder
        Answered,         // the player answered a check-in
        RescueArrive,     // "Hold on, brother!" — comrades come in
        Rescued,          // fade → debrief
        Died,             // bled out
    }

    /// <summary>Configuration of one self-aid run (random per seed, mirrored to the non-dominant hand).</summary>
    [Serializable]
    public sealed class SelfAidConfig
    {
        public SelfAidLimb limb = SelfAidLimb.Arm;
        /// The LEFT side is hit (default: right-handed players — the wounded hand is the weak one).
        public bool left = true;
        /// Arm: the wound is on the upper arm (band must sit above it) instead of the forearm.
        public bool upperArmWound;
        /// Seconds to bleed out when nothing is done (≤ 0 → arm 180 s, thigh 120 s).
        public float lethalSeconds = -1f;
        public float exposureLimit = 10f;      // seconds in the open before the drone comes back
        public float droneWarning = 6f;        // seconds in the open before the warning line
        public float leftCoverGrace = 3f;      // out of cover this long after reaching it = "left cover"
        public float tqDeadline = 60f;         // TCCC: tourniquet in the first minute
        public float ackDelay = 2f;
        public float rescueAfterReport = 75f;  // help arrives this long after the operator's answer
        public float rescueWithoutReport = 210f;
        public float noReportCall = 150f;      // nobody heard from him: the operator calls
        public float firstCheckAfterAck = 18f;
        public float checkInterval = 24f;
        public float answerWindow = 10f;
        public float blanketPromptAfterAck = 7f;
        public float arrivalSeconds = 6f;      // comrades walk in, then the fade
        /// "High and tight": the band within this many segment units of the shoulder/hip (0.35 ≈ upper third).
        public float highAndTightMax = 0.4f;
        /// Training: no drone, cannot die, slower bleeding.
        public bool training;
        public int seed;
        public int clockHour = 14, clockMinute = 5;

        public string LimbId => (limb == SelfAidLimb.Arm ? "arm" : "leg") + (left ? "L" : "R");

        public BodyRegion WoundRegion => limb == SelfAidLimb.Arm
            ? (upperArmWound ? (left ? BodyRegion.UpperArmL : BodyRegion.UpperArmR) : (left ? BodyRegion.ForearmL : BodyRegion.ForearmR))
            : (left ? BodyRegion.ThighL : BodyRegion.ThighR);

        /// Wound position on the limb (segment units: 0 shoulder/hip, 1 elbow/knee, 2 wrist/ankle).
        public float WoundPosition => limb == SelfAidLimb.Arm ? (upperArmWound ? 0.62f : 1.4f) : 0.55f;

        public float LethalSeconds => lethalSeconds > 0f ? lethalSeconds : limb == SelfAidLimb.Arm ? 180f : 120f;

        /// <summary>Random run: wound side = the non-dominant hand, forearm or upper arm, arm or thigh (when allowed).</summary>
        public static SelfAidConfig Random(int seed, bool leftHanded, bool allowThigh)
        {
            var r = new System.Random(seed);
            var c = new SelfAidConfig { seed = seed, left = !leftHanded };
            c.limb = allowThigh && r.NextDouble() < 0.5 ? SelfAidLimb.Thigh : SelfAidLimb.Arm;
            c.upperArmWound = r.NextDouble() < 0.4;
            c.clockMinute = 5 + r.Next(0, 40);
            return c;
        }
    }

    /// One line option in the radio report menu.
    [Serializable]
    public sealed class SelfAidReportOption
    {
        public string ru, kz;
        public bool correct;
        public string Text(bool k) => k ? kz : ru;
    }

    /// <summary>
    /// The player is the casualty (w9b, owner: "what to do if you are hit and nobody is around"). TCCC self-aid:
    /// Care Under Fire (get out of the line of fire → self tourniquet with the free hand, high and tight) →
    /// Tactical Field Care (check the bleeding stopped, write the time, report on the radio, rescue blanket, keep
    /// answering the radio) until the unit arrives. Pure C#: the Unity layer feeds it "in cover", drives the
    /// <see cref="tq"/> with the hands and plays the returned <see cref="SelfAidCue"/>s. Bleeding uses
    /// <see cref="PatientPhysiology"/> (one arterial limb wound), calibrated so that an untreated wound kills in
    /// <see cref="SelfAidConfig.LethalSeconds"/>.
    /// </summary>
    public sealed class SelfAidRun
    {
        public readonly SelfAidConfig cfg;
        public readonly PatientPhysiology patient = new PatientPhysiology();
        public readonly Wound wound;
        public readonly TourniquetState tq = new TourniquetState { id = "CAT_self" };

        /// Seconds since the hit (= patient clock).
        public float Clock => patient.secondsSinceStart;
        /// Blood that kills (ml): what an untreated wound has lost at the moment of death.
        public float LethalMl { get; private set; }
        /// 0..1 of the lethal amount (vision, tremor).
        public float LethalFraction => LethalMl > 0f ? Math.Min(1f, patient.bloodLostMl / LethalMl) : 0f;
        public float BleedMultiplier => patient.bleedMultiplier;
        public float FlowMlPerSec => patient.CurrentFlowMlPerSec(wound);
        public bool Bleeding => FlowMlPerSec > 0.05f;
        public bool Ended { get; private set; }
        public string EndReason { get; private set; }

        // ---- care under fire
        public bool InCover { get; private set; }
        public float CoverReachedAt { get; private set; } = -1f;
        public float OpenSeconds { get; private set; }
        public int DronePasses { get; private set; }
        public bool LeftCover { get; private set; }
        float _outOfCover;
        bool _warned;

        // ---- tourniquet
        public float TqWrappedAt { get; private set; } = -1f;
        public bool TqWrappedInOpen { get; private set; }
        public float TqLockedAt { get; private set; } = -1f;
        public float BleedStoppedAt { get; private set; } = -1f;
        public float TimeMarkedAt { get; private set; } = -1f;
        public bool HighAndTight => tq.OnLimb && tq.limbId == cfg.LimbId && tq.limbPosition <= cfg.highAndTightMax && tq.FullOcclusion;
        public bool BleedingControlled => wound.IsControlled;

        // ---- report
        public static readonly int ReportSteps = 5;
        public int ReportStep { get; private set; }
        public bool ReportSent { get; private set; }
        public bool ReportAllCorrect { get; private set; } = true;
        public float ReportSentAt { get; private set; } = -1f;
        public readonly List<string> ReportLog = new List<string>();
        public string whereRu = "руины, дом А", whereKz = "қираған үй А";
        public string wrongWhereRu = "открытое поле у дороги", wrongWhereKz = "жол жанындағы ашық дала";
        public Func<float, string> clock = s => $"{(int)(s / 60f):00}:{(int)(s % 60f):00}";
        float _ackAt = -1f, _blanketPromptAt = -1f;
        public bool Acked { get; private set; }

        // ---- after the report
        public bool Blanket { get; private set; }
        public float BlanketAt { get; private set; } = -1f;
        public bool CheckPending { get; private set; }
        public bool CheckLoud { get; private set; }
        public int ChecksAsked { get; private set; }
        public int ChecksAnswered { get; private set; }
        public int ChecksMissed { get; private set; }
        float _checkDeadline = -1f, _nextCheck = -1f;
        bool _noReportCalled;
        public float RescueAt { get; private set; } = -1f;
        public float ArrivedAt { get; private set; } = -1f;

        /// Mistake code → first time (seconds since the hit).
        public readonly Dictionary<string, float> Mistakes = new Dictionary<string, float>();
        /// Timeline of the run (debrief).
        public readonly List<DebriefEntry> Log = new List<DebriefEntry>();

        public SelfAidRun(SelfAidConfig config)
        {
            cfg = config ?? new SelfAidConfig();
            wound = new Wound
            {
                id = "self_" + cfg.LimbId, type = WoundType.ArterialLimb, region = cfg.WoundRegion,
                limbPosition = cfg.WoundPosition, hiddenUnderClothing = false, discovered = true, exposed = true,
            };
            Calibrate();
            patient.wounds.Add(wound);
            patient.AddTourniquet(tq);
        }

        /// <summary>Find the bleed multiplier that kills an untreated casualty in the configured time (bleeding
        /// slows as the blood pressure falls, so the relation is not linear: a few fixed-point iterations).</summary>
        void Calibrate()
        {
            float target = cfg.LethalSeconds;
            float m = 1f, lethalMl = 0f;
            for (int it = 0; it < 6; it++)
            {
                float t = SimulateDeath(m, out lethalMl);
                if (t <= 0f) break;
                float k = t / target;
                m *= k;
                if (Math.Abs(k - 1f) < 0.01f) break;
            }
            SimulateDeath(m, out lethalMl);
            patient.bleedMultiplier = cfg.training ? m * 0.5f : m;
            LethalMl = lethalMl;
        }

        float SimulateDeath(float multiplier, out float lostAtDeath)
        {
            var p = new PatientPhysiology { bleedMultiplier = multiplier };
            p.wounds.Add(new Wound { id = "cal", type = WoundType.ArterialLimb, region = cfg.WoundRegion, limbPosition = cfg.WoundPosition });
            const float dt = 0.25f;
            for (float t = 0f; t < 3600f; t += dt)
            {
                p.Tick(dt);
                if (p.outcome == Outcome.Died) { lostAtDeath = p.bloodLostMl; return t + dt; }
            }
            lostAtDeath = p.bloodLostMl;
            return -1f;
        }

        void Note(string ru, string kz, Severity s) =>
            Log.Add(new DebriefEntry { time = Clock, clock = clock(Clock), text = ru + "|" + kz, severity = s });

        public void Mistake(string code)
        {
            if (Mistakes.ContainsKey(code)) return;
            Mistakes[code] = Clock;
            var m = SelfAidDebrief.Info(code);
            if (m != null) Note(m.ru, m.kz, Severity.Error);
        }

        // ------------------------------------------------------------------ inputs from the runtime
        /// <summary>The loop went over the wounded hand/foot (the free hand holds the band).</summary>
        public bool Wrap(float pos)
        {
            if (!tq.WrapAround(cfg.LimbId, pos)) return false;
            TqWrappedAt = Clock;
            TqWrappedInOpen = !InCover && !cfg.training;
            if (TqWrappedInOpen) Mistake("sa_tq_in_open");
            Note("Петля жгута надета на раненую конечность", "Жгут ілмегі жаралы қолға кигізілді", Severity.Info);
            return true;
        }

        /// <summary>Report menu: options of the current line (two or three, order shuffled per seed).</summary>
        public List<SelfAidReportOption> ReportOptions()
        {
            var o = new List<SelfAidReportOption>();
            bool arm = cfg.limb == SelfAidLimb.Arm;
            string side = cfg.left ? "Левая" : "Правая", sideKz = cfg.left ? "Сол" : "Оң";
            string other = cfg.left ? "Правая" : "Левая", otherKz = cfg.left ? "Оң" : "Сол";
            switch (ReportStep)
            {
                case 0:
                    o.Add(Opt("«Я трёхсотый»", "«Мен үш жүзінші»", true));
                    o.Add(Opt("«Я двухсотый»", "«Мен екі жүзінші»", false));
                    o.Add(Opt("«Всё нормально»", "«Бәрі жақсы»", false));
                    break;
                case 1:
                    o.Add(Opt("Где: " + whereRu, "Қайда: " + whereKz, true));
                    o.Add(Opt("Где: " + wrongWhereRu, "Қайда: " + wrongWhereKz, false));
                    break;
                case 2:
                    o.Add(Opt((arm ? side + " рука" : side + " нога") + ", артериальное кровотечение",
                              (arm ? sideKz + " қол" : sideKz + " аяқ") + ", артериялық қан кету", true));
                    o.Add(Opt((arm ? other + " нога" : other + " рука") + ", царапина", (arm ? otherKz + " аяқ" : otherKz + " қол") + ", сызат", false));
                    break;
                case 3:
                {
                    bool done = tq.Locked;
                    string t = done ? clock(Math.Max(0f, tq.appliedAtSeconds)) : "--:--";
                    o.Add(Opt("Жгут наложен в " + t, "Жгут " + t + " салынды", done));
                    o.Add(Opt("Ничего не сделано", "Ештеңе істелмеді", !done));
                    break;
                }
                default:
                    o.Add(Opt("«Нужна эвакуация»", "«Эвакуация керек»", true));
                    o.Add(Opt("«Помощь не нужна»", "«Көмек керек емес»", false));
                    break;
            }
            // deterministic shuffle per step (the right answer is not always first)
            var r = new System.Random(cfg.seed * 31 + ReportStep * 7 + 3);
            return o.OrderBy(_ => r.Next()).ToList();
        }

        static SelfAidReportOption Opt(string ru, string kz, bool ok) => new SelfAidReportOption { ru = ru, kz = kz, correct = ok };

        public static string ReportStepTitle(int step, bool kz) => step switch
        {
            0 => kz ? "1/5 Кім" : "1/5 Кто",
            1 => kz ? "2/5 Қайда" : "2/5 Где",
            2 => kz ? "3/5 Не болды" : "3/5 Что",
            3 => kz ? "4/5 Не істелді" : "4/5 Что сделано",
            _ => kz ? "5/5 Сұрау" : "5/5 Запрос",
        };

        /// <summary>One line of the report transmitted. Returns true when it was the right line.</summary>
        public bool ChooseReport(SelfAidReportOption option)
        {
            if (ReportSent || option == null || Ended) return false;
            bool ok = option.correct;
            // "what is done" is only complete with the tourniquet time
            if (ReportStep == 3 && !tq.Locked) ok = false;
            if (!ok) ReportAllCorrect = false;
            ReportLog.Add(option.ru);
            ReportStep++;
            if (ReportStep >= ReportSteps)
            {
                ReportSent = true;
                ReportSentAt = Clock;
                _ackAt = Clock + cfg.ackDelay;
                Note("Доклад по рации передан" + (ReportAllCorrect ? "" : " (с ошибками)"), "Рациямен баяндалды" + (ReportAllCorrect ? "" : " (қателермен)"),
                    ReportAllCorrect ? Severity.Ok : Severity.Warning);
                if (!ReportAllCorrect) Mistake("sa_report_incomplete");
            }
            return ok;
        }

        /// <summary>Radio button while a check-in is waiting: "I hear you, holding on". False if nobody asked.</summary>
        public bool AnswerCheck()
        {
            if (!CheckPending || Ended) return false;
            CheckPending = false;
            CheckLoud = false;
            ChecksAnswered++;
            _pendingCues.Add(SelfAidCue.Answered);
            Note("Ответил на вызов по рации", "Рация шақыруына жауап берді", Severity.Ok);
            return true;
        }

        /// <summary>The rescue blanket is on (one-handed: grabbed and wrapped over the chest).</summary>
        public bool PutOnBlanket()
        {
            if (Blanket || Ended) return false;
            Blanket = true;
            BlanketAt = Clock;
            patient.ApplyBlanket();
            Note("Укрылся спасательным одеялом", "Құтқару көрпесімен жабынды", Severity.Ok);
            return true;
        }

        readonly List<SelfAidCue> _pendingCues = new List<SelfAidCue>();

        // ------------------------------------------------------------------ tick
        /// <param name="inCover">head inside a cover zone (ruins, crater, wreck)</param>
        public List<SelfAidCue> Tick(float dt, bool inCover)
        {
            var cues = new List<SelfAidCue>(_pendingCues);
            _pendingCues.Clear();
            if (Ended || dt <= 0f) return cues;

            // the hit is t = 0; physiology bleeds, the band's occlusion is read every tick
            if (cfg.training && patient.bloodLostMl > LethalMl * 0.6f) patient.bleedMultiplier = 0f; // never fails
            patient.Tick(dt);

            // ---- cover / exposure
            InCover = inCover;
            if (inCover)
            {
                if (CoverReachedAt < 0f)
                {
                    CoverReachedAt = Clock;
                    cues.Add(SelfAidCue.CoverReached);
                    Note("Ушёл с линии огня в укрытие", "Оқ сызығынан баспанаға кетті", Severity.Ok);
                }
                OpenSeconds = 0f;
                _outOfCover = 0f;
                _warned = false;
            }
            else if (!cfg.training && ArrivedAt < 0f)
            {
                OpenSeconds += dt;
                if (CoverReachedAt >= 0f)
                {
                    _outOfCover += dt;
                    if (_outOfCover > cfg.leftCoverGrace && !LeftCover) { LeftCover = true; Mistake("sa_left_cover"); }
                }
                if (!_warned && OpenSeconds >= cfg.droneWarning && DronePasses == 0) { _warned = true; cues.Add(SelfAidCue.DroneWarning); }
                if (OpenSeconds >= cfg.exposureLimit)
                {
                    OpenSeconds = 0f;
                    DronePasses++;
                    if (DronePasses == 1)
                    {
                        cues.Add(SelfAidCue.DronePass);
                        Mistake("sa_stayed_open");
                    }
                    else
                    {
                        cues.Add(SelfAidCue.Killed);
                        End("killed");
                        return cues;
                    }
                }
            }

            // ---- tourniquet progress
            if (TqLockedAt < 0f && tq.Locked)
            {
                TqLockedAt = Clock;
                cues.Add(SelfAidCue.TqLocked);
                Note("Вороток зафиксирован в клипсе (" + (int)TqLockedAt + " с после ранения)", "Бұрағыш қысқышқа бекітілді (" + (int)TqLockedAt + " с)", TqLockedAt <= cfg.tqDeadline ? Severity.Ok : Severity.Warning);
            }
            if (BleedStoppedAt < 0f && wound.IsControlled)
            {
                BleedStoppedAt = Clock;
                cues.Add(SelfAidCue.BleedingStopped);
                Note("Кровотечение остановлено", "Қан тоқтады", Severity.Ok);
            }
            if (TimeMarkedAt < 0f && tq.timeMarkedAtSeconds >= 0f)
            {
                TimeMarkedAt = Clock;
                cues.Add(SelfAidCue.TimeMarked);
                Note("Время записано на жгуте: " + clock(tq.timeMarkedAtSeconds), "Жгутқа уақыт жазылды: " + clock(tq.timeMarkedAtSeconds), Severity.Ok);
            }

            // ---- report → acknowledgement → check-ins → rescue
            if (ReportSent && !Acked && Clock >= _ackAt)
            {
                Acked = true;
                cues.Add(SelfAidCue.Ack);
                RescueAt = Clock + cfg.rescueAfterReport;
                _nextCheck = Clock + cfg.firstCheckAfterAck;
                _blanketPromptAt = Clock + cfg.blanketPromptAfterAck;
            }
            if (!ReportSent && !_noReportCalled && Clock >= cfg.noReportCall && !cfg.training)
            {
                // nobody heard from him: the unit calls (an answer is not a report)
                _noReportCalled = true;
                Ask(cues);
            }
            if (!ReportSent && RescueAt < 0f && Clock >= cfg.rescueWithoutReport && !cfg.training)
            {
                Mistake("sa_no_report");
                RescueAt = Clock + 20f; // the group found him by luck, late
            }
            if (_blanketPromptAt > 0f && Clock >= _blanketPromptAt)
            {
                _blanketPromptAt = -1f;
                if (!Blanket) cues.Add(SelfAidCue.BlanketPrompt);
            }
            if (Acked && ArrivedAt < 0f && !CheckPending && _nextCheck > 0f && Clock >= _nextCheck && (RescueAt < 0f || Clock < RescueAt - 6f))
            {
                _nextCheck = Clock + cfg.checkInterval;
                Ask(cues);
            }
            if (CheckPending && Clock >= _checkDeadline)
            {
                if (!CheckLoud)
                {
                    CheckLoud = true;
                    _checkDeadline = Clock + cfg.answerWindow;
                    ChecksMissed++;
                    Mistake("sa_no_answer");
                    cues.Add(SelfAidCue.CheckInLoud);
                }
                else
                {
                    CheckPending = false; // they keep coming anyway
                    CheckLoud = false;
                }
            }
            if (RescueAt > 0f && ArrivedAt < 0f && Clock >= RescueAt)
            {
                ArrivedAt = Clock;
                CheckPending = false;
                cues.Add(SelfAidCue.RescueArrive);
                Note("Группа прибыла: «Держись, брат, свои!»", "Топ келді: «Шыда, бауырым!»", Severity.Ok);
            }
            if (ArrivedAt >= 0f && Clock >= ArrivedAt + cfg.arrivalSeconds)
            {
                cues.Add(SelfAidCue.Rescued);
                End("rescued");
                return cues;
            }

            // ---- death
            if (patient.outcome == Outcome.Died)
            {
                cues.Add(SelfAidCue.Died);
                End("died");
            }
            return cues;
        }

        void Ask(List<SelfAidCue> cues)
        {
            CheckPending = true;
            CheckLoud = false;
            ChecksAsked++;
            _checkDeadline = Clock + cfg.answerWindow;
            cues.Add(SelfAidCue.CheckIn);
        }

        /// <summary>Force the end (tests, menu). Mistakes that depend on the whole run are added here.</summary>
        public void End(string reason)
        {
            if (Ended) return;
            Ended = true;
            EndReason = reason;
            if (cfg.training) return;
            if (TqLockedAt < 0f && tq.Locked) TqLockedAt = Clock; // locked in the very last frame
            if (!tq.Locked) Mistake("sa_no_tq");
            else
            {
                if (TqLockedAt > cfg.tqDeadline) Mistake("sa_tq_slow");
                if (tq.limbPosition > cfg.highAndTightMax) Mistake("sa_tq_low");
                if (!wound.IsControlled) Mistake("sa_bleeding");
                if (TimeMarkedAt < 0f) Mistake("sa_no_time");
            }
            if (reason == "rescued")
            {
                if (!ReportSent) Mistake("sa_no_report");
                if (!Blanket) Mistake("sa_no_blanket");
            }
        }
    }

    /// Debrief row info for a self-aid mistake: what was wrong and why it matters (TCCC).
    public sealed class SelfAidMistakeInfo
    {
        public string code, ru, kz, whyRu, whyKz, stepRu, stepKz;
        public int points;
    }

    /// <summary>Builds the shared <see cref="DebriefReport"/> (board, score, timeline, map) for a self-aid run, plus the
    /// self-aid checklist (cover first / TQ in N s / high and tight / bleeding stopped / time / report / blanket /
    /// radio / stayed in cover).</summary>
    public static class SelfAidDebrief
    {
        static readonly SelfAidMistakeInfo[] Table =
        {
            M("sa_stayed_open", 15, "Оставался на открытом месте дольше 10 с — дрон зашёл повторно", "10 с астам ашық жерде қалды — дрон қайта келді",
                "TCCC: сначала уйти с линии огня. На открытом месте раненый — лёгкая цель для второго сброса.", "TCCC: алдымен оқ сызығынан кет. Ашық жердегі жаралы — оңай нысана.",
                "Укрытие", "Баспана"),
            M("sa_tq_in_open", 10, "Начал накладывать жгут на открытом месте, не укрывшись", "Баспанаға жетпей, ашық жерде жгут сала бастады",
                "Под огнём: укрытие первым — иначе второе ранение до того, как жгут затянут.", "Оқ астында алдымен баспана — әйтпесе жгут тартылмай жатып екінші жара.",
                "Укрытие", "Баспана"),
            M("sa_left_cover", 10, "Выходил из укрытия до прихода группы", "Топ келгенше баспанадан шықты",
                "В укрытии ждать своих: дрон-наблюдатель видит движение.", "Баспанада күт: бақылаушы дрон қозғалысты көреді.",
                "Укрытие", "Баспана"),
            M("sa_no_tq", 30, "Жгут не наложен", "Жгут салынбады",
                "Артериальное кровотечение из конечности убивает за 2–3 минуты. Носи жгут там, где его достанет ЛЮБАЯ рука.", "Артериялық қан кету 2–3 минутта өлтіреді. Жгутты кез келген қол жететін жерде ұста.",
                "Жгут на себя", "Өзіне жгут"),
            M("sa_tq_slow", 15, "Жгут наложен позже 60 с после ранения", "Жгут жарадан кейін 60 с кейін салынды",
                "TCCC: жгут — в первую минуту. Каждая лишняя минута — сотни миллилитров крови.", "TCCC: жгут — бірінші минутта. Әр минут — жүздеген мл қан.",
                "Жгут на себя", "Өзіне жгут"),
            M("sa_tq_low", 10, "Жгут не «высоко и туго» — ниже верхней трети", "Жгут «жоғары әрі қатты» емес",
                "Под огнём жгут — максимально высоко: рана может быть выше, чем кажется, а под одеждой не видно.", "Оқ астында жгут — барынша жоғары: жара көрінгеннен жоғары болуы мүмкін.",
                "Высоко и туго", "Жоғары әрі қатты"),
            M("sa_bleeding", 20, "Кровотечение не остановлено полностью", "Қан толық тоқтатылмады",
                "Крути вороток, пока не прекратится кровь и не пропадёт пульс ниже жгута. Слабый жгут усиливает кровотечение.", "Қан тоқтап, жгуттан төмен пульс жоғалғанша бұра. Әлсіз жгут қан кетуді күшейтеді.",
                "Высоко и туго", "Жоғары әрі қатты"),
            M("sa_no_time", 10, "Время на жгуте не записано", "Жгутқа уақыт жазылмады",
                "Время наложения нужно хирургу: от него зависит, сохранят ли конечность.", "Салынған уақыт хирургке керек: аяқ-қолды сақтау соған байланысты.",
                "Время на жгуте", "Жгут уақыты"),
            M("sa_no_report", 15, "Не доложил по рации", "Рациямен баяндамады",
                "Без доклада тебя никто не ищет. Держи связь со своими — это первое, что ускоряет помощь.", "Баяндамасаң, сені ешкім іздемейді. Өзіңдікілермен байланыста бол.",
                "Доклад", "Баяндау"),
            M("sa_report_incomplete", 8, "Доклад неполный или с ошибками", "Баяндау толық емес немесе қате",
                "Кто, где, что, что сделано: по докладу группа найдёт тебя и возьмёт нужное.", "Кім, қайда, не болды, не істелді — топ сені табады.",
                "Доклад", "Баяндау"),
            M("sa_no_blanket", 10, "Не укрылся спасательным одеялом", "Құтқару көрпесімен жабынбады",
                "Кровопотеря + холод = смертельная триада: гипотермия мешает свёртыванию крови.", "Қан жоғалту + суық = өлім үштігі: гипотермия қанның ұюына кедергі.",
                "Тепло", "Жылу"),
            M("sa_no_answer", 10, "Не отвечал на вызовы по рации", "Рация шақыруларына жауап бермеді",
                "Молчание = «без сознания или погиб». Отвечай: это держит в сознании и подгоняет группу.", "Үнсіздік = «есінен танды». Жауап бер: бұл сені есіңде ұстайды.",
                "Связь", "Байланыс"),
        };

        static SelfAidMistakeInfo M(string code, int pts, string ru, string kz, string whyRu, string whyKz, string stepRu, string stepKz) =>
            new SelfAidMistakeInfo { code = code, points = pts, ru = ru, kz = kz, whyRu = whyRu, whyKz = whyKz, stepRu = stepRu, stepKz = stepKz };

        public static IReadOnlyList<SelfAidMistakeInfo> All => Table;
        public static SelfAidMistakeInfo Info(string code) => Table.FirstOrDefault(m => m.code == code);

        public static int Score(SelfAidRun run)
        {
            int s = 100 - run.Mistakes.Keys.Sum(c => Info(c)?.points ?? 0);
            if (run.EndReason == "killed") s = Math.Min(s, 20);
            if (run.EndReason == "died") s = Math.Min(s, 10);
            return Math.Max(0, s);
        }

        /// <param name="clock">patient seconds → wall clock</param>
        public static DebriefReport Build(ScenarioDefinition def, SelfAidRun run, Func<float, string> clock, bool kz)
        {
            var c = run.cfg;
            bool survived = run.EndReason == "rescued";
            var r = new DebriefReport
            {
                scenarioId = def?.id ?? "SA",
                title = kz ? "Өзіне көмек" : "Самопомощь", // short: the board header also carries the flag + patch
                endReason = run.EndReason,
                survived = survived,
                score = Score(run),
                durationSeconds = run.Clock,
                lossPercent = run.patient.LossFraction * 100f,
                bleedControlSeconds = run.BleedStoppedAt,
                selfAid = true,
            };
            string limb = DebriefBuilder.LimbName(c.LimbId, kz);
            r.outcomeText = run.EndReason switch
            {
                "rescued" => kz ? "Сен тірі қалдың: топ сені баспанадан алып шықты." : "Ты выжил: группа забрала тебя из укрытия.",
                "killed" => kz ? "Дрон екінші рет келді — сен ашық жерде қалдың. Алдымен баспана!" : "Дрон зашёл второй раз — ты остался на открытом месте. Сначала укрытие!",
                "died" => (kz ? "Қан кетуден қаза таптың. Не құтқарар еді: баспана → бірінші минутта жылдам подсумкадан жгут, жоғары әрі қатты. Жгутты кез келген қол жететін жерде ұста."
                              : "Ты погиб от кровопотери. Что спасло бы: укрытие → жгут из быстрого подсумка в первую минуту, высоко и туго. Носи жгут там, где его достанет любая рука."),
                _ => kz ? "Жаттығу аяқталды." : "Тренировка завершена.",
            };
            r.limbOutcome = !run.tq.Locked ? limb + ": " + (kz ? "жгут жоқ" : "жгут не наложен")
                : limb + ": " + TourniquetConversion.LimbOutcome(Math.Max(1f, run.Clock - Math.Max(0f, run.tq.appliedAtSeconds)) + (survived ? 3600f : 0f), false, false, kz);

            foreach (var e in run.Log)
            {
                var parts = e.text.Split('|');
                r.timeline.Add(new DebriefEntry { time = e.time, clock = clock(e.time), text = kz && parts.Length > 1 ? parts[1] : parts[0], severity = e.severity });
            }
            foreach (var kv in run.Mistakes)
            {
                var m = Info(kv.Key);
                if (m == null) continue;
                string text = kz ? m.kz : m.ru;
                if (kv.Key == "sa_tq_slow" && run.TqLockedAt > 0f) text += $" ({run.TqLockedAt:0} с)";
                r.mistakes.Add(new DebriefMistake
                {
                    code = m.code, text = text, points = m.points, time = kv.Value,
                    consequence = kz ? m.whyKz : m.whyRu, step = kz ? m.stepKz : m.stepRu,
                });
            }
            r.mistakes.Sort((a, b) => b.points.CompareTo(a.points));

            // checklist
            string tqText = run.TqLockedAt >= 0f ? (kz ? $"Жгут {run.TqLockedAt:0} с ішінде салынды" : $"Жгут наложен за {run.TqLockedAt:0} с") : (kz ? "Жгут салынбады" : "Жгут не наложен");
            r.checks.Add(new DebriefCheck(kz ? "Өзіне көмекке дейін баспанаға кетті" : "Укрылся до самопомощи", run.CoverReachedAt >= 0f && !run.TqWrappedInOpen && run.DronePasses == 0));
            r.checks.Add(new DebriefCheck(tqText, run.TqLockedAt >= 0f && run.TqLockedAt <= c.tqDeadline));
            r.checks.Add(new DebriefCheck(kz ? "Жгут жоғары әрі қатты" : "Жгут высоко и туго", run.tq.Locked && run.tq.limbPosition <= c.highAndTightMax && run.tq.FullOcclusion));
            r.checks.Add(new DebriefCheck(kz ? "Қан тоқтатылды" : "Кровь остановлена", run.wound.IsControlled));
            r.checks.Add(new DebriefCheck(kz ? "Уақыт жазылды" : "Время записано", run.TimeMarkedAt >= 0f));
            r.checks.Add(new DebriefCheck(kz ? "Рациямен толық баяндады" : "Доклад по рации полный", run.ReportSent && run.ReportAllCorrect));
            r.checks.Add(new DebriefCheck(kz ? "Көрпе" : "Одеяло", run.Blanket));
            r.checks.Add(new DebriefCheck(kz ? "Байланысқа жауап берді" : "Отвечал на связь", run.ChecksMissed == 0 && (run.ChecksAsked > 0 || survived)));
            r.checks.Add(new DebriefCheck(kz ? "Баспанадан шықпады" : "Не выходил из укрытия", run.CoverReachedAt >= 0f && !run.LeftCover && run.DronePasses == 0));

            // phases: under fire until cover, in cover until the report, waiting for the group
            float end = run.Clock;
            float cover = run.CoverReachedAt >= 0f ? run.CoverReachedAt : end;
            r.phases.Add(new DebriefPhase("CUF", 0f, cover));
            if (run.CoverReachedAt >= 0f)
            {
                float rep = run.ReportSentAt >= 0f ? Math.Max(cover, run.ReportSentAt) : end;
                r.phases.Add(new DebriefPhase("TFC", cover, rep));
                if (run.ReportSentAt >= 0f) r.phases.Add(new DebriefPhase("TACEVAC", rep, end));
            }
            r.startTime = 0f;
            r.endTime = Math.Max(1f, end);
            r.NumberMistakes();
            return r;
        }
    }
}
