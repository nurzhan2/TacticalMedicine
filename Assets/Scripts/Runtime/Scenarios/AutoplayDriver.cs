using FieldMedic.Casualty;
using FieldMedic.Medical;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// w10g (owner: "я не могу пройти, пусть бот пройдёт"): "Смотреть, как проходит бот". The game performs every coached
    /// step itself through the same skip-step path (<see cref="ScenarioDirector.SkipStep"/> → <see cref="StepAutoDo"/>),
    /// one step every <see cref="StepSeconds"/>, while the step panel says in plain words what is being done. The player
    /// only watches: nobody shoots at him, his rig is teleported (short fade) next to Erlan when the action moves away.
    /// Hold B = menu (the director's usual exit). The debrief is marked "автопрохождение".
    /// </summary>
    public sealed class AutoplayDriver : MonoBehaviour
    {
        public const float StepSeconds = 3f;
        public const float FollowDistance = 4f;
        public const float WatchDistance = 1.5f;

        public float stepSeconds = StepSeconds;
        public int StepsDone { get; private set; }
        public string LastStepId { get; private set; }

        ScenarioDirector _d;
        string _shownId;
        float _doAt;
        float _teleportAt = -1f;
        Vector3 _teleportTo;
        float _teleportYaw;

        public static AutoplayDriver Ensure(ScenarioDirector d)
        {
            var a = d.GetComponent<AutoplayDriver>();
            if (a == null) a = d.gameObject.AddComponent<AutoplayDriver>();
            a._d = d;
            return a;
        }

        /// <summary>Called by the director's LateUpdate after the coach picked the step; returns the step panel line.</summary>
        public void Tick()
        {
            if (_d == null) _d = GetComponent<ScenarioDirector>();
            if (_d == null || _d.InMenu || _d.State == ScenarioState.Debrief) return;
            FieldMedic.Battle.BattleDirector.PlayerArmed = false; // a spectator is never the target
            Follow();
            var step = _d.CurrentStep;
            if (_d.IncidentPatientTime < 0f || _d.casualty == null || _d.casualty.Protocol == null)
            {
                Say("Бот: ждём — сейчас Ерлана ранит. Смотри и запоминай порядок.", "Бот: күтеміз — қазір Ерланды жаралайды. Қарап, ретін есте сақта.");
                return;
            }
            if (step == null)
            {
                Say("Бот: ждём вертолёт.", "Бот: тікұшақты күтеміз.");
                return;
            }
            if (step.id != _shownId)
            {
                _shownId = step.id;
                _doAt = Time.time + stepSeconds;
            }
            var (ru, kz) = Describe(step);
            Say("Бот: " + ru, "Бот: " + kz);
            if (!step.skippable || Time.time < _doAt) return;
            _doAt = Time.time + stepSeconds; // the same step again later if it could not be done yet (helicopter inbound)
            if (_d.SkipStep()) { StepsDone++; LastStepId = step.id; _shownId = null; }
        }

        void Say(string ru, string kz) =>
            FieldMedic.UI.StepGuide.Set(ru + "   · B (держать) — меню", kz + "   · B (ұста) — мәзір", FieldMedic.UI.ControlIcon.B);

        /// <summary>Keep the watcher ~1.5 m from Erlan: when he is moved more than 4 m away, fade, teleport, fade in.</summary>
        void Follow()
        {
            if (_d.casualty == null || _d.playerRig == null) return;
            var fade = _d.testMode ? null : FieldMedic.Comfort.ComfortFade.Instance;
            if (_teleportAt > 0f)
            {
                if (Time.time < _teleportAt) return;
                _d.PlacePlayerAt(_teleportTo, _teleportYaw);
                _teleportAt = -1f;
                if (fade != null) fade.Fade = 0f;
                return;
            }
            Vector3 hips = Hips(_d.casualty);
            Vector3 eye = _d.head != null ? _d.head.position : _d.playerRig.position;
            Vector3 dir = eye - hips;
            dir.y = 0f;
            if (dir.magnitude <= FollowDistance) return;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.right;
            // the head sits off the rig origin: move the origin so the head lands WatchDistance from him
            Vector3 headOffset = eye - _d.playerRig.position;
            headOffset.y = 0f;
            _teleportTo = hips + dir * WatchDistance - headOffset;
            _teleportYaw = Quaternion.LookRotation(-dir).eulerAngles.y;
            if (fade != null) { fade.Fade = 1f; _teleportAt = Time.time + 0.45f; }
            else { _d.PlacePlayerAt(_teleportTo, _teleportYaw); }
        }

        static Vector3 Hips(CasualtyController c)
        {
            var h = c.rig != null ? c.rig.Bone(HumanBodyBones.Hips) : null;
            return h != null ? h.position : c.transform.position;
        }

        /// <summary>What the bot is doing, in plain words (falls back to the coach line).</summary>
        public static (string ru, string kz) Describe(CoachStep s)
        {
            switch (s.id)
            {
                case "reach": return ("подбегает к Ерлану", "Ерланға жүгіріп барады");
                case "tq_grab": case "tq_on_limb": case "tq_high": case "tq_strap": case "tq_twist": case "tq_lock":
                    return ("накладывает жгут «высоко и туго» над раной и закручивает вороток", "жгутты жарадан жоғары «жоғары әрі қатты» салып, бұрауышты бұрайды");
                case "drag_grab": case "drag_cover": return ("тащит Ерлана в укрытие", "Ерланды баспанаға сүйрейді");
                case "gloves": return ("в укрытии надевает перчатки", "баспанада қолғап киеді");
                case "ifak_open": return ("открывает аптечку", "қобдишаны ашады");
                case "sweep": return ("ощупывает всё тело — ищет другие раны (кровь на перчатках)", "бүкіл денені сипап, басқа жараны іздейді");
                case "rolled": return ("переворачивает на бок — ищет скрытую рану на спине", "бүйіріне аударып, арқадағы жасырын жараны іздейді");
                case "exposed": case "exposed2": return ("разрезает одежду над раной", "жараның үстіндегі киімді кеседі");
                case "gauze_grab": case "packed": case "packed2": return ("туго тампонирует рану гемостатической марлей", "жараны гемостатикалық дәкемен тығыз тампондайды");
                case "held": return ("давит на рану (в бою 3 минуты)", "жараны басып тұр (ұрыста 3 минут)");
                case "bandage": case "bandage2": case "bandage2_on": return ("накладывает давящую повязку", "қысатын таңғыш салады");
                case "seal_open": case "seal_front": case "seal3": return ("клеит окклюзионную наклейку на рану груди", "кеуде жарасына окклюзиялық жапсырма жапсырады");
                case "seal_back": return ("клеит наклейку на выходное отверстие на спине", "арқадағы шығу тесігіне жапсырма жапсырады");
                case "needle": return ("напряжённый пневмоторакс — делает декомпрессию иглой", "кернеулі пневмоторакс — инемен декомпрессия");
                case "npa_grab": case "npa_in": return ("вводит назофарингеальный воздуховод", "мұрын-жұтқыншақ ауа өткізгішін енгізеді");
                case "mouth": return ("очищает рот — рвота", "аузын тазалайды — құсық");
                case "recovery": return ("кладёт в восстановительное положение на бок", "қалпына келтіру қалпына — бүйіріне жатқызады");
                case "pulse_foot": return ("проверяет пульс ниже жгута — его быть не должно", "жгуттан төмен тамыр соғуын тексереді — болмауы керек");
                case "marker_grab": case "tq_time": return ("пишет время наложения жгута", "жгут салынған уақытты жазады");
                case "blanket": return ("укрывает спасательным одеялом — от переохлаждения", "құтқару көрпесімен жабады — суықтан");
                case "card": return ("заполняет карточку раненого", "жаралы карточкасын толтырады");
                case "medevac": return ("вызывает вертолёт по рации (MEDEVAC)", "рациямен тікұшақ шақырады (MEDEVAC)");
                case "loaded": case "heli_aboard": case "board": return ("тащит Ерлана к двери вертолёта", "Ерланды тікұшақ есігіне сүйрейді");
            }
            return (Lower(s.Text(false)), Lower(s.Text(true)));
        }

        static string Lower(string t) => string.IsNullOrEmpty(t) ? t : char.ToLowerInvariant(t[0]) + t.Substring(1);
    }
}
