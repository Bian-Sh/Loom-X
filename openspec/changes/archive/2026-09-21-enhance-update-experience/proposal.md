## Why

Loom-X 当前的更新提示位于主窗口右下角，发现版本后仍需要用户手动发起下载，Release Notes 也被降级为纯文本且只能查看最新版本，流程割裂且难以持续感知下载与安装状态。现在需要参考 OpenCode 的低打扰更新入口，同时保持 Loom-X 自身的扁平化设计语言，将自动下载、安装确认和版本说明浏览整合为统一体验。

## What Changes

- 将更新提示从右下角卡片调整为窗口标题栏中的紧凑状态入口，在下载、校验、就绪和失败状态下提供可悬停展开的文字反馈。
- 自动检查发现正式新版本后自动下载并校验安装包，但必须等待用户在更新浮窗中确认后才启动安装器。
- 新增扁平化更新浮窗，直接渲染当前版本的 Markdown Release Note；下载期间展示实时进度，准备完成后提供“稍后”和“重启并安装”。
- 扩展设置页“更新”Tab，在保留当前版本、自动检查、更新代理和手动检查功能的基础上，增加最近正式版本列表、分页加载、版本标记以及 Markdown Release Notes 阅读区。
- 为 Release 历史提供加载、空、失败、缓存内容保留和加载更多状态，并过滤 Draft 与 Pre-release。
- 复用现有代理、GitHub Releases、SHA-256 校验、日志和本地化约束，不新增更新通道或后台静默安装。

## Capabilities

### New Capabilities

- `desktop-update-experience`: 定义桌面端自动更新状态机、标题栏更新入口、确认浮窗、下载与安装边界，以及设置页多版本 Release Notes 浏览行为。

### Modified Capabilities

无。

## Impact

- 主要涉及 `UpdateService`、`UpdateCoordinator`、`MainWindow`、`SettingsViewModel`、设置页 AXAML、Markdown 展示组件和本地化资源。
- 现有下载后立即启动安装器的服务边界将拆分为“下载并校验”和“用户确认后启动安装器”。
- GitHub Releases 拉取将同时支持最新可升级版本检查与最近正式版本的分页浏览。
- 将新增或调整更新服务、状态机、视图契约、本地化和 Markdown 展示测试；不改变数据库路径、发布资产命名和 SHA-256 校验契约。
