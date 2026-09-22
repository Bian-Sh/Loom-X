# OllamaHub Visual Design Brief

## Product

OllamaHub is a professional desktop application for managing and observing local and remote AI providers, gateways, routing, activity, console output, and service status.

The application must feel like a polished native desktop product rather than a web dashboard.

---

# Visual Direction

Primary inspiration:

- Apple visionOS
- Apple Liquid Glass
- macOS
- Apple system applications
- Windows Fluent
- Linear
- Raycast

Keywords:

> Milky / Airy / Translucent / Calm / Precise / Spatial / Native / Premium

The interface should feel:

- clean
- quiet
- soft
- lightweight
- highly aligned
- information-dense without feeling crowded
- translucent without becoming visually noisy

Avoid the generic AI-generated SaaS aesthetic.

---

# Material Language

Use a layered material system.

Do NOT make every surface glass.

Use translucent materials primarily for:

- navigation
- toolbars
- floating controls
- overlays
- popovers
- selected interactive elements

Use standard surfaces for:

- tables
- forms
- logs
- console
- dense content
- charts

Glass should communicate hierarchy and depth, not decoration.

The visual language should resemble visionOS-inspired translucent materials while remaining appropriate for a desktop application.

---

# Color

Primary background:

soft milky white / very light neutral.

Avoid pure white everywhere.

Use subtle warm-neutral and cool-neutral variation.

Text:

- primary: near-black
- secondary: muted gray
- tertiary: soft gray

Accent:

- restrained
- desaturated
- used only for interaction and semantic emphasis

Avoid saturated blue UI.

Avoid gradients unless they provide actual depth or material separation.

---

# Typography

Typography must establish a clear hierarchy.

Use a restrained desktop typography scale.

Avoid:

- oversized headings
- excessive font weights
- arbitrary font sizes

All typography must come from centralized design tokens.

---

# Spacing

Use a strict spacing scale.

Preferred:

4 / 8 / 12 / 16 / 20 / 24 / 32

Do not introduce arbitrary values such as:

7 / 9 / 13 / 17 / 19

unless there is a documented reason.

---

# Radius

Use a small, controlled radius scale.

Avoid excessive pill-shaped controls.

Rounded corners should communicate grouping and material boundaries.

They must not become decorative.

---

# Iconography

Approved icon libraries:

1. Phosphor Icons — primary
2. Tabler Icons — secondary
3. Fluent UI System Icons — platform/system semantics
4. Heroicons — fallback

All are permissively licensed; MIT-licensed libraries should be preferred.

Prefer one icon family within the same visual context.

Do not mix icon families arbitrarily.

Icon geometry, optical size, stroke weight, and alignment must remain consistent.

Never use Unicode characters as UI icons.

Never use emoji as UI icons.

Never use text glyphs such as "O", "→", "✓", "⚙" as substitutes for real icons.

---

# Component System

Create centralized reusable components for:

- NavigationItem
- IconButton
- Button
- GlassButton
- TextButton
- SearchBox
- TextField
- ComboBox
- Toggle
- CheckBox
- GlassPanel
- GlassCard
- StatusBadge
- Dialog
- Popover
- Tooltip
- TreeItem
- ListItem
- DataGrid
- EmptyState
- LoadingState
- ErrorState

Do not implement one-off visual styles inside individual pages.

---

# Layout

All major layouts must use explicit alignment rules.

Check:

- horizontal alignment
- vertical alignment
- baseline alignment
- consistent padding
- consistent control height
- consistent icon alignment
- consistent content density

A visually correct layout is more important than preserving the current markup structure.

Refactor markup when necessary.

---

# Visual QA

The application has access to a CUA Driver capable of:

- launching/interacting with the application
- clicking controls
- navigating pages
- resizing windows
- capturing screenshots

After every major UI refactoring:

1. Build the application.
2. Launch the application.
3. Navigate through every major page.
4. Capture screenshots.
5. Visually inspect every screenshot.
6. Identify alignment, spacing, typography, iconography, hierarchy, material, and contrast problems.
7. Fix systemic problems at the Design System level.
8. Repeat the visual inspection.

Do not stop after the first successful build.

A successful build does not mean the UI is finished.

---

# Critical Rule

Never solve a global visual problem with a page-specific hack.

If the same visual problem appears in multiple places:

fix the shared Design Token, Theme, or Component.

The final application should have one coherent visual language.

---

# Definition of Done

The UI is considered complete only when:

- all pages share one visual language
- all icons are consistent
- all controls share consistent dimensions
- spacing is systematic
- typography is systematic
- glass materials are consistent
- navigation is visually balanced
- selected/hover/focus states are coherent
- there are no obvious alignment errors
- there are no arbitrary colors
- there are no arbitrary spacing values
- there are no Unicode/emoji placeholder icons
- resizing does not break layouts
- CUA screenshot inspection finds no high or medium severity visual defects

Do not ask the user to identify individual visual problems.

The purpose of this workflow is for the agent to discover and fix them autonomously.