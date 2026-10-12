# 源码插件运行时与宿主 Bridge：完整验证报告

日期：2026-10-12（Asia/Shanghai）。策略：autonomous + TDD + standard 独立审查；verify_mode=full。

## 当前结论

**最终集成 I-1 已修复，原 reviewer 定向复查 PASS；Build 全量1896/1896，正在重新进入 Verify，尚未完成最终阶段守卫。** 本文是验证阶段草稿，不构成归档或发布放行授权。实现提交 f066e16 已推送，审查基线为 3160697236ba2301437f4a28aa863ec8eb0265b9。

## 完整性

- 正式任务 44/44 完成，Comet task revision=85195c241b77e3edefb7ab21edfb02a8adfea928b6ae64a276c1670079917127；任务完成状态不靠本报告手工修改。
- 三个 delta capability：source-plugin-runtime、plugin-host-bridge、plugin-development-skill；33 requirements、82 scenarios 已由唯一最终集成 reviewer Pasteur逐条映射：原首审42/44任务、31/33需求、81/82场景；I-1定向复查补足后，最终44/44、33/33、82/82均PASS。
- proposal 目标没有缩减成 SQLite 去重；包括源码编译与缓存、版本化 Bridge、既有 Vault 兼容、开发工具/Skill、发布验证。

## 正确性与真实检查

| 检查 | 结果 | 原始证据 |
|---|---|---|
| Verify 正式完整测试 | 1884/1884，0 fail、0 skip，exit 0 | [VERIFY-FINAL.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/VERIFY-FINAL.trx) |
| Verify Runtime 检查 | 输入前后摘要一致，真实执行 | [检查日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/checks/a740db92-f537-4cd6-9662-b2b931dd3902.log) |
| 暂存树独立依赖闭包 | 1884/1884，exit 0 | 原 tree=c46172fc9996594c6346b661bd0e121aae28e4d2；随后只补最后两项任务与文档/状态，不称原快照已勾44项 |
| Playground | 85/85，exit 0 | [实际日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/evidence/coordinator-extra-final-20261012-0946.log) |
| 最新发布布局 | 32/32；宿主 SQLite 托管/原生实现各1份；插件禁止负载0 | [发布验收](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/published-extra-acceptance.md) |
| 发布二进制闭环 | 两进程33与8项断言，exit 0；重启命中缓存，SQLite计数2→3 | [发布证据目录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration/sdkless-published-extra-20261012-094710-521-4c7d7c08/) |

冷恢复时 Build scope 被判定 rerun-required，随后经公开 Comet CLI 实际执行 dotnet build LoomX.slnx，exit 0、0 warnings、0 errors，输入前后摘要一致。[本轮Build日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/checks/f626724b-15f0-4a69-b940-e89b224ec3ed.log)。严格 OpenSpec validate 返回 valid=true、issues=[]。

## 已授权修复与独立复查

- Runtime：原 reviewer Mendel 的第2轮定向复查 PASS，I3 关闭；正式相邻24/24、原独立探针4/4、敏感诊断和最终租约释放2/2。[原审查者报告](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/runtime-extra-followup.md)。
- Feedback：原 reviewer Maxwell 第2轮 PASS；真实输出正式回归61/61，独立相邻守卫12组通过；I2 关闭。[原审查者报告](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/feedback-extra-followup.md)。
- 历史第1轮及用户追加授权保留，没有换人、重置次数或合并“正式测试例”与“独立守卫组”的计数。
- 全 change 的最终集成首审由 Pasteur 执行，I-1第1次定向复查PASS，CRITICAL/IMPORTANT均0；[最终定向复查](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/final-integration-followup.md)。原首审和覆盖矩阵不覆盖删除，专家分段PASS不代替最终审查。

## 一致性与安全边界

