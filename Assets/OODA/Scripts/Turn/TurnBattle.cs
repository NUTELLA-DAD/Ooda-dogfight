using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Pilot;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.Turn
{
    /// <summary>
    /// The turn-based dogfight. Each turn both pilots plan at once: the player drags the arrow tip (where the
    /// nose should point when the turn ends) inside the reach shell, sets the throttle and a gun burst on the
    /// 3 s timeline, and sees the ghost path, the tracers and the enemy's "keeps doing what it's doing"
    /// projection. Fly turn: the AI plans without seeing the player's plan, the turn engine flies both,
    /// and the turn plays back. A kill wins; being shot down or hitting the ground loses.
    /// Missions (M cycles): Squadron, 8 P-51s (you and 7 AI wingmen) escorting 3 B-17s up the valley against
    /// 8 Bf 109s, with flak guns on the valley floor; Escort, one B-17 and one Bf 109; Dogfight, one on one.
    /// You lose if you go down or every bomber does; you win when the bombers get through or the enemy is gone.
    /// OODA (O) switches to short 1 s turns, to line up a shot one small step at a time.
    ///
    /// Mouse or touch: drag the yellow tip; drag anywhere else (or right-drag) to look around; wheel zooms;
    /// drag on the timeline to set the burst. Keyboard: WASD or arrows move the arrow, Shift/Ctrl throttle,
    /// Space WEP, F fire at predicted hits, B burst presets, Enter fly, R replay, P projection, V camera,
    /// X straight ahead, Backspace restart, H help. Gamepad: left stick arrow, triggers throttle, A fly,
    /// X burst presets, right bumper fire at predicted hits, Y projection, B replay, right stick look,
    /// left bumper straight ahead, right stick press camera, Start restart.
    /// </summary>
    public class TurnBattle : MonoBehaviour
    {
        public TurnAircraftView player, enemy;
        public TurnCamera view;
        public Terrain terrain;
        public PilotHeadLook playerPilot, enemyPilot;
        [Tooltip("The bomber for the escort mission (hidden in the dogfight).")]
        public TurnAircraftView bomber;

        public enum Mission { Squadron, Escort, Dogfight, ValleyRun }
        [Header("Mission")]
        public Mission mission = Mission.Squadron;
        [Tooltip("The bomber's route, metres (Unity x east, y height, z north); the first point is where it starts.")]
        public Vector3[] escortRoute =
        {
            new Vector3(0, 1250, -6500), new Vector3(0, 1300, 0), new Vector3(0, 1350, 5000), new Vector3(2500, 1450, 8000),
        };
        public float bomberKias = 160;
        [Tooltip("How far ahead along the route the Spitfire starts, metres.")]
        public float escortGapM = 5000;
        [Tooltip("Squadron: how far up the valley the Bf 109s start, m (they meet the formation about turn 6-7 from 8 km)")]
        public float squadronGapM = 8000;
        public Color bomberColor = new Color(0.4f, 1f, 0.45f, 0.7f);

        [Header("Squadron")]
        [Tooltip("Turn-view prefabs (each with a TurnAircraftView) for the extra aircraft: P-51 wingmen, Bf 109s and B-17s.")]
        public GameObject wingmanPrefab, enemyPrefab, bomberPrefab;
        [Tooltip("Where the flak guns stand; their positions are read at the start.")]
        public Transform[] flakSites;
        public int squadronFighters = 8, squadronBombers = 3;
        public Color wingmanColor = new Color(0.3f, 0.7f, 1f, 0.35f);
        public Color wingTracer = new Color(1f, 0.85f, 0.5f, 0.5f);
        public Color flakTracer = new Color(1f, 0.55f, 0.15f, 0.8f);

        [Header("OODA")]
        [Tooltip("The short turn the OODA button switches to, seconds.")]
        public float oodaTurnSec = 1f;
        bool ooda;

        [Header("Start (head-on)")]
        [Tooltip("Middle of the fight, metres (Unity x east, z north; height is ignored).")]
        public Vector3 startCentre = new Vector3(0, 0, -6000);
        public float startAltFt = 5000, startKias = 300, startGapM = 3000;

        [Header("Turns")]
        public float turnSec = (float)Scenarios.TurnSec;
        public float playbackSpeed = 1f;
        public bool showProjection = true;
        public bool showHelp = true;
        [Tooltip("S, the down arrow or the stick pulled back moves the arrow up, as the stick pulls the nose up.")]
        public bool stickPitch = true;

        [Header("Drawing")]
        public float arrowLength = 150f;   // metres
        public Color shellColor = new Color(1f, 1f, 1f, 0.45f);
        [Tooltip("The shaded overpull band: its colour at the white ring (the hardest clean turn) and at the overpull edge.")]
        public Color reachRing = new Color(1f, 1f, 1f, 0.07f);
        public Color reachEdge = new Color(1f, 0.15f, 0.1f, 0.2f);
        public Color arrowColor = new Color(1f, 0.85f, 0.1f, 1f);
        public Color ghostColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        public Color projectionColor = new Color(1f, 0.3f, 0.25f, 0.8f);
        public Color myTracer = new Color(1f, 0.75f, 0.2f, 0.9f);
        public Color theirTracer = new Color(1f, 0.35f, 0.5f, 0.9f);
        public Color overpullColor = new Color(1f, 0.15f, 0.1f, 1f);
        public Color holoColor = new Color(0.25f, 0.85f, 1f, 0.35f);
        public Color holoEnemyColor = new Color(1f, 0.3f, 0.25f, 0.3f);
        public Color holoFriendColor = new Color(0.35f, 1f, 0.55f, 0.22f);
        [Tooltip("The enemy ghost when your planned burst hits it, if it holds.")]
        public Color holoTargetHitColor = new Color(1f, 0.12f, 0.08f, 0.6f);
        [Tooltip("Preview tracers that hit.")]
        public Color hitTracer = new Color(1f, 0.12f, 0.1f, 1f);
        [Tooltip("Preview tracers that pass within twice the hit distance: close, but a miss.")]
        public Color nearTracer = new Color(1f, 0.95f, 0.2f, 0.95f);
        [Tooltip("How strong the always-on gun line (where a burst would go) is drawn, when no burst is set.")]
        [Range(0, 1)] public float gunLineAlpha = 0.25f;
        [Tooltip("Draw the gun line when no burst is set. Off: only a burst you set shows its rounds (the hit band on the bar still shows when to fire).")]
        public bool showGunLine = false;

        enum Phase { Planning, Thinking, Playing, Over }

        Phase phase, phaseAfterPlayback;
        Battle battle;
        TurnAircraftView[] views = new TurnAircraftView[0];
        EnemyAi[] ais = new EnemyAi[0];
        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<Material> spawnedHoloMats = new List<Material>();
        HeightGrid grid;
        Task<TurnPlan?[]> thinking;
        Battle thinkingFor;
        Envelope env;
        TurnPlan plan;
        bool dirty;
        TurnResult preview, last;
        double[] healthBefore = new double[0];
        float playT, prevPlayT;
        string outcome, notice;
        float noticeUntil;

        LinePool lines;
        Transform tip;
        readonly List<(Transform t, float born)> flashes = new List<(Transform, float)>();
        Material flashMat, tipMat;

        bool dragArrow, dragLook, dragBurst, dragMoves;
        Rect movesRect;
        bool showMoves;            // the Advanced timeline panel is open (T)
        float markT;               // the moment on the moves slider being edited, s into the turn
        Vector2 lastPointer;
        float guiScale = 1;
        Rect burstRect;
        readonly List<Rect> guiRects = new List<Rect>();

        static readonly (float a, float b)[] BurstPresets = { (0, 0), (0, 1), (1, 2), (2, 3), (0, 3) };
        int burstPreset;
        string warning;            // what the preview says the plan does to the wing
        bool previewDeparts;
        bool previewCrashes;       // the preview flies the player into the ground or an obstacle
        double lastOverpull;
        TurnResult gunPreview;     // the same turn with the guns firing all the way through and the enemy unbreakable: where the rounds would connect
        readonly List<(double a, double b)> hitWindows = new List<(double, double)>();   // s into the turn when a round fired connects, from gunPreview
        bool[] burstHits = new bool[0];   // by aircraft: the player's planned burst hits it in the preview
        int confidenceTarget = -1;        // the enemy the planned burst hits most, for the what-if-it-breaks check
        int confidenceHits = -1, confidenceOf;   // hits in confidenceHits of confidenceOf tries; -1 not known (yet)
        bool confidencePending;
        float confidenceAt;
        GameObject holoMine, holoTheirs;
        GameObject[] holos = new GameObject[0];   // a ghost for each aircraft: where the preview puts it at the end of the turn
        readonly List<Material> holoMats = new List<Material>();

        // ---------- set up ----------

        void Start()
        {
            if (Ooda.UI.TitleScreen.ChosenMission is int chosen) mission = (Mission)chosen;   // picked on the title screen
            lines = new LinePool(transform, "Lines") { sizeAt = W };
            tipMat = new Material(Shader.Find("Sprites/Default")) { color = arrowColor };
            flashMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.9f, 0.4f, 1f) };
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(t.GetComponent<Collider>());
            t.name = "ArrowTip";
            t.GetComponent<Renderer>().sharedMaterial = tipMat;
            t.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tip = t.transform;
            tip.SetParent(transform, false);
            if (terrain == null) terrain = Terrain.activeTerrain;
            grid = MakeGrid(terrain);
            if (view != null) { view.player = player.transform; view.enemy = enemy.transform; }
            if (playerPilot != null) playerPilot.target = enemy.transform;
            if (enemyPilot != null) enemyPilot.target = player.transform;
            holoMine = MakeHolo(player.gameObject, holoColor, "GhostP51");
            holoTheirs = MakeHolo(enemy.gameObject, holoEnemyColor, "GhostEnemy");
            Restart();
        }

        double GroundFt(double xFt, double yFt)
        {
            if (terrain == null) return 0;
            var w = new Vector3((float)(xFt * Atmosphere.MPerFt), 0, (float)(yFt * Atmosphere.MPerFt));
            float h = terrain.SampleHeight(w) + terrain.transform.position.y;
            return Math.Max(0, h) * Atmosphere.FtPerM;
        }

        /// <summary>A copy of the terrain's heights the turn engine and the AI can read from any thread.</summary>
        static HeightGrid MakeGrid(Terrain t)
        {
            if (t == null) return null;
            var d = t.terrainData;
            int res = d.heightmapResolution;
            float[,] h = d.GetHeights(0, 0, res, res);   // [row = z, column = x], 0..1 of size.y
            var ft = new float[res * res];
            float y0 = t.transform.position.y;
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                    ft[j * res + i] = (float)((y0 + h[j, i] * d.size.y) * Atmosphere.FtPerM);
            Vector3 o = t.transform.position;
            return new HeightGrid(ft, res, res, o.x * Atmosphere.FtPerM, o.z * Atmosphere.FtPerM,
                                  d.size.x / (res - 1) * Atmosphere.FtPerM, d.size.z / (res - 1) * Atmosphere.FtPerM);
        }

        /// <summary>The air hazards as plain shapes in feet, for the AI pilots, who plan off the main thread and can't use the colliders.
        /// A wide margin round each one, so they keep well clear.</summary>
        WireSet MakeWires()
        {
            var hm = FindFirstObjectByType<HazardMarkers>();
            if (hm == null) return null;
            var w = new WireSet();
            const double wireFt = 30;   // about 9 m: the 4 m collider and room to spare
            for (int k = 0; k < hm.WireCount; k++) w.Add(UnityFrame.Pos(hm.wireStarts[k]), UnityFrame.Pos(hm.wireEnds[k]), wireFt);
            if (hm.balloons != null)
                foreach (var c in hm.balloons) { var p = UnityFrame.Pos(c); w.Add(p, p, (hm.balloonRadius + 8f) * Atmosphere.FtPerM); }
            return w.Count > 0 ? w : null;
        }

        int obstacleMask = -2;

        /// <summary>A straight line between two points (feet) through anything on the Obstacles layer: bridges, tunnel mouths, buildings.</summary>
        bool ObstacleHit(Vec3d aFt, Vec3d bFt)
        {
            if (obstacleMask == -2) obstacleMask = LayerMask.GetMask("Obstacles");
            if (obstacleMask == 0) return false;
            return Physics.Linecast(UnityFrame.Pos(aFt), UnityFrame.Pos(bFt), obstacleMask, QueryTriggerInteraction.Ignore);
        }

        bool HasRoute => escortRoute != null && escortRoute.Length >= 2;
        bool CanEscort => bomber != null && HasRoute;
        bool CanSquadron => CanEscort && wingmanPrefab != null && enemyPrefab != null && bomberPrefab != null;
        bool CanValleyRun => HasRoute && wingmanPrefab != null && enemyPrefab != null;
        Mission Playing => mission == Mission.ValleyRun ? (CanValleyRun ? Mission.ValleyRun : Mission.Dogfight)
                         : mission == Mission.Squadron && CanSquadron ? Mission.Squadron : mission != Mission.Dogfight && CanEscort ? Mission.Escort : Mission.Dogfight;

        static Vec3d Ft(Vector3 unityM) => new Vec3d(unityM.x * Atmosphere.FtPerM, unityM.z * Atmosphere.FtPerM, unityM.y * Atmosphere.FtPerM);

        public void Restart()
        {
            thinking = null; thinkingFor = null;
            foreach (var g in spawned) if (g != null) Destroy(g);
            spawned.Clear();
            var route = HasRoute ? escortRoute.Select(Ft).ToArray() : null;
            switch (Playing)
            {
                case Mission.Squadron:
                    var flak = flakSites == null ? new Vec3d[0] : flakSites.Where(f => f != null).Select(f => Ft(f.position)).ToArray();
                    battle = Scenarios.Squadron(route, flak, squadronFighters, squadronBombers, bomberKias, squadronGapM);
                    break;
                case Mission.Escort: battle = Scenarios.Escort(route, bomberKias, escortGapM); break;
                case Mission.ValleyRun:
                {
                    var guns = flakSites == null ? new Vec3d[0] : flakSites.Where(f => f != null).Select(f => Ft(f.position)).ToArray();
                    battle = Scenarios.ValleyRun(route, guns);
                    break;
                }
                default: battle = Scenarios.HeadOn(UnityFrame.Pos(startCentre), startAltFt, startKias, startGapM); break;
            }
            if (grid != null) battle.groundFt = grid.At; else battle.groundFt = GroundFt;
            battle.obstacleHit = ObstacleHit;
            battle.wires = MakeWires();

            // a view for each aircraft: the scene's player, enemy and bomber first, prefabs for the rest
            int n = battle.Count;
            views = new TurnAircraftView[n];
            views[0] = player; views[1] = enemy;
            bool sceneBomberUsed = false;
            for (int i = 2; i < n; i++)
            {
                if (battle.routes[i] != null && !sceneBomberUsed && bomber != null) { views[i] = bomber; sceneBomberUsed = true; continue; }
                var prefab = battle.routes[i] != null ? bomberPrefab : battle.side[i] == 0 ? wingmanPrefab : enemyPrefab;
                if (prefab == null) continue;
                var g = Instantiate(prefab);
                g.name = $"{prefab.name} {i}";
                spawned.Add(g);
                views[i] = g.GetComponent<TurnAircraftView>();
            }
            if (bomber != null) bomber.gameObject.SetActive(sceneBomberUsed);
            for (int i = 0; i < n; i++) if (views[i] != null) views[i].Bind(battle.planes[i]);
            // the same holographic ghost as the player's for everyone: green ours, red theirs
            foreach (var m in spawnedHoloMats) Destroy(m);
            spawnedHoloMats.Clear();
            holos = new GameObject[n];
            holos[0] = holoMine;
            for (int i = 1; i < n; i++)
            {
                if (views[i] == null) continue;
                int before = holoMats.Count;
                holos[i] = MakeHolo(views[i].gameObject, battle.side[i] == 0 ? holoFriendColor : holoEnemyColor, $"Ghost {i}");
                if (holos[i] != null) spawned.Add(holos[i]);
                for (int k = before; k < holoMats.Count; k++) spawnedHoloMats.Add(holoMats[k]);
                holoMats.RemoveRange(before, holoMats.Count - before);
            }

            // an AI pilot for everyone but the player and the bombers
            ais = new EnemyAi[n];
            for (int i = 1; i < n; i++)
            {
                if (battle.routes[i] != null) continue;
                bool ours = battle.side[i] == 0;
                ais[i] = new EnemyAi
                {
                    me = i, rng = new Lcg((uint)(77 + 13 * i)), ignoreObstacles = true,
                    role = ours ? (i % 2 == 0 ? EnemyAi.Role.Escort : EnemyAi.Role.Fighter) : (i % 2 == 1 ? EnemyAi.Role.Hunter : EnemyAi.Role.Fighter),
                };
            }
            healthBefore = (double[])battle.health.Clone();
            shownHp = (double[])battle.health.Clone();
            plan = new TurnPlan { throttle = battle.planes[0].throttle };
            last = null;
            outcome = null;
            foreach (var f in flashes) Destroy(f.t.gameObject);
            flashes.Clear();
            BeginPlanning();
            if (view != null) view.BehindPlayer();
        }

        void BeginPlanning()
        {
            phase = Phase.Planning;
            var m = battle.planes[0];
            env = Envelope.Compute(m, turnSec);
            plan.aim = m.Forward;
            plan.overpull = 0;
            plan.burstStart = plan.burstEnd = 0;
            plan.wep = false;
            plan.marks = null;
            plan.missile = false;
            markT = 0;
            burstPreset = 0;
            dirty = true;
            ShowAllLive();
            UpdateSmoke(battle.health, battle.down);
            FocusNearest();
        }

        void ShowAllLive()
        {
            for (int i = 0; i < views.Length; i++) if (views[i] != null) views[i].ShowLive(battle.planes[i]);
        }

        bool Bombing => battle != null && battle.routes.Any(r => r != null);

        /// <summary>The enemy the HUD, the camera and the enemy ghost follow: the nearest fighter still flying.</summary>
        int focus = 1;
        void FocusNearest()
        {
            int best = -1; double bestR = double.PositiveInfinity;
            for (int i = 1; i < battle.Count; i++)
            {
                if (battle.side[i] == battle.side[0] || battle.Out(i)) continue;
                double r = (battle.planes[i].state.pos - battle.planes[0].state.pos).Length + (battle.routes[i] != null ? 1e6 : 0);
                if (r < bestR) { bestR = r; best = i; }
            }
            focus = best >= 0 ? best : 1;
            var t = views.Length > focus && views[focus] != null ? views[focus].transform : enemy.transform;
            if (view != null) view.enemy = t;
            if (playerPilot != null) playerPilot.target = t;
        }

        /// <summary>Fly turn: the AI pilots plan on other threads (the battle does not change meanwhile), then the turn is flown.</summary>
        void Commit()
        {
            if (phase != Phase.Planning) return;
            var b = battle; var pilots = ais; float ts = turnSec; int n = battle.Count;
            thinkingFor = b;
            b.CheckBreak();   // so the wingmen get their own plans the turn the formation breaks
            thinking = Task.Run(() =>
            {
                var plans = new TurnPlan?[n];
                Parallel.For(1, n, i => { if (pilots[i] != null && !b.Out(i) && !b.InFormation(i)) plans[i] = pilots[i].Plan(b, ts); });
                return plans;
            });
            phase = Phase.Thinking;
        }

        void FinishCommit()
        {
            var task = thinking; thinking = null;
            if (thinkingFor != battle) return;   // restarted meanwhile
            TurnPlan?[] plans;
            if (task.IsFaulted) { Debug.LogException(task.Exception); plans = new TurnPlan?[battle.Count]; }
            else plans = task.Result;
            plans[0] = plan;
            healthBefore = (double[])battle.health.Clone();
            lastOverpull = plan.overpull;
            last = battle.Resolve(plans, turnSec, true);
            phaseAfterPlayback = battle.Over ? Phase.Over : Phase.Planning;
            if (battle.Over) outcome = Outcome();
            newTurn = true;
            StartPlayback();
        }

        void SwitchOoda()
        {
            if (phase != Phase.Planning && phase != Phase.Over) return;
            ooda = !ooda;
            turnSec = ooda ? oodaTurnSec : (float)Scenarios.TurnSec;
            Notice(ooda ? $"OODA: {turnSec:0.#} s turns. Small steps to line up the shot." : "Normal 3 s turns.");
            if (phase == Phase.Planning) { double thr = plan.throttle; BeginPlanning(); plan.throttle = thr; }
        }

        void StartPlayback()
        {
            if (last == null) return;
            playT = prevPlayT = 0;
            phase = Phase.Playing;
        }

        void Replay()
        {
            if (last == null || phase == Phase.Playing || phase == Phase.Thinking) return;
            phaseAfterPlayback = phase;
            newTurn = false;
            StartPlayback();
        }

        string Outcome()
        {
            bool me = battle.down[0];
            int bombers = 0, through = 0, enemies = 0;
            for (int i = 0; i < battle.Count; i++)
            {
                if (battle.routes[i] != null) { bombers++; if (!battle.Out(i)) through++; }
                else if (battle.side[i] != 0 && !battle.Out(i)) enemies++;
            }
            if (Bombing)
            {
                string tally = bombers > 1 ? $" {through} of {bombers} bombers made it." : "";
                if (me) return (battle.crashed[0] ? "You flew into the ground." : "You were shot down.") + " Mission failed." + tally;
                if (through == 0) return (bombers > 1 ? "All the bombers went down." : "The B-17 went down.") + " Mission failed.";
                if (enemies == 0) return (bombers > 1 ? "The enemy squadron is gone." : "Bf 109 down.") + " The bombers are safe. You win!" + tally;
                return "The bombers made it through the valley. Mission complete!" + tally;
            }
            if (Playing == Mission.ValleyRun)
            {
                int foes = 0, crashedFoes = 0;
                for (int i = 0; i < battle.Count; i++)
                    if (battle.side[i] != 0) { foes++; if (battle.crashed[i]) crashedFoes++; }
                string downed = $" {foes - enemies} of {foes} Bf 109s down" + (crashedFoes > 0 ? $", {crashedFoes} of them into the valley." : ".");
                if (me) return (battle.crashed[0] ? "You crashed." : "You were shot down.") + " Mission failed." + downed;
                return "Both Bf 109s down. You win!" + (crashedFoes > 0 ? $" {crashedFoes} of them flew into the valley." : "");
            }
            bool them = battle.down[1];
            if (me && them) return "Both aircraft went down. A draw.";
            if (them) return battle.crashed[1] ? "The Bf 109 flew into the ground. You win!" : "Bf 109 shot down. You win!";
            return battle.crashed[0] ? "You flew into the ground." : "You were shot down.";
        }

        void Notice(string s) { notice = s; noticeUntil = Time.time + 3f; }

        void SwitchMission()
        {
            int missions = Enum.GetValues(typeof(Mission)).Length;
            for (int k = 0; k < missions; k++)
            {
                mission = (Mission)(((int)mission + 1) % missions);
                if (Playing == mission) break;   // skip what this scene can't play
            }
            Restart();
            Notice(Playing == Mission.Squadron ? "Squadron: 8 P-51s and 3 B-17s against 8 Bf 109s and the flak. Get the bombers through."
                 : Playing == Mission.Escort ? "Escort: keep the Bf 109 off the B-17."
                 : Playing == Mission.ValleyRun ? "Valley Run: you and your wingman low in the valley after two Bf 109s. Mind the wires and the balloons."
                 : "Dogfight: you and the Bf 109, head-on.");
        }

        // ---------- every frame ----------

        void Update()
        {
            if (thinking != null && thinking.IsCompleted) FinishCommit();
            HandleInput();
            lines.Begin();
            if (phase == Phase.Playing) Playback();
            else if (phase == Phase.Planning || phase == Phase.Thinking) DrawPlanning();
            else DrawLastPaths(last != null ? last.FrameCount - 1 : 0);
            lines.End();
            tip.gameObject.SetActive((phase == Phase.Planning || phase == Phase.Thinking) && !plan.HasMarks);
            if (phase != Phase.Planning && phase != Phase.Thinking)
            {
                foreach (var h in holos) if (h != null) h.SetActive(false);
                if (holoTheirs != null) holoTheirs.SetActive(false);
            }
            FadeFlashes();
            if (playerPilot != null && view != null) playerPilot.hideHead = view.focus == TurnCamera.Focus.Cockpit;
        }

        float W(Vector3 at) => view == null ? 0.5f : Mathf.Max(0.05f, Vector3.Distance(view.transform.position, at) * 0.0025f);

        Vector3 PlayerPos => player.transform.position;

        void HandleInput()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;
            var pointer = Pointer.current;
            float dt = Time.unscaledDeltaTime;

            bool Pressed(Key k) => kb != null && kb[k].wasPressedThisFrame;
            if (Pressed(Key.H)) showHelp = !showHelp;
            if (Pressed(Key.M)) SwitchMission();
            if (Pressed(Key.O)) SwitchOoda();
            if (Pressed(Key.Backspace) || (pad != null && pad.startButton.wasPressedThisFrame)) Restart();
            if (Pressed(Key.R) || (pad != null && pad.buttonEast.wasPressedThisFrame)) Replay();
            if (Pressed(Key.P) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) showProjection = !showProjection;
            if ((Pressed(Key.V) || (pad != null && pad.rightStickButton.wasPressedThisFrame)) && view != null) view.NextFocus();

            // Looking around: right-drag, a drag off the arrow, the right stick; the wheel zooms
            if (view != null)
            {
                if (mouse != null)
                {
                    if (mouse.rightButton.isPressed) view.Orbit(mouse.delta.ReadValue() * 0.25f);
                    float sc = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(sc) > 0.01f) view.Zoom(Mathf.Sign(sc));
                }
                if (pad != null)
                {
                    view.Orbit(pad.rightStick.ReadValue() * new Vector2(120f, -90f) * dt);
                    if (pad.dpad.up.isPressed) view.Zoom(4f * dt);
                    if (pad.dpad.down.isPressed) view.Zoom(-4f * dt);
                }
                if (kb != null)
                {
                    if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) view.Zoom(4f * dt);
                    if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) view.Zoom(-4f * dt);
                }
            }

            // Pointer (mouse or touch): the arrow tip, the burst timeline, or looking around
            if (pointer != null)
            {
                Vector2 pos = pointer.position.ReadValue();
                Vector2 gui = new Vector2(pos.x, Screen.height - pos.y) / guiScale;
                if (pointer.press.wasPressedThisFrame)
                {
                    if (phase == Phase.Planning && burstRect.Contains(gui)) { dragBurst = true; SetBurstFromBar(gui.x, true); }
                    else if (phase == Phase.Planning && movesRect.Contains(gui)) { dragMoves = true; SetMarkTime(gui.x); }
                    else if (guiRects.Any(r => r.Contains(gui))) { }
                    else if (phase == Phase.Planning && !plan.HasMarks && NearTip(pos)) dragArrow = true;
                    else dragLook = true;
                }
                else if (pointer.press.isPressed)
                {
                    if (dragArrow) DragArrow(pos);
                    else if (dragBurst) SetBurstFromBar(gui.x, false);
                    else if (dragMoves) SetMarkTime(gui.x);
                    else if (dragLook && view != null && (mouse == null || !mouse.rightButton.isPressed)) view.Orbit((pos - lastPointer) * 0.25f);
                }
                if (pointer.press.wasReleasedThisFrame) { dragArrow = dragLook = dragMoves = false; if (dragBurst) { dragBurst = false; TidyBurst(); } }
                lastPointer = pos;
            }

            if (phase != Phase.Planning) return;

            if (Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) { Commit(); return; }

            // The arrow by keys or stick: nudged in the start frame (up = pull, right = right wing)
            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
            }
            if (pad != null) move += pad.leftStick.ReadValue();
            if (stickPitch) move.y = -move.y;   // back on the stick (S, down) brings the nose, and the arrow, up
            // The timeline: [ and ] (or the d-pad) move the slider; Q/E bank and Z/C rudder at the slider's moment.
            // Once there are points, W/S pull, Shift/Ctrl throttle and Space WEP also set them at the slider's moment.
            if (Pressed(Key.T)) showMoves = !showMoves;
            float bank = 0;
            if (showMoves)
            {
                float scrub = 0;
                if (kb != null) { if (kb.leftBracketKey.isPressed) scrub -= 1; if (kb.rightBracketKey.isPressed) scrub += 1; }
                if (pad != null) { if (pad.dpad.left.isPressed) scrub -= 1; if (pad.dpad.right.isPressed) scrub += 1; }
                if (scrub != 0) markT = Mathf.Clamp(markT + scrub * dt * 1.5f, 0, turnSec);
                if (Pressed(Key.Comma)) JumpPoint(-1);
                if (Pressed(Key.Period)) JumpPoint(1);
                if (kb != null) { if (kb.qKey.isPressed) bank -= 1; if (kb.eKey.isPressed) bank += 1; }
                if (pad != null && pad.leftStickButton.isPressed) bank += pad.leftStick.ReadValue().x;   // stick click + sideways banks
                if (bank != 0) NudgeBank(bank * 90f * dt);
                float rud = 0;
                if (kb != null) { if (kb.zKey.isPressed) rud -= 1; if (kb.cKey.isPressed) rud += 1; }
                if (rud != 0) NudgeRudder(rud * 1.5f * dt);
            }
            if (plan.HasMarks)
            {
                if (Mathf.Abs(move.x) > 0.1f && bank == 0) NudgeBank(move.x * 90f * dt);
                if (Mathf.Abs(move.y) > 0.1f) NudgeG(move.y * 3f * dt);
                move = Vector2.zero;
            }
            if (move.sqrMagnitude > 0.01f)
            {
                double rate = 45 * Math.PI / 180 * dt;
                SetAim(plan.aim + env.up * (move.y * rate) + env.right * (move.x * rate));
            }
            if (Pressed(Key.X) || (pad != null && pad.leftShoulder.wasPressedThisFrame)) { if (plan.HasMarks) ClearMarks(); else SetAim(env.fwd); }

            // Throttle and WEP
            float thr = 0;
            if (kb != null) { if (kb.leftShiftKey.isPressed) thr += 1; if (kb.leftCtrlKey.isPressed) thr -= 1; }
            if (pad != null) thr += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            if (Mathf.Abs(thr) > 0.05f)
            {
                if (plan.HasMarks) { var st = plan.StateAt(markT); double v = Math.Max(0, Math.Min(1, st.throttle + thr * 0.6 * dt)); EditPoint(k => { k.throttle = v; return k; }); }
                else { plan.throttle = Math.Max(0, Math.Min(1, plan.throttle + thr * 0.6 * dt)); dirty = true; }
            }
            if (Pressed(Key.Space))
            {
                if (plan.HasMarks) { bool v = !plan.StateAt(markT).wep; EditPoint(k => { k.wep = v; return k; }); }
                else { plan.wep = !plan.wep; dirty = true; }
            }

            // Guns
            if (Pressed(Key.B) || (pad != null && pad.buttonWest.wasPressedThisFrame))
            {
                burstPreset = (burstPreset + 1) % BurstPresets.Length;
                plan.burstStart = BurstPresets[burstPreset].a;
                plan.burstEnd = BurstPresets[burstPreset].b;
                dirty = true;
            }
            if (Pressed(Key.F) || (pad != null && pad.rightShoulder.wasPressedThisFrame)) FireAtPredicted();
            if (Pressed(Key.G)) ArmMissile(!plan.missile);

            if (dirty) RunPreview();
            // the what-if-it-breaks check, once the plan has settled (not while dragging or every frame)
            if (confidencePending && !dirty && !dragArrow && !dragBurst && Time.unscaledTime >= confidenceAt) RunConfidence();
        }

        // ---------- planning ----------

        Vector3 TipPos => PlayerPos + UnityFrame.Dir(plan.aim) * arrowLength;

        bool NearTip(Vector2 screen)
        {
            if (view == null) return false;
            Vector3 s = view.Cam.WorldToScreenPoint(TipPos);
            float reach = Touchscreen.current != null ? 80f : 45f;
            return s.z > 0 && Vector2.Distance(screen, s) < reach * Mathf.Max(1, guiScale);
        }

        void DragArrow(Vector2 screen)
        {
            Ray r = view.Cam.ScreenPointToRay(screen);
            Vector3 c = PlayerPos;
            Vector3 oc = r.origin - c;
            float b = Vector3.Dot(oc, r.direction), q = oc.sqrMagnitude - arrowLength * arrowLength;
            float disc = b * b - q;
            Vector3 point;
            if (disc >= 0)
            {
                float s = Mathf.Sqrt(disc);
                Vector3 p1 = r.GetPoint(-b - s), p2 = r.GetPoint(-b + s);
                point = (p1 - TipPos).sqrMagnitude < (p2 - TipPos).sqrMagnitude ? p1 : p2;
            }
            else point = r.GetPoint(-b);
            Vector3 dir = (point - c).normalized;
            SetAim(UnityFrame.Dir(dir));
        }

        /// <summary>The arrow may go past the ring into the overpull band: past the stall line, toward a spin.</summary>
        void SetAim(Vec3d d)
        {
            plan.aim = env.Clamp(d, true);
            plan.overpull = env.Overpull(plan.aim);
            dirty = true;
        }

        // ---------- the timeline: points that set bank, G, throttle, WEP or rudder from their moment on ----------

        void SetMarkTime(float guiX)
        {
            float t = Mathf.Clamp01((guiX - movesRect.x) / movesRect.width) * turnSec;
            // snap onto a point when close, so a tap on a tick picks that point
            if (plan.HasMarks)
                foreach (var k in plan.marks)
                    if (Math.Abs(k.time - t) < turnSec * 0.03) { markT = (float)k.time; return; }
            markT = Mathf.Round(t / 0.1f) * 0.1f;
        }

        int PointAt(Mark[] list, double t) => list == null ? -1 : Array.FindIndex(list, k => Math.Abs(k.time - t) < 0.05);

        /// <summary>Change the point at the slider's moment (made there if there is none). A point left setting nothing goes.</summary>
        void EditPoint(Func<Mark, Mark> change)
        {
            var list = plan.HasMarks ? plan.marks.ToList() : new List<Mark>();
            int at = list.FindIndex(k => Math.Abs(k.time - markT) < 0.05);
            if (at < 0) { list.Add(new Mark(Math.Round(markT * 10) / 10)); at = list.Count - 1; }
            var k1 = change(list[at]);
            if (k1.Empty) list.RemoveAt(at); else list[at] = k1;
            list.Sort((a, b) => a.time.CompareTo(b.time));
            plan.marks = list.Count > 0 ? list.ToArray() : null;
            plan.overpull = 0;
            dirty = true;
        }

        /// <summary>The bank in force at the slider's moment: the timeline's, or where the ghost's wings are then.</summary>
        double BankAtSlider()
        {
            var st = plan.StateAt(markT);
            if (st.bankDeg != null) return st.bankDeg.Value;
            if (preview != null)
            {
                int fi = Mathf.Clamp(Mathf.RoundToInt(markT / (float)preview.dt), 0, preview.FrameCount - 1);
                var fr = preview.frames[0][fi];
                return AutoPilot.BankDeg(fr.vel.Normalized, fr.up);
            }
            return AutoPilot.BankDeg(battle.planes[0]);
        }

        static double Wrap180(double d) { while (d > 180) d -= 360; while (d < -180) d += 360; return d; }

        void SetBank(double deg) { deg = Wrap180(Math.Round(deg)); EditPoint(k => { k.bankDeg = deg; return k; }); }
        void NudgeBank(double d) => SetBank(BankAtSlider() + d);
        void SetG(double g)
        {
            var n = battle.planes[0].numbers;
            g = Math.Max(n.minG, Math.Min(n.maxG + 2, Math.Round(g * 10) / 10));
            EditPoint(k => { k.g = g; return k; });
        }
        void NudgeG(double d) => SetG(plan.StateAt(markT).g + d);
        void SetRudder(double r) { r = Math.Max(-1, Math.Min(1, Math.Round(r * 20) / 20)); EditPoint(k => { k.rudder = r; return k; }); }
        void NudgeRudder(double d) => SetRudder(plan.StateAt(markT).rudder + d);

        /// <summary>Move the slider to the previous or next point.</summary>
        void JumpPoint(int dir)
        {
            if (!plan.HasMarks) return;
            var times = plan.Sorted().Select(k => (float)k.time).ToList();
            float? to = dir < 0 ? times.Where(t => t < markT - 0.01f).Select(t => (float?)t).LastOrDefault()
                                : times.Where(t => t > markT + 0.01f).Select(t => (float?)t).FirstOrDefault();
            if (to != null) markT = to.Value;
        }

        void ClearMarks() { plan.marks = null; dirty = true; }

        void DeleteMarkAtSlider()
        {
            if (!plan.HasMarks) return;
            var list = plan.marks.Where(k => Math.Abs(k.time - markT) >= 0.05).ToList();
            plan.marks = list.Count > 0 ? list.ToArray() : null;
            dirty = true;
        }

        /// <summary>Arm (or safe) this turn's missile: it leaves the first moment the enemy in focus, or failing that any, is locked.</summary>
        void ArmMissile(bool on)
        {
            if (on && battle.missiles[0] <= 0) on = false;
            plan.missile = on;
            plan.missileTarget = focus;
            dirty = true;
        }

        void SetBurstFromBar(float guiX, bool start)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            t = Mathf.Round(t / 0.05f) * 0.05f;
            if (start) { plan.burstStart = t; plan.burstEnd = t; }
            else plan.burstEnd = t;
            dirty = true;
        }

        void TidyBurst()
        {
            double a = Math.Min(plan.burstStart, plan.burstEnd), b = Math.Max(plan.burstStart, plan.burstEnd);
            if (b - a < 0.1) { a = 0; b = 0; }    // a tap clears the burst
            plan.burstStart = a; plan.burstEnd = b;
            dirty = true;
        }

        void FireAtPredicted()
        {
            if (preview == null) return;
            if (dirty || gunPreview == null) RunPreview();
            var times = gunPreview.hits.Where(h => h.shooter == 0).Select(h => h.fireTime - gunPreview.startTime).ToList();
            if (times.Count == 0) { Notice("No hits predicted on this path if the enemy holds its turn."); plan.burstStart = plan.burstEnd = 0; }
            else { plan.burstStart = Math.Max(0, times.Min() - 0.15); plan.burstEnd = Math.Min(turnSec, times.Max() + 0.15); }
            dirty = true;
            RunPreview();
        }

        void RunPreview()
        {
            var sim = battle.Clone();
            sim.dispersion = false;
            preview = sim.Resolve(new TurnPlan?[] { plan }, turnSec, true);
            dirty = false;
            previewDeparts = preview.frames[0].Any(f => f.departed);
            bool stalls = preview.frames[0].Any(f => f.stalled);
            // flown into the ground or an obstacle (not shot down first and falling)
            previewCrashes = !battle.Out(0) && sim.crashed[0] && sim.health[0] > 0;
            if (previewCrashes) warning = sim.hitObstacle[0] ? "OBSTACLE: this turn hits a cable or structure" : "TERRAIN: this turn flies into the ground";
            else if (battle.planes[0].Departed)
                warning = previewDeparts ? (plan.overpull > 0 ? "SPINNING: still overpulling, it stays departed" : "SPINNING: the arrow flies the recovery, it is not out yet")
                                         : "SPINNING: the arrow flies the recovery (idle, opposite rudder, stick forward)";
            else if (previewDeparts) warning = "OVERPULL: it will depart into a spin";
            else if ((plan.overpull > 0 || plan.HasMarks) && stalls) warning = "OVERPULL: the wing stalls, the turn mushes and bleeds speed";
            else warning = null;

            // who the planned burst hits, if they hold
            burstHits = new bool[battle.Count];
            var byTarget = new int[battle.Count];
            foreach (var h in preview.hits)
                if (h.shooter == 0 && h.target >= 0 && h.target < battle.Count) { burstHits[h.target] = true; byTarget[h.target]++; }
            confidenceTarget = -1;
            for (int i = 0; i < battle.Count; i++)
                if (byTarget[i] > 0 && battle.side[i] != battle.side[0] && (confidenceTarget < 0 || byTarget[i] > byTarget[confidenceTarget])) confidenceTarget = i;
            confidenceHits = -1; confidenceOf = 0;
            confidencePending = confidenceTarget >= 0 && battle.routes[confidenceTarget] == null && !battle.InFormation(confidenceTarget);
            confidenceAt = Time.unscaledTime + 0.25f;

            // The gun line: the same turn with the trigger held all the way and the enemy unbreakable, so every chance shows
            var full = plan; full.burstStart = 0; full.burstEnd = turnSec; full.missile = false;
            var gsim = battle.Clone();
            gsim.dispersion = false;
            for (int j = 0; j < gsim.Count; j++) if (gsim.side[j] != gsim.side[0]) gsim.health[j] = double.PositiveInfinity;
            gsim.bullets.Clear();
            gunPreview = gsim.Resolve(new TurnPlan?[] { full }, turnSec, true);
            // the hit window: when a round fired connects, short gaps merged
            hitWindows.Clear();
            var times = gunPreview.hits.Where(h => h.shooter == 0).Select(h => h.fireTime - gunPreview.startTime).OrderBy(x => x).ToList();
            double half = battle.combat[0].packetSec / 2;
            foreach (var ht in times)
            {
                double a = Math.Max(0, ht - half), b = Math.Min(turnSec, ht + half);
                int n = hitWindows.Count;
                if (n > 0 && a - hitWindows[n - 1].b < 0.1) hitWindows[n - 1] = (hitWindows[n - 1].a, Math.Max(b, hitWindows[n - 1].b));
                else hitWindows.Add((a, b));
            }
        }

        /// <summary>
        /// The confidence check: fly the plan again with the enemy the burst hits breaking left, breaking right and pulling up
        /// instead of holding, and count how many of the four still hit it. Run once the plan has settled, not every frame.
        /// </summary>
        void RunConfidence()
        {
            confidencePending = false;
            int j = confidenceTarget;
            if (preview == null || j < 0 || j >= battle.Count || battle.Out(j)) return;
            var m = battle.planes[j];
            Vec3d fwd = m.Forward;
            Vec3d right = new Vec3d(fwd.y, -fwd.x, 0);   // the horizontal right of its heading: a break is a level turn
            right = right.Length > 1e-3 ? right.Normalized : m.Right;
            double theta = 90 * Math.PI / 180;           // past what it can reach in a turn: it pulls as hard as the wing allows
            Vec3d[] aims =
            {
                (fwd * Math.Cos(theta) - right * Math.Sin(theta)).Normalized,   // break left
                (fwd * Math.Cos(theta) + right * Math.Sin(theta)).Normalized,   // break right
                (fwd * Math.Cos(theta) + m.Up * Math.Sin(theta)).Normalized,    // pull up
            };
            int hits = 1;   // holding, it is hit: that is why we are checking
            foreach (var aim in aims)
            {
                var plans = new TurnPlan?[battle.Count];
                plans[0] = plan;
                plans[j] = new TurnPlan { aim = aim, throttle = 1, wep = m.Kias < 220 };
                var sim = battle.Clone();
                sim.dispersion = false;
                var r = sim.Resolve(plans, turnSec, false);
                if (r.hits.Any(h => h.shooter == 0 && h.target == j)) hits++;
            }
            confidenceHits = hits;
            confidenceOf = aims.Length + 1;
        }

        void DrawPlanning()
        {
            ShowAllLive();
            Vector3 c = PlayerPos;

            // The reach: a clear middle out to a thin white ring (the hardest clean turn), then a band shaded from white
            // into red out to the overpull edge, where the stick goes past the stall line. The band is a stack of rings
            // wide enough to overlap, so it reads as one shaded area.
            const int spokes = 48, bands = 16;
            var ring = new List<Vector3>(spokes);
            for (int k = 0; k < spokes; k++)
            {
                double phi = 2 * Math.PI * k / spokes;
                ring.Add(c + UnityFrame.Dir(env.Direction(phi, env.MaxAngle(phi))) * arrowLength);
            }
            lines.Strip(ring, 0.5f, shellColor, true);
            double spread = 0;
            for (int k = 0; k < spokes; k++) { double phi = 2 * Math.PI * k / spokes; spread += env.OuterAngle(phi) - env.MaxAngle(phi); }
            float bandW = (float)(spread / spokes * arrowLength / bands * 1.6);   // metres per ring, overlapping so it reads smooth
            for (int r = 0; r < bands; r++)
            {
                float u = (r + 0.5f) / bands;
                var pts = new List<Vector3>(spokes);
                for (int k = 0; k < spokes; k++)
                {
                    double phi = 2 * Math.PI * k / spokes;
                    double inner = env.MaxAngle(phi), outerA = env.OuterAngle(phi);
                    pts.Add(c + UnityFrame.Dir(env.Direction(phi, inner + (outerA - inner) * u)) * arrowLength);
                }
                lines.StripMetres(pts, bandW, Color.Lerp(reachRing, reachEdge, u), true);
            }
            lines.Line(c, c + UnityFrame.Dir(env.fwd) * arrowLength, 0.3f, new Color(1, 1, 1, 0.3f));

            if (plan.HasMarks)
            {
                tip.gameObject.SetActive(false);
                if (preview != null)
                {
                    int fi = Mathf.Clamp(Mathf.RoundToInt(markT / (float)preview.dt), 0, preview.FrameCount - 1);
                    var fr = preview.frames[0][fi];
                    Vector3 p = UnityFrame.Pos(fr.pos), up = UnityFrame.Dir(fr.up), fwd = UnityFrame.Dir(fr.vel.Normalized);
                    Vector3 right = Vector3.Cross(up, fwd);
                    float sz = Mathf.Max(6f, W(p) * 30f);
                    lines.Line(p - right * sz, p + right * sz, 1.6f, arrowColor);   // the wings at that moment: you see the bank
                    lines.Line(p, p + up * sz * 0.6f, 1.2f, arrowColor);
                }
                goto Paths;
            }

            // The arrow
            Vector3 t = TipPos;
            Color arrow = Color.Lerp(arrowColor, overpullColor, plan.overpull > 0 ? 0.5f + 0.5f * (float)plan.overpull : 0f);
            if (previewDeparts) arrow = Color.Lerp(overpullColor, Color.white, 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.time * 10f)));
            tipMat.color = arrow;
            lines.Line(c, t, 1.2f, arrow);
            tip.position = t;
            tip.localScale = Vector3.one * W(t) * 8f;

        Paths:
            if (preview == null) return;
            int end = preview.FrameCount - 1;
            bool badPath = previewDeparts || previewCrashes;
            DrawRibbon(preview.frames[0], end, badPath ? overpullColor : ghostColor);
            DrawPath(preview.frames[0], end, badPath ? overpullColor : ghostColor, holoMine == null, false);
            for (int i = 1; i < battle.Count; i++)
            {
                if (battle.Out(i)) continue;
                bool noGhost = i >= holos.Length || holos[i] == null;
                if (battle.routes[i] != null) DrawPath(preview.frames[i], end, bomberColor, noGhost);
                else if (battle.side[i] != 0) { if (showProjection) DrawPath(preview.frames[i], end, projectionColor, noGhost); }
                else DrawPath(preview.frames[i], end, wingmanColor, noGhost);
            }
            ShowHolo(holoMine, preview.frames[0][end], true, badPath ? overpullColor : holoColor);
            for (int i = 1; i < holos.Length; i++)
            {
                bool hitByMe = i < burstHits.Length && burstHits[i] && battle.side[i] != battle.side[0];
                ShowHolo(holos[i], preview.frames[i][end], !battle.Out(i) && (battle.side[i] == 0 || showProjection),
                         battle.side[i] == 0 ? holoFriendColor : hitByMe ? holoTargetHitColor : holoEnemyColor);
            }
            foreach (var tr in preview.traces)
            {
                if (tr.owner != 0 && !showProjection) continue;
                if (tr.missile) { DrawPreviewTrace(tr, new Color(1f, 1f, 1f, 0.8f), 1.0f, 0.3f); continue; }
                DrawPreviewTrace(tr, OutcomeColor(tr, TracerColor(tr.owner)), tr.hit ? 0.5f : 0.35f, tr.hit ? 0.5f : 0.1f);
            }
            // The gun line: where a burst would go, faint, until a burst is set
            if (showGunLine && gunPreview != null && plan.burstEnd == plan.burstStart)
                foreach (var tr in gunPreview.traces)
                {
                    if (tr.owner != 0 || tr.missile) continue;
                    var col = OutcomeColor(tr, myTracer);
                    col.a *= gunLineAlpha;
                    DrawPreviewTrace(tr, col, 0.3f, col.a * 0.3f);
                }
            DrawHazards();
        }

        /// <summary>A preview tracer's whole path; fading to tailAlpha at its far end.</summary>
        void DrawPreviewTrace(Trace tr, Color col, float width, float tailAlpha)
        {
            if (tr.points.Count == 0) return;
            var pts = new List<Vector3>();
            for (int i = 0; i < tr.points.Count; i += 2) pts.Add(UnityFrame.Pos(tr.points[i]));
            pts.Add(UnityFrame.Pos(tr.points[tr.points.Count - 1]));
            if (pts.Count < 2) return;
            lines.Strip(pts, width, col, false, new Color(col.r, col.g, col.b, tailAlpha));
        }

        /// <summary>A preview tracer's colour by what it does: red if it hits, yellow if it passes close, else the normal colour.</summary>
        Color OutcomeColor(Trace tr, Color normal) => tr.hit ? hitTracer : tr.Near ? nearTracer : normal;

        /// <summary>A missile in playback: a white smoke trail behind it, fading, and a bright head.</summary>
        void DrawMissile(Trace tr, int now)
        {
            var pts = new List<Vector3>();
            for (int f = Mathf.Max(tr.startFrame, now - 60); f <= now; f += 3) { var p = tr.At(f); if (p != null) pts.Add(UnityFrame.Pos(p.Value)); }
            var head = tr.At(now);
            if (head != null) pts.Add(UnityFrame.Pos(head.Value));
            if (pts.Count >= 2) lines.Strip(pts, 1.6f, new Color(0.95f, 0.95f, 0.95f, 0.75f), false, new Color(0.9f, 0.9f, 0.9f, 0f));
            if (head != null) { Vector3 h = UnityFrame.Pos(head.Value); lines.Line(h, h + (h - pts[Mathf.Max(0, pts.Count - 2)]).normalized * 3f, 1.2f, new Color(1f, 0.85f, 0.4f)); }
        }

        Color TracerColor(int owner) => owner == 0 ? myTracer
            : owner < 0 ? (-1 - owner < battle.flak.Length && battle.flak[-1 - owner].carrier >= 0 ? wingTracer : flakTracer)   // a B-17 gunner's, or the flak's
            : battle.side[owner] == 0 ? wingTracer : theirTracer;

        /// <summary>
        /// A path as a shaded band the width of a wingspan or so, lying along the wings at each moment, so you see the
        /// bank through the turn: faint where it starts, stronger towards where the turn leaves you.
        /// </summary>
        void DrawRibbon(AircraftFrame[] frames, int upTo, Color color)
        {
            var pts = new List<Vector3>();
            var side = new List<Vector3>();
            void Add(int i)
            {
                var f = frames[i];
                Vector3 fwd = UnityFrame.Dir(f.vel.Normalized), up = UnityFrame.Dir(f.up);
                Vector3 right = Vector3.Cross(up, fwd);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, fwd);
                pts.Add(UnityFrame.Pos(f.pos));
                side.Add(right);
            }
            for (int i = 0; i <= upTo; i += 2) Add(i);
            if (upTo % 2 != 0) Add(upTo);
            lines.Ribbon(pts, side, 6f, 6f, new Color(color.r, color.g, color.b, color.a * 0.12f), new Color(color.r, color.g, color.b, color.a * 0.5f));
        }

        /// <summary>A path through frames up to an index, with a small wings-and-fin mark at its end.</summary>
        void DrawPath(AircraftFrame[] frames, int upTo, Color color, bool endMark, bool line = true)
        {
            if (line)
            {
                var pts = new List<Vector3>();
                for (int i = 0; i <= upTo; i += 3) pts.Add(UnityFrame.Pos(frames[i].pos));
                pts.Add(UnityFrame.Pos(frames[upTo].pos));
                lines.Strip(pts, 0.8f, new Color(color.r, color.g, color.b, color.a * 0.35f), false, color);
            }
            if (!endMark) return;
            var f = frames[upTo];
            Vector3 p = UnityFrame.Pos(f.pos), fwd = UnityFrame.Dir(f.vel.Normalized), up = UnityFrame.Dir(f.up);
            Vector3 right = Vector3.Cross(up, fwd);
            float s = Mathf.Max(6f, W(p) * 25f);
            float lw = 1.2f;
            lines.Line(p - right * s, p + right * s, lw, color);
            lines.Line(p - fwd * s * 0.8f, p + fwd * s * 0.5f, lw, color);
            lines.Line(p - fwd * s * 0.8f, p - fwd * s * 0.8f + up * s * 0.45f, lw, color);
        }

        // ---------- the holographic ghosts ----------

        /// <summary>
        /// A see-through glowing copy of an aircraft's own model, for where the plan puts it: the scripts,
        /// the pilot and the smoke are taken out, and every surface gets one additive colour.
        /// </summary>
        GameObject MakeHolo(GameObject source, Color color, string name)
        {
            if (source == null) return null;
            var g = Instantiate(source);
            g.name = name;
            foreach (var a in g.GetComponentsInChildren<Animator>(true)) if (a != null) DestroyImmediate(a.gameObject);
            foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
            foreach (var ps in g.GetComponentsInChildren<ParticleSystem>(true)) if (ps != null) DestroyImmediate(ps.gameObject);
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var rb in g.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            var mat = HoloMaterial(color);
            holoMats.Add(mat);
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            g.SetActive(false);
            return g;
        }

        static Material HoloMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return new Material(Shader.Find("Sprites/Default")) { color = color };
            var m = new Material(shader);
            m.SetFloat("_Surface", 1);       // transparent
            m.SetFloat("_Blend", 2);         // additive: it glows over what is behind
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", color);
            return m;
        }

        void ShowHolo(GameObject holo, AircraftFrame f, bool show, Color color)
        {
            if (holo == null) return;
            holo.SetActive(show);
            if (!show) return;
            TurnAircraftView.Pose(holo.transform, f.pos, f.vel, f.up, f.alphaDeg);
            // a slow shimmer, like a projection
            float k = 0.8f + 0.2f * Mathf.Sin(Time.time * 6f) + 0.05f * Mathf.Sin(Time.time * 37f);
            var r = holo.GetComponentInChildren<Renderer>();
            if (r != null) r.sharedMaterial.SetColor("_BaseColor", new Color(color.r, color.g, color.b, color.a * k));
        }

        // ---------- playback ----------

        void Playback()
        {
            float speed = playbackSpeed * (Keyboard.current != null && Keyboard.current.tabKey.isPressed ? 3f : 1f);
            prevPlayT = playT;
            playT = Mathf.Min(playT + Time.deltaTime * speed, turnSec);
            int n = last.FrameCount - 1;
            float f = playT / (float)last.dt;
            int i = Mathf.Min(n - 1, Mathf.FloorToInt(f));
            double u = Math.Min(1, f - i);
            int now = Mathf.Min(n, Mathf.RoundToInt(f));
            for (int k = 0; k < views.Length && k < last.frames.Length; k++)
            {
                if (views[k] == null) continue;
                bool crashedNow = last.frames[k][now].down && battle.crashed[k] && now == n;
                views[k].ShowFrame(last.frames[k][i], last.frames[k][i + 1], u, Time.deltaTime, crashedNow);
            }

            DrawLastPaths(now);
            foreach (var tr in last.traces)
            {
                var p1 = tr.At(now);
                if (p1 == null) continue;
                if (tr.missile) { DrawMissile(tr, now); continue; }
                var p0 = tr.At(now - 3) ?? tr.points[0];
                Vector3 a = UnityFrame.Pos(p0), b = UnityFrame.Pos(p1.Value);
                lines.Line(a, b, 0.6f, TracerColor(tr.owner));
            }

            // Hits: a flash where they land, and the damage so far shown as smoke
            double t0 = last.startTime;
            double[] hp = (double[])healthBefore.Clone();
            foreach (var h in last.hits)
            {
                double ht = h.time - t0;
                if (ht <= playT) hp[h.target] -= h.damage;
                if (ht > prevPlayT && ht <= playT) { Flash(UnityFrame.Pos(h.pos)); PilotsReact(h, hp[h.target] <= 0); }
            }
            UpdateSmoke(hp, last.frames.Select(fr => fr[now].down).ToArray());

            if (playT >= turnSec - 1e-4f)
            {
                if (phaseAfterPlayback == Phase.Over) phase = Phase.Over;
                else if (newTurn) BeginPlanning();
                else phase = Phase.Planning;   // back to the plan in hand after a replay
                newTurn = false;
            }
        }

        bool newTurn;

        /// <summary>The pilots react to a hit as it lands: the one hit flinches, the shooter cheers a kill or shakes his fist.</summary>
        void PilotsReact(HitEvent h, bool killed)
        {
            PilotControls Pilot(int k) => k >= 0 && k < views.Length && views[k] != null ? views[k].GetComponentInChildren<PilotControls>() : null;
            Pilot(h.target)?.Play(PilotControls.Gesture.Flinch);
            Pilot(h.shooter)?.Play(killed ? PilotControls.Gesture.Cheer : PilotControls.Gesture.ShakeFist);
        }

        void DrawLastPaths(int upTo)
        {
            if (last == null) return;
            DrawPath(last.frames[0], upTo, ghostColor * new Color(1, 1, 1, 0.6f), false);
            for (int i = 1; i < last.frames.Length; i++)
            {
                var col = battle.routes[i] != null ? bomberColor : battle.side[i] != 0 ? projectionColor : wingmanColor;
                DrawPath(last.frames[i], upTo, col * new Color(1, 1, 1, 0.6f), false);
            }
        }

        double[] shownHp = new double[0];

        void UpdateSmoke(double[] hp, bool[] down)
        {
            shownHp = (double[])hp.Clone();
            for (int i = 0; i < battle.Count && i < views.Length; i++)
                if (views[i] != null) views[i].SetSmoke(down[i] ? 2 : hp[i] < battle.combat[i].health * 0.5 ? 1 : 0);
        }

        void Flash(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = flashMat;
            go.transform.position = at;
            flashes.Add((go.transform, Time.time));
        }

        void FadeFlashes()
        {
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                float age = Time.time - flashes[i].born;
                if (age > 0.35f) { Destroy(flashes[i].t.gameObject); flashes.RemoveAt(i); continue; }
                flashes[i].t.localScale = Vector3.one * Mathf.Lerp(4f, 0.5f, age / 0.35f);
            }
        }

        // ---------- screen ----------

        GUIStyle label, big, small, title;
        float helpTop = 190;

        void OnGUI()
        {
            if (battle == null) return;
            guiScale = Mathf.Clamp(Screen.height / 900f, 1f, 2.5f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * guiScale);
            float sw = Screen.width / guiScale, sh = Screen.height / guiScale;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                label.normal.textColor = new Color(0.85f, 1f, 0.85f);
                big = new GUIStyle(label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(1f, 0.9f, 0.4f);
                small = new GUIStyle(label) { fontSize = 13 };
                title = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
            }
            guiRects.Clear();
            // What is on screen now (during playback, not the end of the turn)
            var me = player.Model;
            var them = views.Length > focus && views[focus] != null ? views[focus].Model : battle.planes[focus];
            string themName = battle.planes[focus].numbers.name.Replace(" G-6", "").Replace("D Mustang", "");

            // Left: the player's aircraft
            Rect panel = new Rect(10, 10, 300, 170);
            guiRects.Add(panel);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(20, 14, 290, 26), $"P-51D   Turn {battle.turn + (phase == Phase.Planning ? 1 : 0)}", title);
            GUI.Label(new Rect(20, 42, 290, 22), $"{me.Kias:F0} KIAS   {me.AltFt:F0} ft   {me.g:F1} G", label);
            Bar(new Rect(20, 68, 270, 14), shownHp[0] / battle.combat[0].health, new Color(0.3f, 0.9f, 0.3f), "Airframe");
            Bar(new Rect(20, 88, 270, 14), battle.ammo[0] / battle.combat[0].ammoSec, new Color(0.9f, 0.75f, 0.2f), $"Ammo {battle.ammo[0]:F1} s   Missiles {battle.missiles[0]}");
            Vec3d los = them.state.pos - me.state.pos;
            double range = los.Length;
            double ata = Envelope.Angle(me.Forward, los) * 180 / Math.PI;
            double aa = Envelope.Angle(them.Forward, los) * 180 / Math.PI;
            GUI.Label(new Rect(20, 108, 290, 22), $"{themName} {range:F0} ft, {ata:F0}° off your nose", label);
            GUI.Label(new Rect(20, 128, 290, 22), $"You are {aa:F0}° off its tail ({(aa < 45 ? "behind it" : aa > 135 ? "in front of it" : "beside it")})", label);
            Bar(new Rect(20, 152, 270, 14), shownHp[focus] / battle.combat[focus].health, new Color(0.9f, 0.3f, 0.3f), themName);
            if (Bombing)
            {
                var bi = Enumerable.Range(0, battle.Count).Where(i => battle.routes[i] != null).ToList();
                int ours = 0, theirs = 0, oursAll = 0, theirsAll = 0;
                for (int i = 0; i < battle.Count; i++)
                {
                    if (battle.routes[i] != null) continue;
                    if (battle.side[i] == 0) { oursAll++; if (!battle.Out(i)) ours++; } else { theirsAll++; if (!battle.Out(i)) theirs++; }
                }
                float bh = 48 + 20 * bi.Count;
                Rect bp = new Rect(10, 184, 300, bh);
                guiRects.Add(bp);
                GUI.Box(bp, GUIContent.none);
                var lead = bi.FirstOrDefault(i => !battle.Out(i));
                var route = battle.routes[lead];
                string togo = route.Done ? "through" : $"waypoint {route.next} of {route.points.Length - 1}";
                GUI.Label(new Rect(20, 188, 290, 22), $"P-51s {ours}/{oursAll}   Bf 109s {theirs}/{theirsAll}   {togo}", label);
                for (int k = 0; k < bi.Count; k++)
                    Bar(new Rect(20, 214 + 20 * k, 270, 14), shownHp[bi[k]] / battle.combat[bi[k]].health, bomberColor, $"B-17 {k + 1}{(battle.Out(bi[k]) ? "  down" : "")}");
                helpTop = 184 + bh + 6;
            }
            else helpTop = 190;

            // Right: buttons
            float bx = sw - 190, by = 10;
            Rect buttons = new Rect(bx - 10, 0, 200, 380);
            guiRects.Add(buttons);
            bool planning = phase == Phase.Planning;
            GUI.enabled = planning;
            if (GUI.Button(new Rect(bx, by, 180, 44), phase == Phase.Thinking ? "Pilots thinking..." : "Fly turn  (Enter)")) Commit();
            GUI.enabled = true;
            by += 52;
            GUI.enabled = last != null && phase != Phase.Playing;
            if (GUI.Button(new Rect(bx, by, 180, 30), "Replay last turn  (R)")) Replay();
            GUI.enabled = true;
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), showProjection ? "Projection: on  (P)" : "Projection: off  (P)")) { showProjection = !showProjection; }
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), $"View: {(view != null ? view.focus.ToString() : "-")}  (V)") && view != null) view.NextFocus();
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), "Restart  (Backspace)")) Restart();
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), $"Mission: {Playing}  (M)")) SwitchMission();
            by += 36;
            var oodaStyle = new GUIStyle(GUI.skin.button) { fontStyle = ooda ? FontStyle.Bold : FontStyle.Normal };
            if (ooda) oodaStyle.normal.textColor = oodaStyle.hover.textColor = new Color(1f, 0.85f, 0.2f);
            if (GUI.Button(new Rect(bx, by, 180, 30), ooda ? $"OODA: {turnSec:0.#} s turns  (O)" : "OODA  (O)", oodaStyle)) SwitchOoda();
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), showHelp ? "Hide help  (H)" : "Help  (H)")) showHelp = !showHelp;

            if (planning) PlanningPanel(sw, sh);
            if (phase == Phase.Playing) GUI.Label(new Rect(0, sh - 60, sw, 30), $"Turn playing  {playT:F1} / {turnSec:F0} s   (hold Tab to speed up)", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            var warn = new GUIStyle(big) { fontSize = 26 };
            warn.normal.textColor = overpullColor;
            if (planning && warning != null) GUI.Label(new Rect(0, sh - 460, sw, 40), warning, warn);
            if (phase == Phase.Playing && last != null)
            {
                int now = Mathf.Min(last.FrameCount - 1, Mathf.RoundToInt(playT / (float)last.dt));
                var f = last.frames[0][now];
                if (f.departed) GUI.Label(new Rect(0, sh * 0.3f, sw, 50), "DEPARTED", warn);
                else if (f.stalled && lastOverpull > 0) GUI.Label(new Rect(0, sh * 0.3f, sw, 50), "STALL", warn);
            }
            if (phase == Phase.Over && outcome != null)
            {
                GUI.Label(new Rect(0, sh * 0.3f, sw, 50), outcome, big);
                GUI.Label(new Rect(0, sh * 0.3f + 50, sw, 30), "Restart for another go, or Replay to watch it again.", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            }
            if (notice != null && Time.time < noticeUntil)
                GUI.Label(new Rect(0, sh * 0.22f, sw, 30), notice, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });

            if (showHelp)
            {
                float hy = helpTop;
                Rect help = new Rect(10, hy, 440, 280);
                GUI.Box(help, GUIContent.none);
                guiRects.Add(help);
                GUI.Label(new Rect(20, hy + 6, 425, 270),
                    $"Plan where your nose points when the {turnSec:0.#} s turn ends. O: OODA, short turns.\n" +
                    "Drag the yellow tip inside the white ring (how far you can turn).\n" +
                    "Past the ring, up to the red ring, you overpull: stall, maybe spin.\n" +
                    "Blue: your path. Red: the enemy if they keep doing what they're doing.\n" +
                    (Bombing ? "Green: the bombers' course. Keep the Bf 109s off them; flak fires orange.\n" : "") +
                    "Your rounds: red hits, yellow just misses, orange misses. Lead the target.\n" +
                    "Drag on the timeline to fire; F fires where hits are predicted.\n\n" +
                    "Keys: WASD arrow (S pulls up, like the stick), Shift/Ctrl throttle,\n" +
                    "Space WEP, B bursts, G missile, T advanced timeline, " +
                    "Enter fly, R replay, P projection, V view, X straight, M mission, H help.\n" +
                    "Right-drag or drag away from the tip to look; wheel zooms.\n" +
                    "Pad: left stick arrow, triggers throttle, A fly, X bursts,\n" +
                    "RB fire at predicted hits, Y projection, B replay, right stick look.", small);
            }
        }

        void PlanningPanel(float sw, float sh)
        {
            float pw = Mathf.Min(640, sw - 40), px = (sw - pw) / 2, py = sh - 178;
            if (showMoves || plan.HasMarks) MovesPanel(px, py - 196, pw);
            else movesRect = Rect.zero;
            string adv = showMoves ? "Advanced: timeline (T)  \u25BE" : plan.HasMarks ? "Advanced: timeline in use (T)  \u25B8" : "Advanced: timeline (T)  \u25B8";
            Rect advRect = new Rect(px + pw - 230, (showMoves || plan.HasMarks) ? py - 236 : py - 40, 230, 24);
            guiRects.Add(advRect);
            if (GUI.Button(advRect, adv)) showMoves = !showMoves;
            Rect panel = new Rect(px - 10, py - 10, pw + 20, 178);
            GUI.Box(panel, GUIContent.none);
            guiRects.Add(panel);

            // Throttle and WEP
            GUI.Label(new Rect(px, py, 110, 22), plan.HasMarks ? $"Start {plan.throttle * 100:F0}%" : $"Throttle {plan.throttle * 100:F0}%", label);
            float thr = GUI.HorizontalSlider(new Rect(px + 120, py + 6, pw - 260, 20), (float)plan.throttle, 0f, 1f);
            if (Math.Abs(thr - plan.throttle) > 1e-4) { plan.throttle = thr; dirty = true; }
            bool wep = GUI.Toggle(new Rect(px + pw - 130, py, 130, 22), plan.wep, " WEP (Space)");
            if (wep != plan.wep) { plan.wep = wep; dirty = true; }

            // The burst timeline
            GUI.Label(new Rect(px, py + 28, pw, 22), plan.burstEnd > plan.burstStart
                ? $"Guns: fire {plan.burstStart:F2} to {plan.burstEnd:F2} s   (drag on the bar, tap to clear)"
                : "Guns: hold fire   (drag on the bar to set a burst)", label);
            burstRect = new Rect(px, py + 54, pw, 28);
            GUI.Box(burstRect, GUIContent.none);
            if (plan.burstEnd != plan.burstStart)
            {
                double a = Math.Min(plan.burstStart, plan.burstEnd), b = Math.Max(plan.burstStart, plan.burstEnd);
                Fill(new Rect(burstRect.x + (float)(a / turnSec) * pw, burstRect.y + 3, (float)((b - a) / turnSec) * pw, burstRect.height - 6), new Color(1f, 0.6f, 0.1f, 0.85f));
            }
            // The hit window: when rounds fired would connect if they hold (from the gun line), over the whole bar
            foreach (var w in hitWindows)
                Fill(new Rect(burstRect.x + (float)(w.a / turnSec) * pw, burstRect.y, Mathf.Max(2f, (float)((w.b - w.a) / turnSec) * pw), burstRect.height),
                     new Color(hitTracer.r, hitTracer.g, hitTracer.b, 0.35f));
            int predicted = 0;
            if (preview != null)
                foreach (var h in preview.hits)
                {
                    if (h.shooter == 0) predicted++;
                    float x = burstRect.x + (float)((h.fireTime - preview.startTime) / turnSec) * pw;
                    if (h.shooter != 0 && h.target != 0) continue;
                    Fill(new Rect(x - 1, burstRect.y, 2, burstRect.height), h.shooter == 0 ? Color.white : new Color(1f, 0.3f, 0.3f));
                }
            for (int s = 0; s <= (int)turnSec; s++)
                GUI.Label(new Rect(burstRect.x + s / turnSec * pw - 8, burstRect.yMax, 40, 20), $"{s}s", small);
            int theirs = preview == null ? 0 : preview.hits.Count(h => h.target == 0);
            if (predicted > 0)
            {
                var hitStyle = new GUIStyle(label) { fontStyle = FontStyle.Bold };
                hitStyle.normal.textColor = hitTracer;
                string sure = confidenceHits >= 0 ? $"   (hits in {confidenceHits} of {confidenceOf})" : confidencePending ? "   (checking...)" : "";
                string hitBack = theirs > 0 ? $"   you get hit {theirs}" : "";
                GUI.Label(new Rect(px, py + 104, pw - 140, 22), $"HITS: {predicted} if they hold{sure}{hitBack}", hitStyle);
            }
            else GUI.Label(new Rect(px, py + 104, pw - 140, 22), $"Predicted if they hold: you hit {predicted}, you get hit {theirs}", label);
            if (GUI.Button(new Rect(px + pw - 130, py + 102, 130, 26), "Fire at hits (F)")) FireAtPredicted();

            // The missile: armed, it leaves the first moment the seeker locks this turn
            int left = battle.missiles[0];
            bool lockedNow = battle.LockTarget(0, focus) >= 0;
            string mText;
            if (left <= 0) mText = "Missiles: none left";
            else if (!plan.missile) mText = $"Missiles: {left} left.  {(lockedNow ? "Seeker has a lock now." : "No lock: get behind one, 1,200 to 9,000 ft, within 30\u00B0 of your nose.")}";
            else
            {
                var mt = preview?.traces.FirstOrDefault(tr => tr.missile && tr.owner == 0);
                mText = mt == null ? "Missile armed: no lock on this path, it stays on the rail" : mt.hit ? "Missile armed: it hits, if they hold" : "Missile armed: it launches, but misses if they hold";
            }
            GUI.Label(new Rect(px, py + 134, pw - 140, 22), mText, label);
            GUI.enabled = left > 0;
            if (GUI.Button(new Rect(px + pw - 130, py + 132, 130, 26), plan.missile ? "Missile: ARMED (G)" : "Missile (G)")) ArmMissile(!plan.missile);
            GUI.enabled = true;
        }

        /// <summary>
        /// The timeline: a slider through the turn with a tick for each point, and the bank, pull, throttle, rudder and WEP
        /// in force at the slider's moment. Changing one sets it from that moment on (a point is made there);
        /// the x beside a setting the point sets lets it carry on from before instead.
        /// </summary>
        void MovesPanel(float px, float py, float pw)
        {
            const float H = 196;
            Rect panel = new Rect(px - 10, py - 10, pw + 20, H);
            GUI.Box(panel, GUIContent.none);
            guiRects.Add(panel);
            var m = battle.planes[0];
            var n = m.numbers;
            double avail = n.AvailableG(m.Kias);
            int at = PointAt(plan.marks, markT);
            Mark here = at >= 0 ? plan.marks[at] : new Mark(markT);
            var st = plan.StateAt(markT);

            string head = !plan.HasMarks
                ? $"Timeline at {markT:F1} s: flying to the arrow. Change anything below to set it from this moment instead."
                : at >= 0 ? $"Point at {markT:F1} s: bright settings change here, dim ones carry on from before."
                          : $"At {markT:F1} s (no point here): change anything to put a point here.";
            GUI.Label(new Rect(px, py, pw, 22), head, small);

            // the bar: ticks for points, the white slider, seconds under it
            movesRect = new Rect(px + 30, py + 22, pw - 60, 22);
            if (GUI.Button(new Rect(px, py + 22, 26, 22), "<")) JumpPoint(-1);
            if (GUI.Button(new Rect(px + pw - 26, py + 22, 26, 22), ">")) JumpPoint(1);
            GUI.Box(movesRect, GUIContent.none);
            float bw0 = movesRect.width;
            if (plan.HasMarks)
                foreach (var k in plan.marks)
                {
                    bool over = k.g != null && k.g.Value > avail;
                    Fill(new Rect(movesRect.x + (float)(k.time / turnSec) * bw0 - 3, movesRect.y, 6, movesRect.height), over ? overpullColor : arrowColor);
                }
            Fill(new Rect(movesRect.x + markT / turnSec * bw0 - 1, movesRect.y - 4, 2, movesRect.height + 8), Color.white);
            for (int sec = 0; sec <= (int)turnSec; sec++)
                GUI.Label(new Rect(movesRect.x + sec / turnSec * bw0 - 6, movesRect.yMax - 2, 30, 18), $"{sec}s", small);

            var on = new GUIStyle(label);
            var dim = new GUIStyle(label); dim.normal.textColor = new Color(0.6f, 0.7f, 0.6f, 0.8f);
            var red = new GUIStyle(label); red.normal.textColor = overpullColor;
            float col = pw / 2, lw = 150, y = py + 62;
            bool marks = plan.HasMarks;

            // left column: bank, pull, rudder
            double bankNow = BankAtSlider();
            Row(px, y, col - 10, lw, here.bankDeg != null, marks,
                st.bankDeg == null ? $"Bank: hold ({(bankNow >= 0 ? "R" : "L")}{Math.Abs(bankNow):F0}°)" : $"Bank {(st.bankDeg >= 0 ? "right" : "left")} {Math.Abs(st.bankDeg.Value):F0}°",
                on, dim, (float)bankNow, -180, 180, v => SetBank(v), () => EditPoint(k => { k.bankDeg = null; return k; }));
            Row(px, y + 26, col - 10, lw, here.g != null, marks,
                $"Pull {st.g:F1} G  (wing {avail:F1})", st.g > avail ? red : on, st.g > avail ? red : dim, (float)st.g, (float)n.minG, (float)n.maxG + 2, v => SetG(v), () => EditPoint(k => { k.g = null; return k; }));
            Row(px, y + 52, col - 10, lw, here.rudder != null, marks,
                st.rudder == 0 ? "Rudder: centred" : $"Rudder {(st.rudder > 0 ? "right" : "left")} {Math.Abs(st.rudder) * 100:F0}%",
                on, dim, (float)st.rudder, -1, 1, v => SetRudder(v), () => EditPoint(k => { k.rudder = null; return k; }));

            // right column: throttle, WEP, and the buttons
            float rx = px + col + 10;
            Row(rx, y, col - 10, 120, here.throttle != null, marks, $"Throttle {st.throttle * 100:F0}%", on, dim, (float)st.throttle, 0, 1,
                v => { double t = Math.Round(v * 20) / 20; EditPoint(k => { k.throttle = t; return k; }); }, () => EditPoint(k => { k.throttle = null; return k; }));
            bool wep = GUI.Toggle(new Rect(rx, y + 26, 120, 22), st.wep, marks && here.wep == null ? " WEP (carried)" : " WEP");
            if (wep != st.wep) EditPoint(k => { k.wep = wep; return k; });
            if (marks && here.wep != null && GUI.Button(new Rect(rx + 124, y + 26, 22, 22), "x")) EditPoint(k => { k.wep = null; return k; });
            if (GUI.Button(new Rect(rx + 150, y + 26, col - 160, 22), "Level, 1 G here")) EditPoint(k => { k.bankDeg = 0; k.g = 1; return k; });
            float bw = (col - 20) / 2;
            GUI.enabled = at >= 0;
            if (GUI.Button(new Rect(rx, y + 52, bw, 24), "Delete point")) DeleteMarkAtSlider();
            GUI.enabled = marks;
            if (GUI.Button(new Rect(rx + bw + 10, y + 52, bw, 24), "Clear all (X)")) ClearMarks();
            GUI.enabled = true;
            GUI.Label(new Rect(px, y + 80, pw, 20), "[ ] slider   , . points   Q/E bank   W/S pull   Z/C rudder   Shift/Ctrl throttle   Space WEP", small);
        }

        /// <summary>One setting: its label (bright when this point sets it), a slider, and an x to let it carry on instead.</summary>
        void Row(float x, float y, float w, float lw, bool setHere, bool marks, string text, GUIStyle on, GUIStyle dim,
                 float value, float lo, float hi, Action<float> set, Action unset)
        {
            GUI.Label(new Rect(x, y, lw, 22), text, !marks || setHere ? on : dim);
            float sw = w - lw - 30;
            float v = GUI.HorizontalSlider(new Rect(x + lw, y + 6, sw, 20), value, lo, hi);
            if (Math.Abs(v - value) > (hi - lo) * 0.004f) set(v);
            if (marks && setHere && GUI.Button(new Rect(x + lw + sw + 6, y, 22, 22), "x")) unset();
        }

        void Bar(Rect r, double fraction, Color c, string text)
        {
            Fill(r, new Color(0, 0, 0, 0.5f));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01((float)fraction), r.height), c);
            GUI.Label(new Rect(r.x + 4, r.y - 4, r.width, 22), text, small);
        }

        static Texture2D white;
        static void Fill(Rect r, Color c)
        {
            if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
        }

        // ---------- the hazards in the planning view ----------

        [Header("Hazards")]
        [Tooltip("Wires (balloon tethers, cable-car ropes, masts and their guys) nearer the player than this are drawn while planning, m.")]
        public float hazardShowM = 3000;
        public Color wireColor = new Color(0.55f, 1f, 1f, 0.4f);
        public Color flakReachColor = new Color(1f, 0.25f, 0.2f, 0.16f);
        HazardMarkers hazards;
        bool hazardsLooked;

        /// <summary>The wires near the player as thin bright lines, a ring round each barrage balloon, and each flak gun's reach as a faint dome.</summary>
        void DrawHazards()
        {
            if (!hazardsLooked) { hazards = FindFirstObjectByType<HazardMarkers>(); hazardsLooked = true; }
            Vector3 me = PlayerPos;
            float show2 = hazardShowM * hazardShowM;
            float SegDist2(Vector3 p, Vector3 a, Vector3 b)
            {
                Vector3 d = b - a;
                float u = d.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude);
                return (p - (a + d * u)).sqrMagnitude;
            }
            List<Vector3> Ring(Vector3 c, float r, float y, int n)
            {
                var pts = new List<Vector3>(n);
                for (int k = 0; k < n; k++)
                {
                    float a = 2 * Mathf.PI * k / n;
                    pts.Add(c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                }
                return pts;
            }
            if (hazards != null)
            {
                for (int i = 0; i < hazards.WireCount; i++)
                {
                    Vector3 a = hazards.wireStarts[i], b = hazards.wireEnds[i];
                    if (SegDist2(me, a, b) < show2) lines.Line(a, b, 0.4f, wireColor);
                }
                if (hazards.balloons != null)
                    foreach (var c in hazards.balloons)
                        if ((c - me).sqrMagnitude < show2) lines.Strip(Ring(c, hazards.balloonRadius * 1.5f, 0f, 16), 0.4f, wireColor, true);
            }
            // the flak's reach: a ring on the ground, two higher up and two arcs over the top
            if (battle == null || battle.flak == null) return;
            foreach (var g in battle.flak)
            {
                if (g == null || g.carrier >= 0) continue;
                Vector3 c = UnityFrame.Pos(g.pos);
                float r = (float)(g.rangeFt * Atmosphere.MPerFt);
                if ((c - me).magnitude > r + hazardShowM) continue;
                foreach (float el in new[] { 0f, 30f, 60f })
                {
                    float e = el * Mathf.Deg2Rad;
                    lines.Strip(Ring(c, r * Mathf.Cos(e), r * Mathf.Sin(e), 48), 0.5f, flakReachColor, true);
                }
                for (int m = 0; m < 2; m++)
                {
                    var arc = new List<Vector3>(25);
                    Vector3 across = m == 0 ? Vector3.right : Vector3.forward;
                    for (int k = 0; k <= 24; k++)
                    {
                        float a = Mathf.PI * k / 24;
                        arc.Add(c + across * (Mathf.Cos(a) * r) + Vector3.up * (Mathf.Sin(a) * r));
                    }
                    lines.Strip(arc, 0.5f, flakReachColor);
                }
            }
        }
    }
}
