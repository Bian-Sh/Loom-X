# Design Notes: Providers

- Artifact id: `689f5fad`
- Source HTML path: `.kun-design/ceed315c/689f5fad/v1.html`
- Design notes file: `.kun-design/ceed315c/689f5fad/DESIGN.md`
- Selected frame: 1280 x 800 canvas pixels
- Current version: v1
- Updated: 2026-08-25

## Page role
Providers is the secure upstream configuration workspace for OllamaHub. It helps a developer select a Provider, verify its base URL and protocol modes, check whether a protected API key exists, test reachability, and save a reviewed configuration. The primary action is `新增 Provider`; the secondary action is `测试连接` or returning to Models to inspect associated aliases.

## Visual direction and tokens
The screen shares the Overview and Models system: `--bg #0E141B`, `--surface #151D26`, `--surface2 #1D2833`, `--surface3 #24323E`, `--border #2C3945`, `--text #DCE7EE`, `--muted #91A4B2`, `--accent #16B8C4`, `--info #4D8DFF`, `--ok #35C98A`, `--warn #F0B44D`, and `--danger #F06D73`. It uses a 240px rail, 68px top bar, 16px work-area gap, compact form controls, and restrained translucent panels with solid graphite fallbacks.

## Structure and interactions
- Top-level sections carry `data-ds-section` markers for navigation, page heading, Provider registry, configuration editor, and safety note.
- Native links connect Overview (`../899ef30d/v2.html`), Models (`../7996c71a/v1.html`), and Activity (`../de3c9aba/v1.html`).
- Selecting a Provider updates the editor with its display name, base URL, protocol modes, protected key presence, and related model count.
- `新增 Provider` prepares a new-provider form state. `测试连接` shows a concrete in-progress state followed by a success result; invalid or failed saves expose a separate retryable error state without a backend.
- `保存 Provider 配置` validates the Provider name and base URL, shows a Provider-specific confirmation, and marks the editor clean. Raw API key values are never shown; the form only supports replacing a protected key.
- The protocol chips and key status are text-labelled, not color-only. The `查看相关模型` action routes to the Models page.

## Responsive behavior
- At the 1280 x 800 desktop frame, the Provider registry and configuration editor use a 0.9fr/1.1fr split so the form has enough width for URLs and headers.
- At tablet widths the rail compresses to 72px and the editor stacks below the Provider list.
- Below 620px the rail becomes a top navigation trigger, action buttons wrap, Provider rows become compact, and the form becomes one column with bottom-pinned save/test controls. Long URLs wrap within their fields without causing page-level horizontal scroll.
- The page uses natural vertical scrolling and fluid grid/flex constraints, and remains legible in a 480px-tall embedded preview.

## Accessibility, security, and motion
- Semantic navigation and form labels, visible `:focus-visible` rings, keyboard-selectable Provider rows, `aria-live` feedback, and 40px minimum control heights are included.
- Covered states: populated Provider list, selected Provider, new Provider preparation, missing key, key present, testing, connection success, connection failure, invalid URL/name, and save confirmation.
- The interface never renders a raw API key. Backend wiring must keep key values write-only and outside route state or activity logs.
- `prefers-reduced-motion: reduce` disables transitions and scroll behavior; standard transitions stay short and ease-out.

## Handoff notes
Backend wiring should connect the editor to `settings.json` provider fields, protected API key storage, provider/model resolution, and protocol modes `openai`, `anthropic`, and `ollama`. Preserve explicit test-before-save behavior and the recovery path from failed tests to Activity. Keep labels and routes aligned with Overview, Models, and Activity.
