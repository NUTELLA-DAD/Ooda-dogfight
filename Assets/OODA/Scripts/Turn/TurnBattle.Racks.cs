using System.Collections.Generic;
using Ooda.Turn.Core;

namespace Ooda.Turn
{
    /// <summary>
    /// The missiles under the wings (MissileRack on each view): as many as the plane was loaded with, less those fired.
    /// While planning they follow the battle's count; in playback and the replay each one leaves its rail the frame its
    /// missile's trail starts, so you see it go.
    /// </summary>
    public partial class TurnBattle
    {
        /// <summary>A plane's load at the start: its type's missiles (or more, if it was given more since).</summary>
        int Loaded(int i) => i < battle.combat.Length ? System.Math.Max(battle.combat[i].missiles, battle.missiles[i]) : 0;

        /// <summary>Planning (and anywhere the battle stands still): what each plane has left now.</summary>
        void RacksLive()
        {
            if (battle == null) return;
            for (int i = 0; i < views.Length && i < battle.missiles.Length; i++)
                if (views[i] != null) views[i].ShowMissiles(Loaded(i), battle.missiles[i]);
        }

        /// <summary>
        /// Playback of the last turn at frame now: what each plane has left after the turn (the battle's count), plus every
        /// missile it fires later in the turn, still on the rail.
        /// </summary>
        void RacksPlayback(TurnResult r, int now)
        {
            if (battle == null || r == null) return;
            for (int i = 0; i < views.Length && i < battle.missiles.Length; i++)
                if (views[i] != null) views[i].ShowMissiles(Loaded(i), battle.missiles[i] + LaunchesAfter(r, i, now));
        }

        /// <summary>The replay: the same from the tape (by tape index), with each turn's count after it.</summary>
        void RacksReplay(TapeTurn t, int now)
        {
            if (t.missilesAfter == null) return;
            for (int s = 0; s < tapeViews.Count && s < t.missilesAfter.Length; s++)
                if (tapeViews[s] != null && s < tapeLoaded.Count)
                    tapeViews[s].ShowMissiles(tapeLoaded[s], t.missilesAfter[s] + LaunchesAfter(t.result, s, now));
        }

        /// <summary>Missiles owner fires in the turn after frame now (a fresh launch: its trail starts there at age 0).</summary>
        static int LaunchesAfter(TurnResult r, int owner, int now)
        {
            int k = 0;
            foreach (var tr in r.traces)
                if (tr.missile && tr.owner == owner && tr.age0 < 1e-6 && tr.startFrame > now) k++;
            return k;
        }

        readonly List<int> tapeLoaded = new List<int>();
    }
}
