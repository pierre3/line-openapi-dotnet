#!/usr/bin/env bash
# LINE .NET クライアント PoC — Kiota 生成スクリプト (bash / macOS・Linux)
# 前提: .NET SDK 8+ / kiota (dotnet tool install --global Microsoft.OpenApi.Kiota)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

mkdir -p openapi
declare -A SPECS=(
  [messaging-api.yml]="https://raw.githubusercontent.com/line/line-openapi/master/messaging-api.yml"
  [channel-access-token.yml]="https://raw.githubusercontent.com/line/line-openapi/master/channel-access-token.yml"
  [webhook.yml]="https://raw.githubusercontent.com/line/line-openapi/master/webhook.yml"
  [liff.yml]="https://raw.githubusercontent.com/line/line-openapi/master/liff.yml"
)
for name in "${!SPECS[@]}"; do
  [ -f "openapi/$name" ] || { echo "downloading $name"; curl -fsSL "${SPECS[$name]}" -o "openapi/$name"; }
done

kg() { echo "kiota $*"; kiota "$@"; }

# 1) Messaging 制御系 (api.line.me) — /content を除外
kg generate -l CSharp -d ./openapi/messaging-api.yml \
  --exclude-path '**/content' --exclude-path '**/content/**' \
  -c MessagingApiClient -n Line.Messaging.Generated.Api \
  -o ./src/Line.Messaging/Generated/Api \
  --exclude-backward-compatible --structured-mime-types application/json

# 2) Messaging データ系 (api-data.line.me) — /content のみ
kg generate -l CSharp -d ./openapi/messaging-api.yml \
  --include-path '**/content' --include-path '**/content/**' \
  -c MessagingBlobApiClient -n Line.Messaging.Generated.Blob \
  -o ./src/Line.Messaging/Generated/Blob \
  --exclude-backward-compatible

# 3) Channel Access Token — form-urlencoded を含める
kg generate -l CSharp -d ./openapi/channel-access-token.yml \
  -c ChannelAccessTokenClient -n Line.ChannelAccessToken.Generated \
  -o ./src/Line.ChannelAccessToken/Generated \
  --exclude-backward-compatible \
  --structured-mime-types application/json --structured-mime-types application/x-www-form-urlencoded

# 4) Webhook — モデル専用。
#    注意: /callback を除外するとモデルが生成されない（モデルはこの唯一のオペレーション
#    経由で参照されるため）。/callback は残し、生成される callback メソッドは使わずモデルのみ利用。
kg generate -l CSharp -d ./openapi/webhook.yml \
  -c WebhookModels -n Line.Messaging.Webhook.Generated \
  -o ./src/Line.Messaging.Webhook/Generated \
  --exclude-backward-compatible

# 5) LIFF
kg generate -l CSharp -d ./openapi/liff.yml \
  -c LiffApiClient -n Line.Liff.Generated \
  -o ./src/Line.Liff/Generated \
  --exclude-backward-compatible --structured-mime-types application/json

echo "生成完了。次: dotnet build / dotnet test"
