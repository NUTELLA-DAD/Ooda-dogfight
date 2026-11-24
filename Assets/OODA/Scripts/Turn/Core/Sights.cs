using System;
using System.Collections.Generic;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The island's sights: the things to fly under, through and round that are not ground. The Harbour Bridge across the
    /// mouth of the west bay (fly under its deck, 60 m over the water, between its towers); the Rock Arch among the sea
    /// stacks off the west coast (an opening 170 m wide and 95 m high); the fleet at anchor south of the island (a carrier,
    /// two cruisers and four destroyers); and the barrage balloons round Skyline City on their cables.
    ///
    /// All of it is laid out here as plain boxes, wires and balloons from fixed numbers, the same on every machine, and it
    /// stands still: the battle crashes aircraft on it and stops rounds on it through the colliders Fx/IslandSights makes
    /// from these shapes (the Obstacles layer), and the AI pilots steer round the tall parts (AiWires). Metres, x east, y
    /// up, z north (Unity's frame).
    /// </summary>
    public static class Sights
    {
        /// <summary>The paints, for the drawing.</summary>
        public const byte Hull = 0, Deck = 1, Upper = 2, Dark = 3, BridgeRed = 4, Concrete = 5, Rock = 6, RockTop = 7, White = 8, Silver = 9;

        /// <summary>A solid box: its middle, its half sizes along its own right, up and forward, and its turn about the
        /// vertical (degrees clockwise seen from above: 0 looks north).</summary>
        public struct Box
        {
            public string name;
            public double x, y, z, hx, hy, hz, yawDeg;
            public byte paint;
        }

        /// <summary>A cable from a to b. Solid ones crash aircraft (a box thickM square); the others are only drawn.</summary>
        public struct Wire
        {
            public string name;
            public double ax, ay, az, bx, by, bz, thickM;
            public bool solid, tether;
        }

        /// <summary>A barrage balloon: its middle, and the ground under its winch.</summary>
        public struct Balloon { public double x, y, z, groundM; }

        public const double BalloonRadiusM = 12;

        // ---------- the Harbour Bridge ----------

        public const double BridgeX = -9050, BridgeZ = -2610, BridgeHalfLengthM = 510, BridgeTowerAtM = 330;
        public const double BridgeDeckM = 62, BridgeDeckHalfDepthM = 2.5, BridgeDeckHalfWidthM = 11, BridgeTowerTopM = 135;
        /// <summary>The clear height under the deck over the water.</summary>
        public const double BridgeClearM = BridgeDeckM - BridgeDeckHalfDepthM;

        // ---------- the Rock Arch ----------

        public const double ArchX = -12650, ArchZ = 1650, ArchPierAtM = 125, ArchPierHalfM = 38, ArchLintelBottomM = 96, ArchTopM = 128;
        /// <summary>The opening: its width between the piers and its height over the water.</summary>
        public const double ArchOpenWidthM = 2 * (ArchPierAtM - ArchPierHalfM), ArchOpenHeightM = ArchLintelBottomM;

        // ---------- the fleet ----------

        public const double FleetX = 3000, FleetZ = -12900, FleetYawDeg = 78;

        public static readonly Box[] Boxes;
        public static readonly Wire[] Wires;
        public static readonly Balloon[] Balloons;
        /// <summary>Each ship: its name, its middle and its length (for the checks and the map).</summary>
        public static readonly (string name, double x, double z, double lengthM)[] Ships;

        static Sights()
        {
            var boxes = new List<Box>();
            var wires = new List<Wire>();
            var ships = new List<(string, double, double, double)>();
            MakeBridge(boxes, wires);
            MakeArch(boxes);
            MakeFleet(boxes, ships);
            var balloons = MakeBalloons(wires);
            Boxes = boxes.ToArray(); Wires = wires.ToArray(); Balloons = balloons; Ships = ships.ToArray();
        }

        /// <summary>A frame to lay boxes out in: an origin on the map and a heading; lx is to the right, lz ahead.</summary>
        struct Frame
        {
            public double x, z, yawDeg;
            public (double x, double z) At(double lx, double lz)
            {
                double a = yawDeg * Math.PI / 180, s = Math.Sin(a), c = Math.Cos(a);
                return (x + c * lx + s * lz, z - s * lx + c * lz);
            }
            public Box Box(string name, double lx, double lz, double y0, double y1, double hx, double hz, byte paint, double turn = 0)
            {
                var p = At(lx, lz);
                return new Box { name = name, x = p.x, z = p.z, y = (y0 + y1) / 2, hy = (y1 - y0) / 2, hx = hx, hz = hz, yawDeg = yawDeg + turn, paint = paint };
            }
        }

        static void MakeBridge(List<Box> b, List<Wire> w)
        {
            var f = new Frame { x = BridgeX, z = BridgeZ, yawDeg = 0 };
            double L = BridgeHalfLengthM, T = BridgeTowerAtM;
            b.Add(f.Box("Harbour Bridge deck", 0, 0, BridgeDeckM - BridgeDeckHalfDepthM, BridgeDeckM + BridgeDeckHalfDepthM, BridgeDeckHalfWidthM, L, BridgeRed));
            b.Add(f.Box("Harbour Bridge roadway", 0, 0, BridgeDeckM + BridgeDeckHalfDepthM, BridgeDeckM + BridgeDeckHalfDepthM + 0.3, BridgeDeckHalfWidthM - 1.5, L, Dark));
            foreach (int s in new[] { -1, 1 })
            {
                foreach (int side in new[] { -1, 1 })
                    b.Add(f.Box("Harbour Bridge tower", side * 13.5, s * T, -30, BridgeTowerTopM, 3.5, 4.5, BridgeRed));
                foreach (double y in new[] { 30.0, BridgeDeckM - 8, 92, 116, BridgeTowerTopM - 3 })
                    b.Add(f.Box("Harbour Bridge tower beam", 0, s * T, y - 3, y + 3, 13.5, 3.2, BridgeRed));
                b.Add(f.Box("Harbour Bridge tower foot", 0, s * T, -30, 6, 22, 9, Concrete));
                // the approach's piers, and the anchorage the cables come down to
                foreach (double at in new[] { 395.0, 450 })
                    b.Add(f.Box("Harbour Bridge pier", 0, s * at, -30, BridgeDeckM - BridgeDeckHalfDepthM, 7, 2.6, Concrete));
                b.Add(f.Box("Harbour Bridge anchorage", 0, s * (L - 14), 0, BridgeDeckM + 8, 15, 14, Concrete));
                // the main cables: tower top down to the deck at the middle of the span, and down to the anchorage outside
                foreach (int side in new[] { -1, 1 })
                {
                    double lx = side * 12.5, sag0 = BridgeTowerTopM - 1, low = BridgeDeckM + 7;
                    const int N = 7;
                    for (int k = 0; k < N; k++)
                    {
                        double u0 = k / (double)N, u1 = (k + 1) / (double)N;        // 0 at the tower, 1 at the middle
                        double y0 = low + (sag0 - low) * (1 - u0) * (1 - u0), y1 = low + (sag0 - low) * (1 - u1) * (1 - u1);
                        var p0 = f.At(lx, s * T * (1 - u0)); var p1 = f.At(lx, s * T * (1 - u1));
                        w.Add(new Wire { name = "Harbour Bridge cable", ax = p0.x, ay = y0, az = p0.z, bx = p1.x, by = y1, bz = p1.z, thickM = 1.6, solid = true });
                    }
                    var t0 = f.At(lx, s * T); var t1 = f.At(lx, s * (L - 14));
                    w.Add(new Wire { name = "Harbour Bridge cable", ax = t0.x, ay = sag0, az = t0.z, bx = t1.x, by = BridgeDeckM + 8, bz = t1.z, thickM = 1.6, solid = true });
                    // the hangers: drawn, not solid (the cable and the deck they hang between are)
                    for (double at = 50; at < T - 20; at += 50)
                    {
                        double u = 1 - at / T, y = low + (sag0 - low) * (1 - u) * (1 - u);
                        var h = f.At(lx, s * at);
                        w.Add(new Wire { name = "Harbour Bridge hanger", ax = h.x, ay = BridgeDeckM, az = h.z, bx = h.x, by = y, bz = h.z, thickM = 0.5 });
                    }
                }
            }
        }

        static void MakeArch(List<Box> b)
        {
            var f = new Frame { x = ArchX, z = ArchZ, yawDeg = 0 };
            b.Add(f.Box("Rock Arch pier", -ArchPierAtM, 0, -20, ArchLintelBottomM + 12, ArchPierHalfM, 32, Rock, 9));
            b.Add(f.Box("Rock Arch pier", ArchPierAtM, 0, -20, ArchLintelBottomM + 12, ArchPierHalfM, 30, Rock, -7));
            b.Add(f.Box("Rock Arch foot", -ArchPierAtM - 22, 6, -20, 34, 46, 44, Rock, -14));
            b.Add(f.Box("Rock Arch foot", ArchPierAtM + 26, -8, -20, 42, 44, 40, Rock, 17));
            b.Add(f.Box("Rock Arch shoulder", -ArchPierAtM - 30, -4, 34, 74, 26, 30, Rock, 24));
            b.Add(f.Box("Rock Arch lintel", 0, 0, ArchLintelBottomM, ArchTopM - 4, ArchPierAtM + ArchPierHalfM + 8, 27, Rock, 2));
            b.Add(f.Box("Rock Arch top", 6, 0, ArchTopM - 4, ArchTopM, ArchPierAtM + ArchPierHalfM - 6, 22, RockTop, 2));
            b.Add(f.Box("Rock Arch crag", ArchPierAtM + 6, 2, ArchTopM - 4, ArchTopM + 16, 24, 18, Rock, 31));
        }

        static void MakeFleet(List<Box> b, List<(string, double, double, double)> ships)
        {
            var fleet = new Frame { x = FleetX, z = FleetZ, yawDeg = FleetYawDeg };
            void Place(string name, double lx, double lz, double length, Action<Frame, string> make)
            {
                var p = fleet.At(lx, lz);
                make(new Frame { x = p.x, z = p.z, yawDeg = FleetYawDeg }, name);
                ships.Add((name, p.x, p.z, length));
            }
            Place("the carrier", 0, 0, 290, Carrier);
            Place("the lead cruiser", -260, 420, 200, Cruiser);
            Place("the rear cruiser", 260, -420, 200, Cruiser);
            Place("a destroyer", -520, -150, 126, Destroyer);
            Place("a destroyer", 520, 150, 126, Destroyer);
            Place("a destroyer", 150, 760, 126, Destroyer);
            Place("a destroyer", -150, -760, 126, Destroyer);

            void Carrier(Frame f, string n)
            {
                b.Add(f.Box(n, 0, -6, -5, 15, 15, 112, Hull));
                b.Add(f.Box(n, 0, 116, -5, 15, 10, 12, Hull));
                b.Add(f.Box(n, 0, 134, -5, 15, 5, 7, Hull));
                b.Add(f.Box(n + "'s flight deck", 0, 0, 15, 17.5, 20, 138, Deck));
                b.Add(f.Box(n + "'s flight deck", -24, -30, 15, 17.5, 5, 60, Deck));            // the angled deck's overhang
                b.Add(f.Box(n + "'s centre line", 0, 0, 17.5, 17.62, 0.6, 128, White));
                foreach (double at in new[] { -128.0, 128 }) b.Add(f.Box(n + "'s deck bar", 0, at, 17.5, 17.62, 16, 0.8, White));
                b.Add(f.Box(n + "'s island", 15.5, 24, 17.5, 33, 4.5, 21, Upper));
                b.Add(f.Box(n + "'s bridge", 15.5, 32, 33, 39, 3.8, 8, Upper));
                b.Add(f.Box(n + "'s funnel", 15.5, 12, 33, 43, 3.2, 6.5, Dark));
                b.Add(f.Box(n + "'s mast", 15.5, 34, 39, 56, 0.7, 0.7, Dark));
                // aircraft parked along the deck's edge, wings folded: little boxes
                for (int k = 0; k < 6; k++)
                {
                    b.Add(f.Box(n + "'s aircraft", -13, -112 + k * 15, 17.5, 20, 1.1, 5, Upper, 28));
                    b.Add(f.Box(n + "'s aircraft", -13, -111 + k * 15, 18.6, 19.1, 5.5, 1.2, Upper, 28));
                }
            }
            void Cruiser(Frame f, string n)
            {
                b.Add(f.Box(n, 0, -8, -4, 7, 10, 78, Hull));
                b.Add(f.Box(n, 0, 78, -4, 7.6, 6.4, 9, Hull));
                b.Add(f.Box(n, 0, 93, -4, 8.2, 3, 6.5, Hull));
                b.Add(f.Box(n + "'s deck", 0, -8, 7, 7.3, 9.2, 77, Deck));
                b.Add(f.Box(n + "'s upper works", 0, 4, 7.3, 18, 7, 27, Upper));
                b.Add(f.Box(n + "'s bridge", 0, 20, 18, 30, 4.6, 7, Upper));
                b.Add(f.Box(n + "'s mast", 0, 18, 30, 46, 0.6, 0.6, Dark));
                foreach (double at in new[] { 2.0, -13 }) b.Add(f.Box(n + "'s funnel", 0, at, 18, 28, 2.8, 4.4, Dark));
                foreach (var (at, up) in new[] { (46.0, 0.0), (60, 3.2), (-44, 3.2), (-58, 0) })
                {
                    b.Add(f.Box(n + "'s turret", 0, at, 7.3 + up, 11.2 + up, 5.4, 6, Upper));
                    b.Add(f.Box(n + "'s guns", 0, at + Math.Sign(at) * 11, 9 + up, 9.8 + up, 3.2, 6, Dark));
                }
            }
            void Destroyer(Frame f, string n)
            {
                b.Add(f.Box(n, 0, -5, -3, 5, 5.6, 48, Hull));
                b.Add(f.Box(n, 0, 49, -3, 5.5, 3.4, 6.5, Hull));
                b.Add(f.Box(n, 0, 59, -3, 6, 1.5, 4, Hull));
                b.Add(f.Box(n + "'s upper works", 0, 6, 5, 11, 4, 14, Upper));
                b.Add(f.Box(n + "'s bridge", 0, 15, 11, 16.5, 3.2, 4, Upper));
                b.Add(f.Box(n + "'s mast", 0, 14, 16.5, 28, 0.45, 0.45, Dark));
                foreach (double at in new[] { 2.0, -9 }) b.Add(f.Box(n + "'s funnel", 0, at, 11, 17, 1.8, 2.8, Dark));
                foreach (double at in new[] { 31.0, -30, -41 })
                {
                    b.Add(f.Box(n + "'s turret", 0, at, 5, 7.6, 2.8, 3.2, Upper));
                    b.Add(f.Box(n + "'s guns", 0, at + Math.Sign(at) * 6, 6.1, 6.6, 1.3, 3.2, Dark));
                }
            }
        }

        /// <summary>The barrage balloons round Skyline City: outside its edge on every side but the river's two ends (the
        /// arena's start corridor), the airport's approach and the road in from Crossroads; 450 to 640 m over the sea.</summary>
        static Balloon[] MakeBalloons(List<Wire> w)
        {
            var at = new (double x, double z, double y)[]
            {
                (11400, 600, 520), (11350, 2800, 610), (11400, 5200, 480), (11500, 7300, 560),
                (19600, -600, 590), (19650, 1900, 470), (19600, 6700, 540),
                (13400, -1750, 500), (17600, -1750, 630),
                (13400, 8050, 600), (17600, 8050, 450),
            };
            var made = new List<Balloon>();
            foreach (var p in at)
            {
                double g = Eastland.Raw(p.x, p.z);
                if (g < 2) continue;   // (in the sea: no winch to hold it)
                made.Add(new Balloon { x = p.x, y = p.y, z = p.z, groundM = g });
                w.Add(new Wire { name = "City balloon cable", ax = p.x, ay = g, az = p.z, bx = p.x, by = p.y - 5, bz = p.z, thickM = 4, solid = true, tether = true });
            }
            return made.ToArray();
        }

        /// <summary>The tall parts as wires with a radius (metres), for the AI pilots' planning: the bridge's deck and
        /// towers, the arch's piers and lintel, the balloons and their cables. The ships are too low to matter to them.</summary>
        public static IEnumerable<(double ax, double ay, double az, double bx, double by, double bz, double radiusM)> AiWires()
        {
            yield return (BridgeX, BridgeDeckM, BridgeZ - BridgeHalfLengthM, BridgeX, BridgeDeckM, BridgeZ + BridgeHalfLengthM, BridgeDeckHalfWidthM + 12);
            foreach (int s in new[] { -1, 1 })
                yield return (BridgeX, 0, BridgeZ + s * BridgeTowerAtM, BridgeX, BridgeTowerTopM, BridgeZ + s * BridgeTowerAtM, 30);
            foreach (int s in new[] { -1, 1 })
                yield return (ArchX + s * ArchPierAtM, 0, ArchZ, ArchX + s * ArchPierAtM, ArchTopM, ArchZ, ArchPierHalfM + 30);
            yield return (ArchX - ArchPierAtM, ArchTopM - 16, ArchZ, ArchX + ArchPierAtM, ArchTopM - 16, ArchZ, 42);
            foreach (var b in Balloons)
            {
                yield return (b.x, b.groundM, b.z, b.x, b.y, b.z, 9);
                yield return (b.x, b.y, b.z, b.x, b.y, b.z, BalloonRadiusM + 8);
            }
        }
    }
}
