# Design Notes: Gateway

- Artifact id: `7996c71a`
- Source HTML path: `.kun-design/ceed315c/7996c71a/v2.html`
- Design notes file: `.kun-design/ceed315c/7996c71a/DESIGN.md`
- Selected frame: 1280 x 962 canvas pixels
- Current version: v2
- Updated: 2026-08-25

## Page role
This screen is now the OllamaHub Gateway workspace. The former “模型” area is intentionally renamed “网关”: it defines which Ollama Endpoint exposes which Provider and upstream Model. The primary action is saving a gateway route; secondary actions are switching Endpoint protocol, testing the route, and opening the related Provider or Console view.

## Information architecture and navigation
- Overview remains the service-health landing route.
- 网关 is the route-mapping workspace and replaces the former model registry label.
- Provider remains the upstream credential and provider configuration workspace.
- 活动 remains request diagnostics.
- 控制台 is a new navigation item directly below 活动 for operational logs and local service output. It is represented as a route target in the shell and can be wired to the Console screen when generated.

## Visual direction and tokens
The page continues the shared OllamaHub control-center tokens: `--bg #0E141B`, `--surface #151D26`, `--surface2 #1D2833`, `--surface3 #24323E`, `--border #2C3945`, `--text #DCE7EE`, `--muted #91A4B2`, `--accent #16B8C4`, `--info #4D8DFF`, `--ok #35C98A`, `--warn #F0B44D`, and `--danger #F06D73`. Product panels use restrained 8px radii and bounded system typography. The composition is inspired by high-quality agent/configuration workspaces: dense but calm, with a route list on the left and explicit configuration details on the right.

## Gateway capabilities
- Endpoint protocol tabs: OpenAI Like, Ollama Like, and Microsoft Azure Like. Each tab updates the endpoint path and explanatory helper copy.
- Route rows map Ollama Endpoint aliases to a concrete Provider and Model, including upstream model ID, context window, and enabled/disabled state.
- Routes support drag-and-drop reorder in the prototype and explicit 上移/下移 controls for keyboard/touch-safe reordering.
- Enable/Disable toggles are text-labelled and update the visible route state and feedback toast.
- Configuration fields include endpoint alias, Provider, Model, model context size, independent proxy URL, custom request headers, and a test-route action.
- Save validation requires endpoint alias, provider, model, and a positive context size. Headers remain editable text and are shown as implementation examples, never secret values.

## Responsive behavior
At 1280x962 the screen uses a 240px navigation rail, endpoint tabs, a route list and a sticky configuration panel. Tablet widths compress the rail and stack the route list above configuration. Mobile widths use a top navigation trigger, full-width endpoint tabs and fields, compact route rows, and touch-sized reorder/enable controls. The route list naturally scrolls vertically; no fixed-height viewport wrapper or page-level horizontal overflow is used.

## Accessibility, state, and handoff
Major sections carry `data-ds-section` markers. The screen includes visible focus rings, labelled fields, helper text, validation feedback, route selection, route reorder feedback, enabled/disabled state, test success, and save confirmation. Reduced-motion preferences disable transitions. Prototype routes connect Overview, Provider, Activity, and the new Console target. Backend wiring should persist endpoint aliases and mappings to the project configuration model, expose separate endpoint protocol adapters, resolve Provider/Model ownership, and keep proxy headers and credentials within their intended scope.
