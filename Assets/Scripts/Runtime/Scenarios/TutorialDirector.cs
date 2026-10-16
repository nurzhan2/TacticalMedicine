using System;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// 60-second hands tutorial at the start of Training (w8): on a practice dummy next to the player — a metal ammo
    /// can and a long wooden rifle crate with a training tourniquet strapped round it — the player learns the five
    /// motions every procedure is made of: GRAB (lift the can), PULL (the strap ≥ 12 cm), TWIST (the windlass half
    /// a turn), PINCH (pick the small tag off the lid) and a TWO-HAND DRAG (both rope handles, ≥ 1 m). The white hint
    /// card leads; «ПРОПУСТИТЬ» skips; done once → the menu remembers it (PlayerPrefs) and Training starts directly.
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        public const string LessonId = "tutorial";
        const string DoneKey = "fm_tutorial_done";
        /// <summary>Tests switch the automatic tutorial off (old flows start their lesson at once).</summary>
        public static bool AutoStart = true;
        public static TutorialDirector Instance { get; private set; }

        public static bool Done
        {
            get => PlayerPrefs.GetInt(DoneKey, 0) == 1;
            set { PlayerPrefs.SetInt(DoneKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool ShouldRun(string lessonId) =>
            lessonId == LessonId || (AutoStart && !Done && lessonId != TrainingCatalog.DemoId);

        public ScenarioDirector director;
        public float liftHeight = 0.15f, strapPull = 0.12f, twistDegrees = 180f, dragDistance = 1f;

        public int Step { get; private set; }
        public bool Finished { get; private set; }
        public bool Skipped { get; private set; }
        public float StartedAt { get; private set; }
        public HintPanel Panel { get; private set; }
        public RoundButton SkipButton { get; private set; }
        public Grabbable Can { get; private set; }
        public Grabbable Crate { get; private set; }
        public ProxyHandle Strap { get; private set; }
        public ProxyHandle Rod { get; private set; }
        public Grabbable Tag { get; private set; }
        public float TwistAccum => _twist;
        public float StrapPulled { get; private set; }
        public float CrateDragged { get; private set; }
        public Transform Root => _root;
        public static readonly LessonStep[] Steps = BuildSteps();

        Action _then;
        Transform _root, _strapVisual, _rodVisual;
        float _canStartY, _twist, _doneAt = -1f;
        Quaternion _rodPrev;
        bool _rodWasHeld;
        Vector3 _crateStart;
        bool _crateTwoHands;
        int _clicks;

        static LessonStep[] BuildSteps() => new[]
        {
            new LessonStep("t_grab", "Хват: подними ящик", "Ұстау: жәшікті көтер").Show(null, null, "practice")
                .Act("Поднеси руку к ручке патронного ящика", "Қолыңды оқ-дәрі жәшігінің тұтқасына апар")
                .Act("Сожми кулак (кнопка хвата) и подними на 15 см", "Жұдырықты қыс (ұстау түймесі) және 15 см көтер"),
            new LessonStep("t_pull", "Потяни ремень", "Белдікті тарт", null).Show(null, null, "tq")
                .Act("Возьмись за свободный конец ремня", "Белдіктің бос ұшынан ұста")
                .Act("Тяни от себя резко, на 12 см — чувствуешь сопротивление", "Өзіңнен қатты тарт, 12 см — кедергіні сезесің"),
            new LessonStep("t_twist", "Поверни вороток", "Бұрағышты бұра", null).Show(null, null, "tq")
                .Act("Возьмись за чёрный стержень", "Қара өзекті ұста")
                .Act("Поверни кисть на пол-оборота — щелчок", "Білекті жарты айналым бұра — шырт"),
            new LessonStep("t_pinch", "Щипок", "Шымшу", null).Show(null, null, "gauze")
                .Act("Мелкие предметы — двумя пальцами", "Ұсақ заттар — екі саусақпен")
                .Act("Сведи большой и указательный и сними бирку", "Бас бармақ пен сұқ саусақты қосып, белгіні ал"),
            new LessonStep("t_drag", "Тащи двумя руками", "Екі қолмен сүйре", null).Show(null, null, "drag")
                .Act("Возьмись за обе верёвочные ручки", "Екі арқан тұтқадан ұста")
                .Act("Тащи ящик на метр — двумя руками быстрее", "Жәшікті бір метрге сүйре — екі қолмен тезірек"),
        };

        // ------------------------------------------------------------------ start
        /// <summary>Starts the tutorial next to the player; `then` runs when it is finished or skipped.</summary>
        public static TutorialDirector Begin(ScenarioDirector d, Action then)
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("Tutorial");
            var t = go.AddComponent<TutorialDirector>();
            t.director = d;
            t._then = then;
            t.Build();
            return t;
        }

        void Awake() => Instance = this;
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Panel != null) Destroy(Panel.gameObject);
            if (_root != null) Destroy(_root.gameObject);
        }

        Transform Head => director != null && director.head != null ? director.head : Camera.main != null ? Camera.main.transform : null;

        void Build()
        {
            StartedAt = Time.time;
            // place the dummy in free space ~0.9 m in front of the player, on the ground
            Vector3 origin = director != null && director.playerRig != null ? director.playerRig.position : Vector3.zero;
            Vector3 fwd = director != null && director.playerRig != null ? director.playerRig.forward : Vector3.forward;
            var h = Head;
            if (h != null) { fwd = Vector3.ProjectOnPlane(h.forward, Vector3.up); if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize(); origin = new Vector3(h.position.x, origin.y, h.position.z); }
            Vector3 dir = WorldUI.FreeDirection(origin + Vector3.up * 0.5f, fwd, 2.2f, out float clear);
            Vector3 p = origin + dir * Mathf.Clamp(clear - 0.7f, 0.6f, 0.95f);
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            _root = new GameObject("PracticeDummy").transform;
            _root.SetPositionAndRotation(p, Quaternion.LookRotation(-dir, Vector3.up));
            BuildProps();

            Panel = HintPanel.Create("TutorialPanel");
            SkipButton = RoundButton.Create(Panel.transform, "Btn_Skip", new Vector3(0.2f, -0.2f, -0.01f), 0.07f, new Color(0.35f, 0.37f, 0.4f),
                GameSettings.T("ПРОПУСТИТЬ", "ӨТКІЗУ"), 0.011f);
            SkipButton.Clicked += Skip;
            ShowStep();
            Debug.Log("[FMVR] tutorial: started at " + p.ToString("F2"));
        }

        void BuildProps()
        {
            var props = TutorialProps.Load();
            var wood = Kit.Mat("PracticeWood", new Color(0.42f, 0.33f, 0.2f), 0.2f);
            var steel = Kit.Mat("PracticeCan", new Color(0.25f, 0.28f, 0.2f), 0.45f, 0.5f);
            var web = Kit.Mat("CatStrap", Kit.CatBlack, 0.25f);
            var red = Kit.Mat("CatTab", Kit.CatRed, 0.35f);

            // ---- rifle crate (two rope handles at the ends)
            var crateGo = new GameObject("PracticeCrate");
            crateGo.transform.SetParent(_root, false);
            crateGo.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            crateGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);    // long side across the player's view
            Vector3 size = new Vector3(0.93f, 0.39f, 0.35f);
            if (props != null && props.crate != null)
            {
                var vis = Instantiate(props.crate, crateGo.transform);
                vis.name = "Visual";
                vis.transform.localScale = Vector3.one * props.crateScale;
                foreach (var c in vis.GetComponentsInChildren<Collider>()) Destroy(c);
                var b = Bounds(vis);
                vis.transform.position += crateGo.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
                size = crateGo.transform.InverseTransformVector(b.size);
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            }
            else Kit.Part(crateGo.transform, PrimitiveType.Cube, "Visual", new Vector3(0f, size.y * 0.5f, 0f), size, wood);
            var box = crateGo.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            box.size = size;
            var rb = crateGo.AddComponent<Rigidbody>();
            rb.mass = 14f;
            rb.linearDamping = 1.5f; rb.angularDamping = 4f;
            Crate = crateGo.AddComponent<Grabbable>();
            Crate.poseKey = "drag_handle";
            Crate.requireGrabPoint = true;
            Crate.referenceMass = 6f;
            Crate.loadWhileHeld = 0.8f;
            for (int s = -1; s <= 1; s += 2)
            {
                var gp = new GameObject(s < 0 ? "Handle_L" : "Handle_R").AddComponent<GrabPoint>();
                gp.transform.SetParent(crateGo.transform, false);
                gp.transform.localPosition = new Vector3(s * (size.x * 0.5f + 0.02f), size.y * 0.62f, 0f);
                gp.transform.localRotation = Quaternion.LookRotation(Vector3.right * s, Vector3.up);
                gp.radius = 0.1f;
                gp.snap = false;
            }
            Crate.RefreshGrabPoints();

            // ---- training tourniquet round the crate: strap tail + windlass rod on the lid
            Transform top = crateGo.transform;
            Kit.Part(top, PrimitiveType.Cube, "Band", new Vector3(0.18f, size.y + 0.004f, 0f), new Vector3(0.038f, 0.006f, size.z + 0.01f), web);
            _strapVisual = Kit.Part(top, PrimitiveType.Cube, "StrapTail", new Vector3(0.18f, size.y * 0.55f, -size.z * 0.5f - 0.045f), new Vector3(0.038f, 0.005f, 0.09f), web).transform;
            Kit.Part(_strapVisual, PrimitiveType.Cube, "Tab", new Vector3(0f, 0f, -0.55f), new Vector3(1.05f, 1.6f, 0.25f), red);
            Strap = ProxyHandle.Create<ProxyHandle>("Grab_PracticeStrap", top, top.TransformPoint(new Vector3(0.18f, size.y * 0.55f, -size.z * 0.5f - 0.09f)), 0.055f, 5);
            Strap.poseKey = "cat_strap";
            _rodVisual = new GameObject("Windlass").transform;
            _rodVisual.SetParent(top, false);
            _rodVisual.localPosition = new Vector3(0.18f, size.y + 0.018f, 0f);
            Kit.Part(_rodVisual, PrimitiveType.Cylinder, "Rod", Vector3.zero, new Vector3(0.016f, 0.06f, 0.016f), web, Quaternion.Euler(90f, 0f, 0f));
            Rod = ProxyHandle.Create<ProxyHandle>("Grab_PracticeWindlass", top, _rodVisual.position, 0.05f, 6);
            Rod.poseKey = "cat_rod";

            // ---- small tag on the lid (pinch)
            var tagGo = new GameObject("PracticeTag");
            tagGo.transform.SetParent(_root, false);
            tagGo.transform.position = crateGo.transform.TransformPoint(new Vector3(-0.22f, size.y + 0.006f, 0f));
            Kit.Part(tagGo.transform, PrimitiveType.Cube, "Tag", Vector3.zero, new Vector3(0.05f, 0.004f, 0.03f), Kit.Mat("TagOlive", Kit.Olive, 0.4f));
            Kit.Part(tagGo.transform, PrimitiveType.Cylinder, "Eyelet", new Vector3(-0.018f, 0.003f, 0f), new Vector3(0.008f, 0.002f, 0.008f), steel);
            var tc = tagGo.AddComponent<BoxCollider>(); tc.size = new Vector3(0.05f, 0.01f, 0.03f);
            var trb = tagGo.AddComponent<Rigidbody>(); trb.mass = 0.02f; trb.isKinematic = true;
            Tag = tagGo.AddComponent<Grabbable>();
            Tag.poseKey = "tab";
            Tag.priority = 3;

            // ---- ammo can (grab + lift), on the ground beside the crate
            var canGo = new GameObject("PracticeAmmoCan");
            canGo.transform.SetParent(_root, false);
            canGo.transform.localPosition = new Vector3(-0.62f, 0f, -0.05f);
            Vector3 csize = new Vector3(0.09f, 0.26f, 0.18f);
            if (props != null && props.ammoCan != null)
            {
                var vis = Instantiate(props.ammoCan, canGo.transform);
                vis.name = "Visual";
                vis.transform.localScale = Vector3.one * props.ammoCanScale;
                foreach (var c in vis.GetComponentsInChildren<Collider>()) Destroy(c);
                var b = Bounds(vis);
                vis.transform.position += canGo.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
                csize = b.size;
            }
            else Kit.Part(canGo.transform, PrimitiveType.Cube, "Visual", new Vector3(0f, csize.y * 0.5f, 0f), csize, steel);
            var cc = canGo.AddComponent<BoxCollider>(); cc.center = new Vector3(0f, csize.y * 0.5f, 0f); cc.size = csize;
            var crb = canGo.AddComponent<Rigidbody>(); crb.mass = 5f;
            Can = canGo.AddComponent<Grabbable>();
            Can.poseKey = "drag_handle";
            Can.allowTwoHands = false;
            _canStartY = canGo.transform.position.y;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------ flow
        void ShowStep()
        {
            if (Step >= Steps.Length) return;
            Panel.SetStep(Steps[Step], Step + 1, Steps.Length, GameSettings.KZ);
            Panel.SetVisible(true);
            _doneAt = -1f;
        }

        public bool StepSatisfied(int i)
        {
            switch (i)
            {
                case 0: return Can != null && Can.IsGrabbed && Can.transform.position.y - _canStartY >= liftHeight;
                case 1: return StrapPulled >= strapPull;
                case 2: return Mathf.Abs(_twist) >= twistDegrees;
                case 3: return Tag != null && Tag.IsGrabbed;
                case 4: return CrateDragged >= dragDistance;
            }
            return false;
        }

        void Update()
        {
            if (Finished) return;
            float dt = Time.deltaTime;
            var h = Head;
            if (h != null && Panel != null && _root != null)
                Panel.Follow(_root.position + Vector3.up * 0.25f, h.position, _root.position, dt);
            if (GameSettings.KZ != _kz) { _kz = GameSettings.KZ; ShowStep(); }

            // strap: pulled away from the crate
            if (Strap != null && Strap.Hand != null)
            {
                StrapPulled = Mathf.Max(StrapPulled, Strap.PalmDelta.magnitude);
                HapticsProfile.Current.StrapPull(Strap.Input, Mathf.Clamp01(Strap.PalmDelta.magnitude / 0.15f));
                if (_strapVisual != null) _strapVisual.localScale = new Vector3(0.038f, 0.005f, 0.09f + Mathf.Min(0.12f, Strap.PalmDelta.magnitude));
            }
            // windlass: wrist rotation about the crate's up axis
            bool rodHeld = Rod != null && Rod.Hand != null;
            if (rodHeld)
            {
                var hr = Rod.Hand.transform.rotation;
                if (!_rodWasHeld) _rodPrev = hr;
                (hr * Quaternion.Inverse(_rodPrev)).ToAngleAxis(out float a, out Vector3 ax);
                if (a > 180f) a -= 360f;
                _rodPrev = hr;
                if (!float.IsNaN(ax.x)) _twist += a * Vector3.Dot(ax, Crate.transform.up);
                int clicks = (int)(Mathf.Abs(_twist) / 180f);
                if (clicks > _clicks) { _clicks = clicks; HapticsProfile.Current.WindlassClick(Rod.Input); }
                if (_rodVisual != null) _rodVisual.localRotation = Quaternion.Euler(0f, _twist, 0f);
            }
            _rodWasHeld = rodHeld;
            // two-hand drag
            bool two = Crate != null && Crate.HolderCount >= 2;
            if (two && !_crateTwoHands) _crateStart = Crate.transform.position;
            _crateTwoHands = two;
            if (two)
            {
                Vector3 d = Crate.transform.position - _crateStart; d.y = 0f;
                CrateDragged = Mathf.Max(CrateDragged, d.magnitude);
            }
            if (Tag != null && Tag.IsGrabbed)
            {
                var trb = Tag.GetComponent<Rigidbody>();
                if (trb != null && trb.isKinematic) trb.isKinematic = false;
            }

            // step logic: green check, then the next card
            if (_doneAt < 0f && StepSatisfied(Step)) { _doneAt = Time.time; Panel.SetDone(); }
            if (_doneAt >= 0f && Time.time - _doneAt > 0.9f)
            {
                Step++;
                if (Step >= Steps.Length) Finish(false);
                else ShowStep();
            }
        }

        bool _kz;

        public void Skip()
        {
            if (Finished) return;
            Skipped = true;
            Finish(true);
        }

        void Finish(bool skipped)
        {
            Finished = true;
            Done = true;
            Debug.Log($"[FMVR] tutorial: {(skipped ? "skipped" : "completed")} after {Time.time - StartedAt:F1} s at step {Step}");
            var then = _then;
            _then = null;
            Destroy(gameObject, 0.05f);
            then?.Invoke();
        }
    }
}
