using System;
using System.Collections;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.UI
{
    /// <summary>
    /// The mission briefing, after the squadron briefing's Ready: your flight gathered round a map table on the apron
    /// (BriefingStage.ShowTable), the island drawn on it as a painted map from the terrain itself with the city out at sea
    /// to the east, a numbered pin at each of the Alpine Campaign's missions (Campaign.All) and a dotted route joining them.
    /// The missions are listed top left, the picked one's name, objective, wingmen, bandits and difficulty on the right.
    /// Launch Mission flies the dogfight over the picked mission's arena with the flight and the start picked before;
    /// Back returns to the squadron briefing. ↑ ↓ (or 1 to 5, or a click on a pin) pick, Enter launches, Esc goes back.
    /// </summary>
    public class MissionSelectScreen
    {
        public enum Result { None, Back, Launch }

        public bool open;
        public int picked;

        BriefingStage stage;
        float t;

        // the map texture: the island on the left, the city's island on the right (the sea between them shortened)
        const int TexW = 1024, TexH = 640;
        const float IslandPx0 = 40, IslandPy0 = 24, IslandSizePx = 592;   // the island's 20 km square, pixels
        const float IslandHalfM = 10000f;
        const float Scale = IslandSizePx / (2 * IslandHalfM);              // pixels a metre, the same for both
        const float CityCx = 838, CityCy = 320;                         // where the city's middle is drawn
        const float BreakPx = 690;                                      // the cut in the sea between them
        static Texture2D map;

        /// <summary>Open on the stage the squadron briefing stands on: the table goes up, the pick starts on the setup's arena.</summary>
        public void Open(BriefingStage on)
        {
            stage = on;
            open = true;
            var s = DogfightSetupScreen.Chosen;
            int m = s.randomArena ? -1 : Campaign.OfArena(s.arena);
            picked = m >= 0 ? m : 0;
            if (map == null) map = Paint();
            var uv = new Vector2[Campaign.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = MissionUv(i);
            stage.ShowTable(map, uv);
        }

        public void Close()
        {
            open = false;
            if (stage != null) stage.HideTable();
        }

        /// <summary>Fix the picked mission's arena in the setup (and keep it for next time).</summary>
        public void Commit()
        {
            var s = DogfightSetupScreen.Chosen;
            s.randomArena = false;
            s.arena = Campaign.Get(picked).arena;
            DogfightSetupScreen.Save();
        }

        void Pick(int i) => picked = (i % Campaign.Count + Campaign.Count) % Campaign.Count;

        /// <summary>Keys and the pad, from Update while open.</summary>
        public Result Keys()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool Kb(Func<Keyboard, UnityEngine.InputSystem.Controls.ButtonControl> k) => kb != null && k(kb).wasPressedThisFrame;
            bool Pad(Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> b) => pad != null && b(pad).wasPressedThisFrame;
            if (Kb(k => k.escapeKey) || Kb(k => k.backspaceKey) || Pad(p => p.buttonEast)) return Result.Back;
            if (Kb(k => k.enterKey) || Kb(k => k.numpadEnterKey) || Pad(p => p.buttonSouth) || Pad(p => p.startButton)) return Result.Launch;
            if (Kb(k => k.upArrowKey) || Kb(k => k.wKey) || Kb(k => k.leftArrowKey) || Kb(k => k.aKey) || Pad(p => p.dpad.up) || Pad(p => p.dpad.left)) Pick(picked - 1);
            if (Kb(k => k.downArrowKey) || Kb(k => k.sKey) || Kb(k => k.rightArrowKey) || Kb(k => k.dKey) || Pad(p => p.dpad.down) || Pad(p => p.dpad.right)) Pick(picked + 1);
            if (kb != null)
                for (int i = 0; i < Campaign.Count && i < 9; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) Pick(i);
            return Result.None;
        }

        /// <summary>Every frame while open: the pins and the camera.</summary>
        public void Tick(float dt)
        {
            t += dt;
            if (stage == null) return;
            stage.TablePins(picked, t);
        }

        // ---------- drawing ----------

        static readonly Color Olive = new Color(0.25f, 0.27f, 0.16f), OliveDark = new Color(0.13f, 0.14f, 0.08f), Cream = new Color(0.95f, 0.91f, 0.78f),
                              Ink = new Color(0.17f, 0.18f, 0.10f), Gold = new Color(0.98f, 0.78f, 0.22f), Brick = new Color(0.62f, 0.10f, 0.08f),
                              Brown = new Color(0.30f, 0.18f, 0.06f);

        GUIStyle overLine, title, campaign, item, itemOn, panelTitle, panelText, rowLabel, rowValue, back, launch, badge, badgeOn, hint;
        Texture2D white, shade, panel, disc, discOn;

        void Styles()
        {
            if (title != null) return;
            white = Texture2D.whiteTexture;
            GUIStyle Stencil(int size, Color ink, Texture2D bg = null, Texture2D hot = null, TextAnchor align = TextAnchor.MiddleCenter)
            {
                var st = new GUIStyle(GUI.skin.label) { font = BriefingStyle.Stencil, fontSize = size, alignment = align, wordWrap = false, clipping = TextClipping.Overflow };
                st.normal.textColor = st.hover.textColor = st.active.textColor = ink;
                if (bg != null) { st.normal.background = bg; st.border = new RectOffset(6, 6, 6, 6); st.padding = new RectOffset(14, 10, 2, 2); }
                if (hot != null) { st.hover.background = hot; st.active.background = hot; }
                return st;
            }
            var olive = Plate(new Color(0.30f, 0.32f, 0.20f), new Color(0.85f, 0.80f, 0.62f), new Color(0.18f, 0.19f, 0.11f));
            var oliveHot = Plate(new Color(0.38f, 0.41f, 0.25f), Gold, new Color(0.18f, 0.19f, 0.11f));
            var gold = Plate(Gold, new Color(0.45f, 0.30f, 0.05f), new Color(1f, 0.92f, 0.55f));
            var goldHot = Plate(new Color(1f, 0.86f, 0.38f), new Color(0.45f, 0.30f, 0.05f), Color.white);
            panel = Plate(new Color(0.20f, 0.22f, 0.13f, 0.94f), new Color(0.80f, 0.74f, 0.55f), new Color(0.10f, 0.11f, 0.06f));
            title = Stencil(52, Cream, align: TextAnchor.MiddleLeft);
            campaign = Stencil(22, Cream, align: TextAnchor.MiddleCenter);
            item = Stencil(22, Cream, olive, oliveHot, TextAnchor.MiddleLeft);
            itemOn = Stencil(24, Brown, gold, goldHot, TextAnchor.MiddleLeft);
            panelTitle = Stencil(28, Cream);
            panelText = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, alignment = TextAnchor.UpperCenter };
            panelText.normal.textColor = Cream;
            rowLabel = Stencil(17, Cream, olive, null, TextAnchor.MiddleLeft);
            rowValue = Stencil(20, Gold, align: TextAnchor.MiddleRight);
            overLine = Stencil(16, Gold);
            back = Stencil(28, Cream, olive, oliveHot);
            launch = Stencil(40, Brown, gold, goldHot);
            badge = Stencil(15, Ink);
            badgeOn = Stencil(19, Cream);
            hint = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            hint.normal.textColor = new Color(0.92f, 0.88f, 0.74f);
            shade = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            shade.SetPixel(0, 0, new Color(0.06f, 0.05f, 0.03f, 0.6f));
            shade.Apply();
            disc = Disc(Cream, Brown);
            discOn = Disc(Brick, Gold);
        }

        /// <summary>Draw it (inside OnGUI, after BriefingStyle.Use). Returns Back or Launch when one is pressed.</summary>
        public Result Draw(float sw, float sh)
        {
            Styles();
            var result = Result.None;
            float k = Mathf.Min(sw / 1280f, sh / 720f);
            var keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1)) * keep;
            float w = sw / k, h = sh / k;
            var s = DogfightSetupScreen.Chosen;
            var m = Campaign.Get(picked);

            // a dark wash down the left behind the heading and the list
            for (int i = 0; i < 24; i++) Fill(new Rect(i * 16, 0, 16, h), new Color(0.05f, 0.04f, 0.02f, 0.5f * (1f - i / 24f)));
            // and behind the heading itself, so the parked planes don't show through it
            for (int i = 0; i < 14; i++) Fill(new Rect(0, i * 8, 560, 8), new Color(0.05f, 0.04f, 0.02f, 0.45f * (1f - i / 14f)));

            // the heading
            Shadow(new Rect(28, 14, 600, 64), "MISSION BRIEFING", title);
            Fill(new Rect(36, 86, 50, 3), Cream);
            Shadow(new Rect(96, 72, 240, 30), Campaign.Name.ToUpperInvariant(), campaign);
            Fill(new Rect(346, 86, 50, 3), Cream);

            // the list
            for (int i = 0; i < Campaign.Count; i++)
            {
                var r = new Rect(28 + (i == picked ? 6 : 0), 116 + i * 54, 330, 46);
                if (GUI.Button(r, $"{i + 1:00}   {Campaign.Get(i).title}", i == picked ? itemOn : item)) Pick(i);
            }

            // a numbered badge over each pin, where the camera sees it; a click on one picks it. Where pins stand close
            // (01 and 02), a badge that would sit under the picked one is pushed clear of it, with a line down to its pin.
            if (stage != null && stage.TableUp)
            {
                int n = Campaign.Count;
                var pinAt = new Vector2[n];
                var badgeAt = new Vector2[n];
                var seen = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 sp = stage.OnScreen(stage.PinTop(i));
                    seen[i] = sp.z > 0;
                    pinAt[i] = new Vector2(sp.x / k, sp.y / k);
                    badgeAt[i] = pinAt[i] + Vector2.down * (i == picked ? 21 : 15);   // the badge's middle, just over the pin
                }
                for (int pass = 0; pass < 3; pass++)
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                        {
                            if (i == j || i == picked || !seen[i] || !seen[j]) continue;
                            if (j != picked && j > i) continue;   // the picked one stays put; of two others the later gives way
                            float need = (i == picked ? 21 : 15) + (j == picked ? 21 : 15) + 4;
                            Vector2 d = badgeAt[i] - badgeAt[j];
                            if (d.magnitude >= need) continue;
                            if (d.sqrMagnitude < 1f) d = new Vector2(-1, -1);
                            badgeAt[i] = badgeAt[j] + d.normalized * need;
                        }
                for (int order = 0; order < n; order++)
                {
                    int i = (picked + 1 + order) % n;   // the picked one last, on top
                    if (!seen[i]) continue;
                    bool on = i == picked;
                    float r = on ? 21 : 15;
                    var at = new Rect(badgeAt[i].x - r, badgeAt[i].y - r, 2 * r, 2 * r);
                    Rule(pinAt[i], badgeAt[i], 2.5f, on ? Gold : Brown);
                    GUI.DrawTexture(at, on ? discOn : disc);
                    if (GUI.Button(at, $"{i + 1:00}", on ? badgeOn : badge)) Pick(i);
                }
            }

            // the picked mission
            var pr = new Rect(w - 300, 160, 284, 280);   // narrow and high, clear of the city and its pin
            GUI.DrawTexture(pr, panel);
            Shadow(new Rect(pr.x, pr.y + 12, pr.width, 42), m.title, panelTitle);
            Fill(new Rect(pr.x + 24, pr.y + 58, pr.width - 48, 2), new Color(0.85f, 0.80f, 0.62f, 0.7f));
            GUI.Label(new Rect(pr.x + 14, pr.y + 64, pr.width - 28, 46), m.objective, panelText);
            Shadow(new Rect(pr.x, pr.y + 112, pr.width, 24), "OVER " + Arenas.Get(m.arena).name.ToUpperInvariant(), overLine);
            float ry = pr.y + 142;
            Row(new Rect(pr.x + 18, ry, pr.width - 36, 32), "WINGMEN", (s.Ours - 1).ToString()); ry += 37;
            Row(new Rect(pr.x + 18, ry, pr.width - 36, 32), "BANDITS", s.Theirs.ToString()); ry += 37;
            Row(new Rect(pr.x + 18, ry, pr.width - 36, 32), "DIFFICULTY", Campaign.Difficulty(m.difficulty));

            // back and launch
            if (GUI.Button(new Rect(28, h - 92, 200, 64), "< BACK", back)) result = Result.Back;
            if (GUI.Button(new Rect(w - 420, h - 104, 392, 84), "LAUNCH MISSION", launch)) result = Result.Launch;
            GUI.DrawTexture(new Rect(w / 2 - 260, h - 30, 520, 24), shade);
            GUI.Label(new Rect(w / 2 - 260, h - 30, 520, 24), "↑ ↓ or a pin to pick  ·  Enter launch  ·  Esc back", hint);

            GUI.matrix = keep;
            return result;
        }

        void Row(Rect r, string label, string value)
        {
            GUI.Label(r, label, rowLabel);
            Shadow(new Rect(r.x, r.y, r.width - 12, r.height), value, rowValue);
        }

        /// <summary>A straight line between two GUI points (under the briefing's scaled GUI.matrix).</summary>
        void Rule(Vector2 a, Vector2 b, float width, Color col)
        {
            var d = b - a;
            if (d.sqrMagnitude < 0.01f) return;
            var keep = GUI.matrix;
            GUI.matrix = keep * Matrix4x4.TRS(a, Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg), Vector3.one) * Matrix4x4.Translate(-a);
            Fill(new Rect(a.x, a.y - width / 2, d.magnitude, width), col);
            GUI.matrix = keep;
        }

        void Fill(Rect r, Color c)
        {
            var keep = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = keep;
        }

        static void Shadow(Rect r, string text, GUIStyle st)
        {
            var c = st.normal.textColor;
            st.normal.textColor = new Color(0, 0, 0, 0.75f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
            st.normal.textColor = c;
            GUI.Label(r, text, st);
        }

        /// <summary>A 16 x 16 nine-slice plate: a fill, a 2-pixel rim, and a 1-pixel bevel line just inside it.</summary>
        static Texture2D Plate(Color fill, Color rim, Color bevel)
        {
            const int N = 16;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int e = Mathf.Min(Mathf.Min(x, y), Mathf.Min(N - 1 - x, N - 1 - y));
                    bool corner = (x < 1 || x > N - 2) && (y < 1 || y > N - 2);
                    var c = corner ? Color.clear : e < 2 ? rim : e == 2 ? bevel : fill;
                    if (e > 2) c = Color.Lerp(c, Color.black, 0.10f * (1f - (float)y / N));
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        /// <summary>A round badge: a fill in a rim, soft at the edge.</summary>
        static Texture2D Disc(Color fill, Color rim)
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                    Color c = r < 0.8f ? fill : rim;
                    c.a = Mathf.Clamp01((1f - r) * 24f);
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        // ---------- the map ----------

        /// <summary>Where mission i's pin stands on the map texture, 0 to 1 (v north).</summary>
        static Vector2 MissionUv(int i)
        {
            var a = Arenas.Get(Campaign.Get(i).arena);
            var p = Arenas.IsCity(Campaign.Get(i).arena) ? CityPx((float)a.x, (float)a.z) : IslandPx((float)a.x, (float)a.z);
            return new Vector2(p.x / TexW, p.y / TexH);
        }

        static Vector2 IslandPx(float x, float z) => new Vector2(IslandPx0 + (x + IslandHalfM) * Scale, IslandPy0 + (z + IslandHalfM) * Scale);
        static Vector2 CityPx(float x, float z) => new Vector2(CityCx + (x - City.CentreX) * Scale, CityCy + (z - City.CentreZ) * Scale);

        // the island's two lakes (as AlpineLayout lays them out: centre, half-axes, level, metres)
        static readonly (float x, float z, float rx, float rz, float level)[] Lakes = { (0, -1750, 340, 880, 175), (4350, 5820, 240, 150, 1098) };

        /// <summary>
        /// The island painted as an old campaign map, from the terrain's own heights: a teal sea with paler shallows and an inked
        /// coast, meadows and the farmland's patchwork low down, woods up the slopes, rock and snow on the peaks, contour
        /// lines every 250 m, shaded from the north-west; the lakes, the runway, the city's blocks and river out to the east;
        /// the campaign's route dotted in gold from pin to pin; on parchment with a worn, darkened edge.
        /// </summary>
        static Texture2D Paint()
        {
            var terrain = Terrain.activeTerrain;
            var city = City.Skyline;
            var hgt = new float[TexW * TexH];      // metres; < 0 the sea
            var kind = new byte[TexW * TexH];      // 0 sea, 1 land, 2 lake, 3 city ground, 4 tower, 5 river
            Bounds tb = default;
            if (terrain != null) { var d = terrain.terrainData; tb = new Bounds(terrain.transform.position + d.size / 2, d.size); }
            for (int y = 0; y < TexH; y++)
                for (int x = 0; x < TexW; x++)
                {
                    int i = y * TexW + x;
                    hgt[i] = -1;
                    if (x < BreakPx)
                    {
                        float wx = (x + 0.5f - IslandPx0) / Scale - IslandHalfM, wz = (y + 0.5f - IslandPy0) / Scale - IslandHalfM;
                        if (terrain == null || wx < tb.min.x || wx > tb.max.x || wz < tb.min.z || wz > tb.max.z) continue;
                        float g = terrain.SampleHeight(new Vector3(wx, 0, wz)) + terrain.transform.position.y;
                        hgt[i] = g;
                        if (g < 1.5f) continue;
                        kind[i] = 1;
                        foreach (var l in Lakes)
                        {
                            float ex = (wx - l.x) / l.rx, ez = (wz - l.z) / l.rz;
                            if (ex * ex + ez * ez < 1.3f && g <= l.level + 2f) kind[i] = 2;
                        }
                    }
                    else
                    {
                        float wx = (x + 0.5f - CityCx) / Scale + City.CentreX, wz = (y + 0.5f - CityCy) / Scale + City.CentreZ;
                        if (city == null || !city.Over(wx, wz)) continue;
                        float top = (float)city.HeightM(wx, wz);
                        hgt[i] = top;
                        kind[i] = (byte)(Mathf.Abs(wx - City.CentreX) < City.RiverHalfM ? 5 : top > City.StreetM + 0.5f ? 4 : 3);
                    }
                }

            // how close the nearest land is, for the shallows (a few passes of a cheap spread)
            var near = new float[TexW * TexH];
            for (int i = 0; i < near.Length; i++) near[i] = kind[i] == 0 ? 0 : 1;
            for (int pass = 0; pass < 10; pass++)
                for (int y = 1; y < TexH - 1; y++)
                    for (int x = 1; x < TexW - 1; x++)
                    {
                        int i = y * TexW + x;
                        if (kind[i] != 0) continue;
                        float n = Mathf.Max(Mathf.Max(near[i - 1], near[i + 1]), Mathf.Max(near[i - TexW], near[i + TexW]));
                        near[i] = Mathf.Max(near[i], n * 0.88f);
                    }

            var px = new Color32[TexW * TexH];
            var sea = new Color(0.33f, 0.58f, 0.62f);
            var shallow = new Color(0.58f, 0.78f, 0.74f);
            var lake = new Color(0.30f, 0.66f, 0.72f);
            var paper = new Color(0.90f, 0.82f, 0.62f);
            var ink = new Color(0.27f, 0.18f, 0.09f);
            for (int y = 0; y < TexH; y++)
                for (int x = 0; x < TexW; x++)
                {
                    int i = y * TexW + x;
                    Color c;
                    byte kd = kind[i];
                    bool coast = false;
                    if (x > 0 && y > 0 && x < TexW - 1 && y < TexH - 1)
                        coast = (kd == 0) != (kind[i - 1] == 0) || (kd == 0) != (kind[i + TexW] == 0) || (kd == 2) != (kind[i - 1] == 2) || (kd == 2) != (kind[i + TexW] == 2);
                    if (kd == 0) c = Color.Lerp(sea, shallow, near[i] * near[i]);
                    else if (kd == 2 || kd == 5) c = lake;
                    else if (kd == 3) c = new Color(0.78f, 0.75f, 0.68f);
                    else if (kd == 4)
                    {
                        float tall = Mathf.InverseLerp(0, 300, hgt[i]);
                        c = Color.Lerp(new Color(0.62f, 0.58f, 0.54f), new Color(0.36f, 0.34f, 0.36f), tall);
                    }
                    else
                    {
                        float g = hgt[i];
                        float wx = (x + 0.5f - IslandPx0) / Scale - IslandHalfM, wz = (y + 0.5f - IslandPy0) / Scale - IslandHalfM;
                        // meadow low down, the farmland's patchwork of fields in the south, woods, then rock and snow
                        var meadow = new Color(0.60f, 0.71f, 0.37f);
                        if (wz < -3200 && g < 320)
                        {
                            int fx = Mathf.FloorToInt(wx / 420f), fz = Mathf.FloorToInt(wz / 300f);
                            float hsh = Hash(fx, fz);
                            meadow = hsh < 0.35f ? new Color(0.86f, 0.76f, 0.42f) : hsh < 0.6f ? new Color(0.68f, 0.76f, 0.38f) : hsh < 0.8f ? new Color(0.55f, 0.68f, 0.34f) : new Color(0.74f, 0.66f, 0.40f);
                        }
                        var woods = new Color(0.27f, 0.46f, 0.24f);
                        float woodsAmt = Mathf.Clamp01(Mathf.InverseLerp(150, 450, g) * (0.65f + 0.5f * Mathf.PerlinNoise(wx * 0.0015f, wz * 0.0015f)));
                        c = Color.Lerp(meadow, woods, woodsAmt);
                        c = Color.Lerp(c, new Color(0.58f, 0.54f, 0.48f), Mathf.InverseLerp(1450, 1750, g));
                        c = Color.Lerp(c, new Color(0.97f, 0.97f, 0.95f), Mathf.InverseLerp(1850, 2100, g));
                        // shaded from the north-west
                        float dx = hgt[y * TexW + Mathf.Min(TexW - 1, x + 1)] - hgt[y * TexW + Mathf.Max(0, x - 1)];
                        float dz = hgt[Mathf.Min(TexH - 1, y + 1) * TexW + x] - hgt[Mathf.Max(0, y - 1) * TexW + x];
                        float lit = Mathf.Clamp(1f - (dx - dz) * 0.0045f, 0.55f, 1.3f);
                        c *= lit;
                        // a contour every 250 m
                        int band = Mathf.FloorToInt(g / 250f);
                        if (x > 0 && y > 0 && (Mathf.FloorToInt(hgt[i - 1] / 250f) != band || Mathf.FloorToInt(hgt[i - TexW] / 250f) != band) && hgt[i - 1] > 1.5f && hgt[i - TexW] > 1.5f)
                            c = Color.Lerp(c, ink, 0.22f);
                    }
                    if (coast) c = Color.Lerp(c, ink, 0.75f);
                    px[i] = c;
                }

            // the runway, if the scene has it
            var rs = GameObject.Find("RunwayStart");
            var re = GameObject.Find("RunwayEnd");
            if (rs != null && re != null)
                Line(px, IslandPx(rs.transform.position.x, rs.transform.position.z), IslandPx(re.transform.position.x, re.transform.position.z), 2.2f, new Color(0.35f, 0.33f, 0.30f));
            // the cut in the sea between the island and the city, and the route from pin to pin
            for (int y = 30; y < TexH - 30; y += 22) Line(px, new Vector2(BreakPx - 8, y), new Vector2(BreakPx + 8, y + 11), 1.2f, Color.Lerp(sea, ink, 0.5f));
            for (int m = 0; m + 1 < Campaign.Count; m++)
            {
                Vector2 a = Vector2.Scale(MissionUv(m), new Vector2(TexW, TexH)), b = Vector2.Scale(MissionUv(m + 1), new Vector2(TexW, TexH));
                float len = Vector2.Distance(a, b);
                for (float d = 16; d < len - 12; d += 13)
                {
                    var p = Vector2.Lerp(a, b, d / len);
                    Dot(px, p, 4.2f, ink);
                    Dot(px, p, 3f, new Color(1f, 0.82f, 0.25f));
                }
            }

            // parchment: a wash of paper over it all, a little grain, darker toward the worn edge, and an inked frame
            for (int y = 0; y < TexH; y++)
                for (int x = 0; x < TexW; x++)
                {
                    int i = y * TexW + x;
                    float edge = Mathf.Min(Mathf.Min(x, TexW - 1 - x), Mathf.Min(y, TexH - 1 - y));
                    Color c = edge < 14 ? paper : (Color)px[i];
                    c = Color.Lerp(c, paper, 0.16f);
                    c *= 0.96f + 0.06f * Mathf.PerlinNoise(x * 0.11f, y * 0.11f) + 0.03f * (Hash(x, y) - 0.5f);
                    float u = (x + 0.5f) / TexW - 0.5f, v = (y + 0.5f) / TexH - 0.5f;
                    c = Color.Lerp(c, new Color(0.45f, 0.30f, 0.15f), Mathf.Clamp01((u * u * 2.2f + v * v * 2.6f) - 0.32f) * 0.9f);
                    if (edge >= 14 && edge < 17) c = Color.Lerp(c, ink, 0.8f);
                    c.a = 1;
                    px[i] = c;
                }

            var t = new Texture2D(TexW, TexH, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "CampaignMap", hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        static void Dot(Color32[] px, Vector2 c, float r, Color col)
        {
            for (int y = (int)(c.y - r - 1); y <= (int)(c.y + r + 1); y++)
                for (int x = (int)(c.x - r - 1); x <= (int)(c.x + r + 1); x++)
                {
                    if (x < 0 || y < 0 || x >= TexW || y >= TexH) continue;
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    float a = Mathf.Clamp01(r + 0.5f - d);
                    if (a > 0) px[y * TexW + x] = Color.Lerp(px[y * TexW + x], col, a);
                }
        }

        static void Line(Color32[] px, Vector2 a, Vector2 b, float r, Color col)
        {
            float len = Vector2.Distance(a, b);
            for (float d = 0; d <= len; d += 0.7f) Dot(px, Vector2.Lerp(a, b, len > 0 ? d / len : 0), r, col);
        }

        static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h & 0xffff) / 65535f; }

        // ---------- the self-test: -missionshots <folder> ----------

        /// <summary>Save the open briefing as a BMP for each of the first two missions picked, then the last (the city).</summary>
        public IEnumerator Shots(string folder)
        {
            foreach (int i in new[] { 0, 2, Campaign.Count - 1 })
            {
                picked = i;
                yield return new WaitForSecondsRealtime(1.2f);
                yield return DogfightSetupScreen.Grab(System.IO.Path.Combine(folder, $"mission_{i + 1:00}.bmp"));
            }
        }
    }
}
