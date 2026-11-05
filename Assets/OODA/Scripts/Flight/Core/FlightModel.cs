using System;

namespace Ooda.Flight.Core
{
    /// <summary>What the pilot's hands are doing. Stick and pedals -1..1, throttle 0..1.</summary>
    public struct FlightControls
    {
        public double pitch;    // + pulls (stick back)
        public double roll;     // + rolls right
        public double yaw;      // + right pedal
        public double throttle; // 0..1
        public bool wep;        // war emergency power
        public bool rocket;     // light the rocket (if it is ready; it then burns for its time whatever the stick says)
        public bool brake;      // the speed brake out
    }

    /// <summary>Controlled flight, a departure that is still building, or a developed spin.</summary>
    public enum Departure { None, Incipient, Spin }

    /// <summary>
    /// The player's aircraft: the OODA point mass flown through the OODA limits and smoothing.
    /// The stick asks for G (neutral is 1 G, trimmed level flight; full back asks maxG, full forward minG);
    /// the G builds at the onset rate and is held under the stall line, so a hard pull at low speed only
    /// mushes. The stick's roll asks for a roll rate up to the full-stick rate at this true airspeed, built
    /// up at the roll acceleration. Nothing levels the wings or holds height: let go in a bank and it
    /// keeps the bank and the nose drops, as a trimmed aeroplane would.
    ///
    /// Held deep past the stall line (rudder or aileron makes it come sooner) a wing drops and the aircraft
    /// departs: an incipient spin, then a developed one, autorotating about the falling path at high angle
    /// of attack with the ailerons nearly useless. Standard recovery gets out: throttle back, opposite
    /// rudder and stick forward of neutral, held; an incipient one also recovers just by unloading.
    /// The departure is a layer on the point mass (it has no rotational dynamics of its own); its numbers
    /// are estimates to tune by feel.
    /// </summary>
    public class FlightModel
    {
        public AircraftNumbers numbers;
        public PointMass.State state;

        // What was flown last step (for the HUD, the picture and the pilot's head)
        public double g = 1;            // G flown
        public double gRate;            // G/s
        public double gAsked = 1;       // G the stick asked for
        public double rollRateDps;      // signed, right positive
        public double sideG;
        public double throttle = 1;     // the engine's, eased toward the lever
        public bool stalled;            // the stick asks for more than the wing gives
        public double excess;           // (thrust - drag) / weight
        public double rocketBurnSec;    // seconds of rocket burn left
        public double rocketCoolSec;    // seconds until the rocket can be lit again (counts from lighting it)
        public bool brakeOut;           // the speed brake was out last step
        public bool wepOn;              // war emergency power (a jet's afterburner) was in last step
        /// <summary>What hits have done to the engine, fuel, controls and wings (see Systems): whole in free flight.</summary>
        public Systems systems;

        /// <summary>The rocket can be lit now.</summary>
        public bool RocketReady => rocketCoolSec <= 0 && rocketBurnSec <= 0 && numbers.rocketG > 0;
        public bool RocketBurning => rocketBurnSec > 0;

        public Departure departure = Departure.None;
        public int spinDir = 1;         // +1 spinning right, -1 left
        public double departBuild;      // seconds of deep stall built up toward a departure
        public double departTime;       // seconds since departing
        public double recoverTime;      // seconds of recovery inputs held
        double alphaShown;
        uint wingDrop = 1944;           // a tiny deterministic generator, so a cloned model flies the same future

        public bool Departed => departure != Departure.None;

        /// <summary>
        /// Vertical flight: nose high and too slow for the wing to fly the path (a tail slide, a hammerhead). The nose is then
        /// its own direction (nose) and the path goes where gravity, the engine and the drag take it.
        /// </summary>
        public bool vertical;
        public Vec3d nose;
        public double Ktas => state.vel.Length * Atmosphere.FtpsToKt;
        public double AltFt => state.pos.z;
        public double Kias => Atmosphere.TasToIasKt(Ktas, AltFt);
        public double Mach => Atmosphere.Mach(Ktas, AltFt);
        /// <summary>Where the nose points: along the path, or (in vertical flight) its own way.</summary>
        public Vec3d Forward => vertical ? nose : state.vel.Normalized;
        public Vec3d Up => state.up;
        public Vec3d Right => Vec3d.Cross(Forward, Up);

        /// <summary>The slowest the point mass may fly (its path has no direction at rest), ft/s.</summary>
        public const double MinSpeedFtps = 50;

        public FlightModel(AircraftNumbers numbers, PointMass.State start)
        {
            this.numbers = numbers;
            state = start;
        }

