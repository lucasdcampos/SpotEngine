# Spot Engine — Backlog & Fixes

Checklist of known gaps and missing features, ordered by how much they hurt day-to-day game development.
Check an item off when it ships.

---

## High Pain

> Blocks real game development today.

- [x] **Play-in-viewport** — editor launches an external process for Play mode; in-process play/pause/step is the single biggest workflow gap
- [x] **Spotlight** — `LightComponent` only has Directional and Point types; cone angle and falloff are absent
- [ ] **Mesh collider (3D)** — only Box/Sphere/Capsule shapes exist; non-trivial level geometry cannot be made solid
- [ ] **Blend trees** — `AnimatorController` has a state machine but no blend-tree nodes; smooth locomotion blends (walk↔run by a float parameter) are not possible
- [x] **Audio mixer / bus routing** — no volume groups (Music, SFX, UI); every source is a flat, ungrouped emitter
- [x] **Point light shadows** — only the directional light casts real-time shadows; point lights are always shadowless

---

## Medium Pain

> Workable today but noticeably missing.

**Editor**
- [x] **Scale handles in the transform gizmo** — TransformGizmo has translation and rotation handles but no visual scale handles
- [ ] **Prefab overrides (Apply / Revert)** — prefab instances exist but the editor has no Apply-to-prefab or Revert-to-prefab workflow
- [x] **Undo gaps** — replaced the polled full-scene snapshot history with a unified, per-operation action history (named entries, selection restore, History panel). Changes made during play are still not undoable, by design: stopping play restores the pre-play scene wholesale
- [ ] **Undo: finish migrating mutation sites** — hierarchy/component structural ops, `.sptui` documents, materials, animator controllers, project settings and the mixer layout still fall back to the coarse *Scene Change* catch-all. The History panel's "generic" counter names what is left
- [ ] **Root sibling order is not persisted** — root entity order lives only in the Hierarchy panel's runtime-id list and is not written to `.sptscene`, so **Move Up**/**Move Down** on a root is lost on save/load (and on play/stop). Undo restores an order the next save discards

**Rendering**
- [ ] **Full PBR material** — `Material` has a `Metallic` slot but no `Roughness` or ambient-occlusion map slot; the lighting model is PBR by half
- [ ] **Cascaded Shadow Maps (CSM)** — single shadow frustum with `ShadowDistance`; large outdoor scenes get blocky shadows at distance
- [ ] **SSAO** — no screen-space ambient occlusion; `AmbientIntensity` on the directional light is a flat constant, not geometry-aware
- [ ] **Depth of Field** — `PostProcessingComponent` has bloom/vignette/FXAA but no DoF effect

**Physics**
- [ ] **Physics joints / constraints** — no hinge, fixed, or spring joint for 3D; doors, ragdolls, and chains require custom script workarounds
- [x] **Physics materials** — no per-surface friction or restitution; all colliders share the same implicit defaults
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
- [x] **Color grading / LUT** — no LUT texture slot in `PostProcessingComponent`; stylistic grading requires a custom shader
- [x] **Occlusion culling** — only frustum culling exists; objects behind walls are submitted to the GPU

**Physics**
- [ ] **Mesh collider (2D)** — 2D physics only has Box and Circle; polygon/compound shapes are absent

**Animation**
- [ ] **IK (Inverse Kinematics)** — no two-bone IK or FABRIK solver; foot planting and hand targets require manual bone scripting

**Audio**
- [ ] **Audio effects** — no reverb, echo, or low-pass filter; OpenAL effects extension (EFX) is unused
- [ ] **Large audio streaming** — long tracks (music) are decoded entirely into memory; no progressive streaming

**Architecture / Tech Debt**
- [x] **GLSL `#include` dedup** — `ShadowCalculation` and `hash` utility functions are copy-pasted across multiple shaders
- [ ] **Incremental asset cook** — `spot cook` reprocesses all assets on every run; no timestamp/hash change detection
- [ ] **Component serialization via source-gen** — `ComponentSerialization.cs` uses a reflective switch; a source generator would make it trim-safe and AOT-friendly
- [ ] **RPC source-gen (networking)** — `RpcRegistry` dispatches via reflection; a source generator would remove the trimming caveat documented in `docs/networking.md`

**Networking**
- [ ] **Networking UI in editor** — host/connect only available through dev-console commands (`net_host`, `net_connect`); no panel in the editor
- [ ] **Networking sample** — no playable multiplayer example in `samples/`
