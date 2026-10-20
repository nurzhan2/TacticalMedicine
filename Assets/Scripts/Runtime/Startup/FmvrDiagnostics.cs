using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Debug = UnityEngine.Debug;

namespace FieldMedic.Startup
{
    /// <summary>
    /// Device-side black-screen diagnostics (w0b), bootstrapped before the first scene loads:
    /// <list type="bullet">
    /// <item>every log line (incl. exceptions from any thread) goes to <c>persistentDataPath/fmvr_log.txt</c>, the
    /// previous launch's file is kept as <c>fmvr_log_prev.txt</c> (adb pull, see tools/quest_bisect.ps1);</item>
    /// <item>crash flag / SAFE mode via <see cref="LaunchGuard"/>;</item>
    /// <item>XR state (active loader, OpenXR runtime, display subsystem) at 1 s and 5 s, a clear
    /// <c>[FMVR] XR FAILED</c> if the display is not running after 5 s of focused, unpaused run time (a headset taken
    /// off the head pauses the app and stops the display - that is deferred, not a failure, see <see cref="XrVerdict"/>);</item>
    /// <item>first rendered frame time, scene load/activation times and the longest frame per second for the first
    /// 60 s (a stall > 1 s is what makes Horizon OS show black / kill the app).</item>
    /// </list>
    /// </summary>
    public sealed class FmvrDiagnostics : MonoBehaviour
    {
        public const string LogFile = "fmvr_log.txt";
        static StreamWriter _w;
        static readonly object Lock = new object();
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        public static float FirstFrameAt { get; private set; } = -1f;
        public static int ErrorCount { get; private set; }
        public static string LogPath { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            ErrorCount = 0;
            if (Application.isEditor) return; // editor: Editor.log + tests; the file/crash flag is for the headset
            try
            {
                string dir = Application.persistentDataPath;
                LogPath = Path.Combine(dir, LogFile);
                var prev = Path.Combine(dir, "fmvr_log_prev.txt");
                if (File.Exists(LogPath)) { if (File.Exists(prev)) File.Delete(prev); File.Move(LogPath, prev); }
                _w = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                LaunchGuard.Begin(dir);
                Write($"=== Field Medic VR {Application.version} unity {Application.unityVersion} device '{SystemInfo.deviceModel}' " +
                      $"gpu '{SystemInfo.graphicsDeviceName}' api {SystemInfo.graphicsDeviceType} mem {SystemInfo.systemMemorySize} MB " +
                      $"time {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Write($"[FMVR] launch mode: {(LaunchGuard.SafeMode ? "SAFE" : "FULL")} ({LaunchGuard.SafeReason})");
            }
            catch (Exception e) { Debug.LogWarning("[FMVR] diagnostics file unavailable: " + e.Message); }
            Application.logMessageReceivedThreaded += OnLog;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (FindAnyObjectByType<FmvrDiagnostics>() != null) return;
            var go = new GameObject("~FmvrDiagnostics");
            DontDestroyOnLoad(go);
            go.AddComponent<FmvrDiagnostics>();
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) ErrorCount++;
            if (_w == null) return;
            string t = type == LogType.Log ? "" : type.ToString().ToUpperInvariant() + " ";
            Write(t + msg + (type == LogType.Exception && !string.IsNullOrEmpty(stack) ? "\n" + stack : ""));
        }

