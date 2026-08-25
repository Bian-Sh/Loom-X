# Design Notes: Activity

- Artifact id: `de3c9aba`
- Source HTML path: `.kun-design/ceed315c/de3c9aba/v2.html`
- Design notes file: `.kun-design/ceed315c/de3c9aba/DESIGN.md`
- Current version: v2 (`.kun-design/ceed315c/de3c9aba/v2.html`)
- Branch: `fix/activity-watermark`
- Updated: 2026-08-25

## Original Brief

继续，同时把设计稿上的水印“the first accepted Design turn requires a Design Profile and target”去掉

## Current User Turn

构建新的分支，先git提交一下，然后你尝试修复这个水印的异常展示

## Selected Context

- [html-screen-frame] Activity - 1280 x 1064 - .kun-design/ceed315c/de3c9aba/v1.html

## Design Context

Design context (honor it in every visual decision):
- Target: Web — default to responsive browser/web-page or web-app layouts; create desktop screen frames around 1280x800 unless the brief asks for another breakpoint.
- Avoid generic AI tells: cream/sand default backgrounds, purple→blue gradients, bounce/elastic easing, nested cards, gray text on colored backgrounds. Verify text contrast and provide a prefers-reduced-motion fallback.


## Visual Direction

- Establish the page layout, hierarchy, color system, typography, spacing, and responsive behavior for this screen.
- Keep visual decisions consistent with root `DESIGN.md` when that valid project theme exists.

## Interaction Notes

- Document important states, inputs, navigation, animation, and accessibility behavior here as the design evolves.

## Handoff Notes

- Keep the HTML file standalone and implementation-ready.
- Note any assumptions or follow-up work that code mode should preserve.

## Watermark diagnosis

The reported text `the first accepted Design turn requires a Design Profile and target` is not present in the Activity HTML, CSS, or visible page copy. It is therefore a Design pipeline/runtime overlay rather than a page layer. This v2 keeps the page artifact clean and standalone; removing the overlay requires fixing the bound Design profile/document target in the renderer, not hiding content with page CSS.
