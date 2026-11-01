# OODA Dogfight

An open-world WW2 dogfighting game in Unity: arcade-paced like a space-shooter, but free-roaming instead of on rails. The flight model is the OODA tool's point-mass model (`dads-debreif`, `src/core/point-mass.js`), ported to C#.

## Milestone 1: fly the P-51 (now the sandbox)

- A P-51D with a spinning propeller, a clear canopy, and the tiger pilot seated inside (Humanoid rig) who looks into the turn.
- The OODA flight model: G and roll from the stick, energy from thrust minus drag, the stall line and the G limits, smoothed G onset and roll. No assists: let go in a bank and it stays banked.
- Chase and cockpit cameras, a tuning HUD, and a test island with sea, hills, clouds and towers.

## The game: turn-based dogfight (Mustang against Spitfire)

Each turn lasts 3 seconds, and both pilots plan at the same time. You drag a flight-path arrow to say where the nose should point when the turn ends, inside a white ring that shows how far the aircraft can turn in 3 s. Past it a band shades from white into red: in it you overpull: the stick goes beyond the stall line, the turn mushes, and held long enough it departs into a spin. The arrow turns red and the screen says what will happen; the next turn's arrow flies the spin recovery. The aircraft flies the best roll and pull to get there, with the same flight model as free flight. A holographic copy of your aircraft shows where the turn leaves you, at the end of the blue ghost path, a shaded band that tilts with your bank. Orange tracers show where your rounds will go: they arc and take time to arrive, so you must lead the target. A tracer turns red when it hits (if the enemy holds) and yellow when it passes close; a red band on the burst timeline marks the hit window, the moments when rounds fired would connect. The readout shows HITS in red, and how many of four enemy moves (hold, break left, break right, pull up) the burst still hits; the target's ghost glows red. The screen warns, and the path turns red, if the turn flies you into the ground or an obstacle. A red line shows the Spitfire if it keeps doing what it's doing. Then press **Fly turn**: the AI plans without seeing your plan, both aircraft fly, and the turn plays back. Shoot it down to win; get shot down or hit the ground and you lose.

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

### The mountains

The big peaks are copies of the snowy mountain model (`Art/Mountain`): 42 of them in three sizes (roughly 2.5 to 11 km across, up to about 4 km high), each turned, stretched a little each way, some mirrored, and overlapping into massifs. `AlpineMountains.cs` places them from a fixed seed between the valleys and raises the terrain to just under each one, so the ground check, the AI, the clouds and the trees all treat them as ground. Nothing of a mountain may stand above the ground within 150 m of a valley's walls, 350 m of the railway or round the basin, so the main valley, the Valley Run, the gorges and the rail valley fly as before; the noise peaks are squashed above 1.7 km so the models stand over them. The 50 MB source FBX isn't committed: `Mountain_Near.obj` (52k triangles) and `Mountain_Far.obj` (6k) are made from it with Unity's mesh LOD generator the first time the world is built, if they are missing.

### Things to fly round, and the Valley Run

The valley has obstacles in the air as well as the rock: 10 to 16 grey barrage balloons 400 to 900 m over the basin and the main valley, each on a steel tether to a winch on the ground; two or three cable-car spans wall to wall across the narrow valley, two ropes with a car in the middle and a lattice pylon and station at each end; and four to six red and white lattice radio masts, 120 to 200 m tall, on the crests beside the valley, each with three guy wires. All of it is solid (the Obstacles layer): fly into a balloon, a tether, a rope or a guy and you crash, and rounds stop on them. The wires are thin to look at but 4 m thick to the battle. `AlpineWorld` places them from a fixed seed, off the bombers' route: nothing within 150 m of it unless it is at least 120 m under it (the cable cars hang about 135 to 165 m under the bombers). While planning, the wires within 3 km show as thin cyan lines, the balloons as rings, and each flak gun's reach as a faint red dome; the scene keeps their places in a `HazardMarkers` component. Run **OODA > Build Test World** again, then **Build Turn Battle**, to get them.

**Valley Run** (the fourth mission on the title screen, or M in the battle): you and a P-51 wingman low in the main valley, about 250 m over the floor among the ropes and the balloons' tethers, chasing two Bf 109s 1.8 km ahead up the valley, with the flak on the floor. Shoot both down to win. The AI pilots plan without seeing the obstacles, so they can fly into the wires too.

The turn engine is plain C# in `Assets/OODA/Scripts/Turn/Core/`: the autopilot that flies the arrow (`AutoPilot.cs`), the reach ring (`Envelope.cs`), guns and hits (`Combat.cs`, `Battle.cs`), the AI (`EnemyAi.cs`) and the start (`Scenarios.cs`). Damage is simple hit points for now; each hit already records where it struck, for hit locations later. The Spitfire IX's numbers and both aircraft's gun numbers are estimates from public figures, to tune by feel.

## Cockpits, pilots and the art pass

**Cockpits.** The P-51, Spitfire and Bf 109 each have a cartoon cockpit in their FBX, under an empty called `Pit`: a painted tub with ribs, a padded leather coaming round a clean-cut opening, an instrument panel (airspeed, horizon, altimeter, climb, compass, RPM and small gauges), glare shield and gunsight, seat and armour plate, rudder pedals, a throttle quadrant with a trim wheel, a switch box, and the control stick (a spade grip in the Spitfire). Everything named `Pit_` uses one atlas, `Art/Cockpit/Cockpit_Atlas.png`. The stick, the throttle lever, the four needles and the horizon move.

**Pilots.** The tiger and the wolf share one rig and the same clips: **Fly** (the base loop: right paw on the stick, left on the throttle), **Instruments** (reaches to the panel, taps a gauge, twists a knob), **ShakeFist**, **CheckSix** (looks back over each shoulder), **Cheer** (fist pumps) and **Flinch**. `PilotControls` (on each pilot) keeps the paws on the stick and throttle with hand IK, leans the stick with the G and the roll, moves the throttle with the engine, drives the needles and the horizon, and plays the gestures: a fist at an enemy the pilot is watching once it is within 450 m, the instruments or a check of six now and then, and in the turn battle a flinch when hit, a fist when his rounds hit and a cheer for a kill. Call `Play(PilotControls.Gesture.X)` for others. Run **Build Test World** and **Build Turn Battle** again to get all of this.

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
