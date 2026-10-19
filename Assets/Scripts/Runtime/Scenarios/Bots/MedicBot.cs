using System.Collections;
using FieldMedic.Casualty;
using FieldMedic.Input;
using FieldMedic.Interaction;
using FieldMedic.Medical;
using FieldMedic.Treatment;
using UnityEngine;

namespace FieldMedic.Scenarios.Bots
{
    /// <summary>
    /// Drives two <see cref="ScriptedHandInput"/> hands through the real interaction code (proximity grabs,
    /// strap pulls, wrist twists, finger strokes...) to perform procedures correctly or deliberately wrong.
    /// Used by PlayMode tests and as a "ghost hands" demo. Every step is a coroutine.
    /// </summary>
    public sealed class MedicBot : MonoBehaviour
    {
        public HandGrabber left, right;
        public float handSpeed = 1.2f;       // m/s
        public float twistSpeed = 360f;      // deg/s
        public int fallbackGrabs;            // grabs the proximity search did not resolve by itself
        public string lastStep;

        ScriptedHandInput _l, _r;
        string _loggedStep;

        /// <summary>Every step change goes to the log (a stuck bot run shows where it stopped).</summary>
        void Update()
        {
            if (lastStep == _loggedStep) return;
            _loggedStep = lastStep;
            Debug.Log($"[FMVR] bot step: {lastStep} at {Time.time:F1} s");
        }

        public void Init(HandGrabber l, HandGrabber r)
        {
            left = l; right = r;
            _l = l.Rig.Input as ScriptedHandInput ?? l.Rig.UseScripted();
            _r = r.Rig.Input as ScriptedHandInput ?? r.Rig.UseScripted();
        }

        ScriptedHandInput In(HandGrabber h) => h == left ? _l : _r;
        public HandGrabber Other(HandGrabber h) => h == left ? right : left;

        // ------------------------------------------------------------------ primitives
        public void SetPalm(HandGrabber h, Vector3 palm)
        {
            var i = In(h);
            i.Position = palm - i.Rotation * h.palmOffset;
        }

        public void SetFingertip(HandGrabber h, Vector3 tip)
        {
            var i = In(h);
            i.Position = tip - i.Rotation * h.fingertipOffset;
        }

