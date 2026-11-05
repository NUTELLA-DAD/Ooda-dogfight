# OODA Dogfight

An open-world WW2 dogfighting game in Unity: arcade-paced like a space-shooter, but free-roaming instead of on rails. The flight model is the OODA tool's point-mass model (`dads-debreif`, `src/core/point-mass.js`), ported to C#.

## Milestone 1: fly the P-51 (now the sandbox)

- A P-51D with a spinning propeller, a clear canopy, and the tiger pilot seated inside (Humanoid rig) who looks into the turn.
- The OODA flight model: G and roll from the stick, energy from thrust minus drag, the stall line and the G limits, smoothed G onset and roll. No assists: let go in a bank and it stays banked.
- Chase and cockpit cameras, a tuning HUD, and a test island with sea, hills, clouds and towers.

## The game: turn-based dogfight (Mustang against Spitfire)

Each turn lasts 3 seconds, and both pilots plan at the same time. You drag a flight-path arrow to say where the nose should point when the turn ends, inside a white ring that shows how far the aircraft can turn in 3 s. Past it a band shades from white into red: in it you overpull: the stick goes beyond the stall line, the turn mushes, and held long enough it departs into a spin. The arrow turns red and the screen says what will happen; the next turn's arrow flies the spin recovery. The aircraft flies the best roll and pull to get there, with the same flight model as free flight. A holographic copy of your aircraft shows where the turn leaves you, at the end of the blue ghost path, a shaded band that tilts with your bank. Orange tracers show where your rounds will go: they arc and take time to arrive, so you must lead the target. A tracer turns red when it hits (if the enemy holds) and yellow when it passes close; a red band on the burst timeline marks the hit window, the moments when rounds fired would connect. The readout shows HITS in red, and how many of four enemy moves (hold, break left, break right, pull up) the burst still hits; the target's ghost glows red. The screen warns, and the path turns red, if the turn flies you into the ground or an obstacle. A red line shows the Spitfire if it keeps doing what it's doing. Then press **Fly turn**: the AI plans without seeing your plan, both aircraft fly, and the turn plays back. Shoot it down to win; get shot down or hit the ground and you lose.

The aircraft turn as the real ones did. The G you can pull is capped by the stall line (slow means mushy) and by the G limit, so each type has a corner speed where it turns quickest and tightest; pulling hard bleeds speed through induced drag, and what thrust is left decides the turn you can hold. Each fighter is fitted to wartime and test figures at sea level with WEP: the Spitfire IX circles in about 18.5 s, the Bf 109 G-6 in 20.5 s, the P-51D and the P-38L in 22 s, and the F-4 holds about 14 deg/s; top speeds and climbs are fitted too (`Tools~/FlightCoreCheck` prints the table). The AI flies to its type's strengths against yours: it keeps its own corner speed, turns when it out-turns you, and goes up and down (speed and height in hand) when it doesn't. Its ghosts are spread across different kinds of move (a climb, a dive, a turn or hard turn each way, straight on), each with the real chance it flies that kind; the unlikely ones are faint and may read under 1%.

Build it with **OODA > Build Turn Battle** (after Build Test World), open `Assets/OODA/Scenes/TurnBattle.unity`, and press Play. The free-flight `TestWorld` stays as the sandbox.

| | Mouse and touch | Keyboard | Gamepad |
|---|---|---|---|
| Move the arrow | Drag the yellow tip | WASD or the arrow keys (S and down pull the arrow up, like the stick) | Left stick (back pulls up) |
| Straight ahead | | X | Left bumper |
| Throttle, WEP | Slider, WEP box | Shift / Ctrl, Space | Triggers |
| Gun burst | Drag on the timeline (a tap clears it) | B steps through bursts, F fires where hits are predicted | X, right bumper |
| Fly the turn | Fly turn button | Enter | A |
| Replay, restart | Buttons | R, Backspace | B, Start |
| Look around, zoom | Drag away from the tip, right-drag, wheel | + / - | Right stick, d-pad |
| Camera (player, Spitfire, both, cockpit) | View button | V | Right stick press |
| Enemy projection on or off | Projection button | P | Y |

### The gun funnel

