using UnityEngine;

namespace FieldMedic.Tests
{
    /// <summary>
    /// Saves and restores the engine-global state that scene/battle tests touch (time scale, fixed step, quality
    /// level, gravity, audio volume), so test order cannot leak physics/timing changes into the V1 bot tests.
    /// </summary>
    public sealed class GlobalStateGuard
    {
        readonly float _timeScale = Time.timeScale, _fixed = Time.fixedDeltaTime, _maxDt = Time.maximumDeltaTime;
        readonly int _quality = QualitySettings.GetQualityLevel();
        readonly Vector3 _gravity = Physics.gravity;
        readonly float _volume = AudioListener.volume;

        public static string Describe() =>
            $"timeScale={Time.timeScale} fixedDt={Time.fixedDeltaTime} maxDt={Time.maximumDeltaTime} quality={QualitySettings.GetQualityLevel()} gravity={Physics.gravity} simMode={Physics.simulationMode} autoSync={Physics.autoSyncTransforms}";

        public void Restore()
        {
            Time.timeScale = _timeScale;
            Time.fixedDeltaTime = _fixed;
            Time.maximumDeltaTime = _maxDt;
            if (QualitySettings.GetQualityLevel() != _quality) QualitySettings.SetQualityLevel(_quality, true);
            Physics.gravity = _gravity;
            AudioListener.volume = _volume;
        }
    }
}