        static IEnumerator Frame()
        {
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        public IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        /// <summary>Move the palm (or fingertip) in a straight line at hand speed.</summary>
        public IEnumerator Move(HandGrabber h, Vector3 to, bool fingertip = false, float speed = -1f)
        {
            float v = speed > 0f ? speed : handSpeed;
            Vector3 from = fingertip ? h.FingertipPosition : h.PalmPosition;
            float dist = Vector3.Distance(from, to);
            float t = 0f, dur = Mathf.Max(0.05f, dist / v);
            while (t < dur)
            {
                t += Time.deltaTime;
                var p = Vector3.Lerp(from, to, Mathf.Clamp01(t / dur));
                if (fingertip) SetFingertip(h, p); else SetPalm(h, p);
                yield return null;
            }
            if (fingertip) SetFingertip(h, to); else SetPalm(h, to);
            yield return Frame();
        }

        /// <summary>Move a held item so that its point `itemPoint` (world, measured now) travels to `to`.</summary>
        public IEnumerator MoveHeld(HandGrabber h, Transform item, Vector3 itemPoint, Vector3 to, float speed = -1f)
        {
            Vector3 offset = itemPoint - h.PalmPosition;
            Vector3 local = item.InverseTransformPoint(itemPoint);
            yield return Move(h, to - offset, false, speed);
            // closed loop: the item hangs in the hand a little differently than measured
            for (int k = 0; k < 15; k++)
            {
                Vector3 err = to - item.TransformPoint(local);
                if (err.magnitude < 0.004f) break;
                SetPalm(h, h.PalmPosition + err);
                yield return Frame();
            }
        }

        /// <summary>Rip the IFAK lid open: grab the red pull tab and pull it up and toward the medic.</summary>
        public IEnumerator OpenPouch(IfakPouch pouch, HandGrabber h)
        {
            if (pouch == null || pouch.IsOpen || pouch.Flap == null || pouch.Flap.Tab == null) yield break;
            lastStep = "open IFAK";
            var tab = pouch.Flap.Tab;
            if (h.Held != null) yield return Release(h);
            yield return Grab(h, tab);
            Vector3 pull = (pouch.transform.up * 0.7f - pouch.transform.forward * 0.7f).normalized;
            yield return Move(h, h.PalmPosition + pull * (pouch.Flap.pullToOpen + 0.04f), false, 0.6f);
            yield return Release(h);
            yield return Wait(0.3f);
        }

        /// <summary>Press a round button with the index fingertip (from in front of the cap, push, pull back).</summary>
        public IEnumerator Poke(FieldMedic.UI.RoundButton b, HandGrabber h, int times = 1)
        {
            if (b == null) yield break;
            lastStep = "poke " + b.name;
            if (h.Held != null) yield return Release(h);
            for (int i = 0; i < times; i++)
            {
                // the cap faces the button's local −Z; its surface is ~2.5 cm in front of the pivot
                Vector3 front = b.transform.TransformPoint(new Vector3(0f, 0f, -0.07f / Mathf.Max(1e-4f, b.transform.lossyScale.z)));
                Vector3 pressed = b.transform.TransformPoint(new Vector3(0f, 0f, -0.004f / Mathf.Max(1e-4f, b.transform.lossyScale.z)));
                yield return Move(h, front, true, 1.5f);
                yield return Move(h, pressed, true, 0.25f);
                yield return Wait(0.1f);
                yield return Move(h, front, true, 0.5f);
                yield return Wait(0.15f);
            }
        }

        public IEnumerator Grab(HandGrabber h, Grabbable g, Vector3? at = null)
        {
            if (g is MedItem mi && mi.InPouch && mi.pouch != null && !mi.pouch.IsSlotOpen(mi.homeSlot))
                yield return OpenPouch(mi.pouch, h);
            lastStep = "grab " + g.name;
            Vector3 aim = at ?? g.transform.position;
            yield return Move(h, aim);
            // handles on a moving body: home in on the live position
            for (int k = 0; k < 150 && Vector3.Distance(h.PalmPosition, g.transform.position) > 0.02f && (at == null || g is ProxyHandle); k++)
            {
                SetPalm(h, Vector3.MoveTowards(h.PalmPosition, g.transform.position, Mathf.Max(0.02f, handSpeed * Time.deltaTime * 3f)));
                yield return Frame();
            }
            if (Vector3.Distance(h.PalmPosition, g.transform.position) > 0.05f)
                Debug.Log($"[FMVR] bot grab drift: aim {aim:F2} palm {h.PalmPosition:F2} input {In(h).Position:F2} hand {h.transform.position:F2} target now {g.transform.position:F2}");
            In(h).Grip = 1f;
            yield return Frame();
            yield return Frame();
            if (h.Held != g)
            {
                // the proximity search picked something else (or nothing): note it, grab the intended one
                fallbackGrabs++;
                var near = Physics.OverlapSphere(h.PalmPosition, h.grabRadius, ~0, QueryTriggerInteraction.Collide);
                var gc = g.GetComponentInChildren<Collider>();
                Debug.Log($"[FMVR] bot fallback grab: wanted {g.name}, got {(h.Held != null ? h.Held.name : "nothing")}; " +
                          $"palm→target {Vector3.Distance(h.PalmPosition, g.transform.position):F3} m, collider {(gc != null ? gc.enabled + "/" + gc.gameObject.activeInHierarchy + " closest " + Vector3.Distance(gc.ClosestPoint(h.PalmPosition), h.PalmPosition).ToString("F3") : "none")}, " +
                          $"overlap {near.Length}: {string.Join(",", System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(near, x => x.name), 8))} grip={In(h).Grip} gripping={h.IsGripping}");
                if (h.Held != null) h.Release();
                h.TryGrab(g);
                yield return Frame();
            }
        }

        public IEnumerator Release(HandGrabber h)
        {
            In(h).Grip = 0f;
            yield return Frame();
            yield return Frame();
        }

        public IEnumerator SetTrigger(HandGrabber h, float v)
        {
            In(h).Trigger = v;
            yield return Frame();
        }

        /// <summary>Rotate the wrist about a world axis, keeping the palm in place.</summary>
        public IEnumerator Twist(HandGrabber h, Vector3 axis, float degrees, float degPerSec = -1f)
        {
            float rate = degPerSec > 0f ? degPerSec : twistSpeed;
            var i = In(h);
            Vector3 palm = h.PalmPosition;
            float done = 0f, total = Mathf.Abs(degrees), sign = Mathf.Sign(degrees);
            while (done < total)
            {
                float step = Mathf.Min(total - done, rate * Time.deltaTime);
                done += step;
                i.Rotation = Quaternion.AngleAxis(step * sign, axis) * i.Rotation;
                i.Position = palm - i.Rotation * h.palmOffset;
                yield return null;
            }
            yield return Frame();
        }

        /// <summary>Orient the hand so a held item's rotation becomes `itemRot` (uses the current grip offset).</summary>
        public IEnumerator AimHeld(HandGrabber h, Transform item, Quaternion itemRot)
        {
            var i = In(h);
            Quaternion local = Quaternion.Inverse(item.rotation) * h.transform.rotation;
            Vector3 palm = h.PalmPosition;
            Quaternion target = itemRot * local;
            Quaternion start = i.Rotation;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                i.Rotation = Quaternion.Slerp(start, target, t);
                i.Position = palm - i.Rotation * h.palmOffset;
                yield return null;
            }
            i.Rotation = target;
            i.Position = palm - i.Rotation * h.palmOffset;
            for (int k = 0; k < 10; k++) yield return Frame();
        }

