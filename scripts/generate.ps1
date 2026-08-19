<#
  LINE .NET クライアント — Kiota 生成スクリプト (Windows / PowerShell)

  前提:
    - .NET SDK 10
    - Kiota CLI:  dotnet tool install --global Microsoft.OpenApi.Kiota --version 1.34.1
    - リポジトリルートで実行すること

  仕様の取得とバージョン追従:
    同梱 openapi/*.yml は openapi/upstream-manifest.json の ref(コミット SHA) を
    正規化した確定スナップショット。既定（-Update なし）は同梱 spec で再現生成する。
    -Update で上流から SHA ピン再取得し manifest を更新する（詳細は設計 §9）。
    ドリフト検知は scripts/check-spec-drift.ps1。
#>
param(
  # CLI 版ピンと不一致でも続行したい場合に指定（再現性を意図的に外す時のみ）。
  [switch]$AllowKiotaVersionMismatch,
  # 上流 line/line-openapi から spec を再取得し openapi/upstream-manifest.json を更新する。
  # 未指定なら同梱スナップショット（openapi/*.yml）をそのまま使う＝再現可能なローカル再生成。
  [switch]$Update,
  # -Update 時に取得する上流コミット SHA（ピン）。未指定なら -Branch の先端を解決して採用。
  [string]$Ref,
  # -Ref 未指定時に先端解決するブランチ（上流の既定ブランチは main）。
  [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 取り込み時正規化（LF + urn 引用符化）を検知器 check-spec-drift.ps1 と共有する。
. (Join-Path $PSScriptRoot "lib/SpecNormalization.ps1")
$manifestPath = Join-Path $root "openapi/upstream-manifest.json"

# --- R3: 生成 CLI 版のピン止め ---
# 生成物の再現性のため Kiota CLI 版を固定する。ランタイム(KiotaBundleVersion, Directory.Build.props)
# とロックステップで運用する。上げる時は本値・KiotaBundleVersion・生成物・公開 API 差分をセットで見る。
# 方針判断は docs/R3-kiota-version-policy.md 参照。ランタイムは 2.0.0 へ移行済み（Directory.Build.props の
# KiotaBundleVersion）。CLI は 2.x 未リリースのため 1.34.1 据え置き（CLI とランタイムは別系統バージョニング）。
$ExpectedKiotaCliVersion = "1.34.1"
$actual = (& kiota --version) 2>&1 | Select-Object -First 1
$actualVersion = ($actual -split '\+')[0].Trim()
if ($actualVersion -ne $ExpectedKiotaCliVersion) {
  $msg = "Kiota CLI 版が想定と不一致: expected $ExpectedKiotaCliVersion, actual $actualVersion。" +
         " `dotnet tool update --global Microsoft.OpenApi.Kiota --version $ExpectedKiotaCliVersion` で合わせてください。"
  if ($AllowKiotaVersionMismatch) { Write-Warning $msg }
  else { throw $msg }
} else {
  Write-Host "Kiota CLI $actualVersion (pinned) を使用します。" -ForegroundColor Green
}

# --- spec スナップショット（openapi/）とバージョンアンカー（manifest）---
# 上流 line/line-openapi はタグ/リリースを持たず spec の info.version も実質固定値のため、
# 「どの上流コミットを取り込んだか」は openapi/upstream-manifest.json の ref（コミット SHA）で
# 一元管理する。同梱の openapi/*.yml はその ref を LF + urn 正規化した確定スナップショット。
if (-not (Test-Path $manifestPath)) { throw "manifest not found: $manifestPath" }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$repo = $manifest.repository
New-Item -ItemType Directory -Force -Path "openapi" | Out-Null

# gh api を呼び raw 文字列を返す（失敗時 $null）。ErrorActionPreference=Stop 下でも
# パイプ直結の ConvertFrom-Json が先に例外を投げてフォールバックを潰さないよう、
# 「代入 → $LASTEXITCODE 判定 → parse」の順に分離する。
function Invoke-GhApiRaw([string]$ApiPath) {
  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { return $null }
  $raw = & gh api $ApiPath 2>$null
  if ($LASTEXITCODE -ne 0 -or -not $raw) { return $null }
  return ($raw | Out-String)
}

# 上流から raw テキストを取得（gh 優先・無ければ Invoke-WebRequest）。SHA ピンで取得する。
function Get-UpstreamSpecText([string]$SpecRef, [string]$Spec) {
  $ghRaw = Invoke-GhApiRaw "repos/$repo/contents/$Spec`?ref=$SpecRef"
  if ($ghRaw) {
    $obj = $ghRaw | ConvertFrom-Json
    if ($obj.content) {
      $bytes = [System.Convert]::FromBase64String(($obj.content -replace "\s", ""))
      return [System.Text.Encoding]::UTF8.GetString($bytes)
    }
  }
  $url = "https://raw.githubusercontent.com/$repo/$SpecRef/$Spec"
  return (Invoke-WebRequest -Uri $url -UseBasicParsing).Content
}

if ($Update) {
  # 上流から再取得 → 取り込み時正規化（LF + urn 引用符化）→ 同梱 spec を更新 → manifest を更新。
  $targetRef = if ($Ref) { $Ref } else {
    $ghRaw = Invoke-GhApiRaw "repos/$repo/commits/$Branch"
    if ($ghRaw) { ($ghRaw | ConvertFrom-Json).sha }
    else { (Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/commits/$Branch" -Headers @{ "User-Agent" = "line-openapi-dotnet" }).sha }
  }
  Write-Host "updating specs from $repo @ $targetRef" -ForegroundColor Cyan
  foreach ($name in $manifest.specs.PSObject.Properties.Name) {
    $entry = $manifest.specs.$name
    $raw = Get-UpstreamSpecText $targetRef $name
    $normalized = ConvertTo-NormalizedSpec -Text $raw
    $sha = Get-NormalizedSpecSha256 -Text $raw
    # imported spec のみ openapi/ にファイルとして展開（Kiota 生成対象）。
    # awareness 専用（module-attach 等）は展開せずハッシュだけ追跡する。
    if ($entry.imported) {
      [System.IO.File]::WriteAllText((Join-Path $root "openapi/$name"), $normalized)
    }
    $entry.sha256 = $sha
  }
  $manifest.ref = $targetRef
  $manifest.retrievedAt = (Get-Date -Format "yyyy-MM-dd")
  # 上流コミット日も更新（可能なら）。
  try {
    $ghRaw = Invoke-GhApiRaw "repos/$repo/commits/$targetRef"
    $c = if ($ghRaw) { $ghRaw | ConvertFrom-Json }
         else { Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/commits/$targetRef" -Headers @{ "User-Agent" = "line-openapi-dotnet" } }
    $manifest.refDate = ($c.commit.committer.date).Substring(0, 10)
  } catch { Write-Warning "could not resolve commit date for $targetRef" }
  # LF 固定で書く（.gitattributes と併せ Windows/CI 間の改行 churn を防ぐ）。
  $manifestJson = (($manifest | ConvertTo-Json -Depth 10) -replace "`r`n", "`n") + "`n"
  [System.IO.File]::WriteAllText($manifestPath, $manifestJson)
  Write-Host "manifest updated: ref=$targetRef" -ForegroundColor Green
} else {
  # 非 Update: 同梱スナップショットを使う。欠損時のみ manifest.ref から取得（再現性のため SHA ピン）。
  foreach ($name in $manifest.specs.PSObject.Properties.Name) {
    $entry = $manifest.specs.$name
    if (-not $entry.imported) { continue }
    $path = Join-Path "openapi" $name
    if (-not (Test-Path $path)) {
      Write-Host "downloading $name @ $($manifest.ref) ..."
      $raw = Get-UpstreamSpecText $manifest.ref $name
      [System.IO.File]::WriteAllText((Join-Path $root "openapi/$name"), (ConvertTo-NormalizedSpec -Text $raw))
    }
  }
}

function Invoke-Kiota { param([string[]]$KiotaArgs)
  Write-Host "kiota $($KiotaArgs -join ' ')" -ForegroundColor Cyan
  & kiota @KiotaArgs
  if ($LASTEXITCODE -ne 0) { throw "kiota failed ($LASTEXITCODE)" }
}

# 1) Messaging 制御系 (api.line.me) — /content を除外
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/messaging-api.yml",
  "--exclude-path","**/content","--exclude-path","**/content/**",
  "-c","MessagingApiClient","-n","Line.OpenApi.Messaging.Generated.Api",
  "-o","./src/Line.OpenApi.Messaging/Generated/Api",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json"
)

# 2) Messaging データ系 (api-data.line.me) — /content のみ（生成後 BaseUrl 上書き）
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/messaging-api.yml",
  "--include-path","**/content","--include-path","**/content/**",
  "-c","MessagingBlobApiClient","-n","Line.OpenApi.Messaging.Generated.Blob",
  "-o","./src/Line.OpenApi.Messaging/Generated/Blob",
  "--exclude-backward-compatible"
)

# 3) Channel Access Token — form-urlencoded を含める
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/channel-access-token.yml",
  "-c","ChannelAccessTokenClient","-n","Line.OpenApi.ChannelAccessToken.Generated",
  "-o","./src/Line.OpenApi.ChannelAccessToken/Generated",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json","--structured-mime-types","application/x-www-form-urlencoded"
)

# 4) Webhook — モデル専用。
#    注意: /callback を除外するとモデルが一切生成されない（モデルはこの唯一の
#    オペレーション経由で参照され生成されるため）。よって /callback は残し、
#    生成される callback メソッド（server が example.com）は使わずモデルのみ利用する。
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/webhook.yml",
  "-c","WebhookModels","-n","Line.OpenApi.Messaging.Webhook.Generated",
  "-o","./src/Line.OpenApi.Messaging.Webhook/Generated",
  "--exclude-backward-compatible"
)

# 5) LIFF
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/liff.yml",
  "-c","LiffApiClient","-n","Line.OpenApi.Liff.Generated",
  "-o","./src/Line.OpenApi.Liff/Generated",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json"
)

# 6) Insight — 統計・分析。api.line.me 単一ホスト、全 GET・JSON。
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/insight.yml",
  "-c","InsightApiClient","-n","Line.OpenApi.Insight.Generated",
  "-o","./src/Line.OpenApi.Insight/Generated",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json"
)

# 7) Module — モジュールチャネル（LOA 代理運用）。api.line.me 単一ホスト、JSON。
#    注意: module-attach.yml（manager.line.biz / Basic 認証 / form+PKCE）は今回未取り込み。
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/module.yml",
  "-c","ModuleApiClient","-n","Line.OpenApi.Module.Generated",
  "-o","./src/Line.OpenApi.Module/Generated",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json"
)

# 8) Shop — ミッションスタンプ送信。api.line.me 単一ホスト、JSON。
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/shop.yml",
  "-c","ShopApiClient","-n","Line.OpenApi.Shop.Generated",
  "-o","./src/Line.OpenApi.Shop/Generated",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json"
)

# 9) ManageAudience 制御系 (api.line.me) — /upload/byFile を除外（データ系）。
#    Messaging と同じ control/data 2 クライアント分離（R1）。data 系は byFile の
#    ファイルアップロード 2 op のみで、operation 単位で server=api-data.line.me を宣言。
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/manage-audience.yml",
  "--exclude-path","**/upload/byFile",
  "-c","ManageAudienceApiClient","-n","Line.OpenApi.ManageAudience.Generated.Api",
  "-o","./src/Line.OpenApi.ManageAudience/Generated/Api",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json"
)

# 10) ManageAudience データ系 (api-data.line.me) — /upload/byFile のみ、multipart/form-data。
#     ファサード ManageAudienceClient で BaseUrl を api-data.line.me に上書き（構築前）。
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/manage-audience.yml",
  "--include-path","**/upload/byFile",
  "-c","ManageAudienceBlobApiClient","-n","Line.OpenApi.ManageAudience.Generated.Blob",
  "-o","./src/Line.OpenApi.ManageAudience/Generated/Blob",
  "--exclude-backward-compatible",
  "--structured-mime-types","multipart/form-data","--structured-mime-types","application/json"
)

Write-Host "`n生成完了。次: dotnet build  /  dotnet test" -ForegroundColor Green
Write-Host "各 -o フォルダに kiota-lock.json が出力されます（コミット対象）。"
