# DESIGN.md: 理解此项目，设计跨 Windows + Linux + macOS

Portable project design guide for Kun, Stitch-style workflows, and code agents.

## Source

- Updated: 2026-08-25T01:09:22.664Z
- Project brief: `.kun-design/ceed315c/design.md`
- Shared token file: `DESIGN.md`
- Origin: Kun design mode

## Product Brief

开干啊，使用白板canvas画图

## Design Context

- Preset: none
- Design context (honor it in every visual decision):
- Target: Web — default to responsive browser/web-page or web-app layouts; create desktop screen frames around 1280x800 unless the brief asks for another breakpoint.
- Avoid generic AI tells: cream/sand default backgrounds, purple→blue gradients, bounce/elastic easing, nested cards, gray text on colored backgrounds. Verify text contrast and provide a prefers-reduced-motion fallback.

## Tokens

See root `DESIGN.md`. Token values are intentionally not duplicated in this generated handoff.

## Components

See root `DESIGN.md` for public component guidance; Kun-native rich component trees remain in internal document sidecars.

## Screens and Prototype Flow

- **Activity** (de3c9aba): HTML `.kun-design/ceed315c/de3c9aba/v1.html`; frame 1280x800; notes `.kun-design/ceed315c/de3c9aba/DESIGN.md`; direction: 开干啊，使用白板canvas画图
  - Overview (899ef30d) via `../899ef30d/v1.html`
- **Providers** (689f5fad): HTML `.kun-design/ceed315c/689f5fad/v1.html`; frame 1280x800; notes `.kun-design/ceed315c/689f5fad/DESIGN.md`; direction: 开干啊，使用白板canvas画图
  - Activity (de3c9aba) via `../de3c9aba/v1.html`
- **Models** (7996c71a): HTML `.kun-design/ceed315c/7996c71a/v1.html`; frame 1280x800; notes `.kun-design/ceed315c/7996c71a/DESIGN.md`; direction: 开干啊，使用白板canvas画图
  - Providers (689f5fad) via `../689f5fad/v1.html`
- **Overview** (899ef30d): HTML `.kun-design/ceed315c/899ef30d/v1.html`; frame 1280x800; notes `.kun-design/ceed315c/899ef30d/DESIGN.md`; direction: 开干啊，使用白板canvas画图
  - Models (7996c71a) via `../7996c71a/v1.html`
- **Logo** (ddfa596f): HTML `.kun-design/ceed315c/ddfa596f/v1.html`; frame 1280x800; notes `.kun-design/ceed315c/ddfa596f/DESIGN.md`; role: logo

## Implementation Guidance

- Read root `DESIGN.md` first and keep UI work aligned with its tokens and component guidance plus the screen flow below.
- Treat each HTML or SVG artifact DESIGN.md as the detailed handoff for states, responsive behavior, animation, and implementation notes.
- Preserve planned prototype hrefs when converting HTML screens into production routes.
- Keep SVG motion declarative and preserve its viewBox, accessibility metadata, and reduced-motion behavior.
