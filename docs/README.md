# Spot Documentation

High-level documentation for the Spot game engine. These docs explain the concepts and how the
pieces fit together. They intentionally avoid specific APIs and code details, which change often —
read the source for the current method names and signatures.

## Contents

1. [Introduction](introduction.md) — what Spot is and how it's organized
2. [Scenes](scenes.md) — the container and lifecycle of everything in your game
3. [Entities & Components](entities-and-components.md) — the data model for game objects
4. [Scripting](scripting.md) — adding behavior to entities
5. [Rendering](rendering.md) — how things get drawn
6. [The Editor](editor.md) — the visual tool for building scenes
7. [Projects & Building a Game](projects-and-building.md) — the project format and shipping a build

## Quick start

```bash
dotnet build SpotEngine.slnx     # build the engine, editor, game, and tools
dotnet run --project editor      # launch the editor
```

See the repository [README](../README.md) for full build and run instructions.
