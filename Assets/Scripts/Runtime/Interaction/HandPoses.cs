using FieldMedic.Casualty;
using FieldMedic.Treatment;

namespace FieldMedic.Interaction
{
    /// <summary>Which <see cref="HandPose"/> a grabbable uses (by item kind / handle type / handle name).</summary>
    public static class HandPoses
    {
        public static string KindKey(MedItemKind k) => k switch
        {
            MedItemKind.Tourniquet => "cat",
            MedItemKind.HemostaticGauze => "gauze",
            MedItemKind.IsraeliBandage => "bandage",
            MedItemKind.ChestSeal => "seal",
            MedItemKind.NasalAirway => "npa",
            MedItemKind.Lubricant => "lube",
            MedItemKind.DecompressionNeedle => "needle",
            MedItemKind.TraumaShears => "shears",
            MedItemKind.NitrileGloves => "gloves",
            MedItemKind.Marker => "marker",
            MedItemKind.ThermalBlanket => "blanket",
            MedItemKind.CasualtyCard => "card",
            _ => null,
        };

        public static string KeyFor(Grabbable g)
        {
            if (g == null) return null;
            if (!string.IsNullOrEmpty(g.poseKey)) return g.poseKey;
            switch (g)
            {
                case MedItem m: return KindKey(m.kind);
                case CasualtyDrag _: return "drag_handle";
                case LimbHandle lh: return lh.grip == LimbHandle.Grip.Wrist ? "wrist" : lh.grip == LimbHandle.Grip.Ankle ? "ankle" : "knee";
                case FieldMedic.Battle.SmokeGrenade _: return "grenade";
                case FieldMedic.Battle.SmokePin _: return "ring";
            }
            string n = g.name;
            if (n == "Grab_StrapL" || n == "Grab_StrapR") return "drag_handle";  // plate-carrier shoulder straps
            if (n == "Grab_Windlass") return "cat_rod";
            if (n.StartsWith("Grab_") && n.Contains("Strap")) return "cat_strap"; // CAT strap / time strap
            if (n.Contains("Tab") || n.Contains("Notch")) return "tab";
            if (n.StartsWith("Roll_Shoulder")) return "shoulder";
            if (n.StartsWith("Roll_Hip")) return "knee";
            return null;
        }

        public static HandPose For(Grabbable g) => g != null ? g.Pose : null;

        public static GrabStyle StyleFor(Grabbable g) => g != null ? g.Style : GrabStyle.Any;
    }
}
