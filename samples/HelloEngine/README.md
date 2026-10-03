# HelloEngine

A minimal Spot **engine** project (level 3): a lit, shadowed scene with a sky and post-processing, physics
cubes raining onto a spinning kinematic block, a particle fountain, a HUD authored as a UI document, and a
ring of glowing dots drawn by a custom render pass with the framework's `BillboardBatch`. It uses no binary
assets — only primitives and generated textures.

```bash
dotnet run --project tools/Spot.Cli -- run --project samples/HelloEngine            # cook and run
dotnet run --project tools/Spot.Cli -- build windows --project samples/HelloEngine  # publish (also linux, browser)
```

Or open `HelloEngine.sptproj` in the editor. Space or the HUD button drops cubes; right-drag orbits the camera
and the scroll wheel zooms.

| Path | What it is |
|---|---|
| `HelloEngine.sptproj` | project config: name, start scene, asset folder |
| `Program.cs` | entry point: creates the application and runs it |
| `Assets/Scenes/Main.sptscene` | the scene (pure data, editable in the editor) |
| `Assets/UI/Hud.sptui` | the HUD, wired to scripts by widget name |
| `Assets/Scripts/` | `CameraOrbit`, `Spinner`, `CubeRain` (spawning + UI), `GlowRing` (render pass) |

`HelloEngine.csproj` and `HelloEngine.sln` are regenerated from the `.sptproj` by `spot generate`, `spot run`,
`spot build` and the editor, so they are not committed.
