# 设计讨论检查点

- Change: add-source-plugin-runtime-and-bridge
- 日期：2026-10-12
- 阶段：Design
- 状态：用户已于 2026-10-12 要求继续直到开发完成，设计方案已确认；正式技术设计以 design.md 为准。
- 正式规格：OpenSpec delta spec。
- 本轮授权：用户确认 Open 产物，并要求继续下一阶段；后续继续指令确认设计补充与持续开发目标；Build 执行配置仍须按 Runtime 缺失字段补齐。

## 已确认事实与范围

- Open 阶段守卫实际通过 8/8 检查，Runtime 已推进至 Design。
- 沿用独立工作区和已有分支，不新建第二个 change。
- Manifest 显式声明源码、入口与宿主能力；第一版仅提供 storage.sqlite/v1、logging/v1、notifications/v1。
- 插件只依赖公共契约；源码插件属于可信进程内扩展，不实现 Worker、OS 沙箱、任意 NuGet 恢复或目录 glob。
- Credential Protection 保持预编译第一方插件，复用原 credential-tokens.db、CredentialTokens 表、DPAPI 密文和长期 placeholder。
- 不改变普通禁用的兼容解析语义，不增加卸载、Vault 销毁或数据迁移操作。
- 现有 proposal、design、tasks 与三个 delta spec 保持原内容，不在用户确认前覆盖已有设计。

## 实际代码核对

- LoomX.PluginHost/PluginCatalog.cs：当前按插件子目录发现 Manifest；需要扩展现有入口，不建立第二条插件发现链。
- LoomX.PluginHost/PluginRuntime.cs：当前只有同步 Start、Initialize、注册及启停，没有 reload 事务与卸载编排；不能把 ALC 可回收当作 Runtime 已具备热更新。
- LoomX.PluginHost/Pipeline.cs：Entry 存在可变 List；直接在请求并发执行期间重排或替换是不安全的。
- LoomX/LoomXHost.cs：组合根启动时取得 request/response Pipeline 并传给 ProviderExecutionPipeline。
- LoomX/Services/ProviderExecutionPipeline.cs：请求和响应管线保存在实例中；仅替换 Runtime 注册而不处理这些长期持有的引用，可能让 reload 不生效。
- LoomX.Plugin.Abstractions/ILoomXPlugin.cs：二参数初始化上下文和 void Initialize 已存在，需要保持旧二进制调用入口。
- LoomX.Plugin.Abstractions/PluginTextCatalog.cs：文案先查程序集目录，再查宿主 plugins/<id>，最后查嵌入 en-US；源码程序集加载自缓存时必须同时处理负载位置。
- plugins/LoomX.CredentialProtection/CredentialTokenStore.cs：同步锁、独立连接、失败事务结束后精确查回；不能机械替换异常类型而遗漏生命周期。
- plugins/LoomX.CredentialProtection/CredentialEngine.cs：文本、JSON、SSE 处理和 token 查表当前同步，不应为数据库依赖去重顺带重写整个解析链。
- plugins/LoomX.CredentialProtection/LoomX.CredentialProtection.csproj：直接引用 Microsoft.Data.Sqlite 10.0.0 并复制运行时依赖，是插件包 SQLite 负载的实际来源。
- 已通过现有 codegraph 查询定位 PluginRuntime 与 CredentialTokenStore，并以当前工作区代码核验索引结果。

## 已确认技术方案

### 1. 契约与宿主能力

- 沿用 Open 阶段的 Runtime 联合结构和 host_capabilities 精确协商。
- 保留 PluginInitializationContext(string, string) 构造与现有属性；以新增属性提供 Bridge，不把二参数构造替换成三参数构造。
- Abstractions 仅承载接口、封闭值 DTO、稳定错误及诊断。SQLite 实现集中到 LoomX.Plugin.Storage.Sqlite，由 LoomX 组合根装配。
- 不宣称整个宿主只能有该程序集直接引用 SQLite：宿主已有配置数据库依赖仍然保留；本次只消除插件自带 SQLite 实现及重复负载。
- 未声明能力使用拒绝代理；Bridge 与具体插件实例代际绑定，已释放实例的句柄不能继续调用。

### 2. 无 SDK 编译与负载

