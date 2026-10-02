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

Underneath the automatic path are progressively lower-level tools, following Spot's
[levels](levels.md):

- **Engine** — the scene rendering system draws your entities for you, with the full lit renderer
  (shadows, many lights, culling, sky, post-processing). [Custom render passes](#custom-render-passes)
  add your own drawing at fixed points of its pipeline.
- **Framework** — code-only renderers you call yourself: shapes, sprites and text on the 2D batch; a
  screen-space batch with blending and scissor clipping; `Camera3D` and `BasicRenderer3D` for meshes
  and models (one directional light and ambient; unlit, textured, instanced, skinned, or with your own
  shader); `BillboardBatch` for blended camera-facing quads; and `FullscreenPass` for running a shader
  over the screen.
- **Core** — the render state (clear, viewport, blending, depth, targets), a minimal immediate
  `Renderer2D` (quads, lines, rectangles), GPU resources, and the backend-neutral graphics device itself
  (`Renderer.Device`) for full control.

You can mix these: a scene can let the engine draw its entities, and a render pass can then issue extra
drawing with any framework renderer — or raw device calls — on top.

## Custom render passes

A **render pass** is drawing the engine has no component for — a custom effect, a debug visualization, a
procedural background — injected into a scene's frame. Implement `IRenderPass` (or wrap a callback in a
`DelegateRenderPass`) and register it with `Scene.AddRenderPass`. Each pass runs at one **stage**:

| Stage | Runs | Typical use |
|---|---|---|
| `BeforeOpaque` | after the shadow maps, before any opaque geometry | backgrounds, custom skies |
| `AfterOpaque` | after opaque meshes and sprites, with depth filled | geometry that should be occluded and lit like the scene |
| `AfterTransparent` | after particles and world text, still inside the HDR capture | effects that should be tone-mapped and bloom |
| `AfterPostProcess` | after post-processing, before the screen-space UI | crisp, untone-mapped world overlays |
| `Overlay` | last, over the UI | debug overlays, cursors, fades |

A pass receives a `RenderContext` with the scene, the camera's view-projection and position, the render
target and viewport bound at that point (the HDR capture, an editor viewport, or the screen), and whether
the frame is post-processed. Passes in the same stage run by their order, then by registration. A pass
should restore any state the engine relies on — depth testing, blending, the bound target — and one that
throws is logged once and skipped, never taking the frame down.

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

## 2D content

A **sprite** is a flat quad drawn with a color and an optional texture (the color tints the texture,
or fills the quad when there is none). Sprites are batched by texture into few draw calls. The sprite
shader **alpha-tests** its texture, so a cut-out texture — a white circle, triangle, or polygon on a
transparent background — renders as that shape rather than a square; this works even though the sprite
pass itself runs without alpha blending. Pair sprites with an **orthographic camera** for a 2D game.

Particles are drawn **after** the opaque passes — both the 3D meshes and the 2D sprite batch — and
before post-processing, so they blend and glow over your scene (and feed bloom) rather than being
painted over by it.

**World-space text** (a Text component on an entity) is drawn alongside particles — blended,
camera-facing by default, before post-processing — so it is tone-mapped like the scene and occluded by
solid geometry. See [Text & Fonts](text.md).

## The UI pass

The runtime UI is the **final** pass, drawn after post-processing directly to the output framebuffer in
screen space (an orthographic projection over the window), so the interface stays crisp and is never
tone-mapped or bloomed. It renders the scene's UI tree — HUDs, menus — with alpha blending and scissor
clipping, which is why small text and soft widget edges look clean where the alpha-tested sprite pass
would not. Scenes with no widgets skip the pass entirely. See [Runtime UI](ui.md).

## Post-processing and quality

Two surfaces control the final image, and they have different jobs:

- **Global render settings** are pipeline/quality knobs that apply to every scene: whether rendering
  goes through an HDR buffer, whether shadows are enabled, and the shadow map's distance and
  resolution. By default the engine renders in HDR with a full, tasteful default look (ACES tone
  mapping, FXAA, gated bloom, a faint vignette) even with no per-scene component present.
- **A Post Processing component** is the *per-scene artistic* control: add it to a scene to customize
  that look — tone mapping, bloom, vignette, color grading LUT, and so on. Adding it is about *customizing*
  the look, not switching quality on.

