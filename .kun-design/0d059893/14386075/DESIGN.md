# Design Notes: 概览 Overview

- Artifact id: `14386075`
- Source HTML path: `.kun-design/0d059893/14386075/v1.html`
- Design notes file: `.kun-design/0d059893/14386075/DESIGN.md`
- Current version: v1 (`.kun-design/0d059893/14386075/v1.html`)
- Updated: 2026-08-25T22:38:28.355Z

## Original Brief

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/899ef30d/v2.html。完整保留其 OllamaHub Control Center 概览页视觉样式、中文文案、响应式布局、状态演示、复制 endpoint 反馈、导航链接、可访问性和 prefers-reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Current User Turn

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/899ef30d/v2.html。完整保留其 OllamaHub Control Center 概览页视觉样式、中文文案、响应式布局、状态演示、复制 endpoint 反馈、导航链接、可访问性和 prefers-reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Selected Context

- [html-screen-frame] 概览 Overview - 1280 x 800 - .kun-design/0d059893/14386075/v1.html

## Design Context

Design context (honor it in every visual decision):
- Target: Web — default to responsive browser/web-page or web-app layouts; create desktop screen frames around 1280x800 unless the brief asks for another breakpoint.
- Avoid generic AI tells: cream/sand default backgrounds, purple→blue gradients, bounce/elastic easing, nested cards, gray text on colored backgrounds. Verify text contrast and provide a prefers-reduced-motion fallback.


## Visual Direction

- 完全沿用源文件 `.kun-design/ceed315c/899ef30d/v2.html` 的视觉：暗色控制中心主题（`#0e141b` 背景、`#151d26` 面板、青绿强调 `#16b8c4`），左侧 240px 导航栏 + 顶栏 + 内容区的应用外壳布局。
- 字体栈 `"Segoe UI", Inter, system-ui`；等宽字体用于 endpoint 与时间戳；全站中文文案（zh-CN）。
- 响应式：≥901px 全导航栏，≤900px 折叠为 72px 图标栏，≤620px 隐藏侧栏并出现 ☰ 移动菜单按钮、指标 2 列。

## Interaction Notes

- 复制 endpoint：`复制` 与顶栏 `复制本地 endpoint` 调用 Clipboard API 并弹出 toast（含失败降级提示）。
- `刷新状态`：更新最近检查时间与响应毫秒数并 toast。
- `模拟停止服务`：在“运行正常/已停止”间切换，同步顶栏状态胶囊、状态文案与按钮标签。
- 最近活动行可点击/键盘触发（Enter/Space），toast 显示对应事件。
- 移动端菜单按钮 toast 提示导航项；toast 2600ms 自动消失；transition ≤180ms ease-out，`prefers-reduced-motion` 下动画/过渡几乎禁用。
- 原型导航已映射到本文档兄弟页面：网关 `../02a05d60/v1.html`、Provider `../7b51be19/v1.html`、活动 `../300c4cf3/v1.html`、控制台 `../7ef92d4f/v1.html`；本页自链 `./v1.html`。

## Handoff Notes

- 单文件独立 HTML（内联 CSS/JS），无外部资源；可直接在任意宽高 iframe 中预览。
- 未重构、未改版：仅将导航/入口链接改写为本项目原型页面路径，其余内容与源文件逐字一致。
- 焦点可见性（focus-visible 3px 强调色描边）、44px 最小导航目标、`aria-current="page"`、`role="status"` toast 均已保留。

## Version History

- v1: `.kun-design/0d059893/14386075/v1.html` - 加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/899ef30d/v2.html。完整保留其 OllamaHub Control Center 概览页视觉样式、中文文案、响应式布局、状态演示、复制 endpoint 反馈、导航链接、可访问性和 prefers-reduced-motion 行为。以该文件当前内容作为页面实现来源。
