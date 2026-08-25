# Design Notes: Console

- Artifact id: `a9213c1e`
- Source HTML path: `.kun-design/ceed315c/a9213c1e/v4.html`
- Design notes file: `.kun-design/ceed315c/a9213c1e/DESIGN.md`
- Selected frame: 1280 x 719 canvas pixels
- Current version: v4
- Screen name: `控制台`
- Updated: 2026-08-25

## Page role
Console is the operational log stream opened from Activity's `控制台` prototype target. It helps OllamaHub operators watch startup, proxy routing, protocol conversion, upstream failures, and local configuration events without leaving the product shell. The primary interaction is filtering and pausing the live stream; secondary actions return to Activity, copy visible logs, or clear the local view.

## Prototype navigation
Activity already exposes `data-prototype-target="控制台"`, and this screen's exact identity is `控制台`, allowing the prototype player to resolve the missing route. The sidebar remains consistent: 概览, 网关, Provider, 活动, 运维 divider, 控制台. Console is active. Native links route to Overview (`../899ef30d/v2.html`), Gateway (`./v3.html`), Provider (`../689f5fad/v1.html`), and Activity (`../de3c9aba/v2.html`).

## Console content
- Summary metrics show retained line count, warning/error counts, redaction state, and stream status.
- Toolbar filters by log level and module, searches request IDs or model names, toggles auto-scroll, and provides refresh.
- Stream rows contain concrete timestamp, level, module, route/model context, status code, latency, and request ID data.
- Error rows expose a direct Activity recovery link.
- Raw API key values are never shown; the page labels the stream as redacted.

## Interactions and states
- Pause/resume changes the stream status without discarding logs.
- Clear removes the local visible stream and shows an explicit empty state.
- Refresh restores demo stream data.
- Search and select filters update visible rows and count.
- Auto-scroll is a labelled checkbox.
- Copy visible logs writes only the currently filtered, redacted entries.
- A simulated live log appends while the stream is active, respecting reduced-motion preferences.

## Visual and responsive direction
The console follows the shared graphite/cyan system but uses a focused monospace log surface for operational scanning. Filters remain conventional controls rather than cards. Desktop keeps the 240px sidebar and wide stream. Tablet compresses the sidebar to 72px. Mobile switches to the top menu, wraps toolbar controls, and allows only the log surface to scroll horizontally. Focus-visible outlines, 40px interaction targets, semantic labels, and `prefers-reduced-motion` are included.

## Handoff notes
Implementation should subscribe to structured logger events rather than scrape text files. Preserve redaction before events reach the browser, cap retained rows, and virtualize large streams. Activity remains the detailed request investigation surface; Console is optimized for live operational observation.
