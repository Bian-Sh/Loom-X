# Design Notes: 请求活动 Activity

- Artifact id: `300c4cf3`
- Source HTML path: `.kun-design/0d059893/300c4cf3/v1.html`
- Design notes file: `.kun-design/0d059893/300c4cf3/DESIGN.md`
- Current version: v1 (`.kun-design/0d059893/300c4cf3/v1.html`)
- Updated: 2026-08-25T22:40:33.605Z

## Original Brief

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/de3c9aba/v2.html。完整保留其请求活动页面、筛选与刷新交互、请求诊断信息、脱敏上下文、导航链接、响应式布局、可访问性和 reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Current User Turn

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/de3c9aba/v2.html。完整保留其请求活动页面、筛选与刷新交互、请求诊断信息、脱敏上下文、导航链接、响应式布局、可访问性和 reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Selected Context

- [html-screen-frame] 请求活动 Activity - 1280 x 800 - .kun-design/0d059893/300c4cf3/v1.html

## Design Context

Design context (honor it in every visual decision):
- Target: Web — default to responsive browser/web-page or web-app layouts; create desktop screen frames around 1280x800 unless the brief asks for another breakpoint.
- Avoid generic AI tells: cream/sand default backgrounds, purple→blue gradients, bounce/elastic easing, nested cards, gray text on colored backgrounds. Verify text contrast and provide a prefers-reduced-motion fallback.


## Visual Direction

- 完全沿用源文件 `.kun-design/ceed315c/de3c9aba/v2.html` 的视觉：暗色控制中心主题（`#0e141b` 背景、`#151d26` 面板、青绿强调 `#16b8c4`），左侧 240px 导航栏 + 顶栏 + 内容区外壳。
- 字体栈 `"Segoe UI", Inter, system-ui`；等宽字体用于时间、request ID 与路由；全站中文文案（zh-CN）。
- 响应式：≥1051px 双栏（活动表 + 详情面板），≤1050px 折叠为图标栏并单栏堆叠，≤620px 隐藏侧栏并出现 ☰ 移动菜单按钮、指标 2 列、表格横向滚动。

## Interaction Notes

- 行筛选：搜索框（模型/Provider/request ID）+ 状态筛选 + 入口协议筛选实时过滤，同步“显示 N 条活动”，空结果时展示空状态文案；`清除筛选` 重置三项。
- 行选择：点击或 Enter/Space 键盘激活，右侧详情面板联动（标题、request ID、状态徽章、入口/转换路径、延迟、脱敏诊断摘要），选中行高亮。
- `复制 request ID` 调 Clipboard API 并 toast（含失败降级）；`刷新活动` toast 提示。
- toast 2600ms 自动消失；transition ≤180ms ease-out；`prefers-reduced-motion` 下动画/过渡几乎禁用。
- 原型导航已映射到本文档兄弟页面：概览 `../14386075/v1.html`、网关 `../02a05d60/v1.html`、Provider `../7b51be19/v1.html`、控制台 `../7ef92d4f/v1.html`；详情面板“查看模型/查看 Provider”分别指向网关与 Provider 页；本页自链 `./v1.html`。

## Handoff Notes

- 单文件独立 HTML（内联 CSS/JS），无外部资源；可直接在任意宽高 iframe 中预览。
- 未重构、未改版：仅将导航/入口链接改写为本项目原型页面路径，其余内容与源文件逐字一致（含 8 条脱敏请求样例数据）。
- 焦点可见性（focus-visible 3px 强调色描边）、44px 最小导航目标、`aria-selected` 行状态、`role="search"`、`role="status"` toast 均已保留。

## Version History

- v1: `.kun-design/0d059893/300c4cf3/v1.html` - 加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/de3c9aba/v2.html。完整保留其请求活动页面、筛选与刷新交互、请求诊断信息、脱敏上下文、导航链接、响应式布局、可访问性和 reduced-motion 行为。以该文件当前内容作为页面实现来源。
