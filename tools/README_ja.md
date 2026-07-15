[English](README.md) | **日本語**

# Line.OpenApi.Tools — LINE 用 CLI / MCP ツール

LINE Platform をローカル PC から操作する **`dotnet` グローバルツール**（コマンド名 `line`）です。`Line.OpenApi.*` クライアントライブラリの上に構築され、**同じ機能を CLI サブコマンドと MCP サーバのツールの両方**で提供します。

- **CLI** — 人間が端末から実行（`line message push ...`）
- **MCP サーバ** — AI エージェント（Claude Desktop / Claude Code など）から呼び出し（`line mcp` で stdio 起動）

どちらも共通のサービス層を共有するため、挙動は一致します。

## 提供機能

| 分類 | 機能 |
|---|---|
| **A. トークン管理** | チャネルアクセストークンの発行（v2.1 JWT / ステートレス）・検証・失効 |
| **B. メッセージ送信・Bot 照会** | push / multicast / broadcast / reply、bot 情報・送信上限・消費数、ユーザープロフィール、メッセージ内容ダウンロード |
| **C. Webhook 開発支援** | ローカル受信サーバ（署名検証付き）・保存ペイロードの署名検証・ローカルアプリへの再送 |
| **D. LIFF 管理** | LIFF アプリの一覧・追加・更新・削除 |

対象フレームワークは **`net10.0`** です。

---

## インストール

> NuGet.org への公開は準備中です（`0.1.0-preview`）。公開後は次のように導入できます。

```sh
dotnet tool install -g Line.OpenApi.Tools
```

ローカルソースからの動作確認（このリポジトリ内）:

```sh
dotnet run --project tools/Line.OpenApi.Tools -- <command> ...
# 例
dotnet run --project tools/Line.OpenApi.Tools -- --help
```

---

## クイックスタート

### 1. 資格情報を設定する（プロファイル）

認証情報は `~/.line/config.json`（Windows は `%USERPROFILE%\.line\config.json`）に**プロファイル**単位で保存します。

```sh
# 静的トークンで最短スタート
line config set default --token "YOUR_CHANNEL_ACCESS_TOKEN"

# トークン発行に使う鍵情報を設定する場合
line config set default \
  --channel-id "1234567890" \
  --kid "your-key-id" \
  --private-key "~/.line/keys/default.pem" \
  --secret "YOUR_CHANNEL_SECRET"

line config list          # プロファイル一覧（* が既定）
line config get default   # 内容表示（秘密はマスク）
line config use staging   # 既定プロファイルの切り替え
```

> **セキュリティ**: config は秘密を平文で保存します。Unix では作成時に `0600`（本人のみ読み書き）へ制限されます。Windows ではユーザープロファイル配下の ACL を継承します（保存時に警告を表示）。秘密鍵は**パス参照のみ**で、中身は config に保存されません。

### 2. 使ってみる

```sh
line bot info                                   # Bot 情報
line message push --to U0123... --text "Hello"  # プッシュ送信
line liff list                                  # LIFF アプリ一覧
```

---

## 資格情報の解決順序

各コマンドは次の優先順位で資格情報を解決します（**上が優先**）:

1. コマンドライン引数（`--channel-token` / `--channel-id` / `--secret` / `--private-key` / `--kid`）
2. 環境変数
3. プロファイル（`--profile <name>`、なければ既定プロファイル）

### 環境変数

| 変数 | 用途 |
|---|---|
| `LINE_CHANNEL_ACCESS_TOKEN` | チャネルアクセストークン |
| `LINE_CHANNEL_ID` | チャネル ID |
| `LINE_CHANNEL_SECRET` | チャネルシークレット（Webhook 署名検証・トークン失効） |
| `LINE_PRIVATE_KEY_PATH` | JWT アサーション署名用の秘密鍵（PEM）パス |
| `LINE_KID` | 署名鍵の Key ID |
| `LINE_PROFILE` | 使用するプロファイル名 |
| `LINE_CONFIG` | config ファイルのパス上書き |

### 共通オプション（全コマンド）

