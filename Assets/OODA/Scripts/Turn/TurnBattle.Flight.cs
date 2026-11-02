using System.Collections.Generic;
using System.Linq;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Pilot;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// Flying your whole flight. Each turn you can plan every fighter on your side: Tab (Shift+Tab back, the roster's
    /// buttons along the top, or the pad's View button) moves you to the next one, and the plane you are on becomes aircraft 0
    /// (Battle.Swap), so the stick, the ghost, the lead ring and the panels all work on it unchanged. A plane you
    /// visit this turn flies the plan you leave it with (it starts from what its AI pilot would do); one you never visit
    /// flies itself. Each new turn starts back on your lead. When the plane you are flying goes down and another of
    /// yours is still up, you carry on in it: the fight is lost only when your whole flight is down.
    /// </summary>
    public partial class TurnBattle
    {
        /// <summary>The plans you made this turn, by aircraft (its FlightModel, which keeps its identity through swaps).</summary>
        readonly Dictionary<FlightModel, TurnPlan> manual = new Dictionary<FlightModel, TurnPlan>();
        /// <summary>Your flight's fighters in their starting order (the lead first), for their numbers and the roster.</summary>
        readonly List<FlightModel> flight = new List<FlightModel>();
        // the scene's own player objects, put back at a restart (a swap points player, holoMine and the rest elsewhere)
        TurnAircraftView homePlayer;
        GameObject homeHolo;
        PilotHeadLook homePilot;
        Transform homeEye;
        bool homeSaved;

        void SaveHome()
        {
            if (homeSaved) return;
            homeSaved = true;
            homePlayer = player; homeHolo = holoMine; homePilot = playerPilot; homeEye = view != null ? view.cockpitEye : null;
        }

        /// <summary>At a restart: the scene's player objects back in place, before the views are laid out.</summary>
        void RestoreHome()
        {
            SaveHome();
            if (playerPilot != null && playerPilot != homePilot) playerPilot.hideHead = false;
            player = homePlayer; holoMine = homeHolo; playerPilot = homePilot;
            if (view != null) { view.player = player.transform; view.cockpitEye = homeEye; }
            manual.Clear();
            flight.Clear();
        }

        /// <summary>After the views are laid out: who is in your flight.</summary>
        void ListFlight()
        {
            flight.Clear();
            for (int i = 0; i < battle.Count; i++)   // the wingmen still in formation count: you can fly them once it breaks
                if (battle.side[i] == SideOf0 && battle.routes[i] == null) flight.Add(battle.planes[i]);
        }

        /// <summary>One of your fighters you can take the stick of: on your side, not a bomber on its route, not held in formation.</summary>
        bool Flyable(int i) => i >= 0 && i < battle.Count && battle.side[i] == SideOf0 && battle.routes[i] == null && !battle.InFormation(i)
                              && (battle.strike == null || !battle.strike.Held(i));

        /// <summary>Your side (aircraft 0's, whichever plane that is now).</summary>
        int SideOf0 => battle.side[0];

        bool CanFly(int i) => Flyable(i) && !battle.Out(i);

        /// <summary>The plane's number in your flight (1 the lead), or 0 when it is not one of them.</summary>
        int FlightNo(FlightModel m) => flight.IndexOf(m) + 1;

        string FlightName(int i)
        {
            int no = FlightNo(battle.planes[i]);
            string kind = battle.planes[i].numbers.name.Replace("D Mustang", "").Replace(" G-6", "");
            return no == 1 ? $"{kind} #1 (lead)" : no > 0 ? $"{kind} #{no}" : kind;
        }

        // ---------- swapping the plane you fly ----------

        /// <summary>Trade aircraft a and b everywhere: the battle, the views, the ghosts, the AI pilots and the last turn's playback.</summary>
        void SwapAll(int a, int b)
        {
            if (a == b) return;
            // let a reading still working on this battle finish first: it reads the lists being swapped
            if (reading != null && !reading.IsCompleted) { try { reading.Wait(); } catch (System.AggregateException) { } }
            reading = null; readingFor = null; read = null;
            battle.Swap(a, b);
            void S<T>(T[] arr) { if (arr != null && a < arr.Length && b < arr.Length) { var t = arr[a]; arr[a] = arr[b]; arr[b] = t; } }
            S(views); S(ais); S(holos); S(healthBefore); S(shownHp);
            for (int i = 0; i < ais.Length; i++) if (ais[i] != null) { ais[i].me = i; ais[i].Forget(); }
            if (botPilot != null) botPilot.Forget();
            int Map(int i) => i == a ? b : i == b ? a : i;
            focus = Map(focus);
            if (pinnedFocus >= 0) pinnedFocus = Map(pinnedFocus);
            if (last != null) SwapResult(last, a, b, Map);
            preview = gunPreview = null;
            burstHits = new bool[battle.Count];
            // aircraft 0's objects are now the other plane's
            if (playerPilot != null) playerPilot.hideHead = false;
            player = views[0] != null ? views[0] : homePlayer;
            holoMine = holos.Length > 0 ? holos[0] : homeHolo;
            playerPilot = player.GetComponentInChildren<PilotHeadLook>();
            if (view != null)
            {
                view.player = player.transform;
                view.cockpitEye = FindDeep(player.transform, "CockpitCamera");
            }
        }

        static void SwapResult(TurnResult r, int a, int b, System.Func<int, int> map)
        {
            if (a < r.frames.Length && b < r.frames.Length) { var t = r.frames[a]; r.frames[a] = r.frames[b]; r.frames[b] = t; }
            for (int k = 0; k < r.hits.Count; k++)
            {
                var h = r.hits[k];
                if (h.shooter >= 0) h.shooter = map(h.shooter);
                h.target = map(h.target);
                r.hits[k] = h;
            }
            foreach (var tr in r.traces) if (tr.owner >= 0) tr.owner = map(tr.owner);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Take the stick of aircraft k (one of yours): the plan in hand stays with the plane you leave.</summary>
        void TakeControl(int k, bool keepPlan = true)
        {
            if (k <= 0 || !CanFly(k)) return;
            if (keepPlan && !battle.Out(0)) manual[battle.planes[0]] = plan;
            TurnPlan? suggestion = read != null && k < read.pred.Length && read.pred[k] != null ? read.pred[k].plans[0] : (TurnPlan?)null;
            SwapAll(0, k);
            var m = battle.planes[0];
            env = Envelope.Compute(m, turnSec);
            if (manual.TryGetValue(m, out var mine)) plan = mine;
            else
            {
                // a plane you have not planned yet this turn starts from what its AI pilot would do
                plan = suggestion ?? new TurnPlan { aim = m.Forward, throttle = m.throttle };
                plan.hold = false;
                plan.marks = null;
                if (plan.rocket && !m.RocketReady) plan.rocket = false;
                plan.aim = env.Clamp(plan.aim, true);
                plan.overpull = env.Overpull(plan.aim);
                manual[m] = plan;
            }
            markT = 0;
            burstPreset = 0;
            dirty = true;
            FocusNearest();
            StartReading();
            if (view != null) view.BehindPlayer();
        }

        /// <summary>Tab: the next of your planes still flying.</summary>
        void NextPlane(int dir = 1)
        {
            if (phase != Phase.Planning) return;
            var mine = new List<int>();
            foreach (var m in flight) { int i = System.Array.IndexOf(battle.planes, m); if (i >= 0 && (i == 0 || CanFly(i))) mine.Add(i); }
            if (mine.Count < 2) return;
            int at = mine.IndexOf(0);
            int next = mine[((at + dir) % mine.Count + mine.Count) % mine.Count];
            TakeControl(next);
            Notice($"Flying {FlightName(0)}: {mine.Count} planes, Tab for the next. One you don't visit flies itself.");
        }

        /// <summary>
        /// At the start of each turn: forget last turn's plans, go back to your lead if it still flies, or into another
        /// of yours if the plane you were in went down.
        /// </summary>
        void FlightTurnStarts()
        {
            manual.Clear();
            if (flight.Count == 0) return;
            int lead = System.Array.IndexOf(battle.planes, flight[0]);
            int to = -1;
            if (lead > 0 && CanFly(lead)) to = lead;
            else if (battle.Out(0))
                foreach (var m in flight) { int i = System.Array.IndexOf(battle.planes, m); if (i > 0 && CanFly(i)) { to = i; break; } }
            if (to <= 0) return;
            bool wasDown = battle.Out(0);
            SwapAll(0, to);
            if (wasDown) Notice($"You're down: now flying {FlightName(0)}.");
        }

        /// <summary>One of yours can carry on when the plane you are in is out.</summary>
        bool CanTakeOver()
        {
            foreach (var m in flight) { int i = System.Array.IndexOf(battle.planes, m); if (i > 0 && CanFly(i)) return true; }
            return false;
        }

        /// <summary>The fight is over, counting a plane of yours still flying as you carrying on.</summary>
        bool FightIsOver()
        {
            if (!battle.Over) return false;
            if (!battle.Out(0) || !CanTakeOver() || Bombing) return true;   // the bomber missions keep their own ending for now
            // you are down but one of yours flies: over only if the enemy is gone too (or the bombers are all done)
            bool enemyLeft = false;
            for (int i = 0; i < battle.Count; i++) if (battle.side[i] != SideOf0 && !battle.Out(i)) enemyLeft = true;
            return !enemyLeft;
        }

        /// <summary>The plans you made, for everyone but aircraft 0 (whose plan is the one in hand).</summary>
        void ApplyManual(TurnPlan?[] plans)
        {
            for (int i = 1; i < plans.Length && i < battle.Count; i++)
                if (!battle.Out(i) && manual.TryGetValue(battle.planes[i], out var p)) plans[i] = p;
        }

        /// <summary>By aircraft: you planned it this turn, so its AI pilot need not.</summary>
        bool[] ManualMask()
        {
            var mask = new bool[battle.Count];
            for (int i = 1; i < mask.Length; i++) mask[i] = manual.ContainsKey(battle.planes[i]);
            return mask;
        }

        // ---------- the roster ----------

        /// <summary>A row of buttons along the top: each of your planes, which you are flying, which you have planned, which are down.</summary>
        void RosterGUI(float sw)
        {
            if (flight.Count < 2) return;
            float bw = 132, gap = 6, total = flight.Count * bw + (flight.Count - 1) * gap;
            float x = (sw - total) / 2, y = 10;
            var r = new Rect(x - 6, y - 4, total + 12, 52);
            guiRects.Add(r);
            for (int k = 0; k < flight.Count; k++)
            {
                var m = flight[k];
                int i = System.Array.IndexOf(battle.planes, m);
                bool down = i < 0 || battle.Out(i);
                string state = down ? "down" : i == 0 ? "flying" : manual.ContainsKey(m) ? "planned" : "auto";
                var st = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = i == 0 ? FontStyle.Bold : FontStyle.Normal };
                if (i == 0) st.normal.textColor = st.hover.textColor = new Color(1f, 0.85f, 0.2f);
                GUI.enabled = !down && i != 0 && phase == Phase.Planning;
                if (GUI.Button(new Rect(x + k * (bw + gap), y, bw, 40), $"#{k + 1}{(k == 0 ? " lead" : "")}\n{state}", st))
                {
                    TakeControl(i);
                    Notice($"Flying {FlightName(0)}.");
                }
                GUI.enabled = true;
            }
            if (phase == Phase.Planning)
                GUI.Label(new Rect(x, y + 42, total, 20), "Tab: your next plane", new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        }
    }
}
