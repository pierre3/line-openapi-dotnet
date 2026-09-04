# 変更履歴

本プロジェクトの主な変更点をこのファイルに記録します。

書式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に準拠し、
バージョニングは [セマンティック バージョニング](https://semver.org/lang/ja/) に従います。

English version: [`CHANGELOG.md`](CHANGELOG.md)

本リポジトリは 3 系統を独立採番で公開しています:

- **ライブラリ** — `Line.OpenApi.*` クライアントパッケージ群。タグ `v*`。
- **ツール** — `Line.OpenApi.Tools` CLI / MCP グローバルツール（コマンド `line`）。タグ `tools-v*`。
- **AI ツール** — `Line.OpenApi.Extensions.AI` アプリ内蔵 Microsoft.Extensions.AI ツール。タグ `ai-v*`。

それぞれ別サイクルで進むため、以下でバージョン履歴を分けて記載します。

---

## ツール — `Line.OpenApi.Tools`

### [1.3.0] - 2026-09-04

Flex プレビューで自分のローカル画像・動画をホスティングなしに表示できるようにします。

#### 追加

- **Flex プレビューのローカルメディア配信（`LINE_FLEX_MCP_ASSET_DIR`）。** この環境変数にフォルダを指定すると、ループバックのプレビューサーバがそのフォルダ内のメディアファイルを配信します。デザイン中は Flex メッセージから **相対** `url`（例 `assets/hero.png`）で参照でき、本番移行時は origin だけを HTTPS の URL に差し替えれば相対パス部分はそのまま使えます。配信対象は LINE が Flex メッセージでレンダリングする形式に一致します＝画像は **JPEG/PNG**（APNG は `.png`）、動画は `video` コンポーネント用の **`.mp4`**（GIF/WebP 等は意図的に配信しません）。配信は **opt-in**（フォルダ未設定なら無効）、指定フォルダ配下への封じ込め（パストラバーサル・フォルダ外シンボリックリンクを拒否）、ループバック限定、read-only 安全です。LINE 本体はローカル URL も `data:` URL もレンダリングしないため、あくまでプレビュー用の利便機能です。

### [1.2.0] - 2026-09-03

LINE アプリに近い見た目の Flex Message ライブプレビューを追加します。

#### 追加

- **Flex Message ライブプレビュー（`line_flex_*`）。** 読み取り系の MCP ツール `line_flex_preview`・`line_flex_get_content`・`line_flex_validate`・`line_flex_open` を追加。Flex JSON をループバックのブラウザビューに描画し、反復のたびにその場で更新します。ブラウザ上で加えた編集は読み戻せます。LINE API 呼び出しやシークレットは使わないため `--read-only` でも利用できます。同じレンダラは `extensions/line-flex-viewer/` の Copilot App canvas 拡張としても提供します（依存パッケージのない Node MCP サーバも代替として同梱）。

### [1.1.0] - 2026-08-13

dev トンネルの再起動時に LINE Developers コンソールへ URL を貼り替える手間を自動化します。

#### 追加

- **Webhook エンドポイント設定。** CLI `line webhook get-endpoint` / `set-endpoint --url <url>` / `test-endpoint [--url <url>]`、MCP `line_webhook_get_endpoint`・`line_webhook_test_endpoint`（読み取り）、`line_webhook_set_endpoint`（変更系）。`test-endpoint` は LINE プラットフォームにエンドポイントへのテスト配信を依頼し、到達可否を返します。
- **LIFF エンドポイント URL 更新。** CLI `line liff update-url <liffId> --url <url>`、MCP `line_liff_update_url`。`view.url` のみを部分更新します。`liffId` は `line liff list` で取得できます。
- 新規の set/test/update コマンドの URL は絶対 **https** を必須とし、ネットワーク呼び出し前に拒否します。

### [1.0.0] - 2026-08-12

初の安定版（GA）リリース。

#### 追加

- CLI / MCP の全サーフェス: `config`・`token`・`message`・`bot`・`webhook`（verify / replay / listen）・`liff`・`richmenu`・`insight`・`audience`・`shop`。
- MCP サーバ（`line mcp`）で同機能を `line_<area>_<verb>` として公開（`webhook listen` を除く）。安全フラグ `--read-only`・`--allow-secret-output`（`line_token_issue` は既定で生トークンを返さない）・`--allow-remote-replay`（既定はループバックのみ）。
- 旗艦ループ「組み立て → dryRun → 送信」向けのメッセージ組立支援: `line_message_schema` と送信ツールの `dryRun` 引数。

### [0.2.0-preview] - 2026-07-16

#### 追加

- リッチメニューのコマンド群（`line richmenu`、画像アップロード/ダウンロード含む）と MCP ツール。
- Insight / Manage Audience / Shop カバレッジパッケージの露出（`line insight` / `line audience` / `line shop`）。

### [0.1.0-preview] - 2026-07-14

- CLI / MCP ツールの初公開（当初 `Line.OpenApi.Cli` として公開後、`Line.OpenApi.Tools` へ改名）。サーフェス: `config`・`token`・`message`・`bot`・`webhook`・`liff`。

---

## ライブラリ — `Line.OpenApi.*`

### 1.0.0 - 2026-08-12

クライアントライブラリの初の安定版（GA）リリース。

- パッケージ構成: `Line.OpenApi.Core`・`.ChannelAccessToken`・`.Messaging`・`.Messaging.Webhook`・`.Liff`・`.Insight`・`.ManageAudience`・`.Module`・`.Shop`・`.Login`・`.MiniApp`、およびメタパッケージ `.Bot`。
- ターゲットフレームワーク `net10.0`。公開仕様 [line-openapi](https://github.com/line/line-openapi) から Kiota で生成したクライアント＋手書きのファサード・DI 拡張・Webhook 受信グルー。

### 0.2.0-preview - 2026-07-16

#### 追加

- カバレッジパッケージ `Line.OpenApi.Insight`・`.ManageAudience`・`.Module`・`.Shop`、および手書きの `Line.OpenApi.Login`。
- `Line.OpenApi.Messaging` に `RichMenuClient` ヘルパ。

### 0.1.0-preview - 2026-07-14

- NuGet.org への初公開: `Line.OpenApi.Core`・`.ChannelAccessToken`・`.Messaging`・`.Messaging.Webhook`・`.Liff`、およびメタパッケージ `.Bot`。

---

## AI ツール — `Line.OpenApi.Extensions.AI`

### 1.0.0 - 2026-08-20

アプリ内蔵 AI ツールパッケージの初の安定版リリース。LINE の Messaging 利用シーンを [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/) の `AIFunction` ツールとして公開し、Semantic Kernel や任意の Microsoft.Extensions.AI ホストから利用できます。

#### 追加

- `LineMessagingAiTools.CreateReadOnly` / `Create` によるツール生成: 読み取り専用 `line_bot_info` / `line_bot_quota` / `line_bot_profile` / `line_message_validate`、および opt-in の `line_message_push` / `line_message_multicast` / `line_message_reply` / `line_message_broadcast`。
- `LineAiToolOptions` による既定安全な送信モデル: `EnableSending`・`AllowBroadcast`・`DryRun`、射程制限の `SendPolicy` ゲート、human-in-the-loop の `BeforeSend` フック。いずれのゲートもツール引数には出さないため、モデルは反転できません。
- 依存は 2 本のみ: `Line.OpenApi.Messaging` と `Microsoft.Extensions.AI.Abstractions`。

---

[1.2.0]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v1.1.0...tools-v1.2.0
[1.1.0]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v1.0.0...tools-v1.1.0
[1.0.0]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v0.2.0-preview...tools-v1.0.0
[0.2.0-preview]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v0.1.0-preview...tools-v0.2.0-preview
[0.1.0-preview]: https://github.com/pierre3/line-openapi-dotnet/releases/tag/tools-v0.1.0-preview
