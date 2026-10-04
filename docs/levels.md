# Spot's Levels: Core, Framework, Engine

Spot is not one library but three, stacked. Each level is its own assembly, compiles on its own, and
depends only on the levels beneath it. Pick the highest level that suits your game and drop down whenever
you need to — **you are never locked in to what the level above supports**.

| Level | Assembly | Feels like | You get | You write |
|---|---|---|---|---|
| **1 — Core** | `Spot.Framework.Core` | SDL, GLFW | a window, events, raw input, the GPU, a minimal 2D batch, a PCM audio device | the loop, everything else |
| **2 — Framework** | `Spot.Framework` (+ `Spot.Framework.Assimp`) | raylib, MonoGame | file loading, sprites, shapes, text, a basic 3D renderer, models, skeletons, a mixer, input actions | the loop, the game's structure, your choice of physics/UI libraries |
| **3 — Engine** | `Spot.Engine` | Unity, Godot | the loop, scenes and entities, the lit renderer, post-processing, particles, physics, animator, UI, assets, editor, `spot` CLI | scripts and content |

## How a feature finds its level

The dividing line is **mechanism versus policy**:

- The **framework** (levels 1 and 2) provides *mechanisms*: data plus functions you call explicitly. It
  owns neither time nor the world — nothing runs unless you call it — and it never picks a third-party
  library for you. Sampling an animation clip into a pose is a mechanism; so is drawing a batch of
  camera-facing quads, or running a shader over the screen.
- The **engine** (level 3) provides *policies*: systems that run on their own every frame, have a file
  format or a component, decide an order, and choose libraries. An animator state machine is a policy;
  so is a particle system, a post-processing stack, or using BepuPhysics for 3D physics.

That is why animation, particles and post-processing are split across levels: the framework has
`Skeleton`, `BillboardBatch` and `FullscreenPass`; the engine builds its Animator, particle system and
post-processing on top of them. It is also why physics lives only in the engine — at the framework
level, bring whichever physics library you like.

## Level 1 — Core

The essentials for writing your own framework or engine, and nothing more: there is **no loop**. You
create a window and write the `while` yourself.

```csharp
using System.Numerics;
using Spot.Framework;
using Spot.Framework.Graphics;

using var window = new Window(new WindowSpec { Title = "Hello", Width = 1280, Height = 720 });

while (window.IsOpen)
{
    window.PollEvents();   // feeds Input, raises events, keeps the viewport in sync
    float dt = Time.Tick(); // real frame time, clamped against hitches
    if (Input.GetKeyDown(Key.Escape))
    {
        window.Close();
    }

    Renderer.Clear();
    Renderer2D.BeginScene(Matrix4x4.CreateOrthographicOffCenter(0, window.Width, 0, window.Height, -1, 1));
    Renderer2D.DrawQuad(new Vector2(100, 100), new Vector2(64, 64), new Vector4(1, 0, 0, 1));
    Renderer2D.EndScene();

    window.SwapBuffers();
}
```

What it holds:

- **Platform** — `Window` (creating one installs its graphics device), `Display`, `Time`, and a
  dependency-free `Log` that writes to the terminal until you plug in your own sinks, and never throws.
- **Input** — raw keyboard, mouse, gamepad and cursor-lock state, plus the generic *captured* and
  *suppressed* switches an overlay uses to withhold input from the game.
- **Events** — the window and input events `PollEvents` dispatches.
- **Graphics** — the backend-neutral `IGraphicsDevice` (OpenGL on desktop, WebGL2 in the browser), GPU
  resources (buffers, vertex arrays, shaders, textures from pixels, framebuffers), render state, and a
  minimal immediate `Renderer2D` (colored and textured quads, lines, rectangles).
- **Audio** — the audio device: OpenAL and Web Audio backends playing PCM buffers through voices, plus
  the listener. No decoders: those are level 2.

**In the browser** the same `Window` API is backed by a canvas and WebGL2, DOM events feed the same
`Input`, and the page's `requestAnimationFrame` arrives as `BrowserPlatform.AnimationFrame`. A browser
cannot run a blocking `while` loop, so a browser app runs one iteration of its loop per animation frame —
supporting the browser is your choice, built from these primitives.

