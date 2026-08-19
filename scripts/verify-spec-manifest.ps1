<#
  LINE .NET クライアント — manifest ↔ 同梱 spec ハッシュ整合ガード

  openapi/upstream-manifest.json に記録した sha256 が、実際の同梱 openapi/*.yml を
  取り込み時と同一の正規化 (LF + urn 引用符化) でハッシュした値と一致することを検証する。

  なぜ要るか: ドリフト検知 (check-spec-drift.ps1) は「上流 vs manifest.sha256」しか見ず、
  「manifest.sha256 == 同梱ファイル」は誰も保証していない。手編集・マージ・autocrlf すり抜けで
  両者が乖離すると、誤った baseline を真として false「up to date」(生成コードが実ファイルより
  古いのに無警告) や false-drift を招く。CI の PR ゲートで安価に固定する。

  ネットワーク不要 (ローカルのファイルのみ)。不一致で exit 1、実行時エラーで exit 2。
#>
[CmdletBinding()]
param([string]$ManifestPath)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $ManifestPath) { $ManifestPath = Join-Path $root "openapi/upstream-manifest.json" }
. (Join-Path $PSScriptRoot "lib/SpecNormalization.ps1")

if (-not (Test-Path $ManifestPath)) { Write-Error "manifest not found: $ManifestPath"; exit 2 }
try { $manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json } catch { Write-Error "manifest parse error: $_"; exit 2 }

$mismatch = $false
foreach ($name in $manifest.specs.PSObject.Properties.Name) {
  $entry = $manifest.specs.$name
  # awareness 専用 (imported:false) はファイルを同梱しないので検証対象外。
  if (-not $entry.imported) { continue }
  $path = Join-Path $root "openapi/$name"
  if (-not (Test-Path $path)) { Write-Host "MISSING  $name (imported but not vendored)"; $mismatch = $true; continue }
  $actual = Get-NormalizedSpecSha256 -Text (Get-Content $path -Raw)
  if ($actual -ne $entry.sha256) {
    Write-Host "MISMATCH $name"
    Write-Host "  manifest: $($entry.sha256)"
    Write-Host "  file    : $actual"
    $mismatch = $true
  } else {
    Write-Host "ok       $name"
  }
}

if ($mismatch) {
  Write-Host "`nmanifest and vendored specs are OUT OF SYNC. Run: pwsh scripts/generate.ps1 -Update"
  exit 1
}
Write-Host "`nmanifest matches vendored specs."
exit 0
