using Ooda.Flight.Core;
using Ooda.Turn.Core;

namespace Ooda.Turn
{
    /// <summary>
    /// The cloud sea (Turn/Core/Skyscape.cs, the Cloud Sea arena): a fight that begins out over it puts the clouds up
    /// (Fx/CloudScape, made the first time it is needed) and lets them white the view out as the camera flies into one; a
    /// fight anywhere else hides them and leaves the fog and the sky alone. Cloud hides a plane from eyes (Battle.SeesByEye,
    /// CloudBetween): an enemy in it, or behind it from your plane, loses its path, its ghosts and its marks (HiddenFromMe),
    /// the AI pilots lose sight of you (they hunt where you went in for a while, EnemyAi.cloudMemorySec) and the gunners
    /// hold fire; radar sees through it, and rounds fly through it. The clouds are not ground (the sea is, under the deck).
    /// </summary>
    public partial class TurnBattle
    {
        void CloudStart()
        {
            bool over = false;
            if (battle != null && battle.Count > 0)
            {
                var p = battle.planes[0].state.pos;
                over = Skyscape.Over(p.x * Atmosphere.MPerFt, p.y * Atmosphere.MPerFt);
            }
            var clouds = over ? Ooda.Fx.CloudScape.Ensure() : Ooda.Fx.CloudScape.Existing;
            if (clouds != null) clouds.Show(over, view != null ? view.Cam : null);
        }
    }
}
