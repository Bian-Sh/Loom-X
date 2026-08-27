param([switch]$Quiet)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $root 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json

if ($manifest.pages.Count -ne 5) { throw "manifest 必须包含 5 个页面，实际为 $($manifest.pages.Count) 个。" }
$active = @($manifest.pages | Where-Object status -eq 'active')
if ($active.Count -ne 5) { throw "active 页面必须为 5 个，实际为 $($active.Count) 个。" }
if (($manifest.pages.key | Sort-Object -Unique).Count -ne 5) { throw '页面 key 必须唯一。' }
if (($manifest.pages.path | Sort-Object -Unique).Count -ne 5) { throw '页面 path 必须唯一。' }

$allPages = @{}
foreach ($page in $manifest.pages) {
  $fullPath = Join-Path (Split-Path $root -Parent) $page.path
  if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "页面不存在：$($page.path)" }
  $allPages[(Split-Path $fullPath -Parent)] = $true
  $html = Get-Content -LiteralPath $fullPath -Raw -Encoding utf8
  if ($html -match '\.kun-design') { throw "页面仍引用 .kun-design：$($page.path)" }
  if ($html -notmatch '<title>[^<]+</title>') { throw "页面缺少有效 title：$($page.path)" }
  if (([regex]::Matches($html, '<h1\b')).Count -ne 1) { throw "页面必须恰好有一个 h1：$($page.path)" }
  if ($html -notmatch 'aria-current="page"') { throw "页面缺少当前导航标记：$($page.path)" }
  foreach ($match in [regex]::Matches($html, 'href="([^"#]+)"')) {
    $href = $match.Groups[1].Value
    if ($href -match '^(https?:|mailto:|javascript:|#)') { continue }
    $target = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $fullPath -Parent) $href))
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "失效内部链接：$($page.path) -> $href" }
  }
}

if (-not $Quiet) { Write-Output "原型校验通过：$($manifest.pages.Count) 个 active 页面，内部链接有效。" }
