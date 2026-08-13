# Spot Documentation

High-level documentation for the **Spot** game engine. These pages explain the concepts and how the
pieces fit together. They intentionally stay away from specific method names and signatures — those
change often, so read the source (and its XML doc comments) for the current API.

Spot is a 2D/3D game engine written in C# (.NET 10) on [Silk.NET](https://github.com/dotnet/Silk.NET)
(windowing, OpenGL, input, Assimp) with a [Dear ImGui](https://github.com/ocornut/imgui) editor.

## Contents

**Core concepts**

1. [Introduction](introduction.md) — what Spot is and how it's organized
2. [Architecture](architecture.md) — the main loop, services, systems, and time
3. [Scenes](scenes.md) — the container and lifecycle of everything in your game
4. [Entities & Components](entities-and-components.md) — the data model for game objects
5. [Scripting](scripting.md) — adding behavior to entities

**Systems**

6. [Rendering](rendering.md) — how things get drawn, lighting, and post-processing
7. [Physics](physics.md) — 2D and 3D simulation, colliders, and collisions
8. [Audio](audio.md) — playing and spatializing sound
9. [Input](input.md) — reading keys directly and binding named actions
10. [Animation](animation.md) — skeletal animation for rigged models
11. [Runtime UI](ui.md) — building HUDs and menus with the retained widget tree
12. [Text & Fonts](text.md) — rendering text on screen and in the world
13. [Assets](assets.md) — importing, cooking, and referencing content

**Tools**

14. [The Editor](editor.md) — the visual tool for building scenes
15. [Projects & Building a Game](projects-and-building.md) — the project format and shipping a build

**Samples**

16. [The Sandbox Hub](sandbox-hub.md) — the showcase project, its menu hub, and the Horde Survival demo

## Quick start

```bash
dotnet build SpotEngine.slnx                 # build the engine, editor, sandbox, and tools
dotnet test  SpotEngine.slnx                 # run the test suite
dotnet run --project editor                  # launch the editor
dotnet run --project sandbox/Sandbox.csproj  # run the sandbox showcase project
dotnet run --project tools/Spot.Cli -- help  # the `spot` command-line tool
```

See the repository [README](../README.md) for build and run instructions.
