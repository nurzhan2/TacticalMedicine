using System;
using System.Linq;
using FieldMedic.Casualty;
using FieldMedic.Casualty.Blood;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using UnityEngine;

namespace FieldMedic.Scenarios
{
    /// <summary>
    /// w10b (owner: "сделай кнопку, чтобы пропустить шаг, чтобы всё само сделалось"): outside the exam, hold A (right
    /// controller primary button) for <see cref="HoldSeconds"/> — or press «ПРОПУСК» on the watch — and the current step
    /// completes by itself. The running director registers <see cref="Handler"/> (lesson / scenario / self-aid); the
    /// fill progress is read by the step line (<see cref="FieldMedic.UI.StepGuide"/>). Never in the exam.
    /// </summary>
    public sealed class StepSkip : MonoBehaviour
    {
        public const float HoldSeconds = 1.5f;
        public static StepSkip Instance { get; private set; }

        /// <summary>Performs the current step; returns true when something was done. Set by the active director.</summary>
        public static Func<bool> Handler;
        /// <summary>Is there a skippable step right now (null = yes whenever a handler exists).</summary>
        public static Func<bool> CanSkip;
        /// <summary>0..1 while A is held (the fill ring on the step panel).</summary>
        public static float Progress01 { get; private set; }
        public static int SkipCount { get; private set; }
        public static float LastSkipAt { get; private set; } = -100f;

        /// <summary>Skip is offered now (not in the exam, a handler is registered and the step can be skipped).</summary>
        public static bool Available
        {
            get
            {
                if (Handler == null) return false;
                var d = ScenarioDirector.Instance;
                if (d != null && (d.Exam || d.InMenu)) return false;
                return CanSkip == null || CanSkip();
            }
        }

        float _held;
        bool _fired;

        public static StepSkip Ensure(GameObject host)
        {
            if (Instance != null) return Instance;
            var s = host.GetComponent<StepSkip>();
            return s != null ? s : host.AddComponent<StepSkip>();
        }

        public static void Register(Func<bool> handler, Func<bool> canSkip)
        {
            Handler = handler;
            CanSkip = canSkip;
            Progress01 = 0f;
        }

        public static void Unregister(Func<bool> handler)
        {
            if (Handler != handler) return;
            Handler = null;
            CanSkip = null;
            Progress01 = 0f;
        }

        void Awake() => Instance = this;
        void OnDestroy()
        {
            if (Instance == this) { Instance = null; Handler = null; CanSkip = null; Progress01 = 0f; }
        }

        void Update()
        {
            var right = HandRig.Find(Handedness.Right);
            bool a = right != null && right.PrimaryButton;
            if (!a || !Available) { _held = 0f; _fired = false; Progress01 = 0f; return; }
            if (_fired) { Progress01 = 0f; return; }
            float before = _held;
            _held += Time.unscaledDeltaTime;
            Progress01 = Mathf.Clamp01(_held / HoldSeconds);
            if (before < 0.05f && _held >= 0.05f && right != null) Haptics.Play(right, Haptics.Hover);
            if (_held >= HoldSeconds)
            {
                _fired = true;
                Progress01 = 0f;
                if (Request() && right != null) Haptics.Play(right, Haptics.PressClick);
            }
        }

        /// <summary>Skip the current step now (watch button, desktop, tests). False in the exam / nothing to skip.</summary>
        public static bool Request()
        {
            if (!Available) return false;
            bool ok;
            try { ok = Handler(); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            if (ok) { SkipCount++; LastSkipAt = Time.time; }
            Debug.Log("[FMVR] skip step: " + (ok ? "done" : "nothing to do"));
            return ok;
        }
    }

    /// <summary>
    /// w10b: performs one lesson / scenario step for the player (the "skip step" button). The item is applied at once
    /// (a CAT is put on high and tight, gauze packed and held, dressings/seals/NPA applied, the casualty moved into cover
    /// or to the helicopter door), through the same protocol calls the hands use, so the physiology and the debrief
    /// follow. Step ids are the lesson vocabulary shared with <see cref="ScenarioCoach"/>.
    /// </summary>
    public static class StepAutoDo
    {
        /// <summary>Steps nothing can be done for (waiting for something).</summary>
        public static bool IsWaitStep(string id) => id is "wait" or "tension" or "heli_landed";

