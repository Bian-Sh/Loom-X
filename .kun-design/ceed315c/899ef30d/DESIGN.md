# Design Notes: Overview

- Artifact id: `899ef30d`
- Source HTML path: `.kun-design/ceed315c/899ef30d/v2.html`
- Design notes file: `.kun-design/ceed315c/899ef30d/DESIGN.md`
- Current version: v2
- Updated: 2026-08-25

## Page role
Overview is the primary operational entry for OllamaHub Control Center. It answers four questions in the first viewport: is the local gateway running, where is its endpoint, how many models are exposed, and whether upstream providers are healthy. The dominant action is copying the local endpoint; the distinct secondary action opens Activity for diagnosis.

## Visual direction and tokens
- Neutral palette: `--bg #0E141B`, `--surface #151D26`, `--surface-raised #1D2833`, `--border #2C3945`, `--text #DCE7EE`, `--muted #91A4B2`.
- Brand accent: `--accent #16B8C4`; supporting informational blue: `--info #4D8DFF`.
- Semantic colors: `--ok #35C98A`, `--warn #F0B44D`, `--danger #F06D73`.
- Typography uses a system UI stack with compact 12px metadata, 14px body/control text, 16px section headings, and 28px page heading. Monospace is reserved for endpoint and model identifiers.
- Layout uses a 240px desktop rail, fixed top status bar, 24px content padding, 16px gaps, 8px spacing rhythm, and restrained translucent panels with solid fallbacks.

## Interactions and states
- Copy local endpoint writes `http://127.0.0.1:11434` to the clipboard when available and shows the specific confirmation `本地 endpoint 已复制`; the fallback still shows a feedback toast.
- Refresh status re-confirms the visible service state and shows a concrete response-time feedback message without requiring a backend.
- The service switch toggles between running and stopped, updates the endpoint status label and feedback message, and preserves the page hierarchy.
- Navigation links use native anchors with prototype routes for Overview, Models, Providers, and Activity. The health and incident modules also expose contextual routes.
- The recent incident action opens Activity; model count opens Models; provider health opens Providers. No dead hash links are used.

## Responsive behavior
- Desktop keeps the 240px rail and two-column operational grid at 1280x800.
- Tablet reduces the rail to 72px and allows the endpoint panel and health summary to stack.
- Mobile keeps the navigation trigger in the top bar; the prototype currently exposes a labeled feedback message for the menu action, ready to be replaced by a real drawer when shell routing is wired.
- The page uses fluid `min()`, `clamp()`, grid wrapping, and natural vertical scrolling rather than a fixed viewport wrapper. It remains usable inside a 480px-tall embedded frame.

## Accessibility and motion
- Semantic landmarks, button labels, `aria-live` status feedback, visible `:focus-visible` rings, and non-color status labels are included.
- Motion is limited to a short status pulse and transitions. `prefers-reduced-motion: reduce` disables pulse and transitions.
- Text uses the neutral ramp with contrast intended for WCAG AA; colored status pills include text labels.

## Handoff notes
Keep the shared shell, token names, route paths, endpoint wording, and status vocabulary consistent with Models, Providers, and Activity. The page is standalone and uses only inline CSS and JavaScript. Backend wiring should replace the demo status cycle and clipboard fallback while preserving the existing feedback copy and loading/error recovery surfaces.

## Version history
- v2: responsive, stateful Overview implementation with tokenized palette, navigation coverage, action feedback, and service-state demo controls.
- v1: initial placeholder preview.
