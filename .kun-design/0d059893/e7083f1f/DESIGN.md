# Design Notes: Logo 展示

- Artifact id: `e7083f1f`
- Source HTML path: `.kun-design/0d059893/e7083f1f/v1.html`
- Design notes file: `.kun-design/0d059893/e7083f1f/DESIGN.md`
- Current version: v1 (`.kun-design/0d059893/e7083f1f/v1.html`)
- Updated: 2026-08-25T22:43:10.558Z

## Original Brief

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/ddfa596f/v2.html。完整保留其 OllamaHub Bridge Mark 品牌标志展示、主标志、紧凑横向组合和单色版本、响应式布局、SVG 可访问性与 reduced-motion 支持。以该文件当前内容作为页面实现来源。

## Current User Turn

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/ddfa596f/v2.html。完整保留其 OllamaHub Bridge Mark 品牌标志展示、主标志、紧凑横向组合和单色版本、响应式布局、SVG 可访问性与 reduced-motion 支持。以该文件当前内容作为页面实现来源。

## Selected Context

- [html-screen-frame] Logo 展示 - 1280 x 800 - .kun-design/0d059893/e7083f1f/v1.html

## Design Context

Design context (honor it in every visual decision):
- Target: Web — default to responsive browser/web-page or web-app layouts; create desktop screen frames around 1280x800 unless the brief asks for another breakpoint.
- Avoid generic AI tells: cream/sand default backgrounds, purple→blue gradients, bounce/elastic easing, nested cards, gray text on colored backgrounds. Verify text contrast and provide a prefers-reduced-motion fallback.


## Visual Direction

- 完全沿用源文件 `.kun-design/ceed315c/ddfa596f/v2.html` 的视觉：暗色品牌展示页（`#0e141b` 背景），居中大卡片（`.tile`）内含 Bridge Mark 主标志、`Ollama<span>Hub</span>` 字标（青绿强调 `#16b8c4`）与等宽 tagline。
- 标志为内联 SVG（viewBox 180×120）：桥接核心矩形 + 上下左右四个协议端口（青绿描边、蓝色内线），语义由 `role="img"` + `<title>/<desc>` 描述。
- 字体栈 `"Segoe UI", system-ui`；等宽字体用于 tagline。响应式：≤620px 时主标志纵向堆叠居中、变体单列。

## Interaction Notes

- 无导航栏与应用外壳：本页为独立品牌标志展示（非 OllamaHub Control Center 应用页面），与源文件一致。
- 无 JS 交互；`focus-visible` 描边为链接/按钮预留。
- `prefers-reduced-motion` 下禁用全部 transition/animation（本页本身无动画，为降级兜底）。

## Handoff Notes

- 单文件独立 HTML（内联 CSS/SVG），无外部资源；可直接在任意宽高 iframe 中预览。
- 与源文件逐字一致，未做任何改动（无导航链接需要映射）。
- SVG 可访问性：主标志 `aria-labelledby` 关联 title/desc；两个变体 `aria-hidden="true"` 由相邻文本标签表达；`.mono` 变体以 `currentColor` 实现单色版本。

## Version History

- v1: `.kun-design/0d059893/e7083f1f/v1.html` - 加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/ddfa596f/v2.html。完整保留其 OllamaHub Bridge Mark 品牌标志展示、主标志、紧凑横向组合和单色版本、响应式布局、SVG 可访问性与 reduced-motion 支持。以该文件当前内容作为页面实现来源。
