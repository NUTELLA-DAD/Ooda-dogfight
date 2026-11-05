using System;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.Turn
{
    /// <summary>
    /// Training, the coach on screen: Training on the title screen (or -tutorial on the command line) opens the dogfight
    /// with the lessons of <see cref="Tutorial"/>. Each lesson is a one on one built from the lesson's setup and armed as it
    /// says (a drone with no pilot holds its course). A panel on the left shows the lesson, what to do now and the goals,
    /// ticking each one off when <see cref="Tutorial.Met"/> says so (checked between turns, not while one plays). Next and
    /// Back move between lessons (Page Down, Page Up), Retry (or R once it is over) flies the lesson again.
    /// </summary>
    public partial class TurnBattle
    {
        /// <summary>Set by the title screen before loading the battle: open in training.</summary>
        public static bool TutorialRequested;
        /// <summary>The lesson to begin at (0 the first).</summary>
        public static int TutorialStartLesson;

        bool tutorial;
        int lessonNo, stepNo;
        bool lessonDone;
        int stepTurnStart, hardTurns;
        double foeWearAtStep;
        bool hardAtCommit;
        bool coachFolded;
        GUIStyle coachHead, coachBody, coachDim, coachGoal, coachDone, coachNow, coachNext;

        Tutorial.Lesson CurrentLesson => Tutorial.Get(lessonNo);

        /// <summary>In Start, before the first Restart: training if the title screen asked for it.</summary>
        void TutorialSetUp()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-tutorial");
            if (at >= 0)
            {
                TutorialRequested = true;
                if (at + 1 < args.Length && int.TryParse(args[at + 1], out int n)) TutorialStartLesson = n - 1;   // -tutorial 5: the fifth lesson
            }
            if (!TutorialRequested || bot) return;
            TutorialRequested = false;
            tutorial = true;
            mission = Mission.Dogfight;
            showHelp = false;   // the coach says what you need
            lessonNo = Mathf.Clamp(TutorialStartLesson, 0, Tutorial.Count - 1);
        }

        /// <summary>The lesson's one on one (Restart builds it in place of the dogfight's).</summary>
        Battle TutorialBattle()
        {
            dogSetup = Tutorial.Setup(CurrentLesson);
            var b = DogfightBattle();
            Tutorial.Arm(b, CurrentLesson);
            return b;
        }

        /// <summary>In Restart, once the pilots are made: the foe's pilot for the lesson (none for the drone), and the first step.</summary>
        void TutorialPilots()
        {
            if (!tutorial) return;
            for (int i = 1; i < ais.Length; i++) if (battle.side[i] != battle.side[0]) ais[i] = Tutorial.Pilot(CurrentLesson, ais[i]);
            stepNo = 0;
            lessonDone = false;
            coachFolded = false;
            StepBegins();
        }

        void StepBegins()
        {
            stepTurnStart = battle.turn;
            hardTurns = 0;
            foeWearAtStep = battle.Count > 1 ? battle.SystemsOf(1).Total : 0;
        }

        /// <summary>At Fly turn: whether this turn was planned with a hard pull, out of the red.</summary>
        void TutorialCommitted()
        {
            if (!tutorial) return;
            hardAtCommit = Tutorial.PullsHard(plan, env);
        }

        /// <summary>Once a turn is flown (before it plays back).</summary>
        void TutorialTurnFlown()
        {
            if (!tutorial) return;
            if (hardAtCommit) hardTurns++;
            hardAtCommit = false;
        }

        /// <summary>The fight is over: the foe down with you flying finishes the lesson whatever step it was on; else, try again.</summary>
        void TutorialFightOver()
        {
            if (!tutorial || outcome == null) return;
            if (!battle.Out(0) && battle.Count > 1 && battle.Out(1))
            {
                lessonDone = true;
                stepNo = CurrentLesson.steps.Length;
                outcome = lessonNo == Tutorial.Count - 1 ? "Training complete!" : "Lesson complete!";
            }
            else outcome += "  Try the lesson again.";
        }

        /// <summary>Every frame: tick the goal off when it is met (between turns), and the lesson keys.</summary>
        void TutorialUpdate()
        {
            if (!tutorial || battle == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.pageDownKey.wasPressedThisFrame) { NextLesson(1); return; }
            if (kb != null && kb.pageUpKey.wasPressedThisFrame) { NextLesson(-1); return; }
            if (lessonDone || phase == Phase.Playing || phase == Phase.Thinking) return;
            var lesson = CurrentLesson;
            if (stepNo >= lesson.steps.Length) return;
            var step = lesson.steps[stepNo];
            int burstHits = 0;
            if (preview != null) foreach (var h in preview.hits) if (h.shooter == 0 && h.target == 1) burstHits++;
            var look = new Tutorial.Look
            {
                battle = battle, plan = plan, env = env, planning = phase == Phase.Planning, burstHits = burstHits,
                turns = battle.turn - stepTurnStart, hardTurns = hardTurns, foeWearAtStep = foeWearAtStep, foe = 1,
            };
            if (!Tutorial.Met(step, look)) return;
            stepNo++;
            if (stepNo >= lesson.steps.Length)
            {
                lessonDone = true;
                Notice(lessonNo == Tutorial.Count - 1 ? "Training complete!" : "Lesson complete!  Next lesson: Page Down, or the button.");
            }
            else { Notice($"Done: {step.todo}."); StepBegins(); }
        }

        /// <summary>To the next (or previous) lesson and fly it. Past the last one, back to the main menu.</summary>
        void NextLesson(int dir)
        {
            int to = lessonNo + dir;
            if (to >= Tutorial.Count) { GoToMainMenu(); return; }
            lessonNo = Mathf.Clamp(to, 0, Tutorial.Count - 1);
            Restart();
        }

        /// <summary>The coach on the left, under your plane's panel (where the help goes; the help is off in training).</summary>
        void TutorialGUI(float sw, float sh)
        {
            if (!tutorial) return;
            if (coachHead == null)
            {
                coachHead = new GUIStyle(title) { fontSize = 18, wordWrap = true };
                coachHead.normal.textColor = Ooda.UI.BriefingStyle.Amber;
                coachBody = new GUIStyle(label) { fontSize = 15, wordWrap = true };
                coachBody.normal.textColor = Color.white;
                coachDim = new GUIStyle(coachBody) { fontSize = 13, fontStyle = FontStyle.Italic };
                coachDim.normal.textColor = new Color(0.75f, 0.85f, 0.75f);
                coachGoal = new GUIStyle(label) { fontSize = 14 };
                coachGoal.normal.textColor = new Color(0.6f, 0.7f, 0.6f);
                coachDone = new GUIStyle(coachGoal);
                coachDone.normal.textColor = new Color(0.35f, 1f, 0.4f);
                coachNow = new GUIStyle(coachGoal) { fontStyle = FontStyle.Bold };
                coachNow.normal.textColor = Ooda.UI.BriefingStyle.Amber;
                coachNext = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                coachNext.normal.textColor = coachNext.hover.textColor = Ooda.UI.BriefingStyle.Amber;
            }
            var lesson = CurrentLesson;
            bool last = lessonNo == Tutorial.Count - 1;
            float w = Mathf.Min(440, sw * 0.42f), x = 10, y = helpTop, pad = 10, iw = w - 2 * pad;
            string head = $"TRAINING  {lessonNo + 1} of {Tutorial.Count}:  {lesson.title}";

            if (coachFolded)
            {
                Rect bar = new Rect(x, y, w, 36);
                GUI.Box(bar, GUIContent.none);
                guiRects.Add(bar);
                GUI.Label(new Rect(x + pad, y + 7, iw - 80, 24), head, coachGoal);
                if (GUI.Button(new Rect(x + w - pad - 70, y + 6, 70, 24), "Coach")) coachFolded = false;
                return;
            }

            // what the coach says now
            string intro = !lessonDone && stepNo == 0 ? lesson.intro : null;
            string say = lessonDone ? lesson.done : lesson.steps[Mathf.Min(stepNo, lesson.steps.Length - 1)].text;
            if (!lessonDone && phase == Phase.Over) say = "Shot down, or into the ground: Retry flies the lesson again.";
            float hh = coachHead.CalcHeight(new GUIContent(head), iw - 50);
            float ih = intro == null ? 0 : coachDim.CalcHeight(new GUIContent(intro), iw) + 4;
            float bh = coachBody.CalcHeight(new GUIContent(say), iw);
            float gh = lesson.steps.Length * 20;
            float total = pad + hh + 6 + ih + bh + 10 + gh + 10 + 30 + pad;
            Rect panel = new Rect(x, y, w, total);
            GUI.Box(panel, GUIContent.none);
            GUI.Box(panel, GUIContent.none);   // twice: a darker glass, to read over the sky
            guiRects.Add(panel);

            float cy = y + pad;
            GUI.Label(new Rect(x + pad, cy, iw - 50, hh), head, coachHead);
            if (GUI.Button(new Rect(x + w - pad - 44, cy, 44, 22), "Hide")) coachFolded = true;
            cy += hh + 6;
            if (intro != null) { GUI.Label(new Rect(x + pad, cy, iw, ih), intro, coachDim); cy += ih; }
            GUI.Label(new Rect(x + pad, cy, iw, bh), say, coachBody);
            cy += bh + 10;

            // the goals: done, now, still to come
            for (int k = 0; k < lesson.steps.Length; k++)
            {
                var s = lesson.steps[k];
                bool done = lessonDone || k < stepNo, now = !done && k == stepNo;
                string count = "";
                if (now && s.turns > 1)
                {
                    int had = s.goal == Tutorial.Goal.FlyHardTurn ? hardTurns : battle.turn - stepTurnStart;
                    count = $"  ({Mathf.Clamp(had, 0, s.turns)} of {s.turns})";
                }
                string mark = done ? "●" : now ? "▶" : "○";
                GUI.Label(new Rect(x + pad, cy, iw, 20), $"{mark}  {s.todo}{count}", done ? coachDone : now ? coachNow : coachGoal);
                cy += 20;
            }
            cy += 10;

            // the buttons: back, again, on
            float bw = (iw - 16) / 3;
            GUI.enabled = lessonNo > 0;
            if (GUI.Button(new Rect(x + pad, cy, bw, 28), "◀ Back  (PgUp)")) NextLesson(-1);
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + pad + bw + 8, cy, bw, 28), "Retry")) Restart();
            string next = lessonDone ? (last ? "Main menu" : "Next lesson ▶") : "Skip ▶  (PgDn)";
            if (GUI.Button(new Rect(x + pad + 2 * (bw + 8), cy, bw, 28), next, lessonDone ? coachNext : GUI.skin.button)) NextLesson(1);
        }
    }
}
