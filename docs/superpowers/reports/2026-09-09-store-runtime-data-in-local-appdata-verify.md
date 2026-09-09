# 运行时数据存放本地 AppData 验证报告

## 结论

验证通过。配置数据库、活动数据库和日志目录均由共享 `AppDataPaths` 固定到当前用户的 `%LOCALAPPDATA%\LoomX`，各入口复用同一路径。

## 验证证据

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| 任务完整性 | PASS | `tasks.md` 3/3 已勾选 |
| 路径合同 | PASS | `AppDataPathsTests` 验证 `LoomX.db`、`LoomX.Activity.db`、`logs` 及迁移锁路径 |
| 迁移与兼容 | PASS | `ApplicationDataMigrationTests`、`ConfigurationDatabaseMigrationTests` 覆盖迁移、幂等、WAL、损坏源和旧文件保留 |
| 定向测试 | PASS | 运行时路径与迁移测试共 9/9 |
| 全量测试 | PASS | `dotnet test LoomX.slnx --no-restore --no-build --nologo`，298/298 |
| 构建 | PASS | `dotnet build LoomX.slnx --no-restore --nologo`，0 错误、7 个既有警告 |
| 代码引用 | PASS | 服务端、桌面端、活动查询和日志初始化均引用 `AppDataPaths`，未发现回退到 `AppContext.BaseDirectory` 的运行时路径 |
| 安全 | PASS | 未新增敏感日志或密钥输出 |

## 分支收尾

改动已在 `master`，按用户既定要求记录为 `branch_status: handled`。

## 说明

本变更原先没有验证报告路径；本报告补齐该缺口并作为归档前的当前验证证据。
