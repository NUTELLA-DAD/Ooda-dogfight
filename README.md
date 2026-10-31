# OODA Dogfight

An open-world WW2 dogfighting game in Unity: arcade-paced like a space-shooter, but free-roaming instead of on rails. The flight model is the OODA tool's point-mass model (`dads-debreif`, `src/core/point-mass.js`), ported to C#.

## Milestone 1: fly the P-51 (now the sandbox)

- A P-51D with a spinning propeller, a clear canopy, and the tiger pilot seated inside (Humanoid rig) who looks into the turn.
- The OODA flight model: G and roll from the stick, energy from thrust minus drag, the stall line and the G limits, smoothed G onset and roll. No assists: let go in a bank and it stays banked.
- Chase and cockpit cameras, a tuning HUD, and a test island with sea, hills, clouds and towers.

## The game: turn-based dogfight (Mustang against Spitfire)

Each turn lasts 3 seconds, and both pilots plan at the same time. You drag a flight-path arrow to say where the nose should point when the turn ends, inside a white ring that shows how far the aircraft can turn in 3 s. Past the ring, out to a red ring, you overpull: the stick goes beyond the stall line, the turn mushes, and held long enough it departs into a spin. The arrow turns red and the screen says what will happen; the next turn's arrow flies the spin recovery. The aircraft flies the best roll and pull to get there, with the same flight model as free flight. A holographic copy of your aircraft shows where the turn leaves you, at the end of the blue ghost path. Orange tracers show where your rounds will go: they arc and take time to arrive, so you must lead the target. A red line shows the Spitfire if it keeps doing what it's doing. Then press **Fly turn**: the AI plans without seeing your plan, both aircraft fly, and the turn plays back. Shoot it down to win; get shot down or hit the ground and you lose.

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

The turn engine is plain C# in `Assets/OODA/Scripts/Turn/Core/`: the autopilot that flies the arrow (`AutoPilot.cs`), the reach ring (`Envelope.cs`), guns and hits (`Combat.cs`, `Battle.cs`), the AI (`EnemyAi.cs`) and the start (`Scenarios.cs`). Damage is simple hit points for now; each hit already records where it struck, for hit locations later. The Spitfire IX's numbers and both aircraft's gun numbers are estimates from public figures, to tune by feel.

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
