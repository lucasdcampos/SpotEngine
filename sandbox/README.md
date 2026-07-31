# Sandbox

A small, data-driven Spot project that doubles as the engine's reference/playground. Open it in the
editor to poke at scenes, or run it standalone. It references the engine as **live source** (not a
copied DLL), so it always builds against the current engine and acts as a smoke test of the public API.

## Run it

```bash
dotnet run --project sandbox        # standalone
dotnet run --project editor         # then File > Open  ->  sandbox/Sandbox.sptproj
```

The standalone build loads `Sandbox.sptproj` and switches to its start scene (`Scenes/Main.sptscene`).

## Layout

```
Sandbox.sptproj          project config (Name, StartScene, AssetDirectory)
Program.cs               entry point: loads the project + start scene, then hands off to the engine
Assets/
  Scenes/                .sptscene files (pure data — editable in the editor)
    Main.sptscene        hero scene: camera, sun, dynamic-cloud sky, ground + primitives
    Water.sptscene       water-material shader on a plane, with sky and a floating sphere
    Primitives.sptscene  catalog of cube / sphere / quad / plane
    Physics2D.sptscene   sprites falling onto a static floor (2D physics + a textured sprite)
  Materials/
    Water.sptmat         the water shader material used by Water.sptscene
  Textures/
    spot.png             sample texture
```

## Conventions

- Scenes use only **built-in engine components** (no custom scripts), so they open and edit fully in the
  editor. The editor process does not load a project's compiled scripts today, so scripted scenes would
  not run in-editor — add them only once that tooling exists.
- Asset paths in scenes/materials are **relative to `Assets/`** (e.g. `Textures/spot.png`,
  `Materials/Water.sptmat`). `AssetPath` resolves them at load, so the project is portable across clones.
- Meshes reference primitives via `primitive:cube|plane|quad|sphere`.
