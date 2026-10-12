param([string]$PublishedRoot = 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/20261012-094447')
$ErrorActionPreference = 'Stop'
$PublishedRoot = [IO.Path]::GetFullPath($PublishedRoot)
$attempt = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$attemptDirectory = Join-Path $PSScriptRoot "attempts/$attempt"
$null = New-Item -ItemType Directory -Path $attemptDirectory
$state = Join-Path ([IO.Path]::GetTempPath()) ('LoomX-published-sdkless-extra-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $state 'app'
$pluginId = 'sdkless-extra-' + [guid]::NewGuid().ToString('N')
$null = New-Item -ItemType Directory -Path $state
$baseline = @(Get-ChildItem -LiteralPath $PublishedRoot -Recurse -File | Where-Object { $_.Name -notin @('plugin-layout-validation.json','验收状态.md') } | ForEach-Object {
    [ordered]@{ relative=[IO.Path]::GetRelativePath($PublishedRoot,$_.FullName); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; length=$_.Length }
})
$baseline | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $attemptDirectory 'published-source-baseline.json') -Encoding utf8
$info = [ordered]@{ status='运行中，候选未放行'; date=(Get-Date -Format o); published=$PublishedRoot; root=$state; app=$app; pluginId=$pluginId; evidence=$attemptDirectory; buildSdk=(Get-Command dotnet).Source; runEnvironment=[ordered]@{ PATH=''; DOTNET_ROOT=$app; DOTNET_ROOT_X64=$app; DOTNET_MULTILEVEL_LOOKUP='0'; DOTNET_ROLL_FORWARD='Disable'; DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX='0' }; processes=@() }
$infoFile = Join-Path $attemptDirectory 'run-info.json'
function SaveInfo { $info | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $infoFile -Encoding utf8 }
SaveInfo
try {
    # SDK 仅构建探针；项目只 HintPath 引用本轮发布 DLL。
    $build = Join-Path $attemptDirectory 'probe-build'
    $project = Join-Path $PSScriptRoot 'PublishedProbe.csproj'
    if ((Get-Content -LiteralPath $project -Raw -Encoding UTF8) -match 'ProjectReference') { throw '禁止 ProjectReference' }
    & $info.buildSdk build $project --configuration Release --output $build --verbosity minimal "-p:BaseIntermediateOutputPath=$attemptDirectory/obj/" *> (Join-Path $attemptDirectory 'BUILD.log')
    $info['buildExit'] = $LASTEXITCODE
    SaveInfo
    if ($LASTEXITCODE -ne 0) { throw '探针构建失败；失败记录保留' }
    $null = New-Item -ItemType Directory -Path $app
    foreach ($item in Get-ChildItem -LiteralPath $PublishedRoot -Force) { Copy-Item -LiteralPath $item.FullName -Destination $app -Recurse }
    foreach ($name in @('PublishedProbe.exe','PublishedProbe.dll','PublishedProbe.pdb')) { Copy-Item -LiteralPath (Join-Path $build $name) -Destination $app }
    Copy-Item -LiteralPath (Join-Path $app 'LoomX.runtimeconfig.json') -Destination (Join-Path $app 'PublishedProbe.runtimeconfig.json')
    # 保留真实发布 deps 的所有条目，只添加探针入口，不替换产品或 runtime。
    $deps = Get-Content -LiteralPath (Join-Path $app 'LoomX.deps.json') -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
    $probeDeps = Get-Content -LiteralPath (Join-Path $build 'PublishedProbe.deps.json') -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
    $target = $deps.runtimeTarget.name
    if ($target -ne $probeDeps.runtimeTarget.name) { throw '探针与发布 runtimeTarget 不一致' }
    foreach ($key in $probeDeps.targets[$target].Keys) {
        if ($deps.targets[$target].ContainsKey($key)) { throw "探针 deps 与产品条目冲突：$key" }
        $deps.targets[$target][$key] = $probeDeps.targets[$target][$key]
        $deps.libraries[$key] = $probeDeps.libraries[$key]
    }
    $deps | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $app 'PublishedProbe.deps.json') -Encoding utf8
    if (@(Get-ChildItem -LiteralPath $app -Recurse -Directory -Filter sdk).Count -ne 0) { throw '发布副本含 sdk 目录' }
    foreach ($phase in @('CLOSED-LOOP','RESTART')) {
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = Join-Path $app 'PublishedProbe.exe'
        $psi.WorkingDirectory = $app
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
        $psi.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
        $psi.ArgumentList.Add($state)
        $psi.ArgumentList.Add($pluginId)
        if ($phase -eq 'RESTART') { $psi.ArgumentList.Add('restart') }
        foreach ($key in $info.runEnvironment.Keys) { $psi.Environment[$key] = $info.runEnvironment[$key] }
        foreach ($key in @('DOTNET_ADDITIONAL_DEPS','DOTNET_SHARED_STORE','DOTNET_STARTUP_HOOKS','DOTNET_HOST_PATH')) { $null = $psi.Environment.Remove($key) }
        $psi.Environment['COREHOST_TRACE'] = '1'
        # native host 的文件接口不接受本证据目录的长路径，先写本次短临时根。
        $traceFile = Join-Path $state ($phase + '-HOSTTRACE.log')
        $psi.Environment['COREHOST_TRACEFILE'] = $traceFile
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $psi
        $started = Get-Date -Format o
        $null = $process.Start()
        $actualPid = $process.Id
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(180000)) { throw "探针超时 PID=$actualPid；不自动杀进程，保留现场" }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $outFile = Join-Path $attemptDirectory ($phase + '.log')
        [IO.File]::WriteAllText($outFile, $stdout + "`n标准错误：`n" + $stderr, [Text.UTF8Encoding]::new($false))
        $assertions = ([regex]::Matches($stdout, '(?m)^\[通过\]')).Count
        $record = [ordered]@{ phase=$phase; pid=$actualPid; started=$started; ended=(Get-Date -Format o); exit=$process.ExitCode; assertions=$assertions; file=$psi.FileName; arguments=@($psi.ArgumentList); log=$outFile }
        $info.processes += $record
        if (Test-Path -LiteralPath $traceFile) { Copy-Item -LiteralPath $traceFile -Destination (Join-Path $attemptDirectory ($phase + '-HOSTTRACE.log')) } else { throw '探针 host trace 文件缺失；保留本次日志' }
        SaveInfo
        Write-Output "${phase}：PID=$actualPid，exit=$($process.ExitCode)，断言=$assertions"
        if ($process.ExitCode -ne 0) { throw "实际运行失败：$phase；保留日志，不修改产品" }
    }
    $hashes = @($baseline | ForEach-Object {
        $source = Join-Path $PublishedRoot $_.relative
        $copy = Join-Path $app $_.relative
        $sourceNow = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $copyHash = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash
        [ordered]@{ relative=$_.relative; before=$_.sha256; publishedNow=$sourceNow; runtimeCopy=$copyHash; passed=($_.sha256 -eq $sourceNow -and $sourceNow -eq $copyHash) }
    })
    $hashes | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $attemptDirectory 'published-hashes.json') -Encoding utf8
    foreach ($name in @('closed-loop-loaded.json','restart-loaded.json')) { Copy-Item -LiteralPath (Join-Path $state $name) -Destination $attemptDirectory }
    $info['hashesPassed'] = @($hashes | Where-Object passed).Count
    $info['hashesFailed'] = @($hashes | Where-Object { !$_.passed }).Count
    if ($info.hashesFailed -ne 0) { throw '发布原件/运行副本 Hash 不一致' }
    $info.status = '侧包 DONE；候选未放行；Runtime/Feedback 第二轮仍执行中'
    SaveInfo
    Write-Output "DONE：$attemptDirectory；发布 Hash $($info.hashesPassed)/$($hashes.Count) 一致；只作侧包证据"
} catch {
    $info.status = '本次侧包失败，候选未放行；保留全部现场'
    $info['failure'] = $_.Exception.Message
    SaveInfo
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $attemptDirectory 'FAILURE.log') -Encoding utf8
    throw
}