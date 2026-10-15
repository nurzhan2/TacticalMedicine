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
    /// <c>[FMVR] XR FAILED</c> if the display is not running after 5 s;</item>
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
            if (!LogXr("5 s")) Debug.LogError("[FMVR] XR FAILED: no running XR display 5 s after start → the headset shows black / the 2D shell. " + XrSummary());
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
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

        void OnApplicationPause(bool paused) => Debug.Log("[FMVR] application " + (paused ? "paused" : "resumed"));
        void OnApplicationFocus(bool focus) => Debug.Log("[FMVR] focus " + focus);

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
