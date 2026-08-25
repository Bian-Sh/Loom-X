# Design Notes: Gateway 3

- Artifact id: `5b7ec717`
- Source HTML path: `.kun-design/ceed315c/5b7ec717/v1.html`
- Design notes file: `.kun-design/ceed315c/5b7ec717/DESIGN.md`
- Selected frame: 1280 x 800 canvas pixels
- Current version: v1
- Updated: 2026-08-25

## Page role
Gateway 3 is the OllamaHub endpoint routing workspace. It is for developers who need to decide which Provider and Model each local protocol endpoint exposes. The primary goal is independent, reviewable route management for OpenAI, Ollama, and Azure endpoints; every change is saved immediately and confirmed inline.

## Unified shell and navigation
The sidebar matches the existing product shell: 概览, 网关, Provider, 活动, 运维 divider, 控制台. Gateway is active. Native links connect Overview (`../899ef30d/v2.html`), Provider (`../689f5fad/v1.html`), Activity (`../de3c9aba/v2.html`), and Console (`../a9213c1e/v4.html`).

## Layout and ownership
- Desktop uses a 240px navigation rail, a compact title/summary area, and a two-column endpoint/model workspace.
- The left column is an exclusive Endpoint selector with independent Enable/Disable switches and copyable endpoint addresses.
- The right column is a dedicated ReorderableList for the selected endpoint. Its title, helper copy, route count, and model data update whenever the endpoint selection changes.
- OpenAI, Ollama, and Azure each own separate model arrays; models never appear as a global shared list.
- The model list provides add, delete, reorder, and enable/disable controls directly. Adding and deleting never require a Provider redirect.

## Visual direction and tokens
Use graphite `#0E141B`, surfaces `#151D26/#1D2833/#24323E`, border `#2C3945`, primary text `#DCE7EE`, muted text `#9BAFBC`, cyan `#16B8C4`, blue `#6DA1FF`, green `#35C98A`, amber `#F0B44D`, and red `#F47F84`. Keep panel radii at 8px, maintain visible focus rings, and use the existing acrylic-like topbar only as a restrained surface material. The page title and helper text use bounded sizes and zero letter spacing.

## States and interactions
- Endpoint selected state uses border, tint, and active navigation treatment.
- Endpoint switch changes listening state and writes a specific saved feedback message.
- URL fields are keyboard-accessible copy targets.
- Model rows show concrete Provider names, model IDs, context sizes, and priority numbers.
- Dragging updates order and reports that the new order was saved.
- The plus action opens an inline Provider/Model picker scoped to the current endpoint; the hover/focus tooltip explains its purpose.
- Delete removes only the current endpoint's route and reports the endpoint name.
- Azure demonstrates the empty route state with a direct add action.

## Responsive behavior and handoff
At desktop the endpoint selector and model list align to the same height. Tablet compresses the rail and places the three endpoint cards in a row above the model list. Mobile stacks the endpoint cards and model list, keeps routes naturally scrollable, and preserves 40px touch targets. Persist each endpoint's enabled state and route array independently; backend integration should keep add/delete/reorder operations endpoint-scoped and save them immediately.