        /// <summary>A copy that flies on independently: for the ghost path, the reach shell and the AI's look-ahead.</summary>
        public FlightModel Clone() => (FlightModel)MemberwiseClone();

        double NextWingDrop()
        {
            wingDrop = wingDrop * 1664525u + 1013904223u;
            return (wingDrop >> 8) / 16777216.0;
        }

        /// <summary>The angle of attack for the picture, degrees: from the lift asked of the wing against the stall line, or the spin's.</summary>
        public double AlphaDeg => alphaShown;

        double FlownAlphaDeg()
        {
            if (Departed) return numbers.spinAlphaDeg;
            double cl = g / Math.Max(numbers.StallLimitG(Math.Max(Kias, 1)), 1e-6); // CL / CLmax
            double a = numbers.alphaZeroLiftDeg + cl * (numbers.alphaStallDeg - numbers.alphaZeroLiftDeg);
            if (stalled && gAsked > g) a += Math.Min(8, (gAsked - g) * 2); // the nose keeps coming up past the stall
            return Math.Max(-12, Math.Min(numbers.alphaStallDeg + 8, a));
        }

        public void Step(FlightControls c, double dt)
        {
            if (vertical) { StepVertical(c, dt); return; }
            var n = numbers;
            double ktas = Ktas, kias = Kias;
            double p = Math.Max(-1, Math.Min(1, c.pitch));
            double ail = Math.Max(-1, Math.Min(1, c.roll));
            double ped = Math.Max(-1, Math.Min(1, c.yaw));

            // G: the stick's ask, eased at the onset rate and its acceleration, then held inside the wing's limits
            gAsked = p >= 0 ? 1 + p * (n.maxG - 1) : 1 + (-p) * (n.minG - 1);
            double gf = systems.GFactor;
            double hi = Math.Min(n.AvailableG(kias), Math.Max(1, n.maxG * gf)), lo = Math.Max(n.AvailableNegG(kias), Math.Min(-0.5, n.minG * gf));   // a hurt tail or wing can't take the full G
            double maxRate = n.MaxRollRateDps(ktas) * systems.RollFactor;
            double rollAccel = n.maxRollRateDps / n.rollFullRateSec;
            double extraDrag = 0;

            if (!Departed)
            {
                double wantRate = Math.Max(-n.gOnsetGps, Math.Min(n.gOnsetGps, (gAsked - g) / Math.Max(dt, 0.05)));
                double dRate = n.gOnsetAccelGps2 * dt;
                gRate = Math.Max(gRate - dRate, Math.Min(gRate + dRate, wantRate));
                double gNext = g + gRate * dt;
                stalled = gNext > hi + 1e-9 || gNext < lo - 1e-9;
                g = Math.Max(lo, Math.Min(hi, gNext));
                if (stalled) gRate = 0;

                // Roll: toward the stick's share of the full-stick rate, at the roll acceleration
                double wantRoll = ail * maxRate * (stalled ? n.stalledRollFactor : 1);
                rollRateDps = Math.Max(rollRateDps - rollAccel * dt, Math.Min(rollRateDps + rollAccel * dt, wantRoll));

                // Departure: held deep past the stall line; crossed controls bring it on sooner
                double depth = gAsked - hi;
                if (stalled && p > 0 && depth > n.departureStallMarginG)
                    departBuild += dt * (1 + 2 * Math.Abs(ped) + Math.Abs(ail));
                else
                    departBuild = Math.Max(0, departBuild - 2 * dt);
                if (departBuild > n.departureSec)
                {
                    departure = Departure.Incipient;
                    double bias = ped + 0.5 * ail;
                    spinDir = Math.Abs(bias) > 0.1 ? Math.Sign(bias) : (NextWingDrop() < 0.5 ? -1 : 1);
                    departTime = recoverTime = departBuild = 0;
                }
            }
            else
            {
                departTime += dt;
                stalled = true;
                // The stalled wing still lifts, a little; the G follows it
                g = Math.Min(n.maxG, n.spinLiftFraction * n.StallLimitG(kias));
                gRate = 0;

                // Autorotation: the roll about the path builds toward the spin rate; the ailerons barely touch it
                double build = departure == Departure.Spin ? 1 : Math.Min(1, departTime / n.incipientSec);
                double wantRoll = spinDir * n.spinRateDps * build + ail * 0.1 * maxRate;
                rollRateDps = Math.Max(rollRateDps - rollAccel * dt, Math.Min(rollRateDps + rollAccel * dt, wantRoll));
                extraDrag = n.spinDragPerWeight * Math.Pow(kias / 120.0, 2);

                // Recovery: opposite rudder and the stick forward, held; an incipient spin also recovers when unloaded
                bool antiRudder = -ped * spinDir > 0.5;
                bool unloaded = p <= 0.05;
                bool recovering = departure == Departure.Incipient ? unloaded : antiRudder && unloaded;
                recoverTime = recovering ? recoverTime + dt * (antiRudder ? 1.5 : 1) : Math.Max(0, recoverTime - dt);
                double needed = departure == Departure.Incipient ? n.incipientRecoverySec : n.spinRecoverySec;
                if (recoverTime > needed)
                {
                    departure = Departure.None;
                    departTime = recoverTime = 0;
                    g = Math.Min(g, 1);
                }
                else if (departure == Departure.Incipient && departTime > n.incipientSec && !unloaded)
                {
                    departure = Departure.Spin;
                }
            }

            // Rudder: a side force that grows with dynamic pressure (none in a spin: the rudder is fighting the rotation)
            double q = Math.Min(1, (kias / n.rudderFullKias) * (kias / n.rudderFullKias));
            sideG = Departed ? 0 : ped * n.rudderSideG * q;

            // Engine: the throttle spools toward the lever
            double lever = Math.Max(0, Math.Min(1, c.throttle));
            throttle += Math.Max(-dt * 0.8, Math.Min(dt * 0.8, lever - throttle));
            wepOn = c.wep && lever > 0.95 && !systems.WepOut;
            double power = Math.Min(throttle, systems.PowerCap) * (wepOn ? n.wepFactor : 1);
            double side = Math.Abs(sideG);
            double dragK = systems.DragFactor;

            // The rocket: lit when asked and ready, it burns for its time and then needs its cool-down before the next
            rocketCoolSec = Math.Max(0, rocketCoolSec - dt);
            if (c.rocket && RocketReady) { rocketBurnSec = n.rocketSec; rocketCoolSec = n.rocketCooldownSec; }
            double boost = 0;
            if (rocketBurnSec > 0) { boost = n.rocketG * Math.Min(1, rocketBurnSec / dt); rocketBurnSec = Math.Max(0, rocketBurnSec - dt); }
            // The speed brake: drag that grows with dynamic pressure, brakeG at 300 KIAS
            brakeOut = c.brake;
            double brakeK = c.brake ? n.brakeG / (300.0 * 300.0) : 0;
            PointMass.ExcessFn excessFn = (vKt, altFt, gg) =>
            {
                double ias = Atmosphere.TasToIasKt(vKt, altFt);
                return power * n.ThrustPerWeight(vKt, altFt) + boost - dragK * n.DragPerWeight(ias, gg) - n.WaveDragPerWeight(vKt, altFt, ias)
                    - brakeK * ias * ias - n.sideslipDragPerG * side - extraDrag;
            };
            excess = excessFn(ktas, AltFt, g);

            state = PointMass.StepBody(state, g, sideG, dt, excessFn);

            // The speed floor, then the roll about the new path
            double speed = state.vel.Length;
            if (speed < MinSpeedFtps) state.vel = state.vel * (MinSpeedFtps / speed);
            Vec3d fwd = state.vel.Normalized;
            Vec3d up = state.up.SquareTo(fwd);
            Vec3d right = Vec3d.Cross(fwd, up);
            double phi = rollRateDps * Math.PI / 180 * dt;
            state.up = (up * Math.Cos(phi) + right * Math.Sin(phi)).SquareTo(fwd);

            alphaShown += (FlownAlphaDeg() - alphaShown) * Math.Min(1, dt * 4);

            // Nose high and nearly out of speed: the wing stops flying the path (a tail slide or a hammerhead comes next)
            if (!Departed && state.vel.Length < n.verticalEntryFtps && fwd.z > Math.Sin(50 * Math.PI / 180))
            {
                vertical = true;
                nose = fwd;
                departBuild = 0;
            }
        }

