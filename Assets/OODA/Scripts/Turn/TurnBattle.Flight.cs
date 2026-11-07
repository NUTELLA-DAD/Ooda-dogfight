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

        /// <summary>After the views are laid out: every plane on your side, the fighters' flights first (your lead first), then the bombers.</summary>
        void ListFlight()
        {
            flight.Clear();
            var mine = Enumerable.Range(0, battle.Count).Where(i => battle.side[i] == SideOf0 && NetMine(i))
                .OrderBy(i => battle.routes[i] != null).ThenBy(i => i == 0 ? -1 : battle.flightOf[i]).ThenBy(i => battle.number[i]).ThenBy(i => i);
            foreach (int i in mine) flight.Add(battle.planes[i]);
        }

        /// <summary>One of yours you can take the stick of: on your side and not waiting on a runway. Bombers and wingmen in formation too.</summary>
        bool Flyable(int i) => i >= 0 && i < battle.Count && battle.side[i] == SideOf0 && (battle.strike == null || !battle.strike.Held(i)) && NetMine(i);

        /// <summary>Your side (aircraft 0's, whichever plane that is now).</summary>
        int SideOf0 => battle.side[0];

        bool CanFly(int i) => Flyable(i) && !battle.Out(i);

        /// <summary>The plane's number in your flight (1 the lead), or 0 when it is not one of them.</summary>
        int FlightNo(FlightModel m) => flight.IndexOf(m) + 1;

        string FlightName(int i)
        {
            string kind = battle.planes[i].numbers.name.Replace("D Mustang", "").Replace(" G-6", "").Replace("C Eagle", "").Replace(" Flanker", "");   // "Blue 1 (F-15, lead)" fits the panel
            int f = battle.flightOf[i];
            if (f < 0 || f >= battle.flights.Count) return kind;
            return $"{CallSign(i)} ({kind}{(battle.Leader(f) == i ? ", lead" : "")})";
        }

        /// <summary>Its flight and number: Blue 2, Bombers 1.</summary>
        string CallSign(int i)
        {
            int f = battle.flightOf[i];
            return f < 0 || f >= battle.flights.Count ? battle.planes[i].numbers.name : $"{battle.flights[f].name} {battle.number[i]}";
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
            net?.seating?.Swapped(a, b);
            void S<T>(T[] arr) { if (arr != null && a < arr.Length && b < arr.Length) { var t = arr[a]; arr[a] = arr[b]; arr[b] = t; } }
            S(views); S(ais); S(holos); S(systemsBefore); S(shownSys);
            for (int i = 0; i < ais.Length; i++) if (ais[i] != null) { ais[i].me = i; ais[i].Forget(); }
            if (botPilot != null) botPilot.Forget();
            int Map(int i) => i == a ? b : i == b ? a : i;
            focus = Map(focus);
            if (pinnedFocus >= 0) pinnedFocus = Map(pinnedFocus);
            if (last != null) SwapResult(last, a, b, Map);
            preview = gunPreview = null;
            burstHits = new bool[battle.Count];
            AdoptPlayer();
        }

        /// <summary>Aircraft 0's objects are now views[0]'s: the camera, the cockpit eye, the pilot and the ghost follow it.</summary>
        void AdoptPlayer()
        {
            if (playerPilot != null) playerPilot.hideHead = false;
            player = views[0] != null ? views[0] : homePlayer;
            holoMine = holos.Length > 0 && holos[0] != null ? holos[0] : homeHolo;
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
            unplannedAsk = null;
            // what it would do by itself: hold its spot, fly its route, or what its AI pilot is likely to do
            var slot = battle.InFormation(k) ? battle.FormationSlot(k) : null;
            TurnPlan? suggestion = slot != null ? slot.Plan(battle, k, turnSec)
                : battle.routes[k] != null && battle.orders[k] != Order.YouControl ? battle.routes[k].Plan(battle.planes[k])
                : read != null && k < read.pred.Length && read.pred[k] != null ? read.pred[k].plans[0] : (TurnPlan?)null;
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
                if (battle.balloonMines[0] <= 0) plan.mine = false;
                if (battle.heatBlades[0] <= 0) plan.ram = false;
                if (battle.stealths[0] <= 0) plan.cloak = false;
                if (battle.jammers[0] <= 0) plan.jam = false;
                if (battle.wells[0] <= 0) plan.well = false;
                if (battle.portals[0] <= 0) plan.portal = false;
                plan.aim = env.Clamp(plan.aim, true);
                plan.overpull = env.Overpull(plan.aim);
                manual[m] = plan;
            }
            untouched = plan;
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
            Notice($"Now on {FlightName(0)}: {Formations.Name(battle.orders[0])}. Steer it to take it over; Tab for the next.");
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
            if (Online) return NetFightIsOver();
            // you are down but one of yours flies: carry on in it unless the fight is over anyway
            return battle.OverWith(battle.Out(0) && !CanTakeOver());
        }

        /// <summary>The plans you made, for everyone but aircraft 0 (whose plan is the one in hand). Only planes under your control take them.</summary>
        void ApplyManual(TurnPlan?[] plans)
        {
            for (int i = 1; i < plans.Length && i < battle.Count; i++)
                if (!battle.Out(i) && battle.orders[i] == Order.YouControl && manual.TryGetValue(battle.planes[i], out var p)) plans[i] = p;
        }

        /// <summary>By aircraft: you planned it this turn, so its AI pilot need not.</summary>
        bool[] ManualMask()
        {
            var mask = new bool[battle.Count];
            for (int i = 1; i < mask.Length; i++) mask[i] = battle.orders[i] == Order.YouControl && manual.ContainsKey(battle.planes[i]);
            return mask;
        }

        // ---------- orders and formations ----------

        /// <summary>The plan as it was handed to you on this plane (its formation, route or AI plan): steering away from it takes the plane over.</summary>
        TurnPlan untouched;
        /// <summary>Planes under your control that you have not planned this turn, while Fly asks about them.</summary>
        List<int> unplannedAsk;

        static bool SamePlan(TurnPlan a, TurnPlan b) =>
            (a.aim - b.aim).Length < 1e-9 && System.Math.Abs(a.throttle - b.throttle) < 1e-6 && a.wep == b.wep && a.rocket == b.rocket && a.brake == b.brake
            && a.mine == b.mine && a.mineAt == b.mineAt && a.well == b.well && a.wellAt == b.wellAt && a.portal == b.portal && a.portalAt == b.portalAt && (a.portalExit - b.portalExit).Length < 1e-9 && (a.portalExitFace - b.portalExitFace).Length < 1e-9 && (a.portalEntry - b.portalEntry).Length < 1e-9 && (a.portalEntryFace - b.portalEntryFace).Length < 1e-9 && a.ram == b.ram && a.cloak == b.cloak && a.jam == b.jam && a.rudder == b.rudder
            && a.burstStart == b.burstStart && a.burstEnd == b.burstEnd && a.missile == b.missile && a.hold == b.hold && a.marks == b.marks;

        /// <summary>At the start of each turn's planning: a plane in formation shows its formation plan, so the stick shows what it will do.</summary>
        void OrdersTurnStarts()
        {
            unplannedAsk = null;
            var slot = battle.InFormation(0) ? battle.FormationSlot(0) : null;
            if (slot != null)
            {
                plan = slot.Plan(battle, 0, turnSec);
                plan.aim = env.Clamp(plan.aim, true);
                plan.overpull = env.Overpull(plan.aim);
            }
            untouched = plan;
        }

        /// <summary>Every frame: steering the plane you are on (anything but your own control) takes it over.</summary>
        void WatchOrders()
        {
            if (phase != Phase.Planning || battle == null || battle.Out(0) || battle.side[0] != SideOf0) return;
            if (battle.orders[0] == Order.YouControl || SamePlan(plan, untouched)) return;
            SetOrder(Order.YouControl, false);
            if (flight.Count > 1) Notice($"{CallSign(0)} is yours now: it flies your plans until you give it Rejoin or Auto Fight.");
        }

        /// <summary>The order for the plane you are on. Rejoin and Auto Fight hand it back to its spot or its AI pilot this turn.</summary>
        void SetOrder(Order o, bool resetPlan = true)
        {
            battle.orders[0] = o;
            unplannedAsk = null;
            if (!resetPlan) return;
            manual.Remove(battle.planes[0]);
            var slot = battle.InFormation(0) ? battle.FormationSlot(0) : null;
            TurnPlan? p = slot != null ? slot.Plan(battle, 0, turnSec)
                : o != Order.YouControl && battle.routes[0] != null ? battle.routes[0].Plan(battle.planes[0])
                : o == Order.AutoFight && ais[0] != null ? ais[0].Plan(battle, turnSec) : (TurnPlan?)null;
            if (p.HasValue)
            {
                plan = p.Value;
                plan.hold = false;
                plan.marks = null;
                plan.aim = env.Clamp(plan.aim, true);
                plan.overpull = env.Overpull(plan.aim);
            }
            untouched = plan;
            dirty = true;
        }

        /// <summary>The formation for the flight the plane you are on leads.</summary>
        void SetFormation(Formation shape)
        {
            int f = battle.flightOf[0];
            if (f < 0 || f >= battle.flights.Count) return;
            battle.flights[f].shape = shape;
            if (battle.InFormation(0)) SetOrder(Order.Rejoin);   // a lead holding station: its plan may have moved with it
            dirty = true;
            Notice($"{battle.flights[f].name} flight: {Formations.Name(shape)}.");
        }

        /// <summary>
        /// Before a turn is flown: planes under your control you haven't planned this turn are listed, and Fly waits for you
        /// to choose (Auto Fly Them, or go and plan them). Pressing Fly again while asked flies them on autopilot.
        /// </summary>
        bool AskAboutUnplanned()
        {
            if (unplannedAsk != null) { unplannedAsk = null; return false; }
            var list = new List<int>();
            for (int i = 1; i < battle.Count; i++)
                if (battle.side[i] == SideOf0 && NetMine(i) && !battle.Out(i) && battle.orders[i] == Order.YouControl && !manual.ContainsKey(battle.planes[i])) list.Add(i);
            if (list.Count == 0) return false;
            unplannedAsk = list;
            return true;
        }

        void UnplannedGUI(float sw, float sh)
        {
            if (unplannedAsk == null || phase != Phase.Planning) return;
            var names = string.Join(", ", unplannedAsk.Where(i => i < battle.Count).Select(CallSign));
            var r = new Rect(sw / 2 - 230, sh * 0.32f, 460, 120);
            guiRects.Add(r);
            GUI.Box(r, GUIContent.none);
            GUI.Box(r, GUIContent.none);   // twice: darker, over the busy sky
            GUI.Label(new Rect(r.x + 14, r.y + 10, r.width - 28, 50), $"No plan this turn for {names}.", new GUIStyle(label) { wordWrap = true, alignment = TextAnchor.UpperCenter });
            if (GUI.Button(new Rect(r.x + 20, r.y + 70, 200, 36), "Auto Fly Them  (Space)")) Commit();
            if (GUI.Button(new Rect(r.x + 240, r.y + 70, 200, 36), "Plan Them"))
            {
                int first = unplannedAsk.Count > 0 ? unplannedAsk[0] : -1;
                unplannedAsk = null;
                if (first > 0) { TakeControl(first); Notice($"Now on {FlightName(0)}."); }
            }
        }

        bool ordersOpen;   // the flight's orders and formations folded out under the roster
        float ordersBottom;   // where the orders bar (and what is folded out of it) ends this frame, 0 when not shown

        /// <summary>
        /// Under the roster: one bar for the flight you're in, with a little picture of its formation, its order and its
        /// shape. Click it to fold out the orders (Rejoin, Auto Fight, You Control) and, when you lead it, the formations,
        /// each a tile with a picture of the shape seen from above.
        /// </summary>
        void OrdersGUI(float sw)
        {
            ordersBottom = 0;
            if (phase != Phase.Planning || flight.Count < 2 || battle.Out(0)) return;
            int f = battle.flightOf[0];
            bool hasFlight = f >= 0 && f < battle.flights.Count;
            bool leads = hasFlight && battle.Leader(f) == 0;
            int inFlight = 0;
            for (int i = 0; i < battle.Count; i++) if (hasFlight && battle.flightOf[i] == f && !battle.Out(i)) inFlight++;
            bool shapes = leads && inFlight >= 2;
            var amber = new Color(1f, 0.85f, 0.2f);

            // the folded bar
            var bs = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            bs.normal.textColor = Color.white;
            string name = hasFlight ? $"{battle.flights[f].name} flight" : CallSign(0);
            string what = Formations.Name(battle.orders[0]) + (hasFlight ? $"  \u00B7  {Formations.Name(battle.flights[f].shape)}" : "");
            string barText = $"{name}:  {what}";
            int planes = Mathf.Clamp(inFlight, 2, 4);
            float y = 74, barW = Mathf.Max(280, bs.CalcSize(new GUIContent(barText)).x + (hasFlight ? 42 : 10) + 30);
            Rect bar = new Rect((sw - barW) / 2, y, barW, 30);
            guiRects.Add(bar);
            if (GUI.Button(bar, GUIContent.none)) ordersOpen = !ordersOpen;
            float tx = bar.x + 10;
            if (hasFlight)
            {
                FormationPic(new Rect(bar.x + 6, bar.y + 3, 30, 24), battle.flights[f].shape, planes, amber, Color.white);
                tx = bar.x + 42;
            }
            GUI.Label(new Rect(tx, bar.y, bar.xMax - tx - 24, bar.height), barText, bs);
            GUI.Label(new Rect(bar.xMax - 22, bar.y, 18, bar.height), ordersOpen ? "\u25BE" : "\u25B8", bs);
            ordersBottom = bar.yMax;
            if (!ordersOpen) return;

            // folded out: the orders
            var orders = new[] { Order.Rejoin, Order.AutoFight, Order.YouControl };
            float bw = 104, gap = 4;
            y += 36;
            float total = orders.Length * bw + (orders.Length - 1) * gap;
            float x = (sw - total) / 2;
            guiRects.Add(new Rect(x - 6, y - 4, total + 12, 38));
            var on = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
            on.normal.textColor = on.hover.textColor = amber;
            var off = new GUIStyle(GUI.skin.button) { fontSize = 12 };
            foreach (var o in orders)
            {
                if (GUI.Button(new Rect(x, y, bw, 30), Formations.Name(o), battle.orders[0] == o ? on : off) && battle.orders[0] != o)
                {
                    SetOrder(o);
                    Notice($"{CallSign(0)}: {Formations.Name(o)}.");
                }
                x += bw + gap;
            }
            ordersBottom = y + 30;
            if (!shapes) return;

            // and the formations, the flight you lead: a tile each, the shape from above (the lead in amber) over its name
            y += 36;
            float tw = 66, th = 54;
            total = Formations.All.Length * tw + (Formations.All.Length - 1) * gap;
            x = (sw - total) / 2;
            guiRects.Add(new Rect(x - 6, y - 4, total + 12, th + 8));
            ordersBottom = y + th;
            var ns = new GUIStyle(small) { fontSize = 10, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            foreach (var shape in Formations.All)
            {
                bool now = battle.flights[f].shape == shape;
                Rect t = new Rect(x, y, tw, th);
                if (GUI.Button(t, GUIContent.none, now ? on : off)) SetFormation(shape);
                if (now) { Fill(new Rect(t.x, t.y, t.width, 2), amber); Fill(new Rect(t.x, t.yMax - 2, t.width, 2), amber); }
                FormationPic(new Rect(t.x + 8, t.y + 4, t.width - 16, th - 22), shape, planes, amber, now ? Color.white : new Color(0.8f, 0.85f, 0.8f));
                ns.normal.textColor = now ? amber : Color.white;
                GUI.Label(new Rect(t.x, t.yMax - 18, t.width, 16), Formations.Name(shape), ns);
                x += tw + gap;
            }
        }

        /// <summary>
        /// A formation seen from above and a little behind, fitted into r: a little plane for each of the flight's planes
        /// (the lead amber, at the front). The height shows as a lift up the picture, so a stepped-up box and a flat
        /// diamond don't look the same.
        /// </summary>
        static void FormationPic(Rect r, Formation shape, int planes, Color lead, Color others)
        {
            double minX = 0, maxX = 0, minY = 0, maxY = 0;
            Vector2 At(int n) { var q = Formations.Spot(shape, n); return new Vector2((float)q.x, (float)(q.y - q.z * 0.35)); }
            for (int n = 2; n <= planes; n++)
            {
                var p = At(n);
                minX = System.Math.Min(minX, p.x); maxX = System.Math.Max(maxX, p.x); minY = System.Math.Min(minY, p.y); maxY = System.Math.Max(maxY, p.y);
            }
            const float plane = 8;   // room each little plane needs
            double spanX = System.Math.Max(1e-3, maxX - minX), spanY = System.Math.Max(1e-3, maxY - minY);
            float k = (float)System.Math.Min((r.width - plane) / spanX, (r.height - plane) / spanY);
            float ox = r.center.x - (float)((minX + maxX) / 2) * k, oy = r.center.y - (float)((minY + maxY) / 2) * k;
            for (int n = planes; n >= 1; n--)
            {
                var p = At(n);
                float cx = ox + (float)p.x * k, cy = oy + (float)p.y * k;   // right is right, back is down the picture
                Color c = n == 1 ? lead : others;
                Fill(new Rect(cx - 4, cy - 1, 8, 2), c);   // wings
                Fill(new Rect(cx - 1, cy - 3, 2, 7), c);   // fuselage
                Fill(new Rect(cx - 2, cy + 3, 4, 1), c);   // tail
            }
        }

        /// <summary>A plane's state for the roster.</summary>
        string RosterState(int i)
        {
            if (i < 0 || battle.Out(i)) return "down";
            string on = i == 0 ? "> " : "";   // short: the roster's buttons are narrow
            if (battle.orders[i] == Order.YouControl) return i == 0 ? "> yours" : manual.ContainsKey(battle.planes[i]) ? "planned" : "no plan";
            if (battle.orders[i] == Order.AutoFight) return on + (battle.routes[i] != null ? "route" : "auto fight");
            var slot = battle.FormationSlot(i);
            if (slot == null) return on + (battle.routes[i] != null ? "route" : "auto fight");
            var l = battle.planes[slot.leader];
            bool far = (slot.Where(l.state.pos, l.state.vel) - battle.planes[i].state.pos).Length > Slot.RejoinFt;
            return on + (far ? "rejoining" : "formation");
        }

        // ---------- the roster ----------

        int rosterOpen = -2;      // the flight folded out in the roster: -2 follows the flight you're flying
        int rosterLastFlight = -3;

        /// <summary>
        /// A row along the top: a button for each of your flights (Blue, Red, Bombers), and the planes of one of them
        /// folded out after it: yours, unless you click another flight's button to look at its planes. Click a plane to
        /// fly it. Each shows which you are flying, which you have planned, which are down.
        /// </summary>
        void RosterGUI(float sw)
        {
            if (flight.Count < 2) return;
            // the flights, in the order their planes come in the roster
            var groups = new List<(int f, List<int> planes)>();
            foreach (var m in flight)
            {
                int i = System.Array.IndexOf(battle.planes, m);
                int f = i >= 0 ? battle.flightOf[i] : -1;
                int g = groups.FindIndex(x => x.f == f);
                if (g < 0) { groups.Add((f, new List<int>())); g = groups.Count - 1; }
                groups[g].planes.Add(i);
            }
            int mine = battle.flightOf[0];
            if (mine != rosterLastFlight) { rosterOpen = -2; rosterLastFlight = mine; }   // you moved to another flight: show it
            int open = rosterOpen == -2 ? mine : rosterOpen;
            bool one = groups.Count == 1;   // a single flight: its planes always show

            // between the panels in the top corners (yours on the left, the buttons on the right)
            float left = 320, right = sw - 200, gap = 4, hw = 104, bw = 96;
            int shown = 0;
            foreach (var g in groups) if (one || g.f == open) shown += g.planes.Count;
            float total = (one ? 0 : groups.Count * (hw + gap)) + shown * (bw + gap) - gap;
            if (total > right - left) { bw = Mathf.Max(70, (right - left - (one ? 0 : groups.Count * (hw + gap)) - shown * gap) / Mathf.Max(1, shown)); total = (one ? 0 : groups.Count * (hw + gap)) + shown * (bw + gap) - gap; }
            float x = Mathf.Max(left, (left + right - total) / 2), y = 10;
            guiRects.Add(new Rect(x - 6, y - 4, total + 12, 52));
            var amber = new Color(1f, 0.85f, 0.2f);
            foreach (var g in groups)
            {
                bool isOpen = one || g.f == open;
                if (!one)
                {
                    bool yours = g.f == mine;
                    int up = g.planes.Count(i => i >= 0 && !battle.Out(i));
                    string fname = g.f >= 0 && g.f < battle.flights.Count ? battle.flights[g.f].name : "Planes";
                    var hs = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
                    if (yours) hs.normal.textColor = hs.hover.textColor = amber;
                    if (GUI.Button(new Rect(x, y, hw, 40), $"{fname} {(isOpen ? "\u25BE" : "\u25B8")}\n{up} of {g.planes.Count} up", hs))
                        rosterOpen = isOpen && !yours ? mine : g.f;   // click again to fold it back to yours
                    x += hw + gap;
                }
                if (!isOpen) continue;
                foreach (int i in g.planes)
                {
                    bool down = i < 0 || battle.Out(i);
                    var st = new GUIStyle(GUI.skin.button) { fontSize = bw < 90 ? 11 : 12, fontStyle = i == 0 ? FontStyle.Bold : FontStyle.Normal };
                    if (i == 0) st.normal.textColor = st.hover.textColor = amber;
                    GUI.enabled = !down && i != 0 && phase == Phase.Planning;
                    if (GUI.Button(new Rect(x, y, bw, 40), $"{(i >= 0 ? CallSign(i) : "")}\n{RosterState(i)}", st))
                    {
                        TakeControl(i);
                        Notice($"Flying {FlightName(0)}.");
                    }
                    GUI.enabled = true;
                    x += bw + gap;
                }
            }
        }
    }
}
