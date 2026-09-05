---
comet_change: rename-project-to-loomx
role: technical-design
canonical_spec: openspec
---

# LoomX 项目更名技术设计

状态：已确认，实施完成

## 1. 设计目标

本设计细化 `rename-project-to-loomx` 的实现方式。目标是把当前已经是单一桌面应用的 OllamaHub 工程收敛为 LoomX，同时安全迁移用户本地数据。

产品显示名为 `Loom-x`；C# namespace、程序集、项目和机器可读标识为 `LoomX`/`loomx`。物理项目结构直接去掉历史 `Desktop` 后缀，发布入口为 `LoomX.exe`。现有 HTTP 路由、协议结构和配置语义不变。

## 2. 当前结构与改名映射

### 2.1 文件和程序集

| 当前 | 目标 |
| --- | --- |
| `OllamaHub.slnx` | `LoomX.slnx` |
| `OllamaHub.Desktop/` | `LoomX/` |
| `OllamaHub.Desktop/OllamaHub.Desktop.csproj` | `LoomX/LoomX.csproj` |
| `OllamaHub.Tests/` | `LoomX.Tests/` |
| `OllamaHub.Tests/OllamaHub.Tests.csproj` | `LoomX.Tests/LoomX.Tests.csproj` |
| `OllamaHub.Desktop/OllamaHubHost.cs` | `LoomX/LoomXHost.cs` |
| `OllamaHubHost` | `LoomXHost` |
| `OllamaHub.*` | `LoomX.*` |
| `OllamaHub.Tests` 程序集 | `LoomX.Tests` 程序集 |

使用 `git mv` 保留 Git 历史。改名完成后，所有项目引用、静态测试路径、XAML `x:Class`/`using:`、`avares://` URI、`InternalsVisibleTo` 和程序集身份必须指向目标名称。`.codegraph`、`graphify-out`、`outputs` 等生成产物不重写、不清理。

### 2.2 运行时标识

- 单实例互斥锁：`Local\\LoomX`
- Shell 引导互斥锁：`Local\\LoomX.ShellBootstrap`
- 多实例调试变量：`LOOMX_ALLOW_MULTIPLE_INSTANCES`
- 自启动参数：`--loomx-child`
- 快捷方式参数前缀：`--loomx-bootstrap-link=`
- 日志文件前缀：`loomx-`
- Activity 请求上下文常量：`LoomX.Activity.Request`

旧运行时标识不作为兼容别名保留，避免旧版和新版本误共享互斥锁或调试开关。

## 3. 应用数据路径与启动时序

### 3.1 路径解析

`AppDataPaths` 是生产路径唯一来源：

```text
%LOCALAPPDATA%\\LoomX\\
├─ LoomX.db
├─ LoomX.Activity.db
└─ logs\\loomx-YYYYMMDD.log
```

同一类型的初始化锁使用 `%LOCALAPPDATA%\\LoomX\\LoomX.db.init.lock`。迁移使用独立的 `%LOCALAPPDATA%\\LoomX\\LoomX.data-migration.lock`，防止多个进程同时判断并复制文件。旧路径常量只用于迁移源：

```text
%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db
%LOCALAPPDATA%\\OllamaHub\\Activity.db
```

正常运行时不得从 `AppContext.BaseDirectory`、当前工作目录或环境变量读取/写入数据库路径。

### 3.2 启动时序

```text
Program.Main
  -> App.OnFrameworkInitializationCompleted
     -> 单实例检查
     -> LoomXHost.CreateAsync
        -> AppDataPaths.EnsureCreated
        -> ApplicationDataMigration.EnsureMigratedAsync
        -> LoggingBootstrap.Configure
        -> ConfigurationDatabase.InitializeAsync
        -> DatabaseConfigurationProvider.ReloadAsync
        -> builder.Build / MapEndpoints
```

迁移必须在 `LoomXHost` 创建任何配置或活动数据库连接之前执行。日志初始化可以在迁移前创建新日志目录，但迁移错误需要通过启动阶段 logger 或已配置的 Serilog 记录安全摘要。

## 4. 数据迁移组件

