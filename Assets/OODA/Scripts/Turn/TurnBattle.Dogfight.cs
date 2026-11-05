using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// Reading the enemy, and the one on one as a loop to learn and tune it on. While the player plans, the AI pilots'
    /// own tries are scored off the main thread (the same ones they pick from when the turn is flown), so the preview
    /// flies each enemy on its likeliest plan rather than "if they hold", and the enemy in focus shows a spread of up
    /// to four ghosts, the likeliest bright, each with its chance. When some arrow can hit the bright ghost, a lead
    /// reticle sits on its path where the rounds would meet it, and your tracers round that moment show how close you are.
    /// After each turn a line says how far the enemy ended from the bright ghost. The one on one begins one of four ways at random, and every
    /// fight is logged to DogfightLog.txt beside the game (Logs/ in the editor). -dogfightbot N flies N fights by
    /// itself (the arrow the lead was found with when it has hits, else an AI pilot's plan), logs them and quits.
    /// </summary>
    public partial class TurnBattle
    {
        [Header("Dogfight: the ghost spread and the lead")]
        public Color leadColor = new Color(1f, 0.55f, 0.1f, 0.95f);
        public Color leadOnColor = new Color(0.3f, 1f, 0.35f, 1f);
        public Color leadLinedColor = new Color(1f, 0.95f, 0.25f, 1f);
        [Tooltip("The most of the enemy's likely endings to show, the likeliest bright.")]
        public int spreadCount = 4;

        /// <summary>What the pilots might do this turn, and where the player's guns can reach the likeliest, worked out off the main thread.</summary>
        class Reading
        {
            public Prediction[] pred;            // by aircraft; null for the player, the bombers and anyone not planning for itself
            public int target = -1;              // the enemy the spread and the lead are for (the one in focus)
            public AircraftFrame[][] spread;     // the target's path flying each of its likely plans (with the player holding)
            public AircraftFrame holdEnd;        // where the old "if they hold" guess put it, for the log
            public Vec3d? lead;                  // the arrow for the most hits on the bright ghost, firing all turn
            public int leadHits;
            public double leadMissFt = double.PositiveInfinity;   // with no hits, how close the nearest round passes
            public Vec3d leadPoint;              // with hits: where on the bright ghost's path they land (the middle one), ft
            public double leadFire;              // and when the rounds that land there are fired, s into the turn
        }

        Task<Reading> reading;
        Battle readingFor;
        Reading read;
        GameObject[] spreadHolos = new GameObject[0];
        string ghostReport;
        System.Random startDice = new System.Random();
        Scenarios.Start dogStart;
        int dogArena;

        bool Dogfighting => Playing == Mission.Dogfight;

        void MakeSpreadHolos() => RemakeSpreadHolos(enemy.gameObject);

        GameObject spreadSource;

        /// <summary>The fainter ghosts made again from another model (the dogfight's enemy lead may be any type).</summary>
        void RemakeSpreadHolos(GameObject source)
        {
            if (source == null || source == spreadSource) return;
            spreadSource = source;
            foreach (var h in spreadHolos) if (h != null) Destroy(h);
            foreach (var m in spreadMats) if (m != null) { holoMats.Remove(m); Destroy(m); }
            spreadMats.Clear();
            int before = holoMats.Count;
            spreadHolos = new GameObject[Mathf.Max(0, spreadCount - 1)];
            for (int k = 0; k < spreadHolos.Length; k++) spreadHolos[k] = MakeHolo(source, holoEnemyColor, $"GhostEnemy {k + 2}");
            for (int k = before; k < holoMats.Count; k++) spreadMats.Add(holoMats[k]);
        }

        readonly List<Material> spreadMats = new List<Material>();

        void HideSpread() { foreach (var h in spreadHolos) if (h != null) h.SetActive(false); }

        /// <summary>The dogfight as the setup screen set it up; a random start is a new one each time.</summary>
        Battle DogfightBattle()
        {
            var s = dogSetup.Copy();
            if (botStart != null) { s.randomStart = false; s.start = botStart.Value; }
            if (botArena != null) { s.randomArena = false; s.arena = botArena.Value; }
            s = s.Resolved(startDice);
            dogStart = s.start;
            dogArena = s.arena;
            dogCraft = new Craft[s.Ours + s.Theirs];   // in the battle's order: you, their lead, your wingmen, their others
            dogCraft[0] = s.ours[0]; dogCraft[1] = s.theirs[0];
            for (int k = 1; k < s.Ours; k++) dogCraft[1 + k] = s.ours[k];
            for (int k = 1; k < s.Theirs; k++) dogCraft[s.Ours + k] = s.theirs[k];
            dogPilots = new PilotKind[dogCraft.Length];                // the same order
            dogPilots[0] = s.PilotOf(true, 0); dogPilots[1] = s.PilotOf(false, 0);
            for (int k = 1; k < s.Ours; k++) dogPilots[1 + k] = s.PilotOf(true, k);
            for (int k = 1; k < s.Theirs; k++) dogPilots[s.Ours + k] = s.PilotOf(false, k);
            // over the arena picked: its middle, its height and its axis (startCentre and startAltFt are only for a world without arenas)
            var a = Arenas.Get(s.arena);
            var b = useArenas ? Scenarios.Dogfight(s, a.CentreFt, a.altFt, startKias, startGapM, true, a.axisDeg)
                              : Scenarios.Dogfight(s, UnityFrame.Pos(startCentre), startAltFt, startKias, startGapM);
            // a low start at long range can put a flight under a ridge: it comes in over it instead (Scenarios.LiftClear)
            double lift = Scenarios.LiftClear(b, GroundFt);
            if (lift > 0) Debug.Log($"OODA: the dogfight over {Arenas.Get(s.arena).name} starts with a flight up to {lift:F0} ft higher than set, to clear the ground.");
            return b;
        }

        /// <summary>The fight's setup: from the setup screen (or -dogfightsides N M for the bot).</summary>
        DogfightSetup dogSetup = new DogfightSetup();
        /// <summary>The aircraft each plane flies, by its index when the fight began (null outside the dogfight).</summary>
        Craft[] dogCraft;
        /// <summary>Who flies each plane, in the same order (null outside the dogfight: everyone's usual pilot).</summary>
        PilotKind[] dogPilots;
        int dogOurs => dogSetup.Ours;
        int dogTheirs => dogSetup.Theirs;

        string DogfightNotice => dogOurs == 1 && dogTheirs == 1
            ? $"Dogfight over {ArenaName}: your {DogfightSetup.Short(dogSetup.ours[0])} against a {DogfightSetup.Short(dogSetup.theirs[0])}, {Scenarios.Name(dogStart)}."
            : $"Dogfight {dogOurs} v {dogTheirs} over {ArenaName}: {DogfightSetup.Describe(dogSetup.ours)} against {DogfightSetup.Describe(dogSetup.theirs)}, {Scenarios.Name(dogStart)}. Tab: your next plane, N: next target.";

        string ArenaName => useArenas ? Arenas.Get(dogArena).name : "the valley";

        // ---------- reading the enemy ----------

        static bool PlansItself(Battle b, EnemyAi[] pilots, int i) =>
            pilots[i] != null && !b.Out(i) && !b.InFormation(i) && (b.strike == null || !b.strike.OnPatrol(i));

        /// <summary>Start working out what the pilots might do this turn (at the start of planning).</summary>
        void StartReading()
        {
            read = null;
            var b = battle; var pilots = ais; double ts = turnSec; int target = focus; var mine = plan; var e = env; int most = Mathf.Max(1, spreadCount);
            readingFor = b;
            reading = Task.Run(() => Read(b, pilots, ts, target, mine, e, most));
        }

        void FinishReading()
        {
            var task = reading; reading = null;
            if (readingFor != battle) return;   // restarted meanwhile
            if (task.IsFaulted) { Debug.LogException(task.Exception); return; }
            read = task.Result;
            dirty = true;   // the preview flies them on their likeliest plans now
        }

        static Reading Read(Battle b, EnemyAi[] pilots, double ts, int target, TurnPlan mine, Envelope e, int most)
        {
            int n = b.Count;
            var r = new Reading { pred = new Prediction[n] };
            Parallel.For(1, n, i => { if (PlansItself(b, pilots, i)) r.pred[i] = pilots[i].Predict(b, ts, most); });
            if (target < 1 || target >= n || r.pred[target] == null) return r;
            r.target = target;

            Battle Sim()
            {
                var s = b.Clone();
                s.dispersion = false;
                s.obstacleHit = s.wires == null ? null : (Func<Vec3d, Vec3d, bool>)s.wires.Hit;   // off the main thread: the plain shapes, not the colliders
                return s;
            }
            var others = new TurnPlan?[n];
            for (int i = 1; i < n; i++) if (r.pred[i] != null) others[i] = r.pred[i].plans[0];

            // the target's path on each of its likely plans, and on the old guess
            var pt = r.pred[target];
            r.spread = new AircraftFrame[pt.plans.Length][];
            Parallel.For(0, pt.plans.Length, k =>
            {
                var plans = (TurnPlan?[])others.Clone();
                plans[target] = pt.plans[k];
                r.spread[k] = Sim().Resolve(plans, ts, true).frames[target];
            });
            {
                var plans = (TurnPlan?[])others.Clone();
                plans[target] = null;
                var f = Sim().Resolve(plans, ts, true).frames[target];
                r.holdEnd = f[f.Length - 1];
            }

            // the lead: try arrows across the reach shell, firing all turn at the bright ghost, then look closer round the best
            if (b.ammo[0] <= 0 || b.Out(0)) return r;
            double Score(double phi, double frac, out Vec3d aim, out int hits, out double miss, out Vec3d point, out double fire)
            {
                frac = Math.Max(0, Math.Min(1, frac));
                aim = frac <= 0 ? e.fwd : e.Direction(phi, e.MaxAngle(phi) * frac);
                var p = mine;
                p.aim = aim; p.overpull = 0; p.marks = null; p.burstStart = 0; p.burstEnd = ts; p.missile = false;
                var s = Sim();
                for (int j = 0; j < s.Count; j++) if (s.side[j] != s.side[0]) s.health[j] = double.PositiveInfinity;
                s.bullets.Clear();
                var plans = (TurnPlan?[])others.Clone();
                plans[0] = p;
                var res = s.Resolve(plans, ts, true);
                var on = res.hits.Where(h => h.shooter == 0 && h.target == target).OrderBy(h => h.fireTime).ToList();
                hits = on.Count;
                point = hits > 0 ? on[hits / 2].pos : default;
                fire = hits > 0 ? on[hits / 2].fireTime - res.startTime : 0;
                miss = double.PositiveInfinity;
                foreach (var tr in res.traces)
                    if (tr.owner == 0 && !tr.missile && tr.closestTarget == target) miss = Math.Min(miss, tr.closestFt);
                if (s.crashed[0]) return double.NegativeInfinity;   // not a lead into the ground
                return hits * 1000.0 - Math.Min(miss, 1e5);
            }
            var tries = new List<(double phi, double frac)> { (0, 0) };
            const int spokes = 16;
            foreach (var frac in new[] { 0.2, 0.4, 0.6, 0.8, 1.0 })
                for (int k = 0; k < spokes; k++) tries.Add((2 * Math.PI * k / spokes, frac));
            (double score, double phi, double frac, Vec3d aim, int hits, double miss, Vec3d point, double fire) best = (double.NegativeInfinity, 0, 0, e.fwd, 0, double.PositiveInfinity, default, 0);
            object gate = new object();
            void Run(List<(double phi, double frac)> list)
            {
                Parallel.ForEach(list, t =>
                {
                    double sc = Score(t.phi, t.frac, out var aim, out int hits, out double miss, out var point, out double fire);
                    lock (gate) if (sc > best.score) best = (sc, t.phi, t.frac, aim, hits, miss, point, fire);
                });
            }
            Run(tries);
            double dPhi = 2 * Math.PI / spokes / 2, dFrac = 0.1;
            for (int pass = 0; pass < 2 && !double.IsNegativeInfinity(best.score); pass++)
            {
                var near = new List<(double, double)>();
                for (int a = -1; a <= 1; a++)
                    for (int c = -1; c <= 1; c++)
                        if (a != 0 || c != 0) near.Add((best.phi + a * dPhi, best.frac + c * dFrac));
                Run(near);
                dPhi /= 2; dFrac /= 2;
            }
            if (double.IsNegativeInfinity(best.score)) return r;
            r.lead = best.aim; r.leadHits = best.hits; r.leadMissFt = best.miss; r.leadPoint = best.point; r.leadFire = best.fire;
            return r;
        }

        /// <summary>Everyone's plans for a preview: the player's, and each AI pilot on its likeliest plan once they are read.</summary>
        TurnPlan?[] WithLikeliest(TurnPlan mine)
        {
            var p = new TurnPlan?[battle.Count];
            p[0] = mine;
            if (read != null)
                for (int i = 1; i < p.Length && i < read.pred.Length; i++) if (read.pred[i] != null) p[i] = read.pred[i].plans[0];
            ApplyManual(p);   // the planes of yours you have planned fly your plans
            NetDrafts(p);     // online: your wingman's fly what they are planning
            return p;
        }

        /// <summary>The hit chance on the spread: the planned turn flown against each of the target's likely plans, weighted by its chance. -1 not known.</summary>
        int SpreadChance(int j)
        {
            if (read == null || j < 0 || j >= read.pred.Length || read.pred[j] == null) return -1;
            var pr = read.pred[j];
            double hit = 0, all = 0;
            for (int k = 0; k < pr.plans.Length; k++)
            {
                var plans = WithLikeliest(plan);
                plans[j] = pr.plans[k];
                var sim = battle.Clone();
                sim.dispersion = false;
                var r = sim.Resolve(plans, turnSec, false);
                all += pr.chance[k];
                if (r.hits.Any(h => h.shooter == 0 && h.target == j)) hit += pr.chance[k];
            }
            return all > 0 ? Mathf.RoundToInt((float)(100 * hit / all)) : -1;
        }

        /// <summary>The reticle shows only when some arrow can hit the bright ghost this turn.</summary>
        bool LeadShown => read != null && read.leadHits > 0 && read.target >= 0 && read.target < battle.Count && !battle.Out(read.target);
        /// <summary>The guns line up: firing all turn on the planned path, rounds hit the bright ghost.</summary>
        bool LinedUp => LeadShown && gunPreview != null && gunPreview.hits.Any(h => h.shooter == 0 && h.target == read.target);
        /// <summary>The planned burst itself hits the bright ghost.</summary>
        bool LeadHit => LeadShown && read.target < burstHits.Length && burstHits[read.target];
        bool OnLead => LinedUp;

        // ---------- drawing it ----------

        /// <summary>The fainter ghosts (the target's other likely plans), and the lead reticle on the bright ghost's path with your tracers near it.</summary>
        void DrawSpreadAndLead()
        {
            HideSpread();
            if (read == null) return;
            int j = read.target;
            if (read.spread != null && showProjection && j >= 0 && j < battle.Count && !battle.Out(j) && !HiddenFromMe(j))
            {
                var pr = read.pred[j];
                for (int k = 1; k < read.spread.Length && k - 1 < spreadHolos.Length; k++)
                {
                    float rel = Mathf.Clamp01((float)(pr.chance[k] / Math.Max(1e-6, pr.chance[0])));
                    var fr = read.spread[k];
                    int end = fr.Length - 1;
                    var col = projectionColor; col.a *= 0.25f + 0.45f * rel;
                    DrawPath(fr, end, col, false);
                    var hc = holoEnemyColor; hc.a *= 0.2f + 0.35f * rel;
                    ShowHolo(spreadHolos[k - 1], fr[end], true, hc);
                }
            }
            if (!LeadShown || !showLeadRing || phase != Phase.Planning) return;
            // the reticle: a ring and four ticks where on its path the rounds can meet it, facing the camera
            Vector3 p = UnityFrame.Pos(read.leadPoint);
            Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
            float rad = Mathf.Max(4f, W(p) * 16f);
            var c = LeadHit ? leadOnColor : LinedUp ? leadLinedColor : leadColor;
            var ring = new List<Vector3>(24);
            for (int k = 0; k < 24; k++) { float a = 2 * Mathf.PI * k / 24; ring.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * rad); }
            lines.Strip(ring, 0.7f, c, true);
            foreach (var d in new[] { right, -right, up, -up }) lines.Line(p + d * rad, p + d * rad * 1.8f, 0.7f, c);
            // your tracers round the moment the lead fires (or the first moment yours connect), until a burst is set
            if (plan.burstEnd != plan.burstStart || gunPreview == null) return;
            double at = read.leadFire;
            var first = gunPreview.hits.Where(h => h.shooter == 0 && h.target == j).Select(h => h.fireTime - gunPreview.startTime).DefaultIfEmpty(-1).Min();
            if (first >= 0) at = first;
            foreach (var tr in gunPreview.traces)
            {
                if (tr.owner != 0 || tr.missile || Math.Abs(tr.startFrame * gunPreview.dt - at) > 0.4) continue;
                var col = OutcomeColor(tr, myTracer);
                col.a *= 0.6f;
                DrawPreviewTrace(tr, col, 0.35f, col.a * 0.3f);
            }
        }
        Vector2? GuiPoint(Vector3 world)
        {
            if (view == null || view.Cam == null) return null;
            Vector3 s = view.Cam.WorldToScreenPoint(world);
            if (s.z <= 0) return null;
            return new Vector2(s.x, Screen.height - s.y) / guiScale;
        }

        /// <summary>The chance beside each ghost, the reticle's label, and last turn's ghost miss.</summary>
        void DogfightGUI(float sw, float sh)
        {
            if (phase != Phase.Planning && phase != Phase.Thinking) return;
            var tag = new GUIStyle(small) { fontStyle = FontStyle.Bold };
            if (reading != null && read == null) Shadow(new Rect(sw / 2 - 120, sh * 0.16f, 240, 22), "Reading the enemy...", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            if (read != null)
            {
                int j = read.target;
                if (read.spread != null && showProjection && !SightViewOn && !JammedNow && j >= 0 && j < battle.Count && !battle.Out(j) && preview != null)
                {
                    var pr = read.pred[j];
                    tag.normal.textColor = projectionColor;
                    for (int k = 0; k < read.spread.Length; k++)
                    {
                        var f = k == 0 ? preview.frames[j][preview.FrameCount - 1] : read.spread[k][read.spread[k].Length - 1];
                        var g = GuiPoint(UnityFrame.Pos(f.pos));
                        if (g != null) Shadow(new Rect(g.Value.x + 14, g.Value.y - 22, 80, 20), Prediction.Percent(pr.chance[k]), tag);
                    }
                }
                if (LeadShown && showLeadRing && phase == Phase.Planning)
                {
                    var g = GuiPoint(UnityFrame.Pos(read.leadPoint));
                    tag.normal.textColor = LeadHit ? leadOnColor : LinedUp ? leadLinedColor : leadColor;
                    string text = LeadHit ? "HIT" : LinedUp ? "LINED UP: F to fire" : "LEAD: steer your tracers here";
                    if (g != null) Shadow(new Rect(g.Value.x + 22, g.Value.y + 4, 240, 20), text, tag);
                }
            }
            // last turn's ghost miss, just above the planning panel
            if (ghostReport != null && phase == Phase.Planning)
                Shadow(new Rect(0, sh - 178 - (showMoves || plan.HasMarks ? 266 : 70), sw, 22), ghostReport, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
        }

        /// <summary>A label with a dark shadow under it, so it reads over snow and grass alike.</summary>
        static void Shadow(Rect r, string text, GUIStyle style)
        {
            var dark = new GUIStyle(style);
            dark.normal.textColor = new Color(0, 0, 0, 0.85f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, dark);
            GUI.Label(r, text, style);
        }

        // ---------- the log, and how far the ghost was off ----------

        int fightNo, fightBursts, fightHits, fightTaken;
        double fightBurstSec;
        bool fightLogged;
        // the bot's running totals
        int botTurns, botBright, botKills, botLosses, botDraws, botLeadTurns;
        double botMiss, botHoldMiss;
        long botLeadHitsSaid, botLeadHitsGot;

        static string LogPath => Application.isEditor
            ? Path.Combine(Application.dataPath, "..", "Logs", "DogfightLog.txt")
            : Path.Combine(Application.dataPath, "..", "DogfightLog.txt");

        void Log(string line)
        {
            if (!Dogfighting) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch (Exception e) { Debug.LogWarning($"OODA: could not write the dogfight log: {e.Message}"); }
        }

        void FightStarted()
        {
            ghostReport = null;
            if (!Dogfighting) return;
            fightNo++; fightBursts = fightHits = fightTaken = 0; fightBurstSec = 0; fightLogged = false;
            Log($"fight {fightNo} begins over {ArenaName}: {Scenarios.Name(dogStart)}{(bot ? " (bot)" : "")}");
        }

        /// <summary>After a turn is flown: how far the enemy ended from the bright ghost, the line on screen, and the log.</summary>
        void TurnFlown(TurnPlan?[] plans, bool onLead)
        {
            ghostReport = null;
            var rd = read;
            int hits = last.hits.Count(h => h.shooter == 0), taken = last.hits.Count(h => h.target == 0);
            double burst = Math.Abs(plan.burstEnd - plan.burstStart);
            if (burst > 0) { fightBursts++; fightBurstSec += burst; }
            fightHits += hits; fightTaken += taken;
            if (rd?.spread != null && rd.target >= 0 && rd.target < battle.Count)
            {
                int j = rd.target;
                var pr = rd.pred[j];
                Vec3d actual = last.frames[j][last.FrameCount - 1].pos;
                Vec3d bright = rd.spread[0][rd.spread[0].Length - 1].pos;
                double miss = (actual - bright).Length, holdMiss = (actual - rd.holdEnd.pos).Length;
                int rank = -1;
                if (plans[j] is TurnPlan chosen) rank = pr.Rank(chosen);
                string name = battle.planes[j].numbers.name.Replace(" G-6", "");
                ghostReport = battle.Out(j) ? null
                    : rank == 0 ? $"Last turn the {name} flew the bright ghost: it ended {miss:F0} ft from it."
                    : rank > 0 ? $"Last turn the {name} took ghost {rank + 1} ({pr.chance[rank] * 100:F0}%): it ended {miss:F0} ft from the bright one."
                    : $"Last turn the {name} flew something outside the spread: it ended {miss:F0} ft from the bright ghost.";
                if (bot) { botTurns++; botMiss += miss; botHoldMiss += holdMiss; if (rank == 0) botBright++; }
                double range = (battle.planes[j].state.pos - battle.planes[0].state.pos).Length;
                Log($"fight {fightNo} turn {battle.turn}: ghost miss {miss:F0} ft (old hold guess {holdMiss:F0} ft), flew ghost {(rank < 0 ? "none" : (rank + 1).ToString())} of {pr.plans.Length} ({(rank >= 0 ? pr.chance[rank] * 100 : 0):F0}%, bright {pr.chance[0] * 100:F0}%), " +
                    $"lead {(rd.lead == null ? "none" : rd.leadHits + " hits")}{(onLead ? " ON LEAD" : "")}, burst {burst:F2} s, hits {hits}, hit by {taken}, range now {range:F0} ft");
            }
            else Log($"fight {fightNo} turn {battle.turn}: burst {burst:F2} s, hits {hits}, hit by {taken}");
            if (bot && onLead && rd != null) { botLeadTurns++; botLeadHitsSaid += rd.leadHits; botLeadHitsGot += hits; }
        }

        void FightOver()
        {
            if (fightLogged || !Dogfighting) return;
            fightLogged = true;
            if (bot) { if (battle.Out(1) && !battle.Out(0)) botKills++; else if (battle.Out(0) && !battle.Out(1)) botLosses++; else botDraws++; }
            Log($"fight {fightNo} over after {battle.turn} turns: {outcome ?? "stopped"}  bursts {fightBursts} ({fightBurstSec:F1} s), hits {fightHits}, hit by {fightTaken}, ammo left {battle.ammo[0]:F1} s");
        }

        // ---------- the bot: -dogfightbot N flies N fights by itself and quits ----------

        bool bot;
        int botFights = 10, botTurnLimit = 30;
        EnemyAi botPilot;
        Scenarios.Start? botStart;   // -dogfightstart HeadOn|Neutral|Offensive|Defensive: always begin this way
        int? botArena;               // -dogfightarena 0..3 (or its name, spaces left out): always begin over this arena
        string botShots;             // -dogfightshots <folder>: a picture of the first turns' planning there
        bool botPlanned, botShot;
        float botWaitUntil;

        void BotSetUp()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-dogfightbot");
            if (at < 0) return;
            bot = true;
            if (at + 1 < args.Length && int.TryParse(args[at + 1], out int n)) botFights = Math.Max(1, n);
            mission = Mission.Dogfight;
            playbackSpeed = 30f;
            botPilot = new EnemyAi { me = 0, role = EnemyAi.Role.Fighter, rng = new Lcg(4242), ignoreObstacles = true };
            int s = Array.IndexOf(args, "-dogfightstart");
            if (s >= 0 && s + 1 < args.Length && Enum.TryParse(args[s + 1], true, out Scenarios.Start st)) botStart = st;
            s = Array.IndexOf(args, "-dogfightarena");
            if (s >= 0 && s + 1 < args.Length) botArena = ParseArena(args[s + 1]);
            s = Array.IndexOf(args, "-dogfightshots");
            if (s >= 0 && s + 1 < args.Length) { botShots = args[s + 1]; Directory.CreateDirectory(botShots); }
            s = Array.IndexOf(args, "-dogfightsides");
            if (s >= 0 && s + 2 < args.Length && int.TryParse(args[s + 1], out int o) && int.TryParse(args[s + 2], out int t))
            { dogSetup.ours = DogfightSetup.Resize(dogSetup.ours, o); dogSetup.theirs = DogfightSetup.Resize(dogSetup.theirs, t); }
            // -dogfightcraft Mustang,Spitfire Bf109,Phantom: the aircraft on each side (and so how many)
            s = Array.IndexOf(args, "-dogfightcraft");
            if (s >= 0 && s + 2 < args.Length && ParseSide(args[s + 1], out var mine) && ParseSide(args[s + 2], out var theirs)) { dogSetup.ours = mine; dogSetup.theirs = theirs; }
            // -dogfightpilots Wizard,Usual Skeleton: who flies each plane of each side (missing ones fly with their usual pilot)
            s = Array.IndexOf(args, "-dogfightpilots");
            if (s >= 0 && s + 2 < args.Length) { dogSetup.oursPilots = ParsePilots(args[s + 1]); dogSetup.theirsPilots = ParsePilots(args[s + 2]); }
            dogSetup.FitPilots();
        }

        static int? ParseArena(string a)
        {
            if (int.TryParse(a, out int n) && n >= 0 && n < Arenas.Count) return n;
            for (int k = 0; k < Arenas.Count; k++)
                if (string.Equals(Arenas.All[k].name.Replace(" ", "").Replace("&", ""), a.Replace("&", ""), StringComparison.OrdinalIgnoreCase)) return k;
            Debug.LogWarning($"OODA: no arena {a}; picking at random.");
            return null;
        }

        static PilotKind[] ParsePilots(string list) =>
            list.Split(',').Select(x => Enum.TryParse(x.Trim(), true, out PilotKind p) ? p : PilotKind.Usual).ToArray();

        static bool ParseSide(string list, out Craft[] side)
        {
            var parts = list.Split(',');
            side = new Craft[Math.Min(parts.Length, Scenarios.MaxSide)];
            for (int k = 0; k < side.Length; k++) if (!Enum.TryParse(parts[k], true, out side[k])) return false;
            return side.Length > 0;
        }

        void BotUpdate()
        {
            if (!bot) return;
            if (phase == Phase.Planning && read != null && reading == null)
            {
                if (battle.turn >= botTurnLimit) { outcome = "Time up: a draw."; phase = Phase.Over; return; }
                if (!botPlanned) { BotPlan(); botPlanned = true; botWaitUntil = Time.unscaledTime + 1.2f; }
                if (botShots != null && fightNo == 1 && battle.turn < 6)
                {
                    if (Time.unscaledTime < botWaitUntil) return;
                    if (!botShot) { StartCoroutine(Grab(Path.Combine(botShots, $"turn{battle.turn + 1}.bmp"))); botShot = true; botWaitUntil = Time.unscaledTime + 0.6f; return; }
                }
                botPlanned = botShot = false;
                Commit();
            }
            else if (phase == Phase.Over)
            {
                FightOver();
                if (fightNo >= botFights)
                {
                    int t = Math.Max(1, botTurns);
                    Log($"bot summary: {fightNo} fights, {botKills} kills, {botLosses} losses, {botDraws} draws; over {botTurns} turns the enemy ended {botMiss / t:F0} ft from the bright ghost on average " +
                        $"(the old hold guess {botHoldMiss / t:F0} ft) and flew the bright ghost {100.0 * botBright / t:F0}% of the time; " +
                        $"on the lead {botLeadTurns} turns, said {botLeadHitsSaid} hits, got {botLeadHitsGot}");
                    Application.Quit();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                    bot = false;
                    return;
                }
                Restart();
            }
        }

        /// <summary>The bot's turn: the arrow on the lead and fire on the hits when the lead has any, else an AI pilot's plan.</summary>
        void BotPlan()
        {
            if (read.lead != null && read.leadHits > 0) { SetAim(read.lead.Value); plan.throttle = 1; FireAtPredicted(); }
            else
            {
                var p = botPilot.Plan(battle, turnSec);
                SetAim(p.aim); plan.throttle = p.throttle; plan.wep = p.wep;
                plan.burstStart = p.burstStart; plan.burstEnd = p.burstEnd;
                dirty = true;
            }
        }
    }
}
