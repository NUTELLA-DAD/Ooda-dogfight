using System;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// Who flies which aircraft in a game over the network, and the one order of the aircraft every machine agrees on.
    ///
    /// Each machine keeps its battle in its own order, with the plane its player is flying at index 0 (the planning
    /// screen, the ghost and the camera all work on aircraft 0, and Tab moves another plane there with Battle.Swap).
    /// On the wire an aircraft is named by its wire id instead: its index when the fight began, on the host. A turn is
    /// always flown in wire order (ResolveInWireOrder), because the order the aircraft are stepped in decides the order
    /// rounds are fired and so the gun dispersion's dice: flown in two different orders, two machines would part ways.
    ///
    /// Pure C#, no Unity.
    /// </summary>
    public class Seating
    {
        /// <summary>A seat no player sits in: its AI pilot flies it (on the host).</summary>
        public const int Ai = -1;

        /// <summary>By local index: the aircraft's wire id.</summary>
        public int[] wireOf;
        /// <summary>By wire id: the player who flies it (0 the host, 1 the first to join, ...), or Ai.</summary>
        public int[] seat;
        /// <summary>This machine's player.</summary>
        public int me;

        public int Count => wireOf.Length;

        /// <summary>A fight of count aircraft as it begins: local order is wire order, every aircraft on its AI pilot.</summary>
        public Seating(int count, int me)
        {
            this.me = me;
            wireOf = new int[count];
            seat = new int[count];
            for (int i = 0; i < count; i++) { wireOf[i] = i; seat[i] = Ai; }
        }

        public Seating Copy()
        {
            var c = (Seating)MemberwiseClone();
            c.wireOf = (int[])wireOf.Clone();
            c.seat = (int[])seat.Clone();
            return c;
        }

        /// <summary>The local index of the aircraft with this wire id.</summary>
        public int LocalOf(int wire) => Array.IndexOf(wireOf, wire);

        /// <summary>The player flying the aircraft at this local index, or Ai.</summary>
        public int SeatOf(int local) => seat[wireOf[local]];

        /// <summary>This machine's player flies the aircraft at this local index.</summary>
        public bool Mine(int local) => SeatOf(local) == me;

        /// <summary>Call after Battle.Swap(a, b) (Tab), so the seating follows the planes.</summary>
        public void Swapped(int a, int b)
        {
            if (a == b) return;
            int t = wireOf[a]; wireOf[a] = wireOf[b]; wireOf[b] = t;
        }

        // ---------- who flies what ----------

        /// <summary>The aircraft a player could fly: fighters still in the fight, not bombers flying a route or gunners' carriers.</summary>
        static bool Flyable(Battle b, int i) => b.routes[i] == null;

        /// <summary>
        /// Co-op: the players share side 0's fighters as evenly as they go, in wire order, the host taking the extra one
        /// (four between two: two each; three: two and one). The other side is the AI's. Call on the host's battle at the start.
        /// </summary>
        public void ShareOurs(Battle b, int players)
        {
            players = Math.Max(1, players);
            for (int w = 0; w < Count; w++) seat[w] = Ai;
            int k = 0;
            for (int w = 0; w < Count; w++)
            {
                int i = LocalOf(w);
                if (b.side[i] != 0 || !Flyable(b, i)) continue;
                seat[w] = k % players;
                k++;
            }
        }

        /// <summary>Player against player: the host flies side 0's fighters, the friend side 1's.</summary>
        public void SideAgainstSide(Battle b)
        {
            for (int w = 0; w < Count; w++)
            {
                int i = LocalOf(w);
                seat[w] = !Flyable(b, i) ? Ai : b.side[i] == 0 ? 0 : 1;
            }
        }

        /// <summary>The local index of this machine's first plane (the one to start each turn on), or -1 if it has none.</summary>
        public int FirstMine()
        {
            for (int w = 0; w < Count; w++) if (seat[w] == me) return LocalOf(w);
            return -1;
        }

        // ---------- local order and wire order ----------

        /// <summary>Put the battle (and this seating) into wire order.</summary>
        public void ToWire(Battle b)
        {
            for (int w = 0; w < Count; w++)
            {
                int l = LocalOf(w);
                if (l != w) { b.Swap(w, l); Swapped(w, l); }
            }
        }

        /// <summary>Put the battle (and this seating) into the local order given (as wireOf was).</summary>
        public void ToOrder(Battle b, int[] order)
        {
            for (int l = 0; l < Count; l++)
            {
                int at = LocalOf(order[l]);
                if (at != l) { b.Swap(l, at); Swapped(l, at); }
            }
        }

        /// <summary>Local plans (by local index) as wire plans (by wire id), the missile's target renamed too.</summary>
        public TurnPlan?[] PlansToWire(TurnPlan?[] local)
        {
            var w = new TurnPlan?[Count];
            for (int l = 0; l < Count && l < local.Length; l++)
            {
                if (!local[l].HasValue) continue;
                var p = local[l].Value;
                if (p.missileTarget >= 0 && p.missileTarget < Count) p.missileTarget = wireOf[p.missileTarget];
                if (p.radarTarget >= 0 && p.radarTarget < Count) p.radarTarget = wireOf[p.radarTarget];
                w[wireOf[l]] = p;
            }
            return w;
        }

        /// <summary>Wire plans (by wire id) as local plans (by local index).</summary>
        public TurnPlan?[] PlansToLocal(TurnPlan?[] wire)
        {
            var local = new TurnPlan?[Count];
            for (int w = 0; w < Count && w < wire.Length; w++)
            {
                if (!wire[w].HasValue) continue;
                var p = wire[w].Value;
                if (p.missileTarget >= 0 && p.missileTarget < Count) p.missileTarget = LocalOf(p.missileTarget);
                if (p.radarTarget >= 0 && p.radarTarget < Count) p.radarTarget = LocalOf(p.radarTarget);
                local[LocalOf(w)] = p;
            }
            return local;
        }

        /// <summary>
        /// Fly one turn in wire order from local plans, and leave the battle back in local order. The result comes back
        /// by local index, as Battle.Resolve's would. Nothing else may read the battle meanwhile.
        /// </summary>
        public TurnResult ResolveInWireOrder(Battle b, TurnPlan?[] localPlans, double turnSec, bool record = true)
        {
            var order = (int[])wireOf.Clone();
            var plans = PlansToWire(localPlans);
            ToWire(b);
            TurnResult r;
            try { r = b.Resolve(plans, turnSec, record); }
            finally { ToOrder(b, order); }
            return ResultToLocal(r);
        }

        /// <summary>A turn result flown in wire order, renumbered into local order.</summary>
        public TurnResult ResultToLocal(TurnResult r)
        {
            if (r == null) return null;
            int Map(int w) => w >= 0 && w < Count ? LocalOf(w) : w;   // flak sites (-1 - k) keep their numbers
            var frames = new AircraftFrame[r.frames.Length][];
            for (int w = 0; w < r.frames.Length; w++) frames[w < Count ? LocalOf(w) : w] = r.frames[w];
            r.frames = frames;
            for (int k = 0; k < r.hits.Count; k++)
            {
                var h = r.hits[k];
                h.shooter = Map(h.shooter);
                h.target = Map(h.target);
                r.hits[k] = h;
            }
            foreach (var tr in r.traces) { tr.owner = Map(tr.owner); tr.closestTarget = Map(tr.closestTarget); }
            foreach (var tr in r.mines) tr.owner = Map(tr.owner);
            return r;
        }

        /// <summary>The battle as bytes, in wire order, for a client to Restore. The battle itself is not changed.</summary>
        public byte[] Snapshot(Battle b)
        {
            var c = b.Clone(); Copy().ToWire(c);
            return Wire.Encode(c);
        }

        /// <summary>The battle's hash in wire order: the same on every machine that flew the same turns, whatever order each keeps.</summary>
        public ulong Hash(Battle b) => Wire.Hash(Snapshot(b));

        /// <summary>Overwrite the battle with a host's Snapshot, keeping this machine's local order.</summary>
        public void Restore(Battle b, byte[] snapshot)
        {
            var order = (int[])wireOf.Clone();
            ToWire(b);
            try { Wire.Restore(b, snapshot); }
            finally { ToOrder(b, order); }
        }
    }
}
