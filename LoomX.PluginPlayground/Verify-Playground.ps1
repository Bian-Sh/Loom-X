param(
    [ValidatePattern('^[a-z0-9-]+$')]
    [string]$RunLabel = 'green-verification'
)
$ErrorActionPreference = 'Stop'
# 每次隔离构建依赖输出与 obj，避免污染共享 worktree；不发布、不删除旧临时目录。
$buildRoot = Join-Path ([IO.Path]::GetTempPath()) ('LoomX-Playground-task7.1-build-' + [Guid]::NewGuid().ToString('N'))
$evidence = Join-Path $PSScriptRoot 'evidence'
New-Item -ItemType Directory -Path $buildRoot | Out-Null
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$log = Join-Path $evidence ($RunLabel + '.log')
if (Test-Path -LiteralPath $log) { throw '请使用新的 RunLabel，禁止覆盖已有验证记录。' }
$project = Join-Path $PSScriptRoot 'LoomX.PluginPlayground.csproj'
"验证开始：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')" | Tee-Object -FilePath $log
"命令：dotnet run --project $project --configuration Release --artifacts-path $buildRoot" | Tee-Object -FilePath $log -Append
dotnet run --project $project --configuration Release --artifacts-path $buildRoot 2>&1 | Tee-Object -FilePath $log -Append
$exitCode = $LASTEXITCODE
"真实退出码：$exitCode" | Tee-Object -FilePath $log -Append
"验证结束：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')" | Tee-Object -FilePath $log -Append
exit $exitCode
