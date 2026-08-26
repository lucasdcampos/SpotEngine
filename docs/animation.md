# Animation

Spot plays **skeletal animation** baked into imported models. Bring in a rigged model (an FBX or glTF
with a skeleton) and it arrives with its bones and its animation clips ready to use. Which clip plays is
decided in one of two ways: an **Animator Controller** (a reusable state-machine asset) or a **script**
that owns the clips and calls `Play`. The `Animator` component itself stays deliberately small.

## Rigged models in the scene

Dragging a rigged model into a scene rebuilds the source's node tree as an **entity hierarchy** — one
entity per node — exactly as it does for static models. For a rig that means the **bones show up as
entities** too (`mixamorig:Hips`, `mixamorig:LeftUpLeg`, …), so you can see and select them, parent
things to them, or drive them yourself. Animation simply poses those bone entities. FBX models import
with one clean node per bone (the exporter's helper "pivot" nodes are baked into the bone transforms),
so the skeleton reads clearly and — importantly — matches across exports, which is what lets a clip from
one file drive a model from another without the skeleton drifting.

Two components appear on such a model:

- An **Animator**, on the model's root. Each frame in play mode it poses the bone entities. The component
  itself only holds an optional **Controller** reference — it has no clip list or playback settings; a
  controller or a script decides what plays.
- A **Skinned Mesh Renderer**, on each skinned mesh part (alongside its Mesh Renderer). It tells the
  render system to draw that part by following the live skeleton rather than its own transform.

You don't add these by hand for an imported model — they come with it.

## Playing clips from code

Without a controller, a script owns the animation: it decides which clip to play and when. The clips are
referenced in the script, not configured on the Animator. Ask the entity for its Animator and play a clip
by name — the names come from the model's baked clips (see `animator.ClipNames`):

```csharp
public class PlayerAnimations : EntityBehaviour
{
    public string IdleClip = "Idle";
    public string RunClip = "Run";

    private AnimatorComponent _animator = null!;

    public override void OnCreate() => _animator = GetComponent<AnimatorComponent>();

    public override void OnUpdate(float dt)
    {
        _animator.Play(IsMoving ? RunClip : IdleClip);
    }
}
```

`Play(string clipName, bool loop = true)` starts a clip; there is also `Play(AnimationClip clip, …)` for a
script that loaded a clip itself, plus `Stop`, `Pause`, `Resume`, `IsPlaying`, and `CurrentClip`. Playing
the clip that is already playing is a no-op, so calling `Play` every frame is fine.

Animation runs in **play mode** (like scripts and audio). In the editor's edit mode the model rests in
its **bind pose**; press Play (or run the game) to see it move.

## Where clips come from

A clip drives the skeleton by **matching bone names**, so any file rigged to the same skeleton just works.
For the code path, clips come from the model the Animator was instantiated from (its baked clips). For a
controller, **each state remembers the file its clip comes from** — so picking the "idle" clip for an Idle
state is all it takes; the runtime loads that file directly, with no separate source list to maintain. Two
conveniences aimed squarely at Mixamo, which makes cross-file animation awkward by default:

- **Clip names come from the file.** Mixamo names *every* exported clip `mixamo.com`, which is useless
  once several are on one animator. When a clip's own name is empty or that generic placeholder, the
  clip takes its **file name** instead — so `idle.fbx` contributes a clip called `idle`, and a rig's
  own throwaway clip is named after the model. That is the name you pick as the Default Clip or pass to
  play from code.
- **Skeleton namespaces are canonicalized.** Mixamo tags each download's skeleton with a namespace whose
  number varies (`mixamorig:`, `mixamorig5:`, …), so a clip authored against one export targets
  `mixamorig5:Hips` while your model's bone is `mixamorig:Hips`. The engine normalizes that namespace
  when matching, so a clip retargets onto the same skeleton regardless of the number.

## Animator Controllers (state machines)

Playing clips by hand is enough for simple cases, but real characters switch between many animations as
the game changes — idle, walk, run, jump. An **Animator Controller** is an asset that wires those clips
into a **state machine**: named *states* (each a clip), *parameters* you drive from code, and
*transitions* between states gated by conditions on those parameters. It mirrors Unity's Animator
Controller, and it is entirely optional — an Animator with no controller still plays clips the simple way.

A controller is a **reusable asset** (`.sptcontroller`): because its states reference clips **by name**
(not by a specific model), one controller drives any model whose clips share those names. Assign it to an
Animator through the new *Controller* slot; when set, the state machine decides which clip plays and the
old *Default Clip* / `Play` path steps aside.

### Parameters and transitions

A controller has four parameter types, matching Unity:

- **Float** and **Int** — compared with *greater* / *less* (and, for ints, *equals* / *not equals*).
- **Bool** — held true/false and compared for equality.
- **Trigger** — a one-shot flag: you *raise* it, and the first transition that consumes it resets it.

A transition fires when **all** its conditions pass. If it has an **exit time**, it also waits until the
current clip has played past that normalized point (0–1) first. An **Any State** transition can fire from
whatever state is current (handy for a jump or a hit reaction); those are evaluated before a state's own
outgoing transitions. Switching is instant today (the target clip starts from the beginning).

### Driving it from code

Set parameters on the Animator and the controller does the rest:

```csharp
var animator = GetComponent<AnimatorComponent>();
animator.SetFloat("Speed", velocity.Length());  // Idle <-> Run
animator.SetBool("Grounded", isGrounded);
animator.SetTrigger("Jump");                     // fires an Any State -> Jump edge once
string? state = animator.CurrentState;           // the live state name
```

There are matching `GetFloat`/`GetInt`/`GetBool` and `ResetTrigger` calls. All are no-ops when the
Animator has no controller, so the same script works whether or not one is assigned.

### The graph editor

Create a controller from the Asset Browser (*right-click → New Animator Controller*) and **double-click**
it to open the node-graph editor. There you:

- **Right-click the canvas** to add a state; drag nodes to arrange them; middle-drag to pan and scroll to
  zoom. The green **Entry** anchor points at the default state; the **Any State** anchor hosts
  any-state transitions.
- **Right-click a state** to make a transition (then click the target), set it as the default, or delete it.
- Use the side panel to manage **parameters**, edit the selected **state** (name, clip, speed, loop), or
  the selected **transition** (exit time + conditions). A state's **clip** is chosen from a picker listing
  every clip found across the project's models (searchable) — or **drag an animation/model file** onto the
  clip field. Either way the state records both the clip name and the file it comes from, so it resolves at
  runtime with nothing else to configure.

Edits save back to the `.sptcontroller` file automatically once you stop interacting.

## Under the hood

- Cooking a rigged model writes its **skeleton** (each bone's inverse-bind matrix) and its **clips**
  (per-bone position/rotation/scale keyframes) into the same cooked mesh the geometry lives in; see
  [Assets](assets.md). Skinned vertices carry up to four bone influences.
- Each frame the Animator samples the current clip and sets the **local transform** of every bone
  entity it names. Because the bones are ordinary entities, the transform hierarchy does the rest.
- At draw time the render system builds a **bone palette** from the live bone transforms and skins the
  mesh on the GPU. Shadows use the same palette, so an animated model casts an animated shadow.
- An **Animator Controller** is a JSON asset (`.sptcontroller`) holding parameters, states (each with its
  clip name and the file that provides it), and transitions; it cooks pass-through (no heavy dependencies)
  and is referenced by guid like any other asset. At runtime each Animator builds a small state-machine
  evaluator from it, seeds the parameters from their defaults, loads each state's clip file, and every
  frame picks the first satisfied transition — then plays that state's clip through the same sampling path
  as the code path.

Everything degrades gracefully: a model with no clips still shows and skins in its bind pose, a bad or
missing clip file is logged and skipped, and a throwing animator is quarantined rather than taking the
frame down.

## Related

- [Rendering](rendering.md) — how meshes (skinned and rigid) are drawn
- [Assets](assets.md) — importing and cooking models, skeletons, and clips
- [Scenes](scenes.md) and [Entities & Components](entities-and-components.md) — the entity hierarchy animation drives
- [Scripting](scripting.md) — driving playback from code
