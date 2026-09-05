---
change: rename-project-to-loomx
design-doc: docs/superpowers/specs/2026-09-05-rename-project-to-loomx-design.md
base-ref: 46a86148bf114b6618f13e87f95af4017f11d30a
---

# LoomX 项目更名与数据迁移实施计划

> 本计划根据 OpenSpec change、已确认的深度设计和当前仓库结构编写。当前仓库未发现可加载的 Superpowers `writing-plans` 技能，因此按现有 `docs/superpowers/plans` 格式记录等价的可执行计划。

## 目标与边界

- 产品显示名统一为 `Loom-x`；C# 命名空间、项目、程序集、运行时机器标识统一为 `LoomX`。
- 物理结构改为 `LoomX/`、`LoomX.Tests/`、`LoomX.slnx`，发布入口唯一为 `LoomX.exe`。
- 正常运行时数据统一位于 `%LOCALAPPDATA%\LoomX`：`LoomX.db`、`LoomX.Activity.db` 和 `logs\loomx-YYYYMMDD.log`。
- 首次启动在创建任何数据库连接前迁移旧配置库 `%LOCALAPPDATA%\OllamaHub\OllamaHub.db` 和旧活动库 `%LOCALAPPDATA%\OllamaHub\Activity.db`；不迁移旧日志、不删除旧目录。
- 使用 `VACUUM INTO` 写入同目录临时文件，完成完整性/必要表检查后原子提交；旧版占用、损坏源库或迁移失败必须阻止启动，不得生成静默空库。
- `/api/tags`、`/v1/chat/completions` 等既有 HTTP 路由、请求/响应结构和 Provider/Model 配置语义保持不变。
- 不重写或清理 `.codegraph`、`graphify-out`、`outputs` 等既有流程产物；计划阶段不修改业务代码。

## 执行约束

- 开始实施前执行 `git pull --ff-only` 和 `git status --short`；遇到其他 session 的未提交改动时只记录并避让，不重置、不清理。
- 所有文档、代码注释和提交消息使用中文；代码中的技术标识按约定保留 `LoomX`、`LOOMX_*` 等英文形式。
- 迁移组件通过 `ILogger<T>` 记录开始、成功、降级/冲突和失败摘要，禁止记录数据库内容、API Key、Authorization、请求/响应正文或用户 prompt。
- 测试使用临时目录和内部构造函数，不读写真实用户 `%LOCALAPPDATA%`；需要 GUI 验证时只截取 LoomX 应用窗口。

---

## 1. 基线检查与改名清单

**依赖：** 无。

**目标文件/目录：** `OllamaHub.slnx`、`OllamaHub.Desktop/`、`OllamaHub.Tests/`、`AGENTS.md`、`README.md`。

**步骤：**

1. 执行 `git pull --ff-only`、`git status --short`，记录基线工作区状态和可能来自其他 session 的文件。
2. 用 `rg --files` 建立源码、XAML、项目、脚本和测试文件清单；排除 `bin/`、`obj/`、`.codegraph/`、`graphify-out/`、`outputs/` 生成内容。
3. 用 `rg -n -i "OllamaHub|OllamaHub\.Desktop|Activity\.db|AppDataPaths|owned_by|EnvironmentVariable|Mutex"` 定位所有改名联动点，作为后续逐项替换的检查表。
4. 记录当前 `origin`，确认设置页和文档链接使用仓库实际 canonical URL，不凭空引入新的远程地址。

**验证：**

- `git status --short` 的无关修改清单已记录，未执行覆盖/清理。
- 旧名称搜索结果已按“源码/测试/脚本/文档/生成产物”分类。

## 2. 物理文件与项目结构重命名

**依赖：** 任务 1。

**目标文件/目录：**

- `OllamaHub.slnx` → `LoomX.slnx`
- `OllamaHub.Desktop/` → `LoomX/`
- `OllamaHub.Desktop/OllamaHub.Desktop.csproj` → `LoomX/LoomX.csproj`
- `OllamaHub.Tests/` → `LoomX.Tests/`
- `OllamaHub.Tests/OllamaHub.Tests.csproj` → `LoomX.Tests/LoomX.Tests.csproj`
- `LoomX/OllamaHubHost.cs` → `LoomX/LoomXHost.cs`

