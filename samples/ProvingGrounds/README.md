# ProvingGrounds

A first-person shooter playground, as an **engine** project (level 3): a training facility with a shooting range,
a reflex test, a physics yard full of crates and explosives, a movement course and a field of drones, played with
a pulse rifle and a grenade launcher. Like the other samples it ships no binary assets: the level is built from
primitive shapes and hand-written materials, the weapons are modeled from cubes and cylinders, every sound is
synthesized at startup, and every icon is drawn from signed distances.

```bash
dotnet run --project tools/Spot.Cli -- run --project samples/ProvingGrounds            # cook and run
dotnet run --project tools/Spot.Cli -- build windows --project samples/ProvingGrounds  # publish a Windows build
```

Or open `ProvingGrounds.sptproj` in the editor and press Play: click the viewport to deploy. In the editor, `Esc`
hands the mouse back to the editor, so pause the game with `P`.

## Controls

| Input | Action |
|---|---|
| W A S D | Move (hold Shift to walk) |
| Space / Ctrl | Jump / crouch |
| Mouse | Look |
| Left / right mouse button | Fire / aim down sights |
| R | Reload |
| 1, 2, Q, mouse wheel | Switch weapon |
| T | Slow motion |
| Backspace | Reset the playground: every prop, target, crate and drone back where it started |
| V | Fly (the character controller's noclip) |
| Tab | Session stats (hold) |
| F1 | Controls sheet |
| Esc, P | Pause menu: mouse sensitivity, field of view, volume, wind, head bob, bloom, frame rate |

## The playground

| Area | What to do | Engine features at work |
|---|---|---|
| **The Hub** | Spawn, pick a direction | octagonal platform, reflecting pools (the water material), world-space text |
| **Shooting Range** | Pop-up bullseyes at 10–40 m and two sliding targets: scored by ring and multiplied by distance | filtered raycasts, child colliders on a tweened hinge, kinematic motion |
| **Reflex Test** | Shoot the red button, then every orb as it appears for 30 s; fast hits score more, a miss breaks the combo | entity references (the orb wall), entities spawned with colliders, coroutines and tweens |
| **Physics Yard** | Crate walls and pyramids, a domino run, a ball pen, a boulder, explosive crates that chain | Bepu rigid bodies, impulses at a point (shots spin crates), collision callbacks, debris on its own layer |
| **Movement Course** | Steps and gap jumps, a jump pad onto a ledge, a crouch tunnel, a lift to a lookout, a strafe-jump speed trap | the character controller (air-strafing, crouching), trigger volumes, a kinematic lift that carries you |
| **Drone Field** | Five drones fly patterns and track you; three hits drop one, which tumbles down and blows up | kinematic bodies turned dynamic at runtime, looping positional audio, point lights |

Grenades from the launcher are physical: they arc under gravity and go off on contact, pushing every body in reach.
Fire one at your feet for a rocket jump.

## How it is built

| Engine feature | Where |
|---|---|
| Character controller (Quake-style movement) | the Player entity; `Player` adds footsteps, landings, camera shake and strafe roll |
| Hitscan with `Scene.Raycast(..., layerMask)` | `WeaponController.Hitscan`: the ray starts inside the player's capsule and leaves its layer out |
| Impulses (`PhysicsBody3D.AddImpulseAtPosition`) | bullets push props where they land; `Explosions.Detonate` blasts every body in reach |
| Collision layers (`PhysicsSettings.SetLayerCollision`) | `Playground`: grenades ignore the player and each other, debris ignores the player |
| Collision and trigger callbacks | `Grenade`, `Drone`, `PhysicsProp` (impact sounds); `JumpPad`, `Zone`, `SpeedTrap` |
| Kinematic bodies | `Elevator` (a lift that carries you), `Drone` (until shot down) |
| Custom render passes with `BillboardBatch` | `Effects`: bullet holes after the opaque pass; tracers, velocity-stretched sparks, muzzle flashes and shockwaves inside the HDR capture, so they bloom |
| Particle systems | `Effects` (pooled fire, smoke and dust emitters), the jump pad and spawn pad |
| Point lights | muzzle flashes and explosions (`Effects`), drones, the range, the tunnel |
| First-person weapon models from primitives | `Viewmodel`: two guns as child entities of the camera, with sway, bob, recoil, reload, swap and wall-retract motion |
| Procedural audio (`new AudioClip(pcm, ...)`) | `Sfx`: gunshots, explosions, hit ticks, a bell, reload clicks, footsteps, wind and drone hum, all synthesized |
| World-space text | score popups (`WorldPopups`), signs and labels in the scene |
| A UI document | `Assets/UI/Hud.sptui`: the corner brand and the frame-rate readout, wired by widget name |
| A custom widget | `HudOverlay`: crosshair with live spread, hit marker, compass with landmarks, score feed, zone banners, weapon and speed panels, the challenge HUD, stats board, controls sheet, start screen |
| Engine widgets restyled by subclassing | `HudKit`: menu buttons, sliders and switches on a frosted panel; laid out by `GameHud` into the pause menu |
| Input actions bound from code | `Playground.BindDefaults`, so they work in the editor's play mode and in a build |
| Time scale | the pause menu freezes the world (`Time.TimeScale = 0`); `T` toggles slow motion |

| Path | What it is |
|---|---|
| `ProvingGrounds.sptproj` | project config: name, start scene, asset folder |
| `Program.cs` | entry point: creates the application and runs it |
| `Assets/Scenes/ProvingGrounds.sptscene` | the level: geometry, props, targets, drones, lights, the player and the game's entities |
| `Assets/Materials/` | hand-written materials: tinted prototyping grids, metals, neon emissives, water |
| `Assets/UI/Hud.sptui` | the HUD's UI document |
| `Assets/Scripts/` | the game: weapons, effects, targets, drones, crates, the challenge, the HUD and the synthesizer |

`ProvingGrounds.csproj` and `ProvingGrounds.sln` are regenerated from the `.sptproj` by `spot generate`,
`spot run`, `spot build` and the editor, so they are not committed.
