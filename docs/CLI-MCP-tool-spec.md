# Line.OpenApi.Tools — CLI / MCP ツール 仕様

**対象:** ローカル PC で使う LINE 開発者向け実用ツール（`dotnet tool` 配布）。同一ロジックを **CLI サブコマンド**と **MCP サーバのツール**の両方で公開する。
**前提ライブラリ:** 本リポジトリの `Line.OpenApi.*`（Messaging / Messaging.Webhook / ChannelAccessToken / Liff）。
**TFM:** `net10.0` 単一（本体の方針に追従。`Nullable=enable` / `LangVersion=latest`）。
**ステータス:** ドラフト（実装前の合意用）。実装着手前に spec ゲート（`spec-reviewer` 相当）を通す。
**最終更新:** 2026-07-14。

---

## 1. 目的とスコープ

LINE 開発・運用で「手元ですぐ試したい／自動化したい」操作を、1 つのツールで **人間（CLI）** と **AI エージェント（MCP）** の双方に提供する。既存ライブラリのドッグフーディングも兼ねる。

**含む（A〜D）:**

- **A. トークン管理** — チャネルアクセストークンの発行 / 検証 / 失効
- **B. メッセージ送信・Bot 照会** — push / multicast / broadcast / reply、bot-info、quota、profile、message content DL
- **C. Webhook 開発支援** — ローカル受信サーバ、保存ペイロードの署名検証、再送
- **D. LIFF 管理** — アプリの一覧 / 追加 / 更新 / 削除

**含まない（将来）:** insight / manage-audience / module / shop（本体ライブラリ未生成のため）。rich menu 等 Messaging の未ラップ領域は生成 API 直叩きで暫定対応可だが本 spec の主対象外。

---

## 2. アーキテクチャ

「コマンドの実体＝DI 登録したサービス層」を 1 回だけ実装し、**薄い CLI アダプタ**と**薄い MCP アダプタ**の 2 枚をかぶせる。二重実装しない。

```
src/Line.OpenApi.Tools/                 (PackAsTool, ToolCommandName = line, PackageId = Line.OpenApi.Tools)
├─ Program.cs            … Generic Host 構築 / DI 登録 / 実行モード分岐（既定=CLI、"mcp" で MCP stdio）
├─ Hosting/
│   ├─ CliContext.cs     … プロファイル・資格情報・出力形式などの実行時コンテキスト
│   └─ CredentialStore.cs… ~/.line/config.json の読み書き（§5）
├─ Services/             … 共通ロジック層（両アダプタが呼ぶ唯一の実体）
│   ├─ TokenService.cs        (A)
│   ├─ MessageService.cs      (B: push/multicast/broadcast/reply/bot-info/quota/profile/content)
│   ├─ WebhookService.cs      (C: verify/replay。listen はホスティング側)
│   └─ LiffService.cs         (D)
├─ Cli/                  … Cocona コマンドクラス（[Command] 付き薄いラッパ）
└─ Mcp/                  … [McpServerToolType]/[McpServerTool] 付き薄いラッパ
```

- **フレームワーク:** CLI = **Cocona**（Generic Host + DI をフル活用。既存 DI 拡張と同じ `IServiceCollection` に同居できる）。MCP = **公式 `ModelContextProtocol`（+ `Microsoft.Extensions.Hosting`）**。両者とも Generic Host に載るため DI コンテナを共有できる。
  - MCP SDK（`ModelContextProtocol`）は **1.4.0 が stable リリース済み**（実装時に確認。当初想定の preview ではない）。バージョン固定＋監査ゲート対象とする。Cocona は 2.2.0、`Microsoft.Extensions.Hosting` は 10.0.0。