        // ------------------------------------------------------------------ tourniquet
        /// <summary>
        /// Full CAT application: loop over the foot/hand, slide to `limbPos`, strap pull, `halfTurns` windlass,
        /// lock, TIME strap, write the time with the marker (if given).
        /// </summary>
        public IEnumerator ApplyTourniquet(TourniquetCAT cat, CasualtyController c, Limb limb, float limbPos,
            int halfTurns = 4, MarkerItem marker = null, HandGrabber h = null, float strapPull = 0.18f)
        {
            h ??= right;
            var rig = c.rig;
            lastStep = "tq: grab";
            yield return Grab(h, cat);
            // past the end of the foot/hand, then along the limb axis
            LimbMath.Points(rig, limb, out _, out _, out var tipJ, out var end);
            Vector3 endDir = (end - tipJ).normalized;
            // uneven ground (crater rims on the steppe): never aim the loop into the soil
            Vector3 Lift(Vector3 q) { if (CasualtyDrag.FloorAt(q, out float fl)) q.y = Mathf.Max(q.y, fl + 0.06f); return q; }
            lastStep = "tq: over the end";
            // like a person: lift the CAT clear of the body first and bring it down past the foot — never drag the loop
            // under a raised knee on the way (that would route it around the limb at the wrong place)
            Vector3 over = Lift(end + endDir * 0.12f + Vector3.up * 0.03f);
            float clear = Mathf.Max(cat.transform.position.y, over.y, c.transform.position.y + 0.55f) + 0.15f;
            yield return MoveHeld(h, cat.transform, cat.transform.position, new Vector3(cat.transform.position.x, clear, cat.transform.position.z));
            yield return MoveHeld(h, cat.transform, cat.transform.position, new Vector3(over.x, clear, over.z));
            yield return MoveHeld(h, cat.transform, cat.transform.position, over);
            for (float p = 2.55f; p >= Mathf.Max(limbPos, 2.0f); p -= 0.1f)
                yield return MoveHeld(h, cat.transform, cat.transform.position, Lift(LimbMath.PointAt(rig, limb, p)), 0.8f);
            for (int k = 0; k < 20 && !cat.State.OnLimb; k++) yield return Frame();
            if (!cat.State.OnLimb)
            {
                lastStep = "tq: wrap failed";
                CasualtyDrag.FloorAt(end, out float floorY);
                Debug.Log($"[FMVR] bot: wrap failed — cat {cat.transform.position:F2}, limb end {end:F2} (floor {floorY:F2}), tip joint {tipJ:F2}, down {c.IsDown}, held {cat.IsHeld}");
                yield break;
            }
            lastStep = "tq: slide";
            float cur = cat.State.limbPosition;
            for (float p = cur; p > limbPos; p = Mathf.Max(limbPos, p - 0.05f))
            {
                yield return Move(h, LimbMath.PointAt(rig, limb, p), false, 0.6f);
                if (Mathf.Approximately(p, limbPos)) break;
            }
            yield return Move(h, LimbMath.PointAt(rig, limb, limbPos), false, 0.6f);
            for (int k = 0; k < 30 && Mathf.Abs(cat.State.limbPosition - limbPos) > 0.02f; k++) yield return Frame();
            yield return Release(h);

            lastStep = "tq: strap";
            yield return Frame();
            var strap = cat.StrapHandle;
            yield return Grab(h, strap, strap.transform.position);
            Vector3 outDir = (strap.transform.position - cat.transform.position).normalized;
            yield return Move(h, h.PalmPosition + outDir * strapPull, false, 0.4f);
            yield return Release(h);

            lastStep = "tq: windlass";
            var rod = cat.RodHandle;
            yield return Grab(h, rod, rod.transform.position);
            yield return Twist(h, cat.transform.up, halfTurns * 180f + 8f);
            // like a person: keep turning until the count is reached (hand pose / crank geometry differ per scene)
            for (int k = 0; k < 12 && cat.State.HalfTurns < halfTurns; k++) yield return Twist(h, cat.transform.up, 45f);
            float err = cat.State.LockError;
            if (err > 20f) yield return Twist(h, cat.transform.up, 180f - (Mathf.Abs(cat.State.windlassDegrees) % 180f) + 4f); // on to the next clip position
            yield return Release(h);

            if (!cat.State.Locked) { lastStep = "tq: not locked"; yield break; }
            lastStep = "tq: time strap";
            yield return Frame();
            var ts = cat.TimeStrapHandle;
            yield return Grab(h, ts, ts.transform.position);
            yield return Move(h, h.PalmPosition + cat.transform.up * 0.05f + cat.transform.forward * 0.05f, false, 0.4f);
            yield return Release(h);

            if (marker != null)
            {
                yield return Write(marker, cat.TimeLabelPoint, h);
                // like a person: look, and write again if the strap is still blank (the strap shifts as he moves)
                for (int k = 0; k < 2 && !cat.State.TimeMarked; k++)
                {
                    if (h.Held != null) yield return Release(h);
                    yield return Write(marker, cat.TimeLabelPoint, h);
                }
            }
            lastStep = "tq: done";
        }

