using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// Where on the island a dogfight begins. Each arena is a stretch of open air the world is built to leave clear:
    /// the start line runs along its axis, so every start (head-on, side by side, the perch, at any range) puts both
    /// flights over open ground at any of the three heights. Only the start is fixed: the whole island is open once the
    /// fight begins. The world builder (AlpineWorld) carves and guards the ground round each one from this table, so it
    /// is the one place the arenas are set. Pure C#, no Unity.
    /// </summary>
    public struct Arena
    {
        public string name, blurb;
        /// <summary>The middle of the start line, metres (x east, z north on the map).</summary>
        public double x, z;
        /// <summary>The usual (Medium) height of the start, ft above the sea; Low and High shift it (DogfightSetup.HeightOffsetFt).</summary>
        public double altFt;
        /// <summary>Which way your flight faces in a head-on start, degrees clockwise from north. Every start is turned to it.</summary>
        public double axisDeg;
        /// <summary>How far along the axis either side of the middle, and across it, the ground is kept clear, metres.</summary>
        public double halfLengthM, halfWidthM;

        /// <summary>The middle of the start line in the flight core's frame (ft; x east, y north).</summary>
        public Vec3d CentreFt => new Vec3d(x * Atmosphere.FtPerM, z * Atmosphere.FtPerM, 0);
    }

    public static class Arenas
    {
        /// <summary>
        /// The four: among the snowy peaks in the north, over the high rail pass, over the chalet lake and its village in
        /// the middle, and over the farmland and the strike town in the south.
        /// </summary>
        public static readonly Arena[] All =
        {
            new Arena { name = "Peak Maze", blurb = "A bowl among the snowy peaks, canyons leading out of it. The rock towers over you.",
                        x = 4200, z = 5600, altFt = 5900, axisDeg = 35, halfLengthM = 3000, halfWidthM = 900 },
            new Arena { name = "High Pass", blurb = "The saddle the railway climbs to, cliffs and tunnels either side.",
                        x = -4500, z = 4200, altFt = 5200, axisDeg = 20, halfLengthM = 3000, halfWidthM = 800 },
            new Arena { name = "Chalet Lake", blurb = "Up the lake past the village and its church, wooded walls either side.",
                        x = 0, z = -1700, altFt = 3000, axisDeg = 0, halfLengthM = 3000, halfWidthM = 800 },
            new Arena { name = "Town & Farmland", blurb = "Open fields, the factory town and the coast. The most room to turn.",
                        x = 1400, z = -5900, altFt = 2600, axisDeg = 90, halfLengthM = 3200, halfWidthM = 1400 },
        };

        public static int Count => All.Length;

        /// <summary>The arena by its number, the first one if it is out of range (an older setup, or a newer one's).</summary>
        public static Arena Get(int i) => All[i >= 0 && i < All.Length ? i : 0];
    }
}