Hover over the burst bar while planning and the chase camera eases round to sit straight behind your ghost at that moment of the turn, lined up with its guns. A gun cross marks the middle and the gun funnel shows your rounds in the air then (as if the trigger were held all turn) as they look from the seat: two thick green lines a wingspan apart, wide close in and narrowing with range, bending away from your turn, with a rung every 1,000 ft and a horizon line. The enemy's likely moves show as red ghost models where each would be at that moment, the likeliest solid and the others fainter by their chance, each with its %; one turns bright red with ON TARGET when your rounds are on it then. Fit its wings across the funnel and set your burst there (F still fires at the likeliest move's hits). Your blue ghost sits low in front of you. The view holds a moment after the mouse leaves the bar, then eases back; there is no funnel in the normal chase view. **K** steps the funnel through hover (the default), lined up all the time, and off. The old lead ring on the target's path is now a toggle, **L**, off at first. It is all in `TurnBattle.Funnel.cs`.

With only one fighter of yours, it is always yours to fly: it starts each turn on your control (straight ahead), never on its AI pilot's plan.

### Dogfight: up to 4 v 4, the side controls, the rocket and the speed brake

Pick **Dogfight** on the title screen and press Play: the **squadron briefing** opens, a 3D airfield at the south end of the town's runway (`UI/BriefingStage.cs`). Your flight is parked on the left and the bandits on the right, lead nearest the middle, each plane on its wheels with its pilot standing in front of it (the seated pilot posed standing through its Humanoid muscles), and a gold ring round the one picked. Click a pilot or a name plate, or use the left and right arrows, to pick one; **Change Pilot** (P, pad X; right-click or Shift goes back) steps him through the type's usual pilot, the tiger, the wolf, the wizard, the skeleton and the bear (the F-4 keeps its crew), and **Change Aircraft** (C, pad Y) steps his plane through every type: P-51D Mustang, P-38L Lightning, Spitfire IX, Bf 109 G-6 or F-4 Phantom, any type on either side. The - and + under each banner (or - and +, the bumpers, for the picked pilot's side) set how many planes each side has, 1 to 4. **Mission** (M, pad Select) opens the start: Random (a new one each time you fly again), Head-on, Side by side, On their six, Them on your six, Crossing (them from your right at right angles), High perch (you 2,500 ft above and behind them, out to one side) or Bounced (the other way round); the range (close, medium, far: 0.6, 1 and 1.7 times the usual gaps), the height (low, about 400 m over the basin; medium; high, 4,400 ft higher) and the speed (220, 300 or 380 KIAS), with a map of where everyone starts, from above. **Ready** (Enter, pad A) flies; Back (Esc, pad B) closes the Mission panel or goes back to the title. What you pick is kept for next time (`UI/DogfightSetupScreen.cs`; the setup itself is `Turn/Core/DogfightSetup.cs`, which `Scenarios.Dogfight` builds the battle from). **OODA > Build Title** gives the title the planes to park (the turn battle's prefabs, `Prefabs/<type>.prefab` for a new type). On the title, the up and down arrows still change your side's size and Page Up and Page Down theirs, and Q starts a quick 1 v 1, P-51 against a Bf 109.

The **P-38L Lightning** is the Hi3D twin-engine model (`Art/P38`), cut from 300k triangles to about 125k by `Tools~/ArtPipeline/p38.py`, which also splits off its canopy and its two propellers (each spinning about its own hub, outward at the top) and places the seat; `build_cockpit.py P38` builds its cockpit, and the tiger flies it (any of the pilots can). Its numbers are estimates from public figures: two turbocharged Allisons, so it is a little slower than the Mustang low down and the fastest of the piston fighters above 20,000 ft, it climbs well but rolls and turns heavily; its 20 mm and four .50s all sit in the nose, so its cone is tight at any range, and two engines make it a little tougher (and a bigger target). `OODA > Build Turn Battle` makes `Prefabs/P38.prefab`.

Against the AI your guns get the forgiving hit radius whatever you fly (the P-51's numbers always had it); an enemy P-51 doesn't. In the fight the F-4 has its M61 cannon (about 7 s of fire, hard hitting) and 4 missiles, and its WEP box is its afterburner (BURNER), with the flame in playback. It has no pilot yet. For a game over the network the host fixes a random start with `Resolved` and sends `ToBytes()`: the same bytes build the same battle on both machines (`Tools~/NetCheck` checks it). The bot takes `-dogfightcraft Spitfire,Phantom Bf109,Mustang` for the aircraft on each side, and `-setupshots <folder>` on the built game's command line saves three pictures of the briefing (2 v 2, 4 v 3, and the Mission panel) and quits. You fly every one of yours: **Tab** (Shift+Tab back, the roster buttons along the top, or the pad's View button) moves you to your next plane, which becomes the one the stick, the ghost and the lead ring work on. A plane you visit in a turn flies the plan you leave it with, starting from what its AI pilot would do; one you never visit flies itself. Each turn starts back on your lead. When the plane you are in goes down you carry on in another; the fight is lost only when your whole flight is. **N** picks which enemy you are after (red brackets mark it). Under the hood, switching planes is `Battle.Swap(0, k)`: aircraft k becomes aircraft 0 everywhere, taking its route, slot and runway place with it (`TurnBattle.Flight.cs`).

