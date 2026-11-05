using System.Collections.Generic;
using System.Linq;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Ooda.UI
{
    /// <summary>
    /// The opening screen: the squadron's close formation (the P-51s' two finger-fours over the B-17 vic, laid out as
    /// Scenarios.Squadron lays them out) flying up the valley route on rails, a camera that cuts between slow cinematic
    /// shots every few seconds, and the title with a mission picker (Dogfight, the default, or Squadron Battle). Enter, Space or pad A starts the chosen mission in
    /// the turn battle, which reads it from ChosenMission.
    /// </summary>
    public partial class TitleScreen : MonoBehaviour
    {
        /// <summary>The mission picked here (TurnBattle.Mission's order: Squadron, Escort, Dogfight, ValleyRun, TownStrike), for the battle to read.</summary>
        public static int? ChosenMission;
        /// <summary>The dogfight's sides picked here: your planes and the enemy's, 1 to 4 each.</summary>
        public static int ChosenOurs = 1, ChosenTheirs = 1;

        public GameObject fighterPrefab, bomberPrefab;
        [Tooltip("The dogfight's turn prefabs by Craft (P-51, Spitfire, Bf 109, F-4, ...), parked on the squadron briefing's apron.")]
        public GameObject[] craftPrefabs;
        [Tooltip("The route the formation flies (metres, x east, y height, z north): the escort route up the valley.")]
        public Vector3[] route;
        public Camera cam;
        public Terrain terrain;
        public string battleScene = "TurnBattle";
        [Tooltip("The jet test: the Phantom in free flight with an afterburner, on its own away from the WW2 missions (OODA > Build Jet Test).")]
        public string jetScene = "JetTest";
        public float kias = 135;
        public float shotSec = 7f, fadeSec = 0.6f;
        [Tooltip("The squadron's spacing, scaled down for the camera: tighter than the battle flies it.")]
        public float formationScale = 0.65f;

        // The menu offers two missions: the dogfight (the default) and the squadron battle. The others (Escort, Valley Run,
        // Town Strike) are still in the battle, M cycles to them there, and the self-tests below can start them.
        static readonly int[] Menu = { 2, 0 };   // TurnBattle.Mission numbers, left to right
        static readonly string[] MenuNames = { "Dogfight", "Squadron Battle" };
        const string SquadronNote = "8 P-51s and 3 B-17s against 8 Bf 109s and the flak";
        int pick = 2;   // a TurnBattle.Mission number: the dogfight by default
        int MenuIndex => Mathf.Max(0, System.Array.IndexOf(Menu, pick));

        struct Member { public Transform t; public Vector3 offset; public float phase; }   // offset: right, up, back from the lead bomber (m)
        readonly List<Member> members = new List<Member>();
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<float> along = new List<float>();
        float distance, clock;
        int shot;
        Vector3 flybyAt;

        void Start()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-strikeshots") >= 0) { pick = 4; Play(); return; }   // the Town Strike self-test (see TurnBattle.StrikeShots)
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-squadshots") >= 0) { pick = 0; Play(); return; }   // the squad controls' self-test (see TurnBattle.SquadShots)
            if (System.Array.IndexOf(args, "-dogfight") >= 0 || System.Array.IndexOf(args, "-dogfightbot") >= 0) { pick = 2; LoadBattle(); return; }   // straight into the one on one (the bot flies it, see TurnBattle.Bot)
            if (cam == null) cam = Camera.main;
            setup.stage.craftPrefabs = craftPrefabs != null && craftPrefabs.Length > 0 ? craftPrefabs : new[] { fighterPrefab };
            if (terrain == null) terrain = Terrain.activeTerrain;
            cam.farClipPlane = 30000f;
            BuildPath();
            BuildFormation();
            StartShot(0);
            int ss = System.Array.IndexOf(args, "-setupshots");   // the dogfight briefing's self-test (see DogfightSetupScreen.Shots)
            if (ss >= 0 && ss + 1 < args.Length) StartCoroutine(setup.Shots(args[ss + 1]));
            LobbyFromCommandLine();   // the online self-test: -nethost or -netjoin
        }

        // ---------- the formation on rails ----------

        void BuildPath()
        {
            // a smooth curve through the route points, with the distance along it
            for (int s = 0; s + 1 < route.Length; s++)
            {
                Vector3 p0 = route[Mathf.Max(0, s - 1)], p1 = route[s], p2 = route[s + 1], p3 = route[Mathf.Min(route.Length - 1, s + 2)];
                for (int k = 0; k < 40; k++)
                {
                    float t = k / 40f;
                    path.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t));
                }
            }
            path.Add(route[route.Length - 1]);
            along.Add(0);
            for (int i = 1; i < path.Count; i++) along.Add(along[i - 1] + Vector3.Distance(path[i - 1], path[i]));
        }

        void BuildFormation()
        {
            var ft = route.Select(p => UnityFrame.Pos(p)).ToArray();
            var battle = Scenarios.Squadron(ft, null, 8, 3, kias);
            // the lead bomber's frame at the start, for each of our aircraft's place in it
            var lead = battle.planes[2];
            Vector3 lp = UnityFrame.Pos(lead.state.pos), lf = UnityFrame.Dir(lead.state.vel);
            lf = new Vector3(lf.x, 0, lf.z).normalized;
            Vector3 lr = Vector3.Cross(Vector3.up, lf);
            var root = new GameObject("Formation").transform;
            for (int i = 0; i < battle.Count; i++)
            {
                if (battle.side[i] != 0) continue;
                bool bomber = battle.routes[i] != null;
                var go = Instantiate(bomber ? bomberPrefab : fighterPrefab, root);
                go.name = (bomber ? "B17 " : "P51 ") + i;
                var view = go.GetComponent<TurnAircraftView>();
                if (view != null) view.Bind(battle.planes[i]);   // the propellers and the pilots follow its throttle and G
                Vector3 d = UnityFrame.Pos(battle.planes[i].state.pos) - lp;
                members.Add(new Member { t = go.transform, offset = new Vector3(Vector3.Dot(d, lr), d.y, -Vector3.Dot(d, lf)) * formationScale, phase = i * 1.7f });
            }
        }

        /// <summary>Where the lead bomber is on the path, which way it's flying, and how much it's banked into the curve.</summary>
        void Lead(float dist, out Vector3 pos, out Vector3 fwd, out float bankDeg)
        {
            dist = Mathf.Repeat(dist, along[along.Count - 1]);
            int i = Mathf.Max(1, along.BinarySearch(dist) is int b && b >= 0 ? b : ~b);
            i = Mathf.Min(i, path.Count - 1);
            float u = Mathf.InverseLerp(along[i - 1], along[i], dist);
            pos = Vector3.Lerp(path[i - 1], path[i], u);
            fwd = (path[i] - path[i - 1]).normalized;
            Vector3 ahead = (path[Mathf.Min(i + 8, path.Count - 1)] - path[Mathf.Min(i + 7, path.Count - 2)]).normalized;
            bankDeg = Mathf.Clamp(Vector3.SignedAngle(new Vector3(fwd.x, 0, fwd.z), new Vector3(ahead.x, 0, ahead.z), Vector3.up) * 6f, -25f, 25f);
        }

        void Update()
        {
            if (along.Count == 0) return;   // skipped straight to the battle (-dogfight): no route was laid out
            float speed = kias * 0.5144f * 1.04f;   // true airspeed at the valley's height, m/s
            distance += speed * Time.deltaTime;
            if (distance > along[along.Count - 1] - 200f) distance = 0;   // round again from the start
            Lead(distance, out var p, out var f, out float bank);
            var flat = new Vector3(f.x, 0, f.z).normalized;
            var r = Vector3.Cross(Vector3.up, flat);
            var rot = Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(0, 0, -bank);
            foreach (var m in members)
            {
                // a gentle bob and weave, as in real close formation
                float t = Time.time + m.phase;
                Vector3 jiggle = new Vector3(Mathf.Sin(t * 0.7f) * 1.5f, Mathf.Sin(t * 0.9f) * 1.2f, Mathf.Sin(t * 0.5f) * 2f);
                m.t.position = p + r * (m.offset.x + jiggle.x) + Vector3.up * (m.offset.y + jiggle.y) - flat * (m.offset.z + jiggle.z);
                m.t.rotation = rot * Quaternion.Euler(Mathf.Sin(t * 0.8f) * 1.5f, 0, Mathf.Sin(t * 0.6f) * 3f);
            }

            clock += Time.deltaTime;
            if (clock > shotSec && !setup.open) StartShot(shot + 1);
            setup.Tick(cam, Time.deltaTime);   // the briefing's airfield: built while it is open, and it holds the camera then
            if (!setup.open) PlaceCamera(p, flat, r);

            if (LobbyUpdate()) return;   // the online panel is open: it takes the keys
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if (setup.open) { SetupKeys(setup.Keys()); return; }   // the dogfight's briefing has the keys while it is up
            if (kb != null && kb.oKey.wasPressedThisFrame) { OpenLobby(); return; }
            if ((kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (pad != null && pad.dpad.left.wasPressedThisFrame)) pick = Menu[(MenuIndex + Menu.Length - 1) % Menu.Length];
            if ((kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (pad != null && pad.dpad.right.wasPressedThisFrame)) pick = Menu[(MenuIndex + 1) % Menu.Length];
            if (pick == 2)
            {
                // the dogfight's sides: up and down yours, page up and down (or the bumpers) theirs (the briefing sets the rest)
                var dog = DogfightSetupScreen.Chosen;
                if ((kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (pad != null && pad.dpad.up.wasPressedThisFrame)) dog.ours = DogfightSetup.Resize(dog.ours, dog.Ours + 1);
                if ((kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (pad != null && pad.dpad.down.wasPressedThisFrame)) dog.ours = DogfightSetup.Resize(dog.ours, dog.Ours - 1);
                if ((kb != null && kb.pageUpKey.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame)) dog.theirs = DogfightSetup.Resize(dog.theirs, dog.Theirs + 1);
                if ((kb != null && kb.pageDownKey.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame)) dog.theirs = DogfightSetup.Resize(dog.theirs, dog.Theirs - 1);
            }
            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))) Play();
            if (kb != null && kb.qKey.wasPressedThisFrame) { pick = 2; DogfightSetupScreen.Chosen = new DogfightSetup(); LoadBattle(); }   // Quick 1v1: P-51 against a Bf 109, a random start
            if (kb != null && kb.jKey.wasPressedThisFrame) PlayJet();
        }

        // ---------- the cinematic camera ----------

        public const int ShotCount = 5;

        public void StartShot(int n)
        {
            shot = n % ShotCount;
            clock = 0;
            Lead(distance, out var p, out var f, out _);
            var flat = new Vector3(f.x, 0, f.z).normalized;
            // the fly-by: a camera standing off the route ahead and to one side, low, that the formation passes
            Lead(distance + kias * 0.5144f * shotSec * 0.55f, out var ahead, out var af, out _);
            var ar = Vector3.Cross(Vector3.up, new Vector3(af.x, 0, af.z).normalized);
            flybyAt = ahead + ar * 110f + Vector3.down * 20f;
        }

        void PlaceCamera(Vector3 p, Vector3 fwd, Vector3 right)
        {
            Vector3 centre = p + (Vector3.up * 90f - fwd * 180f) * formationScale;   // the middle of the whole formation
            float t = clock;
            Vector3 at, look;
            switch (shot)
            {
                case 0: // a slow swing round behind and above
                {
                    float a = Mathf.Lerp(-0.9f, 0.9f, t / shotSec);
                    at = centre - fwd * Mathf.Cos(a) * 260f + right * Mathf.Sin(a) * 260f + Vector3.up * 70f;
                    look = centre;
                    break;
                }
                case 1: // the fly-by
                    at = flybyAt;
                    look = centre;
                    break;
                case 2: // low behind, the whole formation above
                    at = p - fwd * 320f + Vector3.up * (25f + 6f * Mathf.Sin(t * 0.4f)) + right * 30f * Mathf.Sin(t * 0.3f);
                    look = p + fwd * 80f + Vector3.up * 70f;
                    break;
                case 3: // close beside one of the P-51s and its pilot
                {
                    var w = members.FirstOrDefault(m => m.t.name.StartsWith("P51 5")).t ?? members[0].t;
                    at = w.position + w.right * (9f - t * 0.3f) + w.up * 2.2f + w.forward * (6f - t * 0.6f);
                    look = w.position + w.up * 0.6f;
                    break;
                }
                default: // high and wide: the formation small in the valley
                    at = p - fwd * 1300f + Vector3.up * 650f + right * (300f + t * 15f);
                    look = p + fwd * 500f;
                    break;
            }
            // never inside the rock
            if (terrain != null)
            {
                float g = terrain.SampleHeight(at) + terrain.transform.position.y + 25f;
                if (at.y < g) at.y = g;
            }
            cam.transform.position = at;
            cam.transform.LookAt(look);
            cam.nearClipPlane = shot == 3 ? 0.3f : 1f;
            cam.fieldOfView = shot == 3 ? 40f : shot == 4 ? 45f : 55f;
        }

        // ---------- the menu ----------

        public void Play()
        {
            if (pick == 2) { setup.open = true; return; }   // the dogfight opens its briefing first
            LoadBattle();
        }

        /// <summary>Into the turn battle with the picked mission (and the dogfight's setup, DogfightSetupScreen.Chosen).</summary>
        public void LoadBattle()
        {
            ChosenMission = pick;
            ChosenOurs = DogfightSetupScreen.Chosen.Ours; ChosenTheirs = DogfightSetupScreen.Chosen.Theirs;
            SceneManager.LoadScene(battleScene);
        }

        /// <summary>The dogfight's briefing, opened by Play with Dogfight picked.</summary>
        readonly DogfightSetupScreen setup = new DogfightSetupScreen();

        void SetupKeys(DogfightSetupScreen.Result r)
        {
            if (r == DogfightSetupScreen.Result.Back) { setup.open = false; setupOnline = false; }
            else if (r == DogfightSetupScreen.Result.Fly && setupOnline) { DogfightSetupScreen.Save(); setup.open = false; setupOnline = false; StartOnline(); }   // online: the friend gets the same setup
            else if (r == DogfightSetupScreen.Result.Fly) { DogfightSetupScreen.Save(); setup.open = false; LoadBattle(); }
        }

        /// <summary>The jet test, if it has been built into the game.</summary>
        public void PlayJet()
        {
            if (Application.CanStreamedLevelBeLoaded(jetScene)) SceneManager.LoadScene(jetScene);
            else Debug.LogWarning("OODA: no jet test in the build: run OODA > Build Jet Test.");
        }

        GUIStyle title, sub, button, picked, note, hint;
        Texture2D black;

        void OnGUI()
        {
            BriefingStyle.Use();
            if (title == null)
            {
                title = BriefingStyle.Heading(GUI.skin.label, 120);
                title.alignment = TextAnchor.MiddleCenter;
                sub = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
                sub.normal.textColor = BriefingStyle.BrightGreen;
                button = new GUIStyle(GUI.skin.button) { fontSize = 24 };
                picked = new GUIStyle(button) { fontStyle = FontStyle.Bold };
                picked.normal.textColor = picked.hover.textColor = BriefingStyle.Amber;
                note = new GUIStyle(sub) { fontSize = 20 };
                note.normal.textColor = BriefingStyle.Green;
                hint = new GUIStyle(note) { fontSize = 16 };
                black = new Texture2D(1, 1);
                black.SetPixel(0, 0, Color.black);
                black.Apply();
            }
            float sw = Screen.width, sh = Screen.height;
            // a dip to black between the shots
            float fade = Mathf.Max(Mathf.InverseLerp(fadeSec, 0f, clock), Mathf.InverseLerp(shotSec - fadeSec, shotSec, clock));
            if (fade > 0 && !setup.open) { GUI.color = new Color(1, 1, 1, fade); GUI.DrawTexture(new Rect(0, 0, sw, sh), black); GUI.color = Color.white; }

            if (setup.open) { SetupKeys(setup.Draw(sw, sh)); return; }
            Shadowed(new Rect(0, sh * 0.08f, sw, 140), "OODA", title);
            Shadowed(new Rect(0, sh * 0.08f + 130, sw, 40), "Observe  ·  Orient  ·  Decide  ·  Act", sub);
            Shadowed(new Rect(0, sh * 0.08f + 168, sw, 34), "Dogfights over the Alps", note);

            if (lobbyOpen) { LobbyGUI(sw, sh); return; }
            if (GUI.Button(new Rect(24, sh - 72, 212, 48), "Online (O)", button)) OpenLobby();
            float bw = 260, gap = 16, total = Menu.Length * bw + (Menu.Length - 1) * gap;
            float x = (sw - total) / 2, y = sh - 230;
            for (int i = 0; i < Menu.Length; i++)
                if (GUI.Button(new Rect(x + i * (bw + gap), y, bw, 48), (Menu[i] == pick ? "▶ " : "") + MenuNames[i], Menu[i] == pick ? picked : button)) pick = Menu[i];
            var dog = DogfightSetupScreen.Chosen;
            string what = pick != 2 ? SquadronNote
                : $"{dog.Ours} v {dog.Theirs}: {DogfightSetup.Describe(dog.ours)} against {DogfightSetup.Describe(dog.theirs)}. Play to set it up.";
            Shadowed(new Rect(0, y + 56, sw, 30), what, note);
            if (Application.CanStreamedLevelBeLoaded(jetScene) && GUI.Button(new Rect(sw - 236, sh - 72, 212, 48), "Jet Test (J)", button)) PlayJet();
            if (GUI.Button(new Rect((sw - 260) / 2, y + 96, 260, 60), "Play", picked)) Play();
            Shadowed(new Rect(0, y + 162, sw, 26), pick == 2
                ? "Enter for the briefing: sides, aircraft and the start  ·  Q quick 1v1"
                : "Enter, Space or pad A to play  ·  ← → or the d-pad to pick a mission  ·  Q quick 1v1", hint);
        }

        /// <summary>The dogfight's two rows: how many planes on your side, how many on theirs.</summary>
        void SidePicker(float sw, float y, string ourLabel = "Your P-51s", string theirLabel = "Bf 109s")
        {
            float bw = 56, gap = 10, lw = 240, total = lw + 4 * bw + 3 * gap;
            float x = (sw - total) / 2;
            for (int row = 0; row < 2; row++)
            {
                float ry = y + row * 52;
                Shadowed(new Rect(x, ry + 8, lw, 34), row == 0 ? ourLabel : theirLabel, note);
                for (int k = 1; k <= 4; k++)
                {
                    bool on = (row == 0 ? ChosenOurs : ChosenTheirs) == k;
                    if (GUI.Button(new Rect(x + lw + (k - 1) * (bw + gap), ry, bw, 44), k.ToString(), on ? picked : button))
                    { if (row == 0) ChosenOurs = k; else ChosenTheirs = k; }
                }
            }
        }

        static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var c = style.normal.textColor;
            style.normal.textColor = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }
    }
}
