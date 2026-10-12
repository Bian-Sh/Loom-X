# task7.1 Playground 独立侧包交接

## 交付边界
- 共享 worktree：`D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge`。
- 只改 `LoomX.PluginPlayground/**`；不改其他项目、Tests、Runtime、Credential 业务源码或 Comet/SPEC 状态。
- 不连接真实 Provider、不访问生产配置/Vault、不发布、不提交 Git、不清理旧 outputs/临时目录。
- 共享目录内 Runtime、Tests、LoomX 桌面项目、Credential 与正式 change 的未提交/未跟踪改动属于其他 session，本侧包保持原样；不要整仓提交本侧包。

## 实现与覆盖
1. 原有 `GetPipeline is Pipeline` 检查改为 `runtime.Pipelines` 注册 metadata；执行仍经公共动态 Pipeline 接口。
2. 显式引用 `LoomX.Plugin.Storage.Sqlite`，装配公开 `SourcePluginCompiler`、`PluginPackageValidation`、Runtime 的 `SourceCompiler/BridgeFactory`。不引用 LoomX 桌面工程。
3. 源码夹具无 csproj/预编译 DLL，Manifest 显式声明单一 source 和三个 Bridge 能力；未声明错误源码不参与编译；首次编译生成 DLL/PDB，第二次命中缓存。
4. 源码插件实际执行参数化 CRUD，验证 Null/Integer/Real/Text/Blob 和完整 Blob 内容；覆盖提交、多命令原子事务、事务内单/多行查询、显式回滚、未提交释放回滚、唯一约束冲突后回滚、事务结束后拒绝复用、活动事务不混入非事务命令，以及路径穿越拒绝。
5. Logging/Notifications 使用 Playground 白名单 adapter，绑定来源、捕获安全模板/字段/Success 摘要。敏感字段与通知拒绝，未激活 union 字段载荷净化；宿主日志由 ILogger 内存捕获，不作为 CLI 业务诊断输出。
6. 源码成对 request/response、Entry/插件启停、旧动态引用取得新代际；reload 成功生效，编译失败/初始化失败保留当前实例，失败候选释放。
7. 旧租约继续访问原 SQLite 和原代际成对 Pipeline；释放租约后旧 Bridge/句柄拒绝复用，退休调用不创建数据库，新代际继续工作。
8. 按旧生产 SQL 表结构、SHA256、`dpapi:v1:` + CurrentUser DPAPI 格式构造本次专用历史 Vault；先恢复预置历史 token，再签发新 token。释放整个 runtime，创建第二个 runtime 指向同一隔离数据根，恢复旧 token 与前代新 token，验证稳定复用、旧密文/创建时间不变、两条映射无明文、无第二份 Vault。
9. 暂停 Credential 主动保护后，历史 placeholder 的完整性 system/developer 指令与响应恢复继续有效；普通业务原样通过；保留原有顺序、ContinueOnError、FailClosed 检查。

## 真实验证记录

| 日志 | PASS | FAIL | 退出码 | 说明 |
| --- | ---: | ---: | ---: | --- |
| `evidence/red-baseline.log` | 3 | 6 | 1 | 修改实现前运行原版 Playground，缺少 Credential Bridge |
| `evidence/red-extended.log` | 11 | 11 | 1 | 扩展断言先行，Runtime 尚未装配编译器/Bridge；不可用场景明确失败，不当作跳过通过 |
| `evidence/green-attempt-1.log` | 76 | 0 | 0 | 首次完整 GREEN |
| `evidence/green-repeat-release.log` | 76 | 0 | 0 | 新构建/新数据根，Release 再验 |
| `evidence/red-adapter-inactive-value.log` | 76 | 1 | 1 | standard 自查追加未激活日志 union 敏感载荷，真实复现本侧包 adapter 漏洞 |
| `evidence/green-final-release.log` | 77 | 0 | 0 | 修复后最终 Release GREEN |
| `evidence/green-final-repeat.log` | 77 | 0 | 0 | 最终版本再次以新构建/新数据根完整通过 |

