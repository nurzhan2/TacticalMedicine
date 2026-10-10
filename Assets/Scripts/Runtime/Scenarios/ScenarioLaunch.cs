using FieldMedic.Medical;

namespace FieldMedic.Scenarios
{
    public enum LaunchMode { Menu, Scenario, Exam, Training }

    /// <summary>
    /// What to run after the (re)load of the main scene. Every run starts from a fresh scene so no blood pool,
    /// moved item or tourniquet leaks from one run into the next; this static survives the reload.
    /// </summary>
    public static class ScenarioLaunch
    {
        public sealed class Request
        {
            public LaunchMode mode;
            public string scenarioId;
            public ScenarioVariant variant;
            public string lessonId;
            /// Debrief of the previous run to show on the board when returning to the menu.
            public DebriefReport lastReport;
        }

        public static Request Pending;

        public static Request Consume()
        {
            var r = Pending;
            Pending = null;
            return r;
        }

        public static void Scenario(string id, ScenarioVariant v) => Pending = new Request { mode = LaunchMode.Scenario, scenarioId = id, variant = v };

        public static void Exam(int seed)
        {
            var r = new System.Random(seed);
            var all = ScenarioCatalog.All;
            Pending = new Request { mode = LaunchMode.Exam, scenarioId = all[r.Next(all.Count)].id, variant = ScenarioVariant.Random(seed) };
        }

        public static void Training(string lesson) => Pending = new Request { mode = LaunchMode.Training, lessonId = lesson };
        public static void Menu(DebriefReport report = null) => Pending = new Request { mode = LaunchMode.Menu, lastReport = report };
    }
}