- **サービス層の入出力は DTO（プレーンな record）** とし、生成モデルへの依存を境界に閉じ込める。CLI/MCP アダプタは生成モデルを直接触らない。
- LINE ライブラリの登録は既存 DI 拡張をそのまま利用: `AddLineMessaging` / `AddLineWebhook` / `AddLineLiff`。静的トークン運用時は `MessagingClient.CreateWithStaticToken` / `LiffClient.CreateWithStaticToken` 相当を DI で構成。トークン供給は `IChannelAccessTokenSource` 実装（`JwtAssertionTokenSource` / `StatelessJwtAssertionTokenSource`）＋ `RefreshingChannelAccessTokenProvider`。
- **⚠️ トークン領域には DI ヘルパ／ファサードが無い（spec ゲート指摘・Medium）:** Messaging/Webhook/Liff と異なり **`AddLineChannelAccessToken` は存在せず**、verify/revoke を公開するファサード型も無い（公開されるのは `IChannelAccessTokenSource` 系と生成 `ChannelAccessTokenClient` のみ）。よって A. トークン管理（issue/verify/revoke）は CLI の `TokenService` が**生成 `ChannelAccessTokenClient(IRequestAdapter)` を自前で組み立てて使う**（BaseUrl=`api.line.me`、匿名認証で可。form-urlencoded シリアライザはコンストラクタが全域登録）。実装責務は §4.1 参照。

### 実行モード分岐

- `line <command> ...` … 通常の CLI（既定）。
- `line mcp [--transport stdio]` … MCP サーバとして stdio で待受。Claude Desktop / Claude Code 等へ `line-openapi` MCP として登録して利用。
- CLI と MCP でロジックは同一。差分は「入出力の受け渡し」と「公開可否（§4.3）」のみ。

---

## 3. 資格情報とプロファイル（§5 と対）

- 認証情報は **プロファイル**単位で管理（複数チャネル切替）。優先順位: `--channel-token`/明示引数 > 環境変数 > プロファイル（`~/.line/config.json`）。
- 環境変数: `LINE_CHANNEL_ACCESS_TOKEN` / `LINE_CHANNEL_ID` / `LINE_CHANNEL_SECRET` / `LINE_PROFILE`。
- 発行フロー（A）に必要な秘密鍵（JWT アサーション）はファイルパス指定（`--private-key`）または環境変数。**秘密鍵の中身は config に保存しない**（パス参照のみ）。

---

## 4. コマンド／ツール表面

共通グローバルオプション: `--profile <name>` / `--channel-token <t>` / `--json`（機械可読出力）/ `--verbose`。既定出力は人間向け整形、`--json` で JSON。

### 4.1 A. トークン管理（`line token ...`）

| コマンド | 説明 | 実体 |
|---|---|---|
| `token issue` | チャネルアクセストークン発行（`--kind v2.1\|stateless`、既定 v2.1）。`--private-key`/`--kid`/`--channel-id` | `JwtAssertionTokenSource` / `StatelessJwtAssertionTokenSource` ＋ **CLI 自作の JWT 署名器**（下記） |
| `token verify --token <t>` | 有効性・残り寿命の確認 | 生成 `ChannelAccessTokenClient`（verify エンドポイント） |
| `token revoke --token <t>` | 失効 | 生成 `ChannelAccessTokenClient`（revoke エンドポイント） |

**⚠️ 実装責務（spec ゲート指摘・Medium）— トークン領域はライブラリの薄いラップでは済まない:**

1. **JWT アサーション署名器を CLI が新規実装する。** `JwtAssertionTokenSource` / `StatelessJwtAssertionTokenSource` はコンストラクタで**署名済み JWT を返す `assertionFactory`（`Func<CancellationToken, Task<string>>`）を呼び出し側から受け取る**設計で、ライブラリ内に RS256 署名や JWT 組立てヘルパは無い。よって `--private-key`（PEM）/`--kid`/`--channel-id` から**クライアント表明 JWT（RS256）を生成する署名コンポーネント**を CLI 側（例 `Services/JwtAssertionBuilder`）で実装し、`assertionFactory` として渡す。工数見積りに含める。
2. **生成 `ChannelAccessTokenClient` の手配線。** §2 のとおりトークン領域に DI ヘルパ／ファサードが無いため、`TokenService` が `ChannelAccessTokenClient(IRequestAdapter)` を自前構築する（BaseUrl=`api.line.me`、匿名認証）。
3. **stateless の form/oneOf 落とし穴を踏襲。** ステートレス発行（`/oauth2/v3/token`）は生成 oneOf 合成ボディが form 非対応のため、ライブラリの `StatelessJwtAssertionTokenSource`（平坦 form 展開の手書き経路）を必ず経由する（生成ビルダーを直接叩かない）。非ステートレス v2.1 は合成ボディでないため生成ビルダー利用で可。
4. **スコープ外（将来候補）:** v2.0 のチャネルシークレット短命トークン（`issueChannelToken`）、発行済み鍵 ID 列挙（`getsAllValidChannelAccessTokenKeyIds`）は非対象。後者は「kid 一覧・kid 単位失効」でトークン管理を補完しうるので将来候補として保持。

