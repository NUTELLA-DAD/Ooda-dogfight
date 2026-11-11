using System.Collections.Generic;
using UnityEngine;

namespace Ooda.Fx
{
    /// <summary>
    /// The rocket boost, in the same toon language as the blasts: a long inked flame out of the tail in flat bands (a dark
    /// red ink line round a red flame, an orange one, a yellow one and a white-hot core), with jagged edges that flicker in
    /// stepped keys, a row of white shock diamonds down the core, rings of shock pulsing back down the plume, a soft orange
    /// glow round it (the game has no bloom) and white speed lines streaming past the aircraft. It lights with a bang: a
    /// white flash, a star, a shock ring racing out round the tail and another round the plume, and sparks thrown back.
    /// Behind it, RocketPuff lays a thick trail of fire puffs that cool to red and then to grey smoke.
    /// Asked for each frame (Rocket), like the tracers, and drawn from the burn's age, so a replay looks the same.
    /// </summary>
    public sealed partial class ToonFx
    {
        struct RocketReq { public Vector3 P, D, C; public float S, Age, Burn; public int Seed; }
        readonly List<RocketReq> rocketQ = new List<RocketReq>(8);
        int rocketFrame = -1;

        static readonly Color RkInk = Hex("#5a1206"), RkRed = Hex("#e8401c"), RkOrange = Hex("#ff8f1f"), RkYellow = Hex("#ffe04a"), RkWhite = Hex("#fffbea");
        static readonly Color RkGlow = new Color(1f, 0.55f, 0.15f, 0.3f), RkSpeed = new Color(1f, 1f, 1f, 0.9f);
        // the flame's bands, outside in: how long (share of the flame), how wide (share), colour
        static readonly float[] RkLen = { 1.05f, 1f, 0.8f, 0.58f, 0.34f }, RkWid = { 1f, 1f, 0.72f, 0.5f, 0.27f };
        static readonly Color[] RkBand = { RkInk, RkRed, RkOrange, RkYellow, RkWhite };
        const int RkN = 14;          // samples down the flame
        const float RkLenM = 34f;    // the flame's length at size 1 (a fighter), m
        const float RkWidM = 2.2f;   // its half width at the widest, m

        /// <summary>
        /// A rocket burning this frame: its nozzle, the way the flame goes (aft), the aircraft's middle (for the speed
        /// lines), its size (1 for a fighter about 10 m long), seconds since it lit, and how lit it is (0 out to 1).
        /// </summary>
        public void Rocket(Vector3 nozzle, Vector3 back, Vector3 centre, float size, float age, float burn, int seed)
        {
            if (rocketFrame != Time.frameCount) { rocketFrame = Time.frameCount; rocketQ.Clear(); }
            if (burn <= 0.01f || back.sqrMagnitude < 1e-6f) return;
            rocketQ.Add(new RocketReq { P = nozzle, D = back.normalized, C = centre, S = Mathf.Max(0.2f, size), Age = Mathf.Max(0f, age), Burn = Mathf.Clamp01(burn), Seed = seed });
        }

        /// <summary>One puff of a rocket's trail at p: fire that cools to red, then grey smoke. One cluster per aircraft.</summary>
        public void RocketPuff(Vector3 p, Vector3 v, float r, int cluster, int seed, float? t0 = null)
        {
            float t = t0 ?? Now;
            AddPuff(p, v, r * Rng(seed, 1, 0.8f, 1.25f), Rng(seed, 2, 1.2f, 1.8f), t, RocketS, 2.4f, 1.1f, 1.8f, (seed & 15) == 0, cluster);
        }

        void DrawRockets(float now)
        {
            if (rocketFrame != Time.frameCount && ClockOverride < 0f) return;   // (nobody asked this frame)
            for (int i = 0; i < rocketQ.Count; i++) DrawRocket(rocketQ[i]);
        }

        readonly List<Vector3> rkL = new List<Vector3>(RkN + 1), rkR = new List<Vector3>(RkN + 1), rkC = new List<Vector3>(RkN + 1);
        readonly float[] rkW = new float[RkN + 1], rkJagL = new float[RkN + 1], rkJagR = new float[RkN + 1];

