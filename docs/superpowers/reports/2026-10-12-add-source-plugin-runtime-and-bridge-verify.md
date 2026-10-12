# 源码插件运行时与宿主 Bridge：完整验证报告

日期：2026-10-12（Asia/Shanghai）。策略：autonomous + TDD + standard 独立审查；verify_mode=full。

## 当前结论

**开发与完整验证完成，最终结论 PASS。1896/1896，0 fail、0 skip，exit 0；Build 守卫 14/14、Verify 守卫 5/5 均实际通过。** Runtime 已进入待归档确认阶段，本报告不是归档授权。

实现提交 `f066e16aeb5a2927337178902d13ba64e371deec` 与 I-1 修复提交 `d93074573d22be7d87503f55b3c7151dae54608f` 均已推送。审查基线为 `3160697236ba2301437f4a28aa863ec8eb0265b9`；后续本轮仅整理验证报告和公开 CLI 状态，不再改产品代码。

## 完整性、正确性与一致性

| 维度 | 当前结论 | 依据 |
|---|---|---|
| 完整性 | 44/44 任务，33/33 需求 | 三个 delta capability：source-plugin-runtime、plugin-host-bridge、plugin-development-skill；正式 tasks revision：`85195c241b77e3edefb7ab21edfb02a8adfea928b6ae64a276c1670079917127` |
| 正确性 | 独立逐项覆盖 82/82 场景 PASS | [首审覆盖矩阵](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/coverage-matrix.md) + [原 reviewer 定向复查补足](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/final-integration-followup.md) |
| 一致性 | 没有未关闭的阻塞性设计偏差 | [权威 design.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/design.md)；strict OpenSpec validate：valid=true、issues=[] |
| 最终独立审查 | PASS，CRITICAL 0、IMPORTANT 0 | 保留一次全 change 首审及一次 I-1 定向复查，不用专家分段 PASS 替代最终审查 |

proposal 目标没有缩减为 SQLite 去重：源码编译与缓存、版本化 Bridge、既有 Vault 兼容、开发工具/Skill、发布验证均在本次覆盖范围。原首审只有 42/44、31/33、81/82，通过修复和定向复查才补足；原报告、矩阵及历史不足没有删除或覆写。

## 最新真实检查

| 检查 | 实际结果 | 原始证据 |
|---|---|---|
| 提交后 solution build | exit 0，14 warnings、0 errors；输入前后摘要一致 | [最新 Build 日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/checks/5b7a8b2c-bae2-4265-b624-f79f09ae840b.log) |
| Build 阶段守卫 | 14/14 PASS，phase=verify、check_epoch=5 | `comet guard add-source-plugin-runtime-and-bridge build --apply --json` 的真实返回 |
| 修复后 Build scope 完整回归 | 1896/1896，0 fail、0 skip，exit 0 | [BUILD-UI-FIX-VERIFIED.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/BUILD-UI-FIX-VERIFIED.trx) |
| 最终 Verify scope 完整回归 | 1896/1896，0 fail、0 skip，exit 0；正常 bin 布局，Runtime 前后指纹一致 | [VERIFY-UI-FIX-FINAL.trx](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/VERIFY-UI-FIX-FINAL.trx)、[Verify 日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/checks/1ab26b98-426c-49ad-8e0f-9e0511e575b1.log) |
| Verify 阶段守卫 | 5/5 PASS，phase=archive、verify_result=pass、check_epoch=6 | [公开 CLI 原始返回](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/VERIFY-GUARD-FINAL.json) |
| Playground | 85/85，exit 0 | [最新实际日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/evidence/coordinator-ui-fix-20261012-1045.log) |
| 最新发布布局 | 32/32；宿主 SQLite 托管/原生实现各 1 份；插件禁止负载 0；无根目录平铺 payload | [布局结果](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/published-20261012-104237/plugin-layout-validation.json) |
| 发布二进制闭环与重启 | 两进程 33+8 项断言、exit 0；缓存命中，SQLite 计数 2→3；发布文件 464/464 hash 一致，实际加载模块 24/24 来源/hash 一致 | [最新发布探针证据](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/integration-ui-fix/published-20261012-104237/attempts/20261012-104629-662-17c0a35f/) |

