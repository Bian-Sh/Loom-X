# 任务清单

## 1. 删除 maskit，成果并入凭据保护

- [x] `SensitiveRuleStore` 增加 `UpdateRule`（补齐"改"能力）
- [x] 迁入 21 项内置形态规则基线 `CredentialBuiltinRules`（只可开关）
- [x] 迁入自定义密钥前缀 `SecretPrefixes`
- [x] 规则存储增加 `Changed` 事件
- [x] `CredentialEngine` 编译并应用内置形态规则与密钥前缀
- [x] 修复规则加载回归：`rules = Load()`（曾误写成先置空再丢弃返回值）
- [x] 删除 `plugins/LoomX.Maskit` 与三个 maskit 测试文件
- [x] 清理 `LoomX.slnx`、`LoomX.csproj`、`LoomX.Tests.csproj` 引用与打包 target
- [x] 清理输出目录中的 `plugins/loomx.maskit`

## 2. 停用改为语义停用

- [x] `LoomX.Plugin.Abstractions` 新增 `IPluginEnableStateAware`
- [x] `PluginRuntime.SetPluginEnabled` 对实现该契约的插件改为回调，不关闭 Entry
- [x] `CredentialEngine.ActiveProtectionEnabled` 开关，Sanitize 遵循、Restore 不遵循
- [x] `CredentialProtectionPlugin.OnPluginEnabledChanged` 同步引擎与观测状态
- [x] `PluginsViewModel.CanToggle` 放开为 `true`
- [x] 四种语言 `plugins.credential.lifecycle` 改为说明停用语义

## 3. 完善设置页

- [x] Manifest 声明交互式 `detail-body`（settings，interactive: true）
- [x] 设置页区块：新增规则 / 主动保护状态 / 自定义规则 / 内置形态规则 / 自定义密钥前缀
- [x] 停用利害关系三段声明（停用行为、保留能力、停用代价）
- [x] 命令处理 `CredentialSettingsCommands`（增删改查 + 内置开关 + 前缀）
- [x] 多语言文案 `CredentialSettingsText`（简中/繁中/日/英）
- [x] 规则值列表打码、编辑态明文；命令回执不回显规则值

## 4. 测试与验证

- [x] `CredentialProtectionDisableTests`（7 项）覆盖停用语义边界
- [x] `CredentialProtectionSettingsTests`（12 项）覆盖设置页 CRUD 与文案声明
- [x] 更新 3 处过时断言（manifest 贡献数、发布契约、启停契约）
- [x] 实机验证：插件页只剩凭据保护、开关可交互、设置页四区块渲染正常

## 5. 嵌套引用安全缺陷

- [x] 定位根因：随机后缀约 2.28% 概率被内置 `IBAN` 形态规则命中
- [x] `SanitizeText` 改为单遍区间扫描，形态规则按先窄后宽竞争区间
- [x] 已签发引用保护区：历史placeholder 逐字符透传，不二次 token 化
- [x] 敏感字段值为纯引用时不再包一层
- [x] `CredentialProtectionIdempotencyTests`（5 项）固化契约
- [x] 全量测试 1351 项连续三轮 0 失败

## 遗留

- [ ] 内置形态规则之间未逐一验证跨形态误命中（当前只发现并修复了 LANDLINE）
- [ ] `AvaloniaTestBootstrap.Ensure` 只在首个测试线程绑定 Dispatcher，同 collection 跨测试类可能落在不同线程。测试数量增长到一定程度会暴露 `Call from invalid thread`（本轮曾因新增测试耗时过长触发 6 项）。建议后续让每个 Avalonia 测试显式在 `Dispatcher.UIThread` 上执行。