# Rendering

Spot renders both 2D and 3D. The rendering system is **layered** so you can work at whatever level of
control you need.

## The automatic path

The simplest way to draw is to do nothing special: give your entities the right components — a
transform plus a sprite or a mesh — and add a camera to the scene. The engine's rendering system walks
the scene each frame and draws everything visible through the camera. For most games this is all you
need.

A **camera** defines the viewpoint. A scene can have a camera marked as the primary one, and the scene
is rendered from its perspective. Cameras can be 2D (orthographic) or 3D (perspective).

## The layers

Underneath the automatic path are progressively lower-level tools:

- **High level** — the scene rendering system draws your entities for you.
- **Mid level** — separate 2D and 3D renderers let you draw batches of quads, lines, meshes, and
  effects yourself, in your own render passes.
- **Low level** — a core renderer wraps draw calls and render state, and exposes the underlying
  graphics API directly for full control.

You can mix these. A scene can let the engine draw its entities and then issue extra custom drawing on
top.

## 3D content

Spot can import 3D models from common formats (via Assimp) and draw them with materials and textures.
A basic lighting model with a directional light is available, and the engine provides simple built-in
primitives (such as cubes, planes, and spheres) so you can block out scenes without external assets.

## Related

- [Entities & Components](entities-and-components.md) — the visible components (sprite, mesh, camera, light)
- [Scenes](scenes.md) — where rendering fits in the frame
