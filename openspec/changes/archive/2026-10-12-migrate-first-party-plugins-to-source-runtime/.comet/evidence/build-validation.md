# Build 验收与交付准备

## 范围与结论

两个第一方插件改为显式 `csharp-source` 负载；新增薄 Protection Bridge，宿主拥有 DPAPI，插件仍拥有 Vault SQL 与生命周期。没有新增平台后端，没有明文降级，没有改数据库位置、凭据 placeholder 语法或压缩业务。按计划完成相关验收，最终集成审查留在 Verify 唯一执行。

## 实际检查

| 项目 | 结果与证据 |
| --- | --- |
| Protection 契约与宿主适配器 | 真实缺失能力 RED 后 GREEN；默认兼容、旧五参数构造、能力授权、取消/退休、历史 DPAPI、安全失败和敏感日志测试已纳入相关全集 |
| 128次历史恢复日志 | RED=129事件；修复后首个/每64次 Debug，11/11 GREEN；原 reviewer 一次定向复查 PASS |
| 凭据业务 | storage/protection显式组合；历史Vault、冲突/事务回滚、失败不落明文、禁用历史恢复、重载测试通过 |
| 源码动态Runtime | 原assembly声明下13/13真实RED；源码13/13 GREEN；补充生产 PipelineRestoringStream/SsePipelineRestoringStream 跨5字节网络块、跨event工具参数及残缺EOF，总17项在最终全集通过 |
| 精确负载 | 布局4项真实RED后18/18；正式新包精确17/27文件，不含DLL/deps/SQLite/crypto，无平铺manifest/i18n |
| 旧源码夹具 | 不混用assembly副本；21/21通过；动态夹具只用实际测试镜像，不回落仓库源码 |
| 最终完整相关测试 | 直接默认输出 689/689，无失败无跳过；related-final.log。含全部链接夹具，未降安全断言 |
| Comet实际执行 | build --local 经 verify-related.ps1，680/680；checks/2d0a8d3e-9787-44bf-b925-72d41d544c3b.log。Comet子进程无法创建链接，明确排除9项；这9项由直接全集及direct-link-regression.log覆盖 |
| Playground | 独立fresh artifacts，85/85；playground-source.log |
| 全仓库尝试 | 未完成，UI Dispatcher同步调用停滞，终止本次host，退出1；隔离ReleaseHistory 14/14。详见full-test-hang-summary.md，不冒称全量通过 |
| 最终发布 | scripts/publish-desktop.ps1成功；outputs/source-plugin-migration-2026-10-12_19-11-28；final-desktop-publish.log、final-published-layout-check.txt |
| 完整产品UI | 最新包后台CUA：2插件/2启用/0诊断，两详情正常；不编辑真实配置、不调用Provider，详见ui-validation.md |
| 分段审查 | protection-runtime-review.md首审及唯一Runtime定向复查PASS；source-payload-review.md PASS；无待修重要问题。Feedback未消耗修复轮次 |
| 差异卫生 | git diff --check通过；有既存编译/分析器及行尾警告，不宣称零警告 |

## 提交与交付边界

- 仅提交本次实现、测试、正式change/plan、安全验收总结、合成测试日志与公开插件总览。
- 不提交真实设置页树/截图、初始活动树、dump/原始全栈、payload-artifacts、outputs、CodeGraph缓存或既存 LoomX.PluginPlayground/evidence/。
- 保留旧 outputs、所有本地证据及其他会话文件；不合并 master，不创建新 worktree。
- Build guard后执行full Verify与唯一最终独立审查；已有授权为归档并中文提交、推送当前绑定分支 origin，不创建PR。