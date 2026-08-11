# AGENTS.md

Spot is a 2D/3D game engine written in C# (.NET 10) on Silk.NET (windowing, OpenGL, input, Assimp) with a Dear ImGui editor.

## Commands

Run from the repo root. The solution is `SpotEngine.slnx` (XML `.slnx` format, not `.sln`).

```bash
dotnet build SpotEngine.slnx                 # build the whole solution
dotnet test SpotEngine.slnx                  # run the xUnit test suite
dotnet run --project editor                  # launch the ImGui editor
dotnet run --project sandbox/Sandbox.csproj  # run the sandbox project (dir has >1 project file)
dotnet run --project tools/Spot.Cli -- help  # the `spot` CLI (new/generate/build/cook/migrate)
```

## Projects

| Project | Output | Notes |
|---|---|---|
| `engine/Spot.Engine` | library (namespace `Spot`) | the engine core |
| `debugui/Spot.DebugUI` | library (namespace `Spot.DebugUI`) | ImGui debug/authoring panels (hierarchy, inspector, theming); referenced by the editor and hostable as the runtime debug overlay. Kept out of the engine so the runtime carries no authoring UI |
| `editor/Spot.Editor` | exe | ImGui docking editor |
| `sandbox/Sandbox` | exe | data-driven showcase project |
| `tools/Spot.Build` | library | `.sptproj` → buildable app (used by editor + CLI) |
| `tools/Spot.Cli` | exe (`spot`) | thin CLI front-end over Spot.Build |
| `tests/*` | xUnit | `Spot.Engine.Tests`, `Spot.Build.Tests` |

## Rules

- **Never crash the engine.** Bad input, a throwing script, a broken scene, or a faulty panel must log and continue, never take the process down. Preserve the existing safety nets (`Application.Run` frame try/catch, `ScriptSystem` script quarantine, loaders that catch and log).
- **Warnings are errors** in `Spot.Engine`, `Spot.DebugUI`, `Sandbox`, and `Spot.Build` (`TreatWarningsAsErrors`). New code there must be warning-clean. Nullable reference types and `ImplicitUsings` are on everywhere.
- **Before marking a task complete**, always build (`dotnet build SpotEngine.slnx`) and run the tests (`dotnet test SpotEngine.slnx`), and confirm both pass.
- **Always update the docs** under `docs/` when you change behavior, add features, or alter architecture.