- 技术设计注册在 change 内的 [design.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/design.md)，标注 role=technical-design、canonical_spec=openspec；不额外复制一份不同权威设计到 docs/superpowers/specs。Design handoff hash=d8db719913a358d7ee7b8f590deb939b2c764e0cb19004db35c4d570585c60c9。
- 生产组合根将插件身份、AppDataPaths 下的独立数据根、ILogger 与同一 ToastService 接到 Bridge。SQLite 托管/原生依赖由宿主提供；插件自定 SQL/文件名，不引入统一 KV 或新 Vault。
- 保留 credential-tokens.db、CredentialTokens、DPAPI、OriginalHash、精确查表与 fail closed；普通禁用不等于退休，不提前销毁历史 placeholder 恢复资源。
- reload 使用不可变注册代际和在途租约；Provider request/response/SSE 保持同代际，候选失败保留旧实例。
- Logging 的512是包含来源前缀的预展开预算，不是 Serilog quoting/escaping 后长度保证；模板仅 {Name}、{{、}}，拒绝 format/alignment。安全过滤是纵深防御，不承诺识别任意秘密。

## 发布与原生 UI 验证的准确边界

- 最新成品 [outputs/20261012-094447](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/20261012-094447/) 为默认脚本生成的新时间目录，未覆盖旧产物。发布文件464/464 hash一致，实际加载模块24/24来源/hash一致。
- 无 SDK 验证：探针构建允许SDK；实际进程使用发布apphost与app-local自包含runtime、PATH空、禁multilevel/roll-forward、运行副本无sdk，保留deps/runtimeconfig/hosttrace/模块证据。不是物理无SDK设备，也不是完整桌面主程序启动/安装器验收。
- CUA 原生隔离验收壳使用最新发布的真实 PluginsView/VM/Runtime/App资源，四语言披露、启用Warning、详情、停用、原生关闭，8份UIA树，仅截APP；6个关键DLL/native hash一致。[UI验收说明](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/ui-source-trust-extra-2026-10-12_09-47-00/README.md)。底部Toast TextBlock是验收壳，不冒称MainWindow自动隐藏渲染器。
- 未调用真实Provider、读取生产Vault或写入生产核心配置。可信源码插件运行在当前进程权限下，ALC/Bridge/测试数据隔离均不是恶意代码OS沙箱。

## 历史失败与保留现场

- 完整测试首次用 --artifacts-path 重定位造成151项旧固定路径夹具失败；未改无关测试，正常布局完整重编1884/1884。此失败不计产品TDD RED。
- 发布探针夹具glob、长路径及首个UI探针runtimeconfig匹配错误均保留，不计产品RED。最终通过使用正确发布runtimeconfig并校验实际进程路径。
- 保留其他会话及本轮 outputs、未跟踪 evidence/checks/.comet 状态；Git长路径warning不能作为清理授权。仅精确暂存本需求验证报告与合法状态/审查产物。

## 阶段与交付

当前 phase=verify、verify_result=pending、branch_status=pending。待最终审查、报告与 Runtime guard 全部通过才更新。归档、主spec合并与分支交付尚未获得独立授权，不创建PR、不合并master。

## 最终集成首审与修复记录

Pasteur 正式首审发现无效 UI 候选仍能 reload 成功；不是凭据泄漏，级别 IMPORTANT。42/44任务、31/33需求、81/82场景通过，不足全部归同一I-1。保留 [首审报告](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/final-integration-review.md) 与 [原覆盖矩阵](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/coverage-matrix.md)，后续以定向复查记录关闭，不覆盖历史首审。新正式回归真实 RED：12例，10 fail/2 pass，失败均为预期false但实际reload=true。

Verify阶段 checkpoint CLI 拒绝更新（仅允许Build）；未手改内部状态。已用 verify-fail 返回Build，verify_failures=1，check_epoch=4；任务勾选是历史完成状态，不能替代I-1关闭验收。

修复与最新证据详见 [I-1修复说明](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/final-integration-fix.md)；最新成品变为 [outputs/20261012-104237](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/20261012-104237/)。此前09:44发布/原生证据为历史验收，当前修复以10:42发布和新原生reload证据为准。

最新 Build Runtime完整回归1896/1896、0fail/skip、exit0，日志 [完整回归日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/checks/916edcb9-deed-442a-9ab0-5144c36e7615.log)；solution build [构建日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/checks/505ec74f-a77e-493c-bab3-3215cb75a6ef.log)，0warnings/0errors。最新Playground仍85/85，原生UIA已确认无效候选保留旧卡片、合法候选切换；[最新CUA说明](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/ui-reload-candidate-2026-10-12_10-48-00/README.md)。保存原始TRX计数与hash的 [证据索引](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/verify-evidence-summary.json)；并未把首次完整测试失败擦除。
