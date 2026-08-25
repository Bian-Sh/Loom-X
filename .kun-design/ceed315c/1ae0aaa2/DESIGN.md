# Design Notes: Gateway

- Artifact id: `1ae0aaa2`
- Source HTML path: `.kun-design/ceed315c/1ae0aaa2/v1.html`
- Design notes file: `.kun-design/ceed315c/1ae0aaa2/DESIGN.md`
- Current version: v1 (`.kun-design/ceed315c/1ae0aaa2/v1.html`)
- Updated: 2026-08-25T03:07:41.183Z

## Original Brief

OllamaHub Control Center 网关页面。沿用现有深石墨背景、青绿色品牌色、蓝色信息色、浅灰高对比文本、8px 面板圆角和 240px desktop sidebar。左侧 sidebar 在所有页面统一，顺序为：概览、网关、Provider、活动、控制台；当前激活网关。页面目标是把 Ollama Endpoint 路由到 Provider 与 Model。主内容展示三个纵向堆叠的 Endpoint 配置区，直接标为 OpenAI、Ollama、Azure。每个 Endpoint 区域内部使用左右 split：左侧显示 endpoint 地址、监听状态、复制地址和路由说明；右侧显示该 Endpoint 当前绑定的模型列表，模型行展示 Provider、模型名、上下文大小、优先级、Enable/Disable 和拖拽排序。不要在本页展示 Selected Router 面板；路由深层编辑通过 Provider 页面下一级进入。页面首屏包含 H1 网关路由编排、保存网关配置主按钮、Endpoint 总数与启用路由上下文。包含空、禁用、保存成功、拖拽排序和 endpoint 未监听状态。响应式：1280x800 desktop 使用 240px sidebar 与三段纵向配置；tablet sidebar 收窄，mobile 改为顶部菜单，Endpoint 内部左右 split 改为上下堆叠，模型列表自然滚动。使用语义区块、清晰焦点态、40px 触控目标和 prefers-reduced-motion。导航链接到 Overview、Provider、Activity，并将 Console 作为 sidebar 目标。

## Current User Turn

OllamaHub Control Center 网关页面。沿用现有深石墨背景、青绿色品牌色、蓝色信息色、浅灰高对比文本、8px 面板圆角和 240px desktop sidebar。左侧 sidebar 在所有页面统一，顺序为：概览、网关、Provider、活动、控制台；当前激活网关。页面目标是把 Ollama Endpoint 路由到 Provider 与 Model。主内容展示三个纵向堆叠的 Endpoint 配置区，直接标为 OpenAI、Ollama、Azure。每个 Endpoint 区域内部使用左右 split：左侧显示 endpoint 地址、监听状态、复制地址和路由说明；右侧显示该 Endpoint 当前绑定的模型列表，模型行展示 Provider、模型名、上下文大小、优先级、Enable/Disable 和拖拽排序。不要在本页展示 Selected Router 面板；路由深层编辑通过 Provider 页面下一级进入。页面首屏包含 H1 网关路由编排、保存网关配置主按钮、Endpoint 总数与启用路由上下文。包含空、禁用、保存成功、拖拽排序和 endpoint 未监听状态。响应式：1280x800 desktop 使用 240px sidebar 与三段纵向配置；tablet sidebar 收窄，mobile 改为顶部菜单，Endpoint 内部左右 split 改为上下堆叠，模型列表自然滚动。使用语义区块、清晰焦点态、40px 触控目标和 prefers-reduced-motion。导航链接到 Overview、Provider、Activity，并将 Console 作为 sidebar 目标。

## Selected Context

- [html-screen-frame] Gateway - 1280 x 800 - .kun-design/ceed315c/1ae0aaa2/v1.html

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

## Version History

- v1: `.kun-design/ceed315c/1ae0aaa2/v1.html` - OllamaHub Control Center 网关页面。沿用现有深石墨背景、青绿色品牌色、蓝色信息色、浅灰高对比文本、8px 面板圆角和 240px desktop sidebar。左侧 sidebar 在所有页面统一，顺序为：概览、网关、Provider、活动、控制台；当前激活网关。页面目标是把 Ollama Endpoint 路由到 Provider 与 Model。主内容展示三个纵向堆叠的 Endpoint 配置区，直接标为 OpenAI、Ollama、Azure。每个 Endpoint 区域内部使用左右 split：左侧显示 endpoint 地址、监听状态、复制地址和路由说明；右侧显示该 Endpoint 当前绑定的模型列表，模型行展示 Provider、模型名、上下文大小、优先级、Enable/Disable 和拖拽排序。不要在本页展示 Selected Router 面板；路由深层编辑通过 Provider 页面下一级进入。页面首屏包含 H1 网关路由编排、保存网关配置主按钮、Endpoint 总数与启用路由上下文。包含空、禁用、保存成功、拖拽排序和 endpoint 未监听状态。响应式：1280x800 desktop 使用 240px sidebar 与三段纵向配置；tablet sidebar 收窄，mobile 改为顶部菜单，Endpoint 内部左右 split 改为上下堆叠，模型列表自然滚动。使用语义区块、清晰焦点态、40px 触控目标和 prefers-reduced-motion。导航链接到 Overview、Provider、Activity，并将 Console 作为 sidebar 目标。