**步骤：**

1. 只使用 `git mv` 完成上述文件和目录重命名，保留 Git 历史；不得移动或删除 `.codegraph`、`graphify-out`、`outputs` 和其他非本 change 产物。
2. 更新 `LoomX.slnx` 项目路径，使其只引用 `LoomX/LoomX.csproj` 与 `LoomX.Tests/LoomX.Tests.csproj`。
3. 更新两个 `.csproj` 的项目属性、默认程序集/根命名空间、ProjectReference 和内容文件路径，使项目名和路径不再包含 `OllamaHub.Desktop`。
4. 在目录改名后立即运行一次 `dotnet restore LoomX.slnx`，尽早发现解决方案或项目引用遗漏。

**验证：**

- `Test-Path LoomX.slnx`, `Test-Path LoomX/LoomX.csproj`, `Test-Path LoomX.Tests/LoomX.Tests.csproj` 均为真。
- `rg -n "OllamaHub\.Desktop|OllamaHub\.Tests|OllamaHub\.slnx" LoomX.slnx LoomX LoomX.Tests scripts README.md` 只允许显示尚未完成的待改源码引用，不允许出现在项目路径配置中。
- `dotnet restore LoomX.slnx` 成功。

## 3. 命名空间、程序集和 Avalonia 资源身份

**依赖：** 任务 2。

**目标文件：** `LoomX/**/*.cs`、`LoomX/**/*.axaml`、`LoomX/Properties/AssemblyInfo.cs`、`LoomX/app.manifest`、`LoomX.Tests/**/*.cs`、两个项目文件。

**步骤：**

1. 将 `namespace OllamaHub;` 改为 `namespace LoomX;`，将 `namespace OllamaHub.Desktop...` 改为 `namespace LoomX...`；测试命名空间从 `OllamaHub.Tests...` 改为 `LoomX.Tests...`，保持子命名空间层级。
2. 将所有 `using OllamaHub...`、`using OllamaHub.Desktop...` 和 `InternalsVisibleTo("OllamaHub.Tests")` 改为 LoomX 对应值。
3. 将 `OllamaHubHost` 类型和调用点改为 `LoomXHost`，文件名与静态启动契约测试同步更新。
4. 更新项目/程序集身份、`AssemblyTitle`、`AssemblyProduct`、`AssemblyName`、`RootNamespace` 和 manifest 中的显示身份；不保留旧程序集或旧 namespace 包装层。
5. 更新所有 XAML `x:Class`、`xmlns:using`、资源字典引用和 `avares://OllamaHub...` URI 为 `avares://LoomX/...`；同步修复测试中构造的资源 URI。
6. 修复编译器报告的遗漏引用后，再运行一次旧名称搜索；生成的 `graphify-out` 等索引不作为修改目标。

**验证：**

- `dotnet build LoomX/LoomX.csproj --no-restore` 能解析所有 C# 和 XAML 类型。
- `rg -n "namespace OllamaHub|using OllamaHub|OllamaHubHost|avares://OllamaHub|InternalsVisibleTo.*OllamaHub" LoomX LoomX.Tests` 无结果。
- Avalonia 资源契约测试可加载 `avares://LoomX/Styles/VisualTokens.axaml`。

## 4. 运行时标识、UI 品牌和协议身份

**依赖：** 任务 3。

**目标文件：** `LoomX/MainWindow.axaml`、`LoomX/MainWindow.axaml.cs`、`LoomX/ViewModels/**`、`LoomX/Views/**`、`LoomX/InstanceLaunchPolicy.cs`、`LoomX/Logging/**`、`LoomX/LoomXHost.cs`、相关契约测试。

**步骤：**