最新 Build 输入摘要：`9d342ffb36a6fcc60568a824625d03c968603226226a80bc0e78cf79d7be0cd5`。日志 SHA-256：`9d33e6017f0c908b43293031e09e6b59b55e51968c9e7b5aa9b22e2c06eab5a3`。14 条警告包含可空性、未使用测试事件、同步等待分析器及异步 EndOfStream 分析器提示；本轮没有为了消除警告改动无关代码。此前增量构建的 0 warnings 仅为历史结果，不代表最新重新编译无警告。

最终 Verify：check_epoch=5、sequence=33、tier=full、reused=false；与 Build 相同输入摘要且 changedDuringExecution=[]。TRX SHA-256：`F7D577B65BAD2A96D67B2264613BA0BFAD522A1148848FE2E6B146A0DDF16CA5`；Verify 日志 SHA-256：`1bab568e54d766ae22cd500204c2b9c281e88be294b710915e4b7a4c6c14a99c`。

## 已授权修复与审查轮次

- Runtime：原 reviewer Mendel 第 2 轮定向复查 PASS，I3 关闭；正式相邻 24/24、原独立探针 4/4、敏感诊断及最终租约释放 2/2。[Runtime 复查](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/runtime-extra-followup.md)。
- Feedback：原 reviewer Maxwell 第 2 轮 PASS，I2 关闭；正式回归 61/61，独立相邻守卫 12 组通过。[Feedback 复查](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/feedback-extra-followup.md)。
- 用户追加一次修复/原 reviewer 复查的授权和第 1 轮历史完整保留；没有重置次数、换人或再开第 3 轮。正式测试例数与独立守卫组数分别记录。
- 全 change 最终集成：Pasteur 首审发现 IMPORTANT I-1；首次 verify-fail 自动回 Build。修复后由同一 reviewer 第 1 次定向复查 PASS。独立 source/assembly 邻界 36/36，正式窄跑 63/63 + 8/8，原发布探针重新执行 exit 0。[首审](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/final-integration-review.md)、[定向复查](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/final-integration-followup.md)。
- 非阻塞 MINOR：composition 测试的共享实例断言偏弱，以及计划 EOF 空行卫生建议。保留原分类，不冒称所有建议均已修复。

## I-1 根因、真实 TDD 与最小修复

旧实现先接受并发布 reload 候选，再在展示时丢弃无效 UI，错误退休仍可用的旧实例。唯一产品改动为 PluginRuntime.cs 的 29 行候选前置验证：在 UI 订阅、Entry 注册及快照发布前，对 zh-CN/zh-TW/ja-JP/en-US 使用既有 PluginUiValidator，检查 provider、null、id/slot、重复及声明贡献完整性。失败返回安全诊断并清理候选实例/Bridge/ALC，旧实例与租约继续有效；首次加载保留原 UI 降级。

真实 RED：12 例中 10 fail / 2 pass，十个失败均为预期 reload=false 但实际 true。随后正式相邻回归发现两种旧夹具问题：候选与旧在途 UI 共用等待闸门、命令租约夹具声明 panel 却没有 UI provider。只修夹具，等待/释放断言和产品门禁均未弱化。最后窄跑 63/63、相邻 71/71、Build 完整 1896/1896。

[修复说明](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/final-integration-fix.md)与[历史 TRX 计数/hash 索引](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/verify-evidence-summary.json)保留所有真实失败，没有用最终 PASS 擦除 RED 或夹具失败。

## 设计与安全边界

