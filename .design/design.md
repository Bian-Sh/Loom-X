# OllamaHub 原型维护设计

## 1. 目标

`.design` 是 OllamaHub 在 Codex 中维护 APP 原型的唯一工作目录，用于替代原 Agent 客户端专有的 `.kun-design` 数据格式。

迁移完成后：

- Codex 只修改 `.design` 中的原型、规范和工具。
- `.kun-design` 作为只读历史档案，不再回写或同步。
- 五个业务页面使用稳定、可读的路径，不再依赖随机 artifact ID。
- 页面可以直接从本地文件打开，并能在五个页面之间完整导航。
- 原型结构适合 vibecoding：入口明确、上下文集中、变更容易验证。

## 2. 范围

当前维护以下五个业务页面：

| 页面键 | 页面名称 | 旧版来源 | 新路径 |
| --- | --- | --- | --- |
| `overview` | 概览 | `899ef30d/v2.html` | `.design/pages/overview/index.html` |
| `gateway` | 网关 | `a9213c1e/v3.html` | `.design/pages/gateway/index.html` |
| `providers` | Provider | `689f5fad/v1.html` | `.design/pages/providers/index.html` |
| `activity` | 活动 | `de3c9aba/v2.html` | `.design/pages/activity/index.html` |
| `console` | 控制台 | `a9213c1e/v4.html` | `.design/pages/console/index.html` |

Logo、空白 Screen、生成中占位页、旧 Gateway 方案和白板操作日志不迁入主原型，只在来源映射中留档。

## 3. 目录结构

```text
.design/
├─ README.md                 原型入口和快速操作说明
├─ design.md                 本设计与维护规则
├─ manifest.json             机器可读的页面清单
├─ archive-map.md            `.kun-design` 来源和历史映射
├─ pages/
│  ├─ overview/index.html
│  ├─ gateway/index.html
│  ├─ providers/index.html
│  ├─ activity/index.html
│  └─ console/index.html
├─ shared/                   后续出现真实复用时再提取共享资源
└─ scripts/
   └─ validate.ps1           只读校验入口
```

`shared` 不预先拆分 CSS 或 JavaScript。当前页面是自包含 HTML，先保持页面独立，避免为了复用而增加构建系统和运行依赖。

## 4. 数据模型

`manifest.json` 是工具读取原型状态的唯一结构化入口，至少包含：

- `schemaVersion`：清单格式版本。
- `project`：项目名称。
- `entryPage`：默认入口页面键。
- `sourceArchive`：原 Kun Design 工作区路径。
- `pages`：五个页面对象。

每个页面对象包含：

- `key`：稳定页面键。
- `title`：用户可见名称。
- `path`：相对于仓库根目录的 HTML 路径。
- `status`：`active`、`draft` 或 `deprecated`。
- `source.artifactId` 与 `source.versionId`：迁移来源。
- `route`：原型内部逻辑路由。
- `responsibility`：页面职责摘要。

历史版本不复制到 `pages`。必要时通过 Git 历史和 `archive-map.md` 追溯。

## 5. 导航规则

五个页面统一使用相对于当前 HTML 的链接：

```text
../overview/index.html
../gateway/index.html
../providers/index.html
../activity/index.html
../console/index.html
```

规则如下：

- 产品 Logo 始终返回 Overview。
- 主导航顺序固定为：概览、网关、Provider、活动、控制台。
- 当前页面必须设置 `aria-current="page"`。
- 页面中的上下文链接必须指向 `.design` 内的有效页面，不得继续引用 `.kun-design`。
- 原型不得依赖本地服务即可完成基础浏览与交互。

## 6. 维护工作流

日常 vibecoding 流程：

1. 从 `.design/README.md` 或 Overview 打开原型。
2. 只修改本次需求涉及的页面。
3. 若信息架构、设计 token、页面职责或交互契约变化，同步更新本文件和 `manifest.json`。
4. 运行 `.design/scripts/validate.ps1`。
5. 在桌面和移动视口检查相关页面的布局、导航和交互。
6. 提交时将原型、规范和实现代码按需求保持在同一变更上下文中。

新页面必须先加入 `manifest.json`，再创建 HTML。废弃页面先标记为 `deprecated`，确认无引用后再由用户明确授权删除。

## 7. 迁移策略

本次迁移采用一次性快照：

- 从五个确认版本复制自包含 HTML。
- 只进行路径、页面身份和明显失效导航的规范化。
- 不主动重绘视觉、不改变业务交互、不批量重构 CSS/JavaScript。
- 迁移完成后验证所有内部链接均落在 `.design`。
- `.kun-design` 原文件保持原样。

迁移后，`.design` 与 `.kun-design` 不做双向同步。若未来需要参考旧方案，只从 `.kun-design` 人工提取明确选择的内容。

## 8. 校验与完成标准

`validate.ps1` 至少检查：

- `manifest.json` 可以解析。
- 清单中恰好存在五个 `active` 页面。
- 每个页面文件存在。
- 页面键、路径和来源 ID 唯一。
- HTML 不包含 `.kun-design` 引用。
- HTML 内部相对链接目标存在。
- 每页包含有效 `<title>`、一个 `<h1>` 和当前导航标记。

本次迁移完成的判断标准：

- 五个页面均能直接打开。
- 五页主导航互相可达。
- 当前页面高亮正确。
- 原有主要交互仍可使用。
- 校验脚本通过。
- `.kun-design` 没有因迁移产生修改。

## 9. 非目标

本次不包含：

- 将原型直接改造成生产前端。
- 引入 React、Vue、Vite 或其他构建工具。
- 清理 `.kun-design` 历史目录。
- 重构现有页面视觉系统。
- 自动同步后端 API 或实现代码。

这些工作应在后续明确需求中独立设计和实施。

## 10. Provider 页面契约

Provider 页面承载 SQLite 配置中心中的上游连接与模型管理，信息架构固定为“Provider 目录 + 分区编辑器”：

- Provider 目录展示名称、Base URL、协议、模型数、密钥状态与最近连接结果。
- 编辑器分为“基础”“请求”“模型”三个面板，避免把身份、鉴权和模型操作混在同一长表单。
- “基础”维护显示名称、业务 ID、Provider 类型、Base URL 与启用状态。
- “请求”维护协议、DPAPI 密钥状态、有序自定义 Header 与连接测试。
- “模型”维护远程同步、差异预览、搜索、启停、顺序、能力和上下文参数。
- 远程同步只生成差异预览，不隐式删除本地模型；用户确认后才写入候选配置。
- API Key 不回显旧值，只表达“未配置/已配置”，更新和清除是独立动作。
- 页面明确显示 `OllamaHub.db` 为唯一配置真源以及运行快照状态。
- Provider/Model/密钥/Header 的保存可立即替换运行快照；监听地址等需要重启的设置不放在本页。

原型可以使用静态演示数据模拟加载、测试、同步、引用冲突和保存反馈，但交互语义必须与 `docs/superpowers/specs/2026-08-27-sqlite-provider-control-center-design.md` 保持一致。
