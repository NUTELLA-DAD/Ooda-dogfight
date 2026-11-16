using UnityEngine;

namespace Ooda.UI
{
    /// <summary>
    /// The player's settings, the same three rows on the title screen and in the battle's pause menu: the volume, the
    /// display (full screen at the monitor's own resolution, or a window), and how sharp the enemy's pilots are. Each is
    /// kept between runs (the volume by GameAudio, the display by Unity itself, the pilots here in PlayerPrefs).
    /// </summary>
    public static class SettingsPanel
    {
        /// <summary>The rows' height in all, for whoever lays the panel out.</summary>
        public const float Height = 3 * RowH;
        const float RowH = 40;

        const string SkillKey = "Enemy.Skill";
        static readonly string[] SkillNames = { "Rookie", "Regular", "Ace" };
        // EnemyAi.sloppiness: the noise in a pilot's choice of move. 0.15 is the sharp pilot the game has always flown
        // against; training softens its foes to between 0.3 and 0.6.
        static readonly double[] SkillSloppiness = { 0.6, 0.3, 0.15 };

        /// <summary>0 rookie, 1 regular, 2 ace.</summary>
        public static int EnemySkill
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SkillKey, DefaultSkill), 0, SkillNames.Length - 1);
            set { PlayerPrefs.SetInt(SkillKey, Mathf.Clamp(value, 0, SkillNames.Length - 1)); PlayerPrefs.Save(); }
        }
        public const int DefaultSkill = 1;

        /// <summary>The enemy pilots' sloppiness for the chosen skill (see EnemyAi.sloppiness).</summary>
        public static double EnemySloppiness => SkillSloppiness[EnemySkill];
        public static string EnemySkillName => SkillNames[EnemySkill];

        static readonly Vector2Int[] Windows = { new Vector2Int(1600, 900), new Vector2Int(1280, 720) };

        static string DisplayName()
        {
            if (Screen.fullScreen) return $"Full screen {Screen.width} x {Screen.height}";
            return $"Window {Screen.width} x {Screen.height}";
        }

        /// <summary>Full screen at the monitor's resolution, then each window size that fits the monitor, then round again.</summary>
        static void NextDisplay()
        {
            int mw = Display.main.systemWidth, mh = Display.main.systemHeight;
            int at = -1;   // -1: full screen
            if (!Screen.fullScreen)
                for (int i = 0; i < Windows.Length; i++) if (Screen.width == Windows[i].x && Screen.height == Windows[i].y) at = i;
            if (Screen.fullScreen && (Screen.width != mw || Screen.height != mh)) { Screen.SetResolution(mw, mh, FullScreenMode.FullScreenWindow); return; }   // full screen but not the monitor's own: that first
            for (int step = 1; step <= Windows.Length + 1; step++)
            {
                int next = at + step;
                if (next >= Windows.Length) { Screen.SetResolution(mw, mh, FullScreenMode.FullScreenWindow); return; }
                if (Windows[next].x < mw && Windows[next].y < mh) { Screen.SetResolution(Windows[next].x, Windows[next].y, FullScreenMode.Windowed); return; }
            }
        }

        /// <summary>Draw the rows at x, y, w wide (Height tall). skillLocked: the pilots can't be changed here (mid-fight they are already flying).</summary>
        public static void Draw(float x, float y, float w, GUIStyle label, bool skillLocked = false)
        {
            const float lw = 150;
            var button = new GUIStyle(GUI.skin.button) { fontSize = label.fontSize };   // the buttons' text the size of the rows' labels
            float vol = Ooda.Audio.GameAudio.Volume;
            GUI.Label(new Rect(x, y + 6, lw, 24), $"Volume {vol * 100:F0}", label);
            float v = GUI.HorizontalSlider(new Rect(x + lw, y + 14, w - lw, 20), vol, 0f, 1f);
            if (Mathf.Abs(v - vol) > 0.004f) Ooda.Audio.GameAudio.Volume = v;
            y += RowH;
            GUI.Label(new Rect(x, y + 6, lw, 24), "Display", label);
            if (GUI.Button(new Rect(x + lw, y, w - lw, 32), DisplayName(), button)) NextDisplay();
            y += RowH;
            GUI.Label(new Rect(x, y + 6, lw, 24), "Enemy pilots", label);
            bool was = GUI.enabled;
            GUI.enabled = was && !skillLocked;
            if (GUI.Button(new Rect(x + lw, y, w - lw, 32), skillLocked ? $"{EnemySkillName} (next fight)" : EnemySkillName, button)) EnemySkill = (EnemySkill + 1) % SkillNames.Length;
            GUI.enabled = was;
        }
    }
}
