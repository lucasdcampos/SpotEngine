# Animation

Spot plays **skeletal animation** baked into imported models. Bring in a rigged model (an FBX or glTF
with a skeleton) and it arrives with its bones and its animation clips ready to use — play them from a
script or let a default clip run on its own. The goal is Unity-simple: a model, a clip name, and a
"play on start" toggle, with nothing to wire up by hand.

## Rigged models in the scene

Dragging a rigged model into a scene rebuilds the source's node tree as an **entity hierarchy** — one
entity per node — exactly as it does for static models. For a rig that means the **bones show up as
entities** too (`mixamorig:Hips`, `mixamorig:LeftUpLeg`, …), so you can see and select them, parent
things to them, or drive them yourself. Animation simply poses those bone entities. FBX models import
with one clean node per bone (the exporter's helper "pivot" nodes are baked into the bone transforms),
so the skeleton reads clearly and — importantly — matches across exports, which is what lets a clip from
one file drive a model from another without the skeleton drifting.

Two components appear on such a model:

- An **Animator**, on the model's root. It owns the clip list and the playback state (which clip, how
  fast, whether it loops) and, each frame in play mode, writes the sampled pose onto the bone entities.
- A **Skinned Mesh Renderer**, on each skinned mesh part (alongside its Mesh Renderer). It tells the
  render system to draw that part by following the live skeleton rather than its own transform.

You don't add these by hand for an imported model — they come with it.

## Playing clips

There are two ways to start a clip, and they mirror Unity:

- **A default clip.** Set the Animator's *Default Clip* and leave *Play On Start* on, and that clip
  begins as soon as the scene runs. This is the zero-code path.
- **From a script.** Ask the entity for its Animator and tell it to play a clip by name — for example
  `GetComponent<AnimatorComponent>().Play("Run")` — or stop, pause, and resume it. *Speed* and *Loop*
  control the playback.

Animation runs in **play mode** (like scripts and audio). In the editor's edit mode the model rests in
its **bind pose**; press Play (or run the game) to see it move.

## Extra clip files

Animations often ship separately from the model they drive — the classic case is a Mixamo character
plus a folder of downloaded motions. An Animator can therefore reference **extra clip files**: point it
at other animation files and their clips join the model's own, addressable by name. Add these in the
Animator's inspector (or set them from code).

A clip drives the skeleton by **matching bone names**, so any file rigged to the same skeleton just
works — with two conveniences aimed squarely at Mixamo, which makes cross-file animation awkward by
default:

- **Clip names come from the file.** Mixamo names *every* exported clip `mixamo.com`, which is useless
  once several are on one animator. When a clip's own name is empty or that generic placeholder, the
  clip takes its **file name** instead — so `idle.fbx` contributes a clip called `idle`, and a rig's
  own throwaway clip is named after the model. That is the name you pick as the Default Clip or pass to
  play from code.
- **Skeleton namespaces are canonicalized.** Mixamo tags each download's skeleton with a namespace whose
  number varies (`mixamorig:`, `mixamorig5:`, …), so a clip authored against one export targets
  `mixamorig5:Hips` while your model's bone is `mixamorig:Hips`. The engine normalizes that namespace
  when matching, so a clip retargets onto the same skeleton regardless of the number.

## Under the hood

- Cooking a rigged model writes its **skeleton** (each bone's inverse-bind matrix) and its **clips**
  (per-bone position/rotation/scale keyframes) into the same cooked mesh the geometry lives in; see
  [Assets](assets.md). Skinned vertices carry up to four bone influences.
- Each frame the Animator samples the current clip and sets the **local transform** of every bone
  entity it names. Because the bones are ordinary entities, the transform hierarchy does the rest.
- At draw time the render system builds a **bone palette** from the live bone transforms and skins the
  mesh on the GPU. Shadows use the same palette, so an animated model casts an animated shadow.

Everything degrades gracefully: a model with no clips still shows and skins in its bind pose, a bad or
missing clip file is logged and skipped, and a throwing animator is quarantined rather than taking the
frame down.

## Related

- [Rendering](rendering.md) — how meshes (skinned and rigid) are drawn
- [Assets](assets.md) — importing and cooking models, skeletons, and clips
- [Scenes](scenes.md) and [Entities & Components](entities-and-components.md) — the entity hierarchy animation drives
- [Scripting](scripting.md) — driving playback from code
