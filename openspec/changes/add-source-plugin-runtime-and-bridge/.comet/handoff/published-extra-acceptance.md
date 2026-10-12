# 最新发布和依赖闭包验收

2026-10-12。task7.3/7.4已由主会话核对最新实际产物和原始机器可读结果后逐ID验收；正式进度44/44。未进入Verify/Archive，不宣称候选已正式放行。

最新自包含win-x64目录 outputs/20261012-094447，默认发布脚本exit0，未覆盖任何旧outputs。布局32/32：宿主Microsoft.Data.Sqlite/native e_sqlite3各一份，插件SQLite禁止负载0、各插件manifest与4语言完整，无根平铺，固定Roslyn/引用/Skill/runtime齐备。源码示例由发布实际开发工具创建、编译、测试并安装到本次隔离插件布局；不把预编译第一方插件误称为已随发布预安装源码示例。

发布二进制闭环证据 .comet/evidence/integration/sdkless-published-extra-20261012-094710-521-4c7d7c08/：两进程PID30456/33608，33+8断言、各exit0；cache_hit=true、SQLite计数2→3，同一AppDataPaths/plugins/sdkless-extra-5a1f11703e6e444b9722858d264c3554/probe.db。464/464发布负载哈希与24/24实际加载模块来源/hash正确；真实deps只加探针入口，runtimeconfig原件Hash相同，hosttrace自包含和app-localCoreCLR证据。构建探针用SDK，但运行不调用外部dotnet或SDK，PATH空、禁multilevel/roll-forward、运行副本无sdk，符合设计“不仅改PATH”的验证方式。不是物理无SDK设备、不是完整桌面程序启动或安装器验收；没有Provider/Vault/真实配置内容操作。trace长路径、探针glob两次夹具错误保留，不计产品RED。

Runtime/Feedback原审查者授权第2轮已分别通过，侧包报告中“仍执行中”只是侧包结束时的保守边界；本记录按主会话独立报告更新，不能以侧包自行放行。最新真实窗口UIA/PNG四语言、启停、详情、关闭见 outputs/ui-source-trust-extra-2026-10-12_09-47-00/README.md，真实视图但隔离壳，不冒称完整MainWindow。

## 暂存依赖闭包
仅暂存本需求83个明确实现/测试/报告路径，git write-tree得到c46172fc9996594c6346b661bd0e121aae28e4d2；导出独立source，不复制共享未跟踪、bin/obj、outputs或.comet临时状态。新目录D:/AppData/Temp/LoomX-platform-staged-63606e58298e43ea9c52546a978bb629/source，以正常布局完整重编dotnet test=1884/1884、0失败/跳过、exit0。原始results/STAGED-DEPENDENCY-CLOSURE.trx与tests.log保留，证明拟推送产品依赖闭包不依赖未提交文件。此快照tasks为42/44，仅随后补2项task勾选和文档/运行状态，无后续产品源码变化，不伪称快照原始勾选已44。

随后暂存44/44 tasks、刷新Design handoff和验收证据，中文提交推送，再Build guard→Verify唯一最终集成审查。原有非本包warnings不擅自修改。
