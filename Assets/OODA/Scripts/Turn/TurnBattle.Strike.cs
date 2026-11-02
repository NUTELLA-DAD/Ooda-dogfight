using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The Town Strike: before the mission, a map of the island. The player clicks where on the coast the bombers come
    /// in, then up to three turn points, each at Low (a little over the ground there), Medium or High; the bombers fly
    /// that path to the factory in the town and on past it. The map shades what the radar sites can see at the selected
    /// point's height (the mountains cut holes in it), draws the patrol's loop round the town (not where on it they will
    /// be), the 4 km ring inside which the town always sees you, and colours the path where the radar would see it and
    /// where it would fly into the ground. Fly this plan starts the battle; Restart comes back to the map.
    /// </summary>
    public partial class TurnBattle
    {
        [Header("Town strike")]
        [Tooltip("The heights a turn point can be set to: Low over the ground beneath it, Medium and High above the sea, m.")]
        public float strikeLowM = 250, strikeMediumM = 1200, strikeHighM = 2400;
        public Color radarShade = new Color(1f, 0.2f, 0.15f, 0.38f);

        const float IslandHalfM = 10000f, CoastM = 9400f;
        static readonly string[] HeightNames = { "Low", "Medium", "High" };

        Transform townFactory, townRunwayStart, townRunwayEnd;
        readonly List<Vector3> radarSites = new List<Vector3>();
        bool townLooked;

        bool CanStrike => CanSquadron && grid != null && FindTown();

        bool FindTown()
        {
            if (!townLooked)
            {
                townLooked = true;
                var town = GameObject.Find("Town");
                if (town != null)
                {
                    townFactory = town.transform.Find("FactoryTarget");
                    townRunwayStart = town.transform.Find("RunwayStart");
                    townRunwayEnd = town.transform.Find("RunwayEnd");
                    foreach (Transform t in town.transform)
                        if (t.name.StartsWith("RadarSite")) radarSites.Add(t.position);
                }
            }
            return townFactory != null && townRunwayStart != null && townRunwayEnd != null;
        }

        // ---------- the plan ----------

        Vector2 planStart = new Vector2(0, -CoastM);
        readonly List<Vector2> planPoints = new List<Vector2>();
        readonly List<int> planHeights = new List<int> { 0 };   // the start's, then each turn point's
        int planSel;              // 0 the start, k turn point k
        bool briefing, flyPlan;
        int dragPoint = -1;

        Vector2 Factory2 => new Vector2(townFactory.position.x, townFactory.position.z);

        float GroundM(Vector2 p) => Mathf.Max(0f, (float)(grid.At(p.x * Atmosphere.FtPerM, p.y * Atmosphere.FtPerM) * Atmosphere.MPerFt));

        float LevelM(int level, Vector2 at) => level == 0 ? GroundM(at) + strikeLowM : level == 1 ? strikeMediumM : strikeHighM;

        /// <summary>The bombers' route, metres (x, height, z): the start, the turn points, the factory, and on 4 km past it.</summary>
        List<Vector3> PlannedRoute()
        {
            var r = new List<Vector3> { new Vector3(planStart.x, LevelM(planHeights[0], planStart), planStart.y) };
            for (int k = 0; k < planPoints.Count; k++) r.Add(new Vector3(planPoints[k].x, LevelM(planHeights[k + 1], planPoints[k]), planPoints[k].y));
            int last = planHeights[planHeights.Count - 1];
            Vector2 f = Factory2, from = planPoints.Count > 0 ? planPoints[planPoints.Count - 1] : planStart;
            float fy = LevelM(last, f);
            r.Add(new Vector3(f.x, fy, f.y));
            Vector2 on = f + (f - from).normalized * 4000f;
            r.Add(new Vector3(on.x, Mathf.Max(fy, LevelM(last, on)), on.y));
            return r;
        }

        /// <summary>Snap a point to the coast: out to the nearest edge of the square the island sits in.</summary>
        static Vector2 ToCoast(Vector2 p)
        {
            float x = Mathf.Clamp(p.x, -CoastM, CoastM), z = Mathf.Clamp(p.y, -CoastM, CoastM);
            if (Mathf.Abs(x) >= Mathf.Abs(z)) x = Mathf.Sign(x == 0 ? 1 : x) * CoastM; else z = Mathf.Sign(z) * CoastM;
            return new Vector2(x, z);
        }

        // ---------- the battle ----------

        Battle StrikeBattle()
        {
            var route = PlannedRoute().Select(Ft).ToArray();
            var flak = flakSites == null ? new Vec3d[0] : flakSites.Where(f => f != null).Select(f => Ft(f.position)).ToArray();
            return Scenarios.TownStrike(route, Ft(townFactory.position), radarSites.Select(Ft).ToArray(), PatrolLoop(),
                                        Ft(townRunwayStart.position), Ft(townRunwayEnd.position), flak, Math.Max(160, bomberKias));
        }

        Vec3d[] patrolLoop;

        /// <summary>
        /// The patrol's loop: a ring round the factory, the radius (4.5 to 6.5 km) whose highest ground is lowest, flown
        /// 400 m over that (at least 1,500 m), so it clears everything on the way round. Feet.
        /// </summary>
        Vec3d[] PatrolLoop()
        {
            if (patrolLoop != null) return patrolLoop;
            Vector2 c = Factory2;
            float bestR = 5500, bestTop = float.MaxValue;
            for (float r = 4500; r <= 6500; r += 250)
            {
                float top = 0;
                for (int a = 0; a < 360; a += 2)
                    top = Mathf.Max(top, GroundM(c + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * r));
                if (top < bestTop) { bestTop = top; bestR = r; }
            }
            float alt = Mathf.Max(1500f, bestTop + 400f);
            const int n = 16;
            patrolLoop = new Vec3d[n];
            for (int k = 0; k < n; k++)
            {
                float a = -k * 2 * Mathf.PI / n;   // clockwise seen from above
                patrolLoop[k] = Ft(new Vector3(c.x + Mathf.Cos(a) * bestR, alt, c.y + Mathf.Sin(a) * bestR));
            }
            return patrolLoop;
        }

        /// <summary>After a restart: the town strike goes back to the map, unless the restart was Fly this plan.</summary>
        void StrikeRestarted()
        {
            foreach (var b in booms) if (b.t != null) Destroy(b.t.gameObject);
            booms.Clear();
            briefing = Playing == Mission.TownStrike && !flyPlan;
            flyPlan = false;
            if (briefing) { planDirty = true; StartCover(); }
        }

        void UpdateBriefing()
        {
            lines.Begin();
            lines.End();
            tip.gameObject.SetActive(false);
            foreach (var h in holos) if (h != null) h.SetActive(false);
            if (cover == null && coverTask != null && coverTask.IsCompleted) MakeCoverTextures();
            if (!shotsStarted && Array.IndexOf(Environment.GetCommandLineArgs(), "-strikeshots") >= 0) { shotsStarted = true; StartCoroutine(StrikeShots()); }
        }

        void FlyPlan()
        {
            flyPlan = true;
            Restart();
            Notice("The bombers are on their way in. Stay hidden as long as you can.");
        }

        string StrikeOutcome()
        {
            var s = battle.strike;
            int bombers = 0, alive = 0;
            for (int i = 0; i < battle.Count; i++) if (battle.routes[i] != null) { bombers++; if (!battle.Out(i)) alive++; }
            int hit = s.Struck;
            string seen = !s.alerted ? "" : s.alertBy == "town" ? " They only saw you over the town." : $" The {(s.alertBy == "radar" ? "radar" : "patrol")} saw you {s.alertDistFt * Atmosphere.MPerFt / 1000:F0} km out.";
            if (battle.down[0]) return (battle.crashed[0] ? "You flew into the ground." : "You were shot down.") + (hit > 0 ? $" The factory was hit by {hit} of {bombers} B-17s." : " Mission failed.");
            if (hit == 0) return (alive == 0 ? "All the bombers went down." : "The bombers missed the factory.") + " Mission failed." + seen;
            return $"Factory hit by {hit} of {bombers} B-17s. Mission complete!" + seen;
        }

        // ---------- in the air: the alarm and the bombs ----------

        /// <summary>The look-out's state, top middle: not seen yet, or seen and how many fighters are up; and the bombs.</summary>
        void StrikePanel(float sw)
        {
            var s = battle?.strike;
            if (s == null) return;
            Rect r = new Rect(sw / 2 - 230, 10, 460, 50);
            guiRects.Add(r);
            GUI.Box(r, GUIContent.none);
            var st = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            st.normal.textColor = s.alerted ? new Color(1f, 0.4f, 0.3f) : new Color(0.5f, 1f, 0.6f);
            string by = s.alertBy == "radar" ? "Seen by radar" : s.alertBy == "patrol" ? "Seen by the patrol" : "Seen over the town";
            GUI.Label(new Rect(r.x, r.y + 2, r.width, 24), s.alerted ? $"{by}: {s.launched} of {s.scramble.Length} fighters off the runway" : "Not seen yet: the fighters are still on the runway", st);
            double d = double.PositiveInfinity;
            for (int i = 0; i < battle.Count; i++)
                if (battle.routes[i] != null && !battle.Out(i))
                {
                    Vec3d v = battle.planes[i].state.pos - s.target;
                    d = Math.Min(d, Math.Sqrt(v.x * v.x + v.y * v.y));
                }
            string togo = s.Struck > 0 ? $"Factory hit by {s.Struck} B-17{(s.Struck > 1 ? "s" : "")}" : double.IsInfinity(d) ? "" : $"Factory {d * Atmosphere.MPerFt / 1000:F1} km ahead";
            GUI.Label(new Rect(r.x, r.y + 24, r.width, 22), togo, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
        }

        /// <summary>During playback: the alarm the moment it goes off, and the bombs bursting on the factory as each bomber passes over.</summary>
        void StrikePlayback(double from, double to)
        {
            var s = battle.strike;
            if (s == null) return;
            if (s.alerted && s.alertTime > from && s.alertTime <= to)
            {
                Notice(s.alertBy == "radar" ? "Radar has seen you! The fighters are scrambling."
                     : s.alertBy == "patrol" ? "The patrol has seen you! They're coming, and the fighters are scrambling."
                     : "Over the town: they've seen you. The fighters are scrambling.");
                noticeUntil = Time.time + 5f;
            }
            for (int i = 0; i < s.struck.Length; i++)
                if (s.struck[i] && s.struckAt[i] > from && s.struckAt[i] <= to) Bombs(townFactory.position, i);
        }

        readonly List<(Transform t, float born, float life, float size, bool smoke)> booms = new List<(Transform, float, float, float, bool)>();
        Material boomMat, smokeMat;

        /// <summary>A stick of bombs walking across the factory: bursts one after another, then smoke that stays.</summary>
        void Bombs(Vector3 at, int seed)
        {
            if (boomMat == null)
            {
                boomMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.6f, 0.2f, 0.9f) };
                smokeMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.12f, 0.11f, 0.1f, 0.75f) };
            }
            var rnd = new System.Random(seed * 31 + 7);
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = at + new Vector3((float)rnd.NextDouble() * 160 - 80, 4, -90 + k * 24 + (float)rnd.NextDouble() * 10);
                booms.Add((Sphere(p, boomMat), Time.time + k * 0.18f, 1.1f, 30f + (float)rnd.NextDouble() * 15, false));
            }
            for (int k = 0; k < 3; k++)
            {
                Vector3 p = at + new Vector3((float)rnd.NextDouble() * 120 - 60, 20, (float)rnd.NextDouble() * 120 - 60);
                booms.Add((Sphere(p, smokeMat), Time.time + 0.8f + k * 0.3f, -1, 45f, true));
            }
        }

        Transform Sphere(Vector3 p, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.position = p;
            go.transform.localScale = Vector3.zero;
            return go.transform;
        }

        void FadeBooms()
        {
            for (int i = booms.Count - 1; i >= 0; i--)
            {
                var b = booms[i];
                float age = Time.time - b.born;
                if (age < 0) { b.t.localScale = Vector3.zero; continue; }
                if (b.smoke)
                {
                    // a column that rises and swells, and stays
                    b.t.localScale = new Vector3(b.size, b.size * 2.2f, b.size) * Mathf.Min(1.6f, 0.4f + age * 0.08f);
                    b.t.position += Vector3.up * Time.deltaTime * 4f * Mathf.Max(0, 1 - age / 30f);
                    continue;
                }
                if (age > b.life) { Destroy(b.t.gameObject); booms.RemoveAt(i); continue; }
                b.t.localScale = Vector3.one * b.size * Mathf.Sin(Mathf.PI * age / b.life);
            }
        }

        // ---------- the map: what the radar sees ----------

        Texture2D relief;
        Texture2D[] cover;                 // by height level: the radar's view, shaded
        Task<float[][]> coverTask;
        const int CoverN = 160;            // cells across the island

        void StartCover()
        {
            if (cover != null || coverTask != null || grid == null) return;
            var g = grid;
            var radars = radarSites.Select(p => Ft(p)).ToArray();
            double range = new Strike().radarRangeFt;
            float lowFt = strikeLowM * (float)Atmosphere.FtPerM, midFt = strikeMediumM * (float)Atmosphere.FtPerM, highFt = strikeHighM * (float)Atmosphere.FtPerM;
            coverTask = Task.Run(() =>
            {
                var res = new float[3][];
                for (int lv = 0; lv < 3; lv++)
                {
                    var a = new float[CoverN * CoverN];
                    Parallel.For(0, CoverN, j =>
                    {
                        for (int i = 0; i < CoverN; i++)
                        {
                            double x = (-IslandHalfM + (i + 0.5) * 2 * IslandHalfM / CoverN) * Atmosphere.FtPerM;
                            double y = (-IslandHalfM + (j + 0.5) * 2 * IslandHalfM / CoverN) * Atmosphere.FtPerM;
                            double ground = Math.Max(0, g.At(x, y));
                            double z = lv == 0 ? ground + lowFt : lv == 1 ? midFt : highFt;
                            if (z < ground + 50) { a[j * CoverN + i] = -1; continue; }   // in the mountain: you can't fly here at this height
                            var p = new Vec3d(x, y, z);
                            foreach (var r in radars)
                                if ((p - r).Length < range && Strike.Clear(g.At, r, p)) { a[j * CoverN + i] = 1; break; }
                        }
                    });
                    res[lv] = a;
                }
                return res;
            });
        }

        void MakeCoverTextures()
        {
            if (coverTask.IsFaulted) { Debug.LogException(coverTask.Exception); coverTask = null; return; }
            var res = coverTask.Result;
            cover = new Texture2D[3];
            for (int lv = 0; lv < 3; lv++)
            {
                var t = new Texture2D(CoverN, CoverN, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[CoverN * CoverN];
                for (int k = 0; k < px.Length; k++)
                    px[k] = res[lv][k] > 0 ? radarShade : res[lv][k] < 0 ? new Color(0.05f, 0.04f, 0.04f, 0.55f) : Color.clear;
                t.SetPixels(px);
                t.Apply();
                cover[lv] = t;
            }
        }

        /// <summary>The island from above: sea, meadow, forest, rock and snow by height, shaded from the north-west.</summary>
        void MakeRelief()
        {
            const int n = 256;
            relief = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var hs = new float[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    hs[j * n + i] = (float)(grid.At((-IslandHalfM + (i + 0.5) * 2 * IslandHalfM / n) * Atmosphere.FtPerM,
                                                     (-IslandHalfM + (j + 0.5) * 2 * IslandHalfM / n) * Atmosphere.FtPerM) * Atmosphere.MPerFt);
            var px = new Color[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float h = hs[j * n + i];
                    if (h < 2f) { px[j * n + i] = new Color(0.16f, 0.32f, 0.48f); continue; }
                    Color c = h < 600 ? Color.Lerp(new Color(0.42f, 0.58f, 0.32f), new Color(0.30f, 0.46f, 0.26f), h / 600f)
                            : h < 1400 ? Color.Lerp(new Color(0.30f, 0.46f, 0.26f), new Color(0.55f, 0.50f, 0.42f), (h - 600) / 800f)
                            : h < 2600 ? Color.Lerp(new Color(0.55f, 0.50f, 0.42f), new Color(0.92f, 0.93f, 0.95f), (h - 1400) / 1200f)
                            : new Color(0.95f, 0.96f, 0.98f);
                    float dx = hs[j * n + Mathf.Min(n - 1, i + 1)] - hs[j * n + Mathf.Max(0, i - 1)];
                    float dz = hs[Mathf.Min(n - 1, j + 1) * n + i] - hs[Mathf.Max(0, j - 1) * n + i];
                    float shade = Mathf.Clamp(1f - (dx - dz) * 0.0022f, 0.55f, 1.25f);   // lit from the north-west
                    px[j * n + i] = c * shade;
                }
            relief.SetPixels(px);
            relief.Apply();
        }

        // the path, sampled the way the bombers will fly it: their height easing toward each point's, climbing at StrikeClimbDeg and coming down at StrikeDescentDeg at most
        readonly List<(Vector2 p, float h, bool seen, bool crash)> profile = new List<(Vector2, float, bool, bool)>();
        bool planDirty = true;
        float seenKm, firstSeenKm;
        bool crashes;

        void UpdateProfile()
        {
            planDirty = false;
            profile.Clear();
            var route = PlannedRoute();
            var radars = radarSites.Select(p => Ft(p)).ToArray();
            double range = new Strike().radarRangeFt;
            float h = route[0].y, step = 100f;
            float up = Mathf.Tan((float)Scenarios.StrikeClimbDeg * Mathf.Deg2Rad) * step, down = Mathf.Tan((float)Scenarios.StrikeDescentDeg * Mathf.Deg2Rad) * step;
            seenKm = 0; firstSeenKm = -1; crashes = false;
            float flown = 0;
            Vector2 f = Factory2;
            for (int k = 0; k + 1 < route.Count - 1; k++)   // up to the factory; the leg past it doesn't matter
            {
                Vector2 a = new Vector2(route[k].x, route[k].z), b = new Vector2(route[k + 1].x, route[k + 1].z);
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / step));
                for (int s = 0; s < n; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / (float)n);
                    h = Mathf.MoveTowards(h, route[k + 1].y, route[k + 1].y > h ? up : down);
                    var pf = Ft(new Vector3(p.x, h, p.y));
                    bool seen = Vector2.Distance(p, f) > 4000f && radars.Any(r => (pf - r).Length < range && Strike.Clear(grid.At, r, pf));
                    bool crash = h < GroundM(p) + 40f;
                    if (seen) { seenKm += step / 1000f; if (firstSeenKm < 0) firstSeenKm = flown / 1000f; }
                    crashes |= crash;
                    profile.Add((p, h, seen, crash));
                    flown += step;
                }
            }
        }

        // ---------- the map: drawing and clicking ----------

        Rect mapRect;
        Vector2 ToMap(Vector2 w) => new Vector2(mapRect.x + (w.x + IslandHalfM) / (2 * IslandHalfM) * mapRect.width, mapRect.y + (1 - (w.y + IslandHalfM) / (2 * IslandHalfM)) * mapRect.height);
        Vector2 FromMap(Vector2 m) => new Vector2((m.x - mapRect.x) / mapRect.width * 2 * IslandHalfM - IslandHalfM, (1 - (m.y - mapRect.y) / mapRect.height) * 2 * IslandHalfM - IslandHalfM);
        Vector2 ToMap(Vector3 w) => ToMap(new Vector2(w.x, w.z));

        void MapLine(Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            var keep = GUI.matrix;
            GUI.matrix = keep * Matrix4x4.Translate(a) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg)) * Matrix4x4.Translate(-a);
            Fill(new Rect(a.x, a.y - w / 2, len, w), c);
            GUI.matrix = keep;
        }

        void MapRing(Vector2 centreW, float radiusM, float w, Color c, bool dashed = false)
        {
            const int n = 72;
            for (int k = 0; k < n; k++)
            {
                if (dashed && k % 2 == 1) continue;
                float a0 = k * 2 * Mathf.PI / n, a1 = (k + 1) * 2 * Mathf.PI / n;
                MapLine(ToMap(centreW + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radiusM), ToMap(centreW + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radiusM), w, c);
            }
        }

        void MapDot(Vector2 at, float size, Color c) => Fill(new Rect(at.x - size / 2, at.y - size / 2, size, size), c);

        Vector2 PlanPoint(int k) => k == 0 ? planStart : planPoints[k - 1];

        int NearestPlanPoint(Vector2 mouse, float within)
        {
            int best = -1; float bestD = within;
            for (int k = 0; k <= planPoints.Count; k++)
            {
                float d = Vector2.Distance(mouse, ToMap(PlanPoint(k)));
                if (d < bestD) { bestD = d; best = k; }
            }
            return best;
        }

        void BriefingGUI(float sw, float sh)
        {
            if (relief == null) MakeRelief();
            if (planDirty) UpdateProfile();
            Fill(new Rect(0, 0, sw, sh), new Color(0.06f, 0.07f, 0.08f, 1f));
            float size = Mathf.Min(sh - 40, sw - 400);
            mapRect = new Rect(20, 20, size, size);
            GUI.DrawTexture(mapRect, relief);
            int level = planHeights[Mathf.Clamp(planSel, 0, planHeights.Count - 1)];
            if (cover != null) GUI.DrawTexture(mapRect, cover[level]);

            var small2 = new GUIStyle(small) { fontStyle = FontStyle.Bold };
            void Tag(Vector2 at, string text, Color c)
            {
                var st = new GUIStyle(small2); st.normal.textColor = new Color(0, 0, 0, 0.85f);
                GUI.Label(new Rect(at.x + 9, at.y - 8, 220, 20), text, st);   // a shadow, to read on the shading
                st.normal.textColor = c;
                GUI.Label(new Rect(at.x + 8, at.y - 9, 220, 20), text, st);
            }

            // the town: the 4 km ring, the factory, the runway; the radar sites; the patrol's loop
            Vector2 f = Factory2;
            MapRing(f, 4000f, 1.5f, new Color(1f, 1f, 1f, 0.7f), true);
            MapLine(ToMap(townRunwayStart.position), ToMap(townRunwayEnd.position), 3f, new Color(0.15f, 0.15f, 0.15f));
            MapDot(ToMap(f), 10, new Color(1f, 0.85f, 0.2f));
            Tag(ToMap(f), "Factory", new Color(1f, 0.9f, 0.4f));
            Tag(ToMap(townRunwayEnd.position) + new Vector2(-70, -4), "Runway", Color.white);
            foreach (var r in radarSites)
            {
                MapDot(ToMap(r), 9, new Color(1f, 0.25f, 0.2f));
                Tag(ToMap(r), "Radar", Color.white);
            }
            var loop = PatrolLoop();
            for (int k = 0; k < loop.Length; k++)
            {
                Vector2 a = ToMap(UnityFrame.Pos(loop[k])), b = ToMap(UnityFrame.Pos(loop[(k + 1) % loop.Length]));
                MapLine(a, Vector2.Lerp(a, b, 0.6f), 2.5f, new Color(1f, 0.6f, 0.15f, 0.9f));
            }
            Tag(ToMap(UnityFrame.Pos(loop[2])), "Patrol: 4 Bf 109s", new Color(1f, 0.7f, 0.3f));

            // the path: green hidden, red where the radar sees it, black where it flies into the ground
            for (int k = 0; k + 1 < profile.Count; k++)
            {
                var a = profile[k]; var b = profile[k + 1];
                Color c = a.crash ? Color.black : a.seen ? new Color(1f, 0.25f, 0.2f) : new Color(0.3f, 1f, 0.45f);
                MapLine(ToMap(a.p), ToMap(b.p), a.crash ? 5f : 3.5f, c);
            }
            if (profile.Count > 0) MapLine(ToMap(profile[profile.Count - 1].p), ToMap(f), 3.5f, new Color(0.3f, 1f, 0.45f));
            for (int k = 0; k <= planPoints.Count; k++)
            {
                Vector2 m = ToMap(PlanPoint(k));
                MapDot(m, k == planSel ? 16 : 12, k == planSel ? Color.white : new Color(0.1f, 0.1f, 0.1f));
                MapDot(m, k == planSel ? 10 : 8, k == 0 ? new Color(0.3f, 0.7f, 1f) : new Color(0.3f, 1f, 0.45f));
                Tag(m, (k == 0 ? "Start" : $"{k}") + $"  {HeightNames[planHeights[k]]}", Color.white);
            }

            // the panel on the right
            float px = mapRect.xMax + 20, pw = sw - px - 20, y = 24;
            var head = Ooda.UI.BriefingStyle.Heading(title, 24); head.wordWrap = true;
            var text = new GUIStyle(label) { wordWrap = true, fontSize = 15 };
            GUI.Label(new Rect(px, y, pw, 30), "Town Strike: plan the raid", head); y += 36;
            GUI.Label(new Rect(px, y, pw, 120),
                "Get the B-17s to the factory before the fighters are up. Click the coast to choose where they come in, " +
                "then click up to three turn points. Drag a point to move it; right-click to remove it.", text); y += 98;
            GUI.Label(new Rect(px, y, pw, 100),
                "Red shading: what the radar sees at the selected point's height. The mountains hide you behind them. " +
                "Inside the dashed ring the town sees you whatever you do. The patrol flies the orange loop.", text); y += 98;

            GUI.Label(new Rect(px, y, pw, 24), planSel == 0 ? "Selected: the start" : $"Selected: turn point {planSel}", title); y += 30;
            for (int lv = 0; lv < 3; lv++)
            {
                var st = new GUIStyle(GUI.skin.button) { fontStyle = level == lv ? FontStyle.Bold : FontStyle.Normal };
                if (level == lv) st.normal.textColor = st.hover.textColor = new Color(1f, 0.85f, 0.2f);
                if (GUI.Button(new Rect(px + lv * (pw / 3), y, pw / 3 - 6, 34), $"{HeightNames[lv]}  ({lv + 1})", st)) SetLevel(lv);
            }
            y += 40;
            GUI.Label(new Rect(px, y, pw, 40), level == 0 ? $"Low: {strikeLowM:F0} m over the ground there. Hides best, but mind the hills." :
                                             level == 1 ? $"Medium: {strikeMediumM:F0} m. Clear of most ridges." : $"High: {strikeHighM:F0} m. Over nearly everything, and seen from far off.", small); y += 40;

            var verdict = new GUIStyle(label) { wordWrap = true, fontStyle = FontStyle.Bold };
            verdict.normal.textColor = crashes ? new Color(1f, 0.3f, 0.25f) : seenKm > 0 ? new Color(1f, 0.6f, 0.3f) : new Color(0.4f, 1f, 0.5f);
            string v = crashes ? "This path flies into the ground (black). Raise those points or go round."
                     : seenKm > 0 ? $"The radar sees this path for {seenKm:F1} km, first {firstSeenKm:F1} km in."
                     : "The radar never sees this path. Watch out for the patrol.";
            GUI.Label(new Rect(px, y, pw, 50), cover == null ? "Working out what the radar can see..." : v, verdict); y += 56;

            if (GUI.Button(new Rect(px, y, pw, 48), "Fly this plan  (Enter)")) { FlyPlan(); return; }
            y += 56;
            GUI.enabled = planPoints.Count > 0;
            if (GUI.Button(new Rect(px, y, pw / 2 - 4, 30), "Remove point  (Del)")) RemovePoint(planSel > 0 ? planSel : planPoints.Count);
            if (GUI.Button(new Rect(px + pw / 2 + 4, y, pw / 2 - 4, 30), "Clear turn points")) { planPoints.Clear(); planHeights.RemoveRange(1, planHeights.Count - 1); planSel = 0; planDirty = true; }
            GUI.enabled = true;
            y += 36;
            if (GUI.Button(new Rect(px, y, pw, 30), $"Mission: {Playing}  (M)")) { SwitchMission(); return; }

            MapInput();
        }

        void SetLevel(int lv) { planHeights[planSel] = lv; planDirty = true; }

        void RemovePoint(int k)
        {
            if (k < 1 || k > planPoints.Count) return;
            planPoints.RemoveAt(k - 1);
            planHeights.RemoveAt(k);
            planSel = Mathf.Min(planSel, planPoints.Count);
            if (planSel == k) planSel = k - 1;
            planDirty = true;
        }

        void MapInput()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.Use(); FlyPlan(); return; }
                if (e.keyCode == KeyCode.Alpha1 || e.keyCode == KeyCode.Keypad1) { SetLevel(0); e.Use(); }
                if (e.keyCode == KeyCode.Alpha2 || e.keyCode == KeyCode.Keypad2) { SetLevel(1); e.Use(); }
                if (e.keyCode == KeyCode.Alpha3 || e.keyCode == KeyCode.Keypad3) { SetLevel(2); e.Use(); }
                if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) { RemovePoint(planSel); e.Use(); }
                if (e.keyCode == KeyCode.M) { e.Use(); SwitchMission(); }
                return;
            }
            if (!mapRect.Contains(e.mousePosition) && e.type != EventType.MouseUp) return;
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                int k = NearestPlanPoint(e.mousePosition, 16);
                if (k > 0) RemovePoint(k);
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                int k = NearestPlanPoint(e.mousePosition, 16);
                Vector2 w = FromMap(e.mousePosition);
                if (k >= 0) { planSel = k; dragPoint = k; }
                else if (Mathf.Max(Mathf.Abs(w.x), Mathf.Abs(w.y)) > CoastM - 600) { planStart = ToCoast(w); planSel = 0; dragPoint = 0; }   // a click by the coast moves the start
                else if (planPoints.Count < 3)
                {
                    planPoints.Add(w);
                    planHeights.Add(planHeights[planHeights.Count - 1]);
                    planSel = planPoints.Count; dragPoint = planSel;
                }
                else Notice("Up to three turn points: drag one to move it, or right-click it to remove it.");
                planDirty = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && dragPoint >= 0)
            {
                Vector2 w = FromMap(e.mousePosition);
                if (dragPoint == 0) planStart = ToCoast(w);
                else planPoints[dragPoint - 1] = new Vector2(Mathf.Clamp(w.x, -CoastM, CoastM), Mathf.Clamp(w.y, -CoastM, CoastM));
                planDirty = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp) dragPoint = -1;
        }

        // ---------- the self-test: -strikeshots on the command line ----------

        bool shotsStarted;

        /// <summary>
        /// Run with -strikeshots: the title goes straight to the Town Strike; this plans a path on the map, saves the map,
        /// flies a few turns and saves the battle (strike_map.bmp, strike_turn.bmp beside the build), then quits.
        /// </summary>
        System.Collections.IEnumerator StrikeShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            while (cover == null) yield return null;
            planStart = ToCoast(new Vector2(-9400, -6800));
            planPoints.Clear(); planHeights.Clear();
            planHeights.Add(1);
            foreach (var p in new[] { new Vector2(-4000, -3500), new Vector2(-1500, -1800) }) { planPoints.Add(p); planHeights.Add(0); }
            planSel = 1; planDirty = true;
            yield return new WaitForSeconds(1f);
            yield return Grab(System.IO.Path.Combine(dir, "strike_map.bmp"));
            yield return new WaitForSeconds(1f);
            FlyPlan();
            for (int t = 0; t < 4; t++)
            {
                yield return new WaitForSeconds(0.5f);
                Commit();
                while (phase != Phase.Planning && phase != Phase.Over) { playbackSpeed = 4; yield return null; }
            }
            yield return new WaitForSeconds(0.5f);
            yield return Grab(System.IO.Path.Combine(dir, "strike_turn.bmp"));
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        /// <summary>The screen as it stands at the end of this frame, to a BMP (the player has no PNG encoder).</summary>
        static System.Collections.IEnumerator Grab(string path)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, Bmp(tex));
            Destroy(tex);
        }

        static byte[] Bmp(Texture2D tex)
        {
            int w = tex.width, h = tex.height, row = (w * 3 + 3) & ~3;
            var px = tex.GetPixels32();   // bottom row first, as a BMP wants
            var b = new byte[54 + row * h];
            void Int(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
            b[0] = (byte)'B'; b[1] = (byte)'M'; Int(2, b.Length); Int(10, 54); Int(14, 40); Int(18, w); Int(22, h);
            b[26] = 1; b[28] = 24; Int(34, row * h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    int o = 54 + y * row + x * 3;
                    b[o] = c.b; b[o + 1] = c.g; b[o + 2] = c.r;
                }
            return b;
        }
    }
}
