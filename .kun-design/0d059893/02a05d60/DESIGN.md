# Design Notes: 网关 Gateway

- Artifact id: `02a05d60`
- Source HTML path: `.kun-design/0d059893/02a05d60/v1.html`
- Design notes file: `.kun-design/0d059893/02a05d60/DESIGN.md`
- Current version: v1 (`.kun-design/0d059893/02a05d60/v1.html`)
- Updated: 2026-08-25T22:39:28.243Z

## Original Brief

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/7996c71a/v2.html。完整保留其 OllamaHub 网关路由编排页面、Endpoint 协议切换、路由列表、配置表单、保存与测试交互、导航链接、响应式布局、可访问性和 prefers-reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Current User Turn

加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/7996c71a/v2.html。完整保留其 OllamaHub 网关路由编排页面、Endpoint 协议切换、路由列表、配置表单、保存与测试交互、导航链接、响应式布局、可访问性和 prefers-reduced-motion 行为。以该文件当前内容作为页面实现来源。

## Selected Context

- [html-screen-frame] 网关 Gateway - 1280 x 800 - .kun-design/0d059893/02a05d60/v1.html

## Design Context

Design context (honor it in every visual decision):
- Target: Web — default to responsive browser/web-page or web-app layouts; create desktop screen frames around 1280x800 unless the brief asks for another breakpoint.
- Avoid generic AI tells: cream/sand default backgrounds, purple→blue gradients, bounce/elastic easing, nested cards, gray text on colored backgrounds. Verify text contrast and provide a prefers-reduced-motion fallback.


## Visual Direction

- 完全沿用源文件 `.kun-design/ceed315c/7996c71a/v2.html` 的视觉：暗色控制中心主题（`#0e141b` 背景、`#151d26` 面板、青绿强调 `#16b8c4`），240px 左侧导航栏 + 顶栏 + 内容区应用外壳。
- 网关工作区为双栏：左侧 Endpoint 入口 + 可拖动 Model 路由列表，右侧 sticky 的 Provider 路由配置表单（`position:sticky; top:18px`）。
- 等宽字体用于 Endpoint URL 与模型 ID；全站中文文案（zh-CN）。
- 响应式：≤1080px 侧栏折叠为 72px 图标栏、工作区改单栏且配置表单取消 sticky；≤620px 隐藏侧栏、☰ 移动菜单、摘要 2 列、表单操作栏 sticky 底部。

## Interaction Notes

- Endpoint 协议切换：OpenAI Like / Ollama Like / Microsoft Azure Like 三个 tab 切换并更新 URL（`/v1`、`/api`、`/azure/v1`）与 toast。
- 路由列表：每行可拖拽排序（HTML5 drag & drop）、开关启停并同步顶部“已启用路由”计数、行透明度反馈。
- 配置表单：提交校验上下文大小（4,096–1,000,000）与代理 URL（http/https），`撤销修改` 与 `保存网关配置` 均有 toast 反馈。
- 复制地址走 Clipboard API，含失败降级提示。
- 原型导航已映射到兄弟页面：概览 `../14386075/v1.html`、Provider `../7b51be19/v1.html`、活动 `../300c4cf3/v1.html`、控制台 `../7ef92d4f/v1.html`；本页自链 `./v1.html`。
- 原“控制台”入口为占位 toast（data-prototype-target 拦截），已改为真实原型链接并移除对应拦截脚本行。

## Handoff Notes

- 单文件独立 HTML（内联 CSS/JS），无外部资源；可直接在任意宽高 iframe 中预览。
- 未重构、未改版：仅改写导航/入口链接为本项目原型路径，并移除控制台占位拦截脚本；其余内容与源文件逐字一致。
- 可访问性保留：focus-visible 3px 强调色描边、44px 导航目标、tab 语义（role=tab/switch）、aria-describedby 表单提示、role=status toast。

## Version History

- v1: `.kun-design/0d059893/02a05d60/v1.html` - 加载现有 HTML 设计，不重新构思或改版。源文件：.kun-design/ceed315c/7996c71a/v2.html。完整保留其 OllamaHub 网关路由编排页面、Endpoint 协议切换、路由列表、配置表单、保存与测试交互、导航链接、响应式布局、可访问性和 prefers-reduced-motion 行为。以该文件当前内容作为页面实现来源。
