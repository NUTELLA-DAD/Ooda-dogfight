using System;
using System.IO;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>The aircraft a dogfight can be flown in.</summary>
    public enum Craft { Mustang, Spitfire, Bf109, Phantom }

    /// <summary>Who sits in a plane: Usual is the type's own pilot (the tiger in the P-51, the wolf in the Spitfire and the
    /// Bf 109, both in the F-4, which keeps its crew whatever is picked).</summary>
    public enum PilotKind { Usual, Tiger, Wolf, Wizard, Skeleton }

    /// <summary>
    /// How a dogfight is set up before it begins, as the setup screen picks it: the aircraft on each side, one entry
    /// per plane (1 to Scenarios.MaxSide each), how the two sides start (or at random), and how far apart, how high
    /// and how fast. Scenarios.Dogfight(setup, ...) builds the battle from it.
    ///
    /// For a game over the network the host picks it, fixes a random start with Resolved, and sends ToBytes to the
    /// friend; the same bytes build the same battle on both machines. Pure C#, no Unity.
    /// </summary>
    [Serializable]
    public class DogfightSetup
    {
        public enum Range { Close, Medium, Far }
        public enum Height { Low, Medium, High }
        public enum Pace { Slow, Cruise, Fast }

        /// <summary>Your side's aircraft, your lead first.</summary>
        public Craft[] ours = { Craft.Mustang };
        /// <summary>The other side's, their lead first.</summary>
        public Craft[] theirs = { Craft.Bf109 };
        /// <summary>Who flies each of your planes, in the same order as ours (FitPilots keeps the lengths together).</summary>
        public PilotKind[] oursPilots = { PilotKind.Usual };
        /// <summary>Who flies each of theirs.</summary>
        public PilotKind[] theirsPilots = { PilotKind.Usual };
        /// <summary>Pick the start at random when the fight begins (each restart a new one); else start.</summary>
        public bool randomStart = true;
        public Scenarios.Start start = Scenarios.Start.HeadOn;
        public Range range = Range.Medium;
        public Height height = Height.Medium;
        public Pace speed = Pace.Cruise;
        /// <summary>Side 0's guns get the forgiving hit radius and the tighter cone (a game liberty for the player against the AI). Off for side against side.</summary>
        public bool easyAimOurs = true;

        public int Ours => ours.Length;
        public int Theirs => theirs.Length;

        public DogfightSetup Copy()
        {
            var c = (DogfightSetup)MemberwiseClone();
            c.ours = (Craft[])ours.Clone();
            c.theirs = (Craft[])theirs.Clone();
            c.oursPilots = (PilotKind[])(oursPilots ?? new PilotKind[0]).Clone();
            c.theirsPilots = (PilotKind[])(theirsPilots ?? new PilotKind[0]).Clone();
            c.FitPilots();
            return c;
        }

        /// <summary>A side's aircraft at a new size: planes added fly what the last one flies, planes past it go.</summary>
        public static Craft[] Resize(Craft[] side, int count)
        {
            count = Math.Max(1, Math.Min(Scenarios.MaxSide, count));
            var c = new Craft[count];
            for (int k = 0; k < count; k++) c[k] = side.Length == 0 ? Craft.Mustang : side[Math.Min(k, side.Length - 1)];
            return c;
        }

        /// <summary>The pilot lists the same length as the aircraft lists: planes added get the usual pilot, planes past the end go.</summary>
        public void FitPilots()
        {
            oursPilots = FitPilots(oursPilots, ours.Length);
            theirsPilots = FitPilots(theirsPilots, theirs.Length);
        }

        static PilotKind[] FitPilots(PilotKind[] side, int count)
        {
            if (side != null && side.Length == count) return side;
            var p = new PilotKind[count];
            for (int k = 0; k < count && side != null && k < side.Length; k++) p[k] = side[k];
            return p;
        }

        /// <summary>Who flies plane k of a side (ours or theirs): Usual when not picked.</summary>
        public PilotKind PilotOf(bool ourSide, int k)
        {
            var side = ourSide ? oursPilots : theirsPilots;
            return side != null && k >= 0 && k < side.Length ? side[k] : PilotKind.Usual;
        }

        public static string Name(PilotKind p) => p == PilotKind.Tiger ? "Tiger" : p == PilotKind.Wolf ? "Wolf" : p == PilotKind.Wizard ? "Wizard"
                                                 : p == PilotKind.Skeleton ? "Skeleton" : "Usual";

        /// <summary>A copy with a random start fixed (so every machine flies the same one); the others are copied unchanged.</summary>
        public DogfightSetup Resolved(Random dice)
        {
            var c = Copy();
            if (c.randomStart) { c.start = (Scenarios.Start)dice.Next(Scenarios.StartCount); c.randomStart = false; }
            return c;
        }

        /// <summary>How much further apart (or closer) than the usual starts the two sides begin.</summary>
        public double RangeScale => range == Range.Close ? 0.6 : range == Range.Far ? 1.7 : 1;
        /// <summary>Added to the fight's usual height, ft: low puts you about 400 m over the basin floor, high up among the peaks.</summary>
        public double HeightOffsetFt => height == Height.Low ? -1100 : height == Height.High ? 4400 : 0;
        /// <summary>The starting speed, KIAS.</summary>
        public double Kias => speed == Pace.Slow ? 220 : speed == Pace.Fast ? 380 : 300;

        public static string Name(Range r) => r == Range.Close ? "Close" : r == Range.Far ? "Far" : "Medium";
        public static string Name(Height h) => h == Height.Low ? "Low" : h == Height.High ? "High" : "Medium";
        public static string Name(Pace p) => p == Pace.Slow ? "220 kt" : p == Pace.Fast ? "380 kt" : "300 kt";

        // ---------- the aircraft ----------

        public static string Name(Craft c) => c == Craft.Spitfire ? "Spitfire IX" : c == Craft.Bf109 ? "Bf 109 G-6" : c == Craft.Phantom ? "F-4 Phantom" : "P-51D Mustang";
        public static string Short(Craft c) => c == Craft.Spitfire ? "Spitfire" : c == Craft.Bf109 ? "Bf 109" : c == Craft.Phantom ? "F-4" : "P-51";

        public static AircraftNumbers Numbers(Craft c) => c == Craft.Spitfire ? AircraftNumbers.SpitfireIX() : c == Craft.Bf109 ? AircraftNumbers.Bf109G()
                                                         : c == Craft.Phantom ? AircraftNumbers.Phantom() : AircraftNumbers.P51D();

        /// <summary>Its guns. The P-51's own numbers already carry the forgiving aim; with easy set, any type gets it.</summary>
        public static CombatNumbers Guns(Craft c, bool easy)
        {
            var g = c == Craft.Spitfire ? CombatNumbers.SpitfireIX() : c == Craft.Bf109 ? CombatNumbers.Bf109G()
                  : c == Craft.Phantom ? CombatNumbers.Phantom() : CombatNumbers.P51D();
            if (easy) { g.hitRadiusFt = Math.Max(g.hitRadiusFt, 20); g.spreadMil = Math.Min(g.spreadMil, 2.5); }
            else if (c == Craft.Mustang) { var d = new CombatNumbers(); g.hitRadiusFt = d.hitRadiusFt; g.spreadMil = d.spreadMil; }
            return g;
        }

        /// <summary>Which aircraft a set of numbers is (by its name), for the views.</summary>
        public static Craft Of(AircraftNumbers n)
        {
            string s = n?.name ?? "";
            return s.StartsWith("Spitfire") ? Craft.Spitfire : s.StartsWith("Bf 109") ? Craft.Bf109 : s.StartsWith("F-4") ? Craft.Phantom : Craft.Mustang;
        }

        /// <summary>"2 P-51s", "P-51 + Spitfire", "3 F-4s": a side in a few words.</summary>
        public static string Describe(Craft[] side)
        {
            bool same = true;
            for (int k = 1; k < side.Length; k++) if (side[k] != side[0]) same = false;
            if (same) return side.Length == 1 ? Short(side[0]) : $"{side.Length} {Short(side[0])}s";
            var parts = new string[side.Length];
            for (int k = 0; k < side.Length; k++) parts[k] = Short(side[k]);
            return string.Join(" + ", parts);
        }

        // ---------- over the wire ----------

        // 2 added the pilots; a version 1 setup (kept from an older build) still reads, everyone with the usual pilot
        const byte Version = 2;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Version);
                w.Write((byte)ours.Length); foreach (var c in ours) w.Write((byte)c);
                w.Write((byte)theirs.Length); foreach (var c in theirs) w.Write((byte)c);
                w.Write(randomStart); w.Write((byte)start); w.Write((byte)range); w.Write((byte)height); w.Write((byte)speed); w.Write(easyAimOurs);
                for (int k = 0; k < ours.Length; k++) w.Write((byte)PilotOf(true, k));
                for (int k = 0; k < theirs.Length; k++) w.Write((byte)PilotOf(false, k));
                w.Flush();
                return ms.ToArray();
            }
        }

        public static DogfightSetup FromBytes(byte[] bytes)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                byte version = r.ReadByte();
                if (version != Version && version != 1) throw new InvalidDataException("A dogfight setup from another version of the game.");
                var s = new DogfightSetup();
                s.ours = new Craft[r.ReadByte()]; for (int k = 0; k < s.ours.Length; k++) s.ours[k] = (Craft)r.ReadByte();
                s.theirs = new Craft[r.ReadByte()]; for (int k = 0; k < s.theirs.Length; k++) s.theirs[k] = (Craft)r.ReadByte();
                s.randomStart = r.ReadBoolean(); s.start = (Scenarios.Start)r.ReadByte(); s.range = (Range)r.ReadByte();
                s.height = (Height)r.ReadByte(); s.speed = (Pace)r.ReadByte(); s.easyAimOurs = r.ReadBoolean();
                if (s.ours.Length < 1 || s.ours.Length > Scenarios.MaxSide || s.theirs.Length < 1 || s.theirs.Length > Scenarios.MaxSide)
                    throw new InvalidDataException("A dogfight setup with too many or too few planes.");
                s.oursPilots = new PilotKind[s.ours.Length];
                s.theirsPilots = new PilotKind[s.theirs.Length];
                if (version >= 2)
                {
                    for (int k = 0; k < s.ours.Length; k++) s.oursPilots[k] = (PilotKind)r.ReadByte();
                    for (int k = 0; k < s.theirs.Length; k++) s.theirsPilots[k] = (PilotKind)r.ReadByte();
                }
                return s;
            }
        }
    }
}
