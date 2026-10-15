using System.Collections.Generic;
using FieldMedic.Environment;
using UnityEngine;

namespace FieldMedic.Battle
{
    /// <summary>Owns the vehicles: steps them, hands out shots requested by the battle director.</summary>
    public sealed class VehicleLayer : MonoBehaviour
    {
        public static VehicleLayer Instance { get; private set; }
        public List<VehicleAgent> vehicles = new List<VehicleAgent>();
        int _rr;

        void Awake()
        {
            Instance = this;
            vehicles.RemoveAll(v => v == null);
            if (vehicles.Count == 0) vehicles.AddRange(GetComponentsInChildren<VehicleAgent>(true));
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            float now = Time.time, dt = Time.deltaTime;
            for (int i = 0; i < vehicles.Count; i++) if (vehicles[i] != null) vehicles[i].Step(now, dt);
        }

        public int TotalShots
        {
            get { int n = 0; for (int i = 0; i < vehicles.Count; i++) if (vehicles[i] != null) n += vehicles[i].ShotsFired; return n; }
        }

        /// <summary>The next idle vehicle fires at <paramref name="target"/>.</summary>
        public bool RequestShot(Vector3 target, bool preferCannon = true)
        {
            for (int k = 0; k < vehicles.Count; k++)
            {
                var v = vehicles[(_rr + k) % vehicles.Count];
                if (v == null || v.Busy || (preferCannon && v.autocannon && k < vehicles.Count - 1)) continue;
                _rr = (_rr + k + 1) % vehicles.Count;
                return v.RequestShot(target);
            }
            return false;
        }
    }
}