1. 将窗口标题、导航品牌、设置页、诊断摘要和启动提示更新为 `Loom-x`；用户可见短反馈继续遵循 `ToastService`，不显示敏感值。
2. 将单实例互斥锁改为 `Local\\LoomX`，Shell 引导互斥锁改为 `Local\\LoomX.ShellBootstrap`，多实例环境变量改为 `LOOMX_ALLOW_MULTIPLE_INSTANCES`。
3. 将自启动参数改为 `--loomx-child`，快捷方式参数前缀改为 `--loomx-bootstrap-link=`，Activity 请求上下文常量改为 `LoomX.Activity.Request`，日志前缀改为 `loomx-`。
4. 更新根健康响应 `name` 为 `Loom-x`、模型列表 `owned_by` 为 `loomx`；保留所有已有路径和协议字段。
5. 更新设置页项目主页/问题链接为仓库实际 canonical URL，并同步更新对应 UI 契约测试。
6. 为运行时身份和协议兼容补充/调整断言：旧名称不出现在用户可见品牌，`/api/tags`、`/v1/chat/completions` 仍按原有配置语义工作。

**验证：**

- 定向运行实例、启动策略、窗口、设置页和网关测试。
- `rg -n "OllamaHub|ollamahub|--ollamahub|Local\\\\OllamaHub|OLLAMA" LoomX LoomX.Tests` 只允许命中迁移兼容源常量或明确的协议/历史说明，不允许命中用户可见品牌和新运行时标识。

## 5. 新应用数据路径与旧路径常量

**依赖：** 任务 3。

**目标文件：** `LoomX/AppDataPaths.cs`、所有配置/活动/日志创建点、`LoomX.Tests/AppDataPathsTests.cs`、相关路径契约测试。

**步骤：**

1. 将生产路径唯一来源改为 `AppDataPaths`：根目录 `%LOCALAPPDATA%\\LoomX`、配置库 `LoomX.db`、活动库 `LoomX.Activity.db`、日志目录 `logs`、配置初始化锁 `LoomX.db.init.lock`。
2. 增加独立迁移锁 `%LOCALAPPDATA%\\LoomX\\LoomX.data-migration.lock`；旧路径仅作为迁移源常量：`%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db` 和 `%LOCALAPPDATA%\\OllamaHub\\Activity.db`。
3. 检查 `ConfigurationDbContext`、`ActivityStore`、`ActivityQueryService`、`LoggingBootstrap`、`ConfigSnapshotService` 等入口，确保不再使用 `AppContext.BaseDirectory`、当前工作目录或硬编码旧路径创建数据库。
4. 明确日志只从新 `logs` 目录开始写入，不复制旧日志；保留旧目录及其 `-wal`/`-shm` 文件。
5. 更新路径测试，断言配置库、活动库、日志和锁路径，并确保测试使用可控的临时路径构造而不是修改真实用户目录。

**验证：**

- `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~AppDataPathsTests` 通过。
- `rg -n "AppContext\.BaseDirectory|OllamaHub\\OllamaHub\.db|Data Source=.*OllamaHub|Activity\.db" LoomX` 无正常运行时旧路径命中；允许迁移兼容常量单独存在并有注释/测试覆盖。

## 6. 实现安全、幂等的 SQLite 迁移组件

**依赖：** 任务 5。

**目标文件：** 新增 `LoomX/ApplicationDataMigration.cs`（或与现有存储目录一致的等价路径）、新增 `LoomX.Tests/ApplicationDataMigrationTests.cs`、迁移异常/测试辅助类型。

**接口与行为：**

- 提供 `IApplicationDataMigration.EnsureMigratedAsync(CancellationToken)`；生产路径来自 `AppDataPaths`，内部测试构造函数接收显式 `oldRoot`、`newRoot` 和 logger。
- 配置库先迁移，活动库后迁移；两文件不共享跨库事务。
- 目标已存在时直接记录摘要并跳过源库，不覆盖目标；源和目标都不存在时返回给现有初始化流程创建新库。

**步骤：**

