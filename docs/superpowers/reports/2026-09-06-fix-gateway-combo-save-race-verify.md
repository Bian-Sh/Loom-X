# 网关 Combo 保存竞态修复验证报告

## 验证范围

- Combo 名称失焦无实际变化时不重复保存。
- Combo/Route 写操作串行执行，配置刷新在整组写入完成后统一处理。
- 刷新后只继续使用当前集合中的 Combo/Route 对象，并按 ID 恢复当前 Combo 选择。
- 兼容旧配置库中以 `TEXT` 存储的 Guid，覆盖 Combo/Route 按 ID 更新、删除和关联查询。

## 完整验证结果

| 维度 | 结果 | 证据 |
| --- | --- | --- |
| 完整性 | 通过 | `tasks.md` 共 4 项，4 项均已勾选；当前变更无 delta spec capability。 |
| 正确性 | 通过 | Combo/Route Guid 显式字符串转换；更新后重新读取和旧库 `TEXT` Guid 回归测试均通过。 |
| 一致性 | 通过 | 实现符合 `proposal.md` 与 `design.md`：不改服务端 API、数据库结构和配置路径。 |
| 安全性 | 通过 | 未新增密钥、Authorization、请求/响应正文或不安全操作。 |

## 自动化证据

- `dotnet test LoomX.Tests\\LoomX.Tests.csproj --no-restore`：232/232 通过，0 失败，0 跳过。
- `dotnet build LoomX\\LoomX.csproj --no-restore`：0 错误，1 个既有 `NU1903` SQLite 依赖警告。
- 新增 `GatewayComboAndRouteUpdatesReturnPersistedRowsAfterReload`，覆盖更新后重新读取。
- 新增 `GatewayGuidUpdatesSupportLegacyTextIds`，覆盖旧库文本 Guid。
- 发布包：`outputs/20260906-175553`。

## 桌面端验收

使用 CUA Driver 对发布包 `LoomX.exe`（PID 14988，窗口 4393956）执行网关页面验收：

1. sensenova 第一成员开关能够切换为关闭，再切回开启；界面状态恢复，未出现“成员状态保存失败”。证据：`outputs/20260906-175553/verify-member-off.png`、`verify-member-restored.png`。
2. sensenova 成员顺序能够从 `deepseek-v4-flash → glm-5.2 → sensenova-6.8-flash-lite` 临时调整为 `glm-5.2 → sensenova-6.8-flash-lite → deepseek-v4-flash`，界面提示“故障转移顺序已保存”；随后拖回原顺序并再次提示保存成功。证据：`verify-order-moved2.png`、`verify-order-restored3.png`。
3. 全局 Combo 标题开关此前已验证提示“Combo 模型已保存”；本轮排序和成员操作均未出现截图中的“Combo 模型不存在”或“路由不存在”。

## 工具限制与残余风险

- 本机未安装 `openspec` CLI，无法执行其 JSON 状态读取命令；已使用 Comet state/guard 脚本、变更产物全文、源码 diff、自动化测试和 CUA 验收完成等价核对。
- `verification-before-completion` 与 `finishing-a-development-branch` Superpowers 技能在当前技能目录不可用，已在验证过程保留该限制记录。
- 既有依赖警告 `NU1903` 未由本变更引入，未阻断本次验证。

## 最终结论

所有任务已完成，自动化测试、构建、旧库兼容性和网关 Combo 真实界面验收均通过。截图中的两个错误已不再复现，变更可进入归档前流程。
