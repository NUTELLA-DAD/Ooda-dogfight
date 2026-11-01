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
    /// shots every few seconds, and the title with a mission picker. Enter, Space or pad A starts the chosen mission in
    /// the turn battle, which reads it from ChosenMission.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        /// <summary>The mission picked here (TurnBattle.Mission's order: Squadron, Escort, Dogfight), for the battle to read.</summary>
        public static int? ChosenMission;

        public GameObject fighterPrefab, bomberPrefab;
        [Tooltip("The route the formation flies (metres, x east, y height, z north): the escort route up the valley.")]
        public Vector3[] route;
        public Camera cam;
        public Terrain terrain;
        public string battleScene = "TurnBattle";
        public float kias = 135;
        public float shotSec = 7f, fadeSec = 0.6f;
        [Tooltip("The squadron's spacing, scaled down for the camera: tighter than the battle flies it.")]
        public float formationScale = 0.65f;

        static readonly string[] Missions = { "Squadron", "Escort", "Dogfight" };
        static readonly string[] MissionNotes =
        {
            "8 P-51s and 3 B-17s against 8 Bf 109s and the flak",
            "you and one B-17 against a Bf 109",
            "you against a Bf 109, head-on",
        };
        int pick;

        struct Member { public Transform t; public Vector3 offset; public float phase; }   // offset: right, up, back from the lead bomber (m)
        readonly List<Member> members = new List<Member>();
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<float> along = new List<float>();
        float distance, clock;
        int shot;
        Vector3 flybyAt;

        void Start()
        {
            if (cam == null) cam = Camera.main;
            if (terrain == null) terrain = Terrain.activeTerrain;
            cam.farClipPlane = 30000f;
            BuildPath();
            BuildFormation();
            StartShot(0);
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
            if (clock > shotSec) StartShot(shot + 1);
            PlaceCamera(p, flat, r);

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if ((kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (pad != null && pad.dpad.left.wasPressedThisFrame)) pick = (pick + Missions.Length - 1) % Missions.Length;
            if ((kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (pad != null && pad.dpad.right.wasPressedThisFrame)) pick = (pick + 1) % Missions.Length;
            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))) Play();
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
            ChosenMission = pick;
            SceneManager.LoadScene(battleScene);
        }

        GUIStyle title, sub, button, picked, note, hint;
        Texture2D black;

        void OnGUI()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 120, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                title.normal.textColor = Color.white;
                sub = new GUIStyle(title) { fontSize = 26, fontStyle = FontStyle.Italic };
                button = new GUIStyle(GUI.skin.button) { fontSize = 24 };
                picked = new GUIStyle(button) { fontStyle = FontStyle.Bold };
                picked.normal.textColor = picked.hover.textColor = new Color(1f, 0.85f, 0.2f);
                note = new GUIStyle(sub) { fontSize = 20, fontStyle = FontStyle.Normal };
                hint = new GUIStyle(note) { fontSize = 16 };
                black = new Texture2D(1, 1);
                black.SetPixel(0, 0, Color.black);
                black.Apply();
            }
            float sw = Screen.width, sh = Screen.height;
            // a dip to black between the shots
            float fade = Mathf.Max(Mathf.InverseLerp(fadeSec, 0f, clock), Mathf.InverseLerp(shotSec - fadeSec, shotSec, clock));
            if (fade > 0) { GUI.color = new Color(1, 1, 1, fade); GUI.DrawTexture(new Rect(0, 0, sw, sh), black); GUI.color = Color.white; }

            Shadowed(new Rect(0, sh * 0.08f, sw, 140), "OODA", title);
            Shadowed(new Rect(0, sh * 0.08f + 130, sw, 40), "Observe  ·  Orient  ·  Decide  ·  Act", sub);
            Shadowed(new Rect(0, sh * 0.08f + 168, sw, 34), "Dogfights over the Alps", note);

            float bw = 190, gap = 16, total = Missions.Length * bw + (Missions.Length - 1) * gap;
            float x = (sw - total) / 2, y = sh - 230;
            for (int i = 0; i < Missions.Length; i++)
                if (GUI.Button(new Rect(x + i * (bw + gap), y, bw, 48), (i == pick ? "▶ " : "") + Missions[i], i == pick ? picked : button)) pick = i;
            Shadowed(new Rect(0, y + 56, sw, 30), MissionNotes[pick], note);
            if (GUI.Button(new Rect((sw - 260) / 2, y + 96, 260, 60), "Play", picked)) Play();
            Shadowed(new Rect(0, y + 162, sw, 26), "Enter, Space or pad A to play  ·  ← → or the d-pad to pick a mission", hint);
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
