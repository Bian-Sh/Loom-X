# 桌面端设置页设计

## 目标

将 `.design/pages/settings/index.html` 的设置页信息架构落地到 Avalonia 桌面端，替换主导航中的设置占位页，并通过现有 `ConfigSnapshotService` 读写 SQLite 中的 `AppSettings`。

## 结构

- 新增 `SettingsViewModel`，负责设置加载、编辑状态、保存、代理连通性测试、打开数据目录、清理日志和导出诊断摘要。
- 新增 `SettingsView.axaml`，使用分组 `TabControl` 呈现通用、连接、更新、数据与隐私、关于五个分区。
- `MainWindowViewModel.ShowSettings` 创建并显示独立设置 ViewModel；`App.axaml` 增加对应 DataTemplate。

## 行为

- 进入设置页时异步加载 `AppSettingsResponse`，加载失败显示页内错误状态。
- 编辑字段采用防抖自动保存，把全部字段组装为 `AppSettingsInput`；成功后刷新脱敏状态并显示保存时间，不提供单独的保存按钮。
- 自定义代理模式显示地址、端口、用户名和密码字段；密码只作为本次更新输入，已有密码仅显示“已配置”。
- 测试代理只验证当前模式下的配置：直连/系统代理返回说明，自定义代理使用 `HttpClient` 请求代理地址，禁止记录凭据。
- 更新检查只执行本地版本状态检查，明确展示“更新服务尚未接入”，不伪造远程结果。
- 打开数据目录使用系统 Shell 打开 `AppDataPaths.RootDirectory`；清理日志删除该目录下的 `*.log` 文件；导出诊断摘要写入 AppData 目录并用 Shell 打开文件位置。

## 错误与安全

- 所有异步命令捕获异常并写入页内状态，不让异常冒泡到 UI 线程。
- 日志仅记录加载、保存、测试、清理和导出结果的安全摘要，不记录 API Key、代理密码或请求正文。
- 设置校验继续由 `ConfigurationManagementService.ValidateSettings` 负责，ViewModel 只做用户输入的类型转换。

## 验证

- `dotnet build OllamaHub.slnx` 必须通过。
- 现有配置服务测试必须通过；新增 ViewModel 测试覆盖输入映射、保存成功和加载错误状态。