The arrow and its yellow ball are gone: drag the stick in the bottom-right corner (down pulls, sideways rolls into it, past the shaker into the overpull band). WASD and the pad move it as before. **F2** brings the arrow back as a dev view, and it comes back by itself when the window is too narrow for the stick. The OVERPULL, SPINNING, STALL and DEPARTED warnings are a dev view now too (**F3**); the stick shaker and the ghost path tell you.

**Rocket** (1, the d-pad right, or the button in the planning panel): lit at the start of a turn, it shoves for 2.5 s at 1.3 times the weight (about 60 kt more), then needs 30 s, ten normal turns, before it is ready again. **Speed brake** (2, the d-pad left): out for the turn, 0.7 G of drag at 300 KIAS, growing with the square of the speed, so it sheds about 40 kt a turn when fast and much less when slow. The AI pilots use both: the rocket when it leaves them clearly better placed (or whenever they're slow), the brake with the throttle chopped to make you overshoot. The numbers are `rocketG`, `rocketSec`, `rocketCooldownSec` and `brakeG` in `AircraftNumbers`. A flame shows behind a plane on its rocket during playback, and grey panels when its brake is out.

### IR view and flares

**I** (any time, or the IR button over the planning panel) turns on the IR view: the screen as a heat-seeker's thermal camera sees it, the world a dim grey with the sky cold and dark (`Fx/IrView.shader`, which reads the camera's opaque texture, through `Resources/IrView.mat`), and every heat source a white-hot glow as bright as the seeker sees it from where the camera is. It uses the same numbers the missile locks with (`Heat` in `Turn/Core/Combat.cs`): each engine by its throttle (`heatIdle`, `heatFull` in `CombatNumbers`), WEP or the afterburner (`heatWep`: the F-4's burner more than doubles it) and the rocket (+2), all of it from astern but only a quarter from ahead; a missile's motor; and the flares, the hottest things in the sky. The seeker's lock range grows with the square root of the heat it sees: 9,000 ft on a piston fighter at full throttle from behind, about 6,400 ft at idle, up to 13,500 ft on a jet in afterburner or a plane on its rocket. The views and the replay show it too.

**Flares**: every fighter carries 6 (the F-4 8, the B-17 none). They go by themselves: when a heat-seeker tracking you closes inside 3,000 ft, a pair goes out behind you, 0.35 s apart, and each has one chance to pull the seeker off onto itself: 0.9 x 1.6 / (1.6 + the heat it sees on you). A pair saves a piston fighter at full throttle about 80% of the time, a jet in afterburner about 35%: throttle back and stay out of the burner when one is coming. **3** (or the Flares button) holds them for the turn. A red MISSILE ON YOU warning says when one is tracking you. The AI pilots let theirs go the same way. A decoyed missile chases the flare until it burns out (3.5 s), then flies on unguided. `Tools~/HeatCheck` (`dotnet run`) checks the lock ranges, the flares' odds, and that flares survive a clone, a subset and a snapshot.

### Arcade specials: the balloon mine, the heat blade and stealth

With **Arcade** on (the switch under the VS badge in the squadron briefing, or **G**; on by default, and sent to your friend with the rest of the setup), every fighter in a dogfight carries four specials (a game liberty, like the missiles); with it off, and in the other missions, there are none and their buttons are hidden. They are planned like the rocket for the start of a turn, with the buttons over the IR and flare buttons or the keys. The rules are `Turn/Core/Specials.cs`, the look `Turn/TurnBattle.Specials.cs`; the counts are `balloonMines`, `heatBlades` and `stealths` in `CombatNumbers`, which `Specials.Arm` fills when `DogfightSetup.arcade` is on.

- **Balloon mine** (4; 2 each): a red spiked balloon let go 50 ft behind you. It slows to a hang in about a second and drifts up a little, arms after 0.6 s (its light starts blinking), and goes off on the first enemy that flies within 70 ft of it, for 10 damage (most of a fighter). It hangs there for 15 s, so a tailer who planned to follow you straight flies into it, and anyone can blunder into it later. Your own side flies through yours unharmed. While planning each mine shows with a faint red ring for its fuse, and the one you're about to drop shows where it ends the turn.
- **Heat blade** (5; 1 each): the whole plane glows red hot for the turn (a pulsing red copy of every part of the airframe laid over it, `TurnAircraftView.SetRedHot`) and its guns stay cold. Pass within 45 ft of an enemy and it is cut in half (down at once, a kill for you) while you fly on; two blades meeting both go down, and a friendly collision is still a collision. It adds 4 to your heat from every side, so every heat-seeker locks you from farther (up to 13,500 ft) and flares hardly help: it lights you up in the IR view.
- **Jammer** (7; 1 each): every enemy within 9,000 ft is jammed for the next two planning phases. A jammed AI pilot plans against a scrambled picture: everyone on your side moved up to 900 ft and turned up to 70 degrees (`Jam.Scramble`, the same dice on every machine), so it aims and breaks the wrong way. Jammed yourself, the enemies' paths and ghosts jump to the wrong place and flicker, their predicted moves, the funnel and their projected tracers are gone, and a purple JAMMED warning shows: go by the planes themselves.
- **Stealth** (6; 1 each): two normal turns (6 s) unseen. The other side's AI pilots, flak and B-17 gunners don't see you. A stealthy enemy vanishes from your screen with its path, ghosts, projection, funnel and target bracket; only its heat still shows in the IR view, and **a heat-seeker still locks it**. Yours disappears too, leaving only a dotted cyan outline of it (marching dashes round its shape from above and its fin) so you can still see where you are; in the replay every stealthy plane shows that way. Firing your guns or a missile shows you again at once.

The AI pilots use them: a mine when someone is on their tail with guns on them, the jammer when they're out of mines, stealth when they are hurt and out of both (and not firing), and the blade when one of their tries would fly through their target. `Tools~/SpecialsCheck` (`dotnet run`) checks each of them against the battle core, the AI using them, and that they survive a clone, a subset, a swap and a snapshot.

### Rudder pedals, tail slides and hammerheads

There is no stunt button: these come out of the flight model. The **rudder pedals** are the bar above the corner stick (drag it, or hold **Z**/**C**; **X** centres them with the stick). At speed the rudder only skids the nose a little. Pull straight up and let the speed bleed off: below about 120 ft/s with the nose over 50 degrees up the wing has nothing left and the plane flies on its prop and its tail (`FlightModel.StepVertical`). Then:

- **Tail slide**: hold the stick and pedals centred. The plane stops, slides backwards, and the tail weathervanes the nose over and down; at 150 ft/s with the nose along the flight path it flies again.
- **Hammerhead**: kick a pedal near the top. The prop blast over the rudder swings the nose sideways over the top and straight down, with no slide back. The power matters: a prop's slipstream gives the rudder bite even at a standstill, a jet's doesn't (`propWash`), so the Phantom swings over slower.
- **Barrel roll**: the stick pulled to a diagonal with the pedal the same way, or bank points on the timeline while pulling.

`Tools~/VerticalCheck` (`dotnet run`) flies each of them through the battle core.

### The island

The island is 20 km across in three zones. The north is snowy peaks: about 70 copies of the snowy mountain model (`Art/Mountain`), each turned, stretched and sometimes mirrored and overlapping into massifs, with canyons between them. The middle is pine valleys and the chalet lake. The south is farmland (a patchwork of wheat, pasture and meadow with hedgerows), the factory town and the coast. Copies of the grass hill model (`Art/Foothill`) are the foothills and the farmland's swells. The ground under each model is raised to just under it, so the ground check, the AI, the clouds and the trees all treat it as ground.

- **The main valley** runs up the middle from the coast: up the farmland, along the chalet lake, past the village and its church, and up the narrow pass between the peaks. A turquoise river runs down it to the sea, with four little stone arch bridges. The escort route, the Squadron, the Valley Run and the flak follow it.
- **The railway** (27 km) climbs from a station by the town, through tunnels and over seven bridges, the stone viaduct 134 m over the main valley above the village, and up to the High Pass at 800 m. A steam train (an engine, its tender and three coaches) runs it back and forth. It is scenery only: no collider, so the battle never sees it.
- **Four dogfight arenas**, picked in the dogfight's briefing (or Random): **Peak Maze**, a bowl among the snowy peaks with canyons out of it; **High Pass**, the saddle the railway climbs to; **Chalet Lake**, up the lake past the village; **Town & Farmland**, open fields, the town and the coast. The arena only sets the start (`Turn/Core/Arena.cs`): the whole island is open once the fight begins. A start that would put a flight under 500 ft over the ground in its first turn and a half lifts that flight (only that one) clear.

The village is 26 copies of the Hi3D alpine chalet (`Art/Chalet`) round a cobbled square with a fountain, with a church (white nave, slate roof, stone tower with an open belfry and an eight-sided spire); 70 more chalets are farmhouses on the slopes and the farmland. The trees are the round leafy tree low down, the tall tree above it and the pine highest, 12,000 of them. The clouds are banks of three to seven copies of the cloud model, most in a soft haze (`Art/Cloud/Haze.shader`).

The layout (`Editor/AlpineLayout.cs`, `AlpineMountains.cs`, `AlpineDress.cs`) is plain code with no Unity objects, so `Tools~/WorldPreview` runs it outside Unity (`dotnet run -- <dir>`): it checks the railway's grades, bridges and tunnels, the bombers' corridor, every dogfight start in each arena, and the dressing, and draws a map and a few views. Run **OODA > Build Test World**, then **Build Turn Battle**, to get the new island.

The source FBXs (50 to 100 MB each) aren't committed: near and far OBJs of each are made from them with Unity's mesh LOD generator the first time the world is built, if they are missing, and those are committed.

### Things to fly round, and the Valley Run

The valley has obstacles in the air as well as the rock: 10 to 16 grey barrage balloons 400 to 900 m over the basin and the main valley, each on a steel tether to a winch on the ground; two or three cable-car spans wall to wall across the narrow valley, two ropes with a car in the middle and a lattice pylon and station at each end; and four to six red and white lattice radio masts, 120 to 200 m tall, on the crests beside the valley, each with three guy wires. All of it is solid (the Obstacles layer): fly into a balloon, a tether, a rope or a guy and you crash, and rounds stop on them. The wires are thin to look at but 4 m thick to the battle. `AlpineWorld` places them from a fixed seed, off the bombers' route: nothing within 150 m of it unless it is at least 120 m under it (the cable cars hang about 135 to 165 m under the bombers). While planning, the wires within 3 km show as thin cyan lines, the balloons as rings, and each flak gun's reach as a faint red dome; the scene keeps their places in a `HazardMarkers` component. Run **OODA > Build Test World** again, then **Build Turn Battle**, to get them.

**Valley Run** (the fourth mission on the title screen, or M in the battle): you and a P-51 wingman low in the main valley, about 250 m over the floor among the ropes and the balloons' tethers, chasing two Bf 109s 1.8 km ahead up the valley, with the flak on the floor. Shoot both down to win. The AI pilots plan without seeing the obstacles, so they can fly into the wires too.

### Town Strike

**Town Strike** (the fifth mission on the title screen) starts on a map of the island. Click the coast where the B-17s come in, then up to three turn points, each at Low (250 m over the ground beneath it), Medium (1,200 m) or High (2,400 m); 1, 2 and 3 set the selected point's height, Enter flies the plan. The bombers fly that path to the factory in the town in the middle of the island and on past it, climbing at 3 degrees and coming down at 6 at most. The map shades what the three radar sites can see at the selected point's height (the mountains cut holes in it), draws the patrol's loop round the town (not where on it they will be) and the 4 km ring inside which the town always sees you, and colours the path green where it is hidden, red where the radar sees it and black where it flies into the ground.

Until they are seen, 4 Bf 109s fly the loop and the other 4 wait on the runway. A radar site with a clear line to any of ours, the patrol within 4 km with a clear line, or any of ours within 4 km of the factory raises the alarm: the patrol attacks and the runway fighters take off a pair at a time, two turns apart, low and slow off its end. A bomber that passes over the factory drops on it; the mission ends when every bomber still flying has. The rules are `Turn/Core/Strike.cs`, the map and the alarm `Turn/TurnBattle.Strike.cs`, the town, runway and radar sites `Editor/AlpineTown.cs`. OODA > Strike Check flies it headless from three paths and logs what happens; `-strikeshots` on the built game's command line plans a path, saves the map and a turn as BMPs beside the exe, and quits.

The turn engine is plain C# in `Assets/OODA/Scripts/Turn/Core/`: the autopilot that flies the arrow (`AutoPilot.cs`), the reach ring (`Envelope.cs`), guns and hits (`Combat.cs`, `Battle.cs`), the AI (`EnemyAi.cs`) and the start (`Scenarios.cs`). Damage is simple hit points for now; each hit already records where it struck, for hit locations later. The Spitfire IX's numbers and both aircraft's gun numbers are estimates from public figures, to tune by feel.

### The menu and the replay

**Esc** (or the **Menu** button under Help) pauses the battle and opens the menu: Resume, Restart mission, Main menu (back to the title screen) and Quit game. When the fight is over the end screen has **Watch replay** (W), **Fly again** and **Main menu**.

**The replay** flies the whole fight again, turn after turn, with the tracers, the hits, the smoke and the pilots' gestures. Every turn is kept as it is flown, by aircraft in their starting order (`TurnBattle.Replay.cs`), so it follows each plane even after you Tab between yours. Any camera on any aircraft: **V** (or the View button) steps through chase, target, both and cockpit, **C** jumps in and out of the cockpit, **Tab** follows the next plane (Shift+Tab back, the pad's bumpers). **Space** pauses, the left and right arrows step a turn, up and down change the speed (a quarter to four times), the bar along the bottom jumps anywhere in the fight, and **Backspace** (pad B) leaves the replay. Drag or right-drag to look round, the wheel zooms.

### The briefing look

Every screen's text, the title, the planning panels, the buttons, the HUD and the end screens, is green monospaced type (Share Tech Mono) on dark olive panels with a thin green rim, and the big headings are stencil (Stardos Stencil). It all comes from one skin, `UI/BriefingStyle.cs`: each OnGUI calls `BriefingStyle.Use()` first, and any GUIStyle made from `GUI.skin` after that takes the look. The fonts are under `Resources/Fonts` with their SIL Open Font Licenses.

## Online play: a dogfight with a friend on another machine

**O** (or **Online** on the title) opens the online panel. One of you clicks **Host a game**: the panel lists this PC's addresses, Tailscale's 100.x first. The other types that address and clicks **Join** (port 7777 unless you add `:port`). Then the host picks **Co-op** (both of you on your flight against the AI, its planes shared out as evenly as they go, the host taking the extra one) or **Versus** (the host flies your flight, the friend the enemy flight, no easy aim), and **Pick the planes and fly** opens the dogfight briefing; its Fly starts the fight on both machines with the same setup (a random start is fixed by the host first). Over the internet, both install Tailscale and sign in, and the host shares the PC with the friend. Both need the same build: the hello refuses another one.

In the fight you plan your own planes (Tab goes round yours only). **Fly turn** sends your part and waits for your friend's; in co-op your wingman's ghost shows what they are planning as they plan it. A fight ends when a whole side is out, so you may watch your friend carry on after your planes go down. The host starts each new fight; OODA turns and switching missions are off online. If the connection drops, the friend's planes go back to their AI pilots and the fight carries on.

Under it, each turn only the plans cross the network: each player sends the plans, orders and formation shapes for its own planes, the host adds the AI's, flies the turn and sends every plane's plan with a hash of the battle after it; the friend flies the same turn and checks the hash, and takes the host's whole battle (a snapshot of about 1 KB) if they ever differ.

- `Turn/Core/Wire.cs` turns the battle, or any message, into bytes and back by walking their fields (so a new field is carried without listing it), and hashes the battle. Each machine keeps its own aircraft and gun numbers, hazard shapes and ground.
- `Turn/Core/Seating.cs` says who flies which plane and keeps the one order every machine agrees on: an aircraft's wire id is its index at the start, on the host. Each machine keeps its own plane at index 0 as Tab does, and `ResolveInWireOrder` flies the turn in wire order, because the order planes are stepped in decides the order rounds are fired and the gun dispersion's dice.
- `Net/NetLink.cs` is the TCP link (whole messages, a reader thread, a ping, lost after 20 s of silence); `Net/NetSession.cs` the session from the lobby into the battle, the messages, and `Lockstep`, how the host puts the two players' turns together. `UI/TitleScreen.Lobby.cs` is the panel, `Turn/TurnBattle.Net.cs` the fight.

`Tools~/NetCheck` checks it without the game (`dotnet run` there), including turns over real localhost sockets in co-op and versus. The built game tests itself too: `OODA.exe -nethost [-netsides 2 2] [-netversus] -netbot 10` and, in a second copy, `OODA.exe -netjoin 127.0.0.1 -netbot 10` fly ten turns by themselves and write every turn's hash to `OnlineLog-host.txt` and `OnlineLog-friend.txt` beside the dogfight log. **OODA > Build Windows Game** (or `-executeMethod Ooda.EditorTools.GameBuild.Windows -buildPath <exe>`) builds the game.

## Jet Test: the Phantom with an afterburner

A side test, kept apart from the WW2 missions: **Jet Test** in the bottom-right corner of the title (or **J**) puts you in an F-4 Phantom (the Hi3D model cut to about 50k triangles, `Art/Phantom`) in free flight over the Alps, 14,000 ft over the lake at the foot of the main valley at 420 kt. **Space** or pad **A** lights the afterburner (it pushes the throttle to the stop) and puts it out; pulling the throttle back with Ctrl or the left trigger puts it out too. The rest is the sandbox's controls, plus **Esc** back to the title. The HUD shows Mach big, the airspeed in knots and mph, and the best Mach of the flight; a flame and an orange glow come out of the nozzles when the burner is lit, and a vapour cone forms round the jet right at Mach 1 low down.

The numbers are `AircraftNumbers.Phantom()` (the asset `Settings/Phantom.asset`, remade by each build): jet thrust that hardly falls with speed and rises with ram, and a transonic drag rise (`ramPerMach2`, `waveRise` and the rest, zero for the propeller aircraft, which fly exactly as before). It tops out near 630 kt dry and 705 kt (Mach 1.07) in afterburner at sea level, and makes Mach 2 in about seven minutes of afterburner at 36,000 ft. Build it with **OODA > Build Jet Test** (after Build Test World and Build Title); it saves `Scenes/JetTest.unity` and adds it to the build.

## Cockpits, pilots and the art pass

**Cockpits.** The P-51, Spitfire and Bf 109 each have a cartoon cockpit in their FBX, under an empty called `Pit`: a painted tub with ribs, a padded leather coaming round a clean-cut opening, an instrument panel (airspeed, horizon, altimeter, climb, compass, RPM and small gauges), glare shield and gunsight, seat and armour plate, rudder pedals, a throttle quadrant with a trim wheel, a switch box, and the control stick (a spade grip in the Spitfire). Everything named `Pit_` uses one atlas, `Art/Cockpit/Cockpit_Atlas.png`. The stick, the throttle lever, the four needles and the horizon move.

**Pilots.** The tiger and the wolf share one rig and the same clips: **Fly** (the base loop: right paw on the stick, left on the throttle), **Instruments** (reaches to the panel, taps a gauge, twists a knob), **ShakeFist**, **CheckSix** (looks back over each shoulder), **Cheer** (fist pumps) and **Flinch**. `PilotControls` (on each pilot) keeps the paws on the stick and throttle with hand IK, leans the stick with the G and the roll, moves the throttle with the engine, drives the needles and the horizon, and plays the gestures: a fist at an enemy the pilot is watching once it is within 450 m, the instruments or a check of six now and then, and in the turn battle a flinch when hit, a fist when his rounds hit and a cheer for a kill. Call `Play(PilotControls.Gesture.X)` for others. Run **Build Test World** and **Build Turn Battle** again to get all of this.

**The wizard and the skeleton.** Two more pilots on the same rig and clips: the cartoon wizard (black robe, glowing eyes under a wide hat) and the skeleton in a top hat and bow tie, each the Hi3D model cut from 300k to 40k triangles with 4K textures (`Art/WizardPilot`, `Art/SkeletonPilot`). `Tools~/ArtPipeline/rig.py` copies the wolf's armature and his Seated pose onto each T-posed model, moves its hip joint to the wolf's so every seat fits them, sets the bones on the model's own joints and binds it (heat weights for the wizard; the skeleton's loose bones get nearest-bone weights); `pilot_anims.py` then bakes the six clips on it. Every P-51, Spitfire and Bf 109 now carries all the pilots in its seat, all but one hidden (`PilotCast` seats them, `PilotRoster` on the plane shows one). In the dogfight briefing each plane has a pilot button beside its aircraft: a click steps it through Usual (the type's own: the tiger in the P-51, the wolf in the others), Tiger, Wolf, Wizard, Skeleton and Bear. The F-4 keeps its crew. The bot takes `-dogfightpilots Wizard,Usual Skeleton` like `-dogfightcraft`.

**The bear.** A fifth pilot made the same way: the Hi3D bear in a winter flying jacket and a fur ushanka with a red star (`Art/BearPilot`), cut from 300k to 40k triangles with a 4K base map, rigged by `rig.py` (heat weights, the hat and ears pinned to the head) and given the six clips by `pilot_anims.py`. He seats in every P-51, Spitfire and Bf 109 with the others and is the last pick on the pilot button; the briefing's 4 v 3 test shot has him leading your flight.

**The art pass.** Every texture was cleaned (edge-preserving smoothing that keeps the ink lines, seams filled so mipmaps don't pull in dark edges) and is now 4096 px; the P-51's and B-17's ragged invasion stripes are repainted straight from each texel's place on the wing; the cloud is repainted white with a soft grey-blue belly and its pock-marks smoothed; the flak gun is unwrapped and painted. The fighters' meshes lost the torn shards round the cockpit and the dents in their flat skin. The scripts that did it are in `Tools~/ArtPipeline` (Blender 5.2's `bpy` module, run headless); they were run from a scratch folder, so the paths at their tops need pointing at your copies of the `.blend` files. Export aircraft and props to Unity with `unity_export.py`, never Blender's FBX exporter on its own: a straight export leaves the root tilted and every empty pointing up, so the cockpit camera stares at the sky and the pilot is seated on his back. `nose_letters.py` repaints the P-51's MUSTANG bands from a side view of the nose.

## First run

1. Install **Unity 6** (any 6000.x) with Unity Hub.
2. Clone this repo outside OneDrive (OneDrive locks Unity's Library folder), for example `C:\dev\ooda-dogfight`.
3. In Unity Hub, choose **Add > Add project from disk** and pick the folder. If the Hub asks which version, pick your Unity 6.
4. If Unity asks to enable the new Input System backends and restart, say **Yes**.
5. In the menu bar choose **OODA > Build Test World**. It sets up URP, the models' import settings, materials, the pilot's animator, the `P51` prefab and the `TestWorld` scene.
6. Press **Play**.

## Controls

| | Gamepad | Keyboard and mouse |
|---|---|---|
| Pitch and roll | Left stick | W/S (S pulls) and A/D, or the arrow keys |
| Rudder | Bumpers | Q/E |
| Throttle | Right trigger up, left trigger down | Shift up, Ctrl down |
| War emergency power | A (at full throttle) | Space |
| Look | Right stick | Mouse (hold right button in the chase view) |
| Chase / cockpit camera | Y | C |
| Start again | Start | Backspace |
| Show or hide help | Select | H |

## Tuning the flight

The P-51D's numbers are in `Assets/OODA/Settings/P51D.asset` (made by Build Test World). Select it and change them while flying. They are seeded from public figures (wing area, drag, Merlin power, G limits, stall speed, roll rate) and are estimates; `AircraftNumbers.cs` lists the sources.

| Number | What it does |
|---|---|
| `maxG`, `minG` | The G limits the stick asks for at full deflection |
| `stallKias` | 1 G stall speed: the most G the wing gives is (KIAS / stallKias)² |
| `rollPerKtasDps`, `maxRollRateDps` | Full-stick roll rate: grows with true airspeed up to the cap |
| `gOnsetGps` | How fast G builds |
| `thrustK`, `dragA`, `dragB` | Thrust and drag (the OODA tool's fit shape) |
| `wepFactor` | Extra thrust with war emergency power |

## Layout

| Path | What's in it |
|---|---|
| `Assets/OODA/Scripts/Flight/Core/` | The flight core, plain C# with no Unity types: the point mass (`PointMass.cs`), the aircraft's numbers (`AircraftNumbers.cs`), the player's flight with its limits and smoothing (`FlightModel.cs`) |
| `Assets/OODA/Scripts/Flight/` | `AircraftController` flies the GameObject with the core; `PlaneInput`; `PropellerSpin` |
| `Assets/OODA/Scripts/Camera/CameraRig.cs` | Chase and cockpit cameras |
| `Assets/OODA/Scripts/Pilot/PilotHeadLook.cs` | The pilot's look-at IK |
| `Assets/OODA/Scripts/UI/FlightHud.cs` | The readout |
| `Assets/OODA/Editor/TestWorldBuilder.cs` | The OODA > Build Test World menu item |
| `Assets/OODA/Art/` | The cleaned P-51 (48k triangles) and the rigged tiger pilot; the Spitfire IX (106k) and the rigged wolf pilot; 2K textures |
| `Assets/OODA/Scripts/Turn/` | The turn battle: `TurnBattle` (planning, playback, screen), `TurnAircraftView`, `TurnCamera`, `LinePool`; the turn engine in `Core/` |
| `Assets/OODA/Editor/TurnBattleBuilder.cs` | The OODA > Build Turn Battle menu item: the Spitfire prefab and the `TurnBattle` scene |
| `Tools~/FlightCoreCheck/` | Checks the C# core against the OODA tool's JavaScript, and the turn engine (Unity ignores this folder) |

## Checking the port

The C# core flies the same paths as the OODA tool, to the printed digit, for level flight, a loop and two banked turns:

```
cd Tools~/FlightCoreCheck
dotnet run -- <path to dads-debreif>/src/core
```

It also prints both aircraft's top speeds, climb, turn and roll numbers, then checks the turn engine: the reach ring, the autopilot arriving on the arrow, rounds hitting and dropping, and two AI pilots fighting from the head-on start.
