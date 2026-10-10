using System.Collections.Generic;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Environment;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// Training mode (GDD §3): one skill per lesson, no shelling, slow physiology, the 3-minute hold accelerated.
    /// A voice instructor (TTS placeholders, RU/KZ) announces each step; a checklist panel and the instructor-only
    /// vitals monitor float next to the casualty; a ghost glove repeatedly shows the motion from the item to the
    /// target and an arrow marks the target. Steps complete only by doing them with the hands.
    /// </summary>
    public sealed class TrainingDirector : MonoBehaviour
    {
        public static TrainingDirector Instance { get; private set; }
        public ScenarioDirector director;
        public LessonProgress Progress { get; private set; }
        public TrainingLesson Lesson => Progress?.lesson;
        public float slowPhysiology = 0.25f;
        public bool muted;

        CasualtyController C => director.casualty;
        Wound _wound, _exit;
        TextMesh _list, _vitals, _title;
        Transform _panel, _arrow, _ghost;
        AudioSource _voice;
        float _t;

        public static TrainingDirector Ensure(ScenarioDirector d)
        {
            var t = d.GetComponent<TrainingDirector>() ?? d.gameObject.AddComponent<TrainingDirector>();
            t.director = d;
            return t;
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Begin(string lessonId)
        {
            var l = TrainingCatalog.Get(lessonId) ?? TrainingCatalog.All[0];
            Progress = new LessonProgress(l);
            Progress.Advanced += OnAdvanced;
            GameSettings.HoldTimeScale = 6f;
            muted |= director.testMode;
            if (CombatAmbience.Instance != null) CombatAmbience.Instance.intensity = 0.25f;
            if (ThreatSystem.Instance != null) { ThreatSystem.Instance.underFire = false; ThreatSystem.Instance.enableHits = false; }

            var spot = director.spots.FirstOrDefault(s => s.spot == CasualtySpot.FireBay);
            if (spot != null && spot.casualty != null) C.transform.SetPositionAndRotation(spot.casualty.position, spot.casualty.rotation);
            if (spot != null && spot.player != null && director.playerRig != null)
                director.playerRig.position = new Vector3(spot.player.position.x, director.playerRig.position.y, spot.player.position.z);

            C.EnsurePatient();
            var site = C.AddWound(l.woundType, l.region, ScenarioDirector.Visual(l.visual), "lesson");
            _wound = site.Wound;
            if (l.id == "chest")
            {
                var ex = C.AddWound(WoundType.PenetratingChest, l.region, WoundVisual.ExitWound, "lesson_exit", true);
                _exit = ex.Wound;
                _exit.posterior = true;
            }
            if (l.unconscious) C.Patient.tbiUnconscious = true;
            if (l.id != "drag") _wound.discovered = true;
            C.StartDown();
            C.Protocol.phase = l.id == "drag" ? Phase.CareUnderFire : Phase.TacticalFieldCare;
            C.simulationSpeed = slowPhysiology;
            BuildPanel();
            Say("t_welcome");
            Invoke(nameof(SayCurrent), 3.5f);
            Debug.Log("[FMVR] training: lesson " + l.id);
        }

        void SayCurrent() { if (Progress != null && Progress.Step != null) Say(Progress.Step.voice); }

        void OnAdvanced(LessonStep s)
        {
            UiAudio.Click(C.transform.position);
            Debug.Log("[FMVR] training: step done " + s.id);
            if (Progress.Complete) { Say("t_done"); C.Protocol.Note("lesson_done", GameSettings.T("Урок выполнен.", "Сабақ орындалды."), Severity.Ok); }
            else if (!string.IsNullOrEmpty(Progress.Step.voice)) Say(Progress.Step.voice);
        }

        void Say(string id)
        {
            if (muted || string.IsNullOrEmpty(id)) return;
            if (_voice == null)
            {
                _voice = gameObject.AddComponent<AudioSource>();
                _voice.spatialBlend = 0f;
                _voice.volume = 0.9f;
            }
            var clip = Resources.Load<AudioClip>("Radio/" + id + (GameSettings.KZ ? "_kz" : "_ru"));
            if (clip == null) return;
            _voice.Stop();
            _voice.clip = clip;
            _voice.Play();
        }

        // ------------------------------------------------------------------ checks
        TourniquetCAT Cat => FindObjectsByType<TourniquetCAT>(FindObjectsSortMode.None).OrderByDescending(c => c.State.stage).FirstOrDefault();

        public bool Satisfied(string id)
        {
            var p = C.Patient;
            var cat = Cat;
            switch (id)
            {
                case "tq_grab": return cat != null && (cat.IsHeld || cat.State.OnLimb);
                case "tq_on_limb": return cat != null && cat.State.OnLimb;
                case "tq_high": return cat != null && cat.State.OnLimb && !cat.State.OnJoint && cat.State.limbPosition < _wound.LimbPos - 0.05f;
                case "tq_strap": return cat != null && cat.State.stage >= TourniquetStage.StrapPulled;
                case "tq_twist": return _wound.IsControlled;
                case "tq_lock": return cat != null && cat.State.Secured;
                case "tq_time": return cat != null && cat.State.TimeMarked;
                case "pulse_foot": return C.Protocol.log.Any(e => e.code == "pulse" && (e.target == "FootR" || e.target == "FootL"));
                case "gloves": return C.Protocol.glovesOn;
                case "exposed": return _wound.exposed;
                case "packed": return _wound.packed;
                case "held": return _wound.control is BleedControl.PackedAndHeld or BleedControl.PressureDressing;
                case "bandage": return _wound.control == BleedControl.PressureDressing;
                case "seal_front": return _wound.control == BleedControl.OcclusiveSeal;
                case "rolled": return C.rolledOnSide || C.Protocol.log.Any(e => e.code.StartsWith("log_roll"));
                case "seal_back": return _exit != null && _exit.control == BleedControl.OcclusiveSeal;
                case "tension": return p.pneumo is PneumoState.Tension or PneumoState.Decompressed;
                case "needle": return p.pneumo == PneumoState.Decompressed;
                case "drag_grab": return C.Interaction != null && C.Interaction.Drag != null && (C.Interaction.Drag.Hand != null || C.Interaction.Drag.DraggedDistance > 0.3f);
                case "drag_cover": return C.Protocol.phase != Phase.CareUnderFire;
                case "sweep": return _wound.discovered;
                case "npa_grab": return FindObjectsByType<NpaItem>(FindObjectsSortMode.None).Any(n => n.IsHeld || n.Attached);
                case "npa_in": return p.airwayAdjunct;
                case "recovery": return C.rolledOnSide;
            }
            return false;
        }

        /// <summary>Where the ghost hand / arrow should point for a step (item → target).</summary>
        public bool Guide(string id, out Vector3 from, out Vector3 to)
        {
            from = to = Vector3.zero;
            var rig = C.rig;
            if (rig == null) return false;
            var cat = Cat;
            var site = C.WoundSites.FirstOrDefault(s => s != null && s.Wound == _wound);
            Vector3 wound = site != null ? site.transform.position : C.transform.position;
            Vector3 hand = HandGrabber.All.Count > 0 && HandGrabber.All[0] != null ? HandGrabber.All[0].PalmPosition : wound + Vector3.up * 0.5f;
            Vector3 Item<T>() where T : MedItem
            {
                var it = FindObjectsByType<T>(FindObjectsSortMode.None).FirstOrDefault(x => !x.Attached && !x.Consumed);
                return it != null ? it.transform.position : hand;
            }
            Vector3 foot = rig.Bone(HumanBodyBones.RightFoot) != null ? rig.Bone(HumanBodyBones.RightFoot).position : wound;
            switch (id)
            {
                case "tq_grab": from = hand; to = cat != null ? cat.transform.position : wound; return true;
                case "tq_on_limb": from = cat != null ? cat.transform.position : hand; to = foot; return true;
                case "tq_high": from = cat != null ? cat.transform.position : wound; to = LimbMath.PointAt(rig, Limb.RightLeg, 0.12f); return true;
                case "tq_strap": if (cat == null || cat.StrapHandle == null) return false; from = cat.StrapHandle.transform.position; to = from + (from - cat.transform.position).normalized * 0.18f; return true;
                case "tq_twist":
                case "tq_lock": if (cat == null || cat.RodHandle == null) return false; from = to = cat.RodHandle.transform.position; return true;
                case "tq_time": if (cat == null) return false; from = Item<MarkerItem>(); to = cat.TimeLabelPoint; return true;
                case "pulse_foot": from = hand; to = HandTreatment.PulsePoint(rig, PulseSite.PedalR); return true;
                case "gloves": from = hand; to = Item<GlovesItem>(); return true;
                case "exposed": from = Item<ShearsItem>(); to = wound; return true;
                case "packed": from = Item<GauzeItem>(); to = wound; return true;
                case "held": from = hand; to = wound; return true;
                case "bandage": from = Item<BandageItem>(); to = wound; return true;
                case "seal_front": from = Item<ChestSealItem>(); to = wound; return true;
                case "seal_back":
                    var ex = C.WoundSites.FirstOrDefault(s => s != null && s.Wound == _exit);
                    from = Item<ChestSealItem>(); to = ex != null ? ex.transform.position : wound; return true;
                case "needle": from = Item<NeedleItem>(); to = rig.ChestBone.position + rig.BodyRight * 0.15f; return true;
                case "drag_grab": from = hand; to = C.Interaction.Drag.transform.position; return true;
                case "drag_cover":
                    var cz = FindAnyObjectByType<CoverZone>();
                    from = C.Interaction.Drag.transform.position; to = cz != null ? cz.transform.position : from; return true;
                case "rolled":
                case "recovery":
                    from = rig.Bone(HumanBodyBones.RightUpperArm).position; to = from + Vector3.up * 0.3f; return true;
                case "sweep": from = rig.ChestBone.position; to = wound; return true;
                case "npa_grab": from = hand; to = Item<NpaItem>(); return true;
                case "npa_in": from = Item<NpaItem>(); to = rig.Bone(HumanBodyBones.Head).position; return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ panel / ghost
        void BuildPanel()
        {
            _panel = new GameObject("TrainingPanel").transform;
            var hips = C.rig != null && C.rig.Bone(HumanBodyBones.Hips) != null ? C.rig.Bone(HumanBodyBones.Hips).position : C.transform.position;
            Vector3 viewer = director.head != null ? director.head.position : hips + Vector3.left;
            Vector3 toV = viewer - hips; toV.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toV.normalized);
            _panel.position = hips + side * 0.9f + Vector3.up * 1.15f;
            Vector3 face = _panel.position - viewer; face.y = 0f;
            _panel.rotation = Quaternion.LookRotation(face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward);
            WorldUI.Quad(_panel, "Bg", new Vector3(0f, 0f, 0.01f), new Vector2(0.62f, 0.5f), WorldUI.Unlit(new Color(0.02f, 0.03f, 0.025f)));
            _title = WorldUI.Text(_panel, "Title", GameSettings.T("ОБУЧЕНИЕ: ", "ОҚЫТУ: ") + Lesson.Title(GameSettings.KZ), new Vector3(-0.29f, 0.23f, 0f), 0.02f, new Color(1f, 0.8f, 0.4f));
            _list = WorldUI.Text(_panel, "Steps", "", new Vector3(-0.29f, 0.19f, 0f), 0.0125f, Color.white);
            _vitals = WorldUI.Text(_panel, "Vitals", "", new Vector3(-0.29f, -0.13f, 0f), 0.012f, new Color(0.5f, 1f, 0.6f));
            var ag = new GameObject("GuideArrow");
            _arrow = ag.transform;
            var am = WorldUI.Unlit(new Color(1f, 0.7f, 0.2f));
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(_arrow, false); shaft.transform.localPosition = new Vector3(0f, 0.07f, 0f); shaft.transform.localScale = new Vector3(0.012f, 0.045f, 0.012f);
            shaft.GetComponent<Renderer>().sharedMaterial = am;
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(tip.GetComponent<Collider>());
            tip.transform.SetParent(_arrow, false); tip.transform.localPosition = new Vector3(0f, 0.012f, 0f); tip.transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);
            tip.GetComponent<Renderer>().sharedMaterial = am;
            _ghost = new GameObject("GhostHand").transform;
            var gm = WorldUI.Unlit(new Color(0.35f, 0.75f, 1f));
            WorldUI.Box(_ghost, "Palm", new Vector3(0f, 0f, 0.035f), new Vector3(0.082f, 0.028f, 0.09f), gm);
            for (int i = 0; i < 4; i++) WorldUI.Box(_ghost, "F", new Vector3(-0.03f + i * 0.02f, -0.006f, 0.11f), new Vector3(0.016f, 0.016f, 0.065f), gm);
            WorldUI.Box(_ghost, "Thumb", new Vector3(-0.05f, -0.008f, 0.05f), new Vector3(0.018f, 0.018f, 0.05f), gm);
        }

        void Update()
        {
            if (Progress == null || C == null || C.Patient == null) return;
            _t += Time.deltaTime;
            if (!Progress.Complete) Progress.Update(Satisfied, C.Patient.secondsSinceStart);
            // chest lesson: let tension develop quickly once both seals are on
            C.simulationSpeed = Progress.Step != null && Progress.Step.id == "tension" ? 8f : slowPhysiology;
            RefreshPanel();
            UpdateGuide();
        }

        void RefreshPanel()
        {
            if (_list == null) return;
            bool kz = GameSettings.KZ;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Lesson.steps.Count; i++)
            {
                var s = Lesson.steps[i];
                string mark = i < Progress.Current ? "<color=#7CF27C>■</color>" : i == Progress.Current ? "<color=#FFC860>►</color>" : "<color=#777777>□</color>";
                string col = i < Progress.Current ? "#9FCF9F" : i == Progress.Current ? "#FFFFFF" : "#888888";
                sb.Append(mark).Append(" <color=").Append(col).Append('>').Append(s.Text(kz)).Append("</color>\n");
            }
            if (Progress.Complete) sb.Append("\n<color=#7CF27C>").Append(GameSettings.T("УРОК ВЫПОЛНЕН", "САБАҚ ОРЫНДАЛДЫ")).Append("</color>");
            _list.text = sb.ToString();
            var p = C.Patient;
            _vitals.text = GameSettings.T("МОНИТОР ИНСТРУКТОРА", "НҰСҚАУШЫ МОНИТОРЫ") + "\n" +
                           $"ЧСС {p.HeartRate:0}   АД {p.SystolicBP:0}/{p.DiastolicBP:0}   SpO2 {p.SpO2:0}%\n" +
                           $"ЧДД {p.RespRate:0}   {GameSettings.T("кровопотеря", "қан жоғалту")} {p.LossFraction * 100f:0}%   AVPU {p.AVPU.ToString()[0]}   t {p.coreTempC:0.0}°";
        }

        void UpdateGuide()
        {
            bool show = GameSettings.Hints && Progress.Step != null && Guide(Progress.Step.id, out var from, out var to);
            _arrow.gameObject.SetActive(show);
            _ghost.gameObject.SetActive(show);
            if (!show) return;
            Guide(Progress.Step.id, out var a, out var b);
            _arrow.position = b + Vector3.up * (0.06f + 0.02f * Mathf.Sin(_t * 5f));
            float k = Mathf.Repeat(_t / 1.6f, 1f);
            float e = k < 0.8f ? Mathf.SmoothStep(0f, 1f, k / 0.8f) : 1f;
            _ghost.position = Vector3.Lerp(a, b, e) + Vector3.up * (0.06f * Mathf.Sin(e * Mathf.PI));
            Vector3 dir = b - a; dir.y = 0f;
            _ghost.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward) * Quaternion.Euler(10f, 0f, 0f);
            _ghost.localScale = Vector3.one * (k < 0.9f ? 1f : Mathf.Lerp(1f, 0.2f, (k - 0.9f) / 0.1f));
        }
    }
}