### Color grading LUT

The Post Processing component accepts a **2D LUT texture** in horizontal-strip format (the standard
Unity-style LUT: 256 × 16 pixels, encoding a 16³ grading cube). Enable **Enable Lut**, assign a `.png`
or `.sptex` LUT asset to the **Lut Texture** slot, and dial **Lut Intensity** between 0 (no grading) and
1 (full grade). The LUT is sampled after tone-mapping and gamma correction, so it operates in display
space — the same domain LUT tools like Photoshop and DaVinci Resolve export to.

This split follows the engine's convention that graphics are tuned through global settings and a few
existing components rather than scattered ad-hoc knobs.

## Graphics backends

The renderer never talks to a graphics library directly. Every GPU command flows through a small
`IGraphicsDevice` seam — buffers, vertex arrays, shaders, textures, draws — expressed in engine-neutral
types. The seam is public and lives in the core level, so your own code can use it exactly as the
engine does. Two backends implement it:

- **Desktop** uses an OpenGL device backed by **Silk.NET**.
- **The browser** uses a **WebGL2** device that issues each call from C# to JavaScript over `[JSImport]`,
  against the canvas' WebGL2 context. Because WebGL2 is OpenGL ES 3.0, engine shaders (authored once in
  desktop GLSL) are rewritten to `#version 300 es` by the device before compiling.

This seam is what lets the engine's rendering code run **unchanged** across desktop and browser: the same
`RenderSystem` drives both — 2D, UI, particles, and the **3D forward pipeline (meshes, materials, lighting,
shadows, skybox)** all flow through `IGraphicsDevice`, so a feature is written once and runs everywhere.
The seam grew a render-target layer (framebuffers, float/depth texture formats, depth-compare sampling) so
shadow maps work on both backends; genuine platform gaps are absorbed here (WebGL2 has no `glPolygonMode`,
so wireframe is a no-op there, and no clamp-to-border, so the shadow shaders test bounds instead).

