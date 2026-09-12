# 网关模型组合删除按钮修复验证报告

## 验证范围

- 使用临时 SQLite 配置库写入伪造的 SenseNova Provider、`deepseek-v4-flash` 模型、模型组合和 route。
- 覆盖 Combo 未被页面选中时的 route 删除与 route 启用切换。
- 覆盖 Combo 删除、Provider 删除和 Provider 模型删除。

## 验证结果

- `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter 'FullyQualifiedName~GatewayViewContractTests|FullyQualifiedName~GatewayViewModelDeletionTests' --no-restore`：20/20 通过。
- `dotnet test LoomX.slnx --no-restore`：575/575 通过。
- `dotnet build LoomX.slnx --no-restore`：0 错误，7 个既有包、编译器和分析器警告。
- `pwsh -File scripts/publish-desktop.ps1 -Configuration Release`：发布目录 `outputs/20260912-222034`，包含 `LoomX.exe`（322560 bytes）。

## 完整验证清单

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| tasks.md 全部完成 | PASS | 3/3 任务已勾选 |
| proposal/design 目标覆盖 | PASS | 路由删除、开关上下文解析及其它删除入口均有实现和测试证据 |
| 场景覆盖 | PASS | 未选中 Combo 的 route 删除/开关，以及 Combo、Provider、模型删除均通过伪造 SQLite 数据验证 |
| 编译通过 | PASS | `dotnet build LoomX.slnx --no-restore`，0 错误 |
| 相关测试通过 | PASS | 20/20 定向测试、575/575 全量测试通过 |
| 关联 Superpowers Design Doc | SKIP | `.comet.yaml` 的 `design_doc` 为 `null`，本 change 使用自身 `design.md` |
| 安全检查 | PASS | 未新增密钥、Authorization、unsafe 或外部输入记录 |
| 自动代码审查 | SKIP | `review_mode: off`，按 hotfix 预设跳过 |

## 根因闭环

`GatewayViewModel` 现在按 route ID 从 `Combos` 搜索所属 Combo，删除和启用切换不再依赖默认展开卡片是否曾被点击。删除成功后仍在原有 mutation 锁内更新所属 Combo 列表并重编号。
