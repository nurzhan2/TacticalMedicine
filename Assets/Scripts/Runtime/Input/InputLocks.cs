namespace FieldMedic.Input
{
    /// <summary>
    /// w10: controller inputs temporarily owned by a UI (the radio panel steps through its lines with the right stick,
    /// so teleport aiming must not start at the same time).
    /// </summary>
    public static class InputLocks
    {
        public static bool RightStickCaptured;
    }
}
