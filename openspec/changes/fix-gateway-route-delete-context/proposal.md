# 问题

网关页面模型组合中的 route cell 删除按钮在组合卡片默认展开、但页面尚未设置 `SelectedCombo` 时点击无反应。路由开关也复用了同一页面级上下文，存在相同的静默失效风险。

# 根因

`RemoveRouteAsync` 和 `ToggleRouteAsync` 通过 `SelectedCombo` 查找当前 route。`GatewayComboEditorViewModel` 默认展开不会触发 `ToggleCombo_OnClick`，因此直接操作 route cell 时 `SelectedCombo` 仍为空，入口守卫提前返回。

# 修复目标

- route 删除按钮在直接点击任意已加载组合的 route cell 时都能删除对应持久化路由并更新当前列表。
- route 启用开关使用相同的所属 Combo 解析逻辑，避免同类静默失效。
- Combo 删除、Provider 删除和 Provider 模型删除的既有入口保持可用，并用伪造配置数据覆盖验证。
