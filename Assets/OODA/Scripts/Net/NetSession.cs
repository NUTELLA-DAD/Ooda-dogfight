using System;
using System.Collections.Generic;
using Ooda.Turn.Core;

namespace Ooda.Net
{
    /// <summary>First thing each side sends: who it is, and which game it runs (both must run the same build).</summary>
    public class HelloMsg
    {
        public int protocol;
        public string version;
        public string name;
    }

    /// <summary>The host starts a fight: both machines build the same battle from this.</summary>
    public class SetupMsg
    {
        public int mission = 2;        // TurnBattle.Mission: Dogfight
        public int start;              // Scenarios.Start
        public int ours = 1, theirs = 1;
        /// <summary>Side against side (the host the P-51s, the friend the Bf 109s); false, both of you on the P-51s against the AI.</summary>
        public bool versus;
        public int fight;              // counts up with each new fight, so a late message from the last one is ignored
    }

    /// <summary>
    /// One player's turn, in wire order: the plans for the planes it flies (null elsewhere), and the orders and formation
    /// shapes it set for them. Also sent as a Draft while planning, so a co-op wingman's ghost shows what you are planning.
    /// </summary>
    public class PlansMsg
    {
        public int fight, turn;
        public TurnPlan?[] plans;
        public Order[] orders;         // by wire id; only the sender's own planes count
        public Formation[] shapes;     // by flight; only the flights the sender's plane leads count
    }

    /// <summary>The host's word on a turn: every plane's plan, order and formation, and the hash of the battle after it.</summary>
    public class TurnMsg
    {
        public int fight, turn;
        public double turnSec;
        public TurnPlan?[] plans;
        public Order[] orders;
        public Formation[] shapes;
        public ulong hash;
    }

    public class SnapshotMsg
    {
        public int fight, turn;
        public byte[] data;
    }

    /// <summary>
    /// The game with a friend over the network, from the lobby to the end: the link, who is host, the fight being played
    /// and who flies what in it. It outlives the scene change from the title screen to the battle (Current).
    /// </summary>
    public class NetSession
    {
        /// <summary>Bumped whenever the messages change, so two different builds refuse each other.</summary>
        public const int Protocol = 1;

        /// <summary>The game being played over the network, or null playing alone.</summary>
        public static NetSession Current;

        public readonly NetLink link;
        public readonly string version, myName;
        public bool IsHost => link.IsHost;
        /// <summary>This machine's player: 0 the host, 1 the friend.</summary>
        public int Me => IsHost ? 0 : 1;
        public string friendName;
        /// <summary>The friend said hello and runs the same game.</summary>
        public bool Ready { get; private set; }
        /// <summary>A reason the session cannot go on (another version, the link lost), for the screen.</summary>
        public string Problem => problem ?? (link.Lost ? link.Error : null);
        string problem;
        public SetupMsg setup;
        public Seating seating;

        readonly Queue<(Msg, byte[])> held = new Queue<(Msg, byte[])>();

        public NetSession(NetLink link, string version, string myName)
        {
            this.link = link; this.version = version; this.myName = myName;
        }

        public static NetSession Host(string version, string myName, int port = NetLink.DefaultPort) => new NetSession(NetLink.Host(port), version, myName);
        public static NetSession Join(string address, int port, string version, string myName) => new NetSession(NetLink.Join(address, port), version, myName);

        public bool Online => Ready && link.Connected && problem == null;

        bool saidHello;

        /// <summary>
        /// Call every frame: says hello once connected, checks the friend's hello, and keeps the rest for TryNext.
        /// </summary>
        public void Pump()
        {
            if (link.Connected && !saidHello)
            {
                saidHello = true;
                Send(Msg.Hello, new HelloMsg { protocol = Protocol, version = version, name = myName });
            }
            while (link.TryReceive(out var type, out var data))
            {
                if (type == Msg.Hello)
                {
                    HelloMsg h;
                    try { h = Wire.Decode<HelloMsg>(data); }
                    catch (Exception) { problem = "Your friend's game sent something this one can't read: run the same build on both machines."; continue; }
                    friendName = string.IsNullOrWhiteSpace(h.name) ? "your friend" : h.name;
                    if (h.protocol != Protocol || h.version != version)
                        problem = $"{friendName} runs another build of the game ({h.version}, this is {version}). Use the same build on both machines.";
                    else Ready = true;
                    continue;
                }
                held.Enqueue((type, data));
            }
        }