### 4.2 B. メッセージ送信・Bot 照会（`line message ...` / `line bot ...`）

命名一貫性（spec ゲート Low 指摘）のため、A/C/D と揃えて**グループ化**する。送信系は `message` グループ、照会系は `bot` グループ。

| コマンド | 説明 |
|---|---|
| `message push --to <id> [--text ... \| --flex <file> \| --json <file>]` | プッシュ送信。`--flex` は Flex JSON、`--json` は messages 配列そのもの |
| `message multicast --to <id1,id2,...> [...]` | 複数ユーザー送信 |
| `message broadcast [...]` | 全体送信 |
| `message reply --reply-token <t> [...]` | 応答送信（webhook 由来の replyToken） |
| `message content <messageId> -o <file>` | 受信メッセージのバイナリ DL（`api-data.line.me` 経由、`MessagingClient.Blob`） |
| `bot info` | Bot 情報 |
| `bot quota` / `bot quota consumption` | 送信上限 / 当月消費数 |
| `bot profile <userId>` | ユーザープロフィール |

- 実体は `MessagingClient.Api` / `.Blob`。メッセージ本文は共通ビルダ（text / flex / raw json）で `Message` 配列を組み立てる。
- **MCP の送信 4 ツールには `dryRun` 引数＋メッセージ組立支援ツール `line_message_schema` を追加**（旗艦ユースケース A）。詳細は §4.6。
- **CLI トップレベル別名（任意）:** 高頻度の `message push` は `line push` としても打てるトップレベル別名を許容する。ただし **MCP ツール名は例外なく `line_<area>_<verb>` に統一**（別名を作らない）＝ `line_message_push` / `line_bot_info` 等。
- **⚠️ 実装差分（本実装で確定）:** ①messages 配列ファイルのオプションは `--json <file>` ではなく **`--messages <file>`**（グローバル出力オプション `--json` との衝突回避）。②`message content` の出力先は **`-o`/`--output`**。③CLI トップレベル別名 `line push` は MVP では未実装（`line message push` のみ。任意項目のため）。

### 4.3 C. Webhook 開発支援（`line webhook ...`）

| コマンド | 説明 | MCP 公開 |
|---|---|---|
| `webhook listen --port <n> [--secret <s>]` | ローカル受信サーバ。署名検証しつつ受信イベントを整形表示（トンネル併用） | ✕（長時間常駐のため CLI 専用） |
| `webhook verify --body <file> --signature <sig> [--secret <s>]` | 保存ペイロードの署名検証＋逆直列化（イベント要約表示） | ○ |
| `webhook replay --body <file> --to <url>` | 保存イベントを指定 URL へ再送（アプリ側デバッグ） | ○ |

実体は `WebhookRequestParser`（署名検証＝Core の `WebhookSignatureValidator`、逆直列化＝自己完結の `KiotaJsonSerializer` 非依存経路）。`listen`/`verify` はこの Parser を共有。

**トンネル（確定）:** cloudflared / ngrok 等の外部トンネルは**バンドルせずドキュメント案内のみ**。CLI の責務は「ローカルで受信・署名検証・整形表示」に限定し、トンネルの張り方は手順とコマンド例をマニュアルに載せる（外部ツールのライセンス・更新追従・プラットフォーム差を抱え込まない）。

**`replay --to <url>` の宛先（spec ゲート Low 指摘）:** 再送先は**ユーザー指定の非 LINE ホスト**（ローカルアプリ等）のため、LINE クライアントの `AllowedHostsValidator`（制御系＋必要な data 系のみ）の対象外。replay は LINE 認証を伴わない**専用の素の `HttpClient`** を使い、宛先を検証しない。CLI ではユーザーが明示指定する前提のため SSRF 的懸念は許容する。
- **⚠️ MCP での SSRF 緩和（実装ゲート security Medium で追加）:** MCP の `line_webhook_replay` は URL 指定者が LLM のため、**既定でループバック宛先のみ許可**し、非ループバックはサーバ起動 `line mcp --allow-remote-replay` で opt-in（`McpToolOptions.AllowRemoteReplay`）。CLI 側の `webhook replay` は従来どおり無制限（人が指定）。

