using System;
using Ooda.Flight.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The bottom of the screen as a cockpit: a gunmetal instrument panel that wraps round you, its padded coaming
    /// curving up at the sides and dipping in the middle where the stick stands in front of it; and round dials set in
    /// it either side of the stick: the airspeed indicator and the altimeter on the left, the G meter on the right.
    /// While planning they read where the turn starts; during playback they follow the plane.
    /// </summary>
    public partial class TurnBattle
    {
        static Texture2D panelTex, dialTex;
        static float panelTexDip = -1;
        float shownKias, shownAlt, shownG = 1;
        GUIStyle dialNum, dialName;

        /// <summary>The panel behind the controls: its top at the sides, and how far it dips in the middle.</summary>
        void CockpitPanel(float sw, float sh)
        {
            if (!ctl.pictures) return;
            float side = ctl.panelTop, mid = ctl.stick.y + ctl.stick.height * 0.55f;
            if (mid <= side + 4) mid = side + 4;
            float dip = Mathf.Round((mid - side) / (sh - side) * 50f) / 50f;   // the shape, a texture made again only when it changes
            if (panelTex == null || Mathf.Abs(dip - panelTexDip) > 1e-3f) { MakePanelTexture(dip); panelTexDip = dip; }
            GUI.DrawTexture(new Rect(0, side, sw, sh - side), panelTex, ScaleMode.StretchToFill, true);
        }

        /// <summary>The panel's picture: transparent above the coaming's curve, a dark padded rim along it, gunmetal below.</summary>
        static void MakePanelTexture(float dip)
        {
            const int W = 512, H = 160;
            if (panelTex == null) panelTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[W * H];
            var rim = new Color(0.07f, 0.07f, 0.065f, 1f);
            for (int y = 0; y < H; y++)
            {
                float v = (H - 1 - y) / (float)(H - 1);   // 0 at the top of the rect, 1 at the bottom (Unity textures go bottom up)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)(W - 1) * 2 - 1;
                    float edge = dip * (1 - u * u);       // the coaming: high at the sides, dipping in the middle
                    float d = (v - edge) * H;              // pixels below the edge
                    Color c;
                    if (d < 0) c = new Color(0, 0, 0, Mathf.Clamp01(1 + d));
                    else if (d < 9)
                    {
                        // the padded rim: a soft highlight along its top
                        float lit = Mathf.Clamp01(1 - Mathf.Abs(d - 2.5f) / 2.5f) * 0.12f;
                        c = new Color(rim.r + lit, rim.g + lit, rim.b + lit, 1f);
                    }
                    else
                    {
                        // gunmetal, a shade darker toward the bottom and the far sides, with a faint grain
                        float shade = 0.22f - 0.06f * v - 0.04f * u * u + ((x * 7 + y * 13) % 5) * 0.003f;
                        c = new Color(shade * 0.92f, shade, shade * 0.95f, 0.97f);
                        if (d < 11) c = Color.Lerp(c, Color.black, 0.5f);   // the rim's shadow on the panel
                    }
                    px[y * W + x] = c;
                }
            }
            panelTex.SetPixels(px);
            panelTex.Apply();
        }

        /// <summary>A dial's face: black, with a bevelled metal bezel round it.</summary>
        static Texture2D DialTexture()
        {
            if (dialTex != null) return dialTex;
            const int S = 128;
            dialTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[S * S];
            float c = (S - 1) / 2f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy) / c;
                    float a = Mathf.Clamp01((1 - r) * c);   // smooth outer edge
                    Color col;
                    if (r > 0.86f)
                    {
                        float lit = 0.3f + 0.25f * (dy / c) - 0.1f * (dx / c);   // lit from above
                        col = new Color(lit, lit, lit * 1.02f, a);
                    }
                    else if (r > 0.83f) col = new Color(0.02f, 0.02f, 0.02f, 1f);
                    else col = new Color(0.06f, 0.065f, 0.06f, 1f);
                    px[y * S + x] = col;
                }
            dialTex.SetPixels(px);
            dialTex.Apply();
            return dialTex;
        }

        /// <summary>The three dials, in their places in the panel. Drawn whenever the stick is.</summary>
        void Instruments()
        {
            if (!ctl.pictures || ctl.asi.width <= 0) return;
            if (dialNum == null)
            {
                dialNum = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Overflow };
                dialNum.normal.textColor = new Color(0.95f, 0.95f, 0.9f);
                dialName = new GUIStyle(dialNum) { fontSize = 9, fontStyle = FontStyle.Normal };
                dialName.normal.textColor = new Color(0.8f, 0.82f, 0.78f);
            }
            // what to show: where the turn starts while planning, the plane as it flies during playback
            var m = phase == Phase.Playing && player != null && player.Model != null ? player.Model : battle.planes[0];
            float kias = (float)m.Kias, alt = (float)m.AltFt, g = (float)m.g;
            if (Event.current.type == EventType.Repaint)
            {
                float k = 1 - Mathf.Exp(-8f * Time.unscaledDeltaTime);
                shownKias = Mathf.Lerp(shownKias, kias, k); shownAlt = Mathf.Lerp(shownAlt, alt, k); shownG = Mathf.Lerp(shownG, g, k);
            }
            AirspeedDial(ctl.asi, shownKias);
            AltimeterDial(ctl.alt, shownAlt);
            GDial(ctl.gmeter, shownG, (float)m.numbers.maxG);
        }

        /// <summary>A line from c at angle (degrees clockwise from straight up), from r0 to r1 out.</summary>
        static void Spoke(Vector2 c, float angleDeg, float r0, float r1, float width, Color col)
        {
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(angleDeg, c);
            Fill(new Rect(c.x - width / 2, c.y - r1, width, r1 - r0), col);
            GUI.matrix = old;
        }

        /// <summary>A number on the dial at an angle, r out from the middle.</summary>
        void DialNumber(Vector2 c, float angleDeg, float r, string text)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            GUI.Label(new Rect(c.x + Mathf.Sin(a) * r - 12, c.y - Mathf.Cos(a) * r - 7, 24, 14), text, dialNum);
        }

        void DialFace(Rect d, string name, string unit, out Vector2 c, out float rad)
        {
            GUI.DrawTexture(d, DialTexture(), ScaleMode.StretchToFill, true);
            c = d.center; rad = d.width * 0.5f * 0.82f;
            GUI.Label(new Rect(d.x, c.y + rad * 0.32f, d.width, 12), name, dialName);
            if (unit != null) GUI.Label(new Rect(d.x, c.y - rad * 0.48f, d.width, 12), unit, dialName);
        }

        static readonly Color DialWhite = new Color(0.95f, 0.95f, 0.9f), DialRed = new Color(1f, 0.25f, 0.2f), DialAmber = new Color(1f, 0.75f, 0.2f);

        /// <summary>Airspeed: 0 to 500 kt once round, clockwise from the top; a tick each 25 kt, a number each 100.</summary>
        void AirspeedDial(Rect d, float kias)
        {
            DialFace(d, "AIRSPEED", "KNOTS", out var c, out float r);
            float A(float kt) => Mathf.Clamp(kt, 0, 500) / 500f * 340f;
            for (int kt = 0; kt <= 500; kt += 25)
                Spoke(c, A(kt), r * (kt % 100 == 0 ? 0.78f : 0.86f), r * 0.98f, kt % 100 == 0 ? 2f : 1f, DialWhite);
            if (r > 34) for (int kt = 0; kt < 500; kt += 100) DialNumber(c, A(kt), r * 0.6f, (kt / 10).ToString());
            Spoke(c, A(kias), -r * 0.12f, r * 0.9f, 2.5f, DialAmber);
            Fill(new Rect(c.x - 3, c.y - 3, 6, 6), Color.black);
            GUI.Label(new Rect(d.x, d.yMax - 2, d.width, 12), $"{kias:F0} KIAS", dialName);
        }

        /// <summary>The altimeter: the long hand a turn each 1,000 ft, the short one each 10,000; the height in figures under it.</summary>
        void AltimeterDial(Rect d, float alt)
        {
            DialFace(d, "ALT", null, out var c, out float r);
            for (int k = 0; k < 50; k++)
                Spoke(c, k * 7.2f, r * (k % 5 == 0 ? 0.78f : 0.88f), r * 0.98f, k % 5 == 0 ? 2f : 1f, DialWhite);
            if (r > 34) for (int k = 0; k < 10; k++) DialNumber(c, k * 36f, r * 0.6f, k.ToString());
            float a = Mathf.Max(0, alt);
            Spoke(c, a / 10000f * 360f, -r * 0.08f, r * 0.5f, 4f, DialWhite);              // thousands
            Spoke(c, (a % 1000f) / 1000f * 360f, -r * 0.12f, r * 0.9f, 2f, DialAmber);    // hundreds
            Fill(new Rect(c.x - 3, c.y - 3, 6, 6), Color.black);
            GUI.Label(new Rect(d.x, d.yMax - 2, d.width, 12), $"{alt:N0} FT", dialName);
        }

        /// <summary>The G meter: -3 to +9 round three quarters of the dial, 1 G at nine o'clock; red past the airframe's limit.</summary>
        void GDial(Rect d, float g, float maxG)
        {
            DialFace(d, "G", null, out var c, out float r);
            float A(float gg) => -135f + (Mathf.Clamp(gg, -3, 9) + 3) / 12f * 270f;
            for (int gg = -3; gg <= 9; gg++)
                Spoke(c, A(gg), r * (gg % 2 == 1 || gg == -3 ? 0.78f : 0.86f), r * 0.98f, gg == 1 ? 3f : 1.5f, gg > maxG ? DialRed : DialWhite);
            // the limit: a red arc of short ticks from the airframe's limit up
            for (float gg = Mathf.Max(-3, maxG); gg <= 9; gg += 0.25f) Spoke(c, A(gg), r * 0.92f, r * 0.98f, 2f, DialRed);
            if (r > 34) foreach (int gg in new[] { -2, 0, 2, 4, 6, 8 }) DialNumber(c, A(gg), r * 0.6f, gg.ToString());
            Spoke(c, A(g), -r * 0.12f, r * 0.9f, 2.5f, g > maxG ? DialRed : DialAmber);
            Fill(new Rect(c.x - 3, c.y - 3, 6, 6), Color.black);
            GUI.Label(new Rect(d.x, d.yMax - 2, d.width, 12), $"{g:F1} G", dialName);
        }
    }
}
