# OllamaHub Control Center Design Brief

## Brief
OllamaHub Control Center is a responsive web control surface for the OllamaHub cross-platform local gateway. OllamaHub runs across Windows, Linux, and macOS, exposes an Ollama-compatible local HTTP endpoint, and routes configured models to OpenAI-, Anthropic-, or Ollama-compatible upstreams. The web experience is an operational console rather than a marketing site: it should let a developer confirm the gateway is healthy, inspect exposed models and providers, securely manage configuration, and diagnose request failures without editing JSON or reading raw logs first.

The product is for developers using Visual Studio Copilot Chat BYOM, Continue, and other OpenAI/Ollama-compatible clients; advanced users who map several providers to local model aliases; and small engineering teams troubleshooting a shared configuration. The core workflow is: verify the local service, copy the endpoint into a client, configure a provider/model when needed, then inspect activity when a request fails. Security-sensitive changes remain explicit, reviewable, and reversible.

## Concept & audience
The experience is a calm desktop utility presented as a responsive browser interface named "OllamaHub Control Center". It should feel familiar to Windows users who prefer acrylic frosted panels, while remaining coherent on Linux and macOS. The first screen must make the product identity, local endpoint, service state, exposed model count, and next action obvious without explanatory marketing copy.

Primary audience needs:
- Fast confirmation that the local gateway is running and reachable.
- Clear mapping between a public model alias, its provider, and its supported protocol.
- Safe handling of protected API keys and upstream connection tests.
- Useful diagnostic context for protocol conversion, latency, HTTP failures, and recent logs.

## Visual direction
### Mood and material
Use a focused, technical, trustworthy operations-console mood: deep graphite surfaces, crisp cyan-teal accents, restrained semantic colors, and dense but breathable information hierarchy. Express the Windows acrylic preference through selective translucent panels over a dark neutral canvas, with a very subtle cool desktop glow behind the shell. Acrylic means a high-opacity tinted surface, a fine light border, and controlled backdrop blur; it must have a solid graphite fallback when blur is unsupported, transparency is reduced, or contrast would suffer. Do not use a purple-to-blue gradient, cream/sand backgrounds, full-page glassmorphism, decorative blobs, or nested cards.

### Palette intent
Brand accent is electric cyan-teal `#16B8C4`, reserved for active navigation, healthy service state, focus rings, links, and primary actions. Use supporting blue `#4D8DFF` for informational and protocol badges, never as a gradient with the brand accent. Use a real neutral ramp: page background `#0E141B`, primary surface `#151D26`, elevated surface `#1D2833`, border `#2C3945`, primary text `#DCE7EE`, and secondary text `#91A4B2`. Use green `#35C98A` for healthy, amber `#F0B44D` for attention, and red `#F06D73` for errors. Avoid pure black. Verify all text at WCAG AA contrast or better; do not place gray text on colored fills, and do not communicate state through color alone.

### Typography intent
Use one familiar system UI stack: `Inter, Segoe UI, SF Pro Display, Noto Sans, sans-serif`. Use 12px metadata, 14px body and controls with approximately 1.6 line-height, 16px section headings, and 24-30px page headings. Headings are medium or semibold with tighter line-height; body text remains regular and readable. Use a monospace stack only for endpoints, ports, model IDs, status codes, and log excerpts. Keep the hierarchy clear with two to three display sizes rather than oversized hero typography.

### Layout and motion personality
Desktop screen frames default to 1280x800. Use a 240px navigation rail, fixed top status bar, 24px content gutters, 16px panel gaps, an 8px spacing rhythm, and a 12-column content grid in the remaining content area. Prefer operational tables, metric rows, split panes, form sections, and timelines over marketing cards. Panels use mostly 8px corners; dialogs may use 10px. Motion is quiet and functional: 160-220ms ease-out for navigation and panel changes, a restrained status pulse only while connecting, and no bounce, elastic, or overshoot easing. Include a `prefers-reduced-motion: reduce` fallback that removes transitions and pulses while preserving state changes.