### 4.4 D. LIFF 管理（`line liff ...`）

| コマンド | 説明 | 実体 |
|---|---|---|
| `liff list` | アプリ一覧 | `LiffClient.GetAppsAsync` |
| `liff add --file <app.json>` | 追加 | `LiffClient.AddAppAsync` |
| `liff update <liffId> --file <app.json>` | 更新 | `LiffClient.UpdateAppAsync` |
| `liff delete <liffId>` | 削除 | `LiffClient.DeleteAppAsync` |

### 4.5 MCP ツール表面

CLI コマンドのうち **`webhook listen` を除く**すべてを MCP ツールとして公開する。命名は例外なく `line_<area>_<verb>`（例: `line_message_push`、`line_bot_info`、`line_token_issue`、`line_liff_list`、`line_webhook_verify`）。CLI のトップレベル別名（`line push` 等）は MCP には持ち込まない。各ツールに `[Description]` で LLM 可読な説明と引数説明を付す。

- **破壊的/送信系**（push/multicast/broadcast/reply/liff add・update・delete/token revoke/webhook replay）は description に副作用を明記し、可能なら MCP のツール注釈（destructive/idempotent 等）を付与。
- **既定は全ツール有効**。安全側運用のため `line mcp --read-only` で読み取り系（`bot info`/`bot quota`/`bot profile`/`liff list`/`token verify`/`webhook verify`/`message schema`）のみに絞れるフラグを設ける。
- **`token issue` のシークレット非露出（確定・§8-1）:** MCP のツール戻り値はモデル文脈（プロバイダ送信・会話履歴・ログ）へ載るため、**生のチャネルアクセストークンを MCP 経由で返さない**。
  - **既定（C）:** `line_token_issue` は発行したトークンを**ローカルのプロファイルへ保存**し、戻り値はメタのみ = `tokenType`（v2.1 / stateless）/ `expiresIn` / `keyId` / `maskedToken`（末尾数桁のみ、例 `…AbCd`）/ `storedProfile`。以後の送信系ツール（`line_message_push` 等）はこの `storedProfile` を参照して動作するため、エージェントは秘密値に触れず「送信できる能力（capability handle）」だけを得る。
  - **明示的な逃げ道（B）:** サーバ起動フラグ `line mcp --allow-secret-output`（既定 off）を立てたときのみ、`reveal: true` 付き呼び出しで生トークンを返す。人が値を別アプリ設定へ貼るケース向け。ツール description に露出リスクを明記する。
  - `--read-only` とは別軸: `token issue` は資格情報を作る書き込み系のため `--read-only` では元々無効。秘密出力の是非（C/B）は read-only の有無と独立に評価する。
  - `token verify` / `revoke` は**トークンが入力**で出力自体は秘密でない（有効期限・成否）ため本ポリシー対象外。ただし入力トークンをログ/verbose に出さないマスキングは共通で必要。

### 4.6 メッセージ組立支援（旗艦ユースケース A）

**背景（ローカル MCP の需要判断）:** ローカル PC で動く MCP から LINE を操作するユースケースを検討した結果、旗艦は **A: Bot 開発者が Flex/Template を対話で試作 → 自端末に push → 実機で見た目確認 → 直す ループ**と結論。本番の自動応答（Webhook 受信起点）は**サーバー常駐が本質で MCP 不適**（受信 HTTP を待ち受ける MCP は構造的に相容れない）ため対象外とする。

このループの核心は「AI が**型として妥当な** LINE メッセージ JSON を組めること」。型の非対称性が設計を決める:
- **単純 6 種**（text/image/video/audio/location/sticker）＝各 2〜4 プロパティで軽い → send ツールの `[Description]` に最小例を埋め込めば足りる（往復ゼロ）。
- **Flex** ＝ 44 型・`FlexBox` 約 40 プロパティ・**自己再帰ネスト**で桁違いに重い → 説明文埋込は非現実的、**取得ツールが必要**。

→ **非対称ハイブリッド**を採用。単純型は説明文、Flex/Template は on-demand のスキーマ取得ツール、送信前は `dryRun` 型検証で「組む→検証→実機送信」を安全に閉じる。

