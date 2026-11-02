using UnityEngine;

namespace Ooda.UI
{
    /// <summary>
    /// The one look for every screen's text: green monospaced type on dark olive panels, like a typed mission briefing,
    /// with a stencil face for the big headings. Call <see cref="Use"/> first thing in an OnGUI and every label, button,
    /// box and toggle drawn after it (and every GUIStyle made from GUI.skin) takes the look.
    /// The fonts are Share Tech Mono and Stardos Stencil (SIL Open Font License, see Resources/Fonts).
    /// </summary>
    public static class BriefingStyle
    {
        /// <summary>The briefing green for ordinary text.</summary>
        public static readonly Color Green = new Color(0.55f, 1f, 0.55f);
        /// <summary>A brighter green, for headings and the pressed or hovered button.</summary>
        public static readonly Color BrightGreen = new Color(0.75f, 1f, 0.7f);
        /// <summary>A dimmer green, for the less important lines.</summary>
        public static readonly Color DimGreen = new Color(0.4f, 0.75f, 0.4f);
        /// <summary>The amber of a picked option.</summary>
        public static readonly Color Amber = new Color(1f, 0.85f, 0.2f);

        /// <summary>The text size for styles that don't set one.</summary>
        public const int DefaultSize = 13;

        static Font mono, stencil;
        static GUISkin skin;

        /// <summary>The monospaced body face.</summary>
        public static Font Mono => mono != null ? mono : mono = Resources.Load<Font>("Fonts/ShareTechMono-Regular");
        /// <summary>The stencil heading face.</summary>
        public static Font Stencil => stencil != null ? stencil : stencil = Resources.Load<Font>("Fonts/StardosStencil-Bold");

        /// <summary>Sets GUI.skin to the briefing skin. Only call it inside OnGUI.</summary>
        public static void Use()
        {
            if (skin == null) skin = Build(GUI.skin);
            GUI.skin = skin;
        }

        /// <summary>A heading style: the stencil face in bright green.</summary>
        public static GUIStyle Heading(GUIStyle from, int size)
        {
            var st = new GUIStyle(from) { font = Stencil, fontSize = size, fontStyle = FontStyle.Normal };
            st.normal.textColor = BrightGreen;
            return st;
        }

        static GUISkin Build(GUISkin from)
        {
            var s = Object.Instantiate(from);
            s.name = "Briefing";
            s.hideFlags = HideFlags.HideAndDontSave;
            if (Mono != null) s.font = Mono;

            var panel = Frame(new Color(0.03f, 0.07f, 0.03f, 0.82f), new Color(0.3f, 0.6f, 0.3f, 0.9f));
            var off = Frame(new Color(0.06f, 0.13f, 0.06f, 0.92f), new Color(0.35f, 0.7f, 0.35f, 1f));
            var over = Frame(new Color(0.1f, 0.22f, 0.1f, 0.95f), new Color(0.55f, 1f, 0.55f, 1f));
            var down = Frame(new Color(0.16f, 0.32f, 0.14f, 1f), new Color(0.75f, 1f, 0.7f, 1f));
            var border = new RectOffset(2, 2, 2, 2);

            Tint(s.label, Green);
            Tint(s.box, Green);
            s.box.normal.background = panel;
            s.box.border = border;

            Tint(s.button, Green);
            s.button.normal.background = off;
            s.button.hover.background = s.button.focused.background = over;
            s.button.active.background = down;
            s.button.onNormal.background = down;
            s.button.onHover.background = s.button.onActive.background = down;
            s.button.border = border;
            s.button.hover.textColor = s.button.active.textColor = BrightGreen;

            Tint(s.toggle, Green);
            Tint(s.textField, Green);
            Tint(s.window, Green);

            // Styles that left the size to the font: keep them at the old default's size, since the
            // monospaced face runs a little wider and the buttons were laid out for it.
            foreach (var st in new[] { s.label, s.box, s.button, s.toggle, s.textField, s.window })
                if (st.fontSize == 0) st.fontSize = DefaultSize;
            return s;
        }

        static void Tint(GUIStyle st, Color c)
        {
            st.normal.textColor = st.hover.textColor = st.active.textColor = st.focused.textColor = c;
            st.onNormal.textColor = st.onHover.textColor = st.onActive.textColor = st.onFocused.textColor = c;
        }

        /// <summary>An 8 x 8 tile: a fill with a one-pixel rim, nine-sliced by the style's border.</summary>
        static Texture2D Frame(Color fill, Color rim)
        {
            var t = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    t.SetPixel(x, y, x == 0 || y == 0 || x == 7 || y == 7 ? rim : fill);
            t.Apply();
            return t;
        }
    }
}