        /// <summary>
        /// Vertical flight, step by step. No lift: gravity, the engine along the nose and the drag along the path move it.
        /// The nose turns three ways: the controls, as far as the air (the speed) and the propeller's blast over the tail
        /// still work them (full rudder at full power swings it over: the hammerhead, or stall turn); and the relative wind,
        /// which swings it toward the way the aircraft moves (sliding back, the nose falls through: the tail slide). With
        /// the stick neutral it falls forward, onto its belly; held back, over onto its back. Once it is moving along the
        /// nose again and fast enough, it flies normally.
        /// </summary>
        void StepVertical(FlightControls c, double dt)
        {
            var n = numbers;
            double p = Math.Max(-1, Math.Min(1, c.pitch));
            double ail = Math.Max(-1, Math.Min(1, c.roll));
            double ped = Math.Max(-1, Math.Min(1, c.yaw));

            // Engine, rocket, brake: as in normal flight
            double lever = Math.Max(0, Math.Min(1, c.throttle));
            throttle += Math.Max(-dt * 0.8, Math.Min(dt * 0.8, lever - throttle));
            wepOn = c.wep && lever > 0.95 && !systems.WepOut;
            double power = Math.Min(throttle, systems.PowerCap) * (wepOn ? n.wepFactor : 1);
            rocketCoolSec = Math.Max(0, rocketCoolSec - dt);
            if (c.rocket && RocketReady) { rocketBurnSec = n.rocketSec; rocketCoolSec = n.rocketCooldownSec; }
            double boost = 0;
            if (rocketBurnSec > 0) { boost = n.rocketG * Math.Min(1, rocketBurnSec / dt); rocketBurnSec = Math.Max(0, rocketBurnSec - dt); }
            brakeOut = c.brake;

            Vec3d vel = state.vel;
            double v = vel.Length;
            Vec3d up = state.up.SquareTo(nose);
            Vec3d right = Vec3d.Cross(nose, up);

            // What still steers: the airflow from the speed, and the propeller's blast over the tail
            double air = Math.Min(1, v / 200.0);
            double auth = Math.Min(1, 0.1 + n.propWash * 0.9 * throttle + air);
            double rad = Math.PI / 180;
            Vec3d omega = up * (-ped * n.lowSpeedYawDps * auth * rad)          // right pedal swings the nose right
                        + right * (p * n.lowSpeedPitchDps * auth * rad)        // back stick brings it up, over the canopy
                        + nose * (ail * n.lowSpeedRollDps * auth * rad);       // right stick rolls right
            // The relative wind: the nose turns toward the way it moves, faster the faster it moves and the further off it is
            if (v > 3)
            {
                Vec3d dir = vel / v;
                double cosA = Math.Max(-1, Math.Min(1, Vec3d.Dot(nose, dir)));
                Vec3d axis = Vec3d.Cross(nose, dir);
                double s = axis.Length;
                // straight back along the nose: it falls forward (stick neutral or forward) or over onto its back (stick held back)
                axis = s > 0.05 ? axis / s : (p > 0.2 ? right : -right);
                double off = Math.Acos(cosA);
                double q = Math.Min(1, (v / 100.0) * (v / 100.0));   // with the dynamic pressure: barely at all while it hangs
                double rate = n.weathervaneDps * rad * q * Math.Min(1, off / (Math.PI / 4) + 0.15);
                omega = omega + axis * rate;
            }
            double w = omega.Length;
            if (w > 1e-9)
            {
                Vec3d k = omega / w;
                double th = w * dt, ct = Math.Cos(th), st = Math.Sin(th);
                Vec3d Rot(Vec3d x) => x * ct + Vec3d.Cross(k, x) * st + k * (Vec3d.Dot(k, x) * (1 - ct));
                nose = Rot(nose).Normalized;
                up = Rot(up).SquareTo(nose);
            }

            // The path: gravity, the engine along the nose, the drag against the way it moves (no lift)
            double ktas = v * Atmosphere.FtpsToKt;
            double ias = Atmosphere.TasToIasKt(ktas, AltFt);
            double thrust = power * n.ThrustPerWeight(Math.Max(0, Vec3d.Dot(vel, nose)) * Atmosphere.FtpsToKt, AltFt) + boost;
            double drag = systems.DragFactor * n.DragPerWeight(ias, 0) + (c.brake ? n.brakeG * ias * ias / (300.0 * 300.0) : 0);
            Vec3d acc = nose * (thrust * Atmosphere.GFtps2) - (v > 1e-6 ? vel / v : Vec3d.Zero) * (drag * Atmosphere.GFtps2);
            acc.z -= Atmosphere.GFtps2;
            Vec3d v0 = vel;
            vel = vel + acc * dt;
            state.pos = state.pos + (v0 + vel) * (dt / 2);
            state.vel = vel;
            state.up = up;
            excess = thrust - drag;

            g = 0; gRate = 0; gAsked = 1; sideG = 0; stalled = true;
            rollRateDps = ail * n.lowSpeedRollDps * auth;
            alphaShown += (0 - alphaShown) * Math.Min(1, dt * 4);

            // Moving along the nose again, fast enough: the wing flies the path once more
            v = vel.Length;
            if (v > n.verticalExitFtps && Vec3d.Dot(vel / v, nose) > Math.Cos(20 * Math.PI / 180))
            {
                vertical = false;
                state.up = up.SquareTo(vel / v);
                g = Math.Min(1, n.AvailableG(Kias));
            }
        }
    }
}
