using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Pilot
{
    /// <summary>
    /// The pilots a fighter can carry, each already seated in its cockpit by the builder, only one of them shown: the
    /// type's usual one (the tiger in the P-51, the wolf in the Spitfire and the Bf 109) unless the dogfight briefing
    /// picked another. They share one rig and one set of clips, so each sits and flies the same way. Call <see cref="Use"/>
    /// before anything looks the pilot up (the camera's head hiding, the gestures): the others are inactive.
    /// </summary>
    public class PilotRoster : MonoBehaviour
    {
        public PilotKind usual = PilotKind.Wolf;
        public GameObject tiger, wolf, wizard, skeleton;

        public GameObject For(PilotKind k)
        {
            var g = Slot(k == PilotKind.Usual ? usual : k);
            if (g == null) g = Slot(usual);                       // a pilot this plane lacks: its usual one
            if (g == null) foreach (var any in new[] { tiger, wolf, wizard, skeleton }) if (any != null) return any;
            return g;
        }

        GameObject Slot(PilotKind k) =>
            k == PilotKind.Tiger ? tiger : k == PilotKind.Wolf ? wolf : k == PilotKind.Wizard ? wizard : k == PilotKind.Skeleton ? skeleton : null;

        /// <summary>Show pilot k (Usual: the type's own) and hide the rest.</summary>
        public void Use(PilotKind k)
        {
            var on = For(k);
            foreach (var g in new[] { tiger, wolf, wizard, skeleton })
                if (g != null) g.SetActive(g == on);
        }
    }
}
