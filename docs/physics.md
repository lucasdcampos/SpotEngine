# Physics

Spot simulates both 2D and 3D physics. Physics runs only in **play mode** — while a scene's runtime
is being updated — so editing a scene never moves things around.

## The model

You make an entity physical by giving it two kinds of component:

- A **body** — a Physics Body (2D or 3D) that says the entity participates in the simulation and
  whether it's dynamic (moved by forces and gravity) or static.
- A **collider** — a shape used for collision. In 3D that's a box, sphere, or capsule; in 2D, a box.

An entity with a collider but no body is a static obstacle; add a body to make it move. A collider
can also be a **trigger** — it detects overlaps but produces no physical response, which is how you
build pickups, checkpoints, and volumes.

## 3D backend

3D physics runs on **BepuPhysics v2**, a full rigid-body simulation, behind an internal interface.
If that backend ever fails to initialize, the engine falls back to a simple built-in AABB solver so
play mode never dies rather than crashing — consistent with the engine's "never crash" rule. The
backend is built lazily when a scene enters play and torn down when it exits.

A **character controller** component provides capsule-based character movement (walking, slopes,
stepping) for players and NPCs, rather than pushing a raw rigid body around.

## Collisions and triggers

The simulation reports contacts, and the engine dispatches them to the scripts on the entities
involved. Scripts receive **collision** enter/stay/exit callbacks for solid contacts (with the other
entity and a contact normal/point) and **trigger** enter/stay/exit callbacks for overlaps. See
[Scripting](scripting.md).

## Raycasting

You can cast a ray into a scene's 3D simulation and get the closest hit within a distance — useful
for shooting, line-of-sight checks, ground probes, and mouse picking. Raycasts are meaningful only
while the simulation is live (play mode).

## Global settings and collision layers

Like rendering, physics is tuned through a **single global settings surface** rather than per-scene
wiring: the 3D backend to use and world **gravity**. It also defines a **collision layer matrix** —
each collider belongs to a layer, and you can enable or disable collisions between any pair of layers
(symmetrically) so, for example, projectiles pass through each other but hit walls. Settings are read
when a scene builds its simulation (gravity every step), so changes apply to the next scene that
enters play.

## Related

- [Entities & Components](entities-and-components.md) — the body and collider components
- [Scripting](scripting.md) — the collision and trigger callbacks
- [Architecture](architecture.md) — where physics runs in the frame
