using System.Collections.Generic;
using FieldMedic.Casualty;
using FieldMedic.Input;
using FieldMedic.Medical;
using FieldMedic.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// Handheld/vest radio on the left shoulder strap (body-locked like the IFAK). Commander lines play through
    /// it (radio-filtered TTS placeholders in Resources/Radio, swappable with real recordings) with a subtitle on
    /// its small screen. The round PTT button (poke with a finger, point+trigger, desktop: M) transmits the
    /// simplified MEDEVAC request one line per press: location → casualties/precedence → injuries & TQ time →
    /// special (litter, zone). After the last line the request goes out and the commander acknowledges.
    /// </summary>
    public sealed class RadioSet : MonoBehaviour
    {
        public static RadioSet Instance { get; private set; }

        public Transform head;
        public bool bodyLocked = true;
        [Tooltip("w10: on the left shoulder strap, clear of the kit tray in front of the chest.")]
        public Vector3 offsetFromHead = new Vector3(-0.2f, -0.27f, 0.03f);
        public bool muted;
        /// <summary>w9b self-aid: the PTT button answers check-ins / opens the report menu instead of the MEDEVAC lines.
        /// Return true when handled.</summary>
        public System.Func<bool> pttOverride;
        /// <summary>w10 self-aid: the lines the Y panel offers (null result = nothing to choose, the press transmits).</summary>
        public System.Func<RadioReport> reportOverride;
        /// <summary>w10 self-aid: is a radio call expected right now (watch icon)?</summary>
        public System.Func<bool> expectOverride;

        public int MedevacStep { get; private set; }
        public bool MedevacSent { get; private set; }
        public string LastLine { get; private set; }
        public string Subtitle { get; private set; }
        public float SubtitleUntil { get; private set; }

        AudioSource _src;
        TextMesh _screen;
        RoundButton _ptt;
        float _yaw;

        static readonly Dictionary<string, (string ru, string kz)> Lines = new()
        {
            ["air"] = ("Воздух! Дрон над позицией, всем вниз!", "Әуе! Позиция үстінде дрон, бәрі төмен!"),
            ["sniper"] = ("Снайпер работает! Не высовываться!", "Мерген атып жатыр! Бас көтерме!"),
            ["mine"] = ("Стой! Мина! Иди только по своим следам!", "Тоқта! Мина! Тек өз ізіңмен жүр!"),
            ["barrage"] = ("Обстрел! Тащи его в руины, голову не поднимай!", "Атқылау! Оны қирандыға сүйре, басыңды көтерме!"),
            ["fpv"] = ("FPV в небе! Укрыться!", "Аспанда FPV! Жасырыныңдар!"),
            ["kab"] = ("КАБ! Всем вниз! Медик, проверь Ерлана!", "КАБ! Бәрі төмен! Медик, Ерланды тексер!"),
            ["mortar"] = ("Миномёт пристреливается! Не высовываться!", "Миномёт атып жатыр! Бас көтерме!"),
            ["no_evac"] = ("Дорога под дронами. Эвакуации до рассвета не будет. Держитесь.", "Жол дрондардың астында. Таңға дейін эвакуация болмайды."),
            ["report"] = ("Медик, доложи обстановку!", "Медик, жағдайды баянда!"),
            ["medevac_ack"] = ("Принял. Эвакуация выдвигается, держитесь.", "Қабылдадым. Эвакуация шықты, шыдаңдар."),
            ["evac_auto"] = ("Эвакуационная группа выдвигается к вам. Готовь раненого.", "Эвакуация тобы сендерге келе жатыр."),
            ["evac_arrived"] = ("Эвакуационная группа на месте. Передавай раненого.", "Эвакуация тобы келді. Жаралыны тапсыр."),
            ["evac_board"] = ("Раненый на борту. Садись, взлетаем!", "Жаралы бортта. Мін, ұшамыз!"),
            ["evac_away"] = ("Борт взлетел. Хорошая работа, медик.", "Борт ұшты. Жақсы жұмыс, медик."),
            ["sa_hit"] = ("Ты ранен. Сначала в укрытие, потом жгут.", "Сен жараландың. Алдымен баспанаға, сосын жгут."),
            ["sa_tq"] = ("Жгут из аптечки: нажми X. Свободной рукой. Высоко и туго.", "Жгут қобдишада: X бас. Бос қолыңмен. Жоғары әрі қатты."),
            ["sa_time"] = ("Запиши время на ленте жгута.", "Жгут таспасына уақытты жаз."),
            ["sa_report"] = ("Доложи по рации: зажми Y. Кто, где, что, что сделано.", "Рациямен баянда: Y-ты басып тұр. Кім, қайда, не болды, не істелді."),
            ["sa_ack"] = ("Принял, трёхсотый. Группа выдвигается к тебе. Оставайся в укрытии, будь на связи.", "Қабылдадым. Топ саған шықты. Баспанада қал, байланыста бол."),
            ["sa_check"] = ("Медик, как ты? Ответь.", "Медик, қалайсың? Жауап бер."),
            ["sa_check2"] = ("Медик! Ответь! Не спи!", "Медик! Жауап бер! Ұйықтама!"),
            ["sa_blanket"] = ("Укройся одеялом. Не дай себе замёрзнуть.", "Көрпемен жабын. Тоңып қалма."),
            ["sa_arrive"] = ("Держись, брат, свои!", "Шыда, бауырым, өзіміздікілер!"),
            ["sa_drone"] = ("Дрон возвращается! Уходи с открытого места!", "Дрон қайта келе жатыр! Ашық жерден кет!"),
            ["sa_down"] = ("Нога не держит. Не вставай — ползи в укрытие, голову ниже.", "Аяқ көтермейді. Тұрма — баспанаға жорғала, басыңды төмен ұста."),
            ["sa_heard"] = ("Слышу тебя. Держись, мы близко.", "Естіп тұрмын. Шыда, жақынбыз."),
            ["briefing"] = ("Внимание. Над позицией работает противник. Будь готов.", "Назар аударыңдар. Позиция үстінде жау жұмыс істеп жатыр."),
        };

        public static string Text(string id) => Lines.TryGetValue(id, out var l) ? GameSettings.T(l.ru, l.kz) : id;

        void Awake()
        {
            Instance = this;
            // scenes saved before w10 keep the old chest position — it sat inside the kit tray
            if (Vector3.Distance(offsetFromHead, new Vector3(-0.17f, -0.32f, 0.14f)) < 0.01f) offsetFromHead = new Vector3(-0.2f, -0.27f, 0.03f);
            _src = gameObject.AddComponent<AudioSource>();
            _src.spatialBlend = 0.85f;
            _src.minDistance = 0.4f;
            _src.maxDistance = 8f;
            _src.playOnAwake = false;
            Build();
            RadioPanel.Ensure(this);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build()
        {
            var body = WorldUI.Box(transform, "RadioBody", new Vector3(0f, 0f, 0.012f), new Vector3(0.062f, 0.11f, 0.034f), WorldUI.Lit(new Color(0.11f, 0.12f, 0.1f), 0.35f));
            WorldUI.Box(body.transform.parent, "Antenna", new Vector3(0.02f, 0.11f, 0.012f), new Vector3(0.008f, 0.12f, 0.008f), WorldUI.Lit(new Color(0.05f, 0.05f, 0.05f), 0.2f));
            WorldUI.Quad(transform, "Screen", new Vector3(0f, 0.028f, -0.0055f), new Vector2(0.05f, 0.03f), WorldUI.Unlit(new Color(0.12f, 0.2f, 0.12f)));
            _screen = WorldUI.Text(transform, "ScreenText", "", new Vector3(-0.023f, 0.041f, -0.007f), 0.0032f, new Color(0.65f, 1f, 0.6f));
            _ptt = RoundButton.Create(transform, "PTT", new Vector3(0f, -0.022f, -0.006f), 0.036f, new Color(0.45f, 0.1f, 0.08f), "PTT", 0.006f);
            _ptt.Clicked += PressToTalk;
            RefreshScreen();
        }

        Renderer[] _renderers;
        bool _hidden;

        void LateUpdate()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            // w10: the open kit tray fills the space in front of the chest — the shoulder radio steps out of the view
            bool hide = FieldMedic.Treatment.KitTray.Player != null && FieldMedic.Treatment.KitTray.Player.IsOpen;
            if (hide != _hidden)
            {
                _hidden = hide;
                _renderers ??= GetComponentsInChildren<Renderer>(true);
                foreach (var r in _renderers) if (r != null) r.enabled = !hide;
            }
            if (bodyLocked && head != null)
            {
                // follows the torso: yaw lags behind the head like a vest does
                float y = head.eulerAngles.y;
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, y)) > 35f) _yaw = Mathf.MoveTowardsAngle(_yaw, y, 200f * Time.deltaTime);
                var rot = Quaternion.Euler(0f, _yaw, 0f);
                transform.SetPositionAndRotation(head.position + rot * offsetFromHead, rot * Quaternion.Euler(20f, 25f, 0f));
            }
            // desktop M = hold left Y (DesktopSimulator) → RadioPanel
            if (Time.time > SubtitleUntil && Subtitle != null) { Subtitle = null; RefreshScreen(); }
        }

        /// <summary>Commander line by id (Resources/Radio/&lt;id&gt;_&lt;lang&gt;).</summary>
        public void Say(string id) => Say(id, 1f);

        /// <summary>w9b: `volume` &gt; 1 = the operator calls louder (spatial blend drops so it cuts through).</summary>
        public void Say(string id, float volume)
        {
            LastLine = id;
            Subtitle = Text(id);
            SubtitleUntil = Time.time + 6f;
            RefreshScreen();
            Debug.Log("[FMVR] radio: " + id + " — " + Subtitle);
            if (muted) return;
            var clip = FieldMedic.Audio.VoiceOver.Clip(id); // Russian voice-over in both UI languages (w10c)
            if (clip == null) return;
            _src.Stop();
            _src.clip = clip;
            _src.volume = Mathf.Clamp01(volume);
            _src.spatialBlend = volume > 1f ? 0.3f : 0.85f;
            _src.Play();
        }

        /// <summary>Our own transmitted line on the radio screen (w9b report).</summary>
        public void ShowLine(string text, float seconds = 5f)
        {
            Subtitle = text;
            SubtitleUntil = Time.time + seconds;
            RefreshScreen();
            if (!muted && _src != null) UiAudio.Click(transform.position);
        }

        /// <summary>World position of the PTT button (bots, hints).</summary>
        public Transform PttButton => _ptt != null ? _ptt.transform : transform;

        static readonly string[] MedevacRu =
        {
            "1/4 Место: руины, дом А, квадрат 3",
            "2/4 Раненых: 1, срочный",
            "3/4 Ранения: {0}",
            "4/4 Вертолёт. Площадка — поле за руинами, дым обозначу",
        };
        static readonly string[] MedevacKz =
        {
            "1/4 Орын: қирандылар, А үйі, 3-шаршы",
            "2/4 Жаралы: 1, шұғыл",
            "3/4 Жарақат: {0}",
            "4/4 Тікұшақ. Алаң — қирандылар артындағы дала, түтінмен белгілеймін",
        };

        /// <summary>One press of the PTT = one line of the MEDEVAC request.</summary>
        public void PressToTalk()
        {
            if (pttOverride != null && pttOverride()) return;
            if (MedevacSent) { Say("report"); return; }
            var c = FindCasualty();
            string injuries = Injuries(c);
            var lines = GameSettings.KZ ? MedevacKz : MedevacRu;
            Subtitle = string.Format(lines[MedevacStep], injuries);
            SubtitleUntil = Time.time + 5f;
            MedevacStep++;
            if (!muted && _src != null) UiAudio.Click(transform.position);
            if (MedevacStep >= lines.Length)
            {
                MedevacSent = true;
                if (c != null && c.Protocol != null) c.Protocol.Perform(MedAction.MedevacRequest);
                Invoke(nameof(Ack), 1.5f);
            }
            RefreshScreen();
        }

        void Ack() => Say("medevac_ack");

        /// <summary>w10: what the Y panel offers now. MEDEVAC: the four lines, the next one highlighted (they go out in
        /// order, one per Y hold); after the request: a radio check.</summary>
        public RadioReport CurrentReport()
        {
            if (reportOverride != null) return reportOverride();
            bool kz = GameSettings.KZ;
            if (MedevacSent)
            {
                var done = new RadioReport { title = GameSettings.T("РАЦИЯ  ·  эвакуация запрошена", "РАЦИЯ  ·  эвакуация сұралды") };
                done.Add(GameSettings.T("«На связи. Как слышно?»", "«Байланыстамын. Қалай естілед?»"));
                done.send = _ => ShowLine(GameSettings.T("«На связи.»", "«Байланыстамын.»"));
                return done;
            }
            var c = FindCasualty();
            string injuries = Injuries(c);
            var lines = kz ? MedevacKz : MedevacRu;
            var r = new RadioReport { title = GameSettings.T("РАЦИЯ  ·  запрос эвакуации, строка ", "РАЦИЯ  ·  эвакуация сұрау, жол ") + (MedevacStep + 1) + "/4", cursor = MedevacStep };
            for (int i = 0; i < lines.Length; i++)
                r.Add((i < MedevacStep ? "[+] " : "") + string.Format(lines[i], injuries), i == MedevacStep);
            r.send = i => { if (i == MedevacStep) PressToTalk(); };
            return r;
        }

        /// <summary>w10: the watch shows the radio icon while a report is expected.</summary>
        public bool ReportExpected
        {
            get
            {
                if (expectOverride != null) return expectOverride();
                if (MedevacSent) return false;
                var t = TrainingDirector.Instance;
                if (t != null && t.enabled && t.Progress != null) return t.Progress.Step != null && t.Progress.Step.id == "medevac";
                var d = ScenarioDirector.Instance;
                return d != null && !d.InMenu && d.Timeline != null &&
                       (d.State == FieldMedic.Medical.ScenarioState.TacticalFieldCare || d.State == FieldMedic.Medical.ScenarioState.Evacuation);
            }
        }

        /// <summary>Test helper: send the whole request at once.</summary>
        public void SendMedevacNow() { while (!MedevacSent) PressToTalk(); }

        static CasualtyController FindCasualty()
        {
            foreach (var c in CasualtyController.All) if (c != null && c.IsDown) return c;
            return null;
        }

        static string Injuries(CasualtyController c)
        {
            if (c == null || c.Patient == null) return "—";
            var parts = new List<string>();
            bool kz = GameSettings.KZ;
            foreach (var w in c.Patient.wounds)
                if (w.discovered) parts.Add(kz ? FieldMedic.Treatment.CardItem.RegionKz(w.region) : FieldMedic.Treatment.CardItem.RegionRu(w.region));
            string s = parts.Count == 0 ? (kz ? "тексерілуде" : "осмотр") : string.Join(", ", parts);
            foreach (var tq in c.Patient.tourniquets)
                if (tq.Locked) s += (kz ? ", жгут " : ", жгут ") + ScenarioClock.Format(Mathf.Max(0f, tq.appliedAtSeconds));
            return s;
        }

        void RefreshScreen()
        {
            if (_screen == null) return;
            string head = MedevacSent ? "MEDEVAC OK" : MedevacStep > 0 ? "MEDEVAC " + MedevacStep + "/4" : "КАНАЛ 3";
            _screen.text = head + "\n" + WorldUI.Wrap(Subtitle ?? "", 26);
        }
    }
}
