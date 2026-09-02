# Physics

Spot simulates both 2D and 3D physics. Physics runs only in **play mode** — while a scene's runtime
is being updated — so editing a scene never moves things around.

## The model

You make an entity physical by giving it two kinds of component:

- A **body** — a Physics Body (2D or 3D) that says the entity participates in the simulation and
  whether it's dynamic (moved by forces and gravity) or static.
- A **collider** — a shape used for collision. In 3D that's a box, sphere, or capsule; in 2D, a box or circle.

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

## 2D backend

2D physics runs on **Aether.Physics2D**, a full rigid-body simulation (a managed Box2D descendant),
behind the same kind of internal interface as 3D. Box and circle colliders become real bodies with
mass, friction, restitution, and rotation; collisions and triggers are reported, and you can raycast
the XY plane. As with 3D, if the backend fails to initialize the engine falls back to the built-in AABB
solver so play mode never dies, and the simulation is built lazily on play and torn down on exit.

Aether is metric (MKS), so keep 2D collider sizes in a sane range (roughly 0.1–10 units) for a stable,
well-behaved solve — the same advice as any Box2D-family engine. The Sandbox's **Physics 2D** demo is a
playground for it (stacking boxes, bouncing balls, and a jumping character).

## Collisions and triggers

The simulation reports contacts, and the engine dispatches them to the scripts on the entities
involved. Scripts receive **collision** enter/stay/exit callbacks for solid contacts (with the other
entity and a contact normal/point) and **trigger** enter/stay/exit callbacks for overlaps. See
[Scripting](scripting.md).

## Raycasting

You can cast a ray into a scene's 3D simulation and get the closest hit within a distance — useful
for shooting, line-of-sight checks, ground probes, and mouse picking. The 2D simulation offers the
same query in the XY plane (`Scene.Raycast2D`), handy for grounded checks and cursor picking. Raycasts
are meaningful only while the simulation is live (play mode).

## Global settings and collision layers

Like rendering, physics is tuned through a **single global settings surface** rather than per-scene
wiring: the backend to use (`Backend` for 3D, `Backend2D` for 2D) and world **gravity** (`Gravity`,
`Gravity2D`). It also defines a **collision layer matrix**, shared by both dimensions — each collider
belongs to a layer, and you can enable or disable collisions between any pair of layers (symmetrically)
so, for example, projectiles pass through each other but hit walls. Settings are read when a scene
builds its simulation (gravity every step), so changes apply to the next scene that enters play.

## Related

- [Entities & Components](entities-and-components.md) — the body and collider components
- [Scripting](scripting.md) — the collision and trigger callbacks
- [Architecture](architecture.md) — where physics runs in the frame
