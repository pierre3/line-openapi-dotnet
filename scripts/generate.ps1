<#
  LINE .NET クライアント — Kiota 生成スクリプト (Windows / PowerShell)

  前提:
    - .NET SDK 8 以降
    - Kiota CLI:  dotnet tool install --global Microsoft.OpenApi.Kiota
    - リポジトリルートで実行すること

  仕様の取得:
    openapi/ に *.yml が無ければ line/line-openapi から取得します。
    （messaging-api.yml / channel-access-token.yml は同梱済み）
#>
param(
  # CLI 版ピンと不一致でも続行したい場合に指定（再現性を意図的に外す時のみ）。
  [switch]$AllowKiotaVersionMismatch
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

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

$specs = @{
  "messaging-api.yml"        = "https://raw.githubusercontent.com/line/line-openapi/master/messaging-api.yml"
  "channel-access-token.yml" = "https://raw.githubusercontent.com/line/line-openapi/master/channel-access-token.yml"
  "webhook.yml"              = "https://raw.githubusercontent.com/line/line-openapi/master/webhook.yml"
  "liff.yml"                 = "https://raw.githubusercontent.com/line/line-openapi/master/liff.yml"
}
New-Item -ItemType Directory -Force -Path "openapi" | Out-Null
foreach ($name in $specs.Keys) {
  $path = Join-Path "openapi" $name
  if (-not (Test-Path $path)) {
    Write-Host "downloading $name ..."
    Invoke-WebRequest -Uri $specs[$name] -OutFile $path
  }
}

# --- 上流 YAML 正規化（冪等） ---
# channel-access-token.yml のフロー配列内 `urn:ietf:...:jwt-bearer` は未引用だと
# SharpYaml がコロンを誤認してパースエラーになる（master 再取得時に再発しうる）。
# フロー配列 `[ ... ]` 直後の未引用 urn: を引用符化する。既に引用符付きなら何もしない。
$catPath = Join-Path "openapi" "channel-access-token.yml"
if (Test-Path $catPath) {
  $content = Get-Content $catPath -Raw
  $normalized = [regex]::Replace($content, '(?<=\[\s*)(urn:[^\]"'']+?)(?=\s*\])', '"$1"')
  if ($normalized -ne $content) {
    Set-Content -Path $catPath -Value $normalized -NoNewline
    Write-Host "normalized unquoted urn scheme in channel-access-token.yml" -ForegroundColor Yellow
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

Write-Host "`n生成完了。次: dotnet build  /  dotnet test" -ForegroundColor Green
Write-Host "各 -o フォルダに kiota-lock.json が出力されます（コミット対象）。"
