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
    public partial class TurnBattle : MonoBehaviour
    {
        public TurnAircraftView player, enemy;
        public TurnCamera view;
        public Terrain terrain;
        public PilotHeadLook playerPilot, enemyPilot;
        [Tooltip("The bomber for the escort mission (hidden in the dogfight).")]
        public TurnAircraftView bomber;

        public enum Mission { Squadron, Escort, Dogfight, ValleyRun, TownStrike }
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
        [Tooltip("Valley Run: how far along the route the Spitfire starts, m (where the narrow pass begins; the world builder sets it).")]
        public float valleyRunStartM = 4400;
        public Color bomberColor = new Color(0.4f, 1f, 0.45f, 0.7f);

        [Header("Squadron")]
        [Tooltip("Turn-view prefabs (each with a TurnAircraftView) for the extra aircraft: P-51 wingmen, Bf 109s and B-17s.")]
        public GameObject wingmanPrefab, enemyPrefab, bomberPrefab;
        [Tooltip("The dogfight's other aircraft (the setup screen picks any type for either side): the Spitfire, the F-4 Phantom and the P-38, each with a TurnAircraftView. The P-51 is wingmanPrefab, the Bf 109 enemyPrefab.")]
        public GameObject spitfirePrefab, phantomPrefab, p38Prefab;
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
        [Tooltip("The dogfight starts over one of the island's arenas (Arenas.All: picked in the briefing, or at random), not startCentre.")]
        public bool useArenas = true;

        [Header("Turns")]
        public float turnSec = (float)Scenarios.TurnSec;
        public float playbackSpeed = 1f;
        public bool showProjection = true;
        public bool showHelp = true;
        [Tooltip("Draw the reach shell's rings round the arrow (a dev view, F1). Off: the stick shaker tells how hard you pull.")]
        public bool showReachRings = false;
        [Tooltip("Show the OVERPULL, SPINNING, STALL and DEPARTED warnings (a dev view, F3). Off: the stick shaker and the ghost path tell you.")]
        public bool showStallWarnings = false;
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
        Ooda.UI.StickThrottleGauge gauge;   // the stick (bottom middle) and the throttle (bottom left)
        Material tipMat;

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
        int confidencePct = -1;           // or the chance it hits across the enemy's likely plans, %; -1 not known
        bool confidencePending;
        float confidenceAt;
        GameObject holoMine, holoTheirs;
        GameObject[] holos = new GameObject[0];   // a ghost for each aircraft: where the preview puts it at the end of the turn
        readonly List<Material> holoMats = new List<Material>();

        // ---------- set up ----------

        void Start()
        {
            if (Ooda.UI.TitleScreen.ChosenMission is int chosen) mission = (Mission)chosen;   // picked on the title screen
            dogSetup = Ooda.UI.DogfightSetupScreen.Chosen.Copy();   // picked on the setup screen
            BotSetUp();
            TutorialSetUp();
            NetSetUp();
            NetBotSetUp();
            lines = new LinePool(transform, "Lines") { sizeAt = W };
            tipMat = new Material(Shader.Find("Sprites/Default")) { color = arrowColor };
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(t.GetComponent<Collider>());
            t.name = "ArrowTip";
            t.GetComponent<Renderer>().sharedMaterial = tipMat;
            t.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tip = t.transform;
            tip.SetParent(transform, false);
            if (terrain == null) terrain = Terrain.activeTerrain;
            grid = MakeGrid(terrain);
            gauge = Ooda.UI.StickThrottleGauge.Create();
            if (gauge != null && view != null && view.Cam != null) view.Cam.cullingMask &= ~(1 << Ooda.UI.StickThrottleGauge.Layer);
            if (view != null) { view.player = player.transform; view.enemy = enemy.transform; }
            if (playerPilot != null) playerPilot.target = enemy.transform;
            if (enemyPilot != null) enemyPilot.target = player.transform;
            holoMine = MakeHolo(player.gameObject, holoColor, "GhostP51");
            holoTheirs = MakeHolo(enemy.gameObject, holoEnemyColor, "GhostEnemy");
            MakeSpreadHolos();
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
        Mission Playing => mission == Mission.TownStrike ? (CanStrike ? Mission.TownStrike : CanSquadron ? Mission.Squadron : Mission.Dogfight)
                         : mission == Mission.ValleyRun ? (CanValleyRun ? Mission.ValleyRun : Mission.Dogfight)
                         : mission == Mission.Squadron && CanSquadron ? Mission.Squadron : mission != Mission.Dogfight && CanEscort ? Mission.Escort : Mission.Dogfight;

        static Vec3d Ft(Vector3 unityM) => new Vec3d(unityM.x * Atmosphere.FtPerM, unityM.z * Atmosphere.FtPerM, unityM.y * Atmosphere.FtPerM);

        public void Restart()
        {
            if (!NetRestarting()) return;   // online, the host starts each new fight
            StopReplayQuietly();
            thinking = null; thinkingFor = null;
            reading = null; readingFor = null; read = null;
            pinnedFocus = -1;
            RestoreHome();
            foreach (var g in spawned) if (g != null) Destroy(g);
            spawned.Clear();
            var route = HasRoute ? escortRoute.Select(Ft).ToArray() : null;
            dogCraft = null;
            dogPilots = null;
            switch (Playing)
            {
                case Mission.Squadron:
                    var flak = flakSites == null ? new Vec3d[0] : flakSites.Where(f => f != null).Select(f => Ft(f.position)).ToArray();
                    battle = Scenarios.Squadron(route, flak, squadronFighters, squadronBombers, bomberKias, squadronGapM);
                    break;
                case Mission.Escort: battle = Scenarios.Escort(route, bomberKias, escortGapM); break;
                case Mission.TownStrike: battle = StrikeBattle(); break;
                case Mission.ValleyRun:
                {
                    var guns = flakSites == null ? new Vec3d[0] : flakSites.Where(f => f != null).Select(f => Ft(f.position)).ToArray();
                    battle = Scenarios.ValleyRun(route, guns, valleyRunStartM);
                    break;
                }
                default: battle = tutorial ? TutorialBattle() : DogfightBattle(); break;
            }
            if (grid != null) battle.groundFt = grid.At; else battle.groundFt = GroundFt;
            battle.obstacleHit = ObstacleHit;
            battle.wires = MakeWires();
            NetSeat();

            // a view for each aircraft: the scene's player, enemy and bomber first, prefabs for the rest
            int n = battle.Count;
            views = new TurnAircraftView[n];
            // in the dogfight the scene's P-51 and Bf 109 stand in only for a P-51 of yours and a Bf 109 of theirs
            bool scenePlayer = dogCraft == null || dogCraft[0] == Craft.Mustang || CraftPrefab(dogCraft[0]) == null;
            bool sceneEnemy = dogCraft == null || dogCraft[1] == Craft.Bf109 || CraftPrefab(dogCraft[1]) == null;
            player.gameObject.SetActive(scenePlayer);
            enemy.gameObject.SetActive(sceneEnemy);
            views[0] = scenePlayer ? player : Spawn(CraftPrefab(dogCraft[0]), 0);
            views[1] = sceneEnemy ? enemy : Spawn(CraftPrefab(dogCraft[1]), 1);
            bool sceneBomberUsed = false;
            for (int i = 2; i < n; i++)
            {
                if (battle.routes[i] != null && !sceneBomberUsed && bomber != null) { views[i] = bomber; sceneBomberUsed = true; continue; }
                var prefab = battle.routes[i] != null ? bomberPrefab
                           : dogCraft != null && i < dogCraft.Length && CraftPrefab(dogCraft[i]) != null ? CraftPrefab(dogCraft[i])
                           : battle.side[i] == 0 ? wingmanPrefab : enemyPrefab;
                if (prefab == null) continue;
                views[i] = Spawn(prefab, i);
            }
            if (bomber != null) bomber.gameObject.SetActive(sceneBomberUsed);
            // who flies each plane (the briefing's picks in the dogfight, else each type's usual pilot), before anything looks the pilot up
            for (int i = 0; i < n; i++)
            {
                if (views[i] == null) continue;
                var roster = views[i].GetComponent<PilotRoster>();
                if (roster != null) roster.Use(dogPilots != null && i < dogPilots.Length ? dogPilots[i] : PilotKind.Usual);
            }
            if (scenePlayer && player != null)
            {
                if (playerPilot != null) playerPilot.hideHead = false;
                playerPilot = player.GetComponentInChildren<PilotHeadLook>();
            }
            for (int i = 0; i < n; i++) if (views[i] != null) views[i].Bind(battle.planes[i]);
            // the same holographic ghost as the player's for everyone: green ours, red theirs
            foreach (var m in spawnedHoloMats) Destroy(m);
            spawnedHoloMats.Clear();
            holos = new GameObject[n];
            holos[0] = holoMine;
            if (!scenePlayer)
            {
                // you fly something other than the scene's P-51: its own ghost, and the camera, the cockpit and the pilot on it
                int before = holoMats.Count;
                holos[0] = MakeHolo(views[0].gameObject, holoColor, "Ghost 0");
                if (holos[0] != null) spawned.Add(holos[0]);
                for (int k = before; k < holoMats.Count; k++) spawnedHoloMats.Add(holoMats[k]);
                holoMats.RemoveRange(before, holoMats.Count - before);
                AdoptPlayer();
                if (homeHolo != null) homeHolo.SetActive(false);
            }
            RemakeSpreadHolos(views[1] != null ? views[1].gameObject : enemy.gameObject);
            for (int i = 1; i < n; i++)
            {
                if (views[i] == null) continue;
                int before = holoMats.Count;
                holos[i] = MakeHolo(views[i].gameObject, battle.side[i] == MySide ? holoFriendColor : holoEnemyColor, $"Ghost {i}");
                if (holos[i] != null) spawned.Add(holos[i]);
                for (int k = before; k < holoMats.Count; k++) spawnedHoloMats.Add(holoMats[k]);
                holoMats.RemoveRange(before, holoMats.Count - before);
            }

            // an AI pilot for everyone but the player and the bombers
            ais = new EnemyAi[n];
            ais[0] = new EnemyAi { me = 0, rng = new Lcg(71), ignoreObstacles = true, role = EnemyAi.Role.Fighter };   // flies your lead while you fly another
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
            TutorialPilots();   // training: the lesson's foe (no pilot for the drone) and its first step
            NetSitDown();   // online: into your own first plane
            healthBefore = (double[])battle.health.Clone();
            shownHp = (double[])battle.health.Clone();
            ListFlight();
            plan = new TurnPlan { throttle = battle.planes[0].throttle };
            last = null;
            outcome = null;
            Ooda.Fx.ToonFx.ClearAll();
            FightStarted();
            TapeStarts();
            BeginPlanning();
            if (view != null) view.BehindPlayer();
            StrikeRestarted();
            if (Dogfighting && !Online && !tutorial) Notice(DogfightNotice);
        }

        /// <summary>What the WEP box is called for the plane you are in: a jet's is its afterburner.</summary>
        string WepName => battle != null && battle.planes[0].numbers.thrustV0Kt > 1000 ? "BURNER" : "WEP";

        /// <summary>The turn view for an aircraft type in the dogfight, or null if the scene has none for it (then the side's usual one stands in).</summary>
        GameObject CraftPrefab(Craft c) => c == Craft.Spitfire ? spitfirePrefab : c == Craft.Phantom ? phantomPrefab : c == Craft.P38 ? p38Prefab : c == Craft.Bf109 ? enemyPrefab : wingmanPrefab;

        TurnAircraftView Spawn(GameObject prefab, int i)
        {
            var g = Instantiate(prefab);
            g.name = $"{prefab.name} {i}";
            spawned.Add(g);
            return g.GetComponent<TurnAircraftView>();
        }

        void BeginPlanning()
        {
            phase = Phase.Planning;
            sightAuto = true;
            FlightTurnStarts();
            // with only one fighter of yours, it is always yours to fly: it never flies itself on its AI pilot's plan
            int fighters = 0;
            for (int i = 0; i < battle.Count; i++) if (battle.side[i] == battle.side[0] && battle.routes[i] == null && !battle.Out(i) && NetMine(i)) fighters++;
            if (fighters <= 1 && battle.orders[0] == Order.AutoFight) battle.orders[0] = Order.YouControl;
            var m = battle.planes[0];
            env = Envelope.Compute(m, turnSec);
            plan.aim = m.Forward;
            plan.overpull = 0;
            plan.burstStart = plan.burstEnd = 0;
            plan.wep = false;
            plan.marks = null;
            plan.missile = false;
            plan.rocket = plan.brake = false;
            plan.mine = plan.ram = plan.cloak = plan.jam = false;
            plan.rudder = 0;
            markT = 0;
            burstPreset = 0;
            OrdersTurnStarts();
            dirty = true;
            ShowAllLive();
            UpdateSmoke(battle.health, battle.down);
            FocusNearest();
            StartReading();
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
                if (battle.side[i] == battle.side[0] || battle.Out(i) || HiddenFromMe(i)) continue;
                double r = (battle.planes[i].state.pos - battle.planes[0].state.pos).Length + (battle.routes[i] != null ? 1e6 : 0);
                if (r < bestR) { bestR = r; best = i; }
            }
            focus = best >= 0 ? best : 1;
            if (pinnedFocus >= 0 && pinnedFocus < battle.Count && !battle.Out(pinnedFocus) && battle.side[pinnedFocus] != battle.side[0] && !HiddenFromMe(pinnedFocus)) focus = pinnedFocus;
            else pinnedFocus = -1;
            var t = views.Length > focus && views[focus] != null ? views[focus].transform : enemy.transform;
            if (view != null) view.enemy = t;
            if (playerPilot != null) playerPilot.target = t;
        }

        /// <summary>Fly turn: the AI pilots plan on other threads (the battle does not change meanwhile), then the turn is flown.</summary>
        void Commit()
        {
            if (phase != Phase.Planning) return;
            if (AskAboutUnplanned()) return;
            TutorialCommitted();
            var b = battle; var pilots = ais; float ts = turnSec; int n = battle.Count; var rd = reading; var mine = ManualMask();
            mine[0] = battle.orders[0] != Order.AutoFight;   // the plane you are on flies your plan, unless you've told it to fight by itself
            thinkingFor = b;
            thinking = Task.Run(() =>
            {
                try { rd?.Wait(); } catch (AggregateException) { }   // the reading scores the same tries the pilots pick from: let it finish first
                var plans = new TurnPlan?[n];
                Parallel.For(0, n, i => { if (pilots[i] != null && !mine[i] && !b.Out(i) && !b.InFormation(i) && (b.strike == null || !b.strike.OnPatrol(i))) plans[i] = pilots[i].Plan(b, ts); });
                return plans;
            });
            phase = Phase.Thinking;
        }

        void FinishCommit()
        {
            var task = thinking; thinking = null;
            if (thinkingFor != battle) return;   // restarted meanwhile
            if (reading != null && reading.IsCompleted) FinishReading();
            bool onLead = OnLead;
            TurnPlan?[] plans;
            if (task.IsFaulted) { Debug.LogException(task.Exception); plans = new TurnPlan?[battle.Count]; }
            else plans = task.Result;
            if (battle.orders[0] != Order.AutoFight || !plans[0].HasValue) plans[0] = plan;
            ApplyManual(plans);
            if (Online) { NetSendTurn(plans, onLead); return; }   // the turn is flown when both of you have planned
            FlyTurn(plans, onLead);
        }

        /// <summary>Fly the turn from every plane's plan (by local index) and play it back.</summary>
        void FlyTurn(TurnPlan?[] plans, bool onLead)
        {
            healthBefore = (double[])battle.health.Clone();
            lastOverpull = plan.overpull;
            last = Online ? net.seating.ResolveInWireOrder(battle, plans, turnSec, true) : battle.Resolve(plans, turnSec, true);
            RecordTurn();
            TurnFlown(plans, onLead);
            TutorialTurnFlown();
            bool over = FightIsOver();
            phaseAfterPlayback = over ? Phase.Over : Phase.Planning;
            if (over) { outcome = Outcome(); FightOver(); TutorialFightOver(); }
            newTurn = true;
            StartPlayback();
        }

        void SwitchOoda()
        {
            if (Online) { NetNotice("Online fights keep to 3 s turns."); return; }
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
            if (Online && Dogfighting) return NetOutcome();
            if (battle.strike != null) return StrikeOutcome();
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
            if (battle.Count > 2)
            {
                int foes = 0, foesDown = 0, ours = 0, oursDown = 0;
                for (int i = 0; i < battle.Count; i++)
                    if (battle.side[i] != battle.side[0]) { foes++; if (battle.Out(i)) foesDown++; } else { ours++; if (battle.Out(i)) oursDown++; }
                string tally = $" {foesDown} of {foes} {SideKind(1 - battle.side[0], foes > 1)} down, {oursDown} of {ours} {SideKind(battle.side[0], ours > 1)} lost.";
                if (foesDown == foes && oursDown == ours) return "Everyone went down. A draw." + tally;
                if (foesDown == foes) return "The sky is clear. You win!" + tally;
                return (ours > 1 ? "Your whole flight is down." : battle.crashed[0] ? "You flew into the ground." : "You were shot down.") + tally;
            }
            bool them = battle.down[1];
            if (me && them) return "Both aircraft went down. A draw.";
            string foe = SideKind(battle.side[1], false);
            if (them) return battle.crashed[1] ? $"The {foe} flew into the ground. You win!" : $"{foe} shot down. You win!";
            return battle.crashed[0] ? "You flew into the ground." : "You were shot down.";
        }

        /// <summary>What a side flies, in a word or two: "Bf 109s" when they are all one type, else "fighters" (a mixed dogfight).</summary>
        string SideKind(int side, bool plural)
        {
            Craft? kind = null; bool mixed = false;
            for (int i = 0; i < battle.Count; i++)
            {
                if (battle.side[i] != side || battle.routes[i] != null) continue;
                var c = DogfightSetup.Of(battle.planes[i].numbers);
                if (kind == null) kind = c; else if (kind != c) mixed = true;
            }
            if (kind == null || mixed) return plural ? "fighters" : "fighter";
            return DogfightSetup.Short(kind.Value) + (plural ? "s" : "");
        }

        void Notice(string s) { notice = s; noticeUntil = Time.time + 3f; }

        void SwitchMission()
        {
            if (Online) { NetNotice("Online fights are dogfights for now."); return; }
            tutorial = false;   // another mission ends training
            int missions = Enum.GetValues(typeof(Mission)).Length;
            for (int k = 0; k < missions; k++)
            {
                mission = (Mission)(((int)mission + 1) % missions);
                if (Playing == mission) break;   // skip what this scene can't play
            }
            Restart();
            Notice(Playing == Mission.Squadron ? "Squadron: 8 P-51s and 3 B-17s against 8 Bf 109s and the flak. Get the bombers through."
                 : Playing == Mission.TownStrike ? "Town Strike: plan a hidden path to the factory, then get the bombers there."
                 : Playing == Mission.Escort ? "Escort: keep the Bf 109 off the B-17."
                 : Playing == Mission.ValleyRun ? "Valley Run: you and your wingman low in the valley after two Bf 109s. Mind the wires and the balloons."
                 : DogfightNotice);
        }

        // ---------- every frame ----------

        void Update()
        {
            if (view != null) view.sightPose = false;   // set again below while the gunsight view is up
            NetUpdate();
            if (MenuUpdate()) return;   // Esc: the menu is open and the battle waits
            if (replaying) { ReplayUpdate(); return; }
            if (briefing) { UpdateBriefing(); return; }
            if (reading != null && reading.IsCompleted) FinishReading();
            if (thinking != null && thinking.IsCompleted) FinishCommit();
            HandleInput();
            TutorialUpdate();
            SightCamera();
            WatchOrders();
            SquadShotsCheck();
            BotUpdate();
            NetBotUpdate();
            lines.Begin();
            if (phase == Phase.Playing) Playback();
            else if (phase == Phase.Planning || phase == Phase.Thinking) DrawPlanning();
            else { DrawLastPaths(last != null ? last.FrameCount - 1 : 0); RevealAll(); }
            lines.End();
            tip.gameObject.SetActive((phase == Phase.Planning || phase == Phase.Thinking) && !plan.HasMarks && ArrowShown);
            if (phase != Phase.Planning && phase != Phase.Thinking)
            {
                foreach (var h in holos) if (h != null) h.SetActive(false);
                if (holoTheirs != null) holoTheirs.SetActive(false);
                HideSpread();
            }
            if (playerPilot != null && view != null) playerPilot.hideHead = view.focus == TurnCamera.Focus.Cockpit;
            UpdateGauge();
        }

        // ---------- the stick in the middle, the throttle on the left ----------

        void UpdateGauge()
        {
            SyncWep();
            if (gauge == null) { Rumble(0); return; }
            bool show = phase != Phase.Over && !battle.Out(0);
            gauge.SetVisible(show);
            if (!show) { gauge.Shake = 0; Rumble(0); return; }
            GaugeInput(out var stick, out float over, out float throttle, out bool wep);
            gauge.Show(stick, over, throttle, wep);
            gauge.Rudder = (float)plan.rudder;
            gaugeThrottle = throttle; gaugeWep = wep;
            gauge.Shake = ShakeFor(stick, over, (phase == Phase.Planning || phase == Phase.Thinking) && !plan.HasMarks);
            Rumble(menuOpen || bot ? 0 : gauge.Shake);
        }

        /// <summary>
        /// The stick shaker: still until the pull reaches 70% of the hardest clean turn, then buzzing hard by the
        /// ring, and violently through the overpull band. There is no caption for it: the stick (and the pad) shake.
        /// With the arrow the pull is how far out the tip sits in any direction; otherwise it is the stick's back travel.
        /// </summary>
        static float ShakeFor(Vector2 stick, float over, bool arrow)
        {
            const float ring = Ooda.UI.StickThrottleGauge.RingTravel;
            float p = (arrow ? stick.magnitude : Mathf.Max(0, stick.y)) / ring;
            float onset = Mathf.Clamp01((p - 0.7f) / 0.3f);
            return onset > 0 || over > 0 ? 0.35f + onset * 0.3f + Mathf.Clamp01(over) * 0.35f : 0f;
        }

        float gaugeThrottle;
        bool gaugeWep;

        /// <summary>
        /// The bottom of the screen, left to right: the throttle quadrant (the lever's picture and the buttons that go
        /// with the engine: throttle, WEP, rocket, speed brake), the stick in the middle with the rudder pedals under it,
        /// and the weapons panel on the right (guns, missile, flares, IR, the specials, the timeline).
        /// </summary>
        public struct ControlsLayout
        {
            public bool pictures;                    // the stick and throttle pictures fit; false: the arrow comes back
            public Rect stick, pedals, throttle;     // the pictures and the pedal bar (zero without pictures)
            public Rect quadrant;                    // the engine buttons beside the throttle
            public Rect weapons;                     // the gun MFD, bottom right; the switch row sits above it
            public float weaponsRowW;                // how wide the switch row may be (right-aligned)
            public Rect switchRow;                   // where the weapon switches go (flare hold, the specials), right-aligned in it
            public float stickTop;                   // the top of the middle (the stick's caption), for text and the timeline above it
            public Rect asi, alt, gmeter;            // the dials in the panel (zero when there is no room for them)
            public Rect lights;                      // the system lights in a row along the top of the dash (zero with the dials)
            public Rect attitude;                    // the attitude indicator beside the G meter (zero with the dials)
            public float panelTop;                   // the cockpit panel's flat top, over the stick
        }

        ControlsLayout ctl;

        const float CtlMargin = 10, CtlBottom = 8, PedalH = 26, QuadW = 160, QuadRow = 30,
                    WeaponsMinW = 400, WeaponsMaxW = 580;

        /// <summary>The pictures as big as the room allows: shrink them until the quadrant and the weapons fit beside the stick.</summary>
        public static ControlsLayout LayoutControls(float sw, float sh, bool havePictures, bool havePedals = false)
        {
            var c = new ControlsLayout();
            float qh = 2 * QuadRow + SwitchH;   // the throttle, its slider, and the engine's switches
            float mh = Mathf.Clamp(sh * 0.34f, 220f, 280f), mw = mh * 1.3f;   // the gun MFD
            for (float h = Mathf.Min(240f, sh * 0.27f); havePictures && h >= 110; h -= 10)
            {
                float w = h * 0.9f, pw = Mathf.Max(w * 1.1f, 240f);
                float side = (sw - pw) / 2 - 2 * CtlMargin;
                float th = h * 0.9f, tw = th * 0.9f;
                if (side < tw + 8 + QuadW || side < WeaponsMinW) continue;
                if (havePedals)
                {
                    // the pedals' picture at the foot of the stick, the stick standing between the two pedals
                    pw = Mathf.Max(w * 1.5f, 280f);
                    float ph = pw / Ooda.UI.StickThrottleGauge.PedalsAspect;
                    c.pedals = new Rect((sw - pw) / 2, sh - CtlBottom - ph, pw, ph);
                    c.stick = new Rect((sw - w) / 2, sh - CtlBottom - ph * 0.12f - h, w, h);
                }
                else
                {
                    c.pedals = new Rect((sw - pw) / 2, sh - CtlBottom - PedalH, pw, PedalH);
                    c.stick = new Rect((sw - w) / 2, c.pedals.y - 4 - h, w, h);
                }
                // the throttle on the left, standing in front of the panel's slope, the quadrant beside it
                c.throttle = new Rect(CtlMargin, sh - CtlBottom - th, tw, th);
                c.quadrant = new Rect(c.throttle.xMax + 8, sh - CtlBottom - qh, QuadW, qh);
                // the panel's flat top just over the stick's grip, so the stick stands inside it; the system lights in a
                // row along the top of the dash, over the stick; the stick's caption and the text above them
                c.panelTop = c.stick.y - 10;
                float lw = Mathf.Min(520f, sw * 0.34f);
                c.lights = new Rect((sw - lw) / 2, c.panelTop - 24, lw, 20);
                c.stickTop = c.lights.y - 22;
                // the gun MFD set in the panel right of the stick, under the flat top; the weapon switches in the corner past it
                float rx0 = Mathf.Max(c.stick.xMax, c.pedals.xMax) + 14;
                float ww = Mathf.Min(mw, sw * 0.78f - rx0, (sh - CtlBottom - c.panelTop - 14) * 1.3f);
                if (ww >= 240)
                {
                    c.weapons = new Rect(rx0, sh - CtlBottom - ww / 1.3f, ww, ww / 1.3f);
                    float rowX = c.weapons.xMax + 12;
                    c.weaponsRowW = sw - CtlMargin - rowX;
                    c.switchRow = c.weaponsRowW >= 140
                        ? new Rect(rowX, sh - CtlBottom - SwitchH, c.weaponsRowW, SwitchH)
                        : new Rect(c.weapons.x, c.weapons.y - SwitchH - 6, c.weaponsRowW = c.weapons.width, SwitchH);
                }
                else
                {
                    // too tight in the middle: bottom right, the switches over it
                    c.weaponsRowW = Mathf.Min(WeaponsMaxW, side);
                    ww = Mathf.Min(mw, side);
                    c.weapons = new Rect(sw - CtlMargin - ww, sh - CtlBottom - mh, ww, mh);
                    c.switchRow = new Rect(sw - CtlMargin - c.weaponsRowW, c.weapons.y - SwitchH - 6, c.weaponsRowW, SwitchH);
                }
                // the instruments together between the quadrant and the stick: airspeed and altitude side by side along
                // the top, the G meter and the attitude indicator under them
                float lx0 = c.quadrant.xMax + 14, lx1 = Mathf.Min(c.stick.x, c.pedals.x) - 14;
                float rowsH = sh - CtlBottom - (c.panelTop + 22) - 2 * 14;   // two rows, each with its caption under it
                float d = Mathf.Min(130f, (lx1 - lx0 - 10) / 2, rowsH / 2);
                c.pictures = true;
                if (d >= 70)
                {
                    float mid = (lx0 + lx1) / 2, topY = c.panelTop + 22, lowY = sh - CtlBottom - 14 - d;
                    c.asi = new Rect(mid - d - 5, topY, d, d);
                    c.alt = new Rect(mid + 5, topY, d, d);
                    c.gmeter = new Rect(mid - d - 5, lowY, d, d);
                    c.attitude = new Rect(mid + 5, lowY, d, d);
                }
                return c;
            }
            // no room for the pictures: the quadrant bottom left, the weapons beside it, and the arrow to steer
            c.quadrant = new Rect(CtlMargin, sh - CtlBottom - qh, QuadW, qh);
            c.weaponsRowW = Mathf.Min(WeaponsMaxW, sw - QuadW - 3 * CtlMargin);
            float rw = Mathf.Min(mw, c.weaponsRowW);
            c.weapons = new Rect(sw - CtlMargin - rw, sh - CtlBottom - mh, rw, mh);
            c.switchRow = new Rect(sw - CtlMargin - c.weaponsRowW, c.weapons.y - SwitchH - 6, c.weaponsRowW, SwitchH);
            c.stickTop = Mathf.Min(c.quadrant.y, c.weapons.y - SwitchH - 8);
            return c;
        }

        /// <summary>The throttle lever on the left and the stick in the middle, the pedals under it. No warnings: the stick shakes.</summary>
        void DrawGauge(float sw, float sh)
        {
            ctl = LayoutControls(sw, sh, gauge != null && gauge.StickTexture != null, gauge != null && gauge.PedalsTexture != null);
            stickRect = pedalRect = throttleRect = Rect.zero;
            if (!ctl.pictures || !gauge.Visible) return;
            CockpitPanel(sw, sh);
            Instruments();
            Rect left = ctl.throttle, mid = ctl.stick;
            float sk = gauge.Shake;   // the shaker rattles the stick's picture too, harder the deeper you pull
            float jig = sk * 9f;
            Rect shook = new Rect(mid.x + jig * Mathf.Sin(Time.unscaledTime * 131f), mid.y + jig * Mathf.Sin(Time.unscaledTime * 97f + 1f), mid.width, mid.height);
            GUI.DrawTexture(left, gauge.ThrottleTexture, ScaleMode.ScaleToFit, true);
            if (gauge.PedalsTexture != null) GUI.DrawTexture(ctl.pedals, gauge.PedalsTexture, ScaleMode.ScaleToFit, true);   // behind the stick
            GUI.DrawTexture(shook, gauge.StickTexture, ScaleMode.ScaleToFit, true);
            var cap = new GUIStyle(small) { alignment = TextAnchor.UpperCenter };
            bool planning = phase == Phase.Planning;
            if (!planning && phase != Phase.Playing)   // planning and playing, the quadrant beside it says the throttle
                Shadow(new Rect(left.x, left.y - 18, left.width, 20), gaugeWep ? $"Throttle {gaugeThrottle * 100:F0}%  {WepName}" : $"Throttle {gaugeThrottle * 100:F0}%", cap);
            bool steer = planning && !plan.HasMarks;
            Shadow(new Rect(mid.x - 40, ctl.stickTop + 1, mid.width + 80, 20), steer ? "Stick: drag to steer" : "Stick", cap);   // over the lights along the dash
            guiRects.Add(left); guiRects.Add(mid);
            stickRect = mid;
            throttleRect = planning ? left : Rect.zero;
            if (gauge.PedalsTexture != null) PedalsCaption(ctl.pedals, cap);
            else DrawPedals(ctl.pedals, cap);
        }

        /// <summary>
        /// What the stick shows. Planning with the arrow: where the arrow sits in the reach shell, up = back and
        /// right = right, the white ring at <see cref="Ooda.UI.StickThrottleGauge.RingTravel"/> and the overpull band the
        /// rest of the way. Planning with the timeline: the G and the roll at the slider's moment. Playing: the stick
        /// the plane is flying with, its G and roll rate.
        /// </summary>
        void GaugeInput(out Vector2 stick, out float over, out float throttle, out bool wep)
        {
            const float ring = Ooda.UI.StickThrottleGauge.RingTravel;
            var me = battle.planes[0];
            if (phase == Phase.Planning || phase == Phase.Thinking)
            {
                if (!plan.HasMarks)
                {
                    env.Polar(plan.aim, out double phi, out double theta);
                    float r = Mathf.Min(1f, (float)(theta / Math.Max(1e-6, env.MaxAngle(phi))));
                    over = (float)plan.overpull;
                    stick = new Vector2(Mathf.Sin((float)phi), Mathf.Cos((float)phi)) * (r * ring + over * (1 - ring));
                    throttle = (float)plan.throttle; wep = plan.wep;
                    return;
                }
                var st = plan.StateAt(markT);
                stick = new Vector2(Mathf.Clamp(PreviewRollDps(markT) / 120f, -1, 1), PullTravel(me, st.g, out over));
                throttle = (float)st.throttle; wep = st.wep;
                return;
            }
            var m = player.Model;
            stick = new Vector2(Mathf.Clamp((float)m.rollRateDps / 120f, -1, 1), PullTravel(m, m.g, out over));
            if (last != null)
            {
                // a stalled wing: the stick is past the stall line, as far as the plan overpulled
                var f = last.frames[0][Mathf.Min(last.FrameCount - 1, Mathf.RoundToInt(playT / (float)last.dt))];
                if (f.stalled && lastOverpull > 0) { over = (float)lastOverpull; stick.y = ring + over * (1 - ring); }
            }
            throttle = (float)m.throttle;
            wep = plan.HasMarks ? plan.StateAt(playT).wep : plan.wep;
        }

        /// <summary>The stick's back travel for a G: the most the wing gives clean at this speed is the ring, the plan's G
        /// past that runs into the overpull band (2 G more is full aft); a push goes forward the same way.</summary>
        static float PullTravel(FlightModel m, double g, out float over)
        {
            const float ring = Ooda.UI.StickThrottleGauge.RingTravel;
            over = 0;
            if (g >= 1)
            {
                double clean = Math.Max(1.5, m.numbers.AvailableG(m.Kias));
                if (g <= clean) return (float)((g - 1) / (clean - 1)) * ring;
                over = Mathf.Clamp01((float)((g - clean) / 2));
                return ring + over * (1 - ring);
            }
            double neg = Math.Min(-0.5, m.numbers.AvailableNegG(m.Kias));
            return -Mathf.Min(1f, (float)((1 - g) / (1 - neg))) * ring;
        }

        /// <summary>The ghost's roll rate at t into the turn (degrees a second, right positive), from the preview.</summary>
        float PreviewRollDps(float t)
        {
            if (preview == null || preview.FrameCount < 2) return 0;
            int i = Mathf.Clamp(Mathf.RoundToInt(t / (float)preview.dt), 1, preview.FrameCount - 1);
            var a = preview.frames[0][i - 1]; var b = preview.frames[0][i];
            double d = AutoPilot.BankDeg(b.vel.Normalized, b.up) - AutoPilot.BankDeg(a.vel.Normalized, a.up);
            while (d > 180) d -= 360; while (d < -180) d += 360;
            return (float)(d / preview.dt);
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
            if (Pressed(Key.W) && CanWatchReplay) { StartReplay(); return; }
            if (Pressed(Key.M)) SwitchMission();
            if (Pressed(Key.O)) SwitchOoda();
            if (Pressed(Key.F3)) { showStallWarnings = !showStallWarnings; Notice(showStallWarnings ? "Dev: stall and spin warnings on (F3)" : "Dev: stall and spin warnings off (F3)"); }
            if (Pressed(Key.F2)) { showArrow = !showArrow; Notice(showArrow ? "Dev: arrow on (F2)" : "Dev: arrow off (F2): drag the stick"); }
            if (Pressed(Key.F1)) { showReachRings = !showReachRings; Notice(showReachRings ? "Dev: reach rings on (F1)" : "Dev: reach rings off (F1)"); }
            if (Pressed(Key.Backspace) || (pad != null && pad.startButton.wasPressedThisFrame)) Restart();
            if (Pressed(Key.R) || (pad != null && pad.buttonEast.wasPressedThisFrame)) { if (phase == Phase.Over && Dogfighting) Restart(); else Replay(); }   // a new one on one once it is over
            if (Pressed(Key.P) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) showProjection = !showProjection;
            if ((Pressed(Key.V) || (pad != null && pad.rightStickButton.wasPressedThisFrame)) && view != null) view.NextFocus();

            // Looking around: right-drag, a drag off the arrow, the right stick; the wheel zooms
            if (view != null)
            {
                if (mouse != null)
                {
                    if (mouse.rightButton.isPressed) view.Orbit(mouse.delta.ReadValue() * 0.25f);
                    float sc = mouse.scroll.ReadValue().y;
                    Vector2 mp = mouse.position.ReadValue();
                    bool overPanel = guiRects.Any(r => r.Contains(new Vector2(mp.x, Screen.height - mp.y) / guiScale));   // the wheel scrolls the help there
                    if (Mathf.Abs(sc) > 0.01f && !overPanel) view.Zoom(Mathf.Sign(sc));
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
                    else if (PressStick(gui)) { }
                    else if (PressPedals(gui)) { }
                    else if (PressThrottle(gui)) { }
                    else if (guiRects.Any(r => r.Contains(gui))) { }
                    else if (phase == Phase.Planning && !plan.HasMarks && ArrowShown && NearTip(pos)) dragArrow = true;
                    else dragLook = true;
                }
                else if (pointer.press.isPressed)
                {
                    if (dragArrow) DragArrow(pos);
                    else if (dragStick) DragStick(gui);
                    else if (dragPedals) SetPedals(gui.x);
                    else if (dragThrottle) DragThrottle(gui);
                    else if (dragBurst) SetBurstFromBar(gui.x, false);
                    else if (dragMoves) SetMarkTime(gui.x);
                    else if (dragLook && view != null && (mouse == null || !mouse.rightButton.isPressed)) view.Orbit((pos - lastPointer) * 0.25f);
                }
                if (pointer.press.wasReleasedThisFrame) { dragArrow = dragLook = dragMoves = dragStick = dragPedals = dragThrottle = false; if (dragBurst) { dragBurst = false; TidyBurst(); } }
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
            else if (!plan.HasMarks && kb != null)
            {
                // Z/C are the rudder pedals: left and right foot, held for as long as the key is down; X centres them too
                float rud = 0;
                if (kb.zKey.isPressed) rud -= 1;
                if (kb.cKey.isPressed) rud += 1;
                if (rud != 0) { plan.rudder = Math.Max(-1, Math.Min(1, plan.rudder + rud * 1.5 * dt)); dirty = true; }
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
            if (Pressed(Key.X) || (pad != null && pad.leftShoulder.wasPressedThisFrame)) { if (plan.HasMarks) ClearMarks(); else { SetAim(env.fwd); if (plan.rudder != 0) { plan.rudder = 0; dirty = true; } } }

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
                if (!Jet)   // no WEP switch on a piston fighter: Space pushes the throttle to the stop, where WEP is
                {
                    if (plan.HasMarks) EditPoint(k => { k.throttle = 1; return k; });
                    else { plan.throttle = 1; dirty = true; }
                }
                else if (plan.HasMarks) { bool v = !plan.StateAt(markT).wep; EditPoint(k => { k.wep = v; return k; }); }
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
            ExtrasInput(kb, pad);
            FunnelInput(kb, dt);

            if (dirty) RunPreview();
            // the what-if-it-breaks check, once the plan has settled (not while dragging or every frame)
            if (confidencePending && !dirty && !dragArrow && !dragStick && !dragBurst && Time.unscaledTime >= confidenceAt) RunConfidence();
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
            if (times.Count == 0) { Notice("No hits predicted on this path against the bright ghost. Steer until it sits in the gun funnel."); plan.burstStart = plan.burstEnd = 0; }
            else { plan.burstStart = Math.Max(0, times.Min() - 0.15); plan.burstEnd = Math.Min(turnSec, times.Max() + 0.15); }
            dirty = true;
            RunPreview();
        }

        void RunPreview()
        {
            var sim = battle.Clone();
            sim.dispersion = false;
            preview = sim.Resolve(WithLikeliest(plan), turnSec, true);
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

            // who the planned burst hits, if they fly the bright ghost
            burstHits = new bool[battle.Count];
            var byTarget = new int[battle.Count];
            foreach (var h in preview.hits)
                if (h.shooter == 0 && h.target >= 0 && h.target < battle.Count) { burstHits[h.target] = true; byTarget[h.target]++; }
            confidenceTarget = -1;
            for (int i = 0; i < battle.Count; i++)
                if (byTarget[i] > 0 && battle.side[i] != battle.side[0] && (confidenceTarget < 0 || byTarget[i] > byTarget[confidenceTarget])) confidenceTarget = i;
            confidenceHits = -1; confidenceOf = 0; confidencePct = -1;
            confidencePending = confidenceTarget >= 0 && battle.routes[confidenceTarget] == null && !battle.InFormation(confidenceTarget);
            confidenceAt = Time.unscaledTime + 0.25f;

            // The gun line: the same turn with the trigger held all the way and the enemy unbreakable, so every chance shows
            var full = plan; full.burstStart = 0; full.burstEnd = turnSec; full.missile = false;
            var gsim = battle.Clone();
            gsim.dispersion = false;
            for (int j = 0; j < gsim.Count; j++) if (gsim.side[j] != gsim.side[0]) gsim.health[j] = double.PositiveInfinity;
            gsim.bullets.Clear();
            gunPreview = gsim.Resolve(WithLikeliest(full), turnSec, true);
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
            confidencePct = SpreadChance(j);
            if (confidencePct >= 0) return;   // the spread says it better than the three breaks
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
            int hits = 1;   // on the bright ghost it is hit: that is why we are checking
            foreach (var aim in aims)
            {
                var plans = WithLikeliest(plan);
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
            if (preview == null) SpecialsPlanning();   // (with a preview it is done at the end, over the paths)
            Vector3 c = PlayerPos;

            // The reach: a clear middle out to a thin white ring (the hardest clean turn), then a band shaded from white
            // into red out to the overpull edge, where the stick goes past the stall line. The band is a stack of rings
            // wide enough to overlap, so it reads as one shaded area.
            const int spokes = 48, bands = 16;
            if (!showReachRings) goto Arrow;   // a dev view now: the stick shaker tells how hard you pull
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

        Arrow:
            DrawTargetBracket();
            if (ArrowShown) lines.Line(c, c + UnityFrame.Dir(env.fwd) * arrowLength, 0.3f, new Color(1, 1, 1, 0.3f));

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

            // The arrow (a dev view now: the stick steers)
            if (!ArrowShown) goto Paths;
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
            bool sightView = SightViewOn;   // from your ghost's seat: only the funnel, the others and the ground, nothing of your own in the way
            if (!sightView)
            {
                DrawRibbon(preview.frames[0], end, badPath ? overpullColor : ghostColor);
                DrawPath(preview.frames[0], end, badPath ? overpullColor : ghostColor, holoMine == null, false);
            }
            for (int i = 1; i < battle.Count; i++)
            {
                if (battle.Out(i) || HiddenFromMe(i)) continue;   // a stealthy enemy: no path, no ghost
                bool noGhost = i >= holos.Length || holos[i] == null;
                var seen = Seen(i, preview.frames[i]);   // jammed: the enemies' paths where they aren't
                if (battle.routes[i] != null) DrawPath(seen, end, bomberColor, noGhost);
                else if (battle.side[i] != 0) { if (showProjection) DrawPath(seen, end, projectionColor, noGhost); }
                else DrawPath(seen, end, wingmanColor, noGhost);
            }
            // lined up behind it, your ghost shows where it is at the funnel's moment
            ShowHolo(holoMine, preview.frames[0][sightView ? Mathf.Min(end, FunnelFrame()) : end], true, badPath ? overpullColor : holoColor);
            for (int i = 1; i < holos.Length; i++)
            {
                bool hitByMe = i < burstHits.Length && burstHits[i] && battle.side[i] != battle.side[0];
                ShowHolo(holos[i], Seen(i, preview.frames[i])[end], !battle.Out(i) && !HiddenFromMe(i) && JamFlicker(i) && (battle.side[i] == 0 || showProjection) && !(sightView && battle.side[i] != battle.side[0]),   // lined up, only the funnel's target shows (ShowFunnelTarget)
                         battle.side[i] == 0 ? holoFriendColor : hitByMe ? holoTargetHitColor : holoEnemyColor);
            }
            if (JammedNow) HideSpread();   // jammed: no read on their likely moves
            else if (sightView) ShowFunnelTarget();   // lined up: the target's likely moves as models at the funnel's moment
            else DrawSpreadAndLead();
            DrawFunnel();
            if (!sightView)
            foreach (var tr in preview.traces)
            {
                if (tr.owner != 0 && !showProjection || HiddenFromMe(tr.owner) || JammedFrom(tr.owner)) continue;
                if (tr.missile) { DrawPreviewTrace(tr, new Color(1f, 1f, 1f, 0.8f), 1.0f, 0.3f); continue; }
                DrawPreviewTrace(tr, OutcomeColor(tr, TracerColor(tr.owner)), tr.hit ? 0.5f : 0.35f, tr.hit ? 0.5f : 0.1f);
            }
            // The gun line: where a burst would go, faint, until a burst is set
            if (showGunLine && !sightView && gunPreview != null && plan.burstEnd == plan.burstStart)
                foreach (var tr in gunPreview.traces)
                {
                    if (tr.owner != 0 || tr.missile) continue;
                    var col = OutcomeColor(tr, myTracer);
                    col.a *= gunLineAlpha;
                    DrawPreviewTrace(tr, col, 0.3f, col.a * 0.3f);
                }
            DrawHazards();
            SpecialsPlanning();
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

        /// <summary>
        /// A missile in playback at frame now: its motor a small orange glow, and behind it a trail of light toon smoke
        /// (Fx.ToonFx) that billows out and lingers a couple of seconds after it hits.
        /// </summary>
        void DrawMissile(Trace tr, int index, int now, float dt)
        {
            int last = tr.startFrame + tr.points.Count - 1;
            if (now < tr.startFrame || dt <= 0f) return;
            int smokeFrames = Mathf.CeilToInt(Ooda.Fx.ToonFx.MissileSmokeLife / dt);
            if (now - last > smokeFrames) return;
            var fx = Ooda.Fx.ToonFx.Get();
            int seed = index * 7919 + tr.startFrame * 31 + tr.owner + 17;
            var head = tr.At(now);
            if (head != null)
            {
                Vector3 h = UnityFrame.Pos(head.Value), back = UnityFrame.Pos(tr.At(now - 2) ?? tr.points[0]);
                Vector3 dir = (h - back).sqrMagnitude > 1e-4f ? (h - back).normalized : Vector3.forward;
                fx.Tracer(h - dir * 5f, h, new Color(1f, 0.6f, 0.2f, 1f), seed);
            }
            // its puffs back along its path (newest first)
            smokePath.Clear();
            int k0 = Mathf.Min(now, last);
            for (int k = k0; k >= tr.startFrame && k > now - smokeFrames; k--) smokePath.Add(UnityFrame.Pos(tr.points[k - tr.startFrame]));
            fx.MissileSmoke(smokePath, (now - k0) * dt, dt, seed, 60000 + index, k0);
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
            TurnAircraftView.Pose(holo.transform, f.pos, f.Nose, f.up, f.alphaDeg);
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
            DrawExtras(now);
            for (int ti = 0; ti < last.traces.Count; ti++)
            {
                var tr = last.traces[ti];
                if (tr.missile) { DrawMissile(tr, ti, now, (float)last.dt); continue; }
                GlowTracer(tr, ti, now, TracerColor(tr.owner));
            }

            // Hits: a flash where they land, and the damage so far shown as smoke
            double t0 = last.startTime;
            double[] hp = (double[])healthBefore.Clone();
            foreach (var h in last.hits)
            {
                double ht = h.time - t0;
                if (ht <= playT) hp[h.target] -= h.damage;
                if (ht > prevPlayT && ht <= playT) { Flash(UnityFrame.Pos(h.pos), hp[h.target] <= 0 && hp[h.target] + h.damage > 0); PilotsReact(h, hp[h.target] <= 0); }
            }
            UpdateSmoke(hp, last.frames.Select(fr => fr[now].down).ToArray());
            StrikePlayback(t0 + prevPlayT, t0 + playT);

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
                var fr = last.frames[i];
                if (fr != null && fr.Length > 0 && fr[Mathf.Min(upTo, fr.Length - 1)].cloaked && i < battle.Count && battle.side[i] != battle.side[0]) continue;   // a stealthy enemy leaves no path
                DrawPath(fr, upTo, col * new Color(1, 1, 1, 0.6f), false);
            }
        }

        double[] shownHp = new double[0];

        void UpdateSmoke(double[] hp, bool[] down)
        {
            shownHp = (double[])hp.Clone();
            for (int i = 0; i < battle.Count && i < views.Length; i++)
                if (views[i] != null) views[i].SetSmoke(down[i] ? 2 : hp[i] < battle.combat[i].health * 0.5 ? 1 : 0);
        }

        readonly List<Vector3> smokePath = new List<Vector3>(32);

        /// <summary>A round in playback at frame now, as a toon effect (Fx.ToonFx): a glowing streak over its last three frames.</summary>
        void GlowTracer(Trace tr, int index, int now, Color col)
        {
            var p1 = tr.At(now);
            if (p1 == null) return;
            int seed = index * 7919 + tr.startFrame * 31 + tr.owner;
            Ooda.Fx.ToonFx.Get().Tracer(UnityFrame.Pos(tr.At(now - 3) ?? tr.points[0]), UnityFrame.Pos(p1.Value), col, seed);
        }

        /// <summary>A hit lands: a toon star and sparks; the hit that kills blows the aircraft up.</summary>
        void Flash(Vector3 at, bool kill = false)
        {
            var fx = Ooda.Fx.ToonFx.Get();
            if (kill) fx.Blast(at, 2.5f);
            else fx.Hit(at);
        }

        // ---------- screen ----------

        GUIStyle label, big, small, title;
        float helpTop = 190;
        Vector2 helpScroll;   // how far the help is scrolled when it is taller than the room above the throttle

        void OnGUI()
        {
            if (battle == null) return;
            Ooda.UI.BriefingStyle.Use();
            guiScale = Mathf.Clamp(Screen.height / 900f, 1f, 2.5f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * guiScale);
            float sw = Screen.width / guiScale, sh = Screen.height / guiScale;
            // under the menu the screen is drawn but takes no clicks or keys
            var ev = Event.current.type;
            if (!menuOpen || ev == EventType.Repaint || ev == EventType.Layout) BattleGUI(sw, sh);
            IrGui(sw, sh);
            if (menuOpen) MenuGUI(sw, sh);
        }

        void BattleGUI(float sw, float sh)
        {
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                label.normal.textColor = new Color(0.85f, 1f, 0.85f);
                big = new GUIStyle(label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(1f, 0.9f, 0.4f);
                big.font = Ooda.UI.BriefingStyle.Stencil;
                small = new GUIStyle(label) { fontSize = 13 };
                title = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
            }
            guiRects.Clear();
            if (replaying) { ReplayGUI(sw, sh); return; }
            if (briefing) { BriefingGUI(sw, sh); return; }
            // What is on screen now (during playback, not the end of the turn)
            var me = player.Model;
            var them = views.Length > focus && views[focus] != null ? views[focus].Model : battle.planes[focus];
            string themName = EnemyName(focus);

            // Left: the player's aircraft
            Rect panel = new Rect(10, 10, 300, 170);
            guiRects.Add(panel);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(20, 14, 290, 26), $"{(flight.Count > 1 ? FlightName(0) : DogfightSetup.Short(DogfightSetup.Of(battle.planes[0].numbers)))}   Turn {battle.turn + (phase == Phase.Planning ? 1 : 0)}", title);
            string extra = me.RocketBurning ? "   ROCKET" : me.brakeOut ? "   BRAKE" : "";
            GUI.Label(new Rect(20, 42, 290, 22), $"{me.Kias:F0} KIAS   {me.AltFt:F0} ft   {me.g:F1} G{extra}", label);
            Bar(new Rect(20, 68, 270, 14), shownHp[0] / battle.combat[0].health, new Color(0.3f, 0.9f, 0.3f), "Airframe");
            Bar(new Rect(20, 88, 270, 14), battle.ammo[0] / battle.combat[0].ammoSec, new Color(0.9f, 0.75f, 0.2f), $"Ammo {battle.ammo[0]:F1} s   Missiles {battle.missiles[0]}   Flares {battle.flares[0]}");
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
            StrikePanel(sw);

            // Right: buttons
            float bx = sw - 190, by = 10;
            Rect buttons = new Rect(bx - 10, 0, 200, 416);
            guiRects.Add(buttons);
            bool planning = phase == Phase.Planning;
            GUI.enabled = planning;
            if (GUI.Button(new Rect(bx, by, 180, 44), phase == Phase.Thinking ? (netWaiting ? $"Waiting for {Friend}..." : "Pilots thinking...") : "Fly turn  (Enter)")) Commit();
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
            GUI.enabled = net == null || net.IsHost;
            if (GUI.Button(new Rect(bx, by, 180, 30), "Restart  (Backspace)")) Restart();
            GUI.enabled = true;
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), $"Mission: {Playing}  (M)")) SwitchMission();
            by += 36;
            var oodaStyle = new GUIStyle(GUI.skin.button) { fontStyle = ooda ? FontStyle.Bold : FontStyle.Normal };
            if (ooda) oodaStyle.normal.textColor = oodaStyle.hover.textColor = new Color(1f, 0.85f, 0.2f);
            if (GUI.Button(new Rect(bx, by, 180, 30), ooda ? $"OODA: {turnSec:0.#} s turns  (O)" : "OODA  (O)", oodaStyle)) SwitchOoda();
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), showHelp ? "Hide help  (H)" : "Help  (H)")) showHelp = !showHelp;
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), "Menu  (Esc)")) OpenMenu(true);

            DrawGauge(sw, sh);
            RosterGUI(sw);
            OrdersGUI(sw);
            UnplannedGUI(sw, sh);
            if (planning) PlanningPanel(sw, sh);
            else if (phase == Phase.Playing && ctl.pictures) PlaybackPanel();
            DogfightGUI(sw, sh);
            TutorialGUI(sw, sh);
            if (phase == Phase.Playing) GUI.Label(new Rect(0, ctl.stickTop - 30, sw, 30), $"Turn playing  {playT:F1} / {turnSec:F0} s   (hold Tab to speed up)", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            var warn = new GUIStyle(big) { fontSize = 26 };
            warn.normal.textColor = overpullColor;
            bool wingWarning = warning != null && (warning.StartsWith("OVERPULL") || warning.StartsWith("SPINNING"));
            if (planning && warning != null && (!wingWarning || showStallWarnings)) GUI.Label(new Rect(0, sh - 460, sw, 40), warning, warn);
            if (phase == Phase.Playing && last != null && showStallWarnings)
            {
                int now = Mathf.Min(last.FrameCount - 1, Mathf.RoundToInt(playT / (float)last.dt));
                var f = last.frames[0][now];
                if (f.departed) GUI.Label(new Rect(0, sh * 0.3f, sw, 50), "DEPARTED", warn);
                else if (f.stalled && lastOverpull > 0) GUI.Label(new Rect(0, sh * 0.3f, sw, 50), "STALL", warn);
            }
            if (phase == Phase.Over && outcome != null)
            {
                GUI.Label(new Rect(0, sh * 0.3f, sw, 50), outcome, big);
                GUI.Label(new Rect(0, sh * 0.3f + 50, sw, 30), tape.Count > 0 ? "Watch the whole fight again from any camera, fly it again, or go back to the main menu." : "Fly it again, or go back to the main menu.", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
                bool was = GUI.enabled;
                EndButtons(sw, sh * 0.3f + 92);
                GUI.enabled = was;
            }
            if (notice != null && Time.time < noticeUntil)
                GUI.Label(new Rect(0, sh * 0.22f, sw, 30), notice, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
            NetGUI(sw, label);

            if (showHelp)
            {
                float hy = helpTop;
                string helpText =
                    $"Plan where your nose points when the {turnSec:0.#} s turn ends. O: OODA, short turns.\n" +
                    "Drag the stick to steer (down pulls); the bar under it is the rudder. It shakes\n" +
                    "near your hardest turn, and violently in the overpull: stall, maybe spin.\n" +
                    "Drag the throttle lever (left) toward the stick to open it. F1: reach rings.\n" +
                    "Blue: your path. Red: where the enemy may go, the bright one likeliest.\n" +
                    "Green funnel: your rounds at the moment you hover on the burst bar.\n" +
                    "Fire when the target's wings fit across it. K funnel, L the old lead ring.\n" +
                    (Bombing ? "Green: the bombers' course. Keep the Bf 109s off them; flak fires orange.\n" : "") +
                    "Your rounds: red hits, yellow just misses, orange misses. Lead the target.\n" +
                    "Drag on the timeline to fire; F fires where hits are predicted.\n\n" +
                    "Keys: WASD arrow (S pulls up, like the stick), Shift/Ctrl throttle,\n" +
                    "Space full throttle (a jet: burner), 1 rocket, 2 speed brake, 3 hold flares, I IR view, Tab next plane, N next target,\n" +
                    "4 mine, 5 blade, 6 stealth, 7 jammer, B bursts, G missile, T timeline,\n" +
                    "Z/C rudder pedals (slow and nose-high, the rudder swings the nose: hammerhead).\n" +
                    "Enter fly, R replay, P projection, V view, X straight, M mission, H help.\n" +
                    "Drag the view or right-drag to look; wheel zooms. F2: the old arrow.\n" +
                    "Pad: left stick arrow, triggers throttle, A fly, X bursts,\n" +
                    "RB fire at predicted hits, Y projection, B replay, right stick look,\n" +
                    "d-pad right rocket, left speed brake, View button your next plane.";
                // as tall as its text needs (wrapped), but never down over the throttle quadrant: when it would be, the
                // panel stops above it and the rest scrolls (the wheel over the panel, or drag its bar)
                var helpStyle = new GUIStyle(small) { wordWrap = true };
                float leftTop = (ctl.pictures ? ctl.panelTop : ctl.quadrant.y) - 6;
                float th = helpStyle.CalcHeight(new GUIContent(helpText), 425);
                float ph = Mathf.Clamp(leftTop - hy, 80f, th + 12);
                Rect help = new Rect(10, hy, 440, ph);
                GUI.Box(help, GUIContent.none);
                guiRects.Add(help);
                if (th + 12 <= ph) GUI.Label(new Rect(20, hy + 6, 425, th), helpText, helpStyle);
                else
                {
                    float iw = 425 - 18;   // room for the scroll bar
                    th = helpStyle.CalcHeight(new GUIContent(helpText), iw);
                    helpScroll = GUI.BeginScrollView(new Rect(20, hy + 6, 425, ph - 12), helpScroll, new Rect(0, 0, iw, th), false, true);
                    GUI.Label(new Rect(0, 0, iw, th), helpText, helpStyle);
                    GUI.EndScrollView();
                }
            }
            if (planning) SightGUI(sw, sh);   // after the help, so the glass sits on top of it
        }

        /// <summary>
        /// Planning: the engine switches beside the throttle (left), and the gun MFD (right) with the flare and specials
        /// switches over it. The timeline itself opens above the stick.
        /// </summary>
        /// <summary>
        /// While the turn plays the throttle quadrant, the MFD and the weapon switches stay in the dash, looking but not taking clicks: the
        /// funnel follows the turn's clock, so it shows the rounds as they fly.
        /// </summary>
        void PlaybackPanel()
        {
            float keepT = sightT;
            controlsLocked = true;
            sightT = Mathf.Clamp(playT, 0f, (float)turnSec);
            QuadrantGUI(ctl.quadrant);
            WeaponSwitches(ctl.switchRow.x, ctl.switchRow.y, ctl.switchRow.width);
            GunMfd(ctl.weapons);
            sightT = keepT;
            controlsLocked = false;
            burstRect = Rect.zero;   // no setting the burst mid-turn
        }

        /// <summary>True while the cockpit's buttons are drawn for show only (the turn playing).</summary>
        bool controlsLocked;

        void PlanningPanel(float sw, float sh)
        {
            QuadrantGUI(ctl.quadrant);
            Rect wp = ctl.weapons;

            // the weapon switches (flares, the specials) and the gun MFD; the timeline opens over the stick
            float mw = Mathf.Min(640, sw - 40);
            if (showMoves || plan.HasMarks) MovesPanel((sw - mw) / 2, ctl.stickTop - 196 - 4, mw);
            else movesRect = Rect.zero;
            WeaponSwitches(ctl.switchRow.x, ctl.switchRow.y, ctl.switchRow.width);
            GunMfd(wp);
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
            if (Jet)   // the burner; a piston fighter has no WEP switch (full throttle is WEP)
            {
                bool wep = GUI.Toggle(new Rect(rx, y + 26, 120, 22), st.wep, marks && here.wep == null ? $" {WepName} (carried)" : $" {WepName}");
                if (wep != st.wep) EditPoint(k => { k.wep = wep; return k; });
                if (marks && here.wep != null && GUI.Button(new Rect(rx + 124, y + 26, 22, 22), "x")) EditPoint(k => { k.wep = null; return k; });
            }
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