### 4.1 组件边界

新增 `ApplicationDataMigration`（名称可按现有代码风格调整），只负责两个 SQLite 文件的路径迁移，不了解 EF 实体、不转换业务字段、不迁移日志。

建议接口：

```csharp
public interface IApplicationDataMigration
{
    Task EnsureMigratedAsync(CancellationToken cancellationToken = default);
}
```

生产构造函数使用 `AppDataPaths` 的固定路径；内部测试构造函数接收显式 `oldRoot`、`newRoot` 和 logger。迁移组件可以是无 DI 的启动服务，以避免在目标数据库可用前构造依赖目标库的服务。

### 4.2 单文件迁移算法

对配置库和活动库分别执行 `MigrateFileAsync(source, target, databaseKind)`：

1. 如果目标已存在，记录 Debug/Information 摘要并返回，不读取源库、不覆盖目标。
2. 如果目标不存在且源不存在，返回，由各自现有初始化流程创建新库。
3. 创建目标目录，删除本次组件留下的同名临时文件，生成同目录临时路径，例如 `LoomX.db.migrating.<guid>.tmp`。
4. 以 `Mode=ReadOnly; Cache=Private; Pooling=False; DefaultTimeout=5` 打开源连接。
5. 通过参数化 SQLite 命令执行 `VACUUM INTO`，目标指向临时文件。目标路径必须经过 SQLite 字符串参数绑定或严格转义，不能拼接未经处理的用户输入。
6. 关闭源连接后，以只读连接打开临时文件，执行 `PRAGMA integrity_check`，结果必须为 `ok`；配置库额外检查 `AppSettings`、`GatewayConfigurations`、`Providers`、`Models` 必要表，活动库检查 `Events` 表。
7. 使用 `File.Move(tempPath, targetPath)` 在同一目录内原子提交；如果目标在检查期间已经出现，视为冲突，删除临时文件并保留已有目标。
8. 成功后保留 source 及其 `-wal`/`-shm` 文件，不删除旧目录；记录迁移完成的数据库类型和字节数，不记录数据库内容。
9. 所有异常都删除临时文件（删除失败只记录 Warning），包装为带数据库类型和阶段信息的启动异常并向上抛出。

`VACUUM INTO` 读取源库的一致性快照，能包含 WAL 中已提交数据。若旧版进程仍写入或持有锁，命令在超时/忙错误后失败；组件不得执行强制 checkpoint、复制主文件或重试到可能产生不一致快照。

### 4.3 两个文件的事务边界

配置库和活动库按固定顺序独立迁移：先配置库，后活动库。没有跨文件事务：

- 配置库成功、活动库失败：保留配置库，应用启动失败，下次只因目标活动库不存在而重试活动库。
- 配置库失败：不尝试活动库，应用启动失败。
- 目标已存在：视为该文件已完成，不因旧源仍存在而重复迁移。

这样可以避免反复复制已验证的配置库，同时保持“任一关键数据迁移未完成则不启动”的安全语义。

### 4.4 失败分类与用户可见行为

| 阶段 | 典型异常 | 行为 |
| --- | --- | --- |
| 源连接 | `SqliteException` busy/locked、文件不可读 | 记录 Error，提示关闭旧版，阻止启动 |
| 快照 | `VACUUM INTO` 失败 | 删除临时文件，阻止启动 |
| 校验 | `integrity_check` 非 `ok`、必要表缺失 | 删除临时文件，阻止启动 |
| 提交 | 原子移动失败、目标冲突 | 删除临时文件；已有目标保留，阻止当前未完成文件启动 |
| 清理 | 临时文件删除失败 | 记录 Warning；不掩盖原始迁移错误 |

启动错误消息只包含“哪个数据库、哪个阶段、建议关闭旧版/检查权限”等摘要，不包含 API Key、Authorization、请求正文或 SQLite 表内容。

## 5. 代码与资源更新顺序

