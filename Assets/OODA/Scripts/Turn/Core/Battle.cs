using System;
using System.Collections.Generic;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>Everything that happened in one movement turn, for playback.</summary>
    public class TurnResult
    {
        public double startTime, dt;
        public AircraftFrame[][] frames;
        public List<Trace> traces = new List<Trace>();
        public List<HitEvent> hits = new List<HitEvent>();
        /// <summary>The flares' paths (kept apart from the rounds' traces: they hit nothing), age0 their age at startFrame.</summary>
        public List<Trace> flares = new List<Trace>();
        /// <summary>The balloon mines' drift (see Specials), apart too: a hit means it went off.</summary>
        public List<Trace> mines = new List<Trace>();
        /// <summary>The gravity wells' drift (see Specials), apart too: they hit nothing.</summary>
        public List<Trace> wells = new List<Trace>();
        /// <summary>The chaff clouds' drift (see Radar), apart too: they hit nothing and show nothing to a heat-seeker.</summary>
        public List<Trace> chaff = new List<Trace>();
        /// <summary>Rounds bouncing off a ricochet shield (see Specials): when, where, and whose shield (target) and whose round (shooter). Nothing is worn.</summary>
        public List<HitEvent> ricochets = new List<HitEvent>();
        public int FrameCount => frames[0].Length;
    }

    /// <summary>
    /// The aircraft (0 the player, 1 the enemy, then any others, such as the bomber the player escorts), their
    /// sides, their guns and the rounds in the air. Rounds hit aircraft on the other side. Resolve flies one
    /// simultaneous turn from everyone's plans in small steps, firing, moving and checking every packet, and
    /// reports frames for playback. An aircraft with a route flies it by itself. Pure C#, feet, x east, y north, z up; the ground comes in as a function.
    /// A Clone flies a "what if" without touching the real battle (the ghost path, tracer preview, the AI).
    /// </summary>
    public class Battle
    {
        public const double StepSec = 0.02;
        public const double CollisionFt = 25;

        public FlightModel[] planes = new FlightModel[0];
        public CombatNumbers[] combat = new CombatNumbers[0];
        public int[] side = new int[0];              // 0 the player's side, 1 the enemy's
        public Route[] routes = new Route[0];        // set for an aircraft that flies a route by itself
        /// <summary>The flights (see Formations.Organise); flightOf and number place each aircraft in one, -1 and 0 for none.</summary>
        public List<FlightGroup> flights = new List<FlightGroup>();
        public int[] flightOf = new int[0];
        public int[] number = new int[0];             // 1 the lead to 4
        public Order[] orders = new Order[0];         // what each one does when nobody plans its turn
        public double[] ammo = new double[0];        // seconds of trigger left
        public int[] missiles = new int[0];          // missiles left
        public int[] flares = new int[0];            // flares left
        /// <summary>Radar missiles and chaff left (see Radar); jets only.</summary>
        public int[] radarMissiles = new int[0], chaff = new int[0];
        /// <summary>Who each radar is tracking (-1 no one) and for how long without a break: locked from Radar.LockSec.</summary>
        public int[] radarTrack = new int[0];
        public double[] trackSec = new double[0];
        /// <summary>The specials left (see Specials): balloon mines, heat-blade charges, stealth charges.</summary>
        public int[] balloonMines = new int[0], heatBlades = new int[0], stealths = new int[0], jammers = new int[0];
        /// <summary>Gravity wells left (see Specials).</summary>
        public int[] wells = new int[0];
        /// <summary>Ricochet shield charges left, and the shield up until this battle time (see Specials).</summary>
        public int[] mirrors = new int[0];
        public double[] mirrorUntil = new double[0];
        /// <summary>Jammed (seeing scrambled ghosts) until this battle time.</summary>
        public double[] jammedUntil = new double[0];
        /// <summary>The heat blade glowing this turn; stealthy until this battle time.</summary>
        public bool[] ramOn = new bool[0];
        public double[] cloakUntil = new double[0];
        int nextFlareId = 1;
        public bool[] down = new bool[0];            // shot down or collided: out of the fight, falling
        public bool[] crashed = new bool[0];         // hit the ground or sea: stopped
        public bool[] hitObstacle = new bool[0];     // crashed into an obstacle (a cable, a bridge, a building) rather than the ground
        public int Count => planes.Length;
        public List<Bullet> bullets = new List<Bullet>();
        public double time;
        public int turn;
        public bool dispersion = true;
        public Lcg rng = new Lcg(1940);
        public Func<double, double, double> groundFt = (x, y) => 0;
        /// <summary>Solid things above the ground (bridges, tunnel mouths, buildings): true if the segment from a to b (feet) hits one.</summary>
        public Func<Vec3d, Vec3d, bool> obstacleHit;
        /// <summary>The air hazards as plain shapes (thread-safe), for AI pilots planning where obstacleHit can't be used. May be null.</summary>
        public WireSet wires;
        /// <summary>Guns on the ground.</summary>
        public FlakSite[] flak = new FlakSite[0];
        /// <summary>The town strike's look-out, patrol, runway and target, or null in the other missions.</summary>
        public Strike strike;

        double[] fireClock = new double[0];

        public Battle(FlightModel a, CombatNumbers ca, FlightModel b, CombatNumbers cb)
        {
            Add(a, ca, 0);
            Add(b, cb, 1);
        }

        /// <summary>Add an aircraft on a side (0 the player's, 1 the enemy's), flying a route by itself if one is given. Returns its index.</summary>
        public int Add(FlightModel m, CombatNumbers c, int onSide, Route route = null)
        {
            int n = planes.Length;
            Array.Resize(ref planes, n + 1); planes[n] = m;
            Array.Resize(ref combat, n + 1); combat[n] = c;
            Array.Resize(ref side, n + 1); side[n] = onSide;
            Array.Resize(ref routes, n + 1); routes[n] = route?.Copy();
            Array.Resize(ref flightOf, n + 1); flightOf[n] = -1;
            Array.Resize(ref number, n + 1);
            Array.Resize(ref orders, n + 1); orders[n] = Order.AutoFight;
            Array.Resize(ref ammo, n + 1); ammo[n] = c.ammoSec;
            Array.Resize(ref missiles, n + 1); missiles[n] = c.missiles;
            Array.Resize(ref flares, n + 1); flares[n] = c.flares;
            Array.Resize(ref radarMissiles, n + 1); radarMissiles[n] = c.radarRangeFt > 0 ? c.radarMissiles : 0;
            Array.Resize(ref chaff, n + 1); chaff[n] = c.chaff;
            Array.Resize(ref radarTrack, n + 1); radarTrack[n] = -1;
            Array.Resize(ref trackSec, n + 1);
            Array.Resize(ref balloonMines, n + 1); balloonMines[n] = c.balloonMines;
            Array.Resize(ref heatBlades, n + 1); heatBlades[n] = c.heatBlades;
            Array.Resize(ref stealths, n + 1); stealths[n] = c.stealths;
            Array.Resize(ref jammers, n + 1); jammers[n] = c.jammers;
            Array.Resize(ref wells, n + 1); wells[n] = c.wells;
            Array.Resize(ref mirrors, n + 1); mirrors[n] = c.mirrors;
            Array.Resize(ref mirrorUntil, n + 1);
            Array.Resize(ref jammedUntil, n + 1);
            Array.Resize(ref ramOn, n + 1);
            Array.Resize(ref cloakUntil, n + 1);
            Array.Resize(ref down, n + 1);
            Array.Resize(ref crashed, n + 1);
            Array.Resize(ref hitObstacle, n + 1);
            Array.Resize(ref fireClock, n + 1);
            strike?.Size(n + 1);
            return n;
        }

        /// <summary>
        /// The fight is over when the player is out, or the enemy has no aircraft left, or there are aircraft
        /// flying routes (the bombers) and none of them is still flying one: all down, or the ones left are through.
        /// </summary>
        public bool Over => OverWith(Out(0));

        /// <summary>Over, with playerOut saying whether the player counts as out (not when they can carry on in another of theirs).</summary>
        public bool OverWith(bool playerOut)
        {
            {
                if (playerOut) return true;
                bool enemyLeft = false, anyRoute = false, routeFlying = false;
                for (int i = 0; i < Count; i++)
                {
                    if (side[i] != side[0] && (!Out(i) || strike != null && strike.Held(i))) enemyLeft = true;
                    if (routes[i] == null) continue;
                    anyRoute = true;
                    // a strike's bombers are through once they have dropped on the factory
                    if (!Out(i) && !routes[i].Done && (strike == null || !strike.struck[i])) routeFlying = true;
                }
                return (!enemyLeft && strike == null) || (anyRoute && !routeFlying);   // a strike goes on to the factory even with the sky clear
            }
        }
        /// <summary>
        /// Trade two aircraft's places in every list, so aircraft b becomes index a and the other way round (the rounds,
        /// missiles, gunners and formation slots are renumbered with them). For the player taking over a wingman: Swap(0, k).
        /// Each aircraft keeps what it carries (its route, its formation slot, its place on the strike's runway or patrol),
        /// so swapping back restores everything. Any side, any role.
        /// </summary>
        public void Swap(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || a >= Count || b >= Count) return;
            void S<T>(T[] arr) { var t = arr[a]; arr[a] = arr[b]; arr[b] = t; }
            int Map(int i) => i == a ? b : i == b ? a : i;
            side = (int[])side.Clone(); combat = (CombatNumbers[])combat.Clone();   // new arrays: clones share these two
            S(planes); S(combat); S(side); S(routes); S(flightOf); S(number); S(orders); S(ammo); S(missiles); S(flares); S(radarMissiles); S(chaff); S(radarTrack); S(trackSec); S(balloonMines); S(heatBlades); S(stealths); S(jammers); S(wells); S(mirrors); S(mirrorUntil); S(jammedUntil); S(ramOn); S(cloakUntil); S(down); S(crashed); S(hitObstacle); S(fireClock);
            foreach (var bl in bullets)
            {
                if (bl.owner >= 0) bl.owner = Map(bl.owner);
                if (bl is Missile ms && ms.target >= 0) ms.target = Map(ms.target);
                if (bl is Missile ma && ma.aimedAt >= 0) ma.aimedAt = Map(ma.aimedAt);
            }
            for (int i = 0; i < Count; i++) if (radarTrack[i] >= 0) radarTrack[i] = Map(radarTrack[i]);
            foreach (var g in flak)
            {
                if (g.carrier >= 0) g.carrier = Map(g.carrier);
                if (g.target >= 0) g.target = Map(g.target);
            }
            if (strike != null)
            {
                S(strike.patrols); S(strike.held); S(strike.struck); S(strike.struckAt);
                var sc = new int[strike.scramble.Length];   // a new array: clones share the old one
                for (int k = 0; k < sc.Length; k++) sc[k] = Map(strike.scramble[k]);
                strike.scramble = sc;
            }
        }

        /// <summary>Out of the fight: down, crashed, or (in the strike) still waiting on the runway.</summary>
        public bool Out(int i) => down[i] || crashed[i] || strike != null && strike.Held(i);

        /// <summary>Stealthy now (see Specials): the other side's pilots, flak and gunners don't see it; heat-seekers still do.</summary>
        public bool Jammed(int i) => i >= 0 && i < Count && !Out(i) && time < jammedUntil[i] - 1e-9;

        public bool Cloaked(int i) => i >= 0 && i < Count && !Out(i) && time < cloakUntil[i] - 1e-9;

        /// <summary>The ricochet shield is up now (see Specials): rounds bounce off it.</summary>
        public bool Mirrored(int i) => i >= 0 && i < Count && !Out(i) && time < mirrorUntil[i] - 1e-9;

        /// <summary>Aircraft i's heat as a seeker at eye sees it: its engines, and the heat blade's glow on top.</summary>
        public double HeatOf(int i, Vec3d eye)
        {
            if (combat[i].iced) return 0;   // cased in ice (see Loadout): nothing for a seeker to see
            double h = Heat.Of(planes[i], combat[i], eye);
            return ramOn[i] ? h + Specials.BladeHeat : h;
        }

        public Battle Clone()
        {
            var c = (Battle)MemberwiseClone();
            c.planes = new FlightModel[Count];
            c.routes = new Route[Count];
            for (int i = 0; i < Count; i++) { c.planes[i] = planes[i].Clone(); c.routes[i] = routes[i]?.Copy(); }
            c.flightOf = (int[])flightOf.Clone(); c.number = (int[])number.Clone(); c.orders = (Order[])orders.Clone();
            c.flights = flights.ConvertAll(f => f.Copy());
            c.ammo = (double[])ammo.Clone(); c.missiles = (int[])missiles.Clone(); c.flares = (int[])flares.Clone();
            c.radarMissiles = (int[])radarMissiles.Clone(); c.chaff = (int[])chaff.Clone(); c.radarTrack = (int[])radarTrack.Clone(); c.trackSec = (double[])trackSec.Clone();
            c.balloonMines = (int[])balloonMines.Clone(); c.heatBlades = (int[])heatBlades.Clone(); c.stealths = (int[])stealths.Clone(); c.jammers = (int[])jammers.Clone(); c.wells = (int[])wells.Clone(); c.mirrors = (int[])mirrors.Clone(); c.mirrorUntil = (double[])mirrorUntil.Clone(); c.jammedUntil = (double[])jammedUntil.Clone();
            c.ramOn = (bool[])ramOn.Clone(); c.cloakUntil = (double[])cloakUntil.Clone();
            c.down = (bool[])down.Clone(); c.crashed = (bool[])crashed.Clone(); c.hitObstacle = (bool[])hitObstacle.Clone();
            c.fireClock = (double[])fireClock.Clone();
            c.flak = new FlakSite[flak.Length];
            for (int k = 0; k < flak.Length; k++) c.flak[k] = flak[k].Copy();
            c.bullets = new List<Bullet>(bullets.Count);
            foreach (var b in bullets) c.bullets.Add(b.Copy());
            c.strike = strike?.Copy();
            return c;
        }

        /// <summary>The direction the guns point: the nose, which sits the angle of attack above the flight path.</summary>
        public static Vec3d GunLine(FlightModel m)
        {
            double a = m.AlphaDeg * Math.PI / 180;
            return (m.Forward * Math.Cos(a) + m.Up * Math.Sin(a)).Normalized;
        }

        /// <summary>
        /// Fly one turn. With record false nothing is stored for playback (faster, for previews and the AI);
        /// hits and outcomes still happen. onStep sees the battle after each step (the AI scores along the way).
        /// p0 and p1 are the player's and the enemy's plans; aircraft with routes plan for themselves.
        /// </summary>
        public TurnResult Resolve(TurnPlan p0, TurnPlan p1, double turnSec, bool record = true, Action<Battle, int> onStep = null)
            => Resolve(new TurnPlan?[] { p0, p1 }, turnSec, record, onStep);

        /// <summary>Fly one turn from a plan for each aircraft by index; a missing one holds (or flies its route).</summary>
        public TurnResult Resolve(TurnPlan?[] given, double turnSec, bool record = true, Action<Battle, int> onStep = null)
        {
            int n = Count;
            var plans = new TurnPlan[n];
            for (int i = 0; i < n; i++)
            {
                var slot = InFormation(i) ? FormationSlot(i) : null;
                bool mine = i < given.Length && given[i].HasValue;
                plans[i] = strike != null && strike.OnPatrol(i) ? strike.patrols[i].Plan(planes[i])
                         : slot != null ? slot.Plan(this, i, turnSec)
                         : routes[i] != null && !(orders[i] == Order.YouControl && mine) ? routes[i].Plan(planes[i])
                         : mine ? given[i].Value : TurnPlan.Holding();
            }
            int steps = (int)Math.Round(turnSec / StepSec);
            var launched = new bool[n];
            var radarLaunched = new bool[n];
            bool[] dropMine = new bool[n], throwWell = new bool[n];
            for (int i = 0; i < n; i++)
            {
                ramOn[i] = false;
                if (Out(i)) continue;
                if (plans[i].ram && heatBlades[i] > 0) { ramOn[i] = true; heatBlades[i]--; }
                if (plans[i].cloak && stealths[i] > 0) { cloakUntil[i] = time + Specials.CloakSec; stealths[i]--; }
                if (plans[i].mine && balloonMines[i] > 0) { dropMine[i] = true; balloonMines[i]--; }
                if (plans[i].well && wells[i] > 0) { throwWell[i] = true; wells[i]--; }
                if (plans[i].mirror && mirrors[i] > 0 && !Mirrored(i)) { mirrorUntil[i] = time + Specials.MirrorSec; mirrors[i]--; }
            }
            for (int i = 0; i < n; i++)
            {
                if (Out(i) || !plans[i].jam || jammers[i] <= 0) continue;
                jammers[i]--;
                for (int j = 0; j < n; j++)
                    if (side[j] != side[i] && !Out(j) && (planes[j].state.pos - planes[i].state.pos).Length <= Specials.JamRangeFt)
                        jammedUntil[j] = Math.Max(jammedUntil[j], time + Specials.JamSec);
            }
            var res = new TurnResult { startTime = time, dt = StepSec, frames = new AircraftFrame[n][] };
            var traceOf = record ? new Dictionary<Bullet, Trace>() : null;
            if (record)
            {
                for (int i = 0; i < n; i++) { res.frames[i] = new AircraftFrame[steps + 1]; res.frames[i][0] = Frame(i, false); }
                foreach (var b in bullets) StartTrace(res, traceOf, b, 0);
            }

            for (int k = 1; k <= steps; k++)
            {
                double t = (k - 1) * StepSec;   // time into the turn at the start of this step
                bool[] firing = new bool[n];
                var before = new Vec3d[n];
                for (int i = 0; i < n; i++) before[i] = planes[i].state.pos;

                // Aircraft
                for (int i = 0; i < n; i++)
                {
                    if (crashed[i] || strike != null && strike.Held(i)) continue;
                    var m = planes[i];
                    FlightControls c = down[i]
                        ? new FlightControls { pitch = -0.1, roll = 0.25 * (i % 2 == 0 ? 1 : -1), throttle = 0 }
                        : plans[i].ControlsFor(m, t);
                    m.Step(c, StepSec);
                    if (!down[i]) { m.systems.Step(StepSec); if (m.systems.Lost) GoDown(i); }   // the leak runs the tanks dry
                    if (m.AltFt <= groundFt(m.state.pos.x, m.state.pos.y)) { crashed[i] = true; down[i] = true; }
                    else if (obstacleHit != null && obstacleHit(before[i], m.state.pos)) { crashed[i] = true; down[i] = true; hitObstacle[i] = true; }
                }

                // Gravity wells: every open one pulls on every plane still flying (both sides: it bends your own turn too)
                PullPlanes();

                for (int i = 0; i < n; i++) routes[i]?.Advance(planes[i]);
                TrackRadars(plans);

                // Balloon mines: let go behind, in the step its plan's moment falls in (the last step at the latest)
                for (int i = 0; i < n; i++)
                {
                    if (!dropMine[i] || k < steps && t + StepSec <= Math.Max(0, plans[i].mineAt) + 1e-9) continue;
                    dropMine[i] = false;
                    if (Out(i)) continue;
                    var m = planes[i];
                    var mn = Mine.Make(i, side[i], m.state.pos - m.Forward * 50 - m.Up * 6, m.state.vel * 0.25, time + StepSec, k);
                    bullets.Add(mn);
                    if (record) StartTrace(res, traceOf, mn, k);
                }
                // Gravity wells: thrown ahead and into the turn, in the step its plan's moment falls in (the last step at the latest)
                for (int i = 0; i < n; i++)
                {
                    if (!throwWell[i] || k < steps && t + StepSec <= Math.Max(0, plans[i].wellAt) + 1e-9) continue;
                    throwWell[i] = false;
                    if (Out(i)) continue;
                    var m = planes[i];
                    Vec3d from = m.state.pos + m.Forward * 40, to = Specials.WellLanding(m, plans[i]);
                    to.z = Math.Max(to.z, groundFt(to.x, to.y) + 300);   // it hangs clear of the ground
                    var wl = Well.Make(i, side[i], from, (to - from) * Specials.WellDragPerSec, time + StepSec);
                    bullets.Add(wl);
                    if (record) StartTrace(res, traceOf, wl, k);
                }
                if (strike != null) for (int i = 0; i < n; i++) if (strike.OnPatrol(i)) strike.patrols[i].Advance(planes[i]);

                // Guns: a packet each packetSec of trigger while there is ammunition
                for (int i = 0; i < n; i++)
                {
                    if (Out(i) || ramOn[i] || !plans[i].Fires(t) || ammo[i] <= 0) { fireClock[i] = 0; continue; }   // the blade's heat keeps the guns cold
                    firing[i] = true;
                    cloakUntil[i] = Math.Min(cloakUntil[i], time);   // the muzzle flash shows you
                    ammo[i] = Math.Max(0, ammo[i] - StepSec);
                    fireClock[i] -= StepSec;
                    while (fireClock[i] <= 0)
                    {
                        double late = -fireClock[i];   // how long ago in this step the packet was due: it has flown that long
                        fireClock[i] += combat[i].packetSec;
                        var b = Fire(i, late);
                        bullets.Add(b);
                        if (record) StartTrace(res, traceOf, b, k);
                    }
                }
                for (int f = 0; f < flak.Length; f++) FireFlak(f, res, traceOf, k);

                // Missiles: one a turn for each plan that asks, the first moment a target is in the lock cone
                for (int i = 0; i < n; i++)
                {
                    if (!plans[i].missile || launched[i] || missiles[i] <= 0 || Out(i)) continue;
                    int tgt = LockTarget(i, plans[i].missileTarget);
                    if (tgt < 0) continue;
                    var m = planes[i];
                    var ms = Missile.Make(i, side[i], tgt, m.state.pos + m.Forward * 20 - m.Up * 4, m.state.vel + m.Forward * 60, time + StepSec);
                    bullets.Add(ms);
                    missiles[i]--; launched[i] = true;
                    cloakUntil[i] = Math.Min(cloakUntil[i], time);
                    if (record) StartTrace(res, traceOf, ms, k);
                }

                // Radar missiles: one a turn for each plan that asks, the first moment its radar is locked on someone in range
                for (int i = 0; i < n; i++)
                {
                    if (!plans[i].radarMissile || radarLaunched[i] || radarMissiles[i] <= 0 || Out(i) || !Locked(i)) continue;
                    int tgt = radarTrack[i];
                    if ((planes[tgt].state.pos - planes[i].state.pos).Length < Radar.MinRangeFt) continue;
                    var m = planes[i];
                    var ms = Missile.MakeRadar(i, side[i], tgt, m.state.pos + m.Forward * 20 - m.Up * 5, m.state.vel + m.Forward * 50, time + StepSec);
                    bullets.Add(ms);
                    radarMissiles[i]--; radarLaunched[i] = true;
                    cloakUntil[i] = Math.Min(cloakUntil[i], time);
                    if (record) StartTrace(res, traceOf, ms, k);
                }

                // Flares: a seeker on someone closing inside FlareWarnFt makes them let go a pair, unless they hold them
                DropFlares(plans, res, traceOf, k);
                DropChaff(plans, res, traceOf, k);

                // Rounds: gravity and drag, then a check against each other aircraft over the step
                bool anyWell = AnyWellOpen();
                for (int bi = bullets.Count - 1; bi >= 0; bi--)
                {
                    var b = bullets[bi];
                    Vec3d p0b = b.pos;
                    Trace bt = null;
                    if (record) traceOf.TryGetValue(b, out bt);
                    if (b is Well wl)
                    {
                        wl.Step(StepSec);
                        if (wl.age > wl.lifeSec) wl.alive = false;
                        if (bt != null) bt.points.Add(b.pos);
                        if (!b.alive) bullets.RemoveAt(bi);
                        continue;
                    }
                    if (anyWell && !(b is Mine) && !(b is Flare) && !(b is Chaff))
                    {
                        // the gravity wells bend it: a missile hard, a round (light) harder still
                        Vec3d pull = WellPull(b.pos, b is Missile ? Specials.WellMissileG : Specials.WellRoundG);
                        b.vel = b.vel + pull * StepSec;
                        if (b is Missile cm && cm.Tracking && InWellCore(cm.pos)) cm.Lose(MissileLoss.Well);
                    }
                    if (b is Missile ms)
                    {
                        if (ms.decoy >= 0)
                        {
                            var fl = FlareById(ms.decoy);
                            ms.Step(fl != null ? fl.pos : default, fl != null ? fl.vel : default, fl != null, StepSec);
                        }
                        else
                        {
                            // a radar missile rides the shooter's radar: it goes dumb once that lets go of the target
                            if (ms.radar && ms.target >= 0 && !Illuminating(ms.owner, ms.target)) ms.Lose(MissileLoss.Radar);
                            bool ok = ms.target >= 0 && ms.target < n && !Out(ms.target);
                            ms.Step(ok ? planes[ms.target].state.pos : default, ok ? planes[ms.target].state.vel : default, ok, StepSec);
                        }
                    }
                    else if (b is Mine mn)
                    {
                        mn.Step(StepSec);
                        if (!mn.Armed)
                        {
                            if (bt != null) bt.points.Add(b.pos);
                            continue;
                        }
                    }
                    else if (b is Flare || b is Chaff)
                    {
                        // a flare: it falls away and slows, burns out, and hits nothing
                        double decay = Math.Exp(-b.dragPerSec * StepSec);
                        Vec3d vAir = b.vel;
                        b.vel = new Vec3d(vAir.x * decay, vAir.y * decay, vAir.z * decay - Atmosphere.GFtps2 * StepSec);
                        b.pos = b.pos + (vAir + b.vel) * (StepSec / 2);
                        b.age += StepSec;
                        if (b.age > b.lifeSec || b.pos.z < groundFt(b.pos.x, b.pos.y)) b.alive = false;
                        if (bt != null) bt.points.Add(b.pos);
                        if (!b.alive) bullets.RemoveAt(bi);
                        continue;
                    }
                    else
                    {
                        double decay = Math.Exp(-b.dragPerSec * StepSec);
                        Vec3d vAir = b.vel;
                        b.vel = new Vec3d(vAir.x * decay, vAir.y * decay, vAir.z * decay - Atmosphere.GFtps2 * StepSec);
                        b.pos = b.pos + (vAir + b.vel) * (StepSec / 2);
                        b.age += StepSec;
                    }
                    for (int target = 0; target < n && b.alive; target++)
                    {
                        if (side[target] == b.side || crashed[target] || strike != null && strike.Held(target)) continue;
                        Vec3d r0 = p0b - before[target], r1 = b.pos - planes[target].state.pos;
                        double reach = b.hitRadiusFt + combat[target].sizeFt;
                        if (bt != null)
                        {
                            // the closest pass so far, against whichever target it comes nearest to hitting (for the preview's near-miss colour)
                            double d = Closest(r0, r1);
                            if (bt.closestTarget < 0 || d / Math.Max(1e-6, reach) < bt.closestFt / Math.Max(1e-6, bt.reachFt))
                            { bt.closestFt = d; bt.reachFt = reach; bt.closestTarget = target; }
                        }
                        if (Passes(r0, r1, reach) && Mirrored(target) && !(b is Missile) && !(b is Mine))
                        {
                            Ricochet(b, target, res, r0, r1, reach);
                            if (bt != null && bt.bounceAt < 0) { bt.bounceAt = bt.points.Count; bt.bouncer = target; }
                            break;   // it is the shield's now: it flies on from the bubble next step
                        }
                        if (Passes(r0, r1, reach))
                        {
                            Hit(b, target, res, r0, r1, reach);
                            b.alive = false;
                            if (bt != null) bt.hit = true;
                        }
                    }
                    if (b.age > b.lifeSec || b.pos.z < groundFt(b.pos.x, b.pos.y)) b.alive = false;
                    else if (b.alive && obstacleHit != null && obstacleHit(p0b, b.pos)) b.alive = false;
                    if (bt != null) bt.points.Add(b.pos);
                    if (!b.alive) bullets.RemoveAt(bi);
                }

                // Mid-air collision: both go down, unless one has the heat blade lit and the other is an enemy without: that
                // one is cut in half and the blade flies on (and the blade reaches farther)
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        if (crashed[i] || crashed[j] || strike != null && (strike.Held(i) || strike.Held(j))) continue;
                        bool foes = side[i] != side[j];
                        bool bi = foes && ramOn[i] && !down[i], bj = foes && ramOn[j] && !down[j];
                        double reach = (bi || bj ? Specials.BladeReachFt : CollisionFt) + combat[i].sizeFt + combat[j].sizeFt;
                        if (!Passes(before[i] - before[j], planes[i].state.pos - planes[j].state.pos, reach)) continue;
                        if (bi && !bj) Slice(i, j, res);
                        else if (bj && !bi) Slice(j, i, res);
                        else down[i] = down[j] = true;
                    }

                time += StepSec;
                strike?.Step(this);
                if (record) for (int i = 0; i < n; i++) res.frames[i][k] = Frame(i, firing[i]);
                onStep?.Invoke(this, k);
            }
            for (int i = 0; i < n; i++) ramOn[i] = false;   // the blade cools as the turn ends
            turn++;
            return res;
        }

        /// <summary>Any gravity well open now.</summary>
        bool AnyWellOpen()
        {
            foreach (var b in bullets) if (b is Well w && w.Open) return true;
            return false;
        }

        /// <summary>The pull of every open gravity well on something at p, ft/s², for a strongest pull of peakG (see Specials).</summary>
        public Vec3d WellPull(Vec3d p, double peakG)
        {
            Vec3d a = Vec3d.Zero;
            foreach (var b in bullets) if (b is Well w && w.Open) a = a + w.Pull(p, peakG);
            return a;
        }

        /// <summary>Inside an open gravity well's capture: a missile there loses its target.</summary>
        bool InWellCore(Vec3d p)
        {
            foreach (var b in bullets) if (b is Well w && w.Strength > 0.5 && (w.pos - p).Length < Specials.WellCaptureFt) return true;
            return false;
        }

        /// <summary>One step of the gravity wells' pull on every plane still flying.</summary>
        void PullPlanes()
        {
            if (!AnyWellOpen()) return;
            for (int i = 0; i < Count; i++)
            {
                if (down[i] || crashed[i] || strike != null && strike.Held(i)) continue;
                Vec3d a = WellPull(planes[i].state.pos, Specials.WellPlaneG);
                if (a.Length > 1e-9) planes[i].Push(a, StepSec);
            }
        }

        /// <summary>
        /// A round strikes aircraft target's ricochet shield (see Specials): it bounces off the bubble where it struck, mirrored
        /// about it, and is the shield's from now on (its side, its owner, for the kill), with MirrorDamage of its punch.
        /// </summary>
        void Ricochet(Bullet b, int target, TurnResult res, Vec3d r0, Vec3d r1, double reach)
        {
            var m = planes[target];
            Vec3d d = r1 - r0;
            double dd = Vec3d.Dot(d, d);
            double s = dd > 1e-12 ? Math.Max(0, Math.Min(1, -Vec3d.Dot(r0, d) / dd)) : 0;
            Vec3d c = r0 + d * s;
            Vec3d entry = dd > 1e-12 ? c - d * (Math.Sqrt(Math.Max(0, reach * reach - Vec3d.Dot(c, c)) / dd)) : r1;
            Vec3d rel = Specials.Bounce(entry, b.vel - m.state.vel, out var normal);
            int shooter = b.owner;
            b.pos = m.state.pos + normal * (reach + 1);
            b.vel = m.state.vel + rel;
            b.owner = target; b.side = side[target];
            b.damage *= Specials.MirrorDamage;
            res.ricochets.Add(new HitEvent { time = time + StepSec, shooter = shooter, target = target, pos = b.pos, fireTime = b.fireTime, local = normal, system = -1 });
        }

        /// <summary>The heat blade on aircraft a cuts aircraft b in half: down at once, recorded as a hit for the flash and the tally.</summary>
        void Slice(int a, int b, TurnResult res)
        {
            if (down[b]) return;
            var mb = planes[b];
            res.hits.Add(new HitEvent
            {
                time = time + StepSec, shooter = a, target = b, pos = mb.state.pos, fireTime = time, damage = 1,
                local = Vec3d.Zero, system = (int)SystemId.Wings, wear = 1, downed = true,
            });
            mb.systems.wings = 1;   // cut in half: even a spared one (it is not a hit the guns could hold back)
            down[b] = true;
        }

        AircraftFrame Frame(int i, bool firing)
        {
            var m = planes[i];
            return new AircraftFrame
            {
                time = time, pos = m.state.pos, vel = m.state.vel, up = m.state.up,
                alphaDeg = m.AlphaDeg, g = m.g, throttle = m.throttle, firing = firing, stalled = m.stalled, departed = m.Departed, down = down[i],
                rocket = m.RocketBurning, brake = m.brakeOut, wep = m.wepOn, ram = ramOn[i] && !down[i], cloaked = Cloaked(i), mirror = Mirrored(i), nose = m.Forward,
                greyOut = down[i] ? 0 : m.GreyOut, bailed = m.systems.bailed, bailAt = m.systems.bailAt, bailPos = m.systems.bailPos, bailVel = m.systems.bailVel,
            };
        }

        static void StartTrace(TurnResult res, Dictionary<Bullet, Trace> traceOf, Bullet b, int frame)
        {
            var tr = new Trace { owner = b.owner, startFrame = frame, missile = b is Missile, radar = b is Missile rm && rm.radar, chaff = b is Chaff, mine = b is Mine, well = b is Well, age0 = b.age };
            tr.points.Add(b.pos);
            traceOf[b] = tr;
            if (b is Flare) res.flares.Add(tr); else if (b is Chaff) res.chaff.Add(tr); else if (b is Mine) res.mines.Add(tr); else if (b is Well) res.wells.Add(tr); else res.traces.Add(tr);
        }

        /// <summary>Aircraft i's radar can see aircraft j: an enemy still flying, not stealthy, inside its cone and out to range (times rangeFactor).</summary>
        public bool InRadarCone(int i, int j, double rangeFactor = 1)
        {
            if (i < 0 || j < 0 || i >= Count || j >= Count || side[i] == side[j] || Out(i) || Out(j) || Cloaked(j)) return false;
            double reach = combat[i].radarRangeFt * rangeFactor;
            if (reach <= 0) return false;
            Vec3d r = planes[j].state.pos - planes[i].state.pos;
            double range = r.Length;
            if (range > reach || range < 1) return false;
            return Vec3d.Dot(r * (1 / range), planes[i].Forward) >= Math.Cos(Radar.ConeDeg * Math.PI / 180);
        }

        /// <summary>Aircraft i's radar is locked: it has tracked the same target for a whole turn (Radar.LockSec).</summary>
        public bool Locked(int i) => i >= 0 && i < Count && radarTrack[i] >= 0 && trackSec[i] >= Radar.LockSec - 1e-9;

        /// <summary>
        /// The radar that fired a missile still lights its target, so the missile can guide: it still tracks that target in its
        /// cone. A shooter left out of a smaller battle (the AI's quick tries, owner &lt; 0) is taken to hold it.
        /// </summary>
        public bool Illuminating(int owner, int target)
        {
            if (owner < 0 || owner >= Count) return true;
            return radarTrack[owner] == target && InRadarCone(owner, target, Radar.HoldRangeFactor);
        }

        /// <summary>
        /// Each step, every radar picks who it tracks (see Radar): the one asked for if it is in the cone, else the one it
        /// was tracking if it still is, else the one nearest the nose. The same one again adds to the time tracked; anyone
        /// else starts it over.
        /// </summary>
        void TrackRadars(TurnPlan[] plans)
        {
            for (int i = 0; i < Count; i++)
            {
                if (combat[i].radarRangeFt <= 0 || Out(i)) { radarTrack[i] = -1; trackSec[i] = 0; continue; }
                int want = plans[i].radarTarget, cur = radarTrack[i], pick = -1;
                // a radar missile in the air keeps the radar on its target (switching would drop it)
                foreach (var b in bullets) if (b is Missile ms && ms.radar && ms.owner == i && ms.target >= 0 && ms.target == cur) { want = cur; break; }
                if (InRadarCone(i, want, Radar.HoldRangeFactor) && (want == cur || InRadarCone(i, want))) pick = want;
                else if (InRadarCone(i, cur, Radar.HoldRangeFactor)) pick = cur;
                else
                {
                    double best = -2;
                    for (int j = 0; j < Count; j++)
                    {
                        if (!InRadarCone(i, j)) continue;
                        Vec3d r = (planes[j].state.pos - planes[i].state.pos).Normalized;
                        double c = Vec3d.Dot(r, planes[i].Forward);
                        if (c > best) { best = c; pick = j; }
                    }
                }
                if (pick != cur) { radarTrack[i] = pick; trackSec[i] = pick >= 0 ? StepSec : 0; }
                else if (pick >= 0) trackSec[i] += StepSec;
                else trackSec[i] = 0;
            }
        }

        /// <summary>
        /// Each step: for every radar missile still on its target and closing inside Radar.ChaffWarnFt, the target lets go a
        /// bundle of chaff (if it has any and isn't holding its countermeasures), up to ChaffPerMissile, ChaffGapSec apart.
        /// Each has one chance to break the missile: Radar.ChaffBreak, far better while the target is in the notch.
        /// </summary>
        void DropChaff(TurnPlan[] plans, TurnResult res, Dictionary<Bullet, Trace> traceOf, int k)
        {
            for (int bi = 0; bi < bullets.Count; bi++)
            {
                if (!(bullets[bi] is Missile ms) || !ms.radar || ms.target < 0 || ms.target >= Count) continue;
                int i = ms.target;
                if (Out(i) || plans[i].holdFlares || chaff[i] <= 0) continue;
                if (ms.chaffDrawn >= Radar.ChaffPerMissile || time + 1e-9 < ms.nextChaffAt) continue;
                if ((planes[i].state.pos - ms.pos).Length > Radar.ChaffWarnFt) continue;
                var m = planes[i];
                var c = Chaff.Make(i, side[i], m.state.pos - m.Forward * 15 + m.Up * 2, m.state.vel * 0.6 + m.Up * 20, time + StepSec);
                bullets.Add(c);
                chaff[i]--;
                ms.chaffDrawn++;
                ms.nextChaffAt = time + Radar.ChaffGapSec;
                if (traceOf != null) StartTrace(res, traceOf, c, k);
                bool notching = Radar.Beaming(m.state.vel, m.state.pos - ms.pos);
                if (rng.Next() < Radar.ChaffBreak(notching)) ms.Lose(MissileLoss.Chaff);
            }
        }

        Flare FlareById(int id)
        {
            foreach (var b in bullets) if (b is Flare f && f.id == id) return f;
            return null;
        }

        /// <summary>
        /// Each step: for every seeker still on its target and closing inside FlareWarnFt, the target lets go a flare (if it has
        /// any and isn't holding them), up to FlaresPerMissile for that missile, FlareGapSec apart. Each flare has one chance
        /// (Heat.Lure, against how hot the target looks to that seeker) to pull every seeker on that target, close by, off onto itself.
        /// </summary>
        void DropFlares(TurnPlan[] plans, TurnResult res, Dictionary<Bullet, Trace> traceOf, int k)
        {
            for (int bi = 0; bi < bullets.Count; bi++)
            {
                if (!(bullets[bi] is Missile ms) || ms.radar || ms.target < 0 || ms.target >= Count) continue;
                int i = ms.target;
                if (Out(i) || plans[i].holdFlares || flares[i] <= 0) continue;
                if (ms.flaresDrawn >= Heat.FlaresPerMissile || time + 1e-9 < ms.nextFlareAt) continue;
                if ((planes[i].state.pos - ms.pos).Length > Heat.FlareWarnFt) continue;
                var m = planes[i];
                // out behind and below, kicked off the airflow
                double kick = (nextFlareId % 2 == 0 ? 1 : -1) * 15;   // left, then right
                var f = Flare.Make(nextFlareId++, i, side[i], m.state.pos - m.Forward * 15 - m.Up * 3,
                                   m.state.vel * 0.85 - m.Up * 40 + m.Right * kick, time + StepSec);
                bullets.Add(f);
                flares[i]--;
                if (traceOf != null) StartTrace(res, traceOf, f, k);
                // the flare's one chance on every seeker after this aircraft, close enough to see the flare beside it
                foreach (var b in bullets)
                {
                    if (!(b is Missile mm) || mm.radar || mm.target != i) continue;
                    if ((m.state.pos - mm.pos).Length > Heat.FlareWarnFt * 1.5) continue;
                    mm.flaresDrawn++;
                    mm.nextFlareAt = time + Heat.FlareGapSec;
                    double seen = HeatOf(i, mm.pos);
                    if (rng.Next() < Heat.Lure(seen)) { mm.target = -1; mm.decoy = f.id; }
                }
            }
        }

        Bullet Fire(int i, double late)
        {
            var m = planes[i];
            var cn = combat[i];
            Vec3d dir = GunLine(m);
            if (dispersion)
            {
                double s = cn.spreadMil / 1000;
                dir = (dir + m.Right * (rng.Gauss() * s) + m.Up * (rng.Gauss() * s)).Normalized;
            }
            Vec3d vel = m.state.vel + dir * cn.muzzleFps;
            // Leave from just ahead of the aircraft; a packet fired partway through the step has flown a little already
            return new Bullet
            {
                owner = i, side = side[i], damage = cn.damage, hitRadiusFt = cn.hitRadiusFt, dragPerSec = cn.dragPerSec, lifeSec = cn.lifeSec,
                pos = m.state.pos + dir * 12 + vel * late, vel = vel, age = late, fireTime = time + StepSec - late,
            };
        }

        /// <summary>A flak gun's step: start a burst at whoever it can see, fire packets through it, then pause.</summary>
        void FireFlak(int f, TurnResult res, Dictionary<Bullet, Trace> traceOf, int k)
        {
            var g = flak[f];
            if (g.carrier >= 0)
            {
                if (g.carrier >= Count || Out(g.carrier)) return;   // the gunner went down with the bomber
                g.pos = planes[g.carrier].state.pos; g.vel = planes[g.carrier].state.vel;
            }
            double cycle = g.burstSec + g.pauseSec;
            g.phase += StepSec;
            if (g.phase >= cycle) { g.phase -= cycle; g.target = PickFlakTarget(g); g.fireClock = 0; g.errA = rng.Gauss() * g.aimErrorMil / 1000; g.errB = rng.Gauss() * g.aimErrorMil / 1000; }
            if (g.phase >= g.burstSec || g.target < 0 || Out(g.target) || Cloaked(g.target)) return;
            g.fireClock -= StepSec;
            while (g.fireClock <= 0)
            {
                double late = -g.fireClock;
                g.fireClock += g.packetSec;
                Vec3d dir = g.Aim(planes[g.target]);
                if (dispersion)
                {
                    double s = g.spreadMil / 1000;
                    dir = (dir + new Vec3d(rng.Gauss() * s, rng.Gauss() * s, rng.Gauss() * s)).Normalized;
                }
                Vec3d vel = dir * g.muzzleFps + g.vel;
                var b = new Bullet
                {
                    owner = -1 - f, side = g.side, damage = g.damage, hitRadiusFt = g.hitRadiusFt, dragPerSec = g.dragPerSec, lifeSec = g.lifeSec,
                    pos = g.pos + dir * (g.carrier >= 0 ? 40 : 6) + vel * late, vel = vel, age = late, fireTime = time + StepSec - late,
                };
                bullets.Add(b);
                if (traceOf != null) StartTrace(res, traceOf, b, k);
            }
        }

        int PickFlakTarget(FlakSite g)
        {
            // the gunners go for fighters first: they are the low, fast threat; a bomber only when no fighter is in reach
            int best = -1; double bestR = double.PositiveInfinity;
            for (int i = 0; i < Count; i++)
            {
                if (side[i] == g.side || Out(i) || Cloaked(i)) continue;
                double r = (planes[i].state.pos - g.pos).Length;
                if (r > g.rangeFt) continue;
                double cost = r + (routes[i] != null ? g.rangeFt * g.bomberReluctance : 0);
                if (cost < bestR && CanSee(g.pos + new Vec3d(0, 0, 8), planes[i].state.pos)) { bestR = cost; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Who aircraft i's seeker can lock now: the wanted one if it is in the cone and range, else the one nearest the nose; -1 none.
        /// </summary>
        public int LockTarget(int i, int wanted = -1)
        {
            var m = planes[i];
            double cos = Math.Cos(Missile.LockConeDeg * Math.PI / 180);
            int best = -1; double bestCos = cos;
            for (int j = 0; j < Count; j++)
            {
                if (side[j] == side[i] || Out(j) || combat[j].iced) continue;   // iced: nothing for a seeker to see
                Vec3d r = planes[j].state.pos - m.state.pos;
                double range = r.Length;
                if (range < Missile.MinRangeFt || range > Heat.LockRangeFt(HeatOf(j, m.state.pos))) continue;   // a hot one locks from farther
                double c = Vec3d.Dot(r * (1 / range), m.Forward);
                if (c < cos) continue;
                if (Vec3d.Dot(r * (-1 / range), planes[j].Forward) > Math.Cos(Missile.NoLockNoseDeg * Math.PI / 180)) continue;   // head-on: no heat to see
                if (j == wanted) return j;
                if (c > bestCos) { bestCos = c; best = j; }
            }
            return wanted >= 0 ? (best >= 0 ? best : -1) : best;
        }

        /// <summary>A clear line between two points: over the ground all the way and through no obstacle.</summary>
        public bool CanSee(Vec3d a, Vec3d b)
        {
            int n = Math.Max(4, (int)((b - a).Length / 300));
            for (int s = 1; s < n; s++)
            {
                Vec3d p = a + (b - a) * ((double)s / n);
                if (p.z < groundFt(p.x, p.y)) return false;
            }
            return obstacleHit == null || !obstacleHit(a, b);
        }

        /// <summary>
        /// A smaller battle with only some of the aircraft (by index, in that order), for the AI to try plans in
        /// quickly. Rounds fired by the others are left out; the flak and the ground stay.
        /// </summary>
        /// <summary>
        /// True while aircraft i flies its formation spot this turn (and so plans for itself, like a route): its order is
        /// Rejoin and it has a spot to fly to. A wingman that has fallen behind is still in it, closing up.
        /// </summary>
        public bool InFormation(int i) => i >= 0 && i < Count && orders[i] == Order.Rejoin && !Out(i) && FormationSlot(i) != null;

        /// <summary>The flight's lead: the lowest number still flying, or -1.</summary>
        public int Leader(int f)
        {
            int best = -1;
            for (int i = 0; i < Count; i++)
                if (flightOf[i] == f && !Out(i) && (best < 0 || number[i] < number[best])) best = i;
            return best;
        }

        /// <summary>
        /// Where aircraft i belongs in its flight: its number's spot on the lead, or for the lead its station on the flight it
        /// escorts. Null when it has none (no flight, or a lead with nothing to escort).
        /// </summary>
        public Slot FormationSlot(int i)
        {
            int f = i < flightOf.Length ? flightOf[i] : -1;
            if (f < 0 || f >= flights.Count) return null;
            var fl = flights[f];
            int lead = Leader(f);
            if (lead < 0) return null;
            if (lead != i)
            {
                Vec3d s = Formations.SpotFt(fl, number[i], number[lead]);
                return new Slot { leader = lead, lateralFt = s.x, backFt = s.y, upFt = s.z };
            }
            if (fl.stationOn < 0) return null;
            int on = Leader(fl.stationOn);
            if (on < 0 || on == i) return null;
            return new Slot { leader = on, lateralFt = fl.stationLat, backFt = fl.stationBack, upFt = fl.stationUp };
        }

        public Battle Subset(int[] keep)
        {
            var c = (Battle)MemberwiseClone();
            int n = keep.Length;
            c.planes = new FlightModel[n]; c.combat = new CombatNumbers[n]; c.side = new int[n]; c.routes = new Route[n];
            c.ammo = new double[n]; c.missiles = new int[n]; c.flares = new int[n]; c.down = new bool[n];
            c.radarMissiles = new int[n]; c.chaff = new int[n]; c.radarTrack = new int[n]; c.trackSec = new double[n];
            c.balloonMines = new int[n]; c.heatBlades = new int[n]; c.stealths = new int[n]; c.jammers = new int[n]; c.wells = new int[n]; c.mirrors = new int[n]; c.mirrorUntil = new double[n]; c.jammedUntil = new double[n]; c.ramOn = new bool[n]; c.cloakUntil = new double[n]; c.crashed = new bool[n]; c.hitObstacle = new bool[n]; c.fireClock = new double[n];
            var map = new Dictionary<int, int>();
            for (int j = 0; j < n; j++)
            {
                int i = keep[j]; map[i] = j;
                c.planes[j] = planes[i].Clone(); c.combat[j] = combat[i]; c.side[j] = side[i]; c.routes[j] = routes[i]?.Copy();
                c.ammo[j] = ammo[i]; c.missiles[j] = missiles[i]; c.flares[j] = flares[i];
                c.radarMissiles[j] = radarMissiles[i]; c.chaff[j] = chaff[i]; c.trackSec[j] = trackSec[i];
                c.balloonMines[j] = balloonMines[i]; c.heatBlades[j] = heatBlades[i]; c.stealths[j] = stealths[i]; c.jammers[j] = jammers[i]; c.wells[j] = wells[i]; c.mirrors[j] = mirrors[i]; c.mirrorUntil[j] = mirrorUntil[i]; c.jammedUntil[j] = jammedUntil[i]; c.ramOn[j] = ramOn[i]; c.cloakUntil[j] = cloakUntil[i];
                c.down[j] = down[i]; c.crashed[j] = crashed[i]; c.hitObstacle[j] = hitObstacle[i]; c.fireClock[j] = fireClock[i];
            }
            for (int j = 0; j < n; j++)
            {
                int t = radarTrack[keep[j]];
                c.radarTrack[j] = t >= 0 && map.TryGetValue(t, out int tj) ? tj : -1;
                if (c.radarTrack[j] < 0) c.trackSec[j] = 0;
            }
            c.flightOf = new int[n]; c.number = new int[n]; c.orders = new Order[n];
            for (int j = 0; j < n; j++) { c.flightOf[j] = flightOf[keep[j]]; c.number[j] = number[keep[j]]; c.orders[j] = orders[keep[j]]; }
            c.flights = flights.ConvertAll(f => f.Copy());
            c.bullets = new List<Bullet>();
            foreach (var b in bullets)
            {
                var ms = b as Missile;
                bool aimedIn = ms != null && ms.target >= 0 && map.ContainsKey(ms.target);
                if (b.owner >= 0 && !map.ContainsKey(b.owner) && !aimedIn && !(b is Mine) && !(b is Well)) continue;   // keep a missile chasing someone kept, whoever fired it, and every mine and well hanging there
                var bc = b.Copy();
                if (b.owner >= 0) bc.owner = map.TryGetValue(b.owner, out int oj) ? oj : -1000;
                if (bc is Missile mc) { mc.target = aimedIn ? map[ms.target] : -1; mc.aimedAt = ms.aimedAt >= 0 && map.TryGetValue(ms.aimedAt, out int aj) ? aj : -1; }
                c.bullets.Add(bc);
            }
            var kept = new List<FlakSite>();
            foreach (var g0 in flak)
            {
                var g = g0.Copy();
                if (g.carrier >= 0)
                {
                    if (!map.TryGetValue(g.carrier, out int cj)) continue;   // its bomber is left out: so is the gunner
                    g.carrier = cj; g.pos = planes[g0.carrier].state.pos; g.vel = planes[g0.carrier].state.vel;
                }
                g.target = g0.target >= 0 && map.TryGetValue(g0.target, out int t) ? t : -1;
                kept.Add(g);
            }
            c.flak = kept.ToArray();
            c.strike = null;   // the AI's quick tries leave out the look-out and the runway (the ones waiting are left out of them anyway)
            return c;
        }

        /// <summary>What share of its toughness each system takes before it is knocked out (the wings, bigger, take more).</summary>
        public const double SystemShare = 0.6, WingsShare = 0.8;

        /// <summary>The damage a system of aircraft i takes before it is knocked out.</summary>
        public double Capacity(int i, SystemId s) => Math.Max(1e-6, combat[i].toughness * (s == SystemId.Wings ? WingsShare : SystemShare));

        /// <summary>What hits have done to aircraft i (see Systems).</summary>
        public Systems SystemsOf(int i) => planes[i].systems;

        /// <summary>A system is gone: down, and with the engine dead the pilot bails out (a parachute; the aircraft falls on and crashes).</summary>
        void GoDown(int i)
        {
            down[i] = true;
            var m = planes[i];
            if (m.systems.EngineDead && !m.systems.bailed)
            {
                m.systems.bailed = true; m.systems.bailAt = time; m.systems.bailPos = m.state.pos; m.systems.bailVel = m.state.vel;
            }
        }

        /// <summary>Wear a copy of an aircraft's systems by one recorded hit (playback rebuilds the damage hit by hit this way).</summary>
        public static void Apply(ref Systems s, HitEvent h)
        {
            if (h.system < 0) for (int k = 0; k < Systems.Count; k++) s.Wear((SystemId)k, h.wear);
            else s.Wear((SystemId)h.system, h.wear);
        }

        /// <summary>Everyone's systems as they stand now.</summary>
        public Systems[] AllSystems()
        {
            var a = new Systems[Count];
            for (int i = 0; i < Count; i++) a[i] = planes[i].systems;
            return a;
        }

        /// <summary>Hits on aircraft i land but do nothing (a "what if" keeps a target flying to see the whole burst).</summary>
        public void Spare(int i) => planes[i].systems.spared = true;

        /// <summary>
        /// A packet strikes. A gun round knocks at one system, the one where it enters the target's hit sphere (see
        /// Systems.Struck); a missile's or a mine's blast wears every system by its share of the airframe's toughness.
        /// r0 and r1 are the round's position relative to the target over the step, reach the hit sphere's radius.
        /// </summary>
        void Hit(Bullet b, int target, TurnResult res, Vec3d r0, Vec3d r1, double reach)
        {
            if (b is Missile ir && !ir.radar && combat[target].iced) return;   // a heat-seeker shatters on the ice
            var m = planes[target];
            // where it enters the sphere: back along its path from the closest pass
            Vec3d d = r1 - r0;
            double dd = Vec3d.Dot(d, d);
            double s = dd > 1e-12 ? Math.Max(0, Math.Min(1, -Vec3d.Dot(r0, d) / dd)) : 0;
            Vec3d c = r0 + d * s;
            Vec3d entry = dd > 1e-12 ? c - d * (Math.Sqrt(Math.Max(0, reach * reach - Vec3d.Dot(c, c)) / dd)) : r1;
            var local = new Vec3d(Vec3d.Dot(entry, m.Right), Vec3d.Dot(entry, m.Forward), Vec3d.Dot(entry, m.Up));
            bool blast = b is Missile || b is Mine;
            var sys = Systems.Struck(local.x, local.y, local.z);
            var e = new HitEvent
            {
                time = time + StepSec, shooter = b.owner, target = target, pos = b.pos, fireTime = b.fireTime, damage = b.damage,
                local = local, system = blast ? -1 : (int)sys,
            };
            if (!down[target] && !m.systems.spared)
            {
                if (blast)
                {
                    e.wear = b.damage / Math.Max(1e-6, combat[target].toughness);
                    for (int k = 0; k < Systems.Count; k++) m.systems.Wear((SystemId)k, e.wear);
                }
                else
                {
                    e.wear = b.damage / Capacity(target, sys);
                    m.systems.Wear(sys, e.wear);
                }
                if (m.systems.Lost) { GoDown(target); e.downed = true; }
            }
            res.hits.Add(e);
        }

        /// <summary>How close a straight relative move from r0 to r1 comes to the origin.</summary>
        static double Closest(Vec3d r0, Vec3d r1)
        {
            Vec3d d = r1 - r0;
            double dd = Vec3d.Dot(d, d);
            double s = dd > 1e-12 ? Math.Max(0, Math.Min(1, -Vec3d.Dot(r0, d) / dd)) : 0;
            Vec3d c = r0 + d * s;
            return Math.Sqrt(Vec3d.Dot(c, c));
        }

        /// <summary>Whether a straight relative move from r0 to r1 comes within radius of the origin.</summary>
        static bool Passes(Vec3d r0, Vec3d r1, double radius)
        {
            Vec3d d = r1 - r0;
            double dd = Vec3d.Dot(d, d);
            double s = dd > 1e-12 ? Math.Max(0, Math.Min(1, -Vec3d.Dot(r0, d) / dd)) : 0;
            Vec3d c = r0 + d * s;
            return Vec3d.Dot(c, c) < radius * radius;
        }
    }
}