## Information architecture
1. **Overview** — The operational home for service health, local endpoint access, model/provider summary, and recent issues.
2. **Models** — The registry for exposed model aliases, capabilities, protocol modes, limits, and model configuration.
3. **Providers** — The secure workspace for upstream URLs, supported protocols, headers, protected key presence, and connection testing.
4. **Activity** — The diagnostic history for requests, conversion paths, latency, status codes, and sanitized log details.

Navigation and route structure:
- `/overview` is the default route and the primary escape path from every screen.
- `/models` is adjacent to Overview for model inventory and links to the owning Provider and related Activity.
- `/providers` is adjacent to Models for upstream configuration and links back to affected models and failed tests in Activity.
- `/activity` is the diagnostic escape path from Overview, Models, and Providers; each event links back to its model or provider context.

## State & responsiveness plan
### Global states
The shell always exposes the current local endpoint, a copy action, and a text status label: Running, Starting, Stopped, or Error. Use inline banners for persistent issues and short toasts for copy, save, and test confirmations. Loading uses skeleton rows and disabled primary actions. Empty states name the next useful action. Errors include a human-readable cause, status code when available, and a recovery action. API keys are always masked and never rendered in logs, URLs, activity rows, or default clipboard output.

### Page states
Overview covers no configuration, healthy service, degraded upstream, offline service, and stale status refresh. Models covers loading, no models, filtered results, selected model details, unsaved edits, validation errors, and successful save. Providers covers no providers, testing, success, upstream failure, missing protected key, invalid URL, save conflict, and unsaved changes. Activity covers no events, loading history, filtered results, selected event detail, log read failure, and sensitive-field redaction.

### Responsive behavior
At 1024px and above, retain the 240px rail, fixed top status bar, split work areas, and 1280x800 desktop composition. From 768px to 1023px, collapse the rail to a 72px icon-first sidebar with accessible tooltips, reduce the grid to eight columns, and stack secondary panels below the primary work area. Below 768px, use a 56px top bar with a menu button and slide-over navigation drawer, single-column sections, full-width controls, horizontally scrollable data tables with identity columns kept visible where practical, and bottom-aligned primary actions in edit flows. Preserve product name, service state, endpoint access, and save/test actions in the first mobile viewport. Never reduce type below 12px; wrap or horizontally scroll long URLs and model IDs inside dedicated code-like fields.

## Implementation notes
- Every route must set a meaningful document title, for example `OllamaHub Control Center — Overview`, `OllamaHub Control Center — Models`, `OllamaHub Control Center — Providers`, or `OllamaHub Control Center — Activity`; never use Untitled, Draft, or a generic page-type title.
- Keep the same navigation order on desktop and mobile: Overview, Models, Providers, Activity. Use semantic HTML, keyboard-visible focus, accessible labels, and ARIA live regions for connection tests and service changes.
- Reflect the current OllamaHub contracts and terminology: local health/version, `/api/tags`, `/api/show`, `/v1/chat/completions`, `/openai/v1/chat/completions`, providers, models, logging level, protected API keys, and protocol modes `openai`, `anthropic`, and `ollama`. The UI must not imply that the browser itself is the proxy process.
- Use structured, reversible forms for provider and model changes. Validate URL, host/port, model ID, protocol mode, context length, and token limits before showing a review step. Provide explicit save, cancel, and test actions.
- Treat secrets as write-only. Show provider identity and whether a protected key exists, support secure replacement, and never reveal or persist raw keys in client-side route state.
- Use familiar icon-library icons for copy, refresh, search, settings, reveal-state, and external endpoint actions; add tooltips for unfamiliar icon-only controls and do not use emoji as icons.
- The Overview first viewport pairs the primary action `Copy local endpoint` or `Start service` with a clearly different secondary action `Open activity`. Every page has a visible route back to Overview and contextual links to adjacent pages.
- Keep acrylic limited to surfaces that need grouping, retain readable separation on the graphite canvas, and provide an opaque fallback through `@supports` and reduced-transparency preferences. Verify contrast and touch targets at desktop, tablet, and mobile breakpoints before screen generation.
