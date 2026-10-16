using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FieldMedic.Audio;
using FieldMedic.Casualty;
using FieldMedic.Input;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace FieldMedic.Tests
{
    /// <summary>
    /// w8 Quest budget: the living-Erlan components (face, checks, breath audio, physical hands) add no per-frame
    /// managed allocations (A/B of the frame's GC allocation with them on and off), and the physiology-driven
    /// material updates stay ≤ 10 Hz.
    /// </summary>
    public class W8PerfTests : W8SceneTest
    {
        static IEnumerator Measure(int frames, List<long> into)
        {
            using var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            for (int i = 0; i < frames; i++) { yield return null; into.Add(rec.LastValue); }
        }

        static long Median(List<long> v) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[s.Count / 2]; }

        [UnityTest]
        public IEnumerator LivingErlan_NoPerFrameAllocations()
        {
            yield return LoadLesson("assess");
            yield return Ready(0.8f);
            // everything running: the medic listens at his mouth, the pulse under the fingers
            var behaviours = new List<Behaviour> { C.Face, C.Checks, C.Checks.Breath };
            behaviours.AddRange(Object.FindObjectsByType<HandPhysicalVisual>(FindObjectsSortMode.None));
            Assert.IsTrue(behaviours.All(b => b != null), "components present");
            var on = new List<long>(); var off = new List<long>();
            for (int round = 0; round < 3; round++)
            {
                foreach (var b in behaviours) b.enabled = true;
                yield return Measure(60, on);
                foreach (var b in behaviours) b.enabled = false;
                yield return Measure(60, off);
            }
            foreach (var b in behaviours) b.enabled = true;
            long mOn = Median(on), mOff = Median(off);
            Debug.Log($"[FMVR] w8 perf: GC alloc/frame median on {mOn} B, off {mOff} B (editor overhead included)");
            Assert.LessOrEqual(mOn - mOff, 64, "no per-frame managed allocations from the w8 components");
        }
    }
}