| オプション | 説明 |
|---|---|
| `--profile <name>` | 使用するプロファイル |
| `--channel-token <t>` | トークンを直接指定（env/プロファイルより優先） |
| `--json` | 機械可読な JSON で出力 |
| `--verbose` | エラー時に詳細を表示 |

---

## コマンドリファレンス

### A. トークン管理 `line token`

```sh
# 発行（既定 v2.1。--kind stateless でステートレストークン）
line token issue --kind v2.1 --days 30 \
  --channel-id 123 --kid KID --private-key ./key.pem

line token issue --store          # 発行して既定プロファイルへ保存
line token verify --token <t>     # 有効性・残り寿命を確認
line token revoke --token <t>     # 失効（--channel-id / --secret が必要）
```

- 発行にはチャネル ID・Key ID・秘密鍵（PEM）が必要です。CLI が RS256 の JWT アサーションを生成して送信します。
- CLI では発行したトークンを標準出力へ、メタ情報を標準エラーへ出力します（パイプ利用可）。`--store` で解決中プロファイルへ保存できます。

### B. メッセージ送信・Bot 照会 `line message` / `line bot`

メッセージ本文は `--text` / `--flex <file>` / `--messages <file>` のいずれかで指定します。

```sh
line message push --to <id> --text "こんにちは"
line message push --to <id> --flex ./flex.json --alt-text "案内"
line message push --to <id> --messages ./messages.json     # messages 配列 JSON をそのまま
line message multicast --to id1,id2,id3 --text "一斉送信"
line message broadcast --text "全員へ"
line message reply --reply-token <token> --text "返信"
line message content <messageId> -o ./image.jpg           # 受信メッセージのバイナリ DL

line bot info
line bot quota                # 送信上限
line bot quota consumption    # 当月消費数
line bot profile <userId>
```

- `--flex` は Flex メッセージの `contents` JSON を渡すと、`altText` 付きの Flex メッセージへ自動でラップします。
- `--messages` は `[{ "type": "text", "text": "..." }, ...]` の messages 配列 JSON をそのまま送ります。
- `content` は data ホスト（`api-data.line.me`）から取得します（ファサードが自動ルーティング）。

### C. Webhook 開発支援 `line webhook`

```sh
# ローカル受信サーバ（署名検証しつつ受信イベントを整形表示）
line webhook listen --port 5000

# 保存ペイロードの署名検証＋イベント要約
line webhook verify --body ./payload.json --signature <x-line-signature>

# 保存ペイロードをローカルアプリへ再送（署名は付与しない・宛先は検証しない）
line webhook replay --body ./payload.json --to http://localhost:5000/webhook
```

- 署名検証にはチャネルシークレットが必要です（プロファイル / `--secret`）。
- `listen` は外部トンネル（cloudflared / ngrok 等）と併用して LINE からの実 Webhook を受けられます。トンネル自体はツールに含みません。

### D. LIFF 管理 `line liff`

```sh
line liff list
line liff add --file ./app.json               # LIFF アプリ定義 JSON から追加
line liff update <liffId> --file ./app.json
line liff delete <liffId>
```

### 終了コード

| コード | 意味 |
|---|---|
| `0` | 成功 |
| `1` | 一般エラー |
| `2` | 引数エラー（不正な入力・入力ファイル不在など） |
| `3` | 認証・資格情報エラー |
| `4` | LINE API エラー（HTTP 4xx/5xx） |

---

## MCP サーバとして使う

`line mcp` で stdio の MCP サーバとして起動し、AI エージェントから LINE 操作をツールとして呼べます。

```sh
line mcp                       # 全ツール有効
line mcp --read-only           # 読み取り系ツールのみ公開
line mcp --allow-secret-output # token issue が生トークンを返せるようにする（既定は非返却）
line mcp --allow-remote-replay # webhook replay の非ループバック宛先を許可（既定はループバックのみ）
```

### ツール一覧

CLI コマンドを `line_<area>_<verb>` の名前で公開します（`webhook listen` を除く）。

