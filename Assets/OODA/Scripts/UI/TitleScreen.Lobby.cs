using System.Linq;
using Ooda.Net;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Ooda.UI
{
    /// <summary>
    /// The online panel (O, or the Online button): host a dogfight or join a friend's by address, and once you are both
    /// in, the host picks co-op or versus and the sides and starts it. Both games then load the battle with the same
    /// setup (NetSession.Current carries it across). The friend needs the same build of the game. To reach each other
    /// over the internet, both run Tailscale and the friend types the host's 100.x address; on one network, the host's
    /// local address works as it is.
    /// </summary>
    public partial class TitleScreen
    {
        bool lobbyOpen;
        string joinAddress, myName, lobbyError;
        bool lobbyVersus;

        NetSession Lobby => NetSession.Current;

        static string GameVersion => $"{Application.version} {Application.buildGUID}".Trim();

        /// <summary>The online self-test from the command line: -nethost [-netsides O T] [-netversus], or -netjoin address. True: it is running.</summary>
        bool LobbyFromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            int h = System.Array.IndexOf(args, "-nethost"), j = System.Array.IndexOf(args, "-netjoin");
            if (h < 0 && j < 0) return false;
            OpenLobby();
            int sd = System.Array.IndexOf(args, "-netsides");
            if (sd >= 0 && sd + 2 < args.Length && int.TryParse(args[sd + 1], out int o) && int.TryParse(args[sd + 2], out int t)) { ChosenOurs = Mathf.Clamp(o, 1, 4); ChosenTheirs = Mathf.Clamp(t, 1, 4); }
            else { ChosenOurs = 2; ChosenTheirs = 2; }
            lobbyVersus = System.Array.IndexOf(args, "-netversus") >= 0;
            autoStart = h >= 0;
            if (h >= 0) HostGame();
            else if (j + 1 < args.Length) { joinAddress = args[j + 1]; JoinGame(); }
            return true;
        }

        bool autoStart;

        void OpenLobby()
        {
            lobbyOpen = true;
            lobbyError = null;
            if (joinAddress == null) joinAddress = PlayerPrefs.GetString("OODA.JoinAddress", "");
            if (myName == null) myName = PlayerPrefs.GetString("OODA.Name", System.Environment.UserName);
            Application.runInBackground = true;   // the hello and the start come in while the window is behind another
        }

        void CloseLobby()
        {
            Lobby?.Close();
            lobbyOpen = false;
        }

        void HostGame()
        {
            lobbyError = null;
            Lobby?.Close();
            PlayerPrefs.SetString("OODA.Name", myName);
            try { NetSession.Current = NetSession.Host(GameVersion, myName); }
            catch (System.Exception e) { lobbyError = $"Could not host on port {NetLink.DefaultPort}: {e.Message}"; }
        }

        void JoinGame()
        {
            lobbyError = null;
            if (!NetLink.ParseAddress(joinAddress, out var host, out int port)) { lobbyError = "Type the host's address first (from their screen)."; return; }
            Lobby?.Close();
            PlayerPrefs.SetString("OODA.JoinAddress", joinAddress.Trim());
            PlayerPrefs.SetString("OODA.Name", myName);
            NetSession.Current = NetSession.Join(host, port, GameVersion, myName);
        }

        /// <summary>The host starts the fight: tell the friend, then both load the battle.</summary>
        void StartOnline()
        {
            var s = Lobby;
            if (s == null || !s.Online || !s.IsHost) return;
            s.setup = new SetupMsg
            {
                start = Random.Range(0, 4), ours = ChosenOurs, theirs = ChosenTheirs, versus = lobbyVersus, fight = 1,
            };
            s.Send(Msg.Setup, s.setup);
            ChosenMission = 2;
            SceneManager.LoadScene(battleScene);
        }

        /// <summary>Every frame while the panel is open: the link's news, the friend's start, Esc to close. True: it took the frame.</summary>
        bool LobbyUpdate()
        {
            if (!lobbyOpen) return false;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { CloseLobby(); return true; }
            var s = Lobby;
            if (s == null) return true;
            s.Pump();
            while (s.TryNext(out var type, out var data))
            {
                if (type != Msg.Setup || s.IsHost) continue;
                s.setup = NetSession.Read<SetupMsg>(data);
                ChosenMission = 2;
                ChosenOurs = s.setup.ours; ChosenTheirs = s.setup.theirs;
                SceneManager.LoadScene(battleScene);
                return true;
            }
            if (autoStart && s.IsHost && s.Online) { autoStart = false; StartOnline(); return true; }
            if (s.IsHost && s.Online && kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame) ChosenOurs = Mathf.Min(4, ChosenOurs + 1);
                if (kb.downArrowKey.wasPressedThisFrame) ChosenOurs = Mathf.Max(1, ChosenOurs - 1);
                if (kb.pageUpKey.wasPressedThisFrame) ChosenTheirs = Mathf.Min(4, ChosenTheirs + 1);
                if (kb.pageDownKey.wasPressedThisFrame) ChosenTheirs = Mathf.Max(1, ChosenTheirs - 1);
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) StartOnline();
            }
            return true;
        }

        string LobbyStatus(NetSession s)
        {
            if (lobbyError != null) return lobbyError;
            if (s == null) return "Host a dogfight, or join your friend's with their address.";
            if (s.Problem != null) return s.Problem;
            if (s.IsHost)
            {
                if (!s.link.Connected)
                {
                    var mine = NetLink.MyAddresses();
                    string where = mine.Length == 0 ? "this PC's address"
                        : string.Join(",  ", mine.Take(3).Select(a => NetLink.IsTailscale(a) ? $"{a} (Tailscale)" : a));
                    return $"Hosting. Your friend joins with: {where}. Waiting for them...";
                }
                return s.Ready ? $"{s.friendName} joined! Pick the fight and start it." : "Someone is connecting...";
            }
            if (!s.link.Connected) return $"Connecting to {s.link.PeerAddress}...";
            return s.Ready ? $"Connected to {s.friendName}. Waiting for them to start the fight." : "Connected, saying hello...";
        }

        void LobbyGUI(float sw, float sh)
        {
            var s = Lobby;
            float w = 720, x = (sw - w) / 2, y = sh - 400;
            Shadowed(new Rect(0, y, sw, 40), "Online with a friend", sub);
            y += 48;
            Shadowed(new Rect(x - 100, y, w + 200, 56), LobbyStatus(s), note);
            y += 64;

            if (s == null || s.Problem != null)
            {
                GUI.Label(new Rect(x, y + 8, 120, 34), "Your name", hint);
                myName = GUI.TextField(new Rect(x + 130, y, 220, 40), myName ?? "", 24, button);
                y += 52;
                if (GUI.Button(new Rect(x, y, 260, 48), "Host a game", picked)) HostGame();
                GUI.Label(new Rect(x + 280, y + 10, 40, 34), "or", hint);
                joinAddress = GUI.TextField(new Rect(x + 320, y, 260, 48), joinAddress ?? "", 64, button);
                if (GUI.Button(new Rect(x + 590, y, 130, 48), "Join", picked)) JoinGame();
                y += 60;
                Shadowed(new Rect(x - 100, y, w + 200, 52), "Over the internet: both install Tailscale (tailscale.com) and sign in, the host shares their PC with you, and you join its 100.x address. Same build of the game on both.", hint);
            }
            else if (s.IsHost && s.Online)
            {
                if (GUI.Button(new Rect(x + 40, y, 300, 44), (lobbyVersus ? "" : "▶ ") + "Co-op: both of you vs the AI", lobbyVersus ? button : picked)) lobbyVersus = false;
                if (GUI.Button(new Rect(x + 380, y, 300, 44), (lobbyVersus ? "▶ " : "") + $"Versus: you vs {s.friendName}", lobbyVersus ? picked : button)) lobbyVersus = true;
                y += 60;
                SidePicker(sw, y, lobbyVersus ? "Your P-51s" : "P-51s (shared)", lobbyVersus ? $"{s.friendName}'s Bf 109s" : "Bf 109s (AI)");
                y += 112;
                if (GUI.Button(new Rect((sw - 300) / 2, y, 300, 56), "Start the fight", picked)) StartOnline();
                y += 64;
            }
            if (s != null && GUI.Button(new Rect((sw - 200) / 2, sh - 80, 200, 44), "Leave", button)) { s.Close(); lobbyError = null; }
            if (GUI.Button(new Rect(24, sh - 72, 212, 48), "Back (Esc)", button)) CloseLobby();
        }
    }
}