- 全部控制台记录经过已知测试凭据、placeholder 与 DPAPI 密文前缀哨兵检查，没有泄漏；`verification-summary.json` 保存自动计数。
- `git diff --check -- LoomX.PluginPlayground` 通过。Git 的 LF/CRLF 提示只是工作区行尾提示，不是中文编码故障。
- 数据根是每次 `Path.GetTempPath()` 下新建的 `LoomX-Playground-task7.1-<guid>/`；其内 packages/data/cache 分离。构建使用独立 `--artifacts-path`，全部依赖输出与 obj 也在本次临时根。
- 这些 Vault 是合成旧格式夹具，不是用户真实 Vault，也不是启动旧版 Credential 二进制；只证明相同旧 SQL/DPAPI 格式在当前 Bridge 上兼容。

## 复验

```powershell
& 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/Verify-Playground.ps1' -RunLabel green-next-check
```

需 Windows（历史 Vault 检查使用 CurrentUser DPAPI）与构建所需 .NET SDK。脚本使用新隔离构建输出，保存命令、日期、85 项检查与真实退出码。`RunLabel` 必须使用新名称，脚本拒绝覆盖旧日志。

## 不背书的范围
- 源码编译调用的是宿主随附编译能力，但本次通过 dotnet run 构建入口，不作为 task7.4 发布包无 SDK 环境证明。
- 不代替 task7.2 完整 dotnet test、task7.3 发布布局/打包、task7.4 真实发布流程或 task7.5 全量 JSON/SSE/流式及卸载生命周期回归。
- 日志/通知 adapter 是夹具专用白名单，不是桌面组合根的生产 feedback 实现。**短 Basic/Bearer standard 复查残留仍待用户授权，本侧包不修改、不依赖、不为其修复背书。**
- 正式 tasks 7.1 checkbox 与 Comet 验证/归档记录交由主 session 接入，本侧包没有修改流程状态。

## 最终断言补强与真实自测
- 已补齐失败 reload 的 response 缺口：记录 g2 request/response 原 Extension 身份，编译失败及初始化失败后均确认身份不变，并在同一新租约内执行成对 Pipeline，确认仍为 g2；失败初始化候选使用 g3 以便区分。
- 已补齐最近可用 g2 缓存缺口：通过公共编译接口取得 g2 缓存身份，对其 DLL/PDB、cache.json/cache.sig 与全部负载计算 SHA256 摘要；编译失败和初始化失败后均确认整个缓存不变。
- `evidence/red-review-gap-mutation.log`：仅在本次 Playground/隔离缓存中临时模拟 g2 元数据损坏和 response 不执行，85 项中 83 PASS / 2 FAIL，退出码 1，新增断言真实捕获两个故障。公共 Runtime 未修改；故障注入代码已恢复，没有进入最终源码。
- `evidence/green-review-final.log`：2026-10-12 06:18:39 +08:00，85 PASS / 0 FAIL，退出码 0。
- `evidence/green-review-final-repeat.log`：2026-10-12 06:18:46 +08:00，新构建/新数据根再次 85 PASS / 0 FAIL，退出码 0。
- 先前的 77/77 是补强前版本的真实结果，仍保留；最终实现现在为 85 项检查。

## 范围纠正与验收归属
- 用户明确：本侧包只负责 task7.1 Playground 实施与自测，正式独立 standard 审查由主会话统一协调。本侧包不再启动或等待任何额外审查。
- 已产生的 `standard-review*.md` 全部原样保留，仅作非正式参考，不作为独立正式验收，不据此修改 Comet/SPEC checkbox 或宣称 formal standard 通过。
- 最初全仓只读入口后来正常完成；其结论有两项 P2 断言缺口，已在 Playground 内补强并完成上述真实故障注入 RED/GREEN。先前交接中关于「结束审查进程」的描述不准确，已纠正；没有删除或移动任何产物。
- 生产 feedback 短 Basic/Bearer 残留仍待用户授权，本侧包没有修改它，不依赖或为其背书。
- 本次完整文件清单见 `file-manifest.md`。所有隔离临时产物保留，构建/运行路径见日志；其他 session 文件和流程状态未修改，没有提交 Git。