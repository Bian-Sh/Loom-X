# Design Notes: 控制台 Console

- Artifact id: `7ef92d4f`
- Source HTML path: `.kun-design/0d059893/7ef92d4f/v1.html`
- Design notes file: `.kun-design/0d059893/7ef92d4f/DESIGN.md`
- Current version: v1 (`.kun-design/0d059893/7ef92d4f/v1.html`)
- Updated: 2026-08-25T22:40:33.660Z

## Original Brief

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/a9213c1e/v4.html。完整保留其控制台日志流页面、等级和模块筛选、搜索、暂停/恢复、清空视图、自动滚动、脱敏状态、导航链接、响应式布局、可访问性和 prefers-reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Current User Turn

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/a9213c1e/v4.html。完整保留其控制台日志流页面、等级和模块筛选、搜索、暂停/恢复、清空视图、自动滚动、脱敏状态、导航链接、响应式布局、可访问性和 prefers-reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Selected Context

- [html-screen-frame] 控制台 Console - 1280 x 800 - .kun-design/0d059893/7ef92d4f/v1.html

## Design Context

Design context (honor it in every visual decision):
- Target: Web — default to responsive browser/web-page or web-app layouts; create desktop screen frames around 1280x800 unless the brief asks for another breakpoint.
- Avoid generic AI tells: cream/sand default backgrounds, purple→blue gradients, bounce/elastic easing, nested cards, gray text on colored backgrounds. Verify text contrast and provide a prefers-reduced-motion fallback.


## Visual Direction

- 完全沿用源文件 `.kun-design/ceed315c/a9213c1e/v4.html` 的视觉：暗色控制中心主题（`#0e141b` 背景、`#151d26` 面板、青绿强调 `#16b8c4`），240px 导航栏 + 顶栏（含日志流状态胶囊）+ 工具栏 + 日志控制台的应用外壳。
- 日志区为深色终端风格（`#0b1117`），等宽字体 `ui-monospace`，按时间/等级/模块/消息四列网格渲染；`[redacted]` 以警告色高亮。
- 响应式：≥901px 全导航栏，≤900px 折叠为 72px 图标栏并隐藏模块列，≤620px 隐藏侧栏、出现 ☰ 菜单、控制台自适应高度。

## Interaction Notes

- 等级（Info/Warning/Error）与模块（Gateway/Proxy/Config）双下拉筛选 + 关键字搜索实时过滤日志，底部计数与空状态同步更新。
- `暂停日志`/`继续日志` 切换按钮文案与顶栏状态标签；`清空当前视图` 清空内存中的日志数组（日志文件不受影响，toast 明确说明）。
- `自动滚动` 勾选时每次渲染滚动到底部；`role="log"` + `aria-live="polite"` 播报新日志。
- toast 2200ms 自动消失；transition ≤180ms，`prefers-reduced-motion` 下动画/过渡禁用。
- 原型导航已映射到兄弟页面：概览 `../14386075/v1.html`、网关 `../02a05d60/v1.html`、Provider `../7b51be19/v1.html`、活动 `../300c4cf3/v1.html`；本页自链 `./v1.html`（保留原 `data-prototype-target="控制台"`）。

## Handoff Notes

- 单文件独立 HTML（内联 CSS/JS），无外部资源；控制台内容由 JS `render()` 从 8 条脱敏日志数据生成。
- 未重构、未改版：仅将导航链接改写为本项目原型页面路径，其余内容与源文件逐字一致。
- 焦点可见性（focus-visible 3px 强调色描边）、44px 导航/控件目标、`aria-current="page"`、`role="log"`、`role="status"` toast 均已保留。

## Version History

- v1: `.kun-design/0d059893/7ef92d4f/v1.html` - 加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/a9213c1e/v4.html。完整保留其控制台日志流页面、等级和模块筛选、搜索、暂停/恢复、清空视图、自动滚动、脱敏状态、导航链接、响应式布局、可访问性和 prefers-reduced-motion 行为。以该文件当前内容作为页面实现来源。
