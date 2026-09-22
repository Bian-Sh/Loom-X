# OllamaHub 原型

这是 OllamaHub 在 Codex 中维护的 APP 原型入口。原型是纯静态 HTML，不需要安装依赖或启动服务。

## 当前页面

| 页面 | 打开 |
| --- | --- |
| 概览 | [pages/overview/index.html](./pages/overview/index.html) |
| 网关 | [pages/gateway/index.html](./pages/gateway/index.html) |
| Provider | [pages/providers/index.html](./pages/providers/index.html) |
| 活动 | [pages/activity/index.html](./pages/activity/index.html) |
| 控制台 | [pages/console/index.html](./pages/console/index.html) |
| 设置 | [pages/settings/index.html](./pages/settings/index.html) |

页面清单见 [manifest.json](./manifest.json)，完整维护规则见 [design.md](./design.md)，旧 Kun Design 来源见 [archive-map.md](./archive-map.md)。

## Vibecoding 约定

1. 先读 `manifest.json` 和 `design.md`，再修改页面。
2. 页面源文件只放在 `.design/pages/*/index.html`。
3. 保持页面自包含；只有出现真实复用需求时才提取 `shared/`。
4. 不修改 `.kun-design`，也不把页面链接写回 `.kun-design`。
5. 修改后运行：

   ```powershell
   pwsh -File .design/scripts/validate.ps1
   ```

6. 视觉变更同时更新页面的可访问名称、状态和移动布局。

## 本地预览

直接在浏览器打开任意 `index.html` 即可。若需要统一静态服务器，可在仓库根目录运行已有工具链提供的静态服务器；原型本身不依赖特定端口。