        /// <summary>Uncap... squeeze the marker and scribble on a point.</summary>
        public IEnumerator Write(MarkerItem marker, Vector3 point, HandGrabber h = null)
        {
            h ??= right;
            lastStep = "marker";
            yield return Grab(h, marker);
            yield return MoveHeld(h, marker.transform, marker.TipPosition, point + Vector3.up * 0.02f);
            yield return MoveHeld(h, marker.transform, marker.TipPosition, point);
            yield return SetTrigger(h, 1f);
            Vector3 p0 = h.PalmPosition;
            for (int k = 0; k < 4; k++)
            {
                yield return Move(h, p0 + Vector3.right * 0.025f, false, 0.25f);
                yield return Move(h, p0 - Vector3.right * 0.005f, false, 0.25f);
            }
            yield return SetTrigger(h, 0f);
            yield return Release(h);
        }

        // ------------------------------------------------------------------ drag
        /// <summary>Grab the drag handle and walk backward to `dest` (horizontal), pulling in steady strokes.</summary>
        public IEnumerator DragTo(CasualtyController c, Vector3 dest, float timeout = 25f, HandGrabber h = null)
        {
            h ??= right;
            var drag = c.Interaction.Drag;
            lastStep = "drag";
            yield return Grab(h, drag, drag.transform.position);
            float t = 0f;
            var hips = c.rig.Bone(HumanBodyBones.Hips);
            while (t < timeout)
            {
                Vector3 to = dest - hips.position; to.y = 0f;
                if (to.magnitude < 0.25f) break;
                Vector3 handle = drag.transform.position;
                SetPalm(h, new Vector3(handle.x, handle.y + 0.05f, handle.z) + to.normalized * 0.16f);
                t += Time.deltaTime;
                yield return null;
            }
            yield return Release(h);
        }

