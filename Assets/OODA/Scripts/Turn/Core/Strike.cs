using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The town strike: the bombers fly a path the player planned from the coast to a factory in the town. The enemy
    /// doesn't know they are coming until they are seen: by a radar site with a clear line to them over the ground, by
    /// the patrol (Bf 109s flying a loop round the town) if they come within sight, or by the town itself once they are
    /// within townAlertFt of the factory. Then the patrol breaks off its loop to attack, and the fighters waiting on the
    /// runway take off a pair at a time, launchGapSec apart, low and slow off the end of it. A bomber that passes over
    /// the factory drops its bombs there. Feet, x east, y north, z up.
    /// </summary>
    public class Strike
    {
        public Vec3d target;                      // the factory, on the ground
        public double strikeRadiusFt = 650;       // a bomber this near the factory, flat, is over it and drops
        public Vec3d[] radars = new Vec3d[0];     // the antennas
        public double radarRangeFt = 49000;       // about 15 km
        public double patrolSightFt = 13000;      // about 4 km
        public double townAlertFt = 13000;        // about 4 km from the factory: the town sees you whatever you do
        public Vec3d runwayStart, runwayDir;      // the take-off end, and along the runway (flat, unit)
        public double runwayFt = 3900;
        public double launchGapSec = 6;           // between pairs: two turns
        public double launchKias = 170;
        /// <summary>By aircraft: the loop it patrols until the alert, or null.</summary>
        public Route[] patrols = new Route[0];
        /// <summary>The aircraft waiting on the runway, in the order they take off.</summary>
        public int[] scramble = new int[0];

        // state
        public bool alerted;
        public double alertTime;
        public string alertBy;                    // "radar", "patrol" or "town"
        public int launched;
        public bool[] held = new bool[0];         // by aircraft: still on the runway
        public bool[] struck = new bool[0];       // by aircraft: a bomber that has dropped on the factory
        public double[] struckAt = new double[0]; // and when (battle time)
        public double alertDistFt;                // how far the nearest of ours was from the factory when the alert came
        double nextCheck;

        public Strike Copy()
        {
            var c = (Strike)MemberwiseClone();
            c.patrols = new Route[patrols.Length];
            for (int i = 0; i < patrols.Length; i++) c.patrols[i] = patrols[i]?.Copy();
            c.held = (bool[])held.Clone();
            c.struck = (bool[])struck.Clone();
            c.struckAt = (double[])struckAt.Clone();
            return c;
        }

        /// <summary>Make room for n aircraft.</summary>
        public void Size(int n)
        {
            Array.Resize(ref patrols, n);
            Array.Resize(ref held, n);
            Array.Resize(ref struck, n);
            Array.Resize(ref struckAt, n);
        }

        public bool Held(int i) => i < held.Length && held[i];
        /// <summary>Aircraft i is flying its patrol loop (the alert hasn't come yet).</summary>
        public bool OnPatrol(int i) => !alerted && i < patrols.Length && patrols[i] != null;
        public int Struck { get { int k = 0; foreach (bool s in struck) if (s) k++; return k; } }

        /// <summary>Park aircraft i on the runway, slot k of the scramble (pairs side by side, further back for each pair).</summary>
        public void Park(FlightModel m, int k)
        {
            Vec3d right = new Vec3d(runwayDir.y, -runwayDir.x, 0);
            Vec3d at = runwayStart + runwayDir * (60 + 160 * (k / 2)) + right * (k % 2 == 0 ? -45 : 45);
            m.state = PointMass.Make(at + new Vec3d(0, 0, 6), 1, Math.Atan2(runwayDir.y, runwayDir.x));
            m.throttle = 0;
        }

        /// <summary>After each step: the look-out (twice a second), the take-offs, and the bombs.</summary>
        public void Step(Battle b)
        {
            if (!alerted && b.time >= nextCheck)
            {
                nextCheck = b.time + 0.5;
                string by = Spotted(b);
                if (by != null)
                {
                    alerted = true; alertTime = b.time; alertBy = by;
                    alertDistFt = double.PositiveInfinity;
                    for (int i = 0; i < b.Count; i++)
                        if (b.side[i] == 0 && !b.Out(i))
                        {
                            Vec3d d = b.planes[i].state.pos - target;
                            alertDistFt = Math.Min(alertDistFt, Math.Sqrt(d.x * d.x + d.y * d.y));
                        }
                }
            }
            while (alerted && launched < scramble.Length && b.time >= alertTime + launchGapSec * (launched / 2)) Launch(b, scramble[launched++]);
            for (int i = 0; i < b.Count; i++)
            {
                if (b.routes[i] == null || b.side[i] != 0 || b.Out(i) || struck[i]) continue;
                Vec3d d = b.planes[i].state.pos - target;
                if (d.x * d.x + d.y * d.y < strikeRadiusFt * strikeRadiusFt) { struck[i] = true; struckAt[i] = b.time; }
            }
        }

        /// <summary>Who sees the player's side now, or null.</summary>
        string Spotted(Battle b)
        {
            for (int i = 0; i < b.Count; i++)
            {
                if (b.side[i] != 0 || b.Out(i)) continue;
                Vec3d p = b.planes[i].state.pos;
                Vec3d f = p - target;
                if (f.x * f.x + f.y * f.y < townAlertFt * townAlertFt) return "town";
                foreach (var r in radars)
                    if ((p - r).Length < radarRangeFt && Clear(b, r, p)) return "radar";
                for (int j = 0; j < b.Count; j++)
                    if (OnPatrol(j) && !b.Out(j) && (p - b.planes[j].state.pos).Length < patrolSightFt && Clear(b, b.planes[j].state.pos, p)) return "patrol";
            }
            return null;
        }

        /// <summary>A clear line over the ground (only the ground: this runs where the obstacles can't be asked about).</summary>
        public static bool Clear(Battle b, Vec3d a, Vec3d c) => Clear(b.groundFt, a, c);

        public static bool Clear(Func<double, double, double> groundFt, Vec3d a, Vec3d c)
        {
            int n = Math.Max(4, (int)((c - a).Length / 400));
            for (int s = 1; s < n; s++)
            {
                Vec3d p = a + (c - a) * ((double)s / n);
                if (p.z < groundFt(p.x, p.y)) return false;
            }
            return true;
        }

        /// <summary>Off the end of the runway, climbing away: low, slow and full throttle.</summary>
        void Launch(Battle b, int i)
        {
            if (i < 0 || i >= b.Count || !held[i]) return;
            held[i] = false;
            int k = Array.IndexOf(scramble, i);
            Vec3d right = new Vec3d(runwayDir.y, -runwayDir.x, 0);
            Vec3d at = runwayStart + runwayDir * (runwayFt + 300) + right * (k % 2 == 0 ? -80 : 80) - runwayDir * (k % 2 == 0 ? 0 : 150);
            at.z = Math.Max(at.z, b.groundFt(at.x, at.y)) + 220;
            var m = b.planes[i];
            m.state = PointMass.Make(at, Atmosphere.IasToTasKt(launchKias, at.z), Math.Atan2(runwayDir.y, runwayDir.x), 6 * Math.PI / 180);
            m.throttle = 1;
        }
    }
}
