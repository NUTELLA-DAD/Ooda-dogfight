using System.Collections.Generic;
using System.Linq;
using Ooda.Net;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The dogfight with a friend on another machine (see Net/NetSession.cs and the README's Online play). Each of you
    /// plans your own planes; Fly turn sends your part and waits. The host adds the AI pilots' plans, puts the turn
    /// together, flies it and sends every plane's plan with a hash of the result; the friend flies the same turn and
    /// checks the hash, taking the host's whole battle if they ever differ. In co-op your wingman's ghost shows what
    /// they are planning as they plan it. If the connection goes, their planes go back to their AI pilots and the fight
    /// carries on here. The host starts each new fight; Restart on the friend's side waits for it.
    /// </summary>
    public partial class TurnBattle
    {
        /// <summary>The game with a friend, or null playing alone (and after the connection is lost).</summary>
        NetSession net;
        string netLogWho;
        /// <summary>Our part of this turn has gone: waiting for the host's word (or, on the host, for the friend's part).</summary>
        bool netWaiting;
        bool netOnLead, netStarted, netRestartFromHost;
        PlansMsg netMine, netFriend;
        /// <summary>Co-op: what the friend is planning for their planes right now, by wire id (their ghosts fly it).</summary>
        TurnPlan?[] friendDraft;
        int friendDraftTurn = -1;
        byte[] draftSent;
        float draftAt;
        readonly List<(Msg type, byte[] data)> netInbox = new List<(Msg, byte[])>();

        bool Online => net != null;
        string Friend => net?.friendName ?? "your friend";

        /// <summary>At Start: playing with a friend if the title screen set a fight up with one.</summary>
        void NetSetUp()
        {
            var s = NetSession.Current;
            if (s == null) return;
            if (!s.Online || s.setup == null) { s.Close(); return; }
            net = s;
            Application.runInBackground = true;   // keep the turn going while the window is behind another
            mission = Mission.Dogfight;
            dogSetup = DogfightSetup.FromBytes(s.setup.dogfight);   // the host's, its start already fixed
        }

        /// <summary>The side this machine's player flies: the Bf 109s for the friend in a versus fight.</summary>
        int MySide => net != null && net.setup.versus && !net.IsHost ? 1 : 0;

        bool NetMine(int i) => net == null || net.seating == null || (i >= 0 && i < net.seating.Count && net.seating.Mine(i));

        /// <summary>
        /// Before a restart builds a new fight: the friend's Restart waits for the host's; the host's picks how the
        /// next fight begins and tells the friend. False: don't restart now.
        /// </summary>
        bool NetRestarting()
        {
            netWaiting = false; netMine = netFriend = null; friendDraft = null; draftSent = null;
            if (net == null || !netStarted) return true;
            if (!net.IsHost)
            {
                if (netRestartFromHost) return true;
                NetNotice($"{Friend} starts the next fight.");
                return false;
            }
            // the next fight: the same planes, and a new start (and arena) if the briefing asked for a random one
            var next = dogSetup.Copy();
            next.randomStart = Ooda.UI.DogfightSetupScreen.Chosen.randomStart;
            next.randomArena = Ooda.UI.DogfightSetupScreen.Chosen.randomArena;
            dogSetup = next.Resolved(startDice);
            net.setup.dogfight = dogSetup.ToBytes();
            net.setup.fight++;
            net.Send(Msg.Setup, net.setup);
            return true;
        }

        /// <summary>The new fight is built: who flies what (Restart calls this before laying out the views).</summary>
        void NetSeat()
        {
            if (net == null) return;
            net.SeatFor(battle);
        }

        /// <summary>The views are laid out: sit in this player's first plane.</summary>
        void NetSitDown()
        {
            if (net == null) return;
            int first = net.seating.FirstMine();
            if (first > 0)
            {
                // the ghost colours were laid out for aircraft 0 being yours: the plane there now and your own swap colours
                NetRecolourGhost(0, battle.side[0] == MySide ? holoFriendColor : holoEnemyColor);
                NetRecolourGhost(first, holoColor);
                SwapAll(0, first);
            }
            netStarted = true;
            if (Dogfighting)
                NetNotice(net.setup.versus ? $"Online against {Friend}: your {Plural("plane", MineCount())} against theirs."
                                            : $"Online with {Friend}: you fly {MineCount()} of your flight's {SideCount()}, {Friend} the rest.");
        }

        void NetRecolourGhost(int i, Color c)
        {
            if (i < 0 || i >= views.Length || views[i] == null) return;
            if (holos[i] != null) { if (holos[i] == homeHolo) homeHolo.SetActive(false); else Destroy(holos[i]); }
            int before = holoMats.Count;
            holos[i] = MakeHolo(views[i].gameObject, c, $"Ghost {i}");
            if (holos[i] != null) spawned.Add(holos[i]);
            for (int k = before; k < holoMats.Count; k++) spawnedHoloMats.Add(holoMats[k]);
            holoMats.RemoveRange(before, holoMats.Count - before);
        }

        int MineCount() => Enumerable.Range(0, battle.Count).Count(i => net.seating.Mine(i));
        int SideCount() => Enumerable.Range(0, battle.Count).Count(i => battle.side[i] == SideOf0 && battle.routes[i] == null);
        static string Plural(string kind, int n) => n == 1 ? kind : kind + "s";

        void NetNotice(string s) { notice = s; noticeUntil = Time.time + 8f; }

        // ---------- the turn ----------

        /// <summary>Fly turn, online: send our part and wait (the host waits for the friend's part, then flies it).</summary>
        void NetSendTurn(TurnPlan?[] localPlans, bool onLead)
        {
            netOnLead = onLead;
            netWaiting = true;
            phase = Phase.Thinking;
            var part = Lockstep.Mine(battle, net.seating, localPlans, net.IsHost, net.setup.fight);
            if (net.IsHost) { netMine = part; NetTryFly(); }
            else net.Send(Msg.Plans, part);
        }

        /// <summary>On the host: both parts are in, so put the turn together, fly it, and send it with its hash.</summary>
        void NetTryFly()
        {
            if (!net.IsHost || !netWaiting || netMine == null || netFriend == null || netFriend.turn != battle.turn) return;
            var t = Lockstep.Merge(battle, net.seating, netMine, netFriend, 1, turnSec);
            netMine = netFriend = null;
            netWaiting = false;
            Lockstep.Apply(battle, net.seating, t);
            FlyTurn(net.seating.PlansToLocal(t.plans), netOnLead);
            t.hash = net.seating.Hash(battle);
            net.Send(Msg.Turn, t);
            netTurnsFlown++;
            NetLog($"fight {t.fight} turn {t.turn + 1} flown, hash {t.hash:x16}");
        }

        /// <summary>On the friend: the host's word on the turn. Fly it, and ask for the host's battle if we came out different.</summary>
        void NetTakeTurn(TurnMsg t)
        {
            if (!netWaiting || t.turn != battle.turn) return;
            netWaiting = false;
            turnSec = (float)t.turnSec;
            Lockstep.Apply(battle, net.seating, t);
            FlyTurn(net.seating.PlansToLocal(t.plans), netOnLead);
            netTurnsFlown++;
            ulong mine = net.seating.Hash(battle);
            NetLog($"fight {t.fight} turn {t.turn + 1} flown, hash {mine:x16} {(mine == t.hash ? "same as the host's" : $"DIFFERENT from the host's {t.hash:x16}")}");
            if (mine != t.hash)
            {
                netResyncs++;
                Debug.LogWarning($"OODA online: turn {t.turn} came out different from the host's; asking for its battle.");
                net.Send(Msg.Resync, new SnapshotMsg { fight = net.setup.fight, turn = battle.turn });
            }
        }

        /// <summary>On the friend: the host's whole battle, after a turn came out different here.</summary>
        void NetTakeSnapshot(SnapshotMsg s)
        {
            if (reading != null && !reading.IsCompleted) { try { reading.Wait(); } catch (System.AggregateException) { } }
            reading = null; readingFor = null; read = null;
            net.seating.Restore(battle, s.data);
            NetLog($"took the host's battle at turn {s.turn}: hash now {net.seating.Hash(battle):x16}");
            preview = gunPreview = null;
            if (phase == Phase.Planning) BeginPlanning();
            NetNotice($"Caught up with {Friend}'s game after a turn came out differently here.");
        }

        // ---------- every frame ----------

        void NetUpdate()
        {
            if (net == null) return;
            net.Pump();
            while (net.TryNext(out var type, out var data)) netInbox.Add((type, data));
            bool busy = replaying || menuOpen || briefing;
            for (int k = 0; k < netInbox.Count && net != null; k++)
            {
                var (type, data) = netInbox[k];
                if (busy && type != Msg.Draft && type != Msg.Plans && type != Msg.Resync) continue;   // the turn waits for the replay or the menu
                netInbox.RemoveAt(k--);
                try { NetHandle(type, data); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            if (net == null) return;
            if (!net.Online) { NetLost(); return; }   // after what came before the goodbye
            // nothing of yours still flying (co-op, your planes down): your turns go by themselves
            if (phase == Phase.Planning && !netWaiting && !busy && unplannedAsk == null && !Enumerable.Range(0, battle.Count).Any(i => NetMine(i) && !battle.Out(i))) Commit();
            NetDraftOut();
        }

        void NetHandle(Msg type, byte[] data)
        {
            switch (type)
            {
                case Msg.Setup when !net.IsHost:
                    net.setup = NetSession.Read<SetupMsg>(data);
                    dogSetup = DogfightSetup.FromBytes(net.setup.dogfight);
                    netRestartFromHost = true;
                    try { Restart(); } finally { netRestartFromHost = false; }
                    break;
                case Msg.Plans when net.IsHost:
                {
                    var p = NetSession.Read<PlansMsg>(data);
                    if (p.fight != net.setup.fight) break;
                    netFriend = p;
                    NetTryFly();
                    break;
                }
                case Msg.Turn when !net.IsHost:
                {
                    var t = NetSession.Read<TurnMsg>(data);
                    if (t.fight == net.setup.fight) NetTakeTurn(t);
                    break;
                }
                case Msg.Resync when net.IsHost:
                {
                    var r = NetSession.Read<SnapshotMsg>(data);
                    if (r.fight == net.setup.fight) net.Send(Msg.Snapshot, new SnapshotMsg { fight = net.setup.fight, turn = battle.turn, data = net.seating.Snapshot(battle) });
                    break;
                }
                case Msg.Snapshot when !net.IsHost:
                {
                    var s = NetSession.Read<SnapshotMsg>(data);
                    if (s.fight == net.setup.fight) NetTakeSnapshot(s);
                    break;
                }
                case Msg.Draft:
                {
                    var d = NetSession.Read<PlansMsg>(data);
                    if (d.fight != net.setup.fight || d.turn < battle.turn) break;
                    friendDraft = d.plans; friendDraftTurn = d.turn;
                    dirty = true;
                    break;
                }
            }
        }

        /// <summary>Co-op: a few times a second while planning, send what you are planning if it changed, for your wingman's ghosts.</summary>
        void NetDraftOut()
        {
            if (net.setup.versus || phase != Phase.Planning || netWaiting || Time.unscaledTime < draftAt) return;
            draftAt = Time.unscaledTime + 0.2f;
            var p = new TurnPlan?[battle.Count];
            if (!battle.Out(0) && battle.orders[0] != Order.AutoFight) p[0] = plan;
            ApplyManual(p);
            var bytes = Ooda.Turn.Core.Wire.Encode(Lockstep.Mine(battle, net.seating, p, false, net.setup.fight));
            if (draftSent != null && bytes.SequenceEqual(draftSent)) return;
            draftSent = bytes;
            net.link.Send(Msg.Draft, bytes);
        }

        /// <summary>In the preview: the friend's planes fly what the friend is planning (co-op).</summary>
        void NetDrafts(TurnPlan?[] p)
        {
            if (net == null || friendDraft == null || friendDraftTurn != battle.turn) return;
            for (int l = 0; l < p.Length && l < net.seating.Count; l++)
            {
                int w = net.seating.wireOf[l];
                if (!net.seating.Mine(l) && net.seating.seat[w] != Seating.Ai && w < friendDraft.Length && friendDraft[w].HasValue) p[l] = friendDraft[w];
            }
        }

        /// <summary>The connection went: the friend's planes fly on their AI pilots here, and the fight goes on.</summary>
        void NetLost()
        {
            NetLog($"lost: {net.Problem}");
            string why = net.Problem ?? "The connection was lost.";
            string who = Friend;
            net.Close();
            net = null;
            friendDraft = null;
            netInbox.Clear();
            ListFlight();
            if (netWaiting) { netWaiting = false; phase = Phase.Planning; }
            NetNotice($"{why} The AI flies {who}'s planes now; press Fly turn to carry on.");
        }

        /// <summary>Leaving the battle (the main menu, quitting): say goodbye.</summary>
        void NetLeave()
        {
            if (net == null) return;
            net.Close();
            net = null;
        }

        void OnApplicationQuit() => NetLeave();

        /// <summary>Online, the fight is over only when one whole side is out (your friend may fly on after your planes go down).</summary>
        bool NetFightIsOver()
        {
            bool ourSideOut = Enumerable.Range(0, battle.Count).All(i => battle.side[i] != SideOf0 || battle.Out(i));
            return battle.OverWith(ourSideOut);
        }

        string NetOutcome()
        {
            int foes = 0, foesDown = 0, ours = 0, oursDown = 0;
            for (int i = 0; i < battle.Count; i++)
            {
                if (battle.side[i] != SideOf0) { foes++; if (battle.Out(i)) foesDown++; }
                else { ours++; if (battle.Out(i)) oursDown++; }
            }
            string tally = $" {foesDown} of their {foes} down, {oursDown} of your {ours} lost.";
            if (foesDown == foes && oursDown == ours) return "Everyone went down. A draw." + tally;
            if (foesDown == foes) return (net.setup.versus ? $"You beat {Friend}!" : $"The sky is clear. You and {Friend} win!") + tally;
            return (net.setup.versus ? $"{Friend} wins: your side is down." : "Your whole flight is down.") + tally;
        }

        /// <summary>A line at the top while online: who with, and on the host whether the friend has sent their turn.</summary>
        void NetGUI(float sw, GUIStyle label)
        {
            if (net == null) return;
            string what = net.setup.versus ? $"Online against {Friend}" : $"Online with {Friend} (co-op)";
            string state = netWaiting ? (net.IsHost ? $"waiting for {Friend}'s turn" : $"waiting for {Friend}'s game")
                : net.IsHost && netFriend != null ? $"{Friend} is ready" : phase == Phase.Planning ? $"{Friend} is planning" : "";
            GUI.Label(new Rect(sw / 2 - 300, 6, 600, 24), state.Length > 0 ? $"{what}  ·  {state}" : what, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
        }
        // ---------- the online self-test: -netbot N flies N turns by itself on each machine, logs and quits ----------

        int netBotTurns = -1, netResyncs, netTurnsFlown;
        float netBotAt;

        void NetBotSetUp()
        {
            var args = System.Environment.GetCommandLineArgs();
            int at = System.Array.IndexOf(args, "-netbot");
            if (at < 0 || net == null) return;
            netBotTurns = at + 1 < args.Length && int.TryParse(args[at + 1], out int n) ? Mathf.Max(1, n) : 12;
            playbackSpeed = 30f;
            botPilot = new EnemyAi { me = 0, role = EnemyAi.Role.Fighter, rng = new Lcg(net.IsHost ? 4242u : 2424u), ignoreObstacles = true };
        }

        void NetBotUpdate()
        {
            if (netBotTurns < 0) return;
            if (net == null) { NetBotDone("the connection went"); return; }
            if (phase == Phase.Over) { NetBotDone(outcome ?? "over"); return; }
            if (phase != Phase.Planning || netWaiting || read == null || reading != null || Time.unscaledTime < netBotAt) return;
            if (battle.turn >= netBotTurns) { NetBotDone("turn limit"); return; }
            if (!battle.Out(0))
            {
                botPilot.Forget();
                var p = botPilot.Plan(battle, turnSec);
                SetAim(p.aim); plan.throttle = p.throttle; plan.wep = p.wep;
                plan.burstStart = p.burstStart; plan.burstEnd = p.burstEnd;
                dirty = true;
            }
            netBotAt = Time.unscaledTime + 0.3f;
            Commit();
            if (unplannedAsk != null) Commit();   // fly the others on their AI pilots
        }

        void NetBotDone(string why)
        {
            NetLog($"online self-test over ({why}): {netTurnsFlown} turns flown, {netResyncs} came out different and were caught up");
            netBotTurns = -1;
            NetLeave();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        /// <summary>A line in OnlineLog-host.txt or OnlineLog-friend.txt beside the dogfight log.</summary>
        void NetLog(string line)
        {
            if (net != null) netLogWho = net.IsHost ? "host" : "friend";   // kept after the link goes, so the last lines land in the same file
            string who = netLogWho ?? "alone";
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(LogPath), $"OnlineLog-{who}.txt");
                System.IO.File.AppendAllText(path, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{System.Environment.NewLine}");
            }
            catch (System.Exception e) { Debug.LogWarning($"OODA: could not write the online log: {e.Message}"); }
        }
    }
}
