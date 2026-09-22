# LoomX 项目更名技术设计

日期：2026-09-05

状态：待文档审阅

## 1. 目标与范围

项目产品名从 OllamaHub 更名为 Loom-x。技术标识同步收敛为 LoomX，去掉历史上因独立 CLI 工程产生的 Desktop 后缀。此次变更包含源码目录、解决方案、项目和程序集、C# 命名空间、Avalonia 资源标识、运行时互斥标识、环境变量、启动参数、UI/文档文案、发布入口和本地数据目录。

已有用户配置必须保留。旧版本使用的 `%LOCALAPPDATA%\\OllamaHub` 数据目录不删除，首次启动 LoomX 时将配置库和活动库迁移到新的 LoomX 目录；迁移失败不得静默创建空库。

HTTP 路由、请求/响应结构和 Provider/Model 配置语义保持不变，以避免破坏现有客户端。根响应中的产品名称和 OpenAI 模型列表的 `owned_by` 更新为 LoomX 品牌。

## 2. 名称契约

| 类别 | 新名称 | 说明 |
| --- | --- | --- |
| 产品显示名 | `Loom-x` | 用于窗口标题、导航、诊断摘要、README 和发布说明 |
| C# 根命名空间 | `LoomX` | 标识符不能包含连字符 |
| 桌面项目/程序集 | `LoomX` | 取代 `OllamaHub.Desktop` |
| 测试项目/程序集 | `LoomX.Tests` | 取代 `OllamaHub.Tests` |
| 解决方案 | `LoomX.slnx` | 只保留 LoomX 和 LoomX.Tests |
| 桌面源码目录 | `LoomX/` | 取代 `OllamaHub.Desktop/` |
| 测试源码目录 | `LoomX.Tests/` | 取代 `OllamaHub.Tests/` |
| 网关宿主类型 | `LoomXHost` | 取代 `OllamaHubHost` |
| 发布入口 | `LoomX.exe` | 发布目录唯一的应用入口 |

所有 `OllamaHub.*` namespace、`using`、`x:Class`、Avalonia `avares://` URI、程序集属性和静态测试源码路径改为 `LoomX.*`。含有品牌含义的 UI、日志和文档文本使用 `Loom-x`；协议内部的机器可读标识使用 `loomx`。

运行时标识改为以下形式：

- `Local\\OllamaHub.Desktop` → `Local\\LoomX`
- `Local\\OllamaHub.Desktop.ShellBootstrap` → `Local\\LoomX.ShellBootstrap`
- `OLLAMAHUB_*` → `LOOMX_*`
- `--ollamahub-child` → `--loomx-child`
- `--ollamahub-bootstrap-link=` → `--loomx-bootstrap-link=`

不保留旧 namespace、旧程序集或旧 exe 兼容层。旧名称只允许出现在旧数据迁移路径、迁移测试和升级说明中。

## 3. 数据目录与迁移

### 3.1 新旧路径

| 用途 | 旧路径 | 新路径 |
| --- | --- | --- |
| 应用根目录 | `%LOCALAPPDATA%\\OllamaHub` | `%LOCALAPPDATA%\\LoomX` |
| 配置数据库 | `OllamaHub.db` | `LoomX.db` |
| 活动数据库 | `Activity.db` | `LoomX.Activity.db` |
| 日志目录 | `OllamaHub\\logs` | `LoomX\\logs` |
| 配置库初始化锁 | `OllamaHub.db.init.lock` | `LoomX.db.init.lock` |

运行时所有配置、活动、日志和锁路径只能从统一的 `AppDataPaths` 解析，不能使用 `AppContext.BaseDirectory`、当前工作目录或其他隐式位置。旧路径作为迁移源常量保留，不作为正常运行时写入路径。

### 3.2 迁移时机与规则

新增独立的应用数据迁移组件，由 `LoomXHost` 创建数据库连接之前调用。迁移组件只负责文件和 SQLite 数据库迁移，不负责业务配置转换。

迁移流程：

1. 创建 `%LOCALAPPDATA%\\LoomX` 及其日志目录，并通过新目录下的迁移锁避免并发执行。
2. 当新配置库不存在且旧配置库存在时，使用 SQLite 备份能力写入新目录下的临时文件。活动库使用相同流程。
3. 对临时文件执行 SQLite 完整性检查和必要表检查，成功后原子替换为 `LoomX.db` 或 `LoomX.Activity.db`。
4. 迁移成功后保留旧 `%LOCALAPPDATA%\\OllamaHub` 目录及文件，不删除、不覆盖，作为回滚备份。
5. 新旧库同时存在时，新库优先，不重复迁移，也不覆盖新版本已经产生的数据。
6. 旧库不存在时直接走新库的正常初始化流程。
7. 源库损坏、SQLite 备份失败、完整性检查失败或原子替换失败时，删除未完成的临时目标，记录可定位的错误，并阻止应用继续启动；不得继续创建空的新配置库。

