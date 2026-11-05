using System;

namespace Ooda.Flight.Core
{
    /// <summary>The parts of an aircraft a hit can knock out. Where a round lands decides which (see Systems.Struck).</summary>
    public enum SystemId { Engine = 0, Fuel = 1, Controls = 2, Wings = 3 }

    /// <summary>
    /// What a hit has done to an aircraft, in place of a single health bar. Four systems, each from 0 (whole) to 1
    /// (knocked out); past Damaged (a half) it hurts how the aircraft flies:
    ///
    /// - Engine: damaged, the power is held to EnginePowerCap and there is no WEP (or afterburner); out, the engine stops.
    /// - Fuel: damaged, it leaks, and the engine quits LeakSec later; out, it is on fire.
    /// - Controls: damaged, the roll rate and the G it can pull fall (ControlsRoll, ControlsG); out, the tail is shot away.
    /// - Wings: damaged, more drag and less G (WingsDrag, WingsG); out, the wing folds.
    ///
    /// Any system out brings the aircraft down (Battle checks Lost). It lives on the FlightModel, so a clone, a swap,
    /// a subset and a network snapshot carry it with the rest of the aircraft. Pure C#.
    /// </summary>
    [Serializable]
    public struct Systems
    {
        public const double Damaged = 0.5;
        public const double EnginePowerCap = 0.6;
        public const double LeakSec = 30;
        public const double ControlsRoll = 0.5, ControlsG = 0.7;
        public const double WingsDrag = 1.2, WingsG = 0.75;
        public const int Count = 4;

        /// <summary>How near each system is to being knocked out: 0 whole, 1 out.</summary>
        public double engine, fuel, controls, wings;
        /// <summary>Seconds of fuel left once the tanks are holed (counts down only while Leaking).</summary>
        public double leakLeftSec;
        /// <summary>
        /// The pilot bailed out (the engine died): when (battle time), and where and how fast the aircraft was going, so a
        /// parachute can be drawn from there. The aircraft falls on without him.
        /// </summary>
        public bool bailed;
        public double bailAt;
        public Vec3d bailPos, bailVel;
        /// <summary>Hits land but do nothing: the AI's and the preview's "what if" keeps a target flying to see the whole burst.</summary>
        public bool spared;

        public double this[SystemId s]
        {
            get => s == SystemId.Engine ? engine : s == SystemId.Fuel ? fuel : s == SystemId.Controls ? controls : wings;
            set
            {
                value = Math.Max(0, Math.Min(1, value));
                if (s == SystemId.Engine) engine = value;
                else if (s == SystemId.Fuel) fuel = value;
                else if (s == SystemId.Controls) controls = value;
                else wings = value;
            }
        }

        public bool IsDamaged(SystemId s) => this[s] >= Damaged - 1e-9;
        public bool IsOut(SystemId s) => this[s] >= 1 - 1e-9;

        public bool Leaking => IsDamaged(SystemId.Fuel);
        /// <summary>The engine has stopped: shot out, or starved once the leak ran dry.</summary>
        public bool EngineDead => IsOut(SystemId.Engine) || Leaking && leakLeftSec <= 0;
        /// <summary>A system is gone (or the engine starved): the aircraft is going down.</summary>
        public bool Lost => EngineDead || IsOut(SystemId.Fuel) || IsOut(SystemId.Controls) || IsOut(SystemId.Wings);
        /// <summary>The one that brought it down, or null while it flies.</summary>
        public SystemId? LostTo => IsOut(SystemId.Wings) ? SystemId.Wings : IsOut(SystemId.Controls) ? SystemId.Controls
            : IsOut(SystemId.Fuel) ? SystemId.Fuel : EngineDead ? SystemId.Engine : (SystemId?)null;
        /// <summary>Any system hurt at all.</summary>
        public bool Hurt => engine > 0 || fuel > 0 || controls > 0 || wings > 0;
        /// <summary>Any system past Damaged.</summary>
        public bool AnyDamaged => IsDamaged(SystemId.Engine) || Leaking || IsDamaged(SystemId.Controls) || IsDamaged(SystemId.Wings);
        /// <summary>The worst system, 0 to 1: how near it is to coming down.</summary>
        public double Worst => Math.Max(Math.Max(engine, fuel), Math.Max(controls, wings));
        /// <summary>All the harm taken, 0 to 4 (the AI scores a "what if" by it).</summary>
        public double Total => engine + fuel + controls + wings;

        /// <summary>Add wear to one system; holing the tanks starts the leak.</summary>
        public void Wear(SystemId s, double amount)
        {
            if (spared || amount <= 0) return;
            bool wasLeaking = Leaking;
            this[s] = this[s] + amount;
            if (!wasLeaking && Leaking) leakLeftSec = LeakSec;
        }

        /// <summary>Parachute: the pilot carries the aircraft's speed until the drag takes it (ChuteLagSec), then sinks at ChuteSinkFtps.</summary>
        public const double ChuteLagSec = 1.5, ChuteSinkFtps = 18;

        /// <summary>Where a pilot who bailed out from p0 at v0 is t seconds later (ft; before the ground stops him).</summary>
        public static Vec3d ChuteAt(Vec3d p0, Vec3d v0, double t)
        {
            t = Math.Max(0, t);
            double carry = ChuteLagSec * (1 - Math.Exp(-t / ChuteLagSec));
            return new Vec3d(p0.x + v0.x * carry, p0.y + v0.y * carry, p0.z + v0.z * carry - ChuteSinkFtps * t);
        }

        /// <summary>Run the leak for dt seconds.</summary>
        public void Step(double dt)
        {
            if (Leaking && leakLeftSec > 0) { leakLeftSec -= dt; if (leakLeftSec < 1e-6) leakLeftSec = 0; }   // dry, without a rounding crumb left
        }

        /// <summary>The share of the engine's power still there (1 whole, 0 stopped).</summary>
        public double PowerCap => EngineDead ? 0 : IsDamaged(SystemId.Engine) ? EnginePowerCap : 1;
        public bool WepOut => IsDamaged(SystemId.Engine) || EngineDead;
        public double RollFactor => IsDamaged(SystemId.Controls) ? ControlsRoll : 1;
        public double GFactor => (IsDamaged(SystemId.Controls) ? ControlsG : 1) * (IsDamaged(SystemId.Wings) ? WingsG : 1);
        public double DragFactor => IsDamaged(SystemId.Wings) ? WingsDrag : 1;

        /// <summary>
        /// Which system a round strikes, from where it lands in the aircraft's own frame (x right, y forward, z up): the
        /// nose is the engine, the tail the controls, out along the wings the wings, and the fuselage between, from above
        /// or below, the tanks. A round reaches the side it comes from, so the angle off decides: from dead astern the
        /// tail, a deflection shot the wings, head-on the engine.
        /// </summary>
        public static SystemId Struck(double x, double y, double z)
        {
            double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
            if (ay >= ax && ay >= az * 0.8) return y > 0 ? SystemId.Engine : SystemId.Controls;
            return ax >= az ? SystemId.Wings : SystemId.Fuel;
        }

        public static string Name(SystemId s) => s == SystemId.Engine ? "Engine" : s == SystemId.Fuel ? "Fuel" : s == SystemId.Controls ? "Controls" : "Wings";
        public static string Short(SystemId s) => s == SystemId.Engine ? "ENG" : s == SystemId.Fuel ? "FUEL" : s == SystemId.Controls ? "CTRL" : "WING";
    }
}
