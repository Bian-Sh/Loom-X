## 1. 配置契约与持久化

- [x] 1.1 先增加失败测试，覆盖 `StartWithWindows`、`GatewayRunning` 默认值、设置读写和当前配置结构初始化，再实现 AppSettings 实体、输入/响应、运行时快照及数据库列并验证相关配置测试通过 <!-- comet-task:61ae2fad-ead3-41a1-b7cc-193caa84eb59 -->
- [x] 1.2 先增加失败测试，覆盖单字段更新网关运行意图且不改变其他设置，再实现 ConfigurationManagementService、ConfigSnapshotService 和 AppDataStore 的 `SetGatewayRunningAsync` 链路并验证测试通过 <!-- comet-task:fd3c2073-7d4c-4aff-a064-0c682060bdfe -->

## 2. Windows 开机自启动

- [x] 2.1 先增加失败测试，覆盖当前用户 Run 值的命令格式、启用和移除行为，再实现 Windows 自启动注册服务并验证测试通过 <!-- comet-task:4a8b1ea2-6c7f-4ad7-b876-1b136cb353ff -->
- [x] 2.2 在设置 ViewModel、设置页和四套本地化资源中加入“开机时启动 Loom-X”，验证设置加载/保存、资源覆盖率和 AXAML 契约测试通过 <!-- comet-task:2c4f308e-0387-44f8-b40d-7a6301554781 -->

## 3. 网关运行意图恢复

- [x] 3.1 先增加失败测试，验证概览页启动写入 `true`、关闭写入 `false`，且自动状态变化不写入，再修改���览页命令并验证测试通过 <!-- comet-task:cb666a6e-975b-45c2-a2a6-f5c755dcdac2 -->
- [x] 3.2 先增加失败测试，验证 APP 初始化仅在 `GatewayRunning = true` 时恢复网关、启动失败不清零意图、退出清理不改意图，再接入应用启动流程并验证测试通过 <!-- comet-task:1059e916-c96e-41f8-a33f-e27937c5f03a -->

## 4. 集成验证与交付

- [x] 4.1 运行 OpenSpec 严格校验、相关测试、完整测试和 Release 构建，确认无失败或新增警告 <!-- comet-task:264fec92-b221-4d13-978c-6e3c7a466f67 -->
- [x] 4.2 使用隐藏启动方式运行发布包并通过 CUA 验收设置开关、概览页网关启停与重启恢复行为，记录实际结果 <!-- comet-task:96e9d929-22e5-4308-8c4a-ddf65411dd8f -->
- [x] 4.3 生成 win-x64 发布包，以可读时间命名保存到 `outputs/`，验证可执行文件路径和产物内容完整 <!-- comet-task:0addce73-e868-4200-883d-44c9a096c8a3 -->