- 推荐随发布包附带固定 net10.0 编译引用集合、Roslyn 组件与 Abstractions；构建时收集，运行时不寻找本机 SDK，不使用宿主已加载程序集全集作为引用白名单。
- 不采用运行时 TPA 全集：它容易把宿主第三方依赖隐式暴露给插件，且难以锁定不同发布形态下的输入。
- 编译与缓存身份加入实际引用程序集摘要，以及进入产物的本地化负载摘要；不存在只变更 i18n 却继续使用旧文案的问题。
- 候选缓存仍为内容寻址、同卷临时目录和原子发布。缓存中镜像 plugins/<id>/plugin.manifest.json 与 i18n/<culture>.json，程序集位于对应插件目录，并嵌入 en-US。
- 沿用 PluginTextCatalog 的既有查找顺序，不添加按程序集名分层的特判，不平铺 Manifest 或 i18n。
- 源码生成程序集身份含内容摘要，避免同名程序集跨 reload 复用错误的本地化缓存。
- 缺少引用集、摘要不符、输出或 PDB 不完整均视为缓存/发布错误；不隐式在线补齐。

### 3. SQLite 服务边界与事务

- 一命令只接受一条实际 SQL 语句；不能通过按分号切割或关键字正则承担 SQL 安全检查。
- 使用 SQLite prepare/authorizer 层检查，并禁止 ATTACH、路径型 VACUUM INTO、扩展加载及可更改文件落点的 PRAGMA。
- 普通数据库内 DDL/DML、索引、查询及所需 WAL/synchronous/busy_timeout 保持可用。
- BEGIN、COMMIT、ROLLBACK、SAVEPOINT 等事务控制由 Bridge 句柄管理，不接受插件绕过句柄生命周期的裸事务控制 SQL。
- 数据库和事务共享串行操作门；活动事务期间，非事务命令不能悄悄加入该事务。完成后拒绝复用，未提交释放即回滚。
- 路径校验覆盖 plugin id、插件数据根、父目录与既有数据库文件本身；拒绝危险链接，而不仅检查字符串前缀。
- 资源上限由宿主提供固定策略，限制命令、Blob、行数和结果总字节，不引入插件自定义无限额度。
- 取消等待必须停止排队；执行中取消后检查连接状态，无法证明状态时关闭连接。API 名称中的 Async 不作为非阻塞 I/O 保证。
- 错误转换只返回稳定代码和安全摘要，不透传原 SQL、参数、绝对路径或提供程序异常对象。

### 4. Credential Protection 的最小改造

- 推荐维持同步 CredentialEngine/JSON/SSE 业务链，通过插件内部单一适配点消费公开异步 Bridge；第一版宿主 SQLite 实现不依赖 UI SynchronizationContext。
- 这调整 Open 设计中“同步锁改为异步互斥”的候选实现方法，但不改变任何行为规格；不为当前可信第一方插件预做 Worker 迁移。
- 不直接对未知异步实现随处调用 Result；适配点统一处理完成、取消、异常与释放，并通过具有 SynchronizationContext 的测试验证不会等待 UI 续体。
- 同步初始化入口仍存在；源码插件业务可直接 await Bridge。将来真正引入 Worker 时单独评估同步适配的阻塞成本，不能宣称本轮已经完成进程外执行。
- Token/OriginalHash 冲突必须先结束失败事务，再精确查回原映射。DPAPI、表、索引、算法、placeholder 完整性指令及 fail closed 不变。
- 路径由 AppDataPaths 组合根统一提供，不读取或写入生产之外的第二份 Vault；测试只使用临时目录。

### 5. reload 与在途请求

- 推荐候选实例、不可变注册快照与请求级代际租约；不在执行中原地修改 Pipeline Entry List。
- 候选编译、入口、初始化、Extension/UI 校验全部成功后才切换发布。按插件串行化 reload，与启停操作协调。
- ProviderExecutionPipeline 每个完整 Provider 调用取得 request/response 成对快照，不永久绑定启动时的旧 Pipeline；保留现有构造用法供测试与兼容。
- 租约贯穿请求、响应和流式正文释放；SSE 未结束不能关闭旧实例的 SQLite Bridge。
- 新请求使用新代际；旧请求继续原快照。旧租约释放后才解除 UI 订阅、回滚未完成事务、释放实例和 Bridge、申请 ALC 卸载。
- 候选失败只释放本次候选资源，旧实例继续运行。资源未排空时不强行卸载，不删除用户数据。
- 普通禁用不会释放 Credential Protection 的历史解析 Bridge；只有实际实例退休或宿主关闭才进入资源回收。
- 原子替换只覆盖 Runtime 注册与句柄归属，不是任意插件初始化副作用的事务回滚。可信插件初始化应使用幂等/事务化数据库操作，reload 失败不得回滚 Vault 快照覆盖新 token。

### 6. 开发工具、测试与安全诊断

