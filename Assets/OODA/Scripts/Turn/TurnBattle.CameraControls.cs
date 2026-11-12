using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.Turn
{
    /// <summary>
    /// The camera's extra controls, in planning, playback and the replay: Home (not in the replay, where it rewinds),
    /// a double-click on the view or the Recenter button puts it back behind your aircraft; Alt+drag or the middle
    /// button pans the pivot off it; Alt+V or the Follow button turns heading-follow on or off.
    /// </summary>
    public partial class TurnBattle
    {
        float lastTapTime = -10f;
        Vector2 lastTapPos;

        static bool AltHeld(Keyboard kb) => kb != null && kb.altKey.isPressed;

        void CameraKeys(Keyboard kb, Gamepad pad)
        {
            if (view == null || kb == null) return;
            if (kb[Key.Home].wasPressedThisFrame && !replaying) RecenterView();
            if (kb[Key.V].wasPressedThisFrame && AltHeld(kb)) ToggleFollow();
        }

        /// <summary>A drag on the view turns it, or with Alt held moves it across.</summary>
        void OrbitOrPan(Keyboard kb, Vector2 pixels)
        {
            if (AltHeld(kb)) view.Pan(pixels);
            else view.Orbit(pixels * 0.25f);
        }

        /// <summary>True when this press is the second of a quick pair in nearly the same place.</summary>
        bool DoubleTap(Vector2 pos)
        {
            bool twice = Time.unscaledTime - lastTapTime < 0.3f && (pos - lastTapPos).sqrMagnitude < 24f * 24f;
            lastTapTime = twice ? -10f : Time.unscaledTime;
            lastTapPos = pos;
            return twice;
        }

        void RecenterView()
        {
            if (view == null) return;
            view.Recenter();
            Notice("View recentered");
        }

        void ToggleFollow()
        {
            if (view == null) return;
            view.followHeading = !view.followHeading;
            Notice(view.followHeading ? "View follows your heading (Alt+V)" : "View holds its direction (Alt+V)");
        }

        /// <summary>The pair of buttons under View: recenter, and heading-follow on or off.</summary>
        void CameraButtons(float x, float y)
        {
            if (view == null) return;
            if (GUI.Button(new Rect(x, y, 87, 30), "Recenter")) RecenterView();
            if (GUI.Button(new Rect(x + 93, y, 87, 30), view.followHeading ? "Follow: on" : "Follow: off")) ToggleFollow();
        }
    }
}
