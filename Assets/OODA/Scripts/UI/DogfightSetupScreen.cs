using System;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.UI
{
    /// <summary>
    /// The dogfight's briefing, before the fight: how many planes on each side (1 to 4), the aircraft each one flies
    /// (P-51, Spitfire, Bf 109 or F-4 Phantom, any type on either side), how the two sides start (or at random), and
    /// how far apart, how high and how fast. A map in the middle shows where everyone starts, from above, built from the
    /// same Scenarios.Dogfight the battle uses. The title screen opens it when Dogfight is picked and Play pressed.
    /// What is picked is kept (PlayerPrefs) for next time, and is in Chosen for the turn battle to read.
    /// </summary>
    public class DogfightSetupScreen
    {
        const string PrefsKey = "OODA.DogfightSetup";
        static DogfightSetup chosen;

        /// <summary>The setup the next dogfight flies: the last one picked here, kept between runs.</summary>
        public static DogfightSetup Chosen
        {
            get
            {
                if (chosen != null) return chosen;
                chosen = new DogfightSetup();
                try
                {
                    string saved = PlayerPrefs.GetString(PrefsKey, "");
                    if (saved.Length > 0) chosen = DogfightSetup.FromBytes(Convert.FromBase64String(saved));
                }
                catch (Exception e) { Debug.LogWarning($"OODA: the saved dogfight setup could not be read ({e.Message}); starting from the usual one."); }
                return chosen;
            }
            set => chosen = value;
        }

        /// <summary>Keep the setup for the next run of the game.</summary>
        public static void Save()
        {
            PlayerPrefs.SetString(PrefsKey, Convert.ToBase64String(Chosen.ToBytes()));
            PlayerPrefs.Save();
        }

        public enum Result { None, Back, Fly }

        public bool open;

        static readonly Craft[] Types = { Craft.Mustang, Craft.P38, Craft.Spitfire, Craft.Bf109, Craft.Phantom };
        static readonly Color Blue = new Color(0.4f, 0.75f, 1f), Red = new Color(1f, 0.4f, 0.35f);

        GUIStyle heading, head, label, dim, button, picked, small, smallPicked, bigFly;
        Texture2D white;
        string mapKey;      // the setup the map was laid out for
        Battle mapBattle;   // and where it puts everyone

        /// <summary>Enter or pad A flies, Esc or pad B goes back. Call from Update while open.</summary>
        public Result Keys()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) return Result.Back;
            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))) return Result.Fly;
            return Result.None;
        }

        void Styles()
        {
            if (heading != null) return;
            heading = BriefingStyle.Heading(GUI.skin.label, 52);
            heading.alignment = TextAnchor.MiddleCenter;
            head = BriefingStyle.Heading(GUI.skin.label, 26);
            label = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            dim = new GUIStyle(label) { fontSize = 15 };
            dim.normal.textColor = BriefingStyle.DimGreen;
            button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            picked = new GUIStyle(button) { fontStyle = FontStyle.Bold };
            picked.normal.textColor = picked.hover.textColor = BriefingStyle.Amber;
            small = new GUIStyle(button) { fontSize = 15 };
            smallPicked = new GUIStyle(small) { fontStyle = FontStyle.Bold };
            smallPicked.normal.textColor = smallPicked.hover.textColor = BriefingStyle.Amber;
            bigFly = new GUIStyle(picked) { fontSize = 26 };
            white = Texture2D.whiteTexture;
        }

        /// <summary>Draw the screen (inside OnGUI, after BriefingStyle.Use). Returns Back or Fly when one is pressed.</summary>
        public Result Draw(float sw, float sh)
        {
            Styles();
            var s = Chosen;
            var result = Result.None;
            // laid out for 1280 x 780 and scaled down to fit smaller windows
            float k = Mathf.Min(1f, sw / 1280f, sh / 780f);
            var keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1)) * keep;
            float w = sw / k, h = sh / k;
            float pw = 1240, ph = 760, px = (w - pw) / 2, py = (h - ph) / 2;
            GUI.Box(new Rect(px, py, pw, ph), "");
            GUI.Label(new Rect(px, py + 8, pw, 60), "DOGFIGHT BRIEFING", heading);

            // the two flights either side, the map between them
            float colW = 380, top = py + 80;
            s.FitPilots();
            Side(new Rect(px + 20, top, colW, 330), "YOUR FLIGHT", Blue, ref s.ours, s.oursPilots, true);
            Side(new Rect(px + pw - 20 - colW, top, colW, 330), "ENEMY FLIGHT", Red, ref s.theirs, s.theirsPilots, false);
            s.FitPilots();
            Map(new Rect(px + 20 + colW + 20, top + 6, pw - 2 * (colW + 40), 320), s);

            // how they start
            float y = top + 344;
            GUI.Label(new Rect(px + 20, y, 400, 32), "THE START", head);
            y += 38;
            float bw = (pw - 40 - 7 * 10) / 8;
            for (int i = 0; i < 8; i++)
            {
                bool on = i == 0 ? s.randomStart : !s.randomStart && (int)s.start == i - 1;
                string text = i == 0 ? "Random" : Scenarios.Title((Scenarios.Start)(i - 1));
                if (GUI.Button(new Rect(px + 20 + i * (bw + 10), y, bw, 44), text, on ? smallPicked : small))
                {
                    if (i == 0) s.randomStart = true;
                    else { s.randomStart = false; s.start = (Scenarios.Start)(i - 1); }
                }
            }
            y += 52;
            GUI.Label(new Rect(px + 20, y, pw - 40, 26), s.randomStart ? "One of the seven at random, a new one each time you fly again." : Explain(s.start), dim);

            // range, height, speed
            y += 40;
            float gw = (pw - 40 - 2 * 30) / 3;
            int r = (int)s.range, hgt = (int)s.height, sp = (int)s.speed;
            Choice(new Rect(px + 20, y, gw, 44), "RANGE", new[] { "Close", "Medium", "Far" }, ref r);
            Choice(new Rect(px + 20 + gw + 30, y, gw, 44), "HEIGHT", new[] { "Low", "Medium", "High" }, ref hgt);
            Choice(new Rect(px + 20 + 2 * (gw + 30), y, gw, 44), "SPEED", new[] { "220 kt", "300 kt", "380 kt" }, ref sp);
            s.range = (DogfightSetup.Range)r; s.height = (DogfightSetup.Height)hgt; s.speed = (DogfightSetup.Pace)sp;

            // back, the summary, fly
            float by = py + ph - 76;
            if (GUI.Button(new Rect(px + 20, by, 200, 56), "Back (Esc)", button)) result = Result.Back;
            GUI.Label(new Rect(px + 240, by, pw - 520, 56), $"{s.Ours} v {s.Theirs}: {DogfightSetup.Describe(s.ours)} against {DogfightSetup.Describe(s.theirs)}. Click a pilot to change who flies.",
                      new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, wordWrap = true });
            if (GUI.Button(new Rect(px + pw - 260, by, 240, 56), "FLY (Enter)", bigFly)) result = Result.Fly;

            GUI.matrix = keep;
            return result;
        }

        static string Explain(Scenarios.Start st) =>
            st == Scenarios.Start.HeadOn ? "Nose to nose, coming straight at each other, a little off to one side so holding straight is a pass."
          : st == Scenarios.Start.Neutral ? "Side by side and level, flying the same way: neither has the advantage."
          : st == Scenarios.Start.Offensive ? "You behind them, a little to one side: the shot is yours to take."
          : st == Scenarios.Start.Defensive ? "Them behind you: break, or be shot."
          : st == Scenarios.Start.Crossing ? "Them coming in from your right at right angles: who turns first?"
          : st == Scenarios.Start.Perch ? "You high and behind them, out to one side: dive in on them."
          : "Them high and behind you, out to one side: they are diving in.";

        /// <summary>A flight's column: how many, each plane's aircraft (the arrows, or a click on the name, step it) and its pilot
        /// (a click steps it: the type's usual one, the tiger, the wolf, the wizard, the skeleton), and every plane's aircraft at once.</summary>
        void Side(Rect r, string title, Color col, ref Craft[] side, PilotKind[] pilots, bool ours)
        {
            var t = new GUIStyle(head);
            t.normal.textColor = col;
            GUI.Label(new Rect(r.x, r.y, r.width, 32), title, t);
            float y = r.y + 40;
            GUI.Label(new Rect(r.x, y, 110, 40), "Planes", label);
            for (int n = 1; n <= Scenarios.MaxSide; n++)
                if (GUI.Button(new Rect(r.x + 110 + (n - 1) * 62, y, 54, 40), n.ToString(), side.Length == n ? picked : button))
                    side = DogfightSetup.Resize(side, n);
            y += 54;
            for (int i = 0; i < side.Length; i++)
            {
                string who = i == 0 ? (ours ? "You" : "Lead") : $"#{i + 1}";
                GUI.Label(new Rect(r.x, y, 70, 38), who, label);
                float pilotW = 100, nameW = r.width - 162 - pilotW - 6;
                if (GUI.Button(new Rect(r.x + 70, y, 40, 38), "<", button)) side[i] = Step(side[i], -1);
                if (GUI.Button(new Rect(r.x + 116, y, nameW, 38), DogfightSetup.Short(side[i]), button)) side[i] = Step(side[i], 1);
                if (GUI.Button(new Rect(r.x + 122 + nameW, y, 40, 38), ">", button)) side[i] = Step(side[i], 1);
                if (i < pilots.Length)
                {
                    // the F-4 keeps its crew: its pilot button shows Crew and doesn't step
                    bool crew = side[i] == Craft.Phantom;
                    string who2 = crew ? "Crew" : DogfightSetup.Name(pilots[i]);
                    if (GUI.Button(new Rect(r.x + r.width - pilotW, y, pilotW, 38), who2, crew || pilots[i] == PilotKind.Usual ? small : smallPicked) && !crew)
                        pilots[i] = (PilotKind)(((int)pilots[i] + 1) % PilotCount);
                }
                y += 44;
            }
            y = r.y + 40 + 54 + Scenarios.MaxSide * 44 + 6;
            GUI.Label(new Rect(r.x, y, 70, 36), "All", dim);
            float bw = (r.width - 70 - (Types.Length - 1) * 6) / Types.Length;
            for (int j = 0; j < Types.Length; j++)
            {
                var kind = Types[j];
                bool all = true;
                foreach (var c in side) if (c != kind) all = false;
                if (GUI.Button(new Rect(r.x + 70 + j * (bw + 6), y, bw, 36), Types[j] == Craft.Spitfire ? "Spit" : DogfightSetup.Short(Types[j]), all ? smallPicked : small))   // five across: "Spitfire" won't fit
                    for (int i = 0; i < side.Length; i++) side[i] = Types[j];
            }
        }

        static readonly int PilotCount = Enum.GetValues(typeof(PilotKind)).Length;

        static Craft Step(Craft c, int by) => Types[(Array.IndexOf(Types, c) + by + Types.Length) % Types.Length];

        void Choice(Rect r, string title, string[] names, ref int pick)
        {
            GUI.Label(new Rect(r.x, r.y, 100, r.height), title, label);
            float bw = (r.width - 100 - (names.Length - 1) * 6) / names.Length;
            for (int i = 0; i < names.Length; i++)
                if (GUI.Button(new Rect(r.x + 100 + i * (bw + 6), r.y, bw, r.height), names[i], i == pick ? smallPicked : small)) pick = i;
        }

        // ---------- the self-test: -setupshots <folder> ----------

        /// <summary>
        /// With -setupshots folder on the command line: open the briefing on two setups (2 v 2 Spitfire and F-4 against Bf 109s on the
        /// perch; 4 v 3 mixed, head-on, close, low), save each as a BMP there, and quit. Doesn't touch the setup kept for next time.
        /// </summary>
        public System.Collections.IEnumerator Shots(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            var keep = chosen;
            chosen = new DogfightSetup { ours = new[] { Craft.Spitfire, Craft.Phantom }, theirs = new[] { Craft.Bf109, Craft.Bf109 }, randomStart = false, start = Scenarios.Start.Perch,
                                      oursPilots = new[] { PilotKind.Wizard, PilotKind.Usual }, theirsPilots = new[] { PilotKind.Skeleton, PilotKind.Usual } };
            open = true;
            yield return new WaitForSecondsRealtime(2f);
            yield return Grab(System.IO.Path.Combine(folder, "setup_perch.bmp"));
            chosen = new DogfightSetup
            {
                ours = new[] { Craft.Mustang, Craft.Spitfire, Craft.Mustang, Craft.Phantom }, theirs = new[] { Craft.Bf109, Craft.Mustang, Craft.Phantom },
                randomStart = false, start = Scenarios.Start.HeadOn, range = DogfightSetup.Range.Close, height = DogfightSetup.Height.Low, speed = DogfightSetup.Pace.Fast,
            };
            yield return new WaitForSecondsRealtime(1f);
            yield return Grab(System.IO.Path.Combine(folder, "setup_headon.bmp"));
            chosen = keep;
            Application.Quit();
        }

        static System.Collections.IEnumerator Grab(string path)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, Ooda.Turn.TurnBattle.Bmp(tex));
            UnityEngine.Object.Destroy(tex);
        }

        // ---------- the map ----------

        /// <summary>Where everyone starts, from above, north up: a little aircraft for each, its height beside the leads when they differ.</summary>
        void Map(Rect r, DogfightSetup s)
        {
            GUI.Box(r, "");
            var show = s.Copy();
            string caption;
            if (s.randomStart)
            {
                // a random start: step through the seven so you see what it might be
                int n = (int)(Time.unscaledTime / 1.6f) % Scenarios.StartCount;
                show.randomStart = false; show.start = (Scenarios.Start)n;
                caption = $"Random: maybe {Scenarios.Title(show.start).ToLowerInvariant()}";
            }
            else caption = Scenarios.Title(s.start);
            string key = Convert.ToBase64String(show.ToBytes());
            if (key != mapKey)
            {
                mapKey = key;
                try { mapBattle = Scenarios.Dogfight(show, new Vec3d(0, 0, 0), 3600, 300, 3000); }
                catch (Exception e) { mapBattle = null; Debug.LogException(e); }
            }
            var b = mapBattle;
            if (b == null) return;

            GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 20, 26), caption, new GUIStyle(dim) { alignment = TextAnchor.MiddleCenter });
            var inner = new Rect(r.x + 24, r.y + 36, r.width - 48, r.height - 70);
            // fit everyone in, with room for each aircraft's nose
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < b.Count; i++)
            {
                var p = b.planes[i].state.pos;
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
            }
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            double span = Math.Max(Math.Max(maxX - minX, maxY - minY), 2500) * 1.25;
            float ppf = (float)(Math.Min(inner.width, inner.height) / span);   // pixels per foot
            Vector2 At(Vec3d p) => new Vector2(inner.center.x + (float)(p.x - cx) * ppf, inner.center.y - (float)(p.y - cy) * ppf);

            // a 1 km scale bar and N
            float km = 3280.84f * ppf;
            var bar = new Color(0.4f, 0.75f, 0.4f, 0.8f);
            Rule(new Vector2(r.x + 16, r.yMax - 16), new Vector2(r.x + 16 + km, r.yMax - 16), 2, bar);
            GUI.Label(new Rect(r.x + 20 + km, r.yMax - 30, 60, 24), "1 km", dim);
            GUI.Label(new Rect(r.xMax - 34, r.y + 30, 24, 24), "N", dim);
            Rule(new Vector2(r.xMax - 22, r.y + 70), new Vector2(r.xMax - 22, r.y + 54), 2, bar);

            double myAlt = b.planes[0].state.pos.z, theirAlt = b.planes[1].state.pos.z;
            for (int i = 0; i < b.Count; i++)
            {
                var m = b.planes[i];
                var col = b.side[i] == 0 ? Blue : Red;
                Vector2 at = At(m.state.pos);
                float hdg = Mathf.Atan2((float)m.state.vel.x, (float)m.state.vel.y) * Mathf.Rad2Deg;   // clockwise from north
                DrawPlane(at, hdg, i < 2 ? 1.25f : 1f, col);
                if (i < 2)
                {
                    string tag = i == 0 ? "YOU" : "THEM";
                    double up = (i == 0 ? myAlt - theirAlt : theirAlt - myAlt);
                    if (up > 300) tag += $" +{up:N0} ft";
                    var st = new GUIStyle(dim) { fontStyle = FontStyle.Bold };
                    st.normal.textColor = col;
                    GUI.Label(new Rect(at.x + 14, at.y - 12, 160, 24), tag, st);
                }
            }
        }

        /// <summary>A little aircraft from above at a GUI point, heading degrees clockwise from up: fuselage, wings and tail.</summary>
        void DrawPlane(Vector2 at, float hdg, float size, Color col)
        {
            var keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(hdg, at);
            var c = GUI.color;
            GUI.color = col;
            float L = 26 * size, W = 22 * size;
            GUI.DrawTexture(new Rect(at.x - 2 * size, at.y - L / 2, 4 * size, L), white);               // fuselage, nose up
            GUI.DrawTexture(new Rect(at.x - W / 2, at.y - L * 0.12f, W, 4 * size), white);             // wings
            GUI.DrawTexture(new Rect(at.x - W * 0.22f, at.y + L * 0.36f, W * 0.44f, 3 * size), white); // tail
            GUI.color = c;
            GUI.matrix = keep;
        }

        /// <summary>A straight line between two GUI points.</summary>
        void Rule(Vector2 a, Vector2 b, float width, Color col)
        {
            var keep = GUI.matrix;
            var d = b - a;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(ang, a);
            var c = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2, d.magnitude, width), white);
            GUI.color = c;
            GUI.matrix = keep;
        }
    }
}
