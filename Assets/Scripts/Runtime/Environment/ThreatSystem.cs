using System;
using FieldMedic.Audio;
using FieldMedic.Scenarios;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Environment
{
    /// <summary>
    /// Unity side of <see cref="ThreatModel"/>: samples the player's head height against the parapet plane, the
    /// headlamp mode, drones overhead (from <see cref="CombatAmbience"/>) and cover zones every frame; plays the
    /// warning cues (bullet whiz, round into the parapet next to the head) and ends the run on a hit with an
    /// explanation. Scenarios switch <see cref="underFire"/> per phase. Debrief reads <see cref="Model"/>.Exposures.
    /// </summary>
    public sealed class ThreatSystem : MonoBehaviour
    {
        public static ThreatSystem Instance { get; private set; }

        public Transform head;
        [Tooltip("World Y of the eye height above which the head shows over the parapet.")]
        public float parapetHeight = 1.85f;
        [Tooltip("Eye height above which the player counts as standing tall (drone risk).")]
        public float standingHeight = 1.45f;
        public bool underFire = true;
        public bool enableHits = true;
        public bool forceDroneOverhead;
        [Tooltip("Show the run-end overlay on a hit (the scenario director shows its debrief instead).")]
        public bool showRunEndOverlay = true;

        public ThreatModel Model { get; } = new ThreatModel();
        public bool HeadExposed { get; private set; }
        public ThreatCue LastCue { get; private set; }

        public event Action<string> PlayerHit;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (head == null) { if (Camera.main != null) head = Camera.main.transform; else return; }
            Tick(Time.deltaTime);
        }

        public ThreatModel.Situation Sample()
        {
            var p = head.position;
            HeadExposed = p.y > parapetHeight;
            bool cover = false;
            foreach (var c in FindCovers()) if (c != null && c.Contains(p)) { cover = true; break; }
            var amb = CombatAmbience.Instance;
            return new ThreatModel.Situation
            {
                headAboveParapet = HeadExposed,
                underFire = underFire,
                droneOverhead = forceDroneOverhead || (amb != null && amb.DroneOverhead),
                light = Headlamp.Instance != null ? Headlamp.Instance.Mode : LightMode.Off,
                standing = p.y > standingHeight,
                inCover = cover,
            };
        }

        CoverZone[] _covers;
        float _coversAt = -1f;
        CoverZone[] FindCovers()
        {
            if (_covers == null || Time.time - _coversAt > 5f)
            {
                _covers = FindObjectsByType<CoverZone>(FindObjectsSortMode.None);
                _coversAt = Time.time;
            }
            return _covers;
        }

        public void Tick(float dt)
        {
            if (Model.IsHit) return;
            var cue = Model.Tick(Sample(), dt);
            if (cue == ThreatCue.None) return;
            LastCue = cue;
            var amb = CombatAmbience.Instance;
            switch (cue)
            {
                case ThreatCue.BulletWhiz:
                    amb?.PlayWhiz();
                    break;
                case ThreatCue.ParapetImpact:
                    amb?.BulletImpact(head.position + head.forward * 0.6f + Vector3.down * 0.1f);
                    amb?.PlayWhiz();
                    break;
                case ThreatCue.Hit:
                    if (!enableHits) { Model.Reset(); break; }
                    bool kz = GameSettings.Language == GameLanguage.KZ;
                    string text = ThreatModel.Explain(Model.HitReason, kz);
                    Debug.Log("[FMVR] player hit: " + Model.HitReason + " — " + text);
                    PlayerHit?.Invoke(Model.HitReason);
                    if (showRunEndOverlay) RunEndOverlay.Show(kz ? "ЖАРАЛАНДЫҢЫЗ" : "ВЫ РАНЕНЫ", text);
                    break;
            }
        }
    }
}
