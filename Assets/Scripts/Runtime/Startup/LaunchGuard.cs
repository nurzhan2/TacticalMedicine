using System;
using System.IO;

namespace FieldMedic.Startup
{
    /// <summary>
    /// Crash flag + SAFE mode decision (w0b). Every launch writes "started" into
    /// <c>persistentDataPath/fmvr_state.txt</c>; it becomes "core" when Main_Core is up and "ready" once every additive
    /// scene is loaded and the game has rendered for a while. If the previous launch never reached "ready" (black
    /// screen, crash, OS kill during loading) the next launch runs in SAFE mode: Main_Core only, no battle scenes.
    /// A file <c>fmvr_force_full.txt</c> / <c>fmvr_force_safe.txt</c> in the same folder overrides the decision
    /// (adb push, for bisecting on the headset). Pure logic is static and file-system agnostic for EditMode tests.
    /// </summary>
    public static class LaunchGuard
    {
        public const string StateFile = "fmvr_state.txt";
        public const string ForceFullFile = "fmvr_force_full.txt";
        public const string ForceSafeFile = "fmvr_force_safe.txt";

        public enum Stage { None, Started, Core, Ready }

        /// <summary>True when this launch runs Main_Core only.</summary>
        public static bool SafeMode { get; private set; }
        public static string SafeReason { get; private set; } = "";
        public static Stage Current { get; private set; } = Stage.None;
        static string _dir;

        /// <summary>Pure rule: should this launch be SAFE, given the previous launch's stage and the override files?</summary>
        public static bool Decide(Stage previous, int previousFailStreak, bool forceFull, bool forceSafe, out string reason)
        {
            if (forceSafe) { reason = "forced by " + ForceSafeFile; return true; }
            if (forceFull) { reason = "forced full by " + ForceFullFile; return false; }
            if (previous == Stage.None || previous == Stage.Ready) { reason = previous == Stage.None ? "first launch" : "previous launch reached gameplay"; return false; }
            reason = $"previous launch stopped at '{previous.ToString().ToLowerInvariant()}' (fail streak {previousFailStreak})";
            return true;
        }

        /// <summary>"stage|failStreak" → values; garbage → None.</summary>
        public static void Parse(string text, out Stage stage, out int failStreak)
        {
            stage = Stage.None; failStreak = 0;
            if (string.IsNullOrWhiteSpace(text)) return;
            var parts = text.Trim().Split('|');
            if (!Enum.TryParse(parts[0], true, out stage)) stage = Stage.None;
            if (parts.Length > 1) int.TryParse(parts[1], out failStreak);
        }

        public static string Format(Stage stage, int failStreak) => stage.ToString().ToLowerInvariant() + "|" + failStreak;

        /// <summary>Called once per launch (device only) before the first scene loads.</summary>
        public static void Begin(string dir)
        {
            _dir = dir;
            Stage prev = Stage.None; int streak = 0;
            bool forceFull = false, forceSafe = false;
            try
            {
                var p = Path.Combine(dir, StateFile);
                if (File.Exists(p)) Parse(File.ReadAllText(p), out prev, out streak);
                forceFull = File.Exists(Path.Combine(dir, ForceFullFile));
                forceSafe = File.Exists(Path.Combine(dir, ForceSafeFile));
            }
            catch (Exception) { /* unreadable state = first launch */ }
            SafeMode = Decide(prev, streak, forceFull, forceSafe, out var reason);
            SafeReason = reason;
            _streak = prev == Stage.Ready || prev == Stage.None ? 0 : streak + 1;
            Mark(Stage.Started);
        }

        static int _streak;

        public static void Mark(Stage stage)
        {
            if (stage <= Current && stage != Stage.Started) return;
            Current = stage;
            if (_dir == null) return;
            try { File.WriteAllText(Path.Combine(_dir, StateFile), Format(stage, stage == Stage.Ready ? 0 : _streak)); }
            catch (Exception) { /* read-only storage: no crash flag, still run */ }
        }

        /// <summary>Tests / editor: force the mode without files.</summary>
        public static void SetForTests(bool safe, string reason = "test")
        {
            SafeMode = safe; SafeReason = reason; Current = Stage.None; _dir = null;
        }
    }
}