        /// <summary>The next game message (anything but the hello), oldest first.</summary>
        public bool TryNext(out Msg type, out byte[] data)
        {
            if (held.Count > 0) { (type, data) = held.Dequeue(); return true; }
            type = 0; data = null; return false;
        }

        public void Send<T>(Msg type, T message) => link.Send(type, Wire.Encode(message));

        public static T Read<T>(byte[] data) => Wire.Decode<T>(data);

        /// <summary>Leave: say goodbye, close the link, and forget the session if it is the current one.</summary>
        public void Close()
        {
            link.Dispose();
            if (Current == this) Current = null;
        }

        /// <summary>Who flies what in the fight just built from setup: shared out between you (co-op) or side against side.</summary>
        public void SeatFor(Battle b)
        {
            seating = new Seating(b.Count, Me);
            if (setup != null && setup.versus) seating.SideAgainstSide(b);
            else seating.ShareOurs(b, 2);
        }
    }

    /// <summary>
    /// The turn's bookkeeping between the machines, apart from the screen: what a player sends for its planes, how the host
    /// puts both players' turns together, and how a machine takes the host's word on the orders and formations.
    /// All in wire order (see Seating). Pure C#.
    /// </summary>
    public static class Lockstep
    {
        /// <summary>
        /// This machine's part of the turn: the plans (by local index) of the planes this player flies, and on the host also
        /// the AI's planes, with their orders and the shapes of the flights they lead.
        /// </summary>
        public static PlansMsg Mine(Battle b, Seating s, TurnPlan?[] localPlans, bool withAi, int fight)
        {
            var keep = new TurnPlan?[s.Count];
            for (int l = 0; l < s.Count && l < localPlans.Length; l++)
                if (s.Mine(l) || withAi && s.SeatOf(l) == Seating.Ai) keep[l] = localPlans[l];
            return new PlansMsg { fight = fight, turn = b.turn, plans = s.PlansToWire(keep), orders = OrdersToWire(b, s), shapes = Shapes(b) };
        }

        static Order[] OrdersToWire(Battle b, Seating s)
        {
            var o = new Order[s.Count];
            for (int l = 0; l < s.Count; l++) o[s.wireOf[l]] = b.orders[l];
            return o;
        }

        static Formation[] Shapes(Battle b)
        {
            var f = new Formation[b.flights.Count];
            for (int k = 0; k < f.Length; k++) f[k] = b.flights[k].shape;
            return f;
        }

        /// <summary>The player whose plane leads flight f now (the AI's when no player's does).</summary>
        static int FlightOwner(Battle b, Seating s, int f)
        {
            int lead = b.Leader(f);
            return lead < 0 ? Seating.Ai : s.SeatOf(lead);
        }

        /// <summary>
        /// On the host: the whole turn from the host's part and the friend's. Each plane takes its plan and order from the
        /// player who flies it (the AI's from the host), each flight its shape from the player whose plane leads it.
        /// </summary>
        public static TurnMsg Merge(Battle b, Seating s, PlansMsg host, PlansMsg friend, int friendPlayer, double turnSec)
        {
            int n = s.Count;
            var t = new TurnMsg { fight = host.fight, turn = b.turn, turnSec = turnSec, plans = new TurnPlan?[n], orders = new Order[n], shapes = (Formation[])host.shapes.Clone() };
            for (int w = 0; w < n; w++)
            {
                bool theirs = s.seat[w] == friendPlayer;
                t.plans[w] = theirs ? friend.plans[w] : host.plans[w];
                t.orders[w] = theirs ? friend.orders[w] : host.orders[w];
            }
            for (int f = 0; f < t.shapes.Length && f < friend.shapes.Length; f++)
                if (FlightOwner(b, s, f) == friendPlayer) t.shapes[f] = friend.shapes[f];
            return t;
        }

        /// <summary>Take the turn's orders and formation shapes into this machine's battle (in its own order).</summary>
        public static void Apply(Battle b, Seating s, TurnMsg t)
        {
            for (int l = 0; l < s.Count && s.wireOf[l] < t.orders.Length; l++) b.orders[l] = t.orders[s.wireOf[l]];
            for (int f = 0; f < b.flights.Count && f < t.shapes.Length; f++) b.flights[f].shape = t.shapes[f];
        }
    }
}
