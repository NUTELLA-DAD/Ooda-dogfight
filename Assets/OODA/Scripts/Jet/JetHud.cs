using Ooda.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Ooda.UI
{
    /// <summary>The jet test's readout: airspeed and Mach big, height, climb, G, the throttle and AFTERBURNER, the best Mach so far. Esc goes back to the title.</summary>
    public class JetHud : MonoBehaviour
    {
        public AircraftController aircraft;
        public string titleScene = "Title";
        public bool showHelp = true;

        GUIStyle big, mid, small, burner, warn;
        double bestMach;

        void Update()
        {
            if (aircraft == null || aircraft.Model == null) return;
            if (aircraft.Input.HelpPressed) showHelp = !showHelp;
            if (aircraft.Input.ResetPressed) bestMach = 0;
            if (!aircraft.Crashed) bestMach = System.Math.Max(bestMach, aircraft.Model.Mach);
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) BackToTitle();
        }

        void BackToTitle()
        {
            if (Application.CanStreamedLevelBeLoaded(titleScene)) SceneManager.LoadScene(titleScene);
        }

        void OnGUI()
        {
            if (aircraft == null || aircraft.Model == null) return;
            BriefingStyle.Use();
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold };
                big.normal.textColor = BriefingStyle.BrightGreen;
                mid = new GUIStyle(big) { fontSize = 22 };
                small = new GUIStyle(big) { fontSize = 14, fontStyle = FontStyle.Normal };
                burner = new GUIStyle(big) { fontSize = 30 };
                burner.normal.textColor = BriefingStyle.Amber;
                warn = new GUIStyle(big) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
                warn.normal.textColor = new Color(1f, 0.35f, 0.25f);
            }
            var m = aircraft.Model;
            float x = 24, y = 20;
            GUI.Label(new Rect(x, y, 600, 48), $"MACH {m.Mach:F2}", big);
            GUI.Label(new Rect(x, y + 50, 600, 30), $"{m.Kias,4:F0} KIAS   {m.Ktas,4:F0} KTAS   {m.Ktas * 1.15078,5:F0} MPH", mid);
            GUI.Label(new Rect(x, y + 80, 600, 30), $"{m.AltFt,6:F0} FT   {Vector3.Dot(aircraft.Velocity, Vector3.up) * 196.85f,7:F0} FT/MIN", mid);
            GUI.Label(new Rect(x, y + 110, 600, 30), $"{m.g,4:F1} G   THR {m.throttle * 100,3:F0}%", mid);
            GUI.Label(new Rect(x, y + 140, 600, 24), $"best this flight  MACH {bestMach:F2}", small);
            bool lit = aircraft.Input.Wep && m.throttle > 0.95;
            if (lit) GUI.Label(new Rect(x, y + 166, 600, 40), "AFTERBURNER", burner);
            else if (aircraft.Input.Wep) GUI.Label(new Rect(x, y + 166, 600, 40), "AFTERBURNER (spooling)", burner);

            if (GUI.Button(new Rect(Screen.width - 170, 20, 150, 40), "Title")) BackToTitle();

            if (aircraft.Crashed) GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60), "CRASHED", warn);
            else if (m.Departed) GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60), "DEPARTED: throttle back, stick forward", warn);
            else if (m.stalled) GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 60), "STALL", warn);

            if (showHelp)
            {
                GUI.Label(new Rect(x, Screen.height - 130, 1000, 120),
                    "Space or pad A: afterburner on / off (pushes the throttle to full; pulling it back puts it out)\n" +
                    "Keyboard: W/S pitch (S pulls), A/D roll, Q/E rudder, Shift/Ctrl throttle, C camera, Backspace restart, Esc title, H hide this\n" +
                    "Gamepad: left stick fly, bumpers rudder, triggers throttle, right stick look, Y camera, Start restart", small);
            }
        }
    }
}