        // ------------------------------------------------------------------ shears / packing / dressing
        public IEnumerator CutClothing(ShearsItem shears, CasualtyController c, Limb limb, float from, float to, HandGrabber h = null)
        {
            h ??= right;
            lastStep = "shears";
            yield return Grab(h, shears);
            var rig = c.rig;
            Vector3 Surf(float p) => LimbMath.PointAt(rig, limb, p) + LimbMath.UpAround(rig, limb, p) * (LimbMath.RadiusAt(limb, p) + 0.005f);
            yield return MoveHeld(h, shears.transform, shears.TipPosition, Surf(from) + Vector3.up * 0.05f);
            yield return MoveHeld(h, shears.transform, shears.TipPosition, Surf(from));
            yield return SetTrigger(h, 1f);
            Debug.Log($"[FMVR] bot cut start: tip {shears.TipPosition:F3} target {Surf(from):F3} held {shears.IsHeld} trig {shears.Trigger}");
            float step = from > to ? -0.05f : 0.05f;
            for (float p = from; step < 0 ? p >= to : p <= to; p += step)
                yield return MoveHeld(h, shears.transform, shears.TipPosition, Surf(p), 0.3f);
            yield return SetTrigger(h, 0f);
            Debug.Log($"[FMVR] bot cut: held {shears.IsHeld} lengths {string.Join(",", System.Linq.Enumerable.Select(shears.CutLength, kv => kv.Key + "=" + kv.Value.ToString("F2")))}");
            yield return Move(h, h.PalmPosition + Vector3.up * 0.2f);
            yield return Release(h);
        }

        public IEnumerator Pack(GauzeItem gauze, WoundCare care, HandGrabber holdHand = null, int maxStrokes = 40)
        {
            holdHand ??= left;
            var pusher = Other(holdHand);
            lastStep = "pack";
            yield return Grab(holdHand, gauze);
            Vector3 w = care.transform.position, n = care.Normal;
            yield return MoveHeld(holdHand, gauze.transform, gauze.TipPosition, w + n * 0.01f);
            for (int k = 0; k < 20 && gauze.PackingInto == null; k++) yield return Frame();
            // keep the roll just beside the wound
            yield return Move(holdHand, holdHand.PalmPosition + n * 0.08f + Vector3.up * 0.04f);
            for (int s = 0; s < maxStrokes && !care.Packing.Complete; s++)
            {
                yield return Move(pusher, w + n * 0.02f, true, 0.6f);
                yield return Move(pusher, w - n * 0.03f, true, 0.4f);
                yield return Move(pusher, w + n * 0.02f, true, 0.6f);
            }
            yield return Move(pusher, w + n * 0.12f, true);
            if (holdHand.Held != null) yield return Release(holdHand);
        }

        /// <summary>Palm pressed 1–2 cm into the wound for `seconds` of real time.</summary>
        public IEnumerator HoldPressure(WoundCare care, float seconds, HandGrabber h = null)
        {
            h ??= right;
            lastStep = "pressure";
            Vector3 w = care.transform.position, n = care.Normal;
            yield return Move(h, w + n * 0.05f);
            yield return Move(h, w - n * 0.015f, false, 0.3f);
            float t = 0f;
            while (t < seconds)
            {
                w = care.transform.position; n = care.Normal; // keep the hand on the wound while he moves
                SetPalm(h, w - n * 0.015f + Random.insideUnitSphere * 0.001f);
                t += Time.deltaTime;
                yield return null;
            }
            yield return Move(h, w + n * 0.1f);
        }

        public IEnumerator Bandage(BandageItem b, WoundCare care, HandGrabber h = null)
        {
            h ??= right;
            lastStep = "bandage";
            yield return Grab(h, b);
            Vector3 w = care.transform.position, n = care.Normal;
            yield return MoveHeld(h, b.transform, b.TipPosition, w + n * 0.05f);
            yield return MoveHeld(h, b.transform, b.TipPosition, w + n * 0.004f, 0.3f);
            for (int k = 0; k < 10 && b.ApplyingTo == null; k++) yield return Frame();
            if (b.ApplyingTo == null) { lastStep = "bandage: pad not placed"; yield break; }
            Vector3 axis = care.WrapAxis(out Vector3 c, out float r);
            Vector3 start = Vector3.ProjectOnPlane(n, axis).normalized;
            float radius = r + 0.08f;
            float ang = 0f;
            IEnumerator Circle(float degrees)
            {
                float dir = Mathf.Sign(degrees), done = 0f;
                while (done < Mathf.Abs(degrees))
                {
                    float st = Mathf.Min(Mathf.Abs(degrees) - done, 400f * Time.deltaTime);
                    done += st; ang += st * dir;
                    SetPalm(h, c + Quaternion.AngleAxis(ang, axis) * start * radius);
                    yield return null;
                }
            }
            yield return Move(h, c + start * radius);
            yield return Circle(430f);   // first wrap over the pad
            yield return Circle(-60f);   // reverse through the pressure bar
            yield return Circle(-800f);  // tight wraps
            yield return Release(h);
        }