        public static bool Do(string id, CasualtyController c, Wound w, ScenarioDirector d)
        {
            if (c == null || string.IsNullOrEmpty(id) || IsWaitStep(id)) return false;
            c.EnsurePatient();
            var proto = c.Protocol;
            var p = c.Patient;
            if (proto == null || p == null) return false;
            switch (id)
            {
                case "reach":
                    PlayerNear(c, d);
                    return true;

                case "tq_grab": case "tq_on_limb": case "tq_high": case "tq_strap": case "tq_twist": case "tq_lock":
                    return Tourniquet(c, w);

                case "marker_grab": case "tq_time":
                {
                    var cat = CatOn(c, locked: true);
                    if (cat == null) { if (!Tourniquet(c, w)) return false; cat = CatOn(c, locked: true); }
                    if (cat == null) return false;
                    if (cat.State.TimeMarked) return true;
                    if (cat.WriteTime()) return true;
                    proto.Perform(MedAction.MarkTourniquetTime);
                    return true;
                }

                case "pulse_foot":
                {
                    var cat = CatOn(c, locked: true);
                    string limb = cat != null ? cat.State.limbId : w != null ? w.LimbId : "legR";
                    bool leg = LimbGeometry.IsLeg(limb);
                    bool left = limb != null && limb.EndsWith("L");
                    var site = leg ? (left ? BodyRegion.FootL : BodyRegion.FootR) : (left ? BodyRegion.ForearmL : BodyRegion.ForearmR);
                    proto.Perform(MedAction.PulseCheck, site: site);
                    return true;
                }

                case "gloves":
                    proto.Perform(MedAction.Gloves);
                    return true;

                case "ifak_open":
                {
                    var tray = KitTray.Player;
                    if (tray != null && !tray.IsOpen) tray.Open();
                    return true;
                }

                case "exposed": case "exposed2":
                    w ??= FirstWound(p, x => x.hiddenUnderClothing && !x.exposed);
                    if (w == null) return false;
                    Expose(c, w);
                    return true;

                case "gauze_grab": case "packed": case "packed2":
                    w ??= FirstWound(p, x => x.IsLifeThreatening && !x.IsChest && !x.packed);
                    if (w == null) return false;
                    Expose(c, w);
                    if (!w.packed) proto.Perform(MedAction.HemostaticPacking, w);
                    return true;

                case "held":
                    w ??= FirstWound(p, x => x.control == BleedControl.Packed) ?? FirstWound(p, x => x.IsLifeThreatening && !x.IsChest);
                    if (w == null) return false;
                    Expose(c, w);
                    if (!w.packed) proto.Perform(MedAction.HemostaticPacking, w);
                    if (w.control == BleedControl.Packed) proto.HoldPressure(w, Wound.PackingHoldRequired + 1f);
                    return true;

                case "bandage": case "bandage2": case "bandage2_on":
                    w ??= FirstWound(p, x => !x.IsChest && !x.IsControlled && x.control != BleedControl.Tourniquet);
                    if (w == null) return false;
                    Expose(c, w);
                    if (w.control == BleedControl.Packed) proto.HoldPressure(w, Wound.PackingHoldRequired + 1f);
                    proto.Perform(MedAction.PressureDressing, w);
                    return true;

                case "seal_open": case "seal_front": case "seal3": case "seal_back":
                    w ??= FirstWound(p, x => x.IsChest && x.control != BleedControl.OcclusiveSeal && x.posterior == (id == "seal_back"))
                          ?? FirstWound(p, x => x.IsChest && x.control != BleedControl.OcclusiveSeal);
                    if (w == null) return false;
                    Expose(c, w);
                    proto.Perform(MedAction.ChestSeal, w);
                    return true;

                case "needle":
                    if (p.pneumo != PneumoState.Tension) return false;
                    proto.Perform(MedAction.NeedleDecompression);
                    return true;

                case "npa_grab": case "npa_in":
                    proto.Perform(MedAction.NasalAirway);
                    return true;

                case "mouth":
                    proto.Perform(MedAction.ClearMouth);
                    return true;

                case "sweep":
                    proto.Perform(MedAction.BloodSweep);
                    foreach (var x in p.wounds.Where(x => !x.discovered).ToList()) proto.Discover(x);
                    return true;

                case "rolled":
                    proto.Perform(MedAction.LogRoll);
                    return true;

                case "recovery":
                    c.rolledOnSide = true;
                    c.recoveryPosition = true; // w10g: LogRoll resets rolledOnSide from its own angle every frame
                    proto.Perform(MedAction.RecoveryPosition);
                    return true;

                case "blanket":
                    proto.Perform(MedAction.ThermalBlanket);
                    return true;

                case "card":
                    proto.Perform(MedAction.CasualtyCard);
                    return true;

                case "medevac":
                    proto.Perform(MedAction.MedevacRequest);
                    return true;

                case "drag_grab": case "drag_cover":
                    return IntoCover(c, d);

                case "loaded": case "heli_aboard": case "board":
                    return Evac(c, d);
            }
            return false;
        }

        // ------------------------------------------------------------------ helpers
        static Wound FirstWound(PatientPhysiology p, Func<Wound, bool> f) => p.wounds.FirstOrDefault(f);

        static void Expose(CasualtyController c, Wound w)
        {
            w.discovered = true;
            if (!w.hiddenUnderClothing || w.exposed) return;
            w.exposed = true;
            var ci = c.Interaction != null ? c.Interaction : c.EnsureInteraction();
            if (ci != null) ci.Expose(w.region);
        }

        static Limb LimbOf(string limbId) => limbId switch
        {
            "legL" => Limb.LeftLeg, "armL" => Limb.LeftArm, "armR" => Limb.RightArm, _ => Limb.RightLeg,
        };

