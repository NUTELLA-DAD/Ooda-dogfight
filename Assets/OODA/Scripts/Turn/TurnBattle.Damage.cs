using System;
using System.Collections;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The systems damage self-test (-damageshots, from the title straight into the one on one): pictures of the systems
    /// lights with your engine and tanks hit and the enemy's wing damaged, the G-LOC warning while planning, the screen
    /// blacked out in playback, and the enemy's pilot under his parachute once its engine is shot out. Saved as
    /// damage_*.bmp beside the game's data folder. Then it quits.
    /// </summary>
    public partial class TurnBattle
    {
        bool damageShotsStarted;

        void DamageShotsCheck()
        {
            if (damageShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-damageshots") < 0) return;
            damageShotsStarted = true;
            StartCoroutine(DamageShots());
        }

        IEnumerator DamageShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name, float wait = 1.2f) { yield return new WaitForSeconds(wait); yield return Grab(System.IO.Path.Combine(dir, $"damage_{name}.bmp")); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            yield return Planning();

            // 1: your engine and tanks hit (amber, the fuel counting down), its wing hit
            battle.planes[0].systems.Wear(SystemId.Engine, 0.55);
            battle.planes[0].systems.Wear(SystemId.Fuel, 0.55);
            battle.planes[0].systems.Wear(SystemId.Controls, 0.2);
            if (battle.Count > 1) battle.planes[1].systems.Wear(SystemId.Wings, 0.6);
            UpdateSmoke(battle.AllSystems(), battle.down);
            dirty = true;
            yield return Shot("1_lights");

            // 2: nearly greyed out already, a full pull: the planning warning
            // (fast enough to pull the G whatever the saved setup flies: 380 KIAS)
            var m = battle.planes[0];
            m.state.vel = m.state.vel.Normalized * (Atmosphere.IasToTasKt(380, m.AltFt) * Atmosphere.KtToFtps);
            m.gStress = FlightModel.GlocLimit * 0.9;
            env = Envelope.Compute(m, turnSec);
            plan.aim = env.Clamp((m.Forward * 0.2 + m.Up).Normalized);
            plan.overpull = 0;
            plan.burstStart = plan.burstEnd = 0;
            dirty = true;
            yield return Shot("2_gloc_warning");

            // 3: fly it: the screen goes dark
            float speed = playbackSpeed;
            playbackSpeed = 0.5f;
            Commit();
            float until = Time.unscaledTime + 30f;
            while (!(phase == Phase.Playing && player != null && player.GreyOut >= 1f) && Time.unscaledTime < until) yield return null;
            yield return Shot("3_gloc", 0.3f);
            playbackSpeed = speed;
            yield return Planning();

            // 4: the enemy's engine shot out: its pilot bails out, the parachute opening
            battle.planes[0].glocLeftSec = 0; battle.planes[0].gStress = 0;   // awake again for the parachute
            if (battle.Count > 1 && !battle.Out(1))
            {
                battle.planes[1].systems.Wear(SystemId.Engine, 1);
                plan.aim = battle.planes[0].Forward;
                dirty = true;
                playbackSpeed = 0.5f;
                Commit();
                until = Time.unscaledTime + 30f;
                while (!(phase == Phase.Playing && playT > 2.4f) && Time.unscaledTime < until) yield return null;
                playbackSpeed = 0f;   // hold the moment
                var chute = GameObject.Find("Parachute");
                var cam = Camera.main;
                if (chute != null && cam != null)
                {
                    if (view != null) view.enabled = false;
                    var at = chute.transform.position + Vector3.up * 5f;
                    cam.transform.position = at + new Vector3(18f, 4f, -24f);
                    cam.transform.LookAt(at);
                    yield return Shot("4_parachute", 0.5f);
                    // and from farther back, with the empty plane falling away
                    cam.transform.position = at + new Vector3(60f, 25f, -90f);
                    cam.transform.LookAt(at + Vector3.down * 20f);
                    yield return Shot("5_parachute_far", 0.3f);
                    if (view != null) view.enabled = true;
                }
                else Debug.LogWarning("OODA damage shots: no parachute found");
                playbackSpeed = speed;
            }
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