        // ------------------------------------------------------------------ chest
        public IEnumerator ChestSeal(ChestSealItem seal, WoundCare care, HandGrabber holdHand = null)
        {
            holdHand ??= left;
            var other = Other(holdHand);
            lastStep = "seal: tear";
            yield return Grab(holdHand, seal);
            yield return Move(holdHand, care.transform.position + care.Normal * 0.3f + Vector3.up * 0.1f);
            for (int k = 0; k < 5; k++) yield return Frame();
            yield return Grab(other, seal.TearNotch, seal.TearNotch.transform.position);
            yield return Move(other, other.PalmPosition + (seal.TearNotch.transform.position - seal.transform.position).normalized * 0.12f, false, 0.4f);
            yield return Release(other);
            lastStep = "seal: peel";
            for (int k = 0; k < 3; k++) yield return Frame();
            yield return Grab(other, seal.BackingTab, seal.BackingTab.transform.position);
            yield return Move(other, other.PalmPosition + (seal.BackingTab.transform.position - seal.transform.position).normalized * 0.12f, false, 0.4f);
            yield return Release(other);

            lastStep = "seal: wipe";
            Vector3 w = care.transform.position, n = care.Normal;
            Vector3 side = Vector3.Cross(n, Vector3.up).sqrMagnitude > 0.01f ? Vector3.Cross(n, Vector3.up).normalized : Vector3.right;
            for (int k = 0; k < 3; k++)
            {
                yield return Move(other, w + n * 0.01f - side * 0.14f, false, 1.5f);
                yield return Move(other, w + n * 0.01f + side * 0.14f, false, 0.5f);
                yield return Move(other, w + n * 0.12f + side * 0.14f, false, 1.5f);
            }
            lastStep = "seal: place";
            // place the seal face-down onto the wound (item up = wound normal)
            yield return AimHeld(holdHand, seal.transform, Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, n).sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(Vector3.forward, n) : Vector3.right, n));
            for (int attempt = 0; attempt < 4 && seal.PlacedOn == null; attempt++)
            {
                w = care.transform.position; n = care.Normal; // his pose keeps changing (coughing, breathing)
                yield return MoveHeld(holdHand, seal.transform, seal.TipPosition, w + n * 0.05f);
                w = care.transform.position; n = care.Normal;
                yield return MoveHeld(holdHand, seal.transform, seal.TipPosition, w + n * 0.004f, 0.3f);
                for (int k = 0; k < 10 && seal.PlacedOn == null; k++) yield return Frame();
            }
            w = care.transform.position; n = care.Normal;
            if (seal.PlacedOn == null)
                Debug.Log($"[FMVR] bot seal not placed: step {seal.Proc.step} wipes {care.Wipes} held {seal.IsHeld} lat {care.Lateral(seal.TipPosition):F3} h {care.Height(seal.TipPosition):F3} hint {seal.LastHint}");
            if (holdHand.Held != null) yield return Release(holdHand);
            yield return Move(holdHand, holdHand.PalmPosition + n * 0.2f);
            lastStep = "seal: edges";
            for (int s = 0; s < 12 && !seal.Proc.Complete; s++)
            {
                w = care.transform.position; n = care.Normal; // he keeps moving
                Vector3 up = care.transform.up, right = care.transform.right;
                float a = s * 60f * Mathf.Deg2Rad + 0.3f;
                Vector3 e = w + (up * Mathf.Cos(a) + right * Mathf.Sin(a)) * 0.055f;
                yield return Move(other, e + n * 0.03f, true, 1f);
                yield return Move(other, e - n * 0.004f, true, 0.3f);
                yield return Move(other, e + n * 0.03f, true, 1f);
            }
            yield return Move(other, w + n * 0.25f);
        }

        public IEnumerator Needle(NeedleItem needle, CasualtyController c, NeedleSite site, bool rightSide, HandGrabber h = null)
        {
            h ??= right;
            lastStep = "needle";
            NeedleItem.Landmark(c.rig, site, rightSide, out var p, out var n);
            yield return Grab(h, needle);
            yield return Move(h, p + n * 0.25f);
            yield return AimHeld(h, needle.transform, Quaternion.LookRotation(-n, Vector3.ProjectOnPlane(Vector3.up, n).sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(Vector3.up, n) : Vector3.forward));
            yield return MoveHeld(h, needle.transform, needle.TipPosition, p + n * 0.02f, 0.4f);
            yield return MoveHeld(h, needle.transform, needle.TipPosition, p - n * 0.065f, 0.15f);
            yield return Wait(3f);
            if (h.Held != null) yield return Release(h);
        }

        // ------------------------------------------------------------------ other skills
        public IEnumerator Gloves(GlovesItem gloves)
        {
            lastStep = "gloves";
            yield return Grab(left, gloves);
            yield return Move(left, left.PalmPosition + Vector3.up * 0.1f);
            Vector3 g = gloves.transform.position;
            yield return Move(right, g + Vector3.right * 0.1f);
            yield return Move(right, g, false, 0.4f);
            yield return Wait(0.25f);
            yield return Move(right, g + Vector3.right * 0.2f, false, 0.6f);
            // swap: gloved right hand takes the pair, left goes in
            yield return Release(left);
            yield return Move(left, left.PalmPosition + Vector3.left * 0.25f);
            yield return Grab(right, gloves);
            g = gloves.transform.position;
            yield return Move(left, g - Vector3.right * 0.1f);
            yield return Move(left, g, false, 0.4f);
            yield return Wait(0.25f);
            yield return Move(left, g - Vector3.right * 0.25f, false, 0.6f);
            if (right.Held != null) yield return Release(right);
        }

        /// <summary>Open-hand strokes over the body regions (finds hidden wounds).</summary>
        public IEnumerator BloodSweep(CasualtyController c, HandGrabber h = null)
        {
            h ??= right;
            lastStep = "sweep";
            In(h).Grip = 0f;
            foreach (var part in c.rig.Parts)
            {
                if (part == null) continue;
                var col = part.GetComponent<Collider>();
                Vector3 ctr = col.bounds.center;
                Vector3 top = col.ClosestPoint(ctr + Vector3.up * 0.5f);
                yield return Move(h, top + Vector3.up * 0.04f, false, 2f);
                yield return Move(h, top + Vector3.up * 0.01f + (ctr - top).normalized * 0.0f + c.rig.BodyUp * 0.06f, false, 0.4f);
                yield return Move(h, top + Vector3.up * 0.01f - c.rig.BodyUp * 0.06f, false, 0.4f);
            }
            // a wet patch under the glove: stroke slowly over it (the palm finds what the overview stroke missed)
            foreach (var s in c.WoundSites)
            {
                if (s == null || s.Wound == null || s.Wound.discovered || (s.Wound.posterior && !c.rolledOnSide)) continue;
                Vector3 p = s.transform.position;
                yield return Move(h, p + Vector3.up * 0.05f, false, 2f);
                yield return Move(h, p + Vector3.up * 0.01f + c.rig.BodyUp * 0.05f, false, 0.4f);
                yield return Move(h, p + Vector3.up * 0.01f - c.rig.BodyUp * 0.05f, false, 0.4f);
            }
        }

        public IEnumerator CheckPulse(CasualtyController c, PulseSite site, float seconds, HandGrabber h = null)
        {
            h ??= left;
            lastStep = "pulse " + site;
            Vector3 p = HandTreatment.PulsePoint(c.rig, site);
            yield return Move(h, p + Vector3.up * 0.06f, true);
            yield return Move(h, p, true, 0.2f);
            float t = 0f;
            while (t < seconds) { SetFingertip(h, HandTreatment.PulsePoint(c.rig, site)); t += Time.deltaTime; yield return null; }
            yield return Move(h, p + Vector3.up * 0.15f, true);
        }

        /// <summary>Two-hand log roll: shoulder + hip of one side lifted up and across.</summary>
        public IEnumerator Roll(CasualtyController c, bool liftLeftSide)
        {
            var roll = c.Interaction.Roll;
            var sh = liftLeftSide ? roll.shoulderL : roll.shoulderR;
            var hp = liftLeftSide ? roll.hipL : roll.hipR;
            lastStep = "roll";
            yield return Grab(left, sh, sh.transform.position);
            yield return Grab(right, hp, hp.transform.position);
            Vector3 across = Vector3.ProjectOnPlane(c.rig.BodyRight * (liftLeftSide ? 1f : -1f), Vector3.up).normalized;
            Vector3 d = (Vector3.up * 0.25f + across * 0.15f);
            Vector3 a0 = left.PalmPosition, b0 = right.PalmPosition;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 1.2f)
            {
                SetPalm(left, a0 + d * t);
                SetPalm(right, b0 + d * t);
                yield return null;
            }
            yield return Wait(0.6f);
            yield return Release(left);
            yield return Release(right);
            yield return Move(left, left.PalmPosition + Vector3.up * 0.2f);
            yield return Move(right, right.PalmPosition + Vector3.up * 0.2f);
        }

        /// <summary>Thermal blanket: both hands pull the pack apart (unfolds), carry it over his hips, let go.</summary>
        public IEnumerator Blanket(BlanketItem b, CasualtyController c)
        {
            lastStep = "blanket";
            yield return Grab(left, b);
            yield return Grab(right, b);
            Vector3 mid = (left.PalmPosition + right.PalmPosition) * 0.5f;
            Vector3 side = Vector3.Cross(Vector3.up, c.rig.BodyUp).sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, c.rig.BodyUp).normalized : Vector3.right;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
            {
                SetPalm(left, mid - side * 0.35f * t);
                SetPalm(right, mid + side * 0.35f * t);
                yield return null;
            }
            for (int k = 0; k < 5 && !b.Unfolded; k++) yield return Frame();
            Vector3 hips = c.rig.Bone(HumanBodyBones.Hips).position + Vector3.up * 0.45f;
            yield return Move(left, hips - side * 0.35f, false, 1.5f);
            yield return Move(right, hips + side * 0.35f, false, 1.5f);
            yield return Release(left);
            yield return Release(right);
            yield return Move(left, left.PalmPosition + Vector3.up * 0.2f);
            yield return Move(right, right.PalmPosition + Vector3.up * 0.2f);
        }

        /// <summary>Slipped band (S7) or failed conversion: grab the rod, twist until the bleeding stops, lock.</summary>
        public IEnumerator Retighten(TourniquetCAT cat, HandGrabber h = null)
        {
            h ??= right;
            lastStep = "retighten";
            var rod = cat.RodHandle;
            if (rod == null || !rod.gameObject.activeInHierarchy) { lastStep = "retighten: rod not available"; yield break; }
            yield return Grab(h, rod, rod.transform.position);
            for (int k = 0; k < 16 && (!cat.State.FullOcclusion || cat.State.HalfTurns < 4); k++) yield return Twist(h, cat.transform.up, 90f);
            float err = cat.State.LockError;
            if (err > 20f) yield return Twist(h, cat.transform.up, 180f - (Mathf.Abs(cat.State.windlassDegrees) % 180f) + 4f);
            yield return Release(h);
        }

        /// <summary>Conversion (S8): grab the locked rod and unwind it (≥ 270°) — the band is loosened if allowed.</summary>
        public IEnumerator ConvertTourniquet(TourniquetCAT cat, HandGrabber h = null)
        {
            h ??= right;
            lastStep = "convert";
            for (int k = 0; k < 10 && !cat.RodHandle.gameObject.activeInHierarchy; k++) yield return Frame();
            var rod = cat.RodHandle;
            yield return Grab(h, rod, rod.transform.position);
            yield return Twist(h, cat.transform.up, -360f, 240f);
            yield return Release(h);
        }

        public IEnumerator LiftLeg(CasualtyController c, Limb leg, float height, float holdSeconds, HandGrabber h = null)
        {
            h ??= left;
            var handle = c.Interaction.Handle(leg, LimbHandle.Grip.Ankle);
            lastStep = "lift leg";
            yield return Grab(h, handle, handle.transform.position);
            yield return Move(h, h.PalmPosition + Vector3.up * height, false, 0.35f);
            yield return Wait(holdSeconds);
        }
    }
}
