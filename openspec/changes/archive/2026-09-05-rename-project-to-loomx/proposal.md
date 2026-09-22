## Why

项目当前产品名、源码标识、发布入口和本地数据目录仍使用 OllamaHub，且项目已经迁移为单一桌面应用，历史上的 `Desktop` 后缀不再表达实际结构。现在统一更名为 Loom-x/LoomX，可以消除产品、代码和发布包之间的不一致，同时让新安装使用明确的 LoomX 数据目录。

## What Changes

- 将产品显示名统一为 `Loom-x`，将 C# namespace、项目、程序集和解决方案统一为 `LoomX`。
- 将桌面项目、测试项目、源码目录、Avalonia 资源 URI、运行时互斥标识、环境变量和启动参数改为 LoomX 标识。
- 将发布入口从旧名称改为 `LoomX.exe`，同步更新发布脚本、README、诊断信息和设置页链接。
- 将配置数据库从 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db` 迁移到 `%LOCALAPPDATA%\\LoomX\\LoomX.db`。
- 将活动数据库迁移到 `%LOCALAPPDATA%\\LoomX\\LoomX.Activity.db`，日志迁移到 `%LOCALAPPDATA%\\LoomX\\logs`。
- 新增幂等的 SQLite 数据迁移流程，保留旧目录作为回滚备份；迁移失败时禁止静默创建空库。
- 保持 `/api/tags`、`/v1/chat/completions` 等 HTTP 路径以及请求/响应结构不变。
- **BREAKING**：不再提供 `OllamaHub.*` namespace、旧程序集或旧 exe 兼容层。

## Capabilities

### New Capabilities

- `project-identity`: 定义 Loom-x 产品名、LoomX 技术标识、运行时标识和唯一发布入口。
- `app-data-migration`: 定义 OllamaHub 本地数据库向 LoomX 新路径的安全、可重试迁移行为。

### Modified Capabilities

无。当前 `openspec/specs/` 中没有覆盖项目身份或应用数据迁移的现有主规格。

## Impact

- 影响桌面项目、测试项目、解决方案文件、C# namespace、Avalonia XAML、程序集资源 URI、启动策略、日志和发布脚本。
- 影响 `%LOCALAPPDATA%` 下配置库、活动库、日志和初始化锁的路径解析。
- 需要新增 SQLite 迁移组件和迁移测试，并更新所有静态源码路径断言。
- HTTP 路由、Provider/Model 配置模型、DPAPI 密钥内容和现有客户端调用方式保持不变。