## Level 2 — Framework

Friendlier, still 100% code: no editor, no asset pipeline, no files you didn't load yourself. It is
enough to build a whole game on, or your own engine.

- **IO** — a swappable `FileSystem` (disk, or the fetched content store in the browser) every loader
  reads through, with an optional path resolver.
- **Graphics** — `Image` (PNG/JPG/BMP/TGA/GIF decoding into CPU pixels) and `Texture2D.FromFile`; fonts and text;
  shapes and sprites on top of the core batch (triangles, circles, polygons, atlas regions); a screen
  batch with blending and scissor clipping; `Camera3D`; meshes and models; procedural primitives (cube,
  sphere, capsule, cylinder, cone, plane, quad — sized and subdivided by parameters) and utility images
  (solid, flat normal, checkerboard, grid, soft dot), with PNG and OBJ export;
  `BasicRenderer3D` (one directional light and ambient — lit, unlit, textured, instanced, skinned, or
  through your own shader); `BillboardBatch` for blended camera-facing quads; `FullscreenPass` for
  running a shader over the screen; and a pluggable model importer.
- **Animation** — `AnimationClip`, bones and `Skeleton`: sample a clip into a pose and compute skinning
  palettes, with no scene or entities involved.
- **Audio** — `AudioClip` from WAV/OGG files or bytes, the mixer and its buses, and `AudioManager.Init()`
  to open the platform's default device.
- **Input** — named actions bound to keys, buttons and axes (`Input.Bind`, `Input.GetAction`, …), layered
  on the raw level-1 state.
- **Mathematics** and **Diagnostics** — bounding boxes, frustums, math helpers, a profiler, frame stats.

**`Spot.Framework.Assimp`** is an optional module that imports FBX, glTF, OBJ and other source formats
at runtime. It is a separate assembly because it carries a native dependency (and is desktop-only).
Register its importer and `ModelImporter.Load` reads source models directly — no cooking.

## Level 3 — Engine

The opinionated engine: Spot's own choices, wired together. The `Application` owns the loop and wraps
every frame in the never-crash recovery net; scenes hold entities and components; built-in systems run
physics (BepuPhysics in 3D, Aether in 2D), scripts, the animator, particles and audio; the lit renderer
adds shadows, many lights, culling, a sky and post-processing; and the editor, the asset pipeline and the
`spot` CLI turn a project into a shipped game. The rest of this documentation is mostly about this level.

## Escape hatches

Dropping down a level never requires leaving the one you are on:

- **At level 1**, the GPU is fully public: `Renderer.Device` is the `IGraphicsDevice` every renderer is
  built on, every resource wrapper exposes its device handle, and `GraphicsBuffer<T>` is a raw buffer
  primitive. On desktop the underlying OpenGL context is reachable too (`Renderer.Api`).
- **At level 2**, everything is code: renderers accept your own shaders, batches take your own textures
  and blend modes, and loaders read through a file system you can replace.
