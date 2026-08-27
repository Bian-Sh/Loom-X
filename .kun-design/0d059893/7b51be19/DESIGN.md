# Design Notes: Provider 管理

- Artifact id: `7b51be19`
- Source HTML path: `.kun-design/0d059893/7b51be19/v1.html`
- Design notes file: `.kun-design/0d059893/7b51be19/DESIGN.md`
- Current version: v1 (`.kun-design/0d059893/7b51be19/v1.html`)
- Updated: 2026-08-25T22:40:33.483Z

## Original Brief

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/689f5fad/v1.html。完整保留其上游 Provider 管理页面、Provider 注册表、配置编辑器、连接测试与保存交互、安全提示、导航链接、响应式布局、可访问性和 reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Current User Turn

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/689f5fad/v1.html。完整保留其上游 Provider 管理页面、Provider 注册表、配置编辑器、连接测试与保存交互、安全提示、导航链接、响应式布局、可访问性和 reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Selected Context

- [html-screen-frame] Provider 管理 - 1280 x 800 - .kun-design/0d059893/7b51be19/v1.html

## Design Context

Design context (honor it in every visual decision):
- Target: Web — default to responsive browser/web-page or web-app layouts; create desktop screen frames around 1280x800 unless the brief asks for another breakpoint.
- Avoid generic AI tells: cream/sand default backgrounds, purple→blue gradients, bounce/elastic easing, nested cards, gray text on colored backgrounds. Verify text contrast and provide a prefers-reduced-motion fallback.


## Visual Direction

- 完全沿用源文件 `.kun-design/ceed315c/689f5fad/v1.html` 的视觉：暗色控制中心主题（`#0e141b` 背景、`#151d26` 面板、青绿强调 `#16b8c4`），左侧 240px 导航栏 + 顶栏 + 内容区的应用外壳布局。
- 左列 Provider 目录（`270px` 最小列宽）+ 右侧粘性配置编辑器（`.editor{position:sticky}`）；等宽字体用于 base URL，中文全站文案（zh-CN）。
- 响应式：≥981px 双栏工作区，≤980px 折叠为 72px 图标栏且 Provider 目录改三列卡片、编辑器取消粘性，≤620px 隐藏侧栏、☰ 菜单按钮、表单单列。

## Interaction Notes

- 目录项点击切换 `aria-current`，编辑器标题/说明联动更新并 toast（智脑、Anthropic 官方、本地 Ollama 三份不同文案）。
- `测试连接`：条带进入“验证中…”状态，700ms 后变为 success 样式并 toast；保存前校验失败切换 error 样式。
- `保存 Provider`：校验 Provider ID 非空且 base URL 以 http 开头，成功更新标题/说明并 toast。
- `更新 key`：清空密码框、切换到明文输入并提示“不会回显”；`取消修改`、`新增 Provider` 均有 toast 反馈。
- toast 2600ms 自动消失；transition ≤180ms ease-out；`prefers-reduced-motion` 下动画/过渡几乎禁用。
- 原型导航已映射到本文档兄弟页面：概览 `../14386075/v1.html`、网关 `../02a05d60/v1.html`、活动 `../300c4cf3/v1.html`、控制台 `../7ef92d4f/v1.html`；本页自链 `./v1.html`；页首“查看模型”指向网关页。

## Handoff Notes

- 单文件独立 HTML（内联 CSS/JS），无外部资源；可直接在任意宽高 iframe 中预览。
- 未重构、未改版：仅将导航/入口链接改写为本项目原型页面路径，其余内容与源文件逐字一致。
- 焦点可见性（focus-visible 3px 强调色描边）、44px 最小导航/按钮目标、`aria-current`、`role="status"` toast、`aria-describedby` key 提示均已保留。

## Version History

- v1: `.kun-design/0d059893/7b51be19/v1.html` - 加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/689f5fad/v1.html。完整保留其上游 Provider 管理页面、Provider 注册表、配置编辑器、连接测试与保存交互、安全提示、导航链接、响应式布局、可访问性和 reduced-motion 行为。以该文件当前内容作为页面实现来源。
