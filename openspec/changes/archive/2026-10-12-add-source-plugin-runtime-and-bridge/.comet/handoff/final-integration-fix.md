# 最终集成 I-1：候选 UI 发布前验证修复

2026-10-12。唯一最终集成首审 Pasteur 报告 IMPORTANT I-1；Comet 第1次 verify-fail 自动返回 Build，verify_failures=1、check_epoch=4。原 Runtime/Feedback 第2轮 PASS 与轮次完整保留，不重新首审，不额外占用其授权修复轮。

## 根因及最小实现

LoadPlugin 候选已验证 Extension，但只取得 UI provider 并订阅，没有读取及验证 UI；ReloadAsync 随后切换注册快照并退休旧实例，GetUiContributions 才迟后丢弃无效结构。故 schema=999 候选被错误发布。

唯一产品变化：PluginRuntime.cs 增加候选前置验证（29行）。在 UI 订阅和 Entry 注册之前，对四语言快照复用生产 PluginUiValidator，检查声明id/slot、重复/null/根/schema、缺少声明贡献及provider；失败返回null，既有finally清理候选实例/Bridge/ALC，既有ReloadFailed保留旧实例。首次加载不做此候选门禁，保留原降级；动态UI展示仍使用既有验证。没有改Runtime代际、Vault、SQL、Feedback、公开契约、spec或设计。

## 正式 TDD 与相邻回归

- UI-RED.trx：12例、10 fail / 2 pass；十项都失败于预期false但真实reload=true，不是加载或编译夹具错误。
- 新回归覆盖 schema、未声明id、slot、重复/null/缺root/缺贡献/provider、带敏感值异常、ja-JP专属无效，断言旧版本/卡片/Bridge资源与候选释放；另有合法切换与旧租约排空、启动降级两例。
- UI-GREEN.trx：61pass / 2fail；旧SlowUiPlugin夹具让新候选与旧在途读取共用ui-continue等待，导致候选结构读取10s超时。改ReadyUiPlugin仅让候选验证立即完成，旧等待和资源断言保持，UI-GREEN-VERIFIED.trx=63/63。
- BUILD-UI-FIX-FULL.trx：1895pass / 1fail；LeaseUiProbePlugin只实现CommandHandler却在manifest声明panel，缺provider。补合法provider/panel按钮，所有命令等待和资源释放断言保持，没有改产品或放宽门禁。
- UI-ADJACENT-FINAL.trx：71/71，0skip、exit0；上述所有失败现场保留。随后 BUILD-UI-FIX-VERIFIED.trx=1896/1896，0失败/跳过、真实Runtime exit0，前后输入摘要一致；solution build复验0warnings/0errors。

## 最新发布与端到端

最新发布 outputs/20261012-104237，默认脚本exit0，不覆盖任何旧目录。重新执行发布开发工具闭环及重启：layout32/32、两进程PID22092/19740、33+8断言、exit0、cache_hit=true、SQLite计数2→3；464/464发布文件hash一致；24/24实际加载关键托管/native来源及hash一致；产品runtimeconfig原Hash与self-contained/app-local runtime一致。证据 .comet/evidence/integration-ui-fix/published-20261012-104237/，不把构建探针使用SDK误称物理无SDK设备。

原生 CUA：最新发布真实 PluginsView/VM/Runtime隔离壳，PID12308/HWND12851234，路径已校验，背景启用→无效reload=False/version1/旧卡片仍显示→有效reload=True/version3/新卡片→日文→正常原生关闭；原始UIA和APP-only截图已查看；6关键产品/native hash一致。验收按钮和底部结果栏属于壳，不是产品新增UI或MainWindow自动隐藏Toast。无真实Provider/Vault/核心配置内容访问。证据 outputs/ui-reload-candidate-2026-10-12_10-48-00/。

## 独立审查与停止边界

复用原唯一最终集成 reviewer Pasteur，进行I-1第1次定向复查，不再重跑全部140文件审查；报告为final-integration-followup.md。首审final-integration-review.md及coverage-matrix.md原样保留；Pasteur定向复查正式PASS，I-1关闭，CRITICAL/IMPORTANT均0；原发布探针exit0、source/assembly邻界36/36、独立正式63/63及命令租约8/8；最终覆盖44/44 tasks、33/33 requirements、82/82 scenarios全PASS。未Verify PASS、未归档、未合并主spec或master。