1. 先重命名文件和目录，修复 `.slnx`、项目引用、项目/程序集名称和测试路径，使项目结构可被工具识别。
2. 批量更新 `namespace`/`using`/类型名，随后修复编译器报告的遗漏引用。
3. 更新 Avalonia XAML `x:Class`、`using:` 和 `avares://LoomX/...`，运行资源加载测试。
4. 更新 `AppDataPaths`、日志前缀、互斥锁、环境变量、启动参数和迁移接入点。
5. 更新 UI/README/AGENTS/发布脚本及设置页链接。
6. 最后更新协议身份字段：根路径 `name = "Loom-x"`、模型列表 `owned_by = "loomx"`，不改路由。

## 6. 测试设计

### 6.1 迁移单元测试

测试使用临时目录和内部构造函数，不触碰真实 `%LOCALAPPDATA%`：

- 无源库：目标不提前创建，迁移组件不报错，后续初始化可创建新库。
- 配置库首次迁移：Provider、Model、设置和网关行保持，源文件保留。
- 活动库首次迁移：`Events` 行保持，目标文件名为 `LoomX.Activity.db`。
- WAL：先在 WAL 模式数据库提交数据且不手工复制 `-wal`，迁移目标仍包含已提交行。
- 幂等：重复运行不改变目标最后写入时间/内容，不覆盖新库。
- 新旧同时存在：目标数据优先，源数据不回写。
- 配置成功/活动失败：配置目标保留，下一次只执行活动库迁移。
- 源库 locked：迁移失败、目标正式文件不存在、源库保留。
- 损坏源库或完整性失败：不生成可用空库，临时文件清理。
- 模拟原子移动失败：目标不被半成品替换，异常包含数据库类型和阶段。
- DPAPI：密文列字节/字符串原样保留，不在迁移组件中解密或重加密。

### 6.2 名称与契约测试

- `AppDataPaths` 断言新根目录、配置库、活动库、日志和锁路径。
- 静态路径测试只读取 `LoomX` 目录。
- XAML 资源测试加载 `avares://LoomX/Styles/VisualTokens.axaml`。
- 启动策略测试断言 `LoomX.exe`、`LOOMX_ALLOW_MULTIPLE_INSTANCES` 和 LoomX 互斥标识。
- 网关测试断言根响应 `Loom-x`、模型列表 `loomx`，并继续覆盖旧 HTTP 路由。
- 发布脚本契约测试断言项目路径为 `LoomX/LoomX.csproj`、唯一入口为 `LoomX.exe`。

### 6.3 集成验证

完成单元测试后依次执行：

```text
dotnet test LoomX.slnx
dotnet build LoomX.slnx
scripts\\publish-desktop.ps1
```

发布包验证新安装和从旧 `%LOCALAPPDATA%\\OllamaHub` 启动两条路径。使用 CUA 只检查 LoomX 应用窗口：标题、品牌、启动状态、单实例和网关健康检查。发布目录使用时间戳子目录，不修改或清理既有 `outputs` 内容。

## 7. 回滚与发布切换

发布前保留旧版本可执行文件和旧数据目录。LoomX 不把旧目录重命名为新目录，也不删除源文件，因此旧版本仍可读取旧数据。若新版本迁移失败：

1. 停止 LoomX，保留错误日志和新目录中的可诊断临时文件状态。
2. 使用旧版本读取未修改的 `%LOCALAPPDATA%\\OllamaHub` 数据。
3. 关闭旧版本后修复权限/占用问题，重新启动 LoomX 重试未完成文件。

新目录中的正式目标只在完整性检查通过后出现；临时目标不可作为运行时数据库。迁移完成后旧目录仍作为人工回滚备份，不自动清理。

## 8. 验收标准

- `LoomX.slnx` 构建并测试通过，源码和 XAML 资源全部使用 LoomX 标识。
- 发布包应用入口是 `LoomX.exe`，不存在旧应用入口。
- 新安装只创建 `%LOCALAPPDATA%\\LoomX`。
- 旧配置、活动记录和 DPAPI 密文迁移后可正常使用，旧目录保留。
- 旧版占用、损坏源库和迁移失败均不会产生空配置库或启动到半成品状态。
- 既有 HTTP 路由和请求/响应结构不变，身份字段显示 Loom-x/loomx。