        void DrawRocket(in RocketReq q)
        {
            int key = Mathf.FloorToInt(q.Age * KeysPerSec);
            float ka = key / KeysPerSec;   // (the age, stepped)
            Vector3 d = q.D;
            float dist = Vector3.Dot(q.P - camPos, camFwd);
            if (dist < 0.3f && Vector3.Dot(q.P + d * RkLenM * q.S - camPos, camFwd) < 0.3f)
            {
                // all of it behind the camera (riding it from the cockpit): only the lines rushing past
                SurfaceAxes(d, out Vector3 b1, out Vector3 b2);
                SpeedLines(q, q.S, key, b1, b2, 0f);
                return;
            }
            // it reads from the far chase view too: seen end on from behind its length is lost, so then it is held big by its width
            float endOn = Mathf.Abs(Vector3.Dot(d, camFwd));
            float s = q.S * Mathf.Min(3f, Mathf.Max(MinScreen(q.P, RkLenM * q.S * (1f - endOn), 0.22f), MinScreen(q.P, 2f * RkWidM * q.S, 0.09f)));
            int seed = q.Seed;
            // it lights long and fat, then settles to a flicker; it shrinks as it burns out
            float ign = ka < 0.45f ? 1f + 0.9f * (1f - ka / 0.45f) : 1f;
            float flick = 0.86f + 0.28f * R01(seed, key);
            float L = RkLenM * s * flick * ign * Mathf.Lerp(0.25f, 1f, q.Burn);
            float W = RkWidM * s * (1f + 0.6f * endOn) * Mathf.Lerp(1f, 1.35f, (ign - 1f) / 0.9f) * Mathf.Lerp(0.55f, 1f, q.Burn);
            Vector3 side = Vector3.Cross(d, toCam);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : right;
            SurfaceAxes(d, out Vector3 e1, out Vector3 e2);
            float px = Mathf.Max(0.3f, dist) / pxPerM1;   // (metres a pixel at the nozzle)
            float lw = Mathf.Max(0.1f * s, 1.8f * px);

            // the glow round it, under everything
            SoftDisc(q.P + d * (L * 0.22f), W * 3.4f, new Color(RkGlow.r, RkGlow.g, RkGlow.b, RkGlow.a * q.Burn), 20);
            // (the bang's flash is held to a share of the view when the camera is close, so a cockpit close-up isn't washed out)
            float near = Mathf.Max(0.5f, dist) * 0.3f;
            if (key <= 4) SoftDisc(q.P, Mathf.Min(W * (6f - key), near * 1.5f), new Color(1f, 0.85f, 0.5f, 0.5f - 0.1f * key), 22);

            SpeedLines(q, s, key, e1, e2, px);

            // the shape down the flame: a wiggling centre line and two jagged edges, the same for every band
            for (int i = 0; i <= RkN; i++)
            {
                float u = i / (float)RkN;
                rkW[i] = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(u / 0.18f)) * Mathf.Pow(1f - u, 0.75f);
                rkJagL[i] = 1f + 0.24f * (R01(seed, key * 97 + i * 2) - 0.5f) * 2f;
                rkJagR[i] = 1f + 0.24f * (R01(seed, key * 97 + i * 2 + 1) - 0.5f) * 2f;
            }
            for (int b = 0; b < RkBand.Length; b++)
            {
                float lb = L * RkLen[b], wb = W * RkWid[b], extra = b == 0 ? lw : 0f;
                rkL.Clear(); rkR.Clear(); rkC.Clear();
                for (int i = 0; i <= RkN; i++)
                {
                    float u = i / (float)RkN;
                    Vector3 c = q.P + d * (u * lb) + side * (Mathf.Sin(u * 5.5f + key * 1.7f + seed) * 0.2f * W * u);
                    float w = wb * rkW[i];
                    rkC.Add(c);
                    rkL.Add(c + side * (w * rkJagL[i] + extra));
                    rkR.Add(c - side * (w * rkJagR[i] + extra));
                }
                Strip(rkL, rkR, RkBand[b]);
                // round lobes down it: a scalloped toon flame from the side, rings of fire seen end on (the chase view)
                for (int i = 0; i < RkN; i += 2)
                {
                    float r = wb * rkW[i] * 0.95f + extra;
                    if (r * pxPerM1 / Mathf.Max(0.3f, Vector3.Dot(rkC[i] - camPos, camFwd)) < 1f) continue;
                    Circle(rkC[i], right, up, r, r / px > 30f ? 22 : 12, RkBand[b]);
                }
            }

            // shock diamonds down the core, stepping back and forth a little with the keys
            for (int k = 0; k < 5; k++)
            {
                float u = 0.1f + 0.12f * k + 0.015f * ((key + k) & 1);
                if (u > 0.62f) break;
                Vector3 c = q.P + d * (u * L) + side * (Mathf.Sin(u * 5.5f + key * 1.7f + seed) * 0.2f * W * u);
                float sh = 1f - 0.14f * k, hl = W * 0.5f * sh, hw = W * 0.26f * sh;
                pts.Clear(); pts.Add(c + d * hl); pts.Add(c + side * hw); pts.Add(c - d * hl); pts.Add(c - side * hw);
                Polygon(c, pts, RkYellow);
                pts.Clear(); pts.Add(c + d * hl * 0.62f); pts.Add(c + side * hw * 0.55f); pts.Add(c - d * hl * 0.62f); pts.Add(c - side * hw * 0.55f);
                Polygon(c, pts, Color.white);
            }

            // rings of shock pulsing back down the plume, round it (circles seen from behind, bars across it from the side)
            for (int j = 0; j < 2; j++)
            {
                float ra = Mathf.Repeat(ka + j * 0.25f, 0.5f) / 0.5f;
                Color rc = new Color(1f, 0.96f, 0.82f, 0.9f * (1f - ra) * q.Burn);
                TaperRing(q.P + d * (L * (0.15f + 0.75f * ra)), e1, e2, W * (1.15f + 1.7f * ra), Mathf.Max(lw, W * 0.28f * (1f - ra)), seed + j * 7 + key / 7, 3, rc);
            }