- 六个 loomx.plugin.* 工具共享一个宿主应用服务，注册到既有 ToolRegistry；不新建第二套 Manifest、编译和 reload 规则，不额外建立 CLI 产品线。
- test 使用临时数据根、测试 Bridge 与假 Provider，不连接真实 Provider 或生产 Vault。可信任意 C# 仍可直接访问进程资源，这种测试隔离不是 OS 沙箱。
- validate/diagnostics 不执行插件代码；compile 只编译；test/reload 会执行可信代码，须沿用工具授权与可信代码披露。
- create 不覆盖已存在插件目录；compile 写缓存和 test 执行代码不能冒充纯读取工具。
- 编译错误使用稳定诊断 ID、相对路径、行列与受控消息；不直接回显可能带源码字面量的原始 Roslyn 消息或异常消息。
- Logging/Toast 只提交安全摘要。已知敏感键与形态过滤是防误用措施，不是对任意可信恶意插件的绝对防泄漏保证。

## 候选取舍

- 固定发布引用集优于扫描所有运行时程序集：略增加宿主发布负载，但锁定无 SDK 编译与依赖白名单。
- 同步凭据业务适配优于一次性全链路 async 化：最小变更、保留现有解析回归覆盖；代价是本地 SQLite 调用仍可能占用调用线程。
- 请求级租约优于直接换 List 或全局等待所有请求：保障 SSE 连续性，不阻塞新请求；代价是旧代际可能存活到长流结束。
- 缓存镜像规范负载优于修改文案查找协议：少一个运行时分支，代价是本地化变化使缓存身份变化。

## 测试策略（计划，尚未执行）

1. 契约与旧 DLL：旧二参数构造、旧 Manifest、默认启停、能力版本拒绝、DTO 序列化和禁止类型。
2. 真正发布包：在不调用外部 SDK 的测试子进程验证编译、固定引用集、DLL/PDB、缓存损坏重建及非白名单依赖拒绝；仅修改 PATH 不足以证明未依赖 SDK。
3. SQLite：CRUD、单语句、PRAGMA、authorizer 拒绝外部路径、链接逃逸、事务归属、冲突、取消、资源上限和不泄漏敏感哨兵。
4. 历史 Vault：临时目录构造旧版本格式记录，验证同文件复用、Token/OriginalHash 冲突、DPAPI 解密、暂停主动保护后历史恢复、异常 fail closed，不读取用户生产 Vault。
5. reload：候选初始化失败、并发新请求、旧请求/SSE 仍在途、旧事务延迟释放、启停竞争，以及不持有旧 Pipeline 的组合根回归。
6. 本地化：缓存加载四语言、en-US 嵌入、文案更新生效、旧程序集身份不污染新文案、规范路径及插件负载无 SQLite 文件。
7. 开发闭环：模板、validate/compile/test/reload/diagnostics、可信代码提示、临时 Provider、工具风险分级和敏感异常净化。
8. 实现阶段再构建、运行必要回归、按可读时间命名发布到 outputs，并以仅截取 APP 的 CUA 验收；本次无实现修改，不重复打包或启动 APP。

## 已确认的 Spec Patch

- source-plugin-runtime：追加引用/本地化负载变化使缓存失效；缓存加载遵守既有 i18n 布局；发布包缺少引用集时返回安全诊断。
- source-plugin-runtime：追加 reload 时旧在途请求/SSE 使用旧快照直到完成，新请求使用新代际，候选失败不替换。
- plugin-host-bridge：追加跨事务命令不能混入活动事务，以及实例退休后的句柄拒绝复用；普通禁用保持历史解析能力。
- plugin-development-skill：追加编译诊断遇到敏感源码字面量时不回显原值；测试隔离不被表述为恶意 C# 沙箱。
- 以上只补边界场景，不改原 capability 范围和公共承诺；若用户调整方案，先更新本检查点，再决定正式设计和 Spec Patch。

## 核查资料

- 当前工作区源码为主要依据，现有 CodeGraph 仅用于定位。
- Microsoft.Data.Sqlite 官方异步限制文档已核查：Async ADO.NET 方法在该提供程序中同步执行。
- SQLite sqlite3_set_authorizer 官方文档已核查：授权回调在语句 prepare 阶段工作，不能用字符串搜索替代。
- 未新增编译器或数据库依赖，版本选择和具体 API 调用由 Build 阶段按锁定依赖再次核验。

## 下一步

正式设计与 Spec Patch 已保存；执行 Design 守卫后进入 Build，按缺失的执行方式和 TDD 配置补齐授权，再制定计划和开发。
