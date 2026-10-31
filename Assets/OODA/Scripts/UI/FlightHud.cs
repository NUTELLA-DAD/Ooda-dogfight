using Ooda.Flight;
using UnityEngine;

namespace Ooda.UI
{
    /// <summary>A plain readout for tuning: airspeed, height, G, throttle, and the stall and crash warnings. H shows the controls.</summary>
    public class FlightHud : MonoBehaviour
    {
        public AircraftController aircraft;
        public bool showHelp = true;

        GUIStyle big, small, warn;

        void Update()
        {
            if (aircraft != null && aircraft.Input != null && aircraft.Input.HelpPressed) showHelp = !showHelp;
        }

        void OnGUI()
        {
            if (aircraft == null || aircraft.Model == null) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                big.normal.textColor = new Color(0.75f, 1f, 0.75f);
                small = new GUIStyle(big) { fontSize = 14, fontStyle = FontStyle.Normal };
                warn = new GUIStyle(big) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
                warn.normal.textColor = new Color(1f, 0.35f, 0.25f);
            }
            var m = aircraft.Model;
            float x = 20, y = 20;
            GUI.Label(new Rect(x, y, 400, 30), $"{m.Kias,4:F0} KIAS   {m.Ktas,4:F0} KTAS", big);
            GUI.Label(new Rect(x, y + 30, 400, 30), $"{m.AltFt,6:F0} FT   {Vector3.Dot(aircraft.Velocity, Vector3.up) * 196.85f,6:F0} FT/MIN", big);
            GUI.Label(new Rect(x, y + 60, 400, 30), $"{m.g,4:F1} G   (stick {m.gAsked:F1}, wing {m.numbers.AvailableG(m.Kias):F1})", big);
            GUI.Label(new Rect(x, y + 90, 400, 30), $"THR {m.throttle * 100,3:F0}%{(aircraft.Input.Wep && aircraft.Input.Throttle > 0.95f ? "  WEP" : "")}", big);
            GUI.Label(new Rect(x, y + 120, 400, 24), $"Ps {m.excess * m.Ktas * 1.68781 * 60,6:F0} ft/min   roll {m.rollRateDps,4:F0} deg/s", small);

            if (aircraft.Crashed) GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60), "CRASHED", warn);
            else if (m.departure == Ooda.Flight.Core.Departure.Spin)
            {
                GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60), m.spinDir > 0 ? "SPIN RIGHT" : "SPIN LEFT", warn);
                GUI.Label(new Rect(0, Screen.height * 0.35f + 50, Screen.width, 30), $"throttle idle, {(m.spinDir > 0 ? "LEFT" : "RIGHT")} rudder, stick forward, hold", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            }
            else if (m.departure == Ooda.Flight.Core.Departure.Incipient) GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60), "DEPARTURE", warn);
            else if (m.stalled) GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60), "STALL", warn);

            if (showHelp)
            {
                GUI.Label(new Rect(x, Screen.height - 150, 900, 140),
                    "Gamepad: left stick fly, bumpers rudder, triggers throttle, A war emergency power, right stick look, Y camera, Start reset\n" +
                    "Keyboard: W/S pitch (S pulls), A/D roll, Q/E rudder, Shift/Ctrl throttle, Space WEP,\n" +
                    "          right mouse look (chase), mouse look (cockpit), C camera, Backspace reset, H hide this", small);
            }
        }
    }
}