| ツール / 引数 | 種別 | 説明 |
|---|---|---|
| `line_message_schema(type)` | 読み取り（`--read-only` でも有効） | 指定ルート（`all`/`flex`/`template`/`imagemap`/`quickReply`/`action`、既定 `flex`）の JSON Schema を返す。副作用なし |
| `line_message_{push,multicast,broadcast,reply}` に `dryRun: bool`（既定 false） | — | true のとき **API を呼ばず型検証のみ**。件数＋各要素の CLR 型を返す。誤送信の安全弁 |

- **スキーマ源＝埋込 `openapi/messaging-api.yml`**（Kiota 生成と同一 spec ＝ドリフトしない）。実装 `Services/MessageSchemaService.cs` が `SharpYaml` で読み込み、指定ルートから **`$ref` 推移閉包を 1 つの JSON Schema ドキュメント**（root `$ref` ＋ `$defs`）として返す。**インライン展開しない**（`FlexBox` 自己再帰のため必須。visited set で停止）。`#/components/schemas/X` は `#/$defs/X` に書き換え、`discriminator`/`mapping` と `externalDocs.url`（LINE 公式ドキュメントへのリンク）は保持。同梱 yml はリポジトリ正本を単一の情報源として `EmbeddedResource` 参照（`scripts/generate.*` の再生成で更新）。
- **dryRun の検証本体＝共通サービス層** `MessageService.ValidateMessagesAsync`。既存 `MessageJson.ParseMessagesAsync` を再利用し、不正 JSON は `MessageInputException`（exit 2）へ写像（本改修で生 `JsonReaderException` を包む修正を併せて実施）。send ツールは `dryRun` 分岐を**資格情報の解決前**に置き、送信経路（`MessagingClient` 構築・HTTP）に到達しないことをテストで保証。
- **回帰テスト:** `MessageSchemaServiceTests`（閉包の完結＝dangling ref なし／ref 書き換え／`discriminator` 保持／`FlexBox` 自己再帰終端／不正 type 例外）、`MessageDryRunTests`（検証本体＋ツール層で資格情報未解決＝非送信を実証）。CLI テスト計 60。
- **非対象・follow-up:** CLI への `message schema` サブコマンド（サービス層は共有済みで容易・別途）／スキーマの LLM 向け要約・例示併記（Flex 生成品質が不足した場合の増強策として保留）。

### 4.7 E. リッチメニュー（`line richmenu ...` / `line_richmenu_*`）

**背景:** Rich Menu は `messaging-api.yml` に全操作が含まれ Kiota 生成済み（制御系＝`api.line.me`、画像＝`api-data.line.me` の `/content`）。ギャップは使い勝手で、ライブラリに便利ファサード `RichMenuClient`（`Line.OpenApi.Messaging`）を追加（CRUD/default/link＋画像ヘルパ `SetImageFromFileAsync`＝拡張子から `image/png`/`jpeg` を推論）。詳細は `docs/coverage-roadmap.md`。

**開発サイクル（MCP + CLI 連携）:** ①MCP `line_richmenu_schema` でスキーマ取得→AI が定義 JSON を組む ②MCP `line_richmenu_create`（`dryRun=true` で `validateRichMenuObject` により検証、false で作成し id 取得）③**CLI `line richmenu image <id> --file menu.png` で画像アップロード**（バイナリは MCP 非対応＝意図的に CLI 専用）④MCP `line_richmenu_set_default` / `line_richmenu_link` ⑤実機確認。

| 面 | 操作 |
|---|---|
| CLI `line richmenu` | `create`（--file）/`validate`/`list`/`get`/`delete`/`image`（--file, アップロード）/`image-download`（-o）/`set-default`/`get-default`/`cancel-default`/`link`/`unlink`/`id-of-user` |
| MCP 読み取り | `line_richmenu_schema`（richmenu\|richMenuAlias）/`list`/`get`/`get_default`/`id_of_user` |
| MCP 変更 | `line_richmenu_create`（`dryRun` 対応）/`delete`/`set_default`/`cancel_default`/`link`/`unlink` |

- スキーマ源は `MessageSchemaService` を共用（root に `RichMenuRequest`/`CreateRichMenuAliasRequest` を追加）。dryRun 検証は `RichMenuService.ValidateAsync`（オンライン `validateRichMenuObject`。message の dryRun がオフライン形状チェックなのと非対称＝rich menu は area 座標等の意味検証を LINE 側に委ねるのが妥当）。
- **画像 MCP 非公開の根拠:** バイナリ入出力は MCP のテキスト/JSON 前提と相容れず、モデル文脈にバイナリを載せる意味もないため CLI 専用とし、`line_richmenu_create` の description から CLI 手順へ誘導する。
- サービス層 `RichMenuService` は `RichMenuClient` をトークン単位でメモ化（MCP 常駐の HttpClient 累積回避、既存 Message/Liff と同方針）。

