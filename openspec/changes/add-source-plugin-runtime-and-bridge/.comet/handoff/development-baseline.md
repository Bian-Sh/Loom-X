# 开发前基线

- 日期：2026-10-12
- 基线提交：ee8cbcc47a7bddcd864301d43458584db10662a7
- 阶段：Build，执行配置待确认。
- 本轮只执行原有测试，不修改实现、不创建实施计划、不写入缺失配置。

## 已执行检查

命令：dotnet test LoomX.Tests/LoomX.Tests.csproj --logger trx --results-directory <本 change 的独立基线目录> --verbosity minimal。

- 进程退出码：0。
- 总计：1450。
- 通过：1450。
- 失败：0。
- 跳过/未执行：0。
- SDK：10.0.400；目标框架：net10.0。
- 测试程序集 SHA256：5FCFF029865A1B9B0CAFEC9CB4080425BFE30AA626C786904F826ACB31450893。
- TRX：.comet/evidence/baseline-20261012/开发前基线.trx（只保留本地，不将完整测试数据提交到远端）。

本结果只是新实现前的兼容基线，不能证明新需求完成，也不能代替后续 TDD RED/GREEN 或 task 7.2 的实现后全量回归。

## 已核查的环境与风险

- 当前恢复的 Microsoft.Data.Sqlite/Core 版本为 10.0.0，SQLitePCLRaw 依赖为 2.1.11；尚未添加 Roslyn 依赖。
- NuGet restore 对 SQLitePCLRaw.lib.e_sqlite3 2.1.11 输出 NU1903 高严重性告警。实施 SQLite 宿主化时必须核对修复版本与提供程序兼容性，不能仅去重负载而忽略该告警。
- 原代码存在可空性、异步 Reader、未使用事件及测试同步等待告警；本轮未改动无关代码。
- 既有 scripts/publish-desktop.ps1 默认生成可读时间目录，发布 win-x64、自包含、多文件成品；后续直接复用默认新目录，避免指定已有 OutputDirectory 时触发其删除分支。
- 基线测试未改动生产数据库路径，不将测试通过等同于源码插件、Bridge 或热更新已经存在。

## 尚待确认

Runtime 的 configurationReadiness 仍明确列出 build_mode、tdd_mode；review_mode 为已确认的 standard。继续实施前须获得明确配置授权。
