# Input

Spot exposes input as **polled state**: instead of subscribing to events, you ask "is this down?"
from a script's update. (Discrete, event-driven input is still available by overriding a scene's
event hook — see [Scenes](scenes.md).) There are two ways to ask, and you can freely mix them.

## Reading keys directly

The low-level path queries a physical key or mouse button by name — the same as it has always
worked:

- **Held / pressed / released** for keyboard keys and mouse buttons (the "down this frame" and "up
  this frame" variants fire for a single frame, so they're right for one-shot actions like a jump).
- **Mouse position** and **scroll delta** for the current frame.
- **Cursor lock**, which hides and captures the cursor for mouse-look. The engine transparently
  frees the cursor while it owns input (see [Capture](#capture-and-the-console) below) and restores
  your requested state afterward.

This is the most direct option and is perfect for prototypes and editor tooling. For shipping game
code, prefer **actions**, so the keys aren't hard-coded.

## Actions: binding names to keys

An **action** is a string name — `"forward"`, `"jump"`, `"fire"` — mapped to one or more physical
inputs that trigger it. `"forward"` might be bound to both **W** and the **Up arrow**; `"fire"` to
the **left mouse button**. Your code then asks about the action, never the key:

- `GetAction("forward")` — true while any bound input is held.
- `GetActionDown("forward")` — true on the frame the action becomes active.
- `GetActionUp("forward")` — true on the frame the action becomes inactive.

The down/up edges are about the action as a whole: pressing a second bound key while the action is
already active does not re-fire "down", and releasing one bound key while another is still held does
not fire "up". Action names are matched case-insensitively.

**The game owns the action names.** The engine ships only the machinery to bind, unbind, query, and
persist-through-defaults; which names exist and what they mean is entirely up to your project.

## Default bindings

A project declares its starting bindings on its **application spec** (the same place it sets the
window title and start scene, in your game's entry point). These defaults are applied once at
startup, so they work identically in the editor's play mode and in a shipped build. Conceptually:

```
forward -> W, Up
back    -> S, Down
jump    -> Space
fire    -> left mouse button
```

Because defaults live in code on the spec, they travel with the build automatically — there's no
separate bindings file to ship. The defaults are also remembered as a snapshot so a player's runtime
rebinds can be reverted (see `resetbinds` below).

## Changing bindings at runtime

Bindings can be changed live, both from code and from the developer console:

- **From code** — bind a key/button to an action, unbind a single input (removing it from every
  action), remove a whole action, or reset everything back to the project defaults.
- **From the console** — the engine registers these commands by default:

  | Command | Effect |
  |---|---|
  | `bind <key> <action>` | Bind a key/button to an action, e.g. `bind w forward`. |
  | `unbind <key>` | Remove a key/button from every action, e.g. `unbind w`. |
  | `unbind <action>` | Remove a whole action by name, e.g. `unbind forward`. |
  | `bindings` | List every action and the inputs bound to it. |
  | `resetbinds` | Restore the project's default bindings. |

  `unbind` figures out which you meant: if the argument names a key or button it unbinds that input,
  otherwise it treats the argument as an action name.

### Binding tokens

Keys and buttons are written as short tokens, case-insensitive:

- **Letters and digits**: `a`–`z`, `0`–`9`.
- **Named keys**: `space`, `enter` (`return`), `escape` (`esc`), `tab`, `backspace`, `delete`
  (`del`), `up`/`down`/`left`/`right`, `f1`–`f12`, `leftshift`/`shift`, `leftcontrol`/`ctrl`, `alt`,
  and the right-hand variants (`rshift`, `rctrl`, `ralt`), plus punctuation like `,` `.` `/` `-` `'`.
- **Mouse buttons**: `mouse0`/`lmb`, `mouse1`/`rmb`, `mouse2`/`mmb`, `mouse3`, `mouse4`.

## Capture and the console

While the developer console (opened with the `'` key) is on screen, the **engine owns input**: the
cursor is forced free and both direct and action queries report nothing to the game, so a scene never
reacts to keys you type into the console. Control returns to the game the moment the console closes.

## Not yet

- **Gamepads and analog axes** aren't supported — input is keyboard and mouse, digital.
- **Player rebinds aren't persisted to disk**; runtime changes last for the session, and defaults
  come from the project's code.

## Related

- [Scripting](scripting.md) — where most input is read, from a script's update
- [Scenes](scenes.md) — the event hook for discrete, event-driven input
- [Projects & Building a Game](projects-and-building.md) — where a project's spec (and its defaults) lives
