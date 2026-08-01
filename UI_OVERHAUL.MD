Your task is to redesign and polish the UI of the Spot Editor. The goal is NOT to change its workflow or layout, but to make it feel like a modern, professional game engine editor comparable to Unity, Unreal Engine or Godot.

IMPORTANT:
- Preserve the current docking layout.
- Preserve all existing functionality.
- Preserve panel positions (Hierarchy, Asset Browser, Scene/Game views, Inspector, Console).
- Focus entirely on visual polish, spacing, typography, colors and usability.
- The editor should remain simple, clean and intuitive.
- Avoid adding unnecessary complexity or visual clutter.

=========================
GENERAL DESIGN LANGUAGE
=========================

Adopt a modern, minimal, professional dark theme.

The UI should feel like a commercial game engine, not a default Dear ImGui application.

The interface should communicate quality, precision and scalability.

Use subtle contrast instead of strong borders.

Avoid excessive gradients or glossy effects.

=========================
COLOR PALETTE
=========================

Replace the flat gray appearance with a refined dark palette.

Suggested colors (see whats better):

Window Background:
#1B1B1D

Dock Background:
#222326

Panel Background:
#2A2B2F

Header Background:
#313238

Control Background:
#3A3C42

Hovered Control:
#454850

Selected / Accent:
#4D84FF

Borders:
#3A3A3D

Text Primary:
#E8E8E8

Text Secondary:
#B8B8B8

Disabled Text:
#777777

Never use pure black borders.

=========================
TYPOGRAPHY
=========================

Replace the current font with a modern UI font such as:

- Inter
- Segoe UI Variable
- IBM Plex Sans

Use approximately:

14px normal text

15-16px panel titles

Medium (500) weight where appropriate.

Improve letter spacing and readability.

=========================
SPACING
=========================

Increase padding throughout the interface.

Use consistent spacing:

8px outer padding

6px between controls

12px between component groups

Inspector sections should breathe more.

Avoid crowded layouts.

=========================
BORDERS
=========================

Current borders are visually heavy.

Replace them with:

1px subtle borders

Low contrast

Rounded corners only where appropriate (2-4px maximum)

No thick black outlines.

=========================
TABS
=========================

Improve dock tabs.

Selected tab should:

Have a slightly brighter background

Use accent color underline or highlight

Inactive tabs should blend into the dock.

Increase horizontal padding.

=========================
BUTTONS
=========================

Current buttons look like standard operating system buttons.

Replace them with modern flat buttons.

Buttons should have:

Subtle hover state

Subtle pressed state

No bevel effect

Consistent height

Better padding

Toolbar buttons (Move, Rotate, Scale) should look like professional editor tools.

Prefer icon + text if icons are available.

=========================
INSPECTOR
=========================

Inspector is the area that needs the most improvement.

Each component should look like an independent card.

Example:

▼ Transform

--------------------------------

Position

Rotation

Scale

--------------------------------

▼ Mesh Renderer

--------------------------------

Material

Color

--------------------------------

Increase spacing between components.

Use collapsible headers with better styling.

Separate sections visually.

Do not let everything blend together.

=========================
PROPERTY FIELDS
=========================

Improve numeric fields.

XYZ controls should be aligned.

Reduce saturation of X/Y/Z colors.

Use softer versions:

X:
Muted Red

Y:
Muted Green

Z:
Muted Blue

Fields should have:

consistent width

consistent padding

hover state

focus state

=========================
HIERARCHY
=========================

Improve hierarchy readability.

Increase indentation for child entities.

Use modern monochrome icons.

Increase row height slightly.

Improve selected row styling.

Hover rows should have subtle highlighting.

=========================
ASSET BROWSER
=========================

Make folders look cleaner.

Increase icon size.

Improve spacing.

Improve search bar appearance.

Folder icons should be modern and consistent.

=========================
MENU BAR
=========================

Increase height slightly.

Improve spacing between menu items.

Add optional small application logo.

Menus should feel integrated with the editor.

=========================
VIEWPORT
=========================

Improve viewport appearance.

Keep the scene exactly the same, but improve overlays.

Add:

Small orientation gizmo in top-right corner

Viewport label

Camera information

FPS counter

Optional rendering statistics

These overlays should be subtle.

=========================
CONSOLE
=========================

Improve readability.

Different log levels should use subtle colors.

Increase line spacing slightly.

Improve input field appearance.

=========================
ICONS
=========================

Introduce a consistent icon set across the editor.

Hierarchy

Asset Browser

Inspector components

Toolbar

Menus

Use clean monochrome icons.

Avoid colorful or cartoon-like icons.

=========================
VISUAL HIERARCHY
=========================

Differentiate:

Window

Dock

Panel

Header

Controls

Property fields

without relying on strong borders.

Use subtle changes in brightness.

=========================
INTERACTION
=========================

Improve hover feedback.

Improve selected states.

Improve focus indication.

Improve disabled state appearance.

Animations should be extremely subtle (100–150ms).

=========================
DO NOT CHANGE
=========================

Do not redesign the layout.

Do not move windows.

Do not rename features.

Do not add unnecessary panels.

Do not imitate Unreal Engine exactly.

Keep the workflow familiar.

=========================
FINAL GOAL
=========================

The finished editor should immediately feel like a polished, commercial-grade game engine editor.

It should resemble the level of refinement found in Unity, Godot 4 or modern professional development tools, while preserving Spot's own identity through clean design, excellent typography, balanced spacing and subtle visual hierarchy.