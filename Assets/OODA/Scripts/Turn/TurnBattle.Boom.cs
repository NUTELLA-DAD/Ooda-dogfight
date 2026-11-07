using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The heat blade's sonic boom on screen (the rules are in Turn/Core/Specials.cs). Flying straight with the blade lit the
    /// plane's glow climbs from red to white hot; at the barrier a white shock ring bursts out from it to the boom's reach,
    /// and from then on a Mach cone trails behind it. Each plane the cone shoves gets a white streak along its jolt. While
    /// planning, where your boom will go off shows on your path as a ring of its reach with a starburst.
    /// </summary>
    public partial class TurnBattle
    {
        [Header("Sonic boom")]
        public Color boomColor = new Color(1f, 0.95f, 0.85f, 0.9f);

        /// <summary>How long the shock ring takes to burst out to its reach, and how long a shoved plane's streak shows (s).</summary>
        const float BoomRingSec = 0.7f, ShoveStreakSec = 0.45f;

        readonly List<Vector3> boomPts = new List<Vector3>(48);

        /// <summary>The booms and shoves of a turn at frame now, and the Mach cone behind every plane past the barrier.</summary>
        void DrawBooms(IList<TurnAircraftView> vs, TurnResult r, int now)
        {
            if (r == null || r.booms == null) return;
            foreach (var b in r.booms)
            {
                float age = (now - b.frame) * (float)r.dt;
                if (age < 0 || age > BoomRingSec) continue;
                float f = age / BoomRingSec;
                ShockRing(UnityFrame.Pos(b.pos), UnityFrame.Dir(b.dir), Mathf.Lerp(0.05f, 1f, Mathf.Sqrt(f)) * (float)(Specials.BoomReachFt * Atmosphere.MPerFt), 1f - f);
            }
            if (r.shoves != null)
                foreach (var s in r.shoves)
                {
                    float age = (now - s.frame) * (float)r.dt;
                    if (age < 0 || age > ShoveStreakSec || s.target >= vs.Count || vs[s.target] == null) continue;
                    Vector3 p = vs[s.target].transform.position, jolt = UnityFrame.Dir(s.dv.Normalized);
                    var c = boomColor; c.a *= 1f - age / ShoveStreakSec;
                    lines.Line(p - jolt * 12f, p - jolt * 40f, 1.4f, c);
                    lines.Line(p - jolt * 12f + Vector3.up * 3f, p - jolt * 32f + Vector3.up * 3f, 0.8f, c);
                }
            for (int k = 0; k < vs.Count && k < r.frames.Length; k++)
            {
                if (vs[k] == null || r.frames[k] == null) continue;
                var fr = r.frames[k][Mathf.Min(now, r.frames[k].Length - 1)];
                if (fr.sonic && !fr.down) MachCone(vs[k].transform, k);
            }
        }

        /// <summary>A shock ring at p square to the flight path, radius m, fading with alpha.</summary>
        void ShockRing(Vector3 p, Vector3 fwd, float radius, float alpha)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(fwd, right).normalized;
            var c = boomColor; c.a *= alpha;
            for (int ring = 0; ring < 2; ring++)
            {
                float rr = radius * (ring == 0 ? 1f : 0.82f);
                boomPts.Clear();
                for (int k = 0; k < 40; k++) { float a = k * Mathf.PI / 20; boomPts.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * rr); }
                lines.Strip(boomPts, ring == 0 ? 2.2f : 0.9f, ring == 0 ? c : new Color(c.r, c.g, c.b, c.a * 0.5f), true);
            }
        }

        /// <summary>The Mach cone trailing a plane past the barrier: four lines swept back from its nose, and a ring at their ends.</summary>
        void MachCone(Transform t, int k)
        {
            float len = 60f, spread = len * 0.75f, flick = 0.75f + 0.25f * Mathf.Sin(Time.time * 23f + k);
            Vector3 nose = t.position + t.forward * 5f, back = nose - t.forward * len;
            var c = boomColor; c.a *= 0.55f * flick;
            boomPts.Clear();
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI / 12;
                Vector3 rim = back + (t.right * Mathf.Cos(a) + t.up * Mathf.Sin(a)) * spread;
                boomPts.Add(rim);
                if (i % 6 == 0) lines.Line(nose, rim, 0.9f, c);
            }
            lines.Strip(boomPts, 0.7f, new Color(c.r, c.g, c.b, c.a * 0.6f), true);
        }

        /// <summary>While planning: where your boom will go off on your path, its reach and a starburst.</summary>
        void DrawPreviewBooms()
        {
            if (preview == null || preview.booms == null || !plan.ram) return;
            foreach (var b in preview.booms)
            {
                if (b.owner != 0) continue;
                Vector3 p = UnityFrame.Pos(b.pos), fwd = UnityFrame.Dir(b.dir);
                ShockRing(p, fwd, (float)(Specials.BoomReachFt * Atmosphere.MPerFt), 0.35f);
                float spin = Time.time * 2f, star = Mathf.Max(6f, W(p) * 14f);
                for (int i = 0; i < 8; i++)
                {
                    float a = spin + i * Mathf.PI / 4;
                    Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    if (view != null) d = view.Cam.transform.right * d.x + view.Cam.transform.up * d.y;
                    lines.Line(p + d * star * 0.3f, p + d * star * (i % 2 == 0 ? 1f : 0.6f), 1.2f, boomColor);
                }
            }
            if (preview.shoves != null)
                foreach (var s in preview.shoves)
                    if (s.owner == 0 && s.target < views.Length && views[s.target] != null)
                    {
                        Vector3 p = UnityFrame.Pos(s.pos), jolt = UnityFrame.Dir(s.dv.Normalized);
                        lines.Line(p, p + jolt * 40f, 1.2f, new Color(boomColor.r, boomColor.g, boomColor.b, 0.6f));
                    }
        }
    }
}
