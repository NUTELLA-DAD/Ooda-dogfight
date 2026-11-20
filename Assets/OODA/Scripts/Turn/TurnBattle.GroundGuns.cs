using Ooda.Flight;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The ground guns, easy to pick out: a thin stalk up from each gun with a diamond on top, the flak's orange, the
    /// same size on screen however far off it is, so a gun on the valley floor reads against the grass and the snow
    /// from high or low. While a gun is firing its diamond brightens and a small star flashes at the gun. Drawn with
    /// the pooled lines (a few per gun in reach), nothing new in the scene.
    /// </summary>
    public partial class TurnBattle
    {
        [Header("Ground guns")]
        [Tooltip("Ground guns nearer the camera than this get a marker, m.")]
        public float gunMarkerShowM = 7000;
        [Tooltip("The marker's height as a share of the distance to it (so it keeps its size on screen).")]
        public float gunMarkerSize = 0.012f;
        public Color gunMarkerColor = new Color(1f, 0.5f, 0.12f, 0.75f);
        public Color gunMarkerFiringColor = new Color(1f, 0.85f, 0.35f, 1f);

        /// <summary>A marker over each ground gun in reach. result: the turn being played (to show which guns are firing at frame now), or null.</summary>
        void DrawGunMarkers(TurnResult result = null, int now = 0)
        {
            if (battle == null || battle.flak == null || battle.flak.Length == 0 || view == null) return;
            Transform cam = view.transform;
            Vector3 eye = cam.position, right = cam.right;
            float show2 = gunMarkerShowM * gunMarkerShowM;
            for (int k = 0; k < battle.flak.Length; k++)
            {
                var g = battle.flak[k];
                if (g == null || g.carrier >= 0) continue;   // a bomber's gunner rides the bomber: no marker
                Vector3 c = UnityFrame.Pos(g.pos);
                float d2 = (c - eye).sqrMagnitude;
                if (d2 > show2) continue;
                float s = Mathf.Max(1.5f, Mathf.Sqrt(d2) * gunMarkerSize);
                bool firing = result != null && Firing(result, -1 - k, now);
                Color col = firing ? gunMarkerFiringColor : gunMarkerColor;
                Vector3 top = c + Vector3.up * (s * 3f), mid = top + Vector3.up * s;
                lines.Line(c + Vector3.up * 0.5f, top, 0.35f, col);
                lines.Strip(new[] { top, mid + right * s, mid + Vector3.up * s, mid - right * s }, firing ? 0.9f : 0.6f, col, true);
                if (firing)
                {
                    // the muzzle flash: a small star at the gun
                    float f = s * 0.8f;
                    Vector3 at = c + Vector3.up * (s * 0.4f), up = Vector3.up;
                    lines.Line(at - right * f, at + right * f, 0.7f, gunMarkerFiringColor);
                    lines.Line(at - up * f * 0.5f, at + up * f, 0.7f, gunMarkerFiringColor);
                }
            }
        }

        /// <summary>Whether the gun with this owner number (-1 - k) has a round fresh out of the barrel at frame now.</summary>
        static bool Firing(TurnResult result, int owner, int now)
        {
            foreach (var tr in result.traces)
                if (tr.owner == owner && !tr.missile && now >= tr.startFrame && now < tr.startFrame + 4) return true;
            return false;
        }
    }
}
