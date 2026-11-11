using System;
using System.Collections;
using Ooda.Flight;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -rocketshots (with -dogfight) on the built game's command line: pictures of the rocket boost next to the game, then it
    /// quits. rocket_0 the pilot before it lights; rocket_1 the pilot thrown back as it lights; rocket_2 it lighting, from
    /// the side; rocket_3 burning, from the chase camera; rocket_4 from the side; rocket_5 the pilot pinned back while it
    /// burns; rocket_6 burning out; rocket_7 a second burn from the cockpit. The enemy is put well away, out of the pictures.
    /// </summary>
    public partial class TurnBattle
    {
        bool rocketShotsStarted;

        void RocketShotsCheck()
        {
            if (rocketShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-rocketshots") < 0) return;
            rocketShotsStarted = true;
            StartCoroutine(RocketShots());
        }

        IEnumerator RocketShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name, float wait) { yield return new WaitForSecondsRealtime(wait); yield return Grab(System.IO.Path.Combine(dir, $"rocket_{name}.bmp")); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            IEnumerator PlayTo(float t)
            {
                float until = Time.unscaledTime + 40f;
                while (!(phase == Phase.Playing && playT >= t) && Time.unscaledTime < until) yield return null;
            }
            Transform Me() => views != null && views.Length > 0 && views[0] != null ? views[0].transform : player.transform;
            // close on the cockpit from the right, a little ahead, looking at the pilot's head
            void Pilot()
            {
                var me = Me();
                var eye = FindDeep(me, "CockpitCamera");
                Vector3 head = eye != null ? eye.position : me.position + me.up * 1f;
                Look(head + me.right * 2.6f + me.forward * 0.9f + me.up * 0.45f, head - me.up * 0.25f);
            }
            void Side(float d)
            {
                var me = Me();
                Vector3 mid = me.position - me.forward * (0.9f * d * 0.5f);
                Look(mid + me.right * d + me.up * (0.25f * d), mid);
            }
            void Fly()
            {
                battle.planes[0].rocketCoolSec = 0; battle.planes[0].rocketBurnSec = 0;
                plan.aim = battle.planes[0].Forward; plan.marks = null; plan.hold = false; plan.overpull = 0;
                plan.burstStart = plan.burstEnd = 0; plan.throttle = 1; plan.brake = false;
                plan.rocket = true;
                dirty = true;
            }

            yield return Planning();
            if (battle.Count < 2) { Debug.Log("OODA: rocket shots need a dogfight"); Application.Quit(); yield break; }
            if (battle.planes[0].numbers.rocketG <= 0)
            {
                // (a loadout without the rocket: hand it one, on a copy of the type's numbers)
                var n = (Ooda.Flight.Core.AircraftNumbers)typeof(Ooda.Flight.Core.AircraftNumbers).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(battle.planes[0].numbers, null);
                n.rocketG = 1.3; n.rocketSec = 2.5; n.rocketCooldownSec = 30;
                battle.planes[0].numbers = n;
            }
            float speed = playbackSpeed;
            PlaceFoe(1, 9000, 6000, 150, 250);
            Fly();
            Pilot();
            yield return Shot("0_pilot_before", 2f);

            // the first burn, slowed right down: the pilot as it lights, then the light from the side
            playbackSpeed = 0.3f;
            Commit();
            yield return PlayTo(0.02f);
            playbackSpeed = 0f;
            Pilot();
            yield return Shot("1_pilot_kick", 0.12f);
            Side(55f);
            yield return Shot("2_lighting_side", 0.05f);
            playbackSpeed = 0.3f;
            yield return PlayTo(1.0f);
            playbackSpeed = 0f;
            view.enabled = true;
            yield return Shot("3_chase", 0.4f);
            Debug.Log($"OODA: rocket shots at 1.0 s: rocket age {(views[0] != null ? views[0].RocketAge : -9f):F2}, plume {(views[0] != null && views[0].GetComponent<RocketPlume>() != null)}, pilots shoved {(views[0] != null ? views[0].GetComponentsInChildren<Ooda.Pilot.PilotThrust>(true).Length : 0)}");
            Side(70f);
            yield return Shot("4_side", 0.1f);
            Pilot();
            yield return Shot("5_pilot_pinned", 0.8f);
            view.enabled = true;
            playbackSpeed = 0.3f;
            yield return PlayTo(2.62f);
            playbackSpeed = 0f;
            yield return Shot("6_burnout", 0.35f);
            playbackSpeed = speed;
            Debug.Log($"OODA: rocket shots: burn 1 played, triangles in the effects {Fx.ToonFx.Get().Triangles}");
            yield return Planning();

            // a second burn from the cockpit
            PlaceFoe(1, 9000, -6000, 150, 250);
            Fly();
            if (view.cockpitEye != null) view.focus = TurnCamera.Focus.Cockpit;
            Commit();
            playbackSpeed = 0.5f;
            yield return PlayTo(0.6f);
            playbackSpeed = 0f;
            yield return Shot("7_cockpit", 0.3f);
            playbackSpeed = speed;
            view.focus = TurnCamera.Focus.Player;
            Debug.Log("OODA: rocket shots done");
            Application.Quit();
        }
    }
}
