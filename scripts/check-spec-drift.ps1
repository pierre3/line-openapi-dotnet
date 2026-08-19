<#
  LINE .NET クライアント — 上流 OpenAPI ドリフト検知 (spec drift detector)

  openapi/upstream-manifest.json に記録した「取り込み済み上流コミット (ref) と
  各 spec の LF 正規化内容ハッシュ」を真とし、上流 line/line-openapi の指定ブランチ
  (既定 main) の現在の内容と比較して、追従漏れ (drift) を検出する。
  加えて、上流ルートに存在するが manifest 未追跡の *.yml (新規 spec) も報告する。

  純検知のみ。再生成もファイル変更も行わない。ローカルと CI (spec-sync.yml) の両方から呼ぶ。

  出力:
    - 既定: 人間可読サマリを stdout へ。
    - -Json: 機械可読な JSON オブジェクトを stdout へ (サマリは stderr へ)。
  終了コード:
    - 0: 取り込み済み spec に drift 無し。
    - 1: 取り込み済み spec に drift あり (再生成が必要)。-FailOnAwareness 指定時は
         awareness 専用 spec (module-attach 等) の変化でも 1。
    - 2: 実行時エラー (manifest 不正・上流取得失敗など)。

  HTTP: gh CLI があれば `gh api` を優先 (CI の GITHUB_TOKEN・レート上限に有利)。
        無ければ GitHub REST を Invoke-RestMethod で直接叩く。

  重要 (このプロジェクト固有の落とし穴):
    手元 spec は歴史的に CRLF で保存され得るが上流 raw は LF。生バイトで比較すると
    全行が差分に見える (messaging-api だけで ~11,800 行の誤検知)。よって比較前に必ず
    改行を LF へ正規化してからハッシュする。.gitattributes(openapi/*.yml text eol=lf)
    と manifest の LF ハッシュはこの前提とセット。
#>
[CmdletBinding()]
param(
  [string]$ManifestPath,
  [string]$Branch = "main",
  [switch]$Json,
  [switch]$FailOnAwareness
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $ManifestPath) { $ManifestPath = Join-Path $root "openapi/upstream-manifest.json" }

# 取り込み時と同一の正規化 (LF + urn 引用符化) を共有ライブラリから読み込む。
. (Join-Path $PSScriptRoot "lib/SpecNormalization.ps1")

function Write-Info([string]$msg) { [Console]::Error.WriteLine($msg) }

# --- GitHub 取得ヘルパ (gh 優先、無ければ REST) ---
$script:UseGh = [bool](Get-Command gh -ErrorAction SilentlyContinue)

function Get-GhJson([string]$ApiPath) {
  if ($script:UseGh) {
    $raw = & gh api $ApiPath 2>$null
    if ($LASTEXITCODE -ne 0) { throw "gh api failed: $ApiPath" }
    return ($raw | ConvertFrom-Json)
  }
  $headers = @{ "User-Agent" = "line-openapi-dotnet-spec-drift"; "Accept" = "application/vnd.github+json" }
  if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $($env:GITHUB_TOKEN)" }
  return Invoke-RestMethod -Uri "https://api.github.com/$ApiPath" -Headers $headers
}

# spec の生内容 (UTF-8 文字列) を取得。正規化・ハッシュは共有ライブラリに委譲する。
function Get-SpecContentRaw([string]$Repo, [string]$Ref, [string]$Spec) {
  $obj = Get-GhJson "repos/$Repo/contents/$Spec`?ref=$Ref"
  if (-not $obj.content) { throw "no content for $Spec@$Ref" }
  $bytes = [System.Convert]::FromBase64String(($obj.content -replace "\s", ""))
  return [System.Text.Encoding]::UTF8.GetString($bytes)
}

# --- manifest 読み込み ---
if (-not (Test-Path $ManifestPath)) { Write-Info "manifest not found: $ManifestPath"; exit 2 }
try { $manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json } catch { Write-Info "manifest parse error: $_"; exit 2 }
$repo = $manifest.repository
$manifestRef = $manifest.ref
if (-not $repo -or -not $manifestRef) { Write-Info "manifest missing repository/ref"; exit 2 }

# --- 上流ブランチ最新 SHA ---
try { $latestSha = (Get-GhJson "repos/$repo/commits/$Branch").sha } catch { Write-Info "failed to resolve $Branch head: $_"; exit 2 }

# --- 区間コミット一覧 (人間向け文脈) ---
$commits = @()
if ($manifestRef -ne $latestSha) {
  try {
    $cmp = Get-GhJson "repos/$repo/compare/$manifestRef...$latestSha"
    foreach ($c in $cmp.commits) {
      $commits += [ordered]@{
        sha     = $c.sha.Substring(0, 8)
        date    = ($c.commit.committer.date).Substring(0, 10)
        message = ($c.commit.message -split "`n")[0]
      }
    }
  } catch { Write-Info "warn: compare failed (${manifestRef}...${latestSha}): $_" }
}

# --- spec 別ハッシュ比較 ---
$specNames = $manifest.specs.PSObject.Properties.Name
$results = @()
$importedDrift = $false
$awarenessDrift = $false
foreach ($spec in $specNames) {
  $entry = $manifest.specs.$spec
  $imported = [bool]$entry.imported
  $oldSha = $entry.sha256
  $status = "unchanged"
  $newSha = $null
  try {
    $newSha = Get-NormalizedSpecSha256 (Get-SpecContentRaw $repo $Branch $spec)
  } catch {
    # 取得失敗 (例: 上流で削除) は状態として記録し検知は継続。
    $status = "fetch-failed"
  }
  if ($status -ne "fetch-failed") {
    if (-not $oldSha) { $status = "no-baseline" }
    elseif ($newSha -ne $oldSha) { $status = "changed" }
    else { $status = "unchanged" }
  }
  if ($status -eq "changed" -or $status -eq "no-baseline") {
    if ($imported) { $importedDrift = $true } else { $awarenessDrift = $true }
  }
  $results += [ordered]@{
    spec = $spec; imported = $imported; status = $status; oldSha = $oldSha; newSha = $newSha
  }
}

# --- 上流ルートの新規 spec 検出 (manifest 未追跡の *.yml) ---
# 上流に spec が増えても manifest.specs を反復するだけでは気付けないため、上流ルートを
# 列挙して未知の *.yml を報告する。取り込み判断が要る＝awareness として扱い notify させる。
$unknownSpecs = @()
try {
  $rootItems = Get-GhJson "repos/$repo/contents`?ref=$Branch"
  # 追跡済み spec ＋ spec でない既知 yml (docker-compose 等) を除外集合とする。
  $ignored = @()
  if ($manifest.ignoredUpstreamFiles) { $ignored = @($manifest.ignoredUpstreamFiles) }
  $known = @($specNames) + $ignored
  foreach ($item in $rootItems) {
    if ($item.type -eq "file" -and $item.name -like "*.yml" -and ($known -notcontains $item.name)) {
      $unknownSpecs += $item.name
    }
  }
} catch { Write-Info "warn: could not list upstream root to detect new specs: $_" }
if ($unknownSpecs.Count -gt 0) { $awarenessDrift = $true }

$drifted = $importedDrift
$compareUrl = "https://github.com/$repo/compare/$manifestRef...$latestSha"

$report = [ordered]@{
  drifted        = $drifted
  awarenessDrift = $awarenessDrift
  repository     = $repo
  branch         = $Branch
  manifestRef    = $manifestRef
  latestSha      = $latestSha
  compareUrl     = $compareUrl
  specs          = $results
  unknownSpecs   = $unknownSpecs
  upstreamCommits = $commits
}

# --- サマリ出力 ---
$summaryLines = @()
$summaryLines += "Upstream spec drift check — $repo @ $Branch"
$summaryLines += "  manifest ref : $manifestRef"
$summaryLines += "  latest head  : $latestSha"
if ($manifestRef -ne $latestSha) { $summaryLines += "  compare      : $compareUrl" }
$summaryLines += "  specs:"
foreach ($r in $results) {
  $tag = if ($r.imported) { "" } else { " (awareness)" }
  $mark = switch ($r.status) { "changed" { "DRIFT " } "no-baseline" { "NEW?  " } "fetch-failed" { "FAIL  " } default { "ok    " } }
  $summaryLines += ("    {0}{1}{2}" -f $mark, $r.spec, $tag)
}
if ($unknownSpecs.Count -gt 0) {
  $summaryLines += "  new upstream specs not tracked in manifest:"
  foreach ($u in $unknownSpecs) { $summaryLines += ("    NEW   {0}" -f $u) }
}
if ($commits.Count -gt 0) {
  $summaryLines += "  upstream commits since manifest ref ($($commits.Count)):"
  foreach ($c in $commits) { $summaryLines += ("    {0} {1} {2}" -f $c.sha, $c.date, $c.message) }
}
$verdict = if ($drifted) { "RESULT: DRIFT (imported specs changed — regeneration needed)" }
           elseif ($awarenessDrift) { "RESULT: awareness-only change (un-imported spec changed)" }
           else { "RESULT: up to date" }
$summaryLines += $verdict
$summaryText = ($summaryLines -join "`n")

if ($Json) {
  Write-Info $summaryText
  $report | ConvertTo-Json -Depth 8
} else {
  Write-Output $summaryText
}

if ($drifted) { exit 1 }
if ($awarenessDrift -and $FailOnAwareness) { exit 1 }
exit 0
