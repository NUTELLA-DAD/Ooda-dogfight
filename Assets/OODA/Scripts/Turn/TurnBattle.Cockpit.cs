using System;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The bottom of the screen as a cockpit: a gunmetal instrument panel shaped as a trapezoid, its padded coaming
    /// flat across the middle over the stick (it stands inside it) and sloping down to the bottom corners;
    /// the instruments grouped left of the stick: the airspeed indicator and the altimeter side by side, the G meter and the
    /// attitude indicator under them; and the system lights in a row along the top of the dash.
    /// While planning they read where the turn starts; during playback they follow the plane.
    /// </summary>
    public partial class TurnBattle
    {
        static Texture2D panelTex, dialTex;
        static float panelTexDip = -1;
        float shownKias, shownAlt, shownG = 1;
        GUIStyle dialNum, dialName;

        /// <summary>The panel behind the controls: a trapezoid, its flat top just over the stick and the throttle across the middle half, its sides sloping down to the bottom corners.</summary>
        void CockpitPanel(float sw, float sh)
        {
            if (!ctl.pictures) return;
            float top = ctl.panelTop;
            if (panelTex == null || panelTexDip != 1) { MakePanelTexture(); panelTexDip = 1; }
            GUI.DrawTexture(new Rect(0, top, sw, sh - top), panelTex, ScaleMode.StretchToFill, true);
        }

        /// <summary>The panel's picture: transparent above the coaming, a dark padded rim along it, gunmetal below.</summary>
        static void MakePanelTexture()
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
                    // the coaming: flat across the middle half, sloping straight down to near the bottom corners, the bends eased
                    float a = Mathf.Abs(u), t = Mathf.Clamp01((a - 0.5f) / 0.5f);
                    float bend = 0.06f;
                    float slope = a < 0.5f - bend ? 0 : a > 0.5f + bend ? t : Mathf.Pow((a - (0.5f - bend)) / (2 * bend), 2) * bend / 0.5f;
                    float edge = slope * 0.85f;
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
                        c = new Color(shade * 0.92f, shade, shade * 0.95f, 1f);
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

        /// <summary>The dials, in their places in the panel, and the system lights. Drawn whenever the stick is.</summary>
        void Instruments()
        {
            if (!ctl.pictures || ctl.asi.width <= 0) return;
            if (dialNum == null)
            {
                dialNum = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Overflow, wordWrap = false };
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
            if (ctl.attitude.width > 0)
            {
                float pitch = Mathf.Asin(Mathf.Clamp((float)m.Forward.z, -1, 1)) * Mathf.Rad2Deg, bank = (float)AutoPilot.BankDeg(m);
                if (Event.current.type == EventType.Repaint)
                {
                    float k = 1 - Mathf.Exp(-8f * Time.unscaledDeltaTime);
                    shownPitch = Mathf.Lerp(shownPitch, pitch, k); shownBank = Mathf.LerpAngle(shownBank, bank, k);
                }
                AttitudeDial(ctl.attitude, shownPitch, Mathf.DeltaAngle(0, shownBank));
            }
            if (ctl.lights.width > 0) SystemLights(ctl.lights);
        }

        static readonly string[] LightNames = { "ENG", "FUEL", "CTRL", "WING" };
        GUIStyle lightText;

        /// <summary>
        /// The crucial system lights in a row along the top of the dash, over the stick: a lamp each for the engine, the
        /// fuel, the flight controls and the wings. <see cref="SystemLight"/> says what each shows; without it they stay green.
        /// </summary>
        void SystemLights(Rect r)
        {
            if (lightText == null)
            {
                lightText = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip };
                lightText.font = Ooda.UI.BriefingStyle.Stencil;
            }
            guiRects.Add(r);
            Fill(r, SwEdge);
            Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), SwHole);
            float cw = (r.width - 2 - 2 * 5) / 4;
            for (int k = 0; k < 4; k++)
            {
                var cell = new Rect(r.x + 2 + k * (cw + 2), r.y + 2, cw, r.height - 4);
                Color lamp = LampGreen; string text = LightNames[k];
                SystemLight(k, ref lamp, ref text);
                Fill(cell, Color.Lerp(SwHole, lamp, 0.85f));
                Fill(new Rect(cell.x, cell.y, cell.width, 2), Color.Lerp(lamp, Color.white, 0.4f));   // the lens catching the light
                lightText.normal.textColor = new Color(0.05f, 0.05f, 0.04f);
                GUI.Label(cell, text, lightText);
            }
        }

        float shownPitch, shownBank;
        static Texture2D attTex;
        static float attPitch = 999, attBank = 999;
        static readonly Color AttSky = new Color(0.22f, 0.55f, 0.92f), AttGround = new Color(0.55f, 0.34f, 0.14f);

        /// <summary>The attitude indicator: a ball of sky over ground that tilts with the bank and drops as the nose comes up
        /// (a rung each 10 degrees, the ball's edge at 40), a fixed amber aircraft over it, and the bank scale along the top.</summary>
        void AttitudeDial(Rect d, float pitch, float bank)
        {
            GUI.DrawTexture(d, DialTexture(), ScaleMode.StretchToFill, true);
            Vector2 c = d.center; float rad = d.width * 0.5f * 0.82f, br = rad * 0.8f;
            if (Event.current.type == EventType.Repaint && (attTex == null || Mathf.Abs(pitch - attPitch) > 0.2f || Mathf.Abs(Mathf.DeltaAngle(bank, attBank)) > 0.2f))
            {
                BuildAttitudeBall(pitch, bank);
                attPitch = pitch; attBank = bank;
            }
            GUI.DrawTexture(new Rect(c.x - br, c.y - br, 2 * br, 2 * br), attTex, ScaleMode.StretchToFill, true);
            // the bank scale: ticks at 0, 10, 20, 30, 60 either side, and the pointer turning with the ball
            foreach (float t in new[] { 0f, 10f, 20f, 30f, 60f, -10f, -20f, -30f, -60f })
                Spoke(c, t, rad * (t % 30 == 0 ? 0.84f : 0.88f), rad * 0.98f, t == 0 ? 3f : 1.5f, DialWhite);
            Spoke(c, -bank, br * 0.8f, br, 3f, DialAmber);
            // the aircraft: a wing each side of a dot
            Fill(new Rect(c.x - br * 0.62f, c.y - 1.5f, br * 0.42f, 3), DialAmber);
            Fill(new Rect(c.x + br * 0.2f, c.y - 1.5f, br * 0.42f, 3), DialAmber);
            Fill(new Rect(c.x - 2.5f, c.y - 2.5f, 5, 5), DialAmber);
            GUI.Label(new Rect(d.x - 30, d.yMax - 2, d.width + 60, 12), $"PITCH {pitch:+0;-0;0}  BANK {Mathf.Abs(bank):0}", dialName);
        }

        static void BuildAttitudeBall(float pitch, float bank)
        {
            const int S = 96;
            if (attTex == null) attTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[S * S];
            float h = (S - 1) / 2f, sb = Mathf.Sin(bank * Mathf.Deg2Rad), cb = Mathf.Cos(bank * Mathf.Deg2Rad);
            float per10 = 0.25f;                       // the ball's radius is 40 degrees of pitch
            float horizon = -pitch / 40f, line = 1.3f / h;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - h) / h, dy = (y - h) / h, r = Mathf.Sqrt(dx * dx + dy * dy);   // y up (textures go bottom up)
                    if (r > 1) { px[y * S + x] = Color.clear; continue; }
                    // in the ball's frame: rolled with the bank (right wing down tips the horizon's right end up, as the
                    // world looks from a plane banked right; the sky pointer turns the same way)
                    float u = dx * cb + dy * sb, v = -dx * sb + dy * cb;
                    Color col = v > horizon ? AttSky : AttGround;
                    if (Mathf.Abs(v - horizon) < line) col = Color.white;
                    for (int n = -2; n <= 2; n++)
                    {
                        if (n == 0) continue;
                        float half = n % 2 == 0 ? 0.32f : 0.18f;
                        if (Mathf.Abs(v - (horizon + n * per10)) < line * 0.7f && Mathf.Abs(u) < half) col = Color.white;
                    }
                    col.a = Mathf.Clamp01((1 - r) * h);   // a smooth edge
                    px[y * S + x] = col;
                }
            attTex.SetPixels(px);
            attTex.Apply();
        }

        /// <summary>
        /// What system light k (0 engine, 1 fuel, 2 flight controls, 3 wings) shows for the player's plane: its lamp's
        /// colour (green whole, amber damaged, red out) and its text. The systems damage work fills this in.
        /// </summary>
        partial void SystemLight(int k, ref Color lamp, ref string text);

        /// <summary>A line from c at angle (degrees clockwise from straight up), from r0 to r1 out.</summary>
        static void Spoke(Vector2 c, float angleDeg, float r0, float r1, float width, Color col)
        {
            var old = GUI.matrix;
            RotateGui(angleDeg, c);
            Fill(new Rect(c.x - width / 2, c.y - r1, width, r1 - r0), col);
            GUI.matrix = old;
        }

        /// <summary>
        /// Turn what is drawn next by deg (clockwise) about a GUI point. GUIUtility.RotateAroundPivot puts the pivot in
        /// the wrong place once GUI.matrix scales, as it does on screens taller than 900 px: the dials' needles and ticks
        /// and the MFD's funnel lines then land all over the screen.
        /// </summary>
        static void RotateGui(float deg, Vector2 pivot)
        {
            GUI.matrix = GUI.matrix * Matrix4x4.TRS(pivot, Quaternion.Euler(0, 0, deg), Vector3.one) * Matrix4x4.Translate(-pivot);
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
            // the name up in the dial's middle, clear of the numbers round the edge
            GUI.Label(new Rect(d.x, c.y - rad * 0.36f - 6, d.width, 12), unit != null ? $"{name} {unit}" : name, dialName);
        }

        static readonly Color DialWhite = new Color(0.95f, 0.95f, 0.9f), DialRed = new Color(1f, 0.25f, 0.2f), DialAmber = new Color(1f, 0.75f, 0.2f);

        /// <summary>Airspeed: 0 to 500 kt once round, clockwise from the top; a tick each 25 kt, a number each 100.</summary>
        void AirspeedDial(Rect d, float kias)
        {
            DialFace(d, "KT", "x10", out var c, out float r);
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

        /// <summary>The G meter: -3 to +9 round three quarters of the dial (from seven o'clock to five), red past the airframe's limit.</summary>
        void GDial(Rect d, float g, float maxG)
        {
            DialFace(d, "G", null, out var c, out float r);
            float A(float gg) => -135f + (Mathf.Clamp(gg, -3, 9) + 3) / 12f * 270f;
            for (int gg = -3; gg <= 9; gg++)
                Spoke(c, A(gg), r * (gg % 2 == 0 ? 0.78f : 0.86f), r * 0.98f, gg == 1 ? 3f : 1.5f, gg > maxG ? DialRed : DialWhite);
            // the limit: a red arc of short ticks from the airframe's limit up
            for (float gg = Mathf.Max(-3, maxG); gg <= 9; gg += 0.25f) Spoke(c, A(gg), r * 0.92f, r * 0.98f, 2f, DialRed);
            if (r > 34) foreach (int gg in new[] { -2, 0, 2, 4, 6, 8 }) DialNumber(c, A(gg), r * 0.6f, gg.ToString());
            Spoke(c, A(g), -r * 0.12f, r * 0.9f, 2.5f, g > maxG ? DialRed : DialAmber);
            Fill(new Rect(c.x - 3, c.y - 3, 6, 6), Color.black);
            GUI.Label(new Rect(d.x, d.yMax - 2, d.width, 12), $"{g:F1} G", dialName);
        }
    }
}