1. 获取 `LoomX.data-migration.lock`，创建新根目录；清理由本组件留下的同名临时文件，不删除未知文件。
2. 对每个源/目标文件执行 `MigrateFileAsync(source, target, databaseKind)`：目标存在则跳过，源不存在则跳过，其他情况使用只读、私有缓存、关闭连接池、`DefaultTimeout=5` 的 SQLite 连接打开源库。
3. 使用参数绑定/严格安全路径处理执行 `VACUUM INTO`，写入同目录临时文件 `LoomX*.db.migrating.<guid>.tmp`；不执行强制 checkpoint、不直接复制主库、WAL 或 SHM 文件。
4. 关闭源连接后以只读方式打开临时库，执行 `PRAGMA integrity_check`；配置库检查 `AppSettings`、`GatewayConfigurations`、`Providers`、`Models`，活动库检查 `Events`。
5. 完整性和必要表检查通过后使用同目录 `File.Move` 原子提交；若检查期间目标已出现，删除临时文件并保留已有目标。
6. 成功时保留旧源文件及其 WAL/SHM，记录数据库类型、源/目标字节数和耗时；禁止记录表内容或密文内容。
7. 对 locked/busy、源不可读、快照失败、完整性失败、必要表缺失、原子提交失败分别包装带数据库类型/阶段的启动异常；尽力清理临时文件，清理失败只记录 Warning，不掩盖原始异常。
8. 确认活动库失败不会回滚已成功配置库；应用仍阻止启动，下次启动仅重试缺失的活动目标。

**验证：**

- `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~ApplicationDataMigrationTests` 覆盖无源库、配置/活动首次迁移、WAL、重复运行、目标优先、配置成功活动失败、锁定、损坏、完整性失败、原子提交失败、临时文件清理和 DPAPI 字节原样保留。
- 测试断言迁移异常不会创建可用空配置库，旧目录始终存在，新目标只有完整性检查通过后才出现。
- 测试 logger 的结构化事件不会包含 API Key、Authorization、数据库正文或用户 prompt。

## 7. 在宿主启动前接入迁移

**依赖：** 任务 6。

**目标文件：** `LoomX/LoomXHost.cs`、`LoomX/App.axaml.cs`、`LoomX/Program.cs`、启动/宿主测试。

**步骤：**

1. 调整 `LoomXHost.CreateAsync` 的顺序为：单实例完成后创建路径/迁移锁与新日志目录，执行 `EnsureMigratedAsync`，再创建配置数据库连接、初始化 EF、加载配置并构建 WebApplication。
2. 确保迁移发生在任何配置库或活动库连接、`ConfigurationDatabase.InitializeAsync`、`ActivityStore` hosted service 创建之前；日志 bootstrap 可提前，但迁移异常必须记录安全摘要。
3. 将迁移失败向上抛为启动失败，UI/宿主只显示“数据库类型、阶段、关闭旧版/检查权限”等摘要；严禁 catch 后继续初始化空库。
4. 保持单实例、网关路由注册和现有配置加载语义不变；新库优先时不回读旧库。
5. 添加宿主启动契约测试，断言迁移调用位于首次 `ConfigurationDbContext`/`ActivityStore` 创建之前，并断言迁移失败不进入正常监听状态。

**验证：**

- 定向运行 `OllamaHubHostTests` 改名后的 `LoomXHostTests` 和启动契约测试。
- 通过日志/测试双重断言：旧版持锁或源库损坏时没有新空库、没有网关监听、旧源未被删除。

## 8. 测试路径与契约全面改名

**依赖：** 任务 3、任务 5、任务 7。

**目标文件：** `LoomX.Tests/**/*.cs`，尤其是 `Views/*ContractTests.cs`、`Desktop/InstanceLaunchPolicyTests.cs`、`Views/WindowAppearanceCoordinatorTests.cs`、`TempDbProbeTests.cs`。

**步骤：**

1. 将所有静态路径中的 `OllamaHub.Desktop` 改为 `LoomX`，测试项目和 namespace 改为 `LoomX.Tests`。
2. 将实例、资源 URI、窗口、品牌、网关根响应、模型 `owned_by`、日志前缀和发布入口断言更新为新契约。
3. 删除/替换硬编码真实用户路径的探针测试，使数据库测试全部使用临时目录和显式连接字符串；保留旧路径只用于迁移 fixture。
4. 新增静态扫描测试或脚本契约，限制旧名称只出现在迁移兼容代码、迁移测试和升级说明中；生成索引目录不参与断言。

**验证：**

