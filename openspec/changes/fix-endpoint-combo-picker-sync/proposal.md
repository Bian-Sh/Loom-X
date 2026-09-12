# 问题

网关页面右侧新增、删除、重命名或停用模型组合后，左侧 Endpoint 的模型组合选择下拉仍显示操作前的快照。删除组合时，若该组合已被 Endpoint 使用，原有绑定和组合数据会被物理删除，刷新后无法恢复。

# 根因

`GatewayComboEntity` 没有删除状态，且 Combo 与 Endpoint binding 使用级联删除；`DeleteGatewayComboAsync` 直接移除实体，导致已绑定组合的数据和绑定一起消失。Endpoint DTO 也没有携带删除状态，ViewModel 刷新后无法重建 tombstone。

# 修复目标

- 新增组合后立即加入右侧列表，并追加到每个 Endpoint 的模型组合选择下拉。
- 重命名或启停组合后，所有 Endpoint 下拉项立即刷新名称和状态；停用文案使用多语言“停用”。
- 删除已被 Endpoint 使用的组合时，持久化标记 `IsDeleted`，保留组合数据、路由和 Endpoint binding；下拉项保持原名称和勾选状态，并以红色多语言“不存在”显示。
- 已删除项仍可取消勾选；取消最后一个 Endpoint binding 后才清理该软删除组合，使该 Endpoint 下拉项消失。
- 组合以相同 ID 恢复时，原 Endpoint binding 自动恢复为可用项。
