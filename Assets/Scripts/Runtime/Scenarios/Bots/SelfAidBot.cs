using System.Collections;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Scenarios.SelfAid;
using FieldMedic.Treatment;
using FieldMedic.UI;
using UnityEngine;

namespace FieldMedic.Scenarios.Bots
{
    /// <summary>
    /// Plays the self-aid drill (w9b) through the real interaction code with two scripted hands: walks the "body"
    /// (head + hands) into cover, holds the wounded arm out, takes the CAT from the quick pouch with the free hand, loop
    /// over the wounded hand, slide high, strap, windlass, clip, TIME strap, marker, radio report by poking the menu,
    /// blanket to the chest, answers the check-ins with the PTT button. Built on the <see cref="MedicBot"/> primitives.
    /// </summary>
    public sealed class SelfAidBot : MonoBehaviour
    {
        public SelfAidDirector d;
        public MedicBot hands;
        public Transform rig;     // moved when the bot "walks" (the head is its child)
        public float walkSpeed = 2f;
        public string lastStep;
        public int checksAnswered;

        HandGrabber Free => d.Free;
        HandGrabber Wounded => d.Wounded;

        public void Init(SelfAidDirector director, Transform body)
        {
            d = director;
            rig = body;
            hands = GetComponent<MedicBot>() ?? gameObject.AddComponent<MedicBot>();
            hands.Init(d.leftHand, d.rightHand);
        }

        void Step(string s) { lastStep = s; hands.lastStep = s; Debug.Log("[FMVR] self-aid bot: " + s); }

        Vector3 Fwd { get { var f = Vector3.ProjectOnPlane(d.head.forward, Vector3.up); return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; } }
        Vector3 Right => Vector3.Cross(Vector3.up, Fwd);

        /// <summary>Walk (or crawl) the whole body — head and both hands — to a point on the ground.</summary>
        public IEnumerator WalkTo(Vector3 ground, float speed = -1f)
        {
            Step("walk to " + ground.ToString("F1"));
            float v = speed > 0f ? speed : walkSpeed;
            Vector3 target = new Vector3(ground.x, rig.position.y, ground.z);
            var ls = d.leftHand.Rig.Input as FieldMedic.Input.ScriptedHandInput;
            var rs = d.rightHand.Rig.Input as FieldMedic.Input.ScriptedHandInput;
            while (Vector3.Distance(rig.position, target) > 0.02f)
            {
                Vector3 prev = rig.position;
                rig.position = Vector3.MoveTowards(rig.position, target, v * Time.deltaTime);
                Vector3 delta = rig.position - prev;
                if (ls != null) ls.Position += delta;
                if (rs != null) rs.Position += delta;
                yield return null;
            }
        }

        /// <summary>Wounded arm held out in front of the chest (elbow bent), still — the free hand works on it.</summary>
        public IEnumerator HoldWoundedArm()
        {
            Step("hold the wounded arm out");
            float side = d.Config.left ? -1f : 1f;
            Vector3 palm = d.head.position + Fwd * 0.4f + Vector3.down * 0.42f + Right * (0.12f * side);
            var i = d.Wounded.Rig.Input as FieldMedic.Input.ScriptedHandInput;
            if (i != null) i.Rotation = Quaternion.LookRotation(Fwd + Right * (-0.5f * side), Vector3.up);
            yield return hands.Move(Wounded, palm, false, 1f);
            yield return hands.Wait(0.2f);
        }

