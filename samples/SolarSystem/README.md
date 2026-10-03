# SolarSystem

A showcase **engine** project (level 3): the Sun and its eight planets orbiting in real time, with the Moon,
Saturn's and Uranus' rings and an asteroid belt. Hover a body to see an info card with its mass, size, gravity,
day and year, distance, temperature and moons; click it to fly there. Like `HelloEngine`, it ships no binary
assets: every surface is a procedural shader and every UI shape is generated at startup.

```bash
dotnet run --project tools/Spot.Cli -- run --project samples/SolarSystem            # cook and run
dotnet run --project tools/Spot.Cli -- build windows --project samples/SolarSystem  # publish (also linux, browser)
```

Or open `SolarSystem.sptproj` in the editor: every body is an entity whose `CelestialBody` script holds its orbit,
look and facts, editable in the inspector.

## Controls

The interface stays out of the way: a title, the cards, and four icons in the bottom-right corner — pause,
back to the overview, guided tour, and settings, each with a tooltip naming its key. The settings pop-up holds
the time speed and switches for orbits, labels, the asteroid belt, bloom and the idle tour.

| Input | Action |
|---|---|
| Drag (either button), arrows / WASD | Orbit the camera |
| Scroll, Q / E | Zoom |
| Hover | Show a body's card |
| Click | Focus a body and pin its card (click empty space to unpin) |
| 1-8, 0, M | Focus a planet, the Sun, the Moon |
| Tab / Shift+Tab | Next / previous body |
| Esc, Backspace | Back to the overview |
| Space | Pause or resume time |
| `,` / `.` | Slower / faster (¼ day to 1 year per second) |
| T | Guided tour (it also starts by itself after 90 s without input) |
| O / L | Show or hide orbits / labels |
| H | Hide the interface |
| F1 | All shortcuts |

## What it shows

| Engine feature | Where |
|---|---|
| Custom render passes (`Scene.AddRenderPass`) with your own GLSL, inside the HDR frame | `SpaceRenderer`, `SpaceShaders`: starfield and Milky Way, planet surfaces, the Sun, atmospheres, corona, rings |
| `BillboardBatch` from a render pass | `SpaceRenderer.DrawOrbits`: the orbit trails |
| Lights driving custom shaders | the Sun's point light colors the planets; the dim directional light is the ambient fill |
| The lit renderer, GPU instancing | `AsteroidBelt`: 1,400 Mesh Renderer entities drawn in a handful of instanced calls, lit by the Sun |
| Post-processing | the Post Processing entity: ACES, bloom on the Sun and corona, vignette, FXAA |
| A UI document | `Assets/UI/Hud.sptui`: the title |
| Engine widgets restyled by subclassing | `HudWidgets.cs`: icon buttons with tooltips (`Button`), switches (`Toggle`), a stepped slider (`Slider`), a frosted panel that fades as a whole (`Panel`); laid out by `HudControls` |
| A custom widget | `SolarOverlay`: labels, the hover reticle, the info card, the shortcut sheet |
| Procedural UI art | `UITextures`: the icons, drawn from signed distances, and the rounded panels, all generated at startup |
| Scripts working together | `Simulation` (clock), `CelestialBody` (data), `OrbitCamera`, `SolarHud` |

The positions follow the real date: each planet starts at its mean longitude for today and moves at its real
period, on a circular orbit. Sizes and distances are compressed to fit one screen (distances grow with the
square root of the real ones), and spins are slowed down so fast rotators don't blur.

| Path | What it is |
|---|---|
| `SolarSystem.sptproj` | project config: name, start scene, asset folder |
| `Program.cs` | entry point: creates the application and runs it |
| `Assets/Scenes/SolarSystem.sptscene` | the scene: every body and its data, the camera, the lights |
| `Assets/UI/Hud.sptui` | the title, as a UI document |
| `Assets/Scripts/` | the scripts, the shaders, and the HUD's widgets |

`SolarSystem.csproj` and `SolarSystem.sln` are regenerated from the `.sptproj` by `spot generate`, `spot run`,
`spot build` and the editor, so they are not committed.
