namespace FieldMedic.Scenarios
{
    /// <summary>In-game wall clock: the night scenario starts at 01:40 (GDD §2); time = start + scenario seconds.</summary>
    public static class ScenarioClock
    {
        public static int StartHour = 1;
        public static int StartMinute = 40;

        public static string Format(float scenarioSeconds)
        {
            int total = StartHour * 60 + StartMinute + (int)(scenarioSeconds / 60f);
            total %= 24 * 60;
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
