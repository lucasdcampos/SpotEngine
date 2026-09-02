# Audio

Spot plays sound through a pluggable audio backend — **OpenAL** on desktop and the **Web Audio API**
in the browser — with positional (3D) audio driven by the same entity/component model as everything
else. The backend is a thin platform seam (`IAudioBackend`) behind the mixer; everything above it
(sources, listener, clips, the audio system) is backend-agnostic and identical across targets.

## Sources and the listener

Audio is built from two components:

- An **Audio Source** plays a sound clip. Attach it to an entity and it can play, loop, and be
  positioned in the world; because it lives on an entity, a source attached to a moving object moves
  with it.
- An **Audio Listener** is the "ears" of the scene — usually on the camera or the player. Its
  position and orientation are where the world is heard from.

With a listener present, sources are **spatialized**: sounds are panned and attenuated by their
position relative to the listener, so things sound like they come from where they are. A source can
also play non-positionally for music and UI sounds.

## Clips and playback

Sound clips come from imported audio assets (see [Assets](assets.md)) and are decoded for playback.
The **audio system** runs as part of a scene's play-mode update, keeping sources and the listener in
sync with their entities' transforms each frame. Audio runs on the engine's real clock, so pausing or
slow-motion gameplay doesn't distort or starve playback.

Global audio behavior (such as master volume) is exposed through a settings surface, matching the
engine's convention of a single global knob-set per system.

## In the browser

The browser build maps the same source/buffer/listener operations onto the Web Audio API over one
`AudioContext` (a `WebAudioBackend` mirroring the desktop OpenAL one). Positional audio is fully
supported: spatial sources route through a `PannerNode` and attenuate with distance, non-spatial
sources (music, UI) play flat, and the listener drives the `AudioContext` listener.

Browsers block audio until the player interacts with the page (the autoplay policy), so sound stays
silent until the first click or key press, which unlocks the context automatically. If a browser has
no Web Audio support the mixer degrades to silence, exactly as it does when no audio device is
present on desktop — the game keeps running either way.

## Related

- [Entities & Components](entities-and-components.md) — the audio source and listener components
- [Assets](assets.md) — how audio files become playable clips