        /// <summary>The whole one-handed CAT: quick pouch → loop over the hand → slide to `pos` → strap → windlass →
        /// clip → TIME strap → (marker).</summary>
        public IEnumerator SelfTourniquet(float pos = 0.18f, bool writeTime = true)
        {
            var tq = d.Tq;
            var cat = d.KitCat;
            var limb = d.Limb;
            Step("tq: grab from the quick pouch");
            yield return hands.Grab(Free, cat);
            if (cat == null || !cat.IsHeldBy(Free)) { Step("tq: grab failed"); yield break; }
            Step("tq: loop over the wounded hand");
            // come in from beyond the fingertips, along the forearm
            Vector3 axis = (limb.Tip - limb.Distal).normalized;
            yield return hands.MoveHeld(Free, cat.transform, cat.transform.position, limb.Tip + axis * 0.15f + Vector3.up * 0.06f, 0.8f);
            for (float p = 2.6f; p >= 2.1f && !tq.Wrapped; p -= 0.1f)
                yield return hands.MoveHeld(Free, cat.transform, cat.transform.position, limb.PointAt(p), 0.6f);
            for (int k = 0; k < 20 && !tq.Wrapped; k++) yield return null;
            if (!tq.Wrapped) { Step("tq: wrap failed"); yield break; }
            Step("tq: slide high");
            if (Free.Held != tq.BandHandle) Free.TryGrab(tq.BandHandle);
            for (float p = tq.State.limbPosition; p > pos; p = Mathf.Max(pos, p - 0.08f))
            {
                yield return hands.Move(Free, limb.PointAt(p), false, 0.5f);
                if (Mathf.Approximately(p, pos)) break;
            }
            yield return hands.Move(Free, limb.PointAt(pos), false, 0.5f);
            for (int k = 0; k < 60 && Mathf.Abs(tq.State.limbPosition - pos) > 0.03f; k++) { hands.SetPalm(Free, limb.PointAt(pos)); yield return null; }
            yield return hands.Release(Free);
            if (tq.State.stage < TourniquetStage.Positioned) { Step("tq: not positioned"); yield break; }

            Step("tq: strap");
            yield return null;
            yield return hands.Grab(Free, tq.StrapHandle, tq.StrapPoint);
            Vector3 outDir = (tq.StrapPoint - limb.PointAt(tq.State.limbPosition)).normalized;
            yield return hands.Move(Free, Free.PalmPosition + outDir * 0.18f, false, 0.4f);
            yield return hands.Release(Free);

            Step("tq: windlass");
            yield return hands.Grab(Free, tq.RodHandle, tq.RodPoint);
            int need = TourniquetState.RequiredHalfTurns(tq.State.limbId) + 1;
            yield return hands.Twist(Free, tq.Up, need * 180f + 8f);
            for (int k = 0; k < 12 && !tq.State.FullOcclusion; k++) yield return hands.Twist(Free, tq.Up, 45f);
            if (tq.State.LockError > 20f) yield return hands.Twist(Free, tq.Up, 180f - (Mathf.Abs(tq.State.windlassDegrees) % 180f) + 4f);
            yield return hands.Release(Free);
            if (!tq.State.Locked) { Step("tq: not locked"); yield break; }

            Step("tq: time strap");
            yield return null;
            yield return hands.Grab(Free, tq.TimeStrapHandle, tq.TimeStrapPoint);
            yield return hands.Move(Free, Free.PalmPosition + tq.Up * 0.05f + tq.Axis * 0.05f, false, 0.4f);
            yield return hands.Release(Free);

            if (writeTime && d.Marker is MarkerItem marker)
            {
                Step("tq: marker");
                yield return hands.Write(marker, tq.TimeLabelPoint, Free);
                for (int k = 0; k < 2 && !tq.State.TimeMarked; k++)
                {
                    if (Free.Held != null) yield return hands.Release(Free);
                    yield return hands.Write(marker, tq.TimeLabelPoint, Free);
                }
                // put the marker back in the kit
                if (Free.Held != null) yield return hands.Release(Free);
            }
            Step("tq: done");
        }

        /// <summary>PTT on the left shoulder with the free hand's fingertip (falls back to a direct press).</summary>
        public IEnumerator PressRadio()
        {
            Step("radio PTT");
            var radio = RadioSet.Instance;
            var ptt = radio != null ? radio.PttButton.GetComponent<RoundButton>() : null;
            int before = ptt != null ? ptt.ClickCount : 0;
            if (ptt != null) yield return hands.Poke(ptt, Free);
            if (ptt == null || ptt.ClickCount == before) d.PressRadio();
            yield return hands.Wait(0.2f);
        }

        /// <summary>Radio report: open the menu, poke the right line five times (wrongLine = this step gets a wrong line).</summary>
        public IEnumerator Report(int wrongLine = -1)
        {
            if (!d.Menu.Open) yield return PressRadio();
            for (int k = 0; k < SelfAidRun.ReportSteps * 2 && !d.Run.ReportSent; k++)
            {
                yield return hands.Wait(0.55f); // the new line's buttons wake up after the finger is pulled back
                int step = d.Run.ReportStep;
                int idx = step == wrongLine ? (d.Menu.CorrectIndex == 0 ? 1 : 0) : d.Menu.CorrectIndex;
                var b = d.Menu.Buttons[idx];
                Step("report line " + (step + 1) + ": " + d.Menu.Options[idx].ru);
                int before = b.ClickCount;
                yield return hands.Poke(b, Free);
                if (b.ClickCount == before && d.Run.ReportStep == step) b.Click();
                for (int f = 0; f < 5 && d.Run.ReportStep == step; f++) yield return null;
            }
        }

        /// <summary>Blanket from the main pouch (lid ripped open first), pulled to the chest with one hand.</summary>
        public IEnumerator Blanket()
        {
            Step("blanket");
            var b = d.BlanketItem;
            if (b == null) yield break;
            yield return hands.Grab(Free, b);
            Vector3 chest = d.head.position + Vector3.down * 0.35f + Fwd * 0.12f;
            yield return hands.MoveHeld(Free, b.transform, b.transform.position, chest, 0.6f);
            for (int k = 0; k < 60 && !d.Run.Blanket; k++) yield return null;
            if (Free.Held != null) yield return hands.Release(Free);
        }

        /// <summary>Stay in cover and answer every radio check-in until the run ends.</summary>
        public IEnumerator Wait(float timeout)
        {
            Step("wait for the group");
            float t0 = Time.time;
            while (!d.Finishing && d.Report == null && Time.time - t0 < timeout)
            {
                if (d.Run.CheckPending)
                {
                    yield return hands.Wait(0.5f);
                    if (d.Run.CheckPending) { yield return PressRadio(); checksAnswered++; }
                }
                yield return null;
            }
        }
    }
}