        static TourniquetCAT CatOn(CasualtyController c, bool locked) =>
            UnityEngine.Object.FindObjectsByType<TourniquetCAT>(FindObjectsSortMode.None)
                .FirstOrDefault(t => t != null && t.Casualty == c && t.State.OnLimb && (!locked || t.State.Locked));

        /// <summary>A CAT high and tight on the bleeding limb (an existing band on that limb is set right; otherwise a
        /// free CAT — the player's own kit first — is put on).</summary>
        static bool Tourniquet(CasualtyController c, Wound w)
        {
            var p = c.Patient;
            w ??= p.wounds.Where(x => x.IsLifeThreatening && x.IsLimb && !x.IsJunctionalRegion && !x.IsControlled).OrderBy(x => x.LimbPos).FirstOrDefault()
                  ?? p.wounds.FirstOrDefault(x => x.IsLimb && !x.IsJunctionalRegion);
            if (w == null || w.LimbId == null) return false;
            string limbId = w.LimbId;
            const float High = 0.12f;
            float now = p.secondsSinceStart;
            var all = UnityEngine.Object.FindObjectsByType<TourniquetCAT>(FindObjectsSortMode.None).Where(t => t != null).ToList();
            var on = all.FirstOrDefault(t => t.Casualty == c && t.State.OnLimb && t.State.limbId == limbId);
            if (on != null)
            {
                on.ForceRelease();
                bool marked = on.State.TimeMarked;
                float at = on.State.appliedAtSeconds >= 0f ? on.State.appliedAtSeconds : now;
                float pos = on.State.limbPosition < w.LimbPos - 0.05f && !on.State.OnJoint ? on.State.limbPosition : High;
                on.State.PreApply(limbId, pos, 5, marked, at);
                p.AddTourniquet(on.State);
                c.Protocol.TourniquetApplied(on.State);
                return true;
            }
            var pouch = KitTray.Player != null ? KitTray.Player.pouch : null;
            var free = all.Where(t => !t.Consumed && !t.State.OnLimb && t.Casualty == null)
                .OrderBy(t => pouch != null && t.transform.IsChildOf(pouch.transform) ? 0 : 1)
                .ThenBy(t => t.IsHeld ? 0 : 1)
                .FirstOrDefault();
            if (free == null) return false;
            free.ForceRelease();
            free.PreApply(c, LimbOf(limbId), High, 5, false, now);
            if (!free.State.OnLimb) return false;
            c.Protocol.TourniquetApplied(free.State);
            return true;
        }

        static Vector3 Hips(CasualtyController c)
        {
            var h = c.rig != null ? c.rig.Bone(HumanBodyBones.Hips) : null;
            return h != null ? h.position : c.transform.position;
        }

        static void MoveCasualty(CasualtyController c, Vector3 hipsTarget)
        {
            Vector3 delta = hipsTarget - Hips(c);
            delta.y = 0f;
            c.transform.position += delta;
        }

        static void PlayerNear(CasualtyController c, ScenarioDirector d)
        {
            if (d == null) return;
            Vector3 hips = Hips(c);
            Vector3 dir = d.head != null ? d.head.position - hips : Vector3.right;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.right;
            dir.Normalize();
            Vector3 at = hips + dir * 0.9f;
            d.PlacePlayerAt(at, Quaternion.LookRotation(-dir).eulerAngles.y);
        }

        static bool IntoCover(CasualtyController c, ScenarioDirector d)
        {
            var cz = UnityEngine.Object.FindAnyObjectByType<CoverZone>();
            if (cz != null)
            {
                var drag = c.Interaction != null ? c.Interaction.Drag : null;
                if (drag != null) foreach (var h in drag.strapHandles) if (h != null) h.ForceRelease();
                MoveCasualty(c, cz.transform.position);
                PlayerNear(c, d);
            }
            if (c.Protocol.phase == Phase.CareUnderFire) c.Protocol.Perform(MedAction.MoveToCover);
            return true;
        }

        static bool Evac(CasualtyController c, ScenarioDirector d)
        {
            var tac = TacevacController.Instance;
            if (tac == null)
            {
                if (!c.Protocol.medevacRequested) c.Protocol.Perform(MedAction.MedevacRequest);
                return true;
            }
            if (!tac.Called) { tac.Call(d != null && d.testMode); return true; }
            if (!tac.Landed) return false; // still inbound: nothing to do by hand
            if (!tac.CasualtyAboard)
            {
                var drag = c.Interaction != null ? c.Interaction.Drag : null;
                if (drag != null) foreach (var h in drag.strapHandles) if (h != null) h.ForceRelease();
                Vector3 chest = c.rig != null && c.rig.ChestBone != null ? c.rig.ChestBone.position : Hips(c);
                Vector3 delta = tac.DoorPoint - chest;
                delta.y = 0f;
                c.transform.position += delta; // the crew takes him in once his chest is at the door
                return true;
            }
            if (!tac.Loaded) { tac.Board(); return true; }
            return false;
        }
    }
}
