using UnityEngine;

namespace Ooda.App
{
    /// <summary>
    /// What the game remembers about the player between runs (PlayerPrefs): how far through training they are, whether
    /// they chose to skip it, and whether they like the help panel open. Nothing here changes a fight.
    /// </summary>
    public static class Progress
    {
        const string LessonsKey = "Training.LessonsDone", SkippedKey = "Training.Skipped", HelpKey = "Help.Open";

        /// <summary>How many training lessons have been finished, counting from the first without a gap (0: none yet).</summary>
        public static int LessonsDone => PlayerPrefs.GetInt(LessonsKey, 0);

        /// <summary>Lesson number lesson (0 the first) was finished.</summary>
        public static void LessonDone(int lesson)
        {
            if (lesson + 1 <= LessonsDone) return;
            PlayerPrefs.SetInt(LessonsKey, lesson + 1);
            PlayerPrefs.Save();
        }

        /// <summary>The player said no to training when first offered it: not asked again.</summary>
        public static bool SkippedTraining
        {
            get => PlayerPrefs.GetInt(SkippedKey, 0) != 0;
            set { PlayerPrefs.SetInt(SkippedKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>A player who has neither flown a lesson nor declined: offer training before their first fight.</summary>
        public static bool OfferTraining => LessonsDone == 0 && !SkippedTraining;

        /// <summary>The battle's help panel: closed until the player opens it, then as they left it.</summary>
        public static bool HelpOpen
        {
            get => PlayerPrefs.GetInt(HelpKey, 0) != 0;
            set { PlayerPrefs.SetInt(HelpKey, value ? 1 : 0); }
        }
    }
}
