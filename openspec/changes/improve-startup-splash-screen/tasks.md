# Tasks

## 1. 启动进度模型与配置快照

- [x] 1.1 新增 `StartupPhase` / `StartupProgress` 模型，覆盖数据库、插件编译、插件加载、网关、主界面、完成、失败等状态，并补齐阶段推进与失败状态的单元测试 <!-- comet-task:4f095830-eb2e-4613-9611-7520d712fa09 -->
- [x] 1.2 在 `ConfigSnapshotService` 增加一次性启动快照读取 API，确保一次初始化后返回完整配置数据，并用测试验证不再重复打开/初始化同一配置库 <!-- comet-task:3107b4ea-4156-4ef8-9cd9-05f9bb1a77fb -->
- [x] 1.3 将 `AppDataStore.InitializeCoreAsync()` 改为使用一次性启动快照，保留现有写路径与局部刷新逻辑，并用回归测试确认初始化阶段只触发一次数据库读取 <!-- comet-task:1923238c-12a7-49fa-a8f0-cf4f5888f42a -->

## 2. 插件运行时显式编排

- [ ] 2.1 给 `PluginRuntime` 增加真实的编译/加载阶段上报能力，并通过单元测试确认阶段顺序与完成状态准确 <!-- comet-task:90de4c27-de1a-4f7e-bfa1-3f4695984902 -->
- [x] 2.2 调整 `GatewayProcessService` / `LoomXHost` 的初始化顺序，使插件运行时由启动编排器显式拉起，而不是由主窗口构造隐式触发，并补充对应集成测试 <!-- comet-task:de37b2c7-4a5e-47ed-a620-15a3b4031c1a -->
- [x] 2.3 移除 `AssistantViewModel` 构造函数中的隐式 `EnsureHostedServicesAsync()` 调用，改为启动完成后显式初始化助手服务，并测试主窗口构造不再触发插件运行时 <!-- comet-task:4ad82b29-c14b-424d-9a23-fe23a1ddda1b -->

## 3. 启动闪屏 UI

- [x] 3.1 新增独立 `SplashWindow` 与 `SplashViewModel`，展示品牌、版本、阶段列表、当前阶段和总体进度，并通过 UI 契约测试验证布局与绑定 <!-- comet-task:cc154ed0-c36e-493b-bf9c-35173c6654dd -->
- [x] 3.2 在 `App` 启动流程接入 `StartupOrchestrator`，按“加载数据库 → 编译插件 → 加载插件 → 启动网关 → 准备主界面”顺序切换闪屏阶段，并在完成后关闭闪屏、显示主窗口 <!-- comet-task:7252bb05-89d7-4a43-8a66-7f129865aab9 -->
- [x] 3.3 移除当前概览页占位加载文案与奇怪的“导航接入”文案，把加载职责完全收敛到启动闪屏，并更新相关资源与本地化测试 <!-- comet-task:060fdc04-785c-461a-a9aa-aab4034a6785 -->

## 4. 集成验证

- [x] 4.1 运行启动链路相关测试，确认启动顺序、阶段上报、单次读库和主窗口构造行为全部通过 <!-- comet-task:2eb69814-b15c-4b71-86be-fa8cff2e0bcf -->
- [ ] 4.2 用真实启动日志测量优化前后的启动耗时与配置库初始化次数，确认启动等待明显下降且没有引入新的重复初始化 <!-- comet-task:d50aadb3-6e3a-4882-a220-79ebf024faaf -->
- [ ] 4.3 执行整体回归测试与本地化校验，确认新增闪屏文案、主题适配和既有功能没有回归 <!-- comet-task:3141951d-0519-4bd1-85e4-f823e8497fe0 -->