- 読み取り系（`--read-only` でもこれらは有効）: `line_message_schema` / `line_bot_info` / `line_bot_quota` / `line_bot_quota_consumption` / `line_bot_profile` / `line_liff_list` / `line_token_verify` / `line_webhook_verify` / `line_ping`
- 変更系（`--read-only` では除外）: `line_message_push` / `line_message_multicast` / `line_message_broadcast` / `line_message_reply` / `line_liff_add` / `line_liff_update` / `line_liff_delete` / `line_token_issue` / `line_token_revoke` / `line_webhook_replay`

各ツールは任意で `profile` 引数を受け取り、資格情報はプロファイルから解決します。

### AI エージェントによるメッセージ組立（flex / template）

MCP の主要ユースケースの一つが「**組み立てる → 検証する → 送って実機で確認**」のループです。エージェントにリッチメッセージを組ませ、型検証し、自分の端末に push して見た目を確認し、直す——これを確実にする 2 つの補助があります。

- **`line_message_schema(type)`** は LINE メッセージオブジェクトの JSON Schema を返し、エージェントが**形として妥当な** `messagesJson` を組めるようにします。`type` は `all` / `flex` / `template` / `imagemap` / `quickReply` / `action` のいずれか（既定 `flex`）。読み取り系ツール（`--read-only` でも有効）でシークレットは返しません。スキーマは Kiota が生成に使う OpenAPI 仕様と同一物から抽出するためモデルとドリフトせず、`FlexBox` が自己再帰のため参照はインライン展開せず `$ref` + `$defs` で保持します。
  - 単純メッセージ（text / image / video / audio / location / sticker）は軽量で、送信ツールの説明文に例が載っています。スキーマが必要なのは主に **flex** / **template** です。
- **送信ツールの `dryRun: true`**（`line_message_push` / `multicast` / `broadcast` / `reply`）は、メッセージをパース・形状チェックして種別を返すだけで**送信しません**（API 呼び出しなし・資格情報不要）。実送信前の安全チェックに使います。

典型的な流れ: `line_message_schema type=flex` → Flex JSON を組む → `line_message_push ... dryRun=true`（検証）→ `line_message_push ...`（自分の userId へ送信）→ 実機で確認。

### セキュリティ設計（MCP）

MCP ツールの戻り値はモデルのコンテキスト（LLM プロバイダへの送信・会話履歴・ログ）に載る前提で、以下の保護を組み込んでいます。

- **`line_token_issue` は既定で生トークンを返しません。** 発行したトークンはローカルプロファイルへ保存し、戻り値はメタ情報（`tokenType` / `expiresInSeconds` / `keyId` / `maskedToken` / `storedProfile`）のみです。以後の送信系ツールは保存済みプロファイルを参照して動作します。生トークンが必要な場合のみ、サーバを `--allow-secret-output` 付きで起動し、ツール呼び出しで `reveal: true` を指定します。
- **`line_webhook_replay` は既定でループバック宛先のみ許可**します（SSRF 緩和）。リモート宛先は `--allow-remote-replay` で明示的に有効化します。
- 破壊系・送信系ツールは説明文に副作用を明記しています。

### Claude Code への登録例

```sh
claude mcp add line -- line mcp
# 読み取り専用で登録する場合
claude mcp add line -- line mcp --read-only
```

### Claude Desktop への登録例（`claude_desktop_config.json`）

```jsonc
{
  "mcpServers": {
    "line": {
      "command": "line",
      "args": ["mcp"]
    }
  }
}
```

---

## ソースからのビルド

```sh
dotnet build tools/Line.OpenApi.Tools/Line.OpenApi.Tools.csproj
dotnet test  tests/Line.OpenApi.Tools.Tests/Line.OpenApi.Tools.Tests.csproj
```

## 関連ドキュメント

- 仕様: [`docs/CLI-MCP-tool-spec.md`](../docs/CLI-MCP-tool-spec.md)
- ライブラリ本体: リポジトリルートの [`README.md`](../README.md)

## ライセンス

MIT（リポジトリルートの [`LICENSE`](../LICENSE) を参照）。
