using FieldMedic.Treatment;
using UnityEngine;

namespace FieldMedic.Interaction
{
    /// <summary>
    /// w10d (owner on Quest: "до сих пор маленькая зона взаимодействия"): interaction zones are BIG and forgiving
    /// everywhere (lessons, scenarios, self-aid) and keep their realistic strict sizes only in the Exam.
    /// Every trigger radius / dwell time that the player has to hit goes through <see cref="Scale"/>.
    /// </summary>
    public static class Forgiving
    {
        /// <summary>Tests: force the strict (exam) sizes (true) or the forgiving ones (false); null = follow the run.</summary>
        public static bool? Override;

        /// <summary>True unless the current run is an Exam.</summary>
        public static bool On
        {
            get
            {
                if (Override.HasValue) return !Override.Value;
                var d = FieldMedic.Scenarios.ScenarioDirector.Instance;
                return d == null || !d.Exam;
            }
        }

        /// <summary>The strict (exam) value in the exam, the forgiving one everywhere else.</summary>
        public static float Scale(float strictValue, float forgivingValue) => On ? forgivingValue : strictValue;

        /// <summary>Strict value times <paramref name="factor"/> outside the exam.</summary>
        public static float Mul(float strictValue, float factor) => On ? strictValue * factor : strictValue;
    }

    /// <summary>
    /// w10d (owner: "случайно нажимаешь на что-то другое — если требуется только одно определённое действие, ты можешь
    /// сделать только его"): outside the exam, while the current step needs one specific item, the hands only take that
    /// item (plus casualty handles, tools already held, kit / radio UI). The wanted item comes from
    /// <see cref="WantedItem"/> (set by scenario code) or the player's kit tray guide (<see cref="KitTray.Wanted"/>).
    /// Null wanted = everything allowed.
    /// </summary>
    public static class StepFocus
    {
        /// <summary>Item the current step needs (scenario code may set it; the lesson guide sets KitTray.Player.Wanted).</summary>
        public static MedItemKind? WantedItem;
        /// <summary>Master switch (default on): false = never restrict.</summary>
        public static bool Active = true;

        /// <summary>The item kind the hands are focused on now (null = no restriction).</summary>
        public static MedItemKind? Current
        {
            get
            {
                if (!Active || !Forgiving.On) return null;
                if (WantedItem.HasValue) return WantedItem;
                var tray = KitTray.Player;
                return tray != null ? tray.Wanted : null;
            }
        }

        /// <summary>Same family: any tourniquet satisfies a tourniquet step (CAT / Alpha / Esmarch); helper items of the
        /// step stay allowed (NPA + lubricant, chest seal + wipes, gauze / bandage + wipes, card + marker).</summary>
        public static bool Matches(MedItemKind wanted, MedItemKind kind)
        {
            if (wanted == kind) return true;
            if (IsTourniquet(wanted) && IsTourniquet(kind)) return true;
            switch (wanted)
            {
                case MedItemKind.NasalAirway: return kind == MedItemKind.Lubricant;
                case MedItemKind.ChestSeal: return kind == MedItemKind.AntisepticWipes;
                case MedItemKind.CasualtyCard: return kind == MedItemKind.Marker;
                case MedItemKind.Marker: return kind == MedItemKind.CasualtyCard;
            }
            return false;
        }

        static bool IsTourniquet(MedItemKind k) =>
            k == MedItemKind.Tourniquet || k == MedItemKind.AlphaTourniquet || k == MedItemKind.EsmarchTourniquet;

        static int _availFrame = -1;
        static MedItemKind _availKind;
        static bool _avail;

        /// <summary>Does a usable item of the wanted kind exist at all? (if not, never lock the hands out)</summary>
        static bool WantedExists(MedItemKind wanted)
        {
            if (_availFrame == Time.frameCount && _availKind == wanted) return _avail;
            _availFrame = Time.frameCount; _availKind = wanted; _avail = false;
            foreach (var it in Object.FindObjectsByType<MedItem>(FindObjectsSortMode.None))
                if (it != null && it.isActiveAndEnabled && !it.Consumed && Matches(wanted, it.kind)) { _avail = true; break; }
            return _avail;
        }

        /// <summary>May this grabbable be taken now? Non-items (limbs, drag handles, kit/radio UI) are always allowed.</summary>
        public static bool Allows(Grabbable g)
        {
            var wanted = Current;
            if (!wanted.HasValue || g == null) return true;
            var item = g as MedItem ?? g.GetComponentInParent<MedItem>();
            if (item == null) return true;
            if (item.IsGrabbed) return true; // already in a hand (second hand on a bandage / passing between hands)
            if (Matches(wanted.Value, item.kind)) return true;
            if (item.Attached) return true; // already on the casualty (band, seal...): adjusting it is never a "wrong item"
            return !WantedExists(wanted.Value);
        }

        /// <summary>Zone checks: is the wanted kind (if any) this one?</summary>
        public static bool AllowsKind(MedItemKind kind)
        {
            var wanted = Current;
            return !wanted.HasValue || Matches(wanted.Value, kind) || !WantedExists(wanted.Value);
        }
    }
}
