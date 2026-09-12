# 修复方案

保留现有增量保存架构，在 `GatewayViewModel` 的 Combo 新增、保存、启停和删除成功路径中调用一个局部同步方法。该方法把当前组合状态投影到每个 `GatewayEndpointEditorViewModel.ComboOptions`：新增时追加未选项，更新时修改名称与启用状态，删除时将既有 option 标记为 tombstone 而不移除。

`GatewayComboBindingOption` 的名称、启用状态和删除状态改为可通知属性；`StatusText` 先判断删除状态，再判断启用状态，并从资源键解析“不存在”或“停用”。删除项在 XAML 中禁用交互，避免把已不存在的 Combo ID 提交给配置服务。文化切换继续通过现有 `RefreshLocalization` 刷新派生文案。

测试直接构造 ViewModel option 验证属性通知、多语言文案和 tombstone 优先级，并使用临时 SQLite 驱动真实新增、启停和删除命令，验证所有 Endpoint 的下拉集合与持久化结果同步。

## 范围确认

实现预计超过 4 个文件仅因 `Strings.resx`、`Strings.en-US.resx`、`Strings.ja-JP.resx`、`Strings.zh-TW.resx` 必须保持资源对等。用户已确认继续 Hotfix，不升级为完整 Comet 流程。
