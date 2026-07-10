<#
  LINE .NET クライアント PoC — Kiota 生成スクリプト (Windows / PowerShell)

  前提:
    - .NET SDK 8 以降
    - Kiota CLI:  dotnet tool install --global Microsoft.OpenApi.Kiota
    - リポジトリ直下（poc/）で実行すること

  仕様の取得:
    openapi/ に *.yml が無ければ line/line-openapi から取得します。
    （messaging-api.yml / channel-access-token.yml は同梱済み）
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

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

function Invoke-Kiota { param([string[]]$KiotaArgs)
  Write-Host "kiota $($KiotaArgs -join ' ')" -ForegroundColor Cyan
  & kiota @KiotaArgs
  if ($LASTEXITCODE -ne 0) { throw "kiota failed ($LASTEXITCODE)" }
}

# 1) Messaging 制御系 (api.line.me) — /content を除外
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/messaging-api.yml",
  "--exclude-path","**/content","--exclude-path","**/content/**",
  "-c","MessagingApiClient","-n","Line.Messaging.Generated.Api",
  "-o","./src/Line.Messaging/Generated/Api",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json"
)

# 2) Messaging データ系 (api-data.line.me) — /content のみ（生成後 BaseUrl 上書き）
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/messaging-api.yml",
  "--include-path","**/content","--include-path","**/content/**",
  "-c","MessagingBlobApiClient","-n","Line.Messaging.Generated.Blob",
  "-o","./src/Line.Messaging/Generated/Blob",
  "--exclude-backward-compatible"
)

# 3) Channel Access Token — form-urlencoded を含める
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/channel-access-token.yml",
  "-c","ChannelAccessTokenClient","-n","Line.ChannelAccessToken.Generated",
  "-o","./src/Line.ChannelAccessToken/Generated",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json","--structured-mime-types","application/x-www-form-urlencoded"
)

# 4) Webhook — モデル専用。
#    注意: /callback を除外するとモデルが一切生成されない（モデルはこの唯一の
#    オペレーション経由で参照され生成されるため）。よって /callback は残し、
#    生成される callback メソッド（server が example.com）は使わずモデルのみ利用する。
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/webhook.yml",
  "-c","WebhookModels","-n","Line.Messaging.Webhook.Generated",
  "-o","./src/Line.Messaging.Webhook/Generated",
  "--exclude-backward-compatible"
)

# 5) LIFF
Invoke-Kiota @(
  "generate","-l","CSharp","-d","./openapi/liff.yml",
  "-c","LiffApiClient","-n","Line.Liff.Generated",
  "-o","./src/Line.Liff/Generated",
  "--exclude-backward-compatible",
  "--structured-mime-types","application/json"
)

Write-Host "`n生成完了。次: dotnet build  /  dotnet test" -ForegroundColor Green
Write-Host "各 -o フォルダに kiota-lock.json が出力されます（コミット対象）。"