            // the bang as it lights
            if (key <= 6)
            {
                Vector3 p = q.P + toCam * (0.5f * s);
                float fw = Mathf.Min(W, near / 3f);
                bool close = dist < 30f;   // (a cockpit close-up: the rings and sparks, not a flat disc filling the view)
                if (key == 0 && !close) { Circle(p, right, up, fw * 4.2f, 28, new Color(1f, 0.92f, 0.5f, 0.6f)); Circle(p + toCam * 0.01f, right, up, fw * 3f, 24, FlashWhite); }
                if ((key == 1 || key == 2) && !close) Star(p, fw * (key == 1 ? 3.6f : 2.6f), fw * 1.1f, 8, seed + key, key == 1 ? FlashWhite : FlashYellow, R01(seed, 3) * 6.28f);
                if (key <= 5 && !close)
                {
                    TaperRing(p, right, up, W * (2.2f + 1.7f * key), Mathf.Max(lw, W * (0.7f - 0.1f * key)), seed + 11, 4, new Color(1f, 0.98f, 0.9f, 1f));
                    TaperRing(q.P + d * (key * 1.6f * s), e1, e2, W * (1.6f + 1.4f * key), Mathf.Max(lw, W * (0.55f - 0.08f * key)), seed + 13, 3, new Color(1f, 0.85f, 0.45f, 1f));
                }
                if (!close) Sparks(p, s, key, seed, 9, 24f, 2.8f, d);
                else TaperRing(q.P + d * (key * 1.6f * s), e1, e2, fw * (1.6f + 1.4f * key), Mathf.Max(lw, fw * 0.3f), seed + 13, 3, new Color(1f, 0.85f, 0.45f, 0.8f));
            }
        }

        // from the cockpit: anime zoom lines, streaks flying out to the edges of the view from round its middle, a new set each key
        void ZoomLines(in RocketReq q, int key, Color col)
        {
            const int N = 30;
            const float D = 1.2f;   // (just past the windscreen: the frame and the panel still stand in front of them)
            float h = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * D, px = D / pxPerM1;
            Vector3 c = camPos + camFwd * D;
            Color ink = new Color(0.16f, 0.2f, 0.32f, 0.5f * col.a);
            for (int j = 0; j < N; j++)
            {
                int k = key * 37 + j;
                if (R01(q.Seed, k + 960) < 0.3f) continue;
                float a = (j + Rng(q.Seed, k + 970, -0.4f, 0.4f)) * Mathf.PI * 2f / N;
                Vector3 dir = right * Mathf.Cos(a) * cam.aspect + up * Mathf.Sin(a);
                float r0 = Rng(q.Seed, k + 980, 0.55f, 0.95f) * h, r1 = r0 + Rng(q.Seed, k + 990, 0.35f, 0.9f) * h;
                pts.Clear(); pts.Add(c + dir * r0); pts.Add(c + dir * Mathf.Lerp(r0, r1, 0.5f)); pts.Add(c + dir * r1);
                float w = Rng(q.Seed, k + 995, 3f, 7f) * px;
                Stroke(pts, px, w * 1.8f, ink);
                Stroke(pts, px * 0.5f, w, col);
            }
        }

        // anime speed lines: white streaks streaming aft past the aircraft, a new set each key
        void SpeedLines(in RocketReq q, float s, int key, Vector3 e1, Vector3 e2, float px)
        {
            const int N = 22;
            float alpha = RkSpeed.a * q.Burn;
            if (alpha < 0.05f) return;
            Color col = new Color(RkSpeed.r, RkSpeed.g, RkSpeed.b, alpha);
            if ((q.C - camPos).sqrMagnitude < 8f * 8f) { ZoomLines(q, key, col); return; }   // (riding it, from the cockpit)
            for (int j = 0; j < N; j++)
            {
                int h = key * 31 + j;
                if (R01(q.Seed, h + 900) < 0.35f) continue;
                float a = Rng(q.Seed, h + 910, 0f, 6.283f), rr = Rng(q.Seed, h + 920, 2.5f, 9f) * s;
                Vector3 start = q.C + (e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a)) * rr - q.D * (Rng(q.Seed, h + 930, -4f, 28f) * s);   // (from well ahead, so they stream past the cockpit too)
                float len = Rng(q.Seed, h + 940, 9f, 20f) * s;
                Vector3 end = start + q.D * len;
                float dd = Mathf.Max(0.3f, Vector3.Dot(start - camPos, camFwd));
                if (dd < 0.5f) continue;
                float w = Mathf.Max(0.14f * s, 2.6f * dd / pxPerM1);
                pts.Clear(); pts.Add(start); pts.Add(Vector3.Lerp(start, end, 0.3f)); pts.Add(end);
                Stroke(pts, w * 1.9f, 0.02f, new Color(0.16f, 0.2f, 0.32f, 0.45f * q.Burn));   // (a dusky edge, so they show against a pale sky)
                Stroke(pts, w, 0.01f, col);
            }
        }
    }
}
