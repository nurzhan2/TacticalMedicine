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
        public Vector3 offsetFromHead = new Vector3(-0.17f, -0.32f, 0.14f);
        public bool muted;

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
            ["barrage"] = ("Обстрел! Тащи его в блиндаж, голову не поднимай!", "Атқылау! Оны блиндажға сүйре, басыңды көтерме!"),
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
            ["briefing"] = ("Внимание. Над позицией работает противник. Будь готов.", "Назар аударыңдар. Позиция үстінде жау жұмыс істеп жатыр."),
        };

        public static string Text(string id) => Lines.TryGetValue(id, out var l) ? GameSettings.T(l.ru, l.kz) : id;

        void Awake()
        {
            Instance = this;
            _src = gameObject.AddComponent<AudioSource>();
            _src.spatialBlend = 0.85f;
            _src.minDistance = 0.4f;
            _src.maxDistance = 8f;
            _src.playOnAwake = false;
            Build();
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

        void LateUpdate()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (bodyLocked && head != null)
            {
                // follows the torso: yaw lags behind the head like a vest does
                float y = head.eulerAngles.y;
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, y)) > 35f) _yaw = Mathf.MoveTowardsAngle(_yaw, y, 200f * Time.deltaTime);
                var rot = Quaternion.Euler(0f, _yaw, 0f);
                transform.SetPositionAndRotation(head.position + rot * offsetFromHead, rot * Quaternion.Euler(20f, 25f, 0f));
            }
            var kb = Keyboard.current;
            if (!DesktopSimulator.XRActive && kb != null && kb.mKey.wasPressedThisFrame) PressToTalk();
            if (Time.time > SubtitleUntil && Subtitle != null) { Subtitle = null; RefreshScreen(); }
        }

        /// <summary>Commander line by id (Resources/Radio/&lt;id&gt;_&lt;lang&gt;).</summary>
        public void Say(string id)
        {
            LastLine = id;
            Subtitle = Text(id);
            SubtitleUntil = Time.time + 6f;
            RefreshScreen();
            Debug.Log("[FMVR] radio: " + id + " — " + Subtitle);
            if (muted) return;
            var lang = GameSettings.KZ ? "kz" : "ru";
            var clip = Resources.Load<AudioClip>("Radio/" + id + "_" + lang) ?? Resources.Load<AudioClip>("Radio/" + id + "_ru");
            if (clip == null) return;
            _src.Stop();
            _src.clip = clip;
            _src.Play();
        }

        static readonly string[] MedevacRu =
        {
            "1/4 Место: окоп, блиндаж, сектор 3",
            "2/4 Раненых: 1, срочный",
            "3/4 Ранения: {0}",
            "4/4 Нужны носилки. Подход по ходу сообщения",
        };
        static readonly string[] MedevacKz =
        {
            "1/4 Орын: окоп, блиндаж, 3-сектор",
            "2/4 Жаралы: 1, шұғыл",
            "3/4 Жарақат: {0}",
            "4/4 Зембіл керек. Қатынас жолымен",
        };

        /// <summary>One press of the PTT = one line of the MEDEVAC request.</summary>
        public void PressToTalk()
        {
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
            foreach (var w in c.Patient.wounds) if (w.discovered) parts.Add(w.region.ToString());
            string s = parts.Count == 0 ? "осмотр" : string.Join(", ", parts);
            foreach (var tq in c.Patient.tourniquets)
                if (tq.Locked) s += ", жгут " + ScenarioClock.Format(Mathf.Max(0f, tq.appliedAtSeconds));
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
