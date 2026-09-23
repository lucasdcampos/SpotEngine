# Spot Engine — Backlog & Fixes

Checklist of known gaps and missing features, ordered by how much they hurt day-to-day game development.
Check an item off when it ships.

---

## High Pain

> Blocks real game development today.

- [ ] **Play-in-viewport** — editor launches an external process for Play mode; in-process play/pause/step is the single biggest workflow gap
- [x] **Spotlight** — `LightComponent` only has Directional and Point types; cone angle and falloff are absent
- [ ] **Mesh collider (3D)** — only Box/Sphere/Capsule shapes exist; non-trivial level geometry cannot be made solid
- [ ] **Blend trees** — `AnimatorController` has a state machine but no blend-tree nodes; smooth locomotion blends (walk↔run by a float parameter) are not possible
- [ ] **Audio mixer / bus routing** — no volume groups (Music, SFX, UI); every source is a flat, ungrouped emitter
- [x] **Point light shadows** — only the directional light casts real-time shadows; point lights are always shadowless

---

## Medium Pain

> Workable today but noticeably missing.

**Editor**
- [x] **Scale handles in the transform gizmo** — TransformGizmo has translation and rotation handles but no visual scale handles
- [ ] **Prefab overrides (Apply / Revert)** — prefab instances exist but the editor has no Apply-to-prefab or Revert-to-prefab workflow
- [ ] **Undo gaps** — undo/redo uses full-scene JSON snapshots so it only captures inspector edits; changes made by scripts or physics during play are not undoable

**Rendering**
- [ ] **Full PBR material** — `Material` has a `Metallic` slot but no `Roughness` or ambient-occlusion map slot; the lighting model is PBR by half
- [ ] **Cascaded Shadow Maps (CSM)** — single shadow frustum with `ShadowDistance`; large outdoor scenes get blocky shadows at distance
- [ ] **SSAO** — no screen-space ambient occlusion; `AmbientIntensity` on the directional light is a flat constant, not geometry-aware
- [ ] **Depth of Field** — `PostProcessingComponent` has bloom/vignette/FXAA but no DoF effect

**Physics**
- [ ] **Physics joints / constraints** — no hinge, fixed, or spring joint for 3D; doors, ragdolls, and chains require custom script workarounds
- [ ] **Physics materials** — no per-surface friction or restitution; all colliders share the same implicit defaults
- [x] **2D trigger callbacks** — `BoxCollider2DComponent` exists but there are no `OnTriggerEnter2D` / `OnTriggerExit2D` script hooks

**Animation**
- [ ] **Animation events** — no per-frame callback on a clip; the current workaround is manual time polling in a script
- [ ] **Root motion** — movement baked into the root bone is not extracted and applied to the entity's transform

---

## Low Pain / Tech Debt

> Engine works fine without these; address when the above is clear.

**Editor**
- [x] **Profiler window** — no per-system frame-time visualization; only raw log output

**Rendering**
- [ ] **Color grading / LUT** — no LUT texture slot in `PostProcessingComponent`; stylistic grading requires a custom shader
- [ ] **Occlusion culling** — only frustum culling exists; objects behind walls are submitted to the GPU

**Physics**
- [ ] **Mesh collider (2D)** — 2D physics only has Box and Circle; polygon/compound shapes are absent

**Animation**
- [ ] **IK (Inverse Kinematics)** — no two-bone IK or FABRIK solver; foot planting and hand targets require manual bone scripting

**Audio**
- [ ] **Audio effects** — no reverb, echo, or low-pass filter; OpenAL effects extension (EFX) is unused
- [ ] **Large audio streaming** — long tracks (music) are decoded entirely into memory; no progressive streaming

**Architecture / Tech Debt**
- [ ] **GLSL `#include` dedup** — `ShadowCalculation` and `hash` utility functions are copy-pasted across multiple shaders
- [ ] **Incremental asset cook** — `spot cook` reprocesses all assets on every run; no timestamp/hash change detection
- [ ] **Component serialization via source-gen** — `ComponentSerialization.cs` uses a reflective switch; a source generator would make it trim-safe and AOT-friendly
- [ ] **RPC source-gen (networking)** — `RpcRegistry` dispatches via reflection; a source generator would remove the trimming caveat documented in `docs/networking.md`

**Networking**
- [ ] **Networking UI in editor** — host/connect only available through dev-console commands (`net_host`, `net_connect`); no panel in the editor
- [ ] **Networking sandbox demo** — `Spot.Net.dll` is not committed to `sandbox/EngineBin`; no playable multiplayer example
