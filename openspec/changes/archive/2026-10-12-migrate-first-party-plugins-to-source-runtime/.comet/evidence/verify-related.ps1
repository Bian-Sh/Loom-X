$ErrorActionPreference='Stop'
Set-Location -LiteralPath 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge'
# Comet 子进程不允许创建链接；九项链接夹具已由正常直接运行覆盖，完整测试另有独立日志。
$filter='(FullyQualifiedName~LoomX.Tests.Plugins|FullyQualifiedName~ProviderExecutionPipelineTests)&FullyQualifiedName!~Junction&FullyQualifiedName!~打开之后出现侧车链接&FullyQualifiedName!~首审M2_受控置换'
& dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter $filter
exit $LASTEXITCODE