# 追加授权后的 Build 验收

2026-10-12，目标仍为全部44项。Runtime 原Mendel与Feedback原Maxwell分别在用户授权第2轮通过定向复查，历史1轮保留，不换人、不重置额度。原I1/I2/I3及Feedback短Basic/Bearer全部关闭。

## 已逐ID验收
- 1.4/1.5、2.7/2.8、4.6–4.9/4.11：Runtime初审+第一轮I1/I2关闭+本轮I3关闭；Storage侧原94/94复查保留，Runtime提供程序拒绝与真实资源退休闭环补全。
- 3.1/3.2/3.3：Feedback原审查与本轮61/61独立复跑+12组相邻守卫通过；实际输出安全，512/template文档准确。
- 5.1–5.6：credential-review.md既有145/145独立及真实旧Store兼容、跨chunk/SSE守卫保留，当前全量验证依赖闭包；不改Vault文件/SQL/DPAPI。
- 6.1–6.6：tools-review.md既有42/42限定首审，生产工具组合根已与Runtime/Bridge依赖闭包整合；本轮只校准Skill反馈口径，由Maxwell核验。
- 7.1：最新Playground 85/85，新临时数据/artifacts，log=coordinator-extra-final-20261012-0946.log；不把其adapter当生产Feedback证据。
- 7.2/7.5：Runtime执行完整dotnet test=1884/1884、0跳过、exit0，INTEGRATION-EXTRA-VERIFIED.trx；凭据安全生命周期原回归、跨chunk/SSE/未知损坏token包含在本轮完整回归内。

现42/44；7.3发布布局、7.4最新发布二进制无SDK闭环正在侧包验收，未提前勾。既有公开契约、SQLite、Compiler15项完成记录不重做。

## 最新全量失败与正确边界
INTEGRATION-EXTRA-FINAL.trx=1733pass/151fail，是首次把完整测试输出重定位到.artifacts导致旧固定../../../../源码查找落到.artifacts/LoomX等不存在目录，以及依赖根manifest夹具定位错误。不是产品TDD RED。根因核对所有失败均文件定位；保持测试源码不变，正常bin布局重编再跑1884/1884。Comet check run必须用PowerShell参数数组保留--分隔；两次直接输入被shim吞掉分隔符返回Usage，没运行子命令，不计测试。

最新发布 outputs/20261012-094447；最新CUA隔离壳 outputs/ui-source-trust-extra-2026-10-12_09-47-00/README.md、8树与窗口PNG、哈希：四语言、启用Warning、详情、停用、正常关闭，非完整MainWindow/安装入口。不声称物理无SDK设备或恶意代码沙箱。

## 流程与提交
当前Build，未Verify/Archive。正式tasks只由comet task-complete逐ID写入，revision保持85195c241b77e3edefb7ab21edfb02a8adfea928b6ae64a276c1670079917127。后续只暂存本change明确产品/测试/报告，不整仓纳入临时state/evidence；先验证暂存快照依赖闭包，再中文提交推送。
