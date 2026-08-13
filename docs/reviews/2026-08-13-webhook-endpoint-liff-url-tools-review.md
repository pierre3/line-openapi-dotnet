# レビュー記録: CLI/MCP ツール — Webhook endpoint 設定 & LIFF URL 更新

- **日付:** 2026-08-13
- **ブランチ:** `feat-webhook-endpoint-liff-url-tools`（vs `main`）
- **対象:** `Line.OpenApi.Tools`（CLI `line` / MCP）への機能追加
- **目的:** dev トンネル（VS Dev Tunnels / ngrok 等）運用で、トンネル再起動のたびに LINE コンソールへ URL を貼り替える手間を無くす。コマンド／エージェントから Webhook エンドポイント URL・LIFF `view.url` を更新できるようにする。

## 追加サーフェス

| 種別 | CLI | MCP | read/write |
|---|---|---|---|
| Webhook endpoint 取得 | `webhook get-endpoint` | `line_webhook_get_endpoint` | read |
| Webhook endpoint 設定 | `webhook set-endpoint --url <url>` | `line_webhook_set_endpoint` | write |
| Webhook endpoint テスト | `webhook test-endpoint [--url <url>]` | `line_webhook_test_endpoint` | read（診断） |
| LIFF URL 部分更新 | `liff update-url <liffId> --url <url>` | `line_liff_update_url` | write |

- liffId 取得は既存 `liff list` / `line_liff_list`（liffId+URL を返す）で完結＝コマンドだけで貼り替えループが閉じる。
- 実体: 生成 `MessagingClient.Api.V2.Bot.Channel.Webhook.Endpoint`/`.Test`（control host、R1 はファサードが解決）、`LiffClient.UpdateAppAsync`（`view.url` のみ）。
- サービス層は control-plane クライアントを持つ `MessageService` に相乗り（`WebhookService` は資格情報非依存の設計特性を温存するため足さない）。
- set/test(url)/update-url の url は絶対 https を要求し、送信前に `MessageInputException`(exit 2)＝HTTP 不要のテストシーム（共有ヘルパ `UrlGuard.RequireHttps`）。

## 3 役ゲート結果

- **code-reviewer = PASS**（ブロッキングなし）。Stream 破棄・モデル写像・ビルダーパス・入力ガード・DI・read/write ゲーティングいずれも正当と確認。
- **security-reviewer = PASS**。トークン非露出（戻り値・ログ・例外は非機密のみ）、AllowedHostsValidator バイパスなし、set の loopback ゲート不在は妥当（利用者 URL は接続先でなく認証済みリクエストのペイロード＝SSRF 非該当）、test の read-only 分類も許容。
- **test-arch-reviewer = PASS**。MessageService 相乗りは ADR 的に妥当（同一 facade 型のキャッシュ再利用）、URL ガードの reject-before-network テストは RichMenuServiceTests シーム踏襲、MCP exact-set 完全性ガード更新済み。

## 指摘への対応（すべて Low・非ブロッキング）

- **反映:** ①URL ガードを `MessageService` 内の静的から中立ヘルパ `Services/UrlGuard.cs` へ切り出し（サービス間結合の解消。code/test-arch 両者の指摘）。②`test-endpoint` のテキスト出力に `timestamp` を追加（JSON には既出、テキスト経路の情報欠落を解消。code 指摘）。
- **非対応（follow-up として保持）:** control-plane 操作の HTTP 正常系テストは `MessageService`/`MessagingClient` に transport 注入シームが無いため未追加。既存の bot info/quota 等と同じ構造的制約で本変更固有の退行ではない。シーム追加時に set の空ストリーム破棄経路・test のレコード写像を happy-path 化する余地あり。

## 検証

- `dotnet build`：0 警告 / 0 エラー。
- `dotnet test`：Tools 83/83、ライブラリ 264/264、Webhook Isolation 1/1（全緑）。
- CLI ヘルプに新コマンド表示、`set-endpoint --url http://...`（非 https）で exit 2・送信前拒否を実機外で確認。

## 判定

3 役すべて PASS・ブロッキングなし・Low 指摘 2 件反映済み。**GO 推奨、人の go/no-go 待ち。** ライブラリ本体・生成コード・公開 API snapshot は無変更（tools/tests/docs のみ）。

**要実機確認（GA 前・非ブロッキング）:** 実チャネルで `set-endpoint` → `get-endpoint` 反映 → `test-endpoint` success、`liff update-url` → `liff list` 反映のスモーク。
