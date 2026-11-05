// Checks of vertical flight without Unity (FlightModel.StepVertical): pulled up to vertical and out of speed, a P-51
// slides back and falls through (the tail slide); with full power and full rudder at the top it swings over instead
// (the hammerhead); a jet barely can; normal flight never goes vertical; the pedals reach the controls from a plan; and
// the state survives a clone and the trip to bytes.
// Usage: dotnet run   (exit code = failures)
using System;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); }

    /// <summary>Straight up at ktas, wings level, heading north.</summary>
    static FlightModel Up(AircraftNumbers n, double ktas, double throttle) =>
        new FlightModel(n, PointMass.Make(new Vec3d(0, 0, 8000), ktas, Math.PI / 2, 88 * Math.PI / 180)) { throttle = throttle };

    static readonly Vec3d Sky = new Vec3d(0, 0, 1);
    /// <summary>The planner's autopilot holding the arrow straight up, with the throttle and the pedals given.</summary>
    static FlightControls HoldUp(FlightModel m, double throttle, double rudder = 0)
    {
        var c = AutoPilot.Controls(m, Sky, throttle, false);
        c.yaw = rudder;
        return c;
    }

    class Run { public Vec3d rightAtEntry; public double sideOnRight = 0, entrySec = -1; public double Swing => secToNoseDown - entrySec; public bool wentVertical, slidBack, noseDown, flyingAgain; public double maxBackFtps, altLost, minNoseZ = 1, sideAtHorizontal, secToNoseDown = -1; }

    static Run Fly(FlightModel m, Func<FlightModel, FlightControls> stick, double sec)
    {
        var r = new Run(); double top = m.AltFt; const double dt = 0.02;
        for (double t = 0; t < sec; t += dt)
        {
            m.Step(stick(m), dt);
            top = Math.Max(top, m.AltFt);
            if (m.vertical && !r.wentVertical) { r.rightAtEntry = m.Right; r.entrySec = t; }
            if (m.vertical) r.wentVertical = true;
            double along = Vec3d.Dot(m.state.vel, m.Forward);
            if (along < -5) r.slidBack = true;
            r.maxBackFtps = Math.Max(r.maxBackFtps, -along);
            if (r.wentVertical && Math.Abs(m.Forward.z) < 0.1 && r.sideAtHorizontal == 0) r.sideAtHorizontal = Vec3d.Dot(m.Forward, r.rightAtEntry) + 1e-9;   // which way the nose came through the horizon, against the right wing at the top
            if (m.Forward.z < -0.8 && !r.noseDown) { r.noseDown = true; r.secToNoseDown = t; }
            r.minNoseZ = Math.Min(r.minNoseZ, m.Forward.z);
            if (r.wentVertical && !m.vertical) r.flyingAgain = true;
        }
        r.altLost = top - m.AltFt;
        return r;
    }

    static int Main()
    {
        Console.WriteLine("Tail slide: straight up, idle, stick neutral");
        {
            var m = Up(AircraftNumbers.P51D(), 220, 0);
            var r = Fly(m, x => HoldUp(x, 0), 20);
            Check(r.wentVertical, "out of speed nose high, it goes into vertical flight");
            Check(r.slidBack && r.maxBackFtps > 15, $"it slides back tail first (up to {r.maxBackFtps:F0} ft/s)");
            Check(r.noseDown, $"and the nose falls through to point down ({r.secToNoseDown:F1} s)");
            Check(r.flyingAgain && !m.vertical, $"then it flies again, at {m.Kias:F0} KIAS, {r.altLost:F0} ft below the top");
        }

        Console.WriteLine("\nHammerhead: straight up, full power, full right rudder at the top");
        {
            var m = Up(AircraftNumbers.P51D(), 220, 1);
            var r = Fly(m, x => HoldUp(x, 1, x.vertical ? 1 : 0), 30);
            Check(r.wentVertical && r.noseDown, $"it swings over to point down ({r.secToNoseDown:F1} s)");
            Check(r.maxBackFtps < 15, $"without sliding back much ({r.maxBackFtps:F0} ft/s at most)");
            Check(r.sideAtHorizontal > 0.5, $"the nose comes over toward the right wing ({r.sideAtHorizontal:F2} along it as it crosses the horizon)");
            Check(r.flyingAgain, $"and it flies on, {r.altLost:F0} ft below the top");
            var l = Up(AircraftNumbers.P51D(), 220, 1);
            var rl = Fly(l, x => HoldUp(x, 1, x.vertical ? -1 : 0), 20);
            Check(rl.sideAtHorizontal < -0.5, "left rudder brings it over on the left");
        }

        Console.WriteLine("\nAn F-4 has little propeller blast to swing it");
        {
            var jet = Up(AircraftNumbers.Phantom(), 300, 1);
            var rj = Fly(jet, x => HoldUp(x, 1, x.vertical ? 1 : 0), 40);
            var prop = Up(AircraftNumbers.P51D(), 220, 1);
            var rp = Fly(prop, x => HoldUp(x, 1, x.vertical ? 1 : 0), 30);
            Check(rj.wentVertical && rj.noseDown && rp.noseDown && rj.Swing > rp.Swing,
                  $"it takes longer from the top to nose down: {rj.Swing:F1} s against the P-51's {rp.Swing:F1} s");
        }

        Console.WriteLine("\nNormal flight is untouched");
        {
            var m = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 0, 8000), 300, 0)) { throttle = 1 };
            var r = Fly(m, x => new FlightControls { pitch = 0.6, roll = 0.2, throttle = 1 }, 20);
            Check(!r.wentVertical, "hard turning at 300 kt never goes vertical");
            var loop = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 0, 8000), 380, 0)) { throttle = 1 };
            var rl = Fly(loop, x => new FlightControls { pitch = 0.5, throttle = 1 }, 12);
            Check(!rl.wentVertical, "a fast loop goes over the top without going vertical");
        }

        Console.WriteLine("\nThe pedals from a plan, and the state in copies");
        {
            var m = Up(AircraftNumbers.P51D(), 220, 1);
            var plan = new TurnPlan { aim = m.Forward, throttle = 1, rudder = 0.75 };
            Check(Math.Abs(plan.ControlsFor(m, 0).yaw - 0.75) < 1e-9, "a plan's rudder reaches the pedals");
            var m2 = Up(AircraftNumbers.P51D(), 220, 0);
            for (int k = 0; k < 1200 && !(m2.vertical && Vec3d.Dot(m2.state.vel, m2.Forward) < -10); k++) m2.Step(HoldUp(m2, 0), 0.02);   // sliding back
            var c = m2.Clone();
            for (int k = 0; k < 100; k++) { m2.Step(new FlightControls(), 0.02); c.Step(new FlightControls(), 0.02); }
            Check(m2.vertical && (m2.state.pos - c.state.pos).Length < 1e-9 && (m2.nose - c.nose).Length < 1e-12, "a clone in vertical flight flies the same");
            var b = new Battle(m2.Clone(), CombatNumbers.P51D(), new FlightModel(AircraftNumbers.Bf109G(), PointMass.Make(new Vec3d(0, 9000, 8000), 300, 0)), CombatNumbers.Bf109G());
            var back = new Battle(Up(AircraftNumbers.P51D(), 220, 0), CombatNumbers.P51D(), new FlightModel(AircraftNumbers.Bf109G(), PointMass.Make(new Vec3d(0, 9000, 8000), 300, 0)), CombatNumbers.Bf109G());
            Wire.Restore(back, Wire.Encode(b));
            Check(back.planes[0].vertical && (back.planes[0].nose - b.planes[0].nose).Length < 1e-12 && Wire.Hash(back) == Wire.Hash(b), "a snapshot carries it across");
            var res = b.Resolve(new TurnPlan?[] { new TurnPlan { aim = Sky, throttle = 0 }, TurnPlan.Holding() }, 1);
            var f = res.frames[0][10];
            Check((f.Nose - f.vel.Normalized).Length > 0.1, "the recorded frames carry the nose apart from the path (for the model's pose)");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) FAILED.");
        return fails;
    }
}
