param(
    [string]$Configuration = "Release",
    [string]$IdePath = "",
    [string]$OutputDirectory = "",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "LoomX\LoomX.csproj"
$outputsRoot = Join-Path $repositoryRoot "outputs"

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "找不到桌面项目：$projectPath"
}

if ($IdePath -and -not (Test-Path -LiteralPath $IdePath)) {
    throw "找不到 Visual Studio IDE：$IdePath"
}

if ($OutputDirectory) {
    $publishRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory
    } else {
        Join-Path $repositoryRoot $OutputDirectory
    }
    if (Test-Path -LiteralPath $publishRoot) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
} else {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $publishRoot = Join-Path $outputsRoot $stamp
    $suffix = 1
    while (Test-Path -LiteralPath $publishRoot) {
        $publishRoot = Join-Path $outputsRoot ("{0}-{1:D2}" -f $stamp, $suffix)
        $suffix++
    }
}

$publishDirectory = $publishRoot
$logPath = Join-Path $publishRoot "publish.log"

New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
@(
    "发布时间：$(Get-Date -Format o)",
    "Visual Studio：$(if ($IdePath) { $IdePath } else { '(未指定)' })",
    "项目：$projectPath",
    "运行时：win-x64",
    "自包含：true",
    "配置：$Configuration",
    "版本：$(if ($Version) { $Version } else { '(项目默认)' })"
) | Set-Content -LiteralPath $logPath -Encoding utf8

Write-Host "发布 Windows 桌面应用到：$publishDirectory"
$publishArguments = @(
    "publish", $projectPath,
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--property:PublishSingleFile=false",
    "--output", $publishDirectory,
    "--nologo"
)
if ($Version) { $publishArguments += "--property:Version=$Version" }
& dotnet @publishArguments 2>&1 | Tee-Object -FilePath $logPath -Append
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE。详情请查看：$logPath"
}

$generatedCrashDumper = Join-Path $publishDirectory "createdump.exe"
if (Test-Path -LiteralPath $generatedCrashDumper) {
    Remove-Item -LiteralPath $generatedCrashDumper -Force
}

$executables = @(Get-ChildItem -LiteralPath $publishDirectory -Filter *.exe -File)
if ($executables.Count -ne 1 -or $executables[0].Name -ne "LoomX.exe") {
    $names = if ($executables.Count -eq 0) { "(none)" } else { ($executables | ForEach-Object Name) -join ", " }
    throw "发布目录必须只包含 LoomX.exe，实际包含：$names"
}

Write-Host "发布目录：$publishDirectory"
