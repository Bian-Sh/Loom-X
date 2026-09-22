# Provider 请求头与选择状态设计

## 目标

修复 Provider 页面在 API Key 自动保存后丢失选中项的问题，并将“请求”页的自定义请求头改为键值行编辑器。

## 方案

- Provider 自动保存成功后更新当前 `ProviderEditorViewModel` 的字段，不替换列表项引用，以保持 `ListBox.SelectedItem` 和右侧编辑面板稳定。
- Provider 集合非空时，`SelectedProvider` 不接受空值；刷新、删除和新增完成后确保存在一个选中项。仅当集合为空时显示“暂无 Provider”。
- `ProviderEditorViewModel.Headers` 使用可观察的键值行集合，行字段变更时同步到现有 `HeadersJson`，新增/删除操作直接修改该集合。请求发送和持久化继续使用字典。

## 验证

- 构建与既有测试通过。
- 静态检查确认非空集合选择回退、Provider 保存不替换引用，以及 header 行到字典转换路径均已接入现有流程。
