using System;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.UI
{
    /// <summary>
    /// The dogfight's squadron briefing, before the fight: an airfield in the Alps with your flight parked on the left and
    /// the bandits on the right, each pilot standing in front of his plane (BriefingStage), a VS badge between them. Pick a
    /// pilot (a click, or the arrow keys) and Change Pilot or Change Aircraft; the + and - under each banner add or send
    /// home a plane (1 to 4 a side). The Mission panel holds how the two sides start (or at random), how far apart, how
    /// high and how fast, with a map of where everyone starts from above, built from the same Scenarios.Dogfight the battle
    /// uses. The title screen opens it when Dogfight is picked and Play pressed. What is picked is kept (PlayerPrefs) for
    /// next time, and is in Chosen for the turn battle to read.
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
                chosen.FitLoadouts();   // every plane brings a loadout from the briefing on (an older one kept from before them gets the default)
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

        /// <summary>The 3D airfield the flights stand on; the title screen gives it the plane prefabs.</summary>
        public readonly BriefingStage stage = new BriefingStage();

        /// <summary>Every type a plane can fly, in the order Change Aircraft steps through them (new ones join the end of Craft).</summary>
        static readonly Craft[] Types = (Craft[])Enum.GetValues(typeof(Craft));
        static readonly Color Blue = new Color(0.4f, 0.75f, 1f), Red = new Color(1f, 0.4f, 0.35f);

        // the picked place: your planes first (0 your lead), then theirs
        int sel;
        bool mission;   // the mission panel (the start, range, height, speed and the map) is up
        bool loadout;   // the loadout panel (the picked pilot's two capabilities) is up

        GUIStyle heading, head, label, dim, button, picked, small, smallPicked, bigFly;
        GUIStyle barButtonOn, tile, tileOn, tileText;
        GUIStyle barTitle, logo, plaque, plaqueRed, tag, tagPicked, tagRed, tagRedPicked, tagSub, tagSubOn, barButton, ready, vs, hint, count;
        Texture2D white, bar, disc, shade;
        string mapKey;      // the setup the map was laid out for
        Battle mapBattle;   // and where it puts everyone

        // the mockup's colours: olive bars, cream plaques with dark olive or red stencil, gold for ready
        static readonly Color Olive = new Color(0.25f, 0.27f, 0.16f), OliveDark = new Color(0.13f, 0.14f, 0.08f), Cream = new Color(0.95f, 0.91f, 0.78f),
                              Ink = new Color(0.17f, 0.18f, 0.10f), Brick = new Color(0.62f, 0.10f, 0.08f), Gold = new Color(0.98f, 0.78f, 0.22f);

        int Count => Chosen.Ours + Chosen.Theirs;
        bool SelOurs => sel < Chosen.Ours;
        int SelIndex => SelOurs ? sel : sel - Chosen.Ours;

        /// <summary>Keys and the pad, called from Update while open: ← → (the d-pad) pick a pilot, P (pad X) changes his pilot, C (pad Y)
        /// his aircraft, + and − (the bumpers) add or send home a plane on his side, M (pad Select) the mission panel, Enter (pad A)
        /// flies, Esc (pad B) closes the mission panel or goes back.</summary>
        public Result Keys()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool Kb(Func<Keyboard, UnityEngine.InputSystem.Controls.ButtonControl> k) => kb != null && k(kb).wasPressedThisFrame;
            bool Pad(Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> b) => pad != null && b(pad).wasPressedThisFrame;
            bool shift = kb != null && kb.shiftKey.isPressed;
            if (Kb(k => k.escapeKey) || Pad(p => p.buttonEast))
            {
                if (mission) { mission = false; return Result.None; }
                if (loadout) { loadout = false; return Result.None; }
                return Result.Back;
            }
            if (Kb(k => k.enterKey) || Kb(k => k.numpadEnterKey) || Pad(p => p.buttonSouth) || Pad(p => p.startButton)) return Result.Fly;
            if (Kb(k => k.mKey) || Pad(p => p.selectButton)) { mission = !mission; loadout = false; }
            if (Kb(k => k.lKey) || Pad(p => p.rightStickButton)) { loadout = !loadout; mission = false; }
            if (Kb(k => k.gKey) || Pad(p => p.leftStickButton)) Chosen.arcade = !Chosen.arcade;
            if (mission) return Result.None;
            if (Kb(k => k.leftArrowKey) || Kb(k => k.aKey) || Pad(p => p.dpad.left)) MoveSel(-1);
            if (Kb(k => k.rightArrowKey) || Kb(k => k.dKey) || Pad(p => p.dpad.right)) MoveSel(1);
            if (Kb(k => k.pKey) || Pad(p => p.buttonWest)) ChangePilot(shift ? -1 : 1);
            if (Kb(k => k.cKey) || Pad(p => p.buttonNorth)) ChangeAircraft(shift ? -1 : 1);
            if (Kb(k => k.equalsKey) || Kb(k => k.numpadPlusKey) || Pad(p => p.rightShoulder)) AddPlane(SelOurs);
            if (Kb(k => k.minusKey) || Kb(k => k.numpadMinusKey) || Pad(p => p.leftShoulder)) RemovePlane(SelOurs);
            if (loadout && kb != null)
                for (int i = 0; i < Loadout.All.Length && i < 9; i++)
                    if (kb[UnityEngine.InputSystem.Key.Digit1 + i].wasPressedThisFrame) PickKit(Loadout.All[i]);
            return Result.None;
        }

        /// <summary>Left and right across the apron as it looks: your wingmen to the left of your lead, the bandits right of the middle.</summary>
        void MoveSel(int by)
        {
            // screen order, left to right: your last wingman ... your lead, their lead ... their last
            int n = Count, ours = Chosen.Ours;
            int pos = SelOurs ? ours - 1 - sel : sel;
            pos = Mathf.Clamp(pos + by, 0, n - 1);
            sel = pos < ours ? ours - 1 - pos : pos;
        }

        void ChangePilot(int by)
        {
            var s = Chosen;
            s.FitPilots();
            var craft = SelOurs ? s.ours[SelIndex] : s.theirs[SelIndex];
            if (craft == Craft.Phantom) return;   // the F-4 keeps its crew
            var pilots = SelOurs ? s.oursPilots : s.theirsPilots;
            pilots[SelIndex] = NextPilot(pilots[SelIndex], craft, by);
        }

        /// <summary>The next pilot for a type: its usual one, then the others, skipping the one that is the usual one by name.</summary>
        static PilotKind NextPilot(PilotKind p, Craft c, int by)
        {
            var usual = BriefingStage.UsualOf(c);
            var order = new System.Collections.Generic.List<PilotKind> { PilotKind.Usual };
            foreach (PilotKind k in Enum.GetValues(typeof(PilotKind))) if (k != PilotKind.Usual && k != usual) order.Add(k);
            int i = order.IndexOf(p == usual ? PilotKind.Usual : p);
            return order[((i < 0 ? 0 : i) + by + order.Count) % order.Count];
        }

        /// <summary>Who really sits in a plane: the usual pilot named.</summary>
        static string PilotName(PilotKind p, Craft c) => c == Craft.Phantom ? "Tiger & Wolf" : DogfightSetup.Name(p == PilotKind.Usual ? BriefingStage.UsualOf(c) : p);

        void ChangeAircraft(int by)
        {
            var s = Chosen;
            if (SelOurs) s.ours[SelIndex] = Step(s.ours[SelIndex], by);
            else s.theirs[SelIndex] = Step(s.theirs[SelIndex], by);
            s.FitLoadouts();   // a jet can't keep the rocket
        }

        /// <summary>The picked plane's aircraft and loadout.</summary>
        Craft SelCraft => SelOurs ? Chosen.ours[Mathf.Min(SelIndex, Chosen.Ours - 1)] : Chosen.theirs[Mathf.Min(SelIndex, Chosen.Theirs - 1)];
        Loadout SelLoadout
        {
            get { Chosen.FitLoadouts(); return (SelOurs ? Chosen.oursLoadouts : Chosen.theirsLoadouts)[SelIndex]; }
            set { Chosen.FitLoadouts(); (SelOurs ? Chosen.oursLoadouts : Chosen.theirsLoadouts)[SelIndex] = value; }
        }

        /// <summary>Add a capability to the picked pilot's two (it takes the place of the older pick). Picking one again does nothing.</summary>
        void PickKit(Kit k) => SelLoadout = SelLoadout.Pick(k, SelCraft);

        void AddPlane(bool ourSide)
        {
            var s = Chosen;
            if (ourSide) { s.ours = DogfightSetup.Resize(s.ours, s.Ours + 1); sel = s.Ours - 1; }
            else { s.theirs = DogfightSetup.Resize(s.theirs, s.Theirs + 1); sel = s.Ours + s.Theirs - 1; }
            s.FitPilots();
            s.FitLoadouts();
        }

        void RemovePlane(bool ourSide)
        {
            var s = Chosen;
            if (ourSide) s.ours = DogfightSetup.Resize(s.ours, s.Ours - 1);
            else s.theirs = DogfightSetup.Resize(s.theirs, s.Theirs - 1);
            s.FitPilots();
            s.FitLoadouts();
            sel = Mathf.Clamp(sel, 0, Count - 1);
            if (!ourSide && sel < s.Ours) sel = s.Ours;   // stay on their side
            if (ourSide && sel >= s.Ours) sel = s.Ours - 1;
        }

        /// <summary>Every frame (from the title's Update): builds the airfield when the briefing opens and takes it down when it
        /// closes, parks the setup's planes, moves the ring to the picked pilot, and holds the camera on it all.</summary>
        public void Tick(Camera cam, float dt)
        {
            if (!open) { if (stage.Built) stage.Close(); mission = false; loadout = false; return; }
            if (!stage.Built) { stage.Open(cam); sel = 0; }
            var s = Chosen;
            sel = Mathf.Clamp(sel, 0, Count - 1);
            stage.Sync(s);
            var list = SelOurs ? stage.ours : stage.theirs;
            stage.Pick(SelIndex < list.Count ? list[SelIndex] : null);
            stage.Tick(dt);
            stage.PlaceCamera();
        }

        void Styles()
        {
            if (heading != null) return;
            heading = BriefingStyle.Heading(GUI.skin.label, 40);
            heading.alignment = TextAnchor.MiddleCenter;
            head = BriefingStyle.Heading(GUI.skin.label, 24);
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

            // the squadron briefing's own look
            bar = Plate(Olive, OliveDark, new Color(0.42f, 0.44f, 0.28f));
            var cream = Plate(Cream, new Color(0.35f, 0.30f, 0.18f), new Color(0.80f, 0.74f, 0.58f));
            var creamHot = Plate(new Color(1f, 0.96f, 0.80f), Gold, Gold);
            var red = Plate(Brick, new Color(0.30f, 0.05f, 0.04f), new Color(0.85f, 0.70f, 0.55f));
            var redHot = Plate(new Color(0.75f, 0.14f, 0.10f), Gold, Gold);
            var olive = Plate(new Color(0.30f, 0.32f, 0.20f), new Color(0.85f, 0.80f, 0.62f), new Color(0.18f, 0.19f, 0.11f));
            var oliveHot = Plate(new Color(0.38f, 0.41f, 0.25f), Gold, new Color(0.18f, 0.19f, 0.11f));
            var gold = Plate(Gold, new Color(0.45f, 0.30f, 0.05f), new Color(1f, 0.92f, 0.55f));
            var goldHot = Plate(new Color(1f, 0.86f, 0.38f), new Color(0.45f, 0.30f, 0.05f), Color.white);
            disc = Disc();
            GUIStyle Stencil(int size, Color ink, Texture2D bg = null, Texture2D hot = null)
            {
                var st = new GUIStyle(GUI.skin.label) { font = BriefingStyle.Stencil, fontSize = size, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
                st.normal.textColor = st.hover.textColor = st.active.textColor = ink;
                if (bg != null) { st.normal.background = bg; st.border = new RectOffset(5, 5, 5, 5); st.padding = new RectOffset(8, 8, 2, 2); }
                if (hot != null) { st.hover.background = hot; st.active.background = hot; }
                return st;
            }
            barTitle = Stencil(44, Cream);
            logo = Stencil(34, Cream);
            plaque = Stencil(30, Ink, cream);
            plaqueRed = Stencil(30, Brick, cream);
            tag = Stencil(17, Ink, cream);
            tagPicked = Stencil(17, Ink, creamHot);
            tagRed = Stencil(17, Cream, red);
            tagRedPicked = Stencil(17, Cream, redHot);
            tagSub = Stencil(15, Cream);
            tagSubOn = Stencil(15, Gold);
            shade = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            shade.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.05f, 0.72f));
            shade.Apply();
            barButton = Stencil(22, Cream, olive, oliveHot);
            barButtonOn = Stencil(22, Gold, oliveHot, oliveHot);
            tile = Stencil(17, Ink, cream, creamHot);
            tile.alignment = TextAnchor.UpperLeft; tile.padding = new RectOffset(12, 10, 8, 6);
            tileOn = Stencil(17, Ink, creamHot, creamHot);
            tileOn.alignment = TextAnchor.UpperLeft; tileOn.padding = tile.padding;
            tileText = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft };
            tileText.normal.textColor = Ink;
            ready = Stencil(40, new Color(0.25f, 0.16f, 0.02f), gold, goldHot);
            vs = Stencil(38, Gold);
            hint = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            hint.normal.textColor = new Color(0.92f, 0.88f, 0.74f);
            count = Stencil(18, Cream, olive, oliveHot);
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
                    if (e > 2) c = Color.Lerp(c, Color.black, 0.10f * (1f - (float)y / N));   // a touch darker toward the foot
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        /// <summary>The VS badge's medal: an olive disc in a gold ring, soft at the edge.</summary>
        static Texture2D Disc()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                    Color c = r < 0.78f ? Color.Lerp(new Color(0.33f, 0.35f, 0.20f), OliveDark, r / 0.78f * 0.6f)
                            : r < 0.84f ? new Color(0.45f, 0.30f, 0.05f)
                            : r < 0.97f ? Color.Lerp(Gold, new Color(1f, 0.93f, 0.6f), Mathf.Abs(r - 0.905f) < 0.02f ? 1 : 0)
                            : new Color(0.45f, 0.30f, 0.05f);
                    c.a = Mathf.Clamp01((1f - r) * 40f);
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        /// <summary>Draw the screen (inside OnGUI, after BriefingStyle.Use). Returns Back or Fly when one is pressed.</summary>
        public Result Draw(float sw, float sh)
        {
            Styles();
            var s = Chosen;
            s.FitPilots();
            s.FitLoadouts();
            var result = Result.None;
            // laid out for 1280 x 720 and scaled to fit the window
            float k = Mathf.Min(sw / 1280f, sh / 720f);
            var keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1)) * keep;
            float w = sw / k, h = sh / k;
            const float TopH = 64, BotH = 86;

            // the bar along the top: OODA, then SQUADRON BRIEFING
            GUI.DrawTexture(new Rect(-4, -4, w + 8, TopH + 4), bar);
            for (int i = 0; i < 3; i++)
            {
                float len = 34 - i * 8, y = 22 + i * 8;
                Fill(new Rect(54 - len, y, len, 4), Cream);
                Fill(new Rect(162, y, len, 4), Cream);
            }
            GUI.Label(new Rect(58, 4, 100, TopH - 8), "OODA", logo);
            Fill(new Rect(212, 14, 3, TopH - 28), new Color(0.85f, 0.80f, 0.62f, 0.6f));
            GUI.Label(new Rect(0, 2, w, TopH - 4), "SQUADRON BRIEFING", barTitle);

            if (!stage.Built)
            {
                GUI.matrix = keep;
                return result;
            }

            // the flights' banners, with how many each side has
            float bannerY = TopH + 18;
            Banner(new Rect(w * 0.25f - 170, bannerY, 340, 54), "YOUR FLIGHT", plaque, Blue);
            Banner(new Rect(w * 0.75f - 150, bannerY, 300, 54), "BANDITS", plaqueRed, Red);
            Counter(new Rect(w * 0.25f - 90, bannerY + 60, 180, 30), true);
            Counter(new Rect(w * 0.75f - 90, bannerY + 60, 180, 30), false);

            // VS between them, and the Arcade switch under it
            Badge(new Vector2(w / 2, TopH + 96));
            var arc = new GUIStyle(s.arcade ? tagPicked : tag) { fontSize = 15 };
            if (GUI.Button(new Rect(w / 2 - 95, TopH + 162, 190, 32), s.arcade ? "ARCADE: ON (G)" : "ARCADE: OFF (G)", arc)) s.arcade = !s.arcade;
            GUI.DrawTexture(new Rect(w / 2 - 150, TopH + 196, 300, 22), shade);
            GUI.Label(new Rect(w / 2 - 150, TopH + 197, 300, 20), s.arcade ? "EACH PILOT BRINGS TWO: L LOADOUT" : "STRAIGHT DOGFIGHT: NO SPECIALS", tagSub);

            // the name plates: a row above the foot bar in the order they stand, lead nearest the middle (under each
            // pilot's feet they crowd together as the wingmen step back)
            for (int side = 0; side < 2; side++)
            {
                var list = side == 0 ? stage.ours : stage.theirs;
                for (int i = 0; i < list.Count; i++)
                {
                    float dir = side == 0 ? -1 : 1;
                    float x = w / 2 + dir * (w * 0.06f + 70 + i * 140);
                    float y = h - BotH - 92;
                    bool on = (side == 0 ? i : s.Ours + i) == sel;
                    string who = side == 0 ? (i == 0 ? "LEAD" : $"WINGMAN {i}") : (i == 0 ? "BANDIT LEAD" : $"BANDIT {i + 1}");
                    var st = side == 0 ? (on ? tagPicked : tag) : (on ? tagRedPicked : tagRed);
                    if (GUI.Button(new Rect(x - 68, y, 136, 30), who, st)) sel = side == 0 ? i : s.Ours + i;
                    var craft = side == 0 ? s.ours[i] : s.theirs[i];
                    var pk = side == 0 ? s.oursPilots[i] : s.theirsPilots[i];
                    var load = s.LoadoutOf(side == 0, i);
                    bool showLoad = s.arcade && load != null;
                    var sub = new Rect(x - 68, y + 32, 136, showLoad ? 58 : 40);
                    GUI.DrawTexture(sub, shade);
                    GUI.Label(new Rect(sub.x, sub.y + 1, sub.width, 20), PilotName(pk, craft).ToUpperInvariant(), on ? tagSubOn : tagSub);
                    GUI.Label(new Rect(sub.x, sub.y + 19, sub.width, 20), DogfightSetup.Short(craft).ToUpperInvariant(), on ? tagSubOn : tagSub);
                    if (showLoad && GUI.Button(new Rect(sub.x, sub.y + 37, sub.width, 20), load.Value.Describe(), on ? tagSubOn : tagSub))
                    {
                        sel = side == 0 ? i : s.Ours + i;
                        loadout = true; mission = false;
                    }
                }
            }

            // the bar along the foot: back, change pilot, change aircraft, the mission, ready
            float by = h - BotH;
            GUI.DrawTexture(new Rect(-4, by, w + 8, BotH + 4), bar);
            float bh = 54, bty = by + (BotH - bh) / 2;
            if (GUI.Button(new Rect(24, bty, 128, bh), "< BACK", barButton)) { if (mission) mission = false; else if (loadout) loadout = false; else result = Result.Back; }
            Divider(162, by);
            bool f4 = SelCraft == Craft.Phantom;
            var keepCol = GUI.color;
            if (f4) GUI.color = new Color(1, 1, 1, 0.5f);
            if (GUI.Button(new Rect(176, bty, 210, bh), "CHANGE PILOT", barButton)) ChangePilot(Event.current.button == 1 ? -1 : 1);
            GUI.color = keepCol;
            Divider(396, by);
            if (GUI.Button(new Rect(410, bty, 248, bh), "CHANGE AIRCRAFT", barButton)) ChangeAircraft(Event.current.button == 1 ? -1 : 1);
            Divider(668, by);
            if (GUI.Button(new Rect(682, bty, 152, bh), "LOADOUT", loadout ? barButtonOn : barButton))
            { loadout = !loadout; mission = false; }
            Divider(844, by);
            if (GUI.Button(new Rect(858, bty, Mathf.Max(140, w - 858 - 290), bh), mission ? "CLOSE" : "MISSION", barButton)) { mission = !mission; loadout = false; }
            if (GUI.Button(new Rect(w - 268, by + 8, 240, BotH - 16), "READY", ready)) result = Result.Fly;
            GUI.DrawTexture(new Rect(0, by - 26, w, 26), shade);
            Shadow(new Rect(0, by - 25, w, 24), mission ? "Esc closes the mission panel  ·  Enter flies"
                : loadout ? "← → pick a pilot  ·  1 – 9 add a capability (it replaces the older pick)  ·  L or Esc closes  ·  Enter ready"
                : "← → pick a pilot  ·  P pilot  ·  C aircraft  ·  L loadout  ·  + − planes on that side  ·  M mission  ·  G arcade  ·  Enter ready", hint);

            var panel = new Rect(w / 2 - 530, TopH + 10, 1060, 384);
            if (mission) MissionPanel(new Rect(w / 2 - 560, TopH + 20, 1120, by - TopH - 56), s);
            else if (loadout) LoadoutPanel(panel, s);
            if (!mission && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                // a click on a plane or a pilot picks it
                Vector2 m = Event.current.mousePosition;
                if (m.y > TopH && m.y < by && !(loadout && panel.Contains(m)))
                {
                    float best = float.MaxValue;
                    int pick = -1;
                    for (int side = 0; side < 2; side++)
                    {
                        var list = side == 0 ? stage.ours : stage.theirs;
                        for (int i = 0; i < list.Count; i++)
                        {
                            Rect r = stage.ScreenBox(list[i]);
                            r = new Rect(r.x / k - 20, r.y / k - 10, r.width / k + 40, r.height / k + 20);
                            if (!r.Contains(m)) continue;
                            float d = Vector2.Distance(m, r.center);
                            if (d < best) { best = d; pick = side == 0 ? i : s.Ours + i; }
                        }
                    }
                    if (pick >= 0) { sel = pick; Event.current.Use(); }
                }
            }

            GUI.matrix = keep;
            return result;
        }

        void Fill(Rect r, Color c)
        {
            var keep = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = keep;
        }

        void Divider(float x, float barY) => Fill(new Rect(x, barY + 20, 3, 46), new Color(0.85f, 0.80f, 0.62f, 0.55f));

        static void Shadow(Rect r, string text, GUIStyle st)
        {
            var c = st.normal.textColor;
            st.normal.textColor = new Color(0, 0, 0, 0.75f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, st);
            st.normal.textColor = c;
            GUI.Label(r, text, st);
        }

        /// <summary>A plaque with wings either side, like the mockup's: three stripes each way in the side's colour.</summary>
        void Banner(Rect r, string text, GUIStyle st, Color col)
        {
            var wing = Color.Lerp(col, new Color(0.9f, 0.85f, 0.7f), 0.35f);
            for (int i = 0; i < 3; i++)
            {
                float len = 70 - i * 18, y = r.y + 14 + i * 9;
                Fill(new Rect(r.x - len + 6, y, len, 5), new Color(0, 0, 0, 0.35f));
                Fill(new Rect(r.x - len + 4, y - 1, len, 5), wing);
                Fill(new Rect(r.xMax - 6, y, len, 5), new Color(0, 0, 0, 0.35f));
                Fill(new Rect(r.xMax - 4, y - 1, len, 5), wing);
            }
            GUI.Label(r, text, st);
        }

        /// <summary>− n PLANES + under a banner.</summary>
        void Counter(Rect r, bool ourSide)
        {
            int n = ourSide ? Chosen.Ours : Chosen.Theirs;
            if (GUI.Button(new Rect(r.x, r.y, 34, r.height), "-", count)) RemovePlane(ourSide);
            Shadow(new Rect(r.x + 34, r.y, r.width - 68, r.height), n == 1 ? "1 PLANE" : $"{n} PLANES", new GUIStyle(hint) { fontSize = 16, font = BriefingStyle.Stencil });
            if (GUI.Button(new Rect(r.xMax - 34, r.y, 34, r.height), "+", count)) AddPlane(ourSide);
        }

        /// <summary>The VS medal with gold wings.</summary>
        void Badge(Vector2 c)
        {
            for (int i = 0; i < 4; i++)
            {
                float len = 92 - i * 16, y = c.y - 20 + i * 11;
                Fill(new Rect(c.x - 44 - len, y + 2, len, 6), new Color(0, 0, 0, 0.35f));
                Fill(new Rect(c.x - 46 - len, y, len, 6), Gold);
                Fill(new Rect(c.x + 46, y + 2, len, 6), new Color(0, 0, 0, 0.35f));
                Fill(new Rect(c.x + 46, y, len, 6), Gold);
            }
            GUI.DrawTexture(new Rect(c.x - 52, c.y - 52, 104, 104), disc);
            Shadow(new Rect(c.x - 52, c.y - 52, 104, 104), "VS", vs);
        }

        /// <summary>
        /// The loadout panel: the picked pilot's two capabilities (Loadout), a tile for each one he could bring with what it
        /// does; a click (or its number key) adds it in place of the older pick. Guns, flares and the speed brake always come.
        /// With Arcade off it says so: a straight dogfight carries the same for everyone.
        /// </summary>
        void LoadoutPanel(Rect r, DogfightSetup s)
        {
            GUI.Box(r, "");
            GUI.Box(r, "");
            var craft = SelCraft;
            string who = SelOurs ? (SelIndex == 0 ? "LEAD" : $"WINGMAN {SelIndex}") : (SelIndex == 0 ? "BANDIT LEAD" : $"BANDIT {SelIndex + 1}");
            var pk = SelOurs ? s.oursPilots[SelIndex] : s.theirsPilots[SelIndex];
            GUI.Label(new Rect(r.x, r.y + 4, r.width, 40), "LOADOUT", heading);
            var whoStyle = new GUIStyle(head) { alignment = TextAnchor.MiddleCenter };
            whoStyle.normal.textColor = SelOurs ? Blue : Red;
            if (GUI.Button(new Rect(r.x + 150, r.y + 46, 44, 32), "<", small)) MoveSel(-1);
            GUI.Label(new Rect(r.x + 200, r.y + 46, r.width - 400, 32), $"{who}  ·  {PilotName(pk, craft).ToUpperInvariant()}  ·  {DogfightSetup.Name(craft).ToUpperInvariant()}", whoStyle);
            if (GUI.Button(new Rect(r.xMax - 194, r.y + 46, 44, 32), ">", small)) MoveSel(1);

            var note = new GUIStyle(dim) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            if (!s.arcade)
            {
                GUI.Label(new Rect(r.x + 40, r.y + 110, r.width - 80, 80),
                          "Arcade is off: a straight dogfight. Everyone carries guns, heat-seekers, flares, the rocket and the speed brake (a jet its radar missiles too), and no specials.", note);
                if (GUI.Button(new Rect(r.center.x - 130, r.y + 210, 260, 44), "TURN ARCADE ON", smallPicked)) s.arcade = true;
                return;
            }

            var load = SelLoadout;
            GUI.Label(new Rect(r.x + 20, r.y + 82, r.width - 40, 24), $"Pick two to bring: {load}. Guns, flares and the speed brake always come.", note);
            int Cols = (Loadout.All.Length + 1) / 2;   // two rows
            float gap = 12, x0 = r.x + 20, y0 = r.y + 114, tw = (r.width - 40 - gap * (Cols - 1)) / Cols, th = 100;
            for (int i = 0; i < Loadout.All.Length; i++)
            {
                var k = Loadout.All[i];
                var at = new Rect(x0 + (i % Cols) * (tw + gap), y0 + (i / Cols) * (th + gap), tw, th);
                bool on = load.Has(k), can = Loadout.Allowed(k, craft);
                var keep = GUI.color;
                if (!can) GUI.color = new Color(1, 1, 1, 0.45f);
                if (GUI.Button(at, $"{i + 1}  {Loadout.Name(k).ToUpperInvariant()}", on ? tileOn : tile) && can) PickKit(k);
                GUI.Label(new Rect(at.x + 12, at.y + 32, at.width - 22, at.height - 36), Loadout.Blurb(k, craft), tileText);
                GUI.color = keep;
                if (on)
                {
                    // a gold rim round a pick
                    Fill(new Rect(at.x - 3, at.y - 3, at.width + 6, 3), Gold); Fill(new Rect(at.x - 3, at.yMax, at.width + 6, 3), Gold);
                    Fill(new Rect(at.x - 3, at.y, 3, at.height), Gold); Fill(new Rect(at.xMax, at.y, 3, at.height), Gold);
                }
            }
            // the same two for the whole flight
            float fy = y0 + 2 * (th + gap) + 2;
            if (GUI.Button(new Rect(r.center.x - 160, fy, 320, 34), SelOurs ? "SAME FOR YOUR WHOLE FLIGHT" : "SAME FOR ALL THE BANDITS", small))
            {
                var list = SelOurs ? s.oursLoadouts : s.theirsLoadouts;
                for (int j = 0; j < list.Length; j++) list[j] = load;
                s.FitLoadouts();
            }
        }

        /// <summary>The mission panel: how the two flights start, how far apart, how high and fast, and the map of where everyone starts.</summary>
        void MissionPanel(Rect r, DogfightSetup s)
        {
            GUI.Box(r, "");
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x, r.y + 6, r.width, 44), "MISSION", heading);
            float x = r.x + 20, y = r.y + 58, colW = 600;
            GUI.Label(new Rect(x, y, 400, 30), "THE START", head);
            y += 36;
            float bw = (colW - 3 * 8) / 4;
            for (int i = 0; i < 8; i++)
            {
                bool on = i == 0 ? s.randomStart : !s.randomStart && (int)s.start == i - 1;
                string text = i == 0 ? "Random" : Scenarios.Title((Scenarios.Start)(i - 1));
                if (GUI.Button(new Rect(x + (i % 4) * (bw + 8), y + (i / 4) * 48, bw, 42), text, on ? smallPicked : small))
                {
                    if (i == 0) s.randomStart = true;
                    else { s.randomStart = false; s.start = (Scenarios.Start)(i - 1); }
                }
            }
            y += 100;
            GUI.Label(new Rect(x, y, colW, 50), s.randomStart ? "One of the seven at random, a new one each time you fly again." : Explain(s.start), new GUIStyle(dim) { wordWrap = true, alignment = TextAnchor.UpperLeft });
            y += 58;
            int rg = (int)s.range, hgt = (int)s.height, sp = (int)s.speed;
            Choice(new Rect(x, y, colW, 42), "RANGE", new[] { "Close", "Medium", "Far" }, ref rg); y += 52;
            Choice(new Rect(x, y, colW, 42), "HEIGHT", new[] { "Low", "Medium", "High" }, ref hgt); y += 52;
            Choice(new Rect(x, y, colW, 42), "SPEED", new[] { "220 kt", "300 kt", "380 kt" }, ref sp); y += 52;
            s.range = (DogfightSetup.Range)rg; s.height = (DogfightSetup.Height)hgt; s.speed = (DogfightSetup.Pace)sp;
            // where on the island: Random or one of the arenas (short names on the buttons, the arena's own words under them)
            int ar = s.randomArena ? 0 : s.arena + 1;
            Choice(new Rect(x, y, colW, 42), "ARENA", new[] { "Random", "Peaks", "Pass", "Lake", "Town" }, ref ar); y += 48;
            s.randomArena = ar == 0; if (ar > 0) s.arena = ar - 1;
            GUI.Label(new Rect(x + 100, y, colW - 100, 46), s.randomArena ? "Over one of the four arenas at random: the Peak Maze, the High Pass, Chalet Lake or the Town & Farmland."
                                                                         : $"{Arenas.Get(s.arena).name}: {Arenas.Get(s.arena).blurb}", new GUIStyle(dim) { wordWrap = true, alignment = TextAnchor.UpperLeft });
            float mx = x + colW + 24;
            Map(new Rect(mx, r.y + 58, r.xMax - 20 - mx, r.height - 78), s);
        }

        static string Explain(Scenarios.Start st) =>
            st == Scenarios.Start.HeadOn ? "Nose to nose, coming straight at each other, a little off to one side so holding straight is a pass."
          : st == Scenarios.Start.Neutral ? "Side by side and level, flying the same way: neither has the advantage."
          : st == Scenarios.Start.Offensive ? "You behind them, a little to one side: the shot is yours to take."
          : st == Scenarios.Start.Defensive ? "Them behind you: break, or be shot."
          : st == Scenarios.Start.Crossing ? "Them coming in from your right at right angles: who turns first?"
          : st == Scenarios.Start.Perch ? "You high and behind them, out to one side: dive in on them."
          : "Them high and behind you, out to one side: they are diving in.";

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
        /// With -setupshots folder on the command line: open the briefing on three setups and save each as a BMP there, then
        /// quit: 2 v 2 Spitfire and F-4 against Bf 109s with the wizard and the skeleton; 4 v 3 mixed with the bear leading, the penguin on his wing and a wingman picked, against
        /// a Zero flown by the fox, a Mustang with the lion and the F-4;
        /// and the 4 v 3 with the mission panel up. Doesn't touch the setup kept for next time.
        /// </summary>
        public System.Collections.IEnumerator Shots(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            var keep = chosen;
            chosen = new DogfightSetup { ours = new[] { Craft.Spitfire, Craft.Phantom }, theirs = new[] { Craft.Bf109, Craft.Bf109 }, randomStart = false, start = Scenarios.Start.Perch,
                                      oursPilots = new[] { PilotKind.Wizard, PilotKind.Usual }, theirsPilots = new[] { PilotKind.Skeleton, PilotKind.Usual } };
            open = true;
            yield return new WaitForSecondsRealtime(3f);
            yield return Grab(System.IO.Path.Combine(folder, "briefing_2v2.bmp"));
            chosen = new DogfightSetup
            {
                ours = new[] { Craft.Mustang, Craft.Spitfire, Craft.Mustang, Craft.Phantom }, theirs = new[] { Craft.Zero, Craft.Mustang, Craft.Phantom },
                oursPilots = new[] { PilotKind.Bear, PilotKind.Skeleton, PilotKind.Penguin, PilotKind.Usual }, theirsPilots = new[] { PilotKind.Usual, PilotKind.Lion, PilotKind.Usual },
                randomStart = false, start = Scenarios.Start.HeadOn, range = DogfightSetup.Range.Close, height = DogfightSetup.Height.Low, speed = DogfightSetup.Pace.Fast,
            };
            sel = 1;
            yield return new WaitForSecondsRealtime(2f);
            yield return Grab(System.IO.Path.Combine(folder, "briefing_4v3.bmp"));
            mission = true;
            yield return new WaitForSecondsRealtime(1f);
            yield return Grab(System.IO.Path.Combine(folder, "briefing_mission.bmp"));
            mission = false;
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
                // turned to the arena's axis, so north is up as on the island (a random arena shows it heading north)
                double axis = show.randomArena ? 0 : Arenas.Get(show.arena).axisDeg;
                try { mapBattle = Scenarios.Dogfight(show, new Vec3d(0, 0, 0), 3600, 300, 3000, true, axis); }
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
            Rotate(hdg, at);
            var c = GUI.color;
            GUI.color = col;
            float L = 26 * size, W = 22 * size;
            GUI.DrawTexture(new Rect(at.x - 2 * size, at.y - L / 2, 4 * size, L), white);               // fuselage, nose up
            GUI.DrawTexture(new Rect(at.x - W / 2, at.y - L * 0.12f, W, 4 * size), white);             // wings
            GUI.DrawTexture(new Rect(at.x - W * 0.22f, at.y + L * 0.36f, W * 0.44f, 3 * size), white); // tail
            GUI.color = c;
            GUI.matrix = keep;
        }

        /// <summary>Turn what is drawn next by deg about a GUI point. (GUIUtility.RotateAroundPivot puts the pivot in the wrong
        /// place once GUI.matrix scales, as the briefing's does.)</summary>
        static void Rotate(float deg, Vector2 pivot) =>
            GUI.matrix = GUI.matrix * Matrix4x4.TRS(pivot, Quaternion.Euler(0, 0, deg), Vector3.one) * Matrix4x4.Translate(-pivot);

        /// <summary>A straight line between two GUI points.</summary>
        void Rule(Vector2 a, Vector2 b, float width, Color col)
        {
            var keep = GUI.matrix;
            var d = b - a;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Rotate(ang, a);
            var c = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2, d.magnitude, width), white);
            GUI.color = c;
            GUI.matrix = keep;
        }
    }
}
