using Ooda.Audio;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The battle's sound (Audio/GameAudio.cs): your engine and the wind, louder while the turn plays and with the
    /// throttle; a burst for each packet of rounds as it leaves the guns, a rush for a missile; a clang for a hit and a
    /// bang for a kill, each from where it happens; two pips for Fly turn; a horn when the plan first goes into the
    /// overpull; and a fanfare when the fight ends. The whole-fight replay plays the same sounds.
    /// </summary>
    public partial class TurnBattle
    {
        Phase soundPhase;
        bool soundOverpull, soundStarted;

        /// <summary>Once a frame, whatever the battle is doing (not while the pause menu is up: the listener is paused then).</summary>
        void SoundUpdate()
        {
            if (battle == null) return;
            bool playing = replaying || phase == Phase.Playing;
            bool mineOut = battle.Out(0);
            float throttle = player != null && player.Model != null ? (float)player.Model.throttle : 1f;
            // while planning the engine idles under the talk of the cockpit; when the turn flies it comes up
            float level = phase == Phase.Over && !replaying ? 0.15f : playing ? 1f : 0.3f;
            if (mineOut && !replaying) level *= 0.3f;
            GameAudio.Engine(briefing ? 0f : level, throttle, Jet, briefing ? 0f : playing ? 0.8f : 0.3f);

            if (!soundStarted) { soundStarted = true; soundPhase = phase; return; }   // nothing for the state the scene opens in
            if (phase != soundPhase)
            {
                if (phase == Phase.Thinking && soundPhase == Phase.Planning) GameAudio.Play(Sfx.Commit, 0.5f);
                if (phase == Phase.Over && outcome != null && !outcome.Contains("draw")) GameAudio.Play(outcome.Contains("win") ? Sfx.Win : Sfx.Lose, 0.6f);
                soundPhase = phase;
            }
            // the horn as the plan first crosses into the overpull (the stick shaker is the feel of it; this is the sound)
            bool over = phase == Phase.Planning && warning != null && (warning.StartsWith("OVERPULL") || warning.StartsWith("SPINNING"));
            if (over && !soundOverpull) GameAudio.Play(Sfx.Warning, 0.35f);
            soundOverpull = over;
        }

        int soundFrame = -1, soundFrameBefore = -1;

        /// <summary>
        /// A packet of rounds or a missile at playback frame now: its sound as playback passes the frame it leaves on
        /// (playback may step over frames when it runs fast, and holds one when it is paused).
        /// </summary>
        void SoundForTrace(Trace tr, int now)
        {
            if (now != soundFrame) { soundFrameBefore = now < soundFrame ? now - 1 : soundFrame; soundFrame = now; }   // (back at the start: a new turn, or the replay wound back)
            if (tr.startFrame <= 0 || tr.startFrame <= soundFrameBefore || tr.startFrame > now || tr.points.Count == 0) return;   // (startFrame 0: already in the air when the turn began)
            Vector3 at = UnityFrame.Pos(tr.points[0]);
            if (tr.missile) GameAudio.PlayAt(Sfx.MissileLaunch, at, 0.9f);
            else GameAudio.PlayAt(Sfx.Gun, at, tr.owner == 0 ? 0.8f : 0.6f, tr.owner < 0 ? 0.7f : 0.94f + 0.03f * (tr.startFrame % 5));   // flak is deeper; the guns vary a little
        }
    }
}
