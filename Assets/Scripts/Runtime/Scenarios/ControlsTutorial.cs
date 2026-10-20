using System;
using System.Collections.Generic;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Treatment;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// w10 first-launch controls tutorial (60–90 s, skippable, replayable from the menu «УПРАВЛЕНИЕ»): turn with the
    /// right stick, take a canteen with GRIP, drop it, take it with the TRIGGER, X → the kit tray in front of the eyes,
    /// take the CAT and put it back over the tray, X → fold it to the hip, hold Y → radio, look at the watch. Every step
    /// completes only through the real input (buttons → HandGrabber / KitTray / RadioPanel). Runs in place in the menu
    /// (no scene reload), the board hides meanwhile and comes back at the end.
    /// </summary>
    public sealed class ControlsTutorial : MonoBehaviour
    {
        public static ControlsTutorial Instance { get; private set; }

        public sealed class Step
        {
            public string id, ru, kz;
            public ControlIcon[] icons;
            public Func<bool> done;
            public float minSeconds = 0.6f, autoAfter = -1f;
        }

        public ScenarioDirector director;
        public Transform head;
        public readonly List<Step> Steps = new List<Step>();
        public int Index { get; private set; }
        public Step Current => Index >= 0 && Index < Steps.Count ? Steps[Index] : null;
        public bool Finished { get; private set; }
        public bool Skipped { get; private set; }
        public Grabbable Canteen { get; private set; }
        public event Action<bool> Ended;

        Transform _card, _crate;
        TMPro.TextMeshPro _title, _text, _counter;
        RoundButton _skip;
        float _stepAt, _startYaw, _watchSeen, _doneAt = -1f;
        bool _catTaken, _canteenReleased;
        int _radioSends;
        Vector3 _canteenHome;
        RadioSet _radio;

        public static ControlsTutorial Begin(ScenarioDirector d, Transform headOverride = null)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ControlsTutorial");
            var t = go.AddComponent<ControlsTutorial>();
            t.director = d;
            t.head = headOverride != null ? headOverride : d != null && d.head != null ? d.head : Camera.main != null ? Camera.main.transform : null;
            t.Setup();
            return t;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_card != null) Destroy(_card.gameObject);
            if (_crate != null) Destroy(_crate.gameObject);
            if (Canteen != null) Destroy(Canteen.gameObject);
            if (_radio != null && _radio.reportOverride == TutorialReport) _radio.reportOverride = null;
        }

        static IfakPouch Pouch => KitTray.Player != null ? KitTray.Player.pouch : null;
        static TourniquetCAT Cat => Pouch != null ? Pouch.Find<TourniquetCAT>() : null;

        bool HeldWith(GrabButton b)
        {
            foreach (var h in HandGrabber.All) if (h != null && h.Held == Canteen && Canteen != null && h.HeldWith == b) return true;
            return false;
        }

        void Setup()
        {
            Debug.Log("[FMVR] tutorial: controls tutorial started");
            if (director != null && director.board != null) director.board.gameObject.SetActive(false);
            _radio = RadioSet.Instance;
            if (_radio != null) _radio.reportOverride = TutorialReport;
            if (head != null) _startYaw = head.eulerAngles.y;
            BuildProps();
            BuildCard();

            Add("turn", "Осмотрись. Правый стик влево/вправо — поворот", "Айналаңа қара. Оң стик солға/оңға — бұрылу",
                () => head != null && Mathf.Abs(Mathf.DeltaAngle(_startYaw, head.eulerAngles.y)) > 35f, 8f, ControlIcon.Stick);
            Add("grip", "Поднеси руку к фляге — она подсветится. Нажми GRIP (боковая кнопка) и держи",
                "Қолыңды фляжкаға жақында — жарқырайды. GRIP (бүйір түйме) бас та ұста", () => HeldWith(GrabButton.Grip), -1f, ControlIcon.Grip);
            Add("drop", "Отпусти GRIP — фляга упадёт", "GRIP-ті жібер — фляжка түседі", () => _canteenReleased, -1f, ControlIcon.Grip);
            Add("trigger", "Теперь возьми её КУРКОМ (указательный палец)", "Енді оны ШҮРІППЕМЕН ал (сұқ саусақ)", () => HeldWith(GrabButton.Trigger), -1f, ControlIcon.Trigger);
            Add("kit", "Нажми X на левом контроллере — аптечка перед глазами", "Сол контроллердегі X бас — қобдиша алдыңда",
                () => KitTray.Player == null || KitTray.Player.IsOpen, -1f, ControlIcon.X);
            Add("kit_item", "Возьми жгут CAT. Отпусти над аптечкой — он вернётся на место",
                "CAT жгутын ал. Қобдишаның үстінде жібер — орнына қайтады", () => KitTray.Player == null || (_catTaken && Cat != null && Cat.InPouch), -1f, ControlIcon.Grip, ControlIcon.Trigger);
            Add("kit_close", "Нажми X ещё раз — аптечка уйдёт на левое бедро", "X-ті тағы бас — қобдиша сол жамбасқа кетеді",
                () => KitTray.Player == null || !KitTray.Player.IsOpen, -1f, ControlIcon.X);
            Add("radio", "Рация: зажми Y, выбери строку, отпусти Y — отправлено", "Рация: Y-ты басып тұр, жолды таңда, Y жібер — жіберілді",
                () => _radio == null || _radioSends > 0, -1f, ControlIcon.Y);
            Add("watch", "Посмотри на левое запястье: часы, текущий шаг, кнопка АПТЕЧКА", "Сол білекке қара: сағат, қазіргі қадам, ҚОБДИША түймесі",
                () => _watchSeen > 0.6f, 12f, ControlIcon.Hand);
            Add("done", "Готово! Тащить раненого: держи стропу + стик назад. Фонарь — нажми левый стик. Держи B — меню. Повтор — «УПРАВЛЕНИЕ»",
                "Дайын! Жаралыны сүйреу: бауды ұста + стик артқа. Шам — сол стикті бас. B ұста — мәзір. Қайталау — «БАСҚАРУ»", () => true, -1f, ControlIcon.Grip, ControlIcon.Stick, ControlIcon.B);
            Steps[Steps.Count - 1].minSeconds = 4f;
            if (RadioPanel.Instance != null) RadioPanel.Instance.Sent += OnRadioSent;
            Index = 0;
            ShowStep();
        }

        void OnRadioSent(int i) => _radioSends++;

        RadioReport TutorialReport()
        {
            var r = new RadioReport { title = GameSettings.T("РАЦИЯ  ·  проверка связи", "РАЦИЯ  ·  байланысты тексеру") };
            r.Add(GameSettings.T("«Я Медик. Как слышно? Приём.»", "«Мен Медикпін. Қалай естілед? Қабылда.»"));
            r.Add(GameSettings.T("«Медик на позиции.»", "«Медик орнында.»"));
            r.send = i => { if (_radio != null) _radio.ShowLine(r.options[i]); };
            return r;
        }

        void Add(string id, string ru, string kz, Func<bool> done, float autoAfter, params ControlIcon[] icons) =>
            Steps.Add(new Step { id = id, ru = ru, kz = kz, done = done, autoAfter = autoAfter, icons = icons });

        // ------------------------------------------------------------------ props
        void BuildProps()
        {
            Vector3 eye = head != null ? head.position : new Vector3(0f, 1.6f, 0f);
            Vector3 fwd = head != null ? head.forward : Vector3.forward;
            Vector3 dir = WorldUI.FreeDirection(eye + Vector3.down * 0.6f, Quaternion.Euler(0f, 25f, 0f) * fwd, 1.5f, out float clear);
            float floor = director != null && director.playerRig != null ? director.playerRig.position.y : eye.y - 1.6f;
            float dist = Mathf.Clamp(clear - 0.35f, 0.45f, 0.6f);
            Vector3 c = new Vector3(eye.x, floor, eye.z) + dir * dist;
            _crate = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            _crate.name = "Tutorial_Crate";
            _crate.localScale = new Vector3(0.42f, 0.78f, 0.36f);
            _crate.SetPositionAndRotation(c + Vector3.up * 0.39f, Quaternion.LookRotation(-dir));
            _crate.GetComponent<Renderer>().sharedMaterial = Kit.Mat("TutorialCrate", new Color(0.3f, 0.27f, 0.17f), 0.15f);
            _canteenHome = c + Vector3.up * 0.86f;

            var go = new GameObject("Tutorial_Canteen");
            go.transform.position = _canteenHome;
            var body = Kit.Part(go.transform, PrimitiveType.Cylinder, "Body", Vector3.zero, new Vector3(0.085f, 0.075f, 0.085f), Kit.Mat("Canteen", Kit.Olive, 0.3f));
            Kit.Part(go.transform, PrimitiveType.Cylinder, "Cap", new Vector3(0f, 0.085f, 0f), new Vector3(0.032f, 0.012f, 0.032f), Kit.Mat("CanteenCap", new Color(0.08f, 0.08f, 0.07f), 0.3f));
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.085f, 0.18f, 0.085f);
            box.center = new Vector3(0f, 0.01f, 0f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            Canteen = go.AddComponent<Grabbable>();
            Canteen.Released += (_, __) => _canteenReleased = true;
            go.AddComponent<GrabHighlight>();
        }

        void BuildCard()
        {
            _card = new GameObject("TutorialCard").transform;
            UiMesh.Add(_card, "Bg", UiMesh.RoundedRect(0.62f, 0.3f, 0.035f, new Color(0.97f, 0.97f, 0.95f, 0.97f), new Color(0.9f, 0.91f, 0.92f, 0.97f)), UiMesh.Mat(Color.white, 3101), new Vector3(0f, 0f, 0.003f));
            _counter = UiMesh.Text(_card, "Counter", "", new Vector3(-0.28f, 0.13f, 0f), 0.014f, new Color(0.42f, 0.46f, 0.5f), 0.3f, 3104);
            _title = UiMesh.Text(_card, "Title", GameSettings.T("УПРАВЛЕНИЕ", "БАСҚАРУ"), new Vector3(-0.28f, 0.1f, 0f), 0.022f, new Color(0.11f, 0.13f, 0.15f), 0.56f, 3104, TMPro.TextAlignmentOptions.TopLeft, true);
            _text = UiMesh.Text(_card, "Text", "", new Vector3(-0.28f, 0.045f, 0f), 0.017f, new Color(0.11f, 0.13f, 0.15f), 0.56f, 3104);
            _skip = RoundButton.Create(_card, "Skip", new Vector3(0.255f, -0.105f, -0.004f), 0.06f, new Color(0.45f, 0.47f, 0.5f), "»", 0.022f, GameSettings.T("пропустить", "өткізу"), 0.011f);
            _skip.Clicked += Skip;
            PlaceCard();
        }

        void PlaceCard()
        {
            if (head == null || _crate == null) return;
            // above the crate, a little further, facing the player
            Vector3 eye = head.position;
            Vector3 p = _crate.position + Vector3.up * 0.75f;
            Vector3 flat = p - eye; flat.y = 0f;
            p = eye + flat.normalized * Mathf.Max(0.85f, flat.magnitude + 0.25f) + Vector3.up * (p.y - eye.y);
            p.y = eye.y - 0.05f;
            _card.SetPositionAndRotation(p, Quaternion.LookRotation(p - eye));
        }

        // ------------------------------------------------------------------ flow
        void ShowStep()
        {
            var s = Current;
            if (s == null) return;
            _stepAt = Time.time;
            bool kz = GameSettings.KZ;
            _counter.text = (Index + 1) + " / " + Steps.Count;
            _text.text = kz ? s.kz : s.ru;
            StepGuide.Set(s.ru, s.kz, s.icons);
            Debug.Log("[FMVR] tutorial: step " + s.id);
            if (s.id == "kit_item" && KitTray.Player != null) KitTray.Player.Wanted = MedItemKind.Tourniquet;
            if (s.id == "kit_close" && KitTray.Player != null) KitTray.Player.Wanted = null;
        }

        void Update()
        {
            if (Finished) return;
            var s = Current;
            if (s == null) { Finish(false); return; }
            if (Cat != null && Cat.IsHeld) _catTaken = true;
            if (s.id == "drop" && !HeldWith(GrabButton.Grip) && Canteen != null && !Canteen.IsGrabbed) _canteenReleased = true;
            if (s.id == "grip") _canteenReleased = false;
            WatchLook();
            KeepCanteenReachable();
            _text.text = GameSettings.KZ ? s.kz : s.ru;
            float age = Time.time - _stepAt;
            bool ok = age >= s.minSeconds && (s.done() || (s.autoAfter > 0f && age >= s.autoAfter));
            if (!ok) return;
            if (s.id == "done") { Finish(false); return; }
            UiAudio.Click(_card != null ? _card.position : Vector3.zero);
            Index++;
            ShowStep();
        }

        void WatchLook()
        {
            var w = FindAnyObjectByType<WristWatch>();
            if (w == null || head == null) return;
            Vector3 to = head.position - w.transform.position;
            // the face looks at the eyes (watch content faces its −Z) within ~50 cm
            bool seen = to.magnitude < 0.55f && Vector3.Dot(-w.transform.forward, to.normalized) > 0.5f;
            _watchSeen = seen ? _watchSeen + Time.deltaTime : Mathf.Max(0f, _watchSeen - Time.deltaTime);
        }

        void KeepCanteenReachable()
        {
            if (Canteen == null || Canteen.IsGrabbed || Canteen.Body == null) return;
            // fell far / rolled away: back on the crate (never lost under the floor)
            Vector3 eye = head != null ? head.position : _canteenHome;
            float far = Vector3.Distance(new Vector3(Canteen.transform.position.x, 0f, Canteen.transform.position.z), new Vector3(eye.x, 0f, eye.z));
            if (Canteen.transform.position.y < _canteenHome.y - 2.5f || far > 2.2f)
            {
                Canteen.Body.linearVelocity = Vector3.zero;
                Canteen.Body.angularVelocity = Vector3.zero;
                Canteen.transform.SetPositionAndRotation(_canteenHome, Quaternion.identity);
                Canteen.Body.position = _canteenHome;
            }
        }

        public void Skip()
        {
            if (Finished) return;
            Debug.Log("[FMVR] tutorial: skipped at step " + (Current != null ? Current.id : "-"));
            Finish(true);
        }

        void Finish(bool skipped)
        {
            if (Finished) return;
            Finished = true;
            Skipped = skipped;
            GameSettings.TutorialSeen = true;
            if (KitTray.Player != null) { KitTray.Player.Wanted = null; if (KitTray.Player.IsOpen) KitTray.Player.Close(); }
            if (RadioPanel.Instance != null) RadioPanel.Instance.Sent -= OnRadioSent;
            if (_radio != null && _radio.reportOverride == TutorialReport) _radio.reportOverride = null;
            Debug.Log("[FMVR] tutorial: " + (skipped ? "skipped" : "complete"));
            Ended?.Invoke(skipped);
            if (director != null && director.board != null)
            {
                director.board.gameObject.SetActive(true);
                director.board.ShowMain();
            }
            StepGuide.Set("Меню: наведи луч на кнопку, КУРОК — нажать (или нажми пальцем)", "Мәзір: сәулені түймеге бағытта, ШҮРІППЕ — басу (не саусақпен)", ControlIcon.Trigger);
            Destroy(gameObject);
        }
    }
}
