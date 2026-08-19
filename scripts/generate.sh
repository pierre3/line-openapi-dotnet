#!/usr/bin/env bash
# LINE .NET クライアント PoC — Kiota 生成スクリプト (bash / macOS・Linux)
# 前提: .NET SDK 8+ / kiota (dotnet tool install --global Microsoft.OpenApi.Kiota)
#
# ⚠️ parity メモ: このスクリプトは generate.ps1 の全機能を持たない副系統。
#   欠けているもの: Kiota CLI 版ピン照合 / LF+urn 正規化 / upstream-manifest.json 連携 /
#   -Update（SHA ピン再取得・manifest 更新）。上流追従（ドリフト検知→再生成→PR）の
#   正となるのは generate.ps1（pwsh）＋ scripts/check-spec-drift.ps1 で、CI(spec-sync.yml)も
#   そちらを使う。このスクリプトは欠損 spec の素朴なフォールバック取得と手元生成のみを想定。
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

mkdir -p openapi
# 取得元は上流の既定ブランチ main（旧 master ブランチは存在しない）。再現性が要るときは
# generate.ps1 -Update -Ref <sha> を使い manifest でコミット SHA をピンすること。
declare -A SPECS=(
  [messaging-api.yml]="https://raw.githubusercontent.com/line/line-openapi/main/messaging-api.yml"
  [channel-access-token.yml]="https://raw.githubusercontent.com/line/line-openapi/main/channel-access-token.yml"
  [webhook.yml]="https://raw.githubusercontent.com/line/line-openapi/main/webhook.yml"
  [liff.yml]="https://raw.githubusercontent.com/line/line-openapi/main/liff.yml"
  [insight.yml]="https://raw.githubusercontent.com/line/line-openapi/main/insight.yml"
  [module.yml]="https://raw.githubusercontent.com/line/line-openapi/main/module.yml"
  [shop.yml]="https://raw.githubusercontent.com/line/line-openapi/main/shop.yml"
  [manage-audience.yml]="https://raw.githubusercontent.com/line/line-openapi/main/manage-audience.yml"
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

# 9) ManageAudience 制御系 (api.line.me) — /upload/byFile を除外（データ系）。
kg generate -l CSharp -d ./openapi/manage-audience.yml \
  --exclude-path '**/upload/byFile' \
  -c ManageAudienceApiClient -n Line.OpenApi.ManageAudience.Generated.Api \
  -o ./src/Line.OpenApi.ManageAudience/Generated/Api \
  --exclude-backward-compatible --structured-mime-types application/json

# 10) ManageAudience データ系 (api-data.line.me) — /upload/byFile のみ、multipart/form-data。
kg generate -l CSharp -d ./openapi/manage-audience.yml \
  --include-path '**/upload/byFile' \
  -c ManageAudienceBlobApiClient -n Line.OpenApi.ManageAudience.Generated.Blob \
  -o ./src/Line.OpenApi.ManageAudience/Generated/Blob \
  --exclude-backward-compatible --structured-mime-types multipart/form-data --structured-mime-types application/json

echo "生成完了。次: dotnet build / dotnet test"
