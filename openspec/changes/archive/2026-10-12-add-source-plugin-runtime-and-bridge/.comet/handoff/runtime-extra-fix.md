# Runtime 额外授权修复

2026-10-12，按用户明确追加授权实施，不重置历史轮次。原 I3 的依赖解析构造异常发生在 Load 诊断 try 外。修改仅将 PluginLoadContext 构造置入既有 try，将变量改为 nullable，并使用 ?.Unload 安全清理。Load/Initialize/Capability 分类和逐资源隔离清理保持原样。

正式新增 PluginRuntimeReviewRegressionTests 损坏 deps 首次加载及 reload 两例；先真实构建运行 EXTRA-RED.trx（2失败、0通过）：首次结构化诊断为空，reload仅LX_RELOAD_FAILED。候选 manifest 本身验证通过、旧请求租约仍能工作，失败确为产品诊断缺失。

修复后 EXTRA-GREEN.trx 59/59，覆盖 RuntimeReviewRegression、RuntimeLease、ProviderGeneration、RegistrationAtomicity、PackageValidation、SourceRuntimeIntegration 范围，退出0。两次均使用 dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path .artifacts/runtime-extra-20261012；RED filter=FullyQualifiedName~损坏依赖清单在首次加载，GREEN为上述6类 OR filter。原始TRX在 .comet/evidence/runtime-extra/，未覆盖原证据、未回退代码伪造RED。

原审查者 Mendel 已恢复，将执行用户追加授权的第2轮定向复查（历史reviewRounds=1保持），未通过前不验收整个包。