Post-processing — HDR capture, bloom, ACES tone mapping, FXAA — is the one part still desktop-only. It
lives behind an `IScenePostProcessor` seam that the desktop host installs; the browser leaves it unset and
renders straight to the screen. "Limiting" a platform is therefore a **runtime choice** (which processor,
which `RenderSettings`), not a forked renderer. Neutralizing the post pipeline for the browser is a
follow-up. See [Projects & Building](projects-and-building.md#the-browser-target) for the browser target.

## Scalable rendering

The 3D pipeline is built to scale to large scenes rather than draw everything blindly.

**Frustum culling.** Before a mesh is drawn, its model bounds are transformed to world space and tested
against the camera's view frustum; meshes fully outside the view are skipped, in both the main pass and
the directional shadow pass (there tested against the light's frustum, so off-map casters are skipped
too). Culling is conservative — it never drops something actually on screen. Skinned meshes have their
bind-pose bounds padded first, so animation can never pop a limb out of view. The `RendererDebug`
surface exposes `VisibleMeshCount` / `CulledMeshCount` (updated each frame) and a
`DisableFrustumCulling` toggle for A/B comparison. What survives this test then faces occlusion culling
below.

**Occlusion culling.** Frustum culling only removes what is off screen; a wall's worth of geometry can
still sit in view and be drawn for nothing. Tick **Occluder** on a mesh renderer and the engine starts
dropping what that mesh hides. Each frame the occluders on screen are rasterized into a small **software
depth buffer** on the CPU (256 px wide by default), and every other mesh is then culled when the box it
covers is entirely behind what that buffer already holds. It is pure CPU work through and through — no
GPU queries, no frame of latency, nothing to bake — so it behaves identically on the desktop and WebGL2
backends, and it is covered by ordinary unit tests.

The test is **conservative in both directions**: a pixel is written only when an occluder covers it
whole, at that occluder's farthest depth within the pixel, while a candidate is measured by its nearest
depth over a screen rectangle rounded outward, and is dropped only when *every* pixel of that rectangle
is already blocked. A coarse buffer therefore means culling slightly less, never culling something
visible. Whole faces are rasterized at once rather than as triangle pairs, since a conservative test
would otherwise leave a one-pixel seam along each shared diagonal — and one unwritten pixel keeps a
candidate alive.

What an occluder contributes is its **bounding box**, not its triangles, which is what keeps the cost
flat (twelve triangles apiece) no matter how detailed the model is. That makes the flag an assertion
about shape: mark geometry whose box is solid all the way through — a wall, a floor slab, a closed
crate, a cliff — and not something you can see into or past, like a hollow building shell, a doorway
frame, a fence or a tree, whose box would cover the opening and hide what should show through it.
Skinned meshes (they leave their bind pose) and see-through ones (alpha or water) are ignored as
occluders even when flagged. Up to 32 occluders are rasterized per frame, the largest on screen first;
ones covering less than about 0.15% of the screen are skipped as not worth the fill.

Global knobs are `RenderSettings.OcclusionCulling` (**on by default** — it costs nothing in a scene that
marks no occluders) and `RenderSettings.OcclusionBufferWidth`. The `occlusion` console command toggles it
and prints what the last pass culled, `stats` includes the same counts, and the editor's Profiler panel
shows drawn / off-screen / occluded meshes with a checkbox per stage for A/B'ing. `RendererDebug` carries
the counters (`OccludedMeshCount`, `OccluderCount`) and a `DisableOcclusionCulling` debug override.

**GPU instancing.** After culling, standard (non-water) rigid meshes are grouped by mesh + material and
drawn with **instanced draw calls** — one call per group, however many copies it holds — instead of one
call per object. Each copy's world matrix and color travel as per-instance vertex attributes, so a forest
of hundreds of identical trees costs a handful of draw calls rather than hundreds. The grouping buffers
are reused frame to frame, so the path is allocation-free once warmed up. Skinned and water meshes keep
their own per-draw paths. This flows through `IGraphicsDevice` (a `DrawElementsInstanced` /
`VertexAttribDivisor` seam), so it runs on both the desktop and WebGL2 backends.

**Many lights.** Point lights are uploaded once per frame into a shared **uniform buffer** (UBO) — a
`std140` block of up to 256 lights — instead of a fixed handful of individual uniforms, so a scene can be
lit by many point lights at once. The UBO seam (`BindBufferBase`, uniform-block binding) is part of
`IGraphicsDevice` and works on both backends; where uniform buffers are somehow unavailable, point lights
are disabled rather than crashing (the directional light still renders).

**Clustered lighting.** With many lights, testing every one at every fragment is wasteful. Enable
`RenderSettings.ClusteredLighting` and the view frustum is diced each frame into a cluster grid ("froxels")
— screen tiles in x/y, an exponential radial-distance slice in z — and each light is assigned (on the CPU)
to the froxels its range reaches. The assignment is carried to the shader in two integer lookup textures (a
per-froxel `offset·count`, and a flat light-index list), so a fragment loops over just its froxel's lights.
The froxel a fragment falls in is computed the same way on both sides, and everything derives from the
view-projection so no extra camera plumbing is needed. It is **off by default** and falls back to the
brute-force loop for orthographic/degenerate cameras; it applies to standard meshes (water always uses the
brute-force loop). The integer-texture seam (`R32UI` + `usampler2D` lookups) is part of `IGraphicsDevice`,
so — like everything else here — it runs on both the desktop and WebGL2 backends.

## Measuring performance

`RenderSettings.VSync` (on by default) gates whether the buffer swap waits for the monitor's vertical
blank. With it on, the frame rate is capped at the refresh rate, so an on-screen FPS reading tells you
nothing above that cap — an "empty scene at 70 FPS" is usually just the cap, not the cost. Turn VSync off
to profile: the `vsync` console command toggles it live (`vsync off`), the `stats` command prints the
current frame time / FPS / VSync state, and the editor's viewport HUD shows `ms` next to FPS. Frame times
come from `FrameStats`, which averages the real (unclamped) frame delta so a stall shows up instead of
hiding behind the simulation's delta clamp.

## Related

- [Entities & Components](entities-and-components.md) — the visible components (sprite, mesh, camera, light)
- [Runtime UI](ui.md) and [Text & Fonts](text.md) — the screen-space UI pass and world/screen text
- [Assets](assets.md) — how models, textures, and materials are imported
- [Architecture](architecture.md) — where rendering sits in the frame