- `dotnet test LoomX.Tests/LoomX.Tests.csproj` 通过。
- `rg -n -i "OllamaHub|ollamahub" LoomX LoomX.Tests scripts README.md AGENTS.md docs --glob '!docs/superpowers/specs/**' --glob '!docs/superpowers/plans/**'` 的命中均能归类为迁移源常量、迁移测试或升级说明。

## 9. 文档、发布脚本和升级说明

**依赖：** 任务 3、任务 5、任务 8。

**目标文件：** `README.md`、`AGENTS.md`、`scripts/publish-desktop.ps1`、`LoomX/LoomX.csproj`、`LoomX/app.manifest`、升级说明文件（若仓库没有既有文件则新增 `docs/loomx-upgrade.md`）。

**步骤：**

1. README 更新产品名、运行/构建/测试/发布命令、数据路径、日志前缀、迁移行为和唯一入口 `LoomX.exe`；明确旧目录保留、不迁移旧日志、旧版占用时启动失败。
2. AGENTS 中的数据库唯一路径和相关工程约定改为 LoomX 新路径，并补充旧 OllamaHub 路径仅用于一次性迁移；不要改动与本 change 无关的规则。
3. 发布脚本将项目路径改为 `LoomX\LoomX.csproj`，保持带时间戳 `outputs` 输出，不删除既有输出；发布后严格断言 exe 数量为 1、名称为 `LoomX.exe`，并断言不存在旧应用入口。
4. 更新 manifest/项目元数据和设置页链接；升级说明记录迁移前提、失败处理、回滚方式和旧目录保留策略。
5. 检查项目文件的默认程序集名和发布配置，确保不会额外产生 `LoomX.Desktop.exe` 或旧 `OllamaHub*.exe`。

**验证：**

- `pwsh -File scripts/publish-desktop.ps1 -Configuration Release` 能发布到新的时间戳目录。
- 逐项检查 README、AGENTS、升级说明和脚本中的路径/命令与实现一致。

## 10. 全量验证、发布包与桌面验收

**依赖：** 任务 8、任务 9。

**步骤：**

1. 运行 `dotnet test LoomX.slnx`，记录失败测试、修复遗漏后从失败用例重新运行，再执行全量测试。
2. 运行 `dotnet build LoomX.slnx`，确认桌面项目和测试项目的程序集输出均为 LoomX 标识。
3. 运行 `scripts\\publish-desktop.ps1`，只在 `outputs/<yyyyMMdd-HHmmss>/` 新建发布目录；检查目录包含 `LoomX.exe`，不包含旧入口，且 Overview/Avalonia 资源存在。
4. 使用临时用户数据根验证“新安装”和“旧库迁移”两条路径：新安装只创建 `%LOCALAPPDATA%\\LoomX`；旧库迁移后 Provider、Model、设置、活动记录和 DPAPI 密文可读，旧目录仍存在。
5. 使用 CUA/应用窗口级检查 Loom-x 标题、品牌、正常启动、单实例、网关健康检查、`/api/tags` 和 `/v1/chat/completions` 路由；不截取桌面全屏。
6. 汇总 `dotnet test`、`dotnet build`、发布检查、迁移场景和 GUI 检查证据，写入中文验证报告；不得删除既有 `outputs` 或流程产物。

**失败处理：**

- 编译失败先修复项目路径、namespace、资源 URI 或程序集身份；不添加旧名称兼容层。
- 迁移测试失败先修复快照/校验/原子提交边界；不得退回到直接复制数据库文件。
- 发布或 GUI 验收失败时保留失败输出和日志，修复后重跑受影响的定向测试及全量验证。

## 11. 交付收尾

**依赖：** 任务 10。

**步骤：**

1. 根据实际完成情况勾选 `openspec/changes/rename-project-to-loomx/tasks.md`，只勾选有测试或命令证据的任务。
2. 更新 Comet/OpenSpec 状态和中文验证报告，列明旧目录保留、旧日志不迁移、兼容路由保持不变及任何残余风险。
3. 运行最终 `git diff --check` 和受影响文件的旧名称扫描，确认没有误改生成产物或无关文档。
4. 使用中文提交消息提交并推送；提交前再次确认没有包含其他 session 的未提交修改，后续再进入 Comet verify/archive。

