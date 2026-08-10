# Rendering

Spot renders both 2D and 3D. The renderer is **layered** so you can work at whatever level of
control you need, and the default look is tuned so that simply lighting a scene well looks good
without hand-configuring the pipeline.

## The automatic path

The simplest way to draw is to do nothing special: give your entities the right components — a
transform plus a sprite or a mesh — and add a camera to the scene. The engine's rendering system
walks the scene each frame and draws everything visible through the camera. For most games this is
all you need.

A **camera** defines the viewpoint. A scene renders through the camera marked *primary*, and cameras
can be **2D** (orthographic) or **3D** (perspective). The camera also carries the background color
the scene clears to.

## The layers

Underneath the automatic path are progressively lower-level tools:

- **High level** — the scene rendering system draws your entities for you.
- **Mid level** — separate 2D and 3D renderers let you draw batches of quads, lines, meshes, and
  effects yourself, in your own render passes.
- **Low level** — a core renderer wraps draw calls and render state, and exposes the underlying
  graphics API directly for full control.

You can mix these: a scene can let the engine draw its entities and then issue extra custom drawing
on top.

## 3D content and lighting

Spot imports 3D models from common formats (via Assimp) and draws them with **materials** and
textures. It also ships simple built-in **primitives** (cube, plane, sphere, and friends) so you can
block out scenes without external assets.

A mesh renderer points at a model and, optionally, a single **submesh** of it (its `SubmeshIndex`;
the default of `-1` draws the whole model). This is what lets a model with many parts be spread
across an **entity hierarchy** — one entity per part, each drawing its own submesh with its own
material — rather than collapsed onto a single object. Dragging a model into a scene builds exactly
that hierarchy; see [The Editor](editor.md).

A **rigged** model is drawn the same way but skinned: its mesh follows a skeleton of bone entities that
an Animator poses each frame. See [Animation](animation.md).

Lighting supports a **directional** light (a sun, with an ambient term) and **point** lights.
Directional lights can cast real-time **shadows**. A **skybox** and optional **dynamic clouds**
provide the backdrop.

## Post-processing and quality

Two surfaces control the final image, and they have different jobs:

- **Global render settings** are pipeline/quality knobs that apply to every scene: whether rendering
  goes through an HDR buffer, whether shadows are enabled, and the shadow map's distance and
  resolution. By default the engine renders in HDR with a full, tasteful default look (ACES tone
  mapping, FXAA, gated bloom, a faint vignette) even with no per-scene component present.
- **A Post Processing component** is the *per-scene artistic* control: add it to a scene to customize
  that look — tone mapping, bloom, vignette, and so on. Adding it is about *customizing* the look,
  not switching quality on.

This split follows the engine's convention that graphics are tuned through global settings and a few
existing components rather than scattered ad-hoc knobs.

## Related

- [Entities & Components](entities-and-components.md) — the visible components (sprite, mesh, camera, light)
- [Assets](assets.md) — how models, textures, and materials are imported
- [Architecture](architecture.md) — where rendering sits in the frame