- **At level 3**, the engine exposes its `Application.Window` and the graphics device, lets you add
  per-frame logic as an `ISystem` and global subsystems as an `IEngineService`, and lets you draw at fixed
  points of the scene pipeline with an `IRenderPass` (see [Rendering](rendering.md#custom-render-passes)).
  Inside a pass any framework renderer — or the raw device — works.

## Never crash, per level

Levels 1 and 2 are libraries: they never take the process down on their own (logging never throws,
nothing throws on a background thread), but they *do* report errors to the caller, because the loop
belongs to you — a missing file throws from the call that loaded it. The engine's safety nets — the
per-frame recovery boundary, script quarantine, render-pass and system guards, loaders that log and carry
on — all live at level 3. See [Introduction](introduction.md#a-note-on-resilience).

## Namespaces

Inside the framework, **the namespace is the topic and the assembly is the level**, as in MonoGame:
`using Spot.Framework.Graphics;` sees whatever the referenced framework assemblies offer about graphics,
level 1 and level 2 alike.

| Namespace | What lives there |
|---|---|
| `Spot.Framework` | `Window`, `Time`, `Log`, `Display`, `Input` and keys, input actions, profiler, frame stats |
| `Spot.Framework.Events` | window and input events |
| `Spot.Framework.Graphics` | the device, GPU resources, images, 2D and basic 3D renderers, text, meshes and models |
| `Spot.Framework.Audio` | the audio device, clips, decoders, the mixer |
| `Spot.Framework.Animation` | clips, bones, `Skeleton` |
| `Spot.Framework.IO` | `FileSystem` and its disk and in-memory implementations |
| `Spot.Framework.Mathematics` | bounding boxes, frustums, math helpers |
| `Spot.Framework.Assimp` | the Assimp model importer |
| `Spot.Framework.Browser` | `BrowserPlatform`: the page's entry points and animation-frame event |
| `Spot.Engine` | `Application`, projects, services, engine logging |
| `Spot.Engine.Scenes` | scenes, entities, components, systems, scripting, render passes |
| `Spot.Engine.Rendering` | the lit 3D renderer, post-processing, particles, `RenderSettings` |
| `Spot.Engine.Assets` | the asset pipeline, cooked formats, materials, guid loading |
| `Spot.Engine.Animation` | the animator controller |
| `Spot.Engine.Physics` | colliders, bodies, the character controller, raycasts |
| `Spot.Engine.UI` | the runtime widget tree and its serializer |
| `Spot.Engine.Console` | the developer console |
| `Spot.Engine.Browser` | `BrowserHost`: boots the engine in a browser build |

Projects written against Spot 0.2's namespaces (`Spot.Core`, `Spot.Rendering`, `Spot.Scenes`, …) are
moved by `spot migrate`; see [Projects & Building](projects-and-building.md#upgrading-a-project).

## Samples

The `samples/` folder has small programs for each level. The framework ones are plain programs, run with
`dotnet run --project samples/<Name>` (add `-- --frames N` to exit after N frames):

| Sample | Level | Shows |
|---|---|---|
| `HelloQuad` | 1 | your own loop, raw input, `Renderer2D` quads |
| `HelloTriangle` | 1 | a triangle straight through `IGraphicsDevice`: buffers, a vertex array, a shader |
| `Hello2D` | 2 | sprites, shapes, text, a sound built from PCM, input actions |
| `Hello3D` | 2 | `Camera3D`, lit primitives, instancing, and an optional model loaded with Assimp and animated with `Skeleton` |
| `HelloEngine` | 3 | a project: a scene with lighting, shadows, sky and post-processing, physics, particles, a UI document, scripts, and a custom render pass |
| `SolarSystem` | 3 | a showcase project: the solar system in real time, with procedural planet shaders in custom render passes, an instanced asteroid belt, hover info cards, and an icon toolbar with a settings pop-up built from restyled engine widgets |
| `ProvingGrounds` | 3 | a first-person shooter playground: the character controller, hitscan raycasts and grenades, impulses, explosive chain reactions, kinematic lifts and drones, trigger volumes, weapon effects in custom render passes, synthesized sound, and a full HUD and pause menu |
| `Voxelcraft` | 3 | an infinite block world: chunks generated and meshed on worker threads and streamed around the player, greedy meshing with ambient occlusion and flood-filled light, a renderer built entirely from custom render passes (shadow map, sky, terrain, water), a day and night cycle, and a first-person player that walks, swims, flies, breaks and places blocks |

`HelloEngine` is an engine project like any other: open `samples/HelloEngine/HelloEngine.sptproj` in the
editor, or cook and run it with `spot run --project samples/HelloEngine` (and publish it with
`spot build windows` or `spot build browser`). It uses no binary assets — primitives and generated textures
only. `SolarSystem`, `ProvingGrounds` and `Voxelcraft` are opened and run the same way; they ship no assets
either — `SolarSystem` draws every surface with its own shaders, `ProvingGrounds` builds its level and weapons
from primitives and synthesizes every sound, and `Voxelcraft` paints its block textures in code at startup.

## Related

- [Introduction](introduction.md) — what Spot is
- [Architecture](architecture.md) — how the engine's loop, services and systems fit together
- [Rendering](rendering.md) — the framework renderers, the engine pipeline, and custom render passes