配置库迁移失败是启动失败；活动库迁移失败同样阻止启动，以免用户误以为活动历史已完整迁移。日志只记录路径、数据库类型、阶段和异常摘要，不记录 API Key、请求正文或数据库内容。DPAPI 加密字段原样迁移，由同一 Windows 用户继续解密，不重新生成密钥。

### 3.3 可测试性

`AppDataPaths` 继续提供生产环境的固定路径；迁移组件的内部测试入口接收显式源路径和目标路径，使测试不触碰当前用户的真实 `%LOCALAPPDATA%`。生产代码不增加可改变数据目录的环境变量或命令行开关。

## 4. 实施边界

### 4.1 代码与资源

- 使用 `git mv` 重命名解决方案、项目目录、项目文件、测试目录和 `OllamaHubHost.cs`。
- 更新项目引用、`InternalsVisibleTo`、Avalonia `x:Class`、`using`、namespace 和资源 URI。
- 更新窗口标题、导航品牌、诊断摘要、根健康响应、模型列表 `owned_by`、日志文件名前缀和启动提示。
- 更新设置页项目主页和问题反馈链接为 `https://github.com/Bian-Sh/Loom-X`。`origin` 已经指向该仓库，无需修改 remote。
- 不删除或清理既有 `.codegraph`、`graphify-out`、`outputs` 及其他流程产物。

### 4.2 文档与脚本

- README 和 `AGENTS.md` 的产品名、命令、数据库路径、日志路径和发布文件名更新为 LoomX 约定。
- `scripts/publish-desktop.ps1` 改为发布 `LoomX` 项目，并严格校验发布目录只有 `LoomX.exe`（必要的运行库文件除外，不允许出现旧入口）。
- 为升级说明保留一段旧路径到新路径的迁移说明；这是允许出现 `OllamaHub` 旧名称的文档范围。

## 5. 验证策略

### 5.1 自动化测试

- 更新现有测试项目和所有静态源码路径断言，确保只引用 `LoomX` 路径和 namespace。
- 新增迁移测试：配置库首次迁移、活动库首次迁移、重复启动幂等、新库已存在、SQLite WAL、源库损坏、目标完整性失败和失败后不生成空库。
- 更新 `AppDataPaths` 测试，断言新目录、文件名和锁路径。
- 更新发布契约测试，断言项目、程序集、资源 URI、互斥标识和唯一 `LoomX.exe` 入口。
- 执行完整 `dotnet test LoomX.slnx`。

### 5.2 构建与发布验证

- 执行 `dotnet build LoomX.slnx`。
- 执行发布脚本，输出到 `outputs/yyyyMMdd-HHmmss/`。
- 使用发布包启动 `LoomX.exe`，验证单实例约束、网关健康检查、配置读取、活动库读取和旧库迁移。
- 使用 CUA 进行桌面窗口级验证，只检查 LoomX 应用窗口，不截取全屏。
- 在活动源码、项目文件、脚本、用户文档和测试范围内搜索，确认旧名称只出现在迁移兼容代码、迁移测试和升级说明中；`.codegraph`、`graphify-out`、`outputs` 等既有生成产物不纳入此项搜索，也不为本变更清理或重写；协议路由及现有接口路径保持不变。

## 6. 回滚与验收

发布前保留旧版本发布包和 `%LOCALAPPDATA%\\OllamaHub` 目录。若 LoomX 启动或迁移失败，可停止 LoomX，使用旧版本读取未删除的旧目录；新目录中的不完整临时文件由迁移组件清理，正式旧库不被修改。

验收条件：

1. 源码、项目、程序集、namespace、资源 URI、UI、脚本和发布入口统一使用 LoomX/Loom-x 约定。
2. 全部自动化测试和解决方案构建通过。
3. 已有旧配置和 API Key 在新路径下可用，且旧目录仍保留。
4. 新安装只创建 `%LOCALAPPDATA%\\LoomX`，不再创建新的 `%LOCALAPPDATA%\\OllamaHub` 数据库。
5. 发布目录中的应用入口为 `LoomX.exe`，桌面应用和内嵌网关可正常启动。
6. `/api/tags`、`/v1/chat/completions` 等既有 HTTP 路由继续可用。