### 4.8 F. カバレッジパッケージ（`line insight` / `line audience` / `line shop`）

**背景:** 2026-07-15 のカバレッジ拡充で追加した `Line.OpenApi.Insight` / `.ManageAudience` / `.Shop` をツールへ露出する（`.Module` はパートナー限定・概念難でローカル開発ツールに不適のため見送り）。各ファサードの薄ラップで、既存サービス層（トークン単位メモ化）と同型。

| 面 | 操作 |
|---|---|
| CLI `line insight` | `demographic`/`deliveries <date>`/`followers <date>`/`events <requestId>`/`per-unit <unit> --from --to`/`richmenu-summary <id> --from --to`/`richmenu-daily <id> --from --to`（日付は yyyyMMdd） |
| CLI `line audience` | `list [--page --size]`/`get <id>`/`create --file`/`add-users --file`/`delete <id>`/`upload-file --file [--description --ifa --upload-description]`/`add-file <id> --file [--upload-description]` |
| CLI `line shop` | `mission --file` |
| MCP 読み取り | `line_insight_*`（7 本・全 read-only＝`--read-only` でも有効）/`line_audience_list`/`line_audience_get` |
| MCP 変更 | `line_audience_create`/`line_audience_add_users`/`line_audience_delete`/`line_shop_mission` |

- **Insight は全 GET＝全 read-only。** MCP でも `--read-only` に含める（分析ナレーション用途と好相性）。生成レスポンスモデルは素のデータのため DTO 化せず JSON 直列化（rich menu の `get` と同方針）。
- **by-file アップロードは CLI 専用。** `audience upload-file`/`add-file` は multipart のファイル入力で、バイナリ/ファイルは MCP のテキスト/JSON 前提と相容れないため MCP 非公開（rich menu 画像と同方針）。MCP の `line_audience_create` description から CLI 手順へ誘導。ManageAudience は control/data 2 ホストだがファサードが R1 分離を解決済み。
- **入力 JSON の解析ガード:** `audience create`/`add-users`・`shop mission` は生成リクエストモデルへ解析し、不正 JSON は `MessageInputException`（exit 2）に写像（`RichMenuService.ParseAsync` と同型）。
- **⚠️ 実機確認（GA 前）:** ManageAudience の multipart `file` パートに `filename` 属性が付かない（Kiota 仕様）ため、by-file アップロードの実 LINE 受理は要スモーク（ライブラリ側の既知事項を踏襲）。

---

## 5. 設定ファイル

`~/.line/config.json`（Windows は `%USERPROFILE%\.line\config.json`）。

```jsonc
{
  "defaultProfile": "default",
  "profiles": {
    "default": {
      "channelAccessToken": "…",      // 任意（静的トークン運用）
      "channelId": "…",
      "channelSecret": "…",            // webhook 署名検証・トークン発行に使用
      "privateKeyPath": "~/.line/keys/default.pem", // 発行フロー用（パス参照のみ）
      "kid": "…"
    }
  }
}
```

- 秘密の平文保存になるため、生成時にファイル権限を絞る（Windows は現ユーザーのみの ACL、POSIX は `0600`）。保存時に警告を出す。
- `config` 系サブコマンド: `line config set/get/list`、`line config use <profile>`。

---

## 6. 出力・終了コード

- 既定は人間向け整形（表・要約）。`--json` で安定した JSON（スクリプト連携用）。
- 終了コード: `0`=成功 / `1`=一般エラー / `2`=引数エラー / `3`=認証・資格情報エラー / `4`=LINE API エラー（HTTP 4xx/5xx を要約）。
- LINE API のエラー応答（`details` 含む）は整形して表示。`--verbose` で生応答。

---

## 7. 配布・依存・テスト

