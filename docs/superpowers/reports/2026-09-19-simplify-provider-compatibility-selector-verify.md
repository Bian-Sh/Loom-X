# Provider 兼容类型选择器紧凑化验证报告

## 结论

验证通过。基础 Tab 已使用单个下拉菜单选择接口兼容类型，下拉项仅显示类型名称，当前接口 URI 在下拉菜单外单独显示；现有字段映射、自动保存与请求协议未改变。

## 完整性

- OpenSpec 任务：3/3 完成。
- Delta spec：`provider-panel` 新增的紧凑选择器需求已实现。
- 实现范围：仅修改 Provider 视图布局与对应视图契约测试。

## 正确性

- `ProvidersView.axaml` 使用 `CompatibilityOptions` 和 `SelectedCompatibility` 双向绑定。
- 下拉项模板只包含三种兼容类型标题，不包含 URI 描述。
- `/chat/completions`、`/responses`、`/messages` 描述位于下拉菜单外，并随当前兼容类型切换可见性。
- 视图契约测试覆盖下拉绑定、旧 RadioButton 卡片移除、URI 外置和 API Key 所属 Tab。

## 一致性

- 复用现有 `ProviderCompatibilityOption`、`ProviderCompatibilityMatchConverter` 和本地化资源，没有增加新的持久化字段或事件处理。
- 未修改数据库路径、日志、安全数据边界或运行时请求路由。

## 验证证据

- 定向测试：Provider 相关测试 55/55 通过。
- 完整测试：通过临时测试输出配置关闭 xUnit collection 并行后，1032/1032 通过；该配置仅写入忽略的 `bin` 目录，未进入源码。默认并行运行曾出现既有 Avalonia `Call from invalid thread` 调度波动，与本次两处代码改动无关。
- Release 构建：成功，0 错误；保留既有 `SQLitePCLRaw.lib.e_sqlite3` NU1903 警告。
- OpenSpec：`openspec validate simplify-provider-compatibility-selector --strict` 通过。
- 发布包：`outputs/20260920-004401/LoomX.exe`。
- 发布包启动复验受现有另一工作区 LoomX 单实例进程阻止；新进程按预期退出，未终止或干扰其他 Session 的进程。布局行为已由编译与视图契约测试覆盖。

## 分支处理

- 实现提交：`89db6b9 tweak: 简化提供商兼容类型选择器`。
- 已推送到 `origin/master`。
