using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The planning buttons as cockpit switches: each sits on its own dark panel plate with a lamp at the top that
    /// lights when it is on, a metal toggle bat that flips up for on and down for off (a square push button for one-shot
    /// actions, a red flip-up guard over the missile's arm switch), and a stencilled name and key under it.
    /// The engine's switches (rocket, speed brake, and the burner on a jet) sit by the throttle; the rest (IR, flares,
    /// the specials, the gun sight toggles and the timeline) in a row over the weapons panel.
    /// </summary>
    public partial class TurnBattle
    {
        enum SwitchKind { Toggle, Push, Guarded }

        static readonly Color SwPlate = new Color(0.11f, 0.13f, 0.12f, 0.94f), SwEdge = new Color(0.36f, 0.4f, 0.36f, 1f),
                              SwMetal = new Color(0.8f, 0.82f, 0.84f, 1f), SwShade = new Color(0.42f, 0.44f, 0.46f, 1f),
                              SwHole = new Color(0.03f, 0.03f, 0.03f, 1f), SwLampOff = new Color(0.2f, 0.2f, 0.17f, 1f),
                              SwText = new Color(0.93f, 0.91f, 0.8f, 1f), SwGuard = new Color(0.78f, 0.1f, 0.08f, 1f);
        const float SwitchH = 58, SwitchGap = 6, SwitchMaxW = 66;
        GUIStyle swName, swKey;

        /// <summary>One cockpit switch on its plate in r. True when clicked (and enabled).</summary>
        bool CockpitSwitch(Rect r, string name, string key, bool on, bool enabled, Color lamp, SwitchKind kind = SwitchKind.Toggle)
        {
            if (swName == null)
            {
                swName = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
                swName.font = Ooda.UI.BriefingStyle.Stencil;
                swKey = new GUIStyle(swName) { fontSize = 10, font = null };
            }
            guiRects.Add(r);
            float dim = enabled ? 1f : 0.45f;
            Fill(r, SwEdge);
            Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), SwPlate);
            // the four screws
            foreach (var (sx, sy) in new[] { (r.x + 3, r.y + 3), (r.xMax - 5, r.y + 3), (r.x + 3, r.yMax - 5), (r.xMax - 5, r.yMax - 5) })
                Fill(new Rect(sx, sy, 2, 2), SwShade);

            // the lamp
            Rect lr = new Rect(r.center.x - 6, r.y + 4, 12, 6);
            Fill(new Rect(lr.x - 1, lr.y - 1, lr.width + 2, lr.height + 2), SwHole);
            Fill(lr, on ? lamp : SwLampOff);
            if (on) Fill(new Rect(lr.x - 3, lr.y - 2, lr.width + 6, lr.height + 4), new Color(lamp.r, lamp.g, lamp.b, 0.25f));

            // the switch itself, between the lamp and the name
            float top = r.y + 13, bottom = r.yMax - 25, mid = (top + bottom) / 2;
            float cx = r.center.x;
            if (kind == SwitchKind.Push)
            {
                Rect cap = new Rect(cx - 11, mid - 8, 22, 16);
                Fill(new Rect(cap.x - 2, cap.y - 2, cap.width + 4, cap.height + 4), SwHole);
                Fill(cap, on ? Color.Lerp(SwShade, lamp, 0.6f) : SwShade);
                Fill(new Rect(cap.x, cap.y, cap.width, 3), Color.Lerp(SwMetal, Color.white, 0.3f) * new Color(1, 1, 1, dim));
            }
            else
            {
                // the nut the bat comes out of, and the bat: up for on, down for off, its ball end catching the light
                Fill(new Rect(cx - 7, mid - 3, 14, 6), SwHole);
                Fill(new Rect(cx - 5, mid - 2, 10, 4), SwShade);
                float len = (bottom - top) / 2 - 1;
                Color bat = Color.Lerp(SwPlate, SwMetal, dim);
                if (on) { Fill(new Rect(cx - 3, mid - len, 6, len), bat); Fill(new Rect(cx - 5, mid - len - 2, 10, 6), Color.white * new Color(1, 1, 1, dim)); }
                else { Fill(new Rect(cx - 3, mid, 6, len), bat); Fill(new Rect(cx - 5, mid + len - 4, 10, 6), Color.white * new Color(1, 1, 1, dim)); }
                if (kind == SwitchKind.Guarded)
                {
                    if (on) Fill(new Rect(cx - 10, top - 3, 20, 3), SwGuard);   // the guard flipped up out of the way
                    else
                    {
                        // the guard down over the switch, striped
                        Rect g = new Rect(cx - 10, top, 20, bottom - top);
                        Fill(g, new Color(SwGuard.r, SwGuard.g, SwGuard.b, 0.6f * (enabled ? 1f : 0.6f)));   // see-through: the switch shows under it
                        Fill(new Rect(g.x, g.y, g.width, 2), SwGuard); Fill(new Rect(g.x, g.yMax - 2, g.width, 2), SwGuard); Fill(new Rect(g.x, g.y, 2, g.height), SwGuard); Fill(new Rect(g.xMax - 2, g.y, 2, g.height), SwGuard);
                    }
                }
            }

            // the name and the key, stencilled under it
            swName.fontSize = r.width < 56 ? 9 : 11;   // a long name on a narrow plate
            swName.normal.textColor = new Color(SwText.r, SwText.g, SwText.b, dim);
            swKey.normal.textColor = new Color(SwText.r * 0.7f, SwText.g * 0.75f, SwText.b * 0.7f, dim);
            GUI.Label(new Rect(r.x + 1, r.yMax - 25, r.width - 2, 13), name, swName);
            GUI.Label(new Rect(r.x + 1, r.yMax - 13, r.width - 2, 11), key, swKey);

            if (controlsLocked) return false;
            bool was = GUI.enabled;
            GUI.enabled = was && enabled;
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            GUI.enabled = was;
            return hit;
        }

        /// <summary>A switch to draw in a row: what it shows and what a click does.</summary>
        struct SwitchSpec
        {
            public string name, key;
            public bool on, enabled;
            public Color lamp;
            public SwitchKind kind;
            public Action click;
        }

        static SwitchSpec Sw(string name, string key, bool on, bool enabled, Color lamp, Action click, SwitchKind kind = SwitchKind.Toggle)
            => new SwitchSpec { name = name, key = key, on = on, enabled = enabled, lamp = lamp, click = click, kind = kind };

        /// <summary>The switches side by side from x (right-aligned to x + w when alignRight), each as wide as fits up to SwitchMaxW.</summary>
        void SwitchRow(float x, float y, float w, List<SwitchSpec> row, bool alignRight = false)
        {
            if (row.Count == 0) return;
            float sw = Mathf.Min(SwitchMaxW, (w - SwitchGap * (row.Count - 1)) / row.Count);
            float total = row.Count * sw + (row.Count - 1) * SwitchGap;
            float sx = alignRight ? x + w - total : x;
            Action clicked = null;
            foreach (var s in row)
            {
                if (CockpitSwitch(new Rect(sx, y, sw, SwitchH), s.name, s.key, s.on, s.enabled, s.lamp, s.kind)) clicked = s.click;
                sx += sw + SwitchGap;
            }
            clicked?.Invoke();
        }

        static readonly Color LampGreen = new Color(0.3f, 1f, 0.35f), LampAmber = new Color(1f, 0.7f, 0.15f),
                              LampRed = new Color(1f, 0.2f, 0.12f), LampBlue = new Color(0.45f, 0.8f, 1f);

        /// <summary>The engine's switches under the throttle slider: the rocket, the speed brake, and a jet's burner.</summary>
        void EngineSwitches(float x, float y, float w)
        {
            var m = battle.planes[0];
            int turns = RocketTurns(m);
            var row = new List<SwitchSpec>();
            // no rocket switch on a plane that doesn't carry one (a jet, or a loadout without it)
            if (m.numbers.rocketG > 0)
                row.Add(Sw("ROCKET", plan.rocket ? "1 lit" : m.RocketBurning ? "burning" : turns == 0 ? "1 ready" : $"{turns} turn{(turns == 1 ? "" : "s")}",
                           plan.rocket, plan.rocket || m.RocketReady, LampAmber, ToggleRocket));
            row.Add(Sw("BRAKE", "2", plan.brake, true, LampBlue, ToggleBrake));
            if (Jet) row.Add(Sw("BURNER", "Space", plan.wep, true, LampRed, () => { plan.wep = !plan.wep; dirty = true; }));
            SwitchRow(x, y, w, row);
        }

        /// <summary>The weapon switches, in the corner past the gun MFD: flare hold and the specials (Arcade). IR, the funnel, the ring and the timeline are the MFD's buttons.</summary>
        void WeaponSwitches(float x, float y, float w)
        {
            var row = new List<SwitchSpec>
            {
                Sw(battle.combat[0].chaff > 0 ? "CM HOLD" : "FLR HOLD", battle.combat[0].chaff > 0 ? $"3  ({battle.flares[0]}/{battle.chaff[0]})" : $"3  ({battle.flares[0]})",
                   plan.holdFlares, battle.flares[0] + battle.chaff[0] > 0, LampAmber, ToggleFlares),
            };
            var load = battle.combat[0];
            // the specials this plane's loadout brought (all four with no loadout picked)
            if (load.balloonMines > 0)
                row.Add(Sw("MINE", $"4  ({battle.balloonMines[0]})", plan.mine, plan.mine || battle.balloonMines[0] > 0, mineColor, ToggleMine));
            if (load.heatBlades > 0)
                row.Add(Sw("BLADE", $"5  ({battle.heatBlades[0]})", plan.ram, plan.ram || battle.heatBlades[0] > 0, bladeColor, ToggleBlade));
            if (load.stealths > 0)
                row.Add(Sw(battle.Cloaked(0) ? "STEALTHY" : "STEALTH", $"6  ({battle.stealths[0]})", plan.cloak, plan.cloak || battle.stealths[0] > 0, new Color(stealthColor.r, stealthColor.g, stealthColor.b, 1f), ToggleCloak));
            if (load.jammers > 0)
                row.Add(Sw("JAM", $"7  ({battle.jammers[0]})", plan.jam, plan.jam || battle.jammers[0] > 0, jamColor, ToggleJam));
            if (load.wells > 0)
                row.Add(Sw("WELL", $"8  ({battle.wells[0]})", plan.well, plan.well || battle.wells[0] > 0, wellColor, ToggleWell));
            if (load.slowBombs > 0)
                row.Add(Sw("SLOW", $"Y  ({battle.slowBombs[0]})", plan.slow, plan.slow || battle.slowBombs[0] > 0, slowColor, ToggleSlow));
            if (load.blinks > 0)
                row.Add(Sw("BLINK", $"Y  ({battle.blinks[0]})", plan.blink, plan.blink || battle.blinks[0] > 0, blinkColor, ToggleBlink));
            if (load.mirrors > 0)
                row.Add(Sw(battle.Mirrored(0) ? "SHIELD UP" : "SHIELD", $"/  ({battle.mirrors[0]})", plan.mirror, plan.mirror || battle.mirrors[0] > 0, mirrorColor, ToggleMirror));
            SwitchRow(x, y, w, row, true);
            if (JammedNow)
            {
                var w2 = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
                w2.normal.textColor = Mathf.Repeat(Time.unscaledTime, 0.7f) < 0.45f ? jamColor : Color.white;
                Shadow(new Rect(x, y - 24, w, 22), "JAMMED: their ghosts are scrambled. Trust your eyes, not the projections.", w2);
            }
        }

        /// <summary>A jet: its throttle's last notch is the afterburner. A piston fighter has no WEP switch now.</summary>
        bool Jet => battle != null && battle.planes[0].numbers.thrustV0Kt > 1000;

        /// <summary>
        /// No WEP switch on a piston fighter: the throttle's last notch is war emergency power, so full throttle runs
        /// on WEP (the figures the types are fitted to). A jet keeps its afterburner as its own switch (Space).
        /// </summary>
        void SyncWep()
        {
            if (Jet || bot || battle == null || phase != Phase.Planning || plan.HasMarks) return;
            bool w = plan.throttle >= 0.995;
            if (w != plan.wep) { plan.wep = w; dirty = true; }
        }
    }
}
