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
    /// setup (NetSession.Current carries it across). The friend needs the same build of the game. The host's address
    /// is never on screen unless the host asks for it (Show my address): a screenshot or a stream must not give it away.
    /// </summary>
    public partial class TitleScreen
    {
        bool lobbyOpen;
        string joinAddress, myName, lobbyError;
        bool lobbyVersus;
        /// <summary>The host asked to see this PC's addresses (hidden until then, and again each time a game is hosted).</summary>
        bool showAddress;
        /// <summary>The dogfight's briefing is open for the online fight: Fly starts it for both of you.</summary>
        bool setupOnline;

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
            int o = 2, t = 2;
            if (sd >= 0 && sd + 2 < args.Length) { int.TryParse(args[sd + 1], out o); int.TryParse(args[sd + 2], out t); }
            var dog = DogfightSetupScreen.Chosen;
            dog.ours = DogfightSetup.Resize(dog.ours, Mathf.Clamp(o, 1, 4));
            dog.theirs = DogfightSetup.Resize(dog.theirs, Mathf.Clamp(t, 1, 4));
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
            showAddress = false;
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

        /// <summary>The host picks the planes and the start on the dogfight's briefing; its Fly starts the online fight.</summary>
        void OpenOnlineBriefing()
        {
            if (Lobby == null || !Lobby.Online || !Lobby.IsHost) return;
            setup.open = true;
            setupOnline = true;
        }

        /// <summary>The host starts the fight: the briefing's setup, its random start fixed, to the friend; then both load the battle.</summary>
        void StartOnline()
        {
            var s = Lobby;
            if (s == null || !s.Online || !s.IsHost) return;
            var dog = DogfightSetupScreen.Chosen.Copy();
            if (lobbyVersus) dog.easyAimOurs = false;   // no easy aim when the other side is a person too
            dog = dog.Resolved(new System.Random());
            s.setup = new SetupMsg { dogfight = dog.ToBytes(), versus = lobbyVersus, fight = 1 };
            s.Send(Msg.Setup, s.setup);
            ChosenMission = 2;
            SceneManager.LoadScene(battleScene);
        }

        /// <summary>Every frame while the panel is open: the link's news, the friend's start, Esc to close. True: it took the frame.</summary>
        bool LobbyUpdate()
        {
            if (!lobbyOpen) return false;
            var kb = Keyboard.current;
            var s = Lobby;
            s?.Pump();
            if (missions.open) { MissionKeys(missions.Keys()); return true; }   // the mission briefing for the online fight
            if (setup.open) { SetupKeys(setup.Keys()); return true; }   // the briefing for the online fight
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { CloseLobby(); return true; }
            if (s == null) return true;
            while (s.TryNext(out var type, out var data))
            {
                if (type != Msg.Setup || s.IsHost) continue;
                s.setup = NetSession.Read<SetupMsg>(data);
                ChosenMission = 2;
                SceneManager.LoadScene(battleScene);
                return true;
            }
            if (autoStart && s.IsHost && s.Online) { autoStart = false; StartOnline(); return true; }
            if (s.IsHost && s.Online && kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) OpenOnlineBriefing();
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
                    if (!showAddress) return "Hosting. Waiting for your friend to join...";
                    var mine = NetLink.MyAddresses();
                    string where = mine.Length == 0 ? "this PC's address" : string.Join(",  ", mine.Take(3));
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
                Shadowed(new Rect(x - 100, y, w + 200, 52), "To join, type the address your friend gives you. Same build of the game on both.", hint);
            }
            else if (s.IsHost && !s.link.Connected)
            {
                // the address stays off the screen until asked for: it must not end up in a screenshot or a stream
                if (GUI.Button(new Rect((sw - 300) / 2, y, 300, 44), showAddress ? "Hide my address" : "Show my address", button)) showAddress = !showAddress;
                y += 56;
            }
            else if (s.IsHost && s.Online)
            {
                if (GUI.Button(new Rect(x + 40, y, 300, 44), (lobbyVersus ? "" : "▶ ") + "Co-op: both of you vs the AI", lobbyVersus ? button : picked)) lobbyVersus = false;
                if (GUI.Button(new Rect(x + 380, y, 300, 44), (lobbyVersus ? "▶ " : "") + $"Versus: you vs {s.friendName}", lobbyVersus ? picked : button)) lobbyVersus = true;
                y += 60;
                Shadowed(new Rect(x - 100, y, w + 200, 30), lobbyVersus ? $"Your flight is yours, the enemy flight is {s.friendName}'s." : $"You share your flight with {s.friendName}; the enemy flight is the AI's.", hint);
                y += 40;
                if (GUI.Button(new Rect((sw - 360) / 2, y, 360, 56), "Pick the planes and fly", picked)) OpenOnlineBriefing();
                y += 64;
            }
            if (s != null && GUI.Button(new Rect((sw - 200) / 2, sh - 80, 200, 44), "Leave", button)) { s.Close(); lobbyError = null; }
            if (GUI.Button(new Rect(24, sh - 72, 212, 48), "Back (Esc)", button)) CloseLobby();
        }
    }
}
