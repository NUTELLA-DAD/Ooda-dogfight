using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// What a pilot can choose to bring to a dogfight. New ones go on the end: the numbers go over the wire.
    /// To add one: add it here, give it its words in Loadout.Name, Short and Blurb, say in Allowed
    /// which aircraft can take it, and in Loadout.Apply what it puts on the plane.
    /// </summary>
    public enum Kit { Missiles, Rocket, Mines, Blade, Stealth, Jammer, Radar, Ice, Well, Grapple }

    /// <summary>
    /// A pilot's loadout: the two capabilities he brings to the fight (with Arcade on in the dogfight setup). Everyone
    /// always carries his guns, his flares (and a jet its chaff and radar) and the speed brake; the picks add to that:
    /// heat-seekers, radar missiles, the rocket, ice, or one of the arcade specials (see Specials). Two different picks;
    /// the jets (no rocket: their afterburner does that job) can't take the rocket, and only a jet (with a radar) can take
    /// radar missiles. Ice cases the plane in ice for the whole fight: no heat-seeker can lock it (CombatNumbers.iced).
    /// Pure C#, no Unity.
    /// </summary>
    [Serializable]
    public struct Loadout : IEquatable<Loadout>
    {
        public Kit a, b;

        public Loadout(Kit a, Kit b) { this.a = a; this.b = b; }

        /// <summary>Every capability, in the order the briefing shows them.</summary>
        public static readonly Kit[] All = (Kit[])Enum.GetValues(typeof(Kit));

        /// <summary>What a plane brings unless its pilot picks otherwise: heat-seekers and mines, a jet both its missiles.</summary>
        public static Loadout Default => new Loadout(Kit.Missiles, Kit.Mines);
        public static Loadout DefaultFor(Craft c) => Allowed(Kit.Radar, c) ? new Loadout(Kit.Missiles, Kit.Radar) : Default;

        public bool Has(Kit k) => a == k || b == k;

        /// <summary>Can this aircraft carry it? The jets have no rocket.</summary>
        public static bool Allowed(Kit k, Craft c) => k == Kit.Rocket ? DogfightSetup.Numbers(c).rocketG > 0
                                                    : k == Kit.Radar ? DogfightSetup.Guns(c, false).radarRangeFt > 0
                                                    : true;

        /// <summary>
        /// Pick k: if it is already one of the two, nothing changes (Drop takes one off); otherwise it takes the place of the
        /// older pick (a), so the newest two stay. Something the aircraft can't carry is refused.
        /// </summary>
        public Loadout Pick(Kit k, Craft c)
        {
            if (Has(k) || !Allowed(k, c)) return this;
            return new Loadout(b, k);
        }

        /// <summary>The same two, made legal for an aircraft: two different picks it can carry (a refused one is swapped for the first that fits).</summary>
        public Loadout FitTo(Craft c)
        {
            Kit x = Allowed(a, c) && Known(a) ? a : Spare(c, b, b);
            Kit y = Allowed(b, c) && Known(b) && b != x ? b : Spare(c, x, x);
            return new Loadout(x, y);
        }

        static bool Known(Kit k) => (int)k >= 0 && (int)k < All.Length;

        static Kit Spare(Craft c, Kit not1, Kit not2)
        {
            foreach (var k in All) if (k != not1 && k != not2 && Allowed(k, c)) return k;
            return Kit.Missiles;
        }

        /// <summary>
        /// Put a loadout on a plane's numbers (copies changed in place): no heat-seekers unless picked, the arcade specials
        /// only as picked, and no rocket unless picked (its rocketG goes to 0, so it is never ready).
        /// </summary>
        public void Apply(AircraftNumbers plane, CombatNumbers guns)
        {
            if (!Has(Kit.Missiles)) guns.missiles = 0;
            if (!Has(Kit.Radar)) guns.radarMissiles = 0;   // the radar itself (and the chaff) stays: it still sees
            guns.iced = Has(Kit.Ice);
            if (!Has(Kit.Rocket)) plane.rocketG = 0;
            guns.balloonMines = Has(Kit.Mines) ? Specials.Mines : 0;
            guns.heatBlades = Has(Kit.Blade) ? Specials.Blades : 0;
            guns.stealths = Has(Kit.Stealth) ? Specials.Stealths : 0;
            guns.jammers = Has(Kit.Jammer) ? Specials.Jammers : 0;
            guns.wells = Has(Kit.Well) ? Specials.Wells : 0;
            guns.grapples = Has(Kit.Grapple) ? Specials.Grapples : 0;
        }

        public static string Name(Kit k) => k == Kit.Missiles ? "Heat-seekers" : k == Kit.Rocket ? "Rocket" : k == Kit.Mines ? "Balloon mines"
                                          : k == Kit.Blade ? "Heat blade" : k == Kit.Stealth ? "Stealth" : k == Kit.Jammer ? "Jammer"
                                          : k == Kit.Radar ? "Radar missiles" : k == Kit.Ice ? "Ice" : k == Kit.Well ? "Gravity well" : k == Kit.Grapple ? "Grapple" : k.ToString();

        /// <summary>A few letters for a name plate.</summary>
        public static string Short(Kit k) => k == Kit.Missiles ? "MSL" : k == Kit.Rocket ? "RKT" : k == Kit.Mines ? "MINE"
                                           : k == Kit.Blade ? "BLADE" : k == Kit.Stealth ? "STLTH" : k == Kit.Jammer ? "JAM"
                                           : k == Kit.Radar ? "RDR" : k == Kit.Ice ? "ICE" : k == Kit.Well ? "WELL" : k == Kit.Grapple ? "HOOK" : k.ToString().ToUpperInvariant();

        /// <summary>What it does, in a line, for an aircraft (how many it carries).</summary>
        public static string Blurb(Kit k, Craft c)
        {
            switch (k)
            {
                case Kit.Missiles: return $"{DogfightSetup.Guns(c, false).missiles} IR missiles: lock from behind, fire, and make them flare.";
                case Kit.Rocket: return Allowed(k, c) ? "A 2.5 s shove of 60 kt to run or catch up; ten turns to recharge." : "Jets can't carry it: the afterburner does that job.";
                case Kit.Mines: return $"{Specials.Mines} mines dropped behind you that hang in the air for anyone on your tail.";
                case Kit.Blade: return $"{Specials.Blades} charge: glow red hot for a turn and cut through anyone you pass close.";
                case Kit.Stealth: return $"{Specials.Stealths} charge: unseen for two turns (heat-seekers still lock you).";
                case Kit.Jammer: return $"{Specials.Jammers} charge: scramble what enemies near you see for two turns.";
                case Kit.Radar: return Allowed(k, c) ? $"{DogfightSetup.Guns(c, false).radarMissiles} radar missiles: long reach, head-on too; keep them in your radar cone."
                                                     : "Jets only: a warbird has no radar.";
                case Kit.Ice: return "Cased in ice all fight: heat-seekers can't lock you. Radar missiles and guns still can.";
                case Kit.Well: return $"{Specials.Wells} wells thrown ahead: they pull planes in to turn tighter, and bend missiles and rounds off course.";
                case Kit.Grapple: return $"{Specials.Grapples} hooks on a line: swing round an enemy to turn tighter and drag him; pull the other way and it yanks you both, hard.";
                default: return "";
            }
        }

        public string Describe() => $"{Short(a)} + {Short(b)}";

        public bool Equals(Loadout o) => a == o.a && b == o.b;
        public override bool Equals(object o) => o is Loadout l && Equals(l);
        public override int GetHashCode() => (int)a * 31 + (int)b;
        public override string ToString() => $"{Name(a)} + {Name(b)}";
    }
}
