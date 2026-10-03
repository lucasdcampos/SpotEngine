# Spot Documentation

High-level documentation for **Spot**, a game framework and engine. These pages explain the concepts and how the
pieces fit together. They intentionally stay away from specific method names and signatures — those
change often, so read the source (and its XML doc comments) for the current API.

Spot is a 2D/3D game engine written in C# (.NET 10) on [Silk.NET](https://github.com/dotnet/Silk.NET)
(windowing, OpenGL, input, Assimp) with a [Dear ImGui](https://github.com/ocornut/imgui) editor. It comes in
three levels — a bare **core**, a code-only **framework**, and the full **engine** — so you can use as much
or as little of it as you want.

## Contents

**Core concepts**

1. [Introduction](introduction.md) — what Spot is and how it's organized
2. [Levels: Core, Framework, Engine](levels.md) — the three layers, what each holds, and how to drop down
3. [Architecture](architecture.md) — the engine's main loop, services, systems, and time
4. [Scenes](scenes.md) — the container and lifecycle of everything in your game
5. [Entities & Components](entities-and-components.md) — the data model for game objects
6. [Scripting](scripting.md) — adding behavior to entities

**Systems**

7. [Rendering](rendering.md) — how things get drawn, lighting, post-processing, and custom passes
8. [Physics](physics.md) — 2D and 3D simulation, colliders, and collisions
9. [Audio](audio.md) — playing and spatializing sound
10. [Input](input.md) — reading keys directly and binding named actions
11. [Animation](animation.md) — skeletal animation for rigged models
12. [Particle Systems](particles.md) — CPU-simulated billboard effects
13. [Runtime UI](ui.md) — building HUDs and menus with the retained widget tree
14. [Text & Fonts](text.md) — rendering text on screen and in the world
15. [Assets](assets.md) — importing, cooking, and referencing content
16. [Networking](networking.md) — server-authoritative multiplayer: transport, spawning, RPCs, sync vars

**Tools**

17. [The Editor](editor.md) — the visual tool for building scenes
18. [Projects & Building a Game](projects-and-building.md) — the project format and shipping a build

## Quick start

```bash
dotnet build SpotEngine.slnx                 # build the framework, engine, editor, samples, and tools
dotnet test  SpotEngine.slnx                 # run the test suite
dotnet run --project editor                  # launch the editor
dotnet run --project tools/Spot.Cli -- help  # the `spot` command-line tool
dotnet run --project samples/HelloQuad       # a level-1 program: your own loop (also HelloTriangle, Hello2D, Hello3D)
dotnet run --project tools/Spot.Cli -- run --project samples/HelloEngine  # cook and run the engine sample
dotnet run --project tools/Spot.Cli -- run --project samples/SolarSystem  # the solar-system showcase
dotnet run --project tools/Spot.Cli -- run --project samples/ProvingGrounds  # the FPS playground
```

See the repository [README](../README.md) for build and run instructions.