- **配置（確定）:** 新設 `/tools/Line.OpenApi.Tools/`（`samples/` と同格）。`/src/` はライブラリ群専用のまま保ち、pack スモークテスト（`verify-packages.ps1` の 6 パッケージ厳密照合）・公開 API snapshot の対象範囲を汚さない。`LineOpenApi.slnx` に `/tools/` フォルダを追加。
- **配布:** `dotnet tool install -g Line.OpenApi.Tools`。`PackAsTool=true` / `ToolCommandName=line`。共通メタデータは `Directory.Build.props` を踏襲（SourceLink/決定的ビルド）。ただし **CLI は app** なので `IncludeSymbols` 等はツール向けに調整。
- **公開サイクル（確定）:** ライブラリ群とは**別サイクル・独立バージョン**。CLI は preview の MCP SDK に依存し安定度が異なるため、ライブラリの後方互換保証に巻き込まない。バージョンは `0.1.0-preview` 起点で以後独立採番。`release.yml` のタグ規約を分離（ライブラリ `v*` / CLI `tools-v*`）。
- **参照:** `Line.OpenApi.Bot`（Messaging + Webhook + ChannelAccessToken を束ねる）＋ `Line.OpenApi.Liff`。＋ `Cocona`、`ModelContextProtocol`、`Microsoft.Extensions.Hosting`。
- **テスト（`Line.OpenApi.Tests` へ追加 or 専用プロジェクト）:**
  - Services 層の単体テスト（既存の実 HTTP モックハンドラ流用で LINE API 呼び出しを検証）。
  - CLI 引数パース（Cocona コマンドのバインド）と終了コード。
  - MCP ツール登録の検証（`[McpServerTool]` が期待どおり列挙され、`--read-only` で破壊系が除外される）。
  - `webhook verify` の署名 OK/NG・逆直列化の回帰（既存 `WebhookRequestParser` テスト資産を再利用）。
  - 資格情報の優先順位（引数 > env > profile）と config ファイル権限。
- **公開 API snapshot:** CLI は app（ライブラリ表面の後方互換保証対象外）のため snapshot 回帰は課さない。代わりに CLI コマンド表面・MCP ツール表面のテストで退行を捕捉。

---

## 8. セキュリティ / 未決事項

- **セキュリティ:** シークレットの平文保存を最小化（パス参照優先・ファイル権限制限・保存時警告）。ログ/verbose にトークンや署名鍵を出さないマスキング。AllowedHostsValidator は本体既定（制御系＋必要な data 系のみ）を継承し CLI から緩めない。
- **MCP のシークレット露出（確定）:** `token issue` の MCP 戻り値は既定でメタのみ（C）、明示フラグ `--allow-secret-output` 時のみ `reveal: true` で生返却（B）。詳細は §4.5 を参照。MCP ツール戻り値がモデル文脈（プロバイダ送信・会話履歴・ログ）へ載る前提で、生トークンを既定で返さないことを不変条件とする。
- **確定済み:** slnx 配置＝新設 `/tools/`（§7）／ `webhook listen` のトンネル＝ドキュメント案内のみ（§4.3）／ NuGet 公開＝ライブラリ群と別サイクル・独立採番・タグ `tools-v*`（§7）。**本 spec に未決事項なし。**

---

## 9. レビュー・ゲート

本体の `docs/REVIEW-WORKFLOW.md` に準拠し、実装後に **code / security / test-arch** の 3 役ゲートを通す（MCP のシークレット露出面があるため security を重視）。記録は `docs/reviews/` に日付付きで残す。

**spec ゲート（済）:** 2026-07-14、`spec-reviewer` で本 spec をレビュー = **CONCERNS**（BLOCK 事由なし・実装フェーズ進行可）。全参照エンドポイント／公開メンバの実在を確認、R1・form-urlencoded・webhook 自己完結逆直列化・blob(api-data) の踏襲前提を妥当と判定。指摘は本改訂で反映済み:
- Medium①（トークン領域に DI ヘルパ／ファサード無し→生成クライアント自前配線）→ §2・§4.1 に明記。
- Medium②（JWT アサーション署名器はライブラリ未提供→CLI 自作）→ §4.1 に実装責務として明記。
- Low（B 系命名不整合→`message`/`bot` グループ化・MCP 名 `line_<area>_<verb>` 統一）→ §4.2・§4.5。
- Low（`replay --to` は AllowedHosts 対象外→専用 HttpClient 明記）→ §4.3。
- Low（v2.0 短命トークン／kid 列挙は将来候補）→ §4.1。
