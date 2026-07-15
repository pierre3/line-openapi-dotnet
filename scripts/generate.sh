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
  [insight.yml]="https://raw.githubusercontent.com/line/line-openapi/master/insight.yml"
  [module.yml]="https://raw.githubusercontent.com/line/line-openapi/master/module.yml"
  [shop.yml]="https://raw.githubusercontent.com/line/line-openapi/master/shop.yml"
)
for name in "${!SPECS[@]}"; do
  [ -f "openapi/$name" ] || { echo "downloading $name"; curl -fsSL "${SPECS[$name]}" -o "openapi/$name"; }
done

kg() { echo "kiota $*"; kiota "$@"; }

# 1) Messaging 制御系 (api.line.me) — /content を除外
kg generate -l CSharp -d ./openapi/messaging-api.yml \
  --exclude-path '**/content' --exclude-path '**/content/**' \
  -c MessagingApiClient -n Line.OpenApi.Messaging.Generated.Api \
  -o ./src/Line.OpenApi.Messaging/Generated/Api \
  --exclude-backward-compatible --structured-mime-types application/json

# 2) Messaging データ系 (api-data.line.me) — /content のみ
kg generate -l CSharp -d ./openapi/messaging-api.yml \
  --include-path '**/content' --include-path '**/content/**' \
  -c MessagingBlobApiClient -n Line.OpenApi.Messaging.Generated.Blob \
  -o ./src/Line.OpenApi.Messaging/Generated/Blob \
  --exclude-backward-compatible

# 3) Channel Access Token — form-urlencoded を含める
kg generate -l CSharp -d ./openapi/channel-access-token.yml \
  -c ChannelAccessTokenClient -n Line.OpenApi.ChannelAccessToken.Generated \
  -o ./src/Line.OpenApi.ChannelAccessToken/Generated \
  --exclude-backward-compatible \
  --structured-mime-types application/json --structured-mime-types application/x-www-form-urlencoded

# 4) Webhook — モデル専用。
#    注意: /callback を除外するとモデルが生成されない（モデルはこの唯一のオペレーション
#    経由で参照されるため）。/callback は残し、生成される callback メソッドは使わずモデルのみ利用。
kg generate -l CSharp -d ./openapi/webhook.yml \
  -c WebhookModels -n Line.OpenApi.Messaging.Webhook.Generated \
  -o ./src/Line.OpenApi.Messaging.Webhook/Generated \
  --exclude-backward-compatible

# 5) LIFF
kg generate -l CSharp -d ./openapi/liff.yml \
  -c LiffApiClient -n Line.OpenApi.Liff.Generated \
  -o ./src/Line.OpenApi.Liff/Generated \
  --exclude-backward-compatible --structured-mime-types application/json

# 6) Insight — 統計・分析。api.line.me 単一ホスト、全 GET・JSON。
kg generate -l CSharp -d ./openapi/insight.yml \
  -c InsightApiClient -n Line.OpenApi.Insight.Generated \
  -o ./src/Line.OpenApi.Insight/Generated \
  --exclude-backward-compatible --structured-mime-types application/json

# 7) Module — モジュールチャネル（LOA 代理運用）。api.line.me 単一ホスト、JSON。
#    注意: module-attach.yml（manager.line.biz / Basic 認証 / form+PKCE）は今回未取り込み。
kg generate -l CSharp -d ./openapi/module.yml \
  -c ModuleApiClient -n Line.OpenApi.Module.Generated \
  -o ./src/Line.OpenApi.Module/Generated \
  --exclude-backward-compatible --structured-mime-types application/json

# 8) Shop — ミッションスタンプ送信。api.line.me 単一ホスト、JSON。
kg generate -l CSharp -d ./openapi/shop.yml \
  -c ShopApiClient -n Line.OpenApi.Shop.Generated \
  -o ./src/Line.OpenApi.Shop/Generated \
  --exclude-backward-compatible --structured-mime-types application/json

echo "生成完了。次: dotnet build / dotnet test"