- design.md 已注册 role=technical-design、canonical_spec=openspec，不复制另一份权威设计。Design handoff hash：`d8db719913a358d7ee7b8f590deb939b2c764e0cb19004db35c4d570585c60c9`。
- 生产组合根把插件身份、AppDataPaths 独立数据根、ILogger 和同一 ToastService 接到 Bridge。SQLite 由宿主提供，插件自定 SQL/文件名，不引入统一 KV 或新 Vault。
- 保留 credential-tokens.db、CredentialTokens、DPAPI、OriginalHash、精确查表和 fail closed；普通禁用不等于退休，不提前销毁历史 placeholder 恢复资源。
- reload 使用不可变代际和在途租约；Provider request/response/SSE 保持同代际，候选失败保留旧实例。新增门禁验证四文化的候选时快照，不保证可信插件以后所有状态永远输出合法 UI。
- Logging 的 512 是含来源前缀的预展开预算，不是 Serilog quoting/escaping 后长度保证；模板仅允许 {Name}、{{、}}，拒绝 format/alignment。安全过滤是纵深防御，不承诺识别任意秘密。
- 源码插件执行可信 C#，具有当前用户进程权限；Bridge、ALC、测试数据隔离都不是恶意代码 OS 沙箱。

## 发布与原生 UI 验证的准确边界

最新成品：[outputs/20261012-104237](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/20261012-104237/)，由默认发布脚本创建新时间目录，没有覆盖旧产物。此前 09:44 发布与 09:47 四语言验收是历史证据；当前 reload 修复以 10:42 发布及新原生验收为准。

无 SDK 运行闭环：构建探针允许使用 SDK；实际进程用真实发布 apphost、app-local self-contained runtime，空 PATH、禁 multilevel/roll-forward、运行副本无 sdk 目录，保留 deps/runtimeconfig/hosttrace 和模块证据。这不是物理无 SDK 设备验收，也不是完整桌面主程序启动或安装器验收。

最新 CUA 隔离壳使用真实发布 PluginsView/VM/Runtime/App 资源，Hidden 启动、ShowActivated=false、绝对进程路径校验，全程 background，仅截 APP。确认启用 Warning → 无效 reload=False/version1/旧卡片仍可用 → 合法 reload=True/version3/新卡片 → 日文 → 正常原生关闭。六份原始 UIA、关键 PNG、事件日志和 6 个关键 DLL/native hash 一致记录均保留；窗口/session/本轮 daemon 已结束。[CUA 验收说明](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/ui-reload-candidate-2026-10-12_10-48-00/README.md)。底部反馈栏和测试按钮属于验收壳，不冒称产品新增 UI 或 MainWindow 自动隐藏 Toast。

没有调用真实 Provider、读取生产 Vault 或读取/写入核心配置内容，仅核对配置元数据不变；不依据透明主题截图武断判断配色。

## 历史失败与保留现场

- 早期完整测试用 --artifacts-path 重定位引发 151 项固定路径夹具失败；未改无关测试，回正常布局后 1884/1884。最终完整测试不重定位 bin 布局。
- I-1 正式 RED、相邻夹具失败与完整 1895 pass / 1 fail 均保留；修复后追加结果而非替换历史。
- 提交引起 Git 行尾归一化后，旧 Build fingerprint 失效；守卫拒绝通过。没有改源文件编码、绕过守卫或手改 Runtime 状态，已在提交后的工作树重新执行 Build 并实际通过 14/14。
- Verify 阶段 checkpoint 曾因只允许 active Build 被 CLI 拒绝；没有手改内部状态。原 checkpoint 反映保存时的待办，不替代最新阶段守卫。
- 发布/UI 探针的 glob、长路径、插值或 deps/runtimeconfig 夹具失败不计为产品 RED；所有证据及 outputs 保留。
- 多个未跟踪 evidence/checks/.comet/Playground 产物仍保留，没有清理其他会话文件，没有整目录暂存。原始大证据与发布产物留在本地；必要审查报告和小型索引精确提交。

## 阶段与交付

当前 `phase=archive`、`verify_result=pass`、`verified_at=2026-10-12`、`check_epoch=6`、`branch_status=pending`、`archive_confirmation=pending`、`archived=false`。这些状态由 Verify 守卫真实更新，没有手设 pass。Runtime 将连续 verify_failures 自动清零为 0；先前 I-1 首次失败和修复历史仍保留在报告及原始证据中。

归档仍未授权：不得合并三个 delta spec 到主 spec、标注文档或移动 change，不创建 PR、不合并 master。验证完成后将单独询问是否确认归档并立即推送当前绑定分支（不创建 PR）。