        static void Write(string line)
        {
            lock (Lock)
            {
                try { _w?.WriteLine($"{Clock.Elapsed.TotalSeconds,8:F2} {line}"); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ per-frame monitor
        float _secMax, _secT;
        int _sec;

        void Awake()
        {
            SceneManager.sceneLoaded += (s, m) => Debug.Log($"[FMVR] scene loaded: {s.name} ({m}) at {Time.realtimeSinceStartup:F2} s");
            SceneManager.activeSceneChanged += (a, b) => Debug.Log($"[FMVR] active scene: {b.name}");
        }

        IEnumerator Start()
        {
            yield return new WaitForEndOfFrame();
            FirstFrameAt = Time.realtimeSinceStartup;
            Debug.Log($"[FMVR] first frame rendered at {FirstFrameAt:F2} s since start (scene {SceneManager.GetActiveScene().name})");
            yield return new WaitForSecondsRealtime(1f);
            LogXr("1 s");
            yield return new WaitForSecondsRealtime(4f);
            // Off the head Horizon OS pauses the app / drops focus within seconds and the display subsystem stops:
            // wait until the app runs focused again for a few seconds before judging (w9c).
            bool deferredLogged = false;
            while (true)
            {
                var v = XrVerdict(XrDisplayRunning() || Application.isEditor, _paused || !Application.isFocused, Time.realtimeSinceStartup - _resumedAt);
                if (v == XrCheck.Ok) { LogXr(deferredLogged ? "5 s (after resume)" : "5 s"); break; }
                if (v == XrCheck.Failed)
                {
                    Debug.LogError("[FMVR] XR FAILED: no running XR display after 5 s of focused run time → the headset shows black / the 2D shell. " + XrSummary());
                    break;
                }
                if (!deferredLogged) { Debug.Log("[FMVR] XR check deferred: app paused / not focused (headset off the head?) - " + XrSummary()); deferredLogged = true; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        public enum XrCheck { Ok, Defer, Failed }

        /// <summary>Seconds of focused, unpaused run time after a resume before a stopped display counts as a failure.</summary>
        public const float ResumeGrace = 4f;

        /// <summary>The 5 s XR self-check decision: a running display is OK; while the app is paused / unfocused (or just
        /// resumed and the OpenXR session is still restarting) a stopped display is expected; otherwise it is a failure.</summary>
        public static XrCheck XrVerdict(bool displayRunning, bool pausedOrUnfocused, float secondsSinceResume)
        {
            if (displayRunning) return XrCheck.Ok;
            if (pausedOrUnfocused || secondsSinceResume < ResumeGrace) return XrCheck.Defer;
            return XrCheck.Failed;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_skipFrame) { _skipFrame = false; return; } // the gap across a pause is not a stall
            if (dt > _secMax) _secMax = dt;
            _secT += dt;
            if (_secT >= 1f)
            {
                _sec++;
                if (_sec <= 60 && (_sec <= 10 || _secMax > 0.1f))
                    Debug.Log($"[FMVR] frame stats s{_sec}: max frame {_secMax * 1000f:F0} ms, fps {Mathf.RoundToInt(1f / Mathf.Max(1e-3f, dt))}");
                if (_secMax > 1f) Debug.LogWarning($"[FMVR] STALL {_secMax:F2} s in second {_sec} (Horizon OS shows black/loading dots during stalls)");
                _secT = 0f; _secMax = 0f;
            }
        }

        bool _paused, _skipFrame;
        float _resumedAt = -100f;

        void OnApplicationPause(bool paused)
        {
            Debug.Log("[FMVR] application " + (paused ? "paused" : "resumed"));
            if (_paused && !paused) { _resumedAt = Time.realtimeSinceStartup; _skipFrame = true; }
            _paused = paused;
        }

        void OnApplicationFocus(bool focus)
        {
            Debug.Log("[FMVR] focus " + focus);
            if (focus) { _resumedAt = Time.realtimeSinceStartup; _skipFrame = true; }
        }

        /// <summary>Logs the XR state; true when an XR display is running.</summary>
        public static bool LogXr(string when)
        {
            bool running = XrDisplayRunning();
            Debug.Log($"[FMVR] XR status @{when}: {XrSummary()}");
            return running || Application.isEditor;
        }

        public static bool XrDisplayRunning()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d.running) return true;
            return false;
        }

        public static string XrSummary()
        {
            try
            {
                var mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                string loader = mgr != null && mgr.activeLoader != null ? mgr.activeLoader.name : "none";
                string runtime = "?";
                try { runtime = UnityEngine.XR.OpenXR.OpenXRRuntime.name + " " + UnityEngine.XR.OpenXR.OpenXRRuntime.version; } catch (Exception) { }
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                string disp = displays.Count == 0 ? "no display subsystem" : "";
                foreach (var d in displays) disp += $"display running={d.running} ";
                return $"loader={loader} init={(mgr != null && mgr.isInitializationComplete)} runtime='{runtime}' {disp}" +
                       $"device='{XRSettings.loadedDeviceName}' active={XRSettings.isDeviceActive} eyeTex={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}";
            }
            catch (Exception e) { return "XR summary failed: " + e.Message; }
        }
    }
}
