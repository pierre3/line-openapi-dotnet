# LINE OpenAPI → .NET クライアントライブラリ 構成・設計方針

**対象リポジトリ:** https://github.com/line/line-openapi
**生成ツール:** Kiota（Microsoft 製 OpenAPI クライアントジェネレータ）
**対象言語 / ランタイム:** C# / **.NET 10 以降（LTS）**。サポート対象は**モダン .NET のみ**とし、`netstandard2.0` / .NET Framework は**対象外**（LINE 連携はモダンなシステムを想定）。
**最終更新:** 2026-07-13（rev.5 — 公開パッケージ ID / ルート名前空間を `Line.OpenApi.*` に確定）
**ステータス:** ドラフト（設計方針の合意用）

---

## 変更履歴

- **rev.5 (2026-07-13):** 公開パッケージ ID / ルート名前空間を **`Line.OpenApi.*`** プレフィックスに確定（§8「パッケージ命名」・§4.2 注記）。既公開の旧 SDK `Line.Messaging`（pierre3/kenakamu 所有）との ID・名前空間衝突を回避するため。
- **rev.4 (2026-07-13):** ドキュメント/ユーザーマニュアル方針（§13）を追加。DocFX で英語 API リファレンスを自動生成し、概念記事は英語・日本語の 2 系統で提供。あわせて**ソースコードのコメントを全て英語化するポリシー**（§13.2）を確定。
- **rev.3 (2026-07-10):** TFM を `net10.0` 単一へ変更、netstandard2.0 を対象外に。
- **rev.2 (2026-07-09):** パッケージ構成を「利用シーン単位の分割」に確定（優先: メッセージ送受信 Bot / LIFF 管理）。G0 レビューの必須指摘（複数 base URL、mime-types 出し分け、PoC スコープ、webhook 多態）を反映。
- rev.1 (2026-07-09): 初版。

---

## 1. 目的とスコープ

LINE が公開する OpenAPI 仕様（`line/line-openapi`）から、Kiota を用いて **強く型付けされた .NET クライアントライブラリ** を生成し、NuGet パッケージとして配布・保守できる状態にすることを目的とする。

本ドキュメントは実装前の**構成・設計方針の合意**を目的とし、コードは含まない。

### スコープ

- **優先して構築する利用シーン:** ①メッセージ送受信（Bot）、②LIFF 管理。
- **将来追加（今回は生成しない）:** マーケティング/分析（insight, manage-audience）、モジュール/代理（module, module-attach）、ミッションスタンプ（shop）。設計上は追加余地を残す。
- 生成コード本体は「ブラックボックス（中身は読まない）」前提で扱う。レビュー主眼は**手書きコード**と**公開 API の使い勝手**。

---

## 2. 対象 API 仕様

`line/line-openapi` に含まれる仕様ファイルは以下の 9 点（README 準拠）。「優先」列は今回の構築対象。

| ファイル | OpenAPI | エンドポイント | 内容 | 種別 | 優先 |
|---|---|---|---|---|---|
| `channel-access-token.yml` | 3.0.0 | `api.line.me` | チャネルアクセストークン発行 | 呼び出し API | ★ |
| `messaging-api.yml` | 3.0.0 | `api.line.me`, `api-data.line.me` | Messaging API（中核） | 呼び出し API | ★ |
| `webhook.yml` | 3.0.3 | （なし） | Webhook イベントオブジェクト定義 | **モデルのみ** | ★ |
| `liff.yml` | 3.0.2 | `api.line.me/liff/` | LIFF アプリ管理 | 呼び出し API | ★ |
| `insight.yml` | 3.0.0 | `api.line.me/v2/bot/insight/` | Insight（統計） | 呼び出し API | 将来 |
| `manage-audience.yml` | 3.0.0 | `api.line.me`, `api-data.line.me` | オーディエンス管理 | 呼び出し API | 将来 |
| `module.yml` | 3.0.0 | `api.line.me/v2/bot/` | Messaging API（Module 機能） | 呼び出し API | 将来 |
| `module-attach.yml` | 3.0.0 | `manager.line.biz` | Module アタッチ | 呼び出し API | 将来 |
| `shop.yml` | 3.0.0 | `api.line.me/shop/` | ミッションスタンプ | 呼び出し API | 将来 |

### 設計上重要な観察

1. **`webhook.yml` はエンドポイントを持たない** — 受信イベント JSON の型定義のみ。モデル生成専用として受信ペイロードのデシリアライズに使う（§4.3）。
2. **`messaging-api.yml` は単一仕様の中で複数ホストを跨ぐ** — `api.line.me`（制御系）と `api-data.line.me`（コンテンツ取得系）。**これはパッケージ分割とは無関係の、クライアント生成レイヤーの制約**（§4.4）。
3. 仕様は独立した複数ファイル。**仕様（≒利用シーン）ごとに独立生成**する構成が自然。

---

## 3. ツール選定（Kiota 採用）

- **採用理由:** Microsoft 公式・OSS、多言語対応、fluent なリクエストビルダーによる探索的で型安全な呼び出し体験、公式ドキュメント充実。
- **割り切り:** Kiota は生成コードの可読性を明示的に非目標としている。生成物は「opaque box」として扱い、公開 API の使い勝手と運用性で評価する（合意済み）。
- **代替案（不採用）:** NSwag / OpenAPI Generator。必要になればサブ検証で比較可能。

---

## 4. 全体アーキテクチャ

### 4.1 レイヤーの整理（パッケージ vs クライアント）

- **パッケージ（NuGet）** = 配布・参照の単位。1 パッケージに複数の Kiota クライアントを同梱できる。
- **Kiota クライアント** = 1 仕様（またはサブセット）から生成し、リクエストアダプタに **base URL を 1 つ**持つ。

複数 base URL 問題（§4.4）は**クライアントのレイヤー**の話であり、パッケージ分割方針とは独立。

### 4.2 パッケージ構成（利用シーン単位・確定）

横断的な土台を **`Line.Core`** に集約し、利用シーン別パッケージがこれに依存する。

**パッケージ一覧**

| パッケージ | 区分 | 対象仕様 | 主な利用シーン | 依存 | 備考 |
|---|---|---|---|---|---|
| **Line.Core** | 共通基盤 | （なし・手書きのみ） | 全シーン共通の土台 | — | 認証プロバイダ、`AllowedHostsValidator`、Webhook 署名検証(HMAC-SHA256)、リトライ/429/例外/ロギング |
| **Line.ChannelAccessToken** | 優先 | `channel-access-token.yml` | トークン発行・失効（Bot/LIFF 共通の認証前提） | Line.Core | 生成時に `form-urlencoded` を含める |
| **Line.Messaging** | 優先 | `messaging-api.yml` | メッセージ送信・応答・コンテンツ取得（Bot） | Line.Core | 内部で制御系(`api.line.me`)とデータ系(`api-data.line.me`)を 2 クライアント分離、ファサードで統合 |
| **Line.Messaging.Webhook** | 優先 | `webhook.yml` | Webhook 受信・イベント解析（Bot） | Line.Core | モデル専用。多態(oneOf/discriminator)デシリアライズ。署名検証は Core 側 |
| **Line.Liff** | 優先 | `liff.yml` | LIFF アプリの登録・管理 | Line.Core | 単一ホストでシンプル |
| **Line.Bot** | 任意（メタ） | — | Bot 一式を 1 参照で導入 | Messaging + Messaging.Webhook + ChannelAccessToken | 便宜パッケージ。導入は任意 |

> **命名（rev.5・確定）:** 上表の短縮ラベル（`Line.Core` 等）は**設計上の呼称**。**公開 NuGet ID と C# ルート名前空間は `Line.OpenApi.*` プレフィックス**を用いる（`Line.OpenApi.Core` / `Line.OpenApi.ChannelAccessToken` / `Line.OpenApi.Messaging` / `Line.OpenApi.Messaging.Webhook` / `Line.OpenApi.Liff` / `Line.OpenApi.Bot`）。理由は §8「パッケージ命名」参照。以降、本書で `Line.Messaging` 等と記す箇所は `Line.OpenApi.Messaging` 等に読み替える。
| **Line.Insight** | 将来 | `insight.yml` | 統計・分析（バックオフィス） | Line.Core | 需要に応じて追加生成 |
| **Line.ManageAudience** | 将来 | `manage-audience.yml` | オーディエンス配信（マーケティング） | Line.Core | `api-data` 系あり（§4.4 と同パターン） |
| **Line.Module** | 将来 | `module.yml` + `module-attach.yml` | モジュール/代理連携 | Line.Core | `manager.line.biz` 含む上級シナリオ |
| **Line.Shop** | 将来 | `shop.yml` | ミッションスタンプ | Line.Core | 限定的 |

**共通基盤**

- **`Line.Core`** — 手書きのみ（生成コードなし）。Kiota ランタイム依存の集約、認証プロバイダ（`IAccessTokenProvider` 実装・`BaseBearerTokenAuthenticationProvider` 配線）、`AllowedHostsValidator` 設定、Webhook 署名検証（HMAC-SHA256）ユーティリティ、リトライ/レート制限(429)/例外/ロギング方針。全シーンパッケージが依存。

**優先構築（Bot / LIFF）**

- **`Line.ChannelAccessToken`** — `channel-access-token.yml` 生成クライアント（トークン発行/失効）。Bot・LIFF 双方の認証前提。`Line.Core` 依存。
- **`Line.Messaging`** — `messaging-api.yml`。内部で **`api.line.me` 系クライアントと `api-data.line.me` 系クライアントを分離生成**し、ファサードで統合（§4.4）。`Line.Core` 依存。
- **`Line.Messaging.Webhook`** — `webhook.yml` のイベントモデル＋受信ヘルパ。署名検証の実体は `Line.Core`。送信（`Line.Messaging`）と**受信は利用シーンが異なる**ため分離。`Line.Core` 依存。
- **`Line.Liff`** — `liff.yml`。単一ホストでシンプル。`Line.Core` 依存。

**便宜メタパッケージ（任意）**

- **`Line.Bot`** — `Line.Messaging` + `Line.Messaging.Webhook` + `Line.ChannelAccessToken` を束ねる。Bot 一式を 1 参照で導入したい利用者向け。導入は任意。

**将来追加（今回は生成しない）**

- `Line.Insight` / `Line.ManageAudience`（api-data 系あり）/ `Line.Module`（module + module-attach、`manager.line.biz` 含む）/ `Line.Shop`。いずれも `Line.Core` 依存で後付け可能。

**依存関係（相互依存なし・一方向）**

```
Line.Core
  ├── Line.ChannelAccessToken
  ├── Line.Messaging
  ├── Line.Messaging.Webhook
  └── Line.Liff
Line.Bot → (Messaging + Messaging.Webhook + ChannelAccessToken)
```

### 4.3 `webhook.yml` の扱い（モデル専用）

`webhook.yml` はエンドポイントを持たないため、モデル型のみ採用し、受信 Webhook ボディのデシリアライズに使う。Kiota モデルは `IParsable` 実装で `KiotaJsonSerialization` 等で復元可能。署名検証（`x-line-signature`、チャネルシークレットの HMAC-SHA256）は仕様外のため `Line.Core` の手書きユーティリティで実装。

> 注意: Webhook イベントは oneOf + discriminator の多態構造。Kiota は discriminator が無いと `MissingDiscriminator` 警告を出し多態復元が不完全になりうる。生成時の警告有無を G1 で確認し、多態デシリアライズをテストで検証（§10）。

**受信グルー（実装済み）:** 上記の「署名検証（Core）＋逆直列化」を利用側が毎回手組みするのは煩雑なため、`Line.Messaging.Webhook` に薄い受信ヘルパ `WebhookRequestParser` を追加した（優先利用シーン①「メッセージ送受信」の受信側を完成させる）。`ParseAsync(body, signature)` が生バイトに対する署名検証（NG は `WebhookSignatureException`）と `CallbackRequest` への逆直列化（失敗は `WebhookPayloadException`、基底 `WebhookException`）を 1 呼び出しに束ねる。逆直列化は **JSON 自己完結の `KiotaJsonSerializer` を使い、グローバルな既定シリアライザレジストリ（`RegisterDefaultDeserializer`）に依存しない**（Messaging クライアント未構築でも単独動作・副作用なし）。イベントの多態復元は生成コードの discriminator に委ね、ヘルパは `CallbackRequest` を返すのみ。DI は `AddLineWebhook`（HTTP 送信を伴わないため `IHttpClientFactory` は不要）。これにより本パッケージは「モデル専用」から「モデル＋受信グルー」へ拡張された（署名検証の実体は引き続き `Line.Core`）。

### 4.4 複数 base URL 問題と確定対処

**前提（事実）:** Kiota は 1 クライアント = root `servers` の**先頭 1 件のみ**を base URL に採用し、オペレーション単位の `servers` オーバーライドを尊重しない（`MultipleServerEntries` 検証警告）。`messaging-api.yml` はコンテンツ取得系（`getMessageContent` 等）を `api-data.line.me` に割り当てるため、**単一クライアント生成では誤ホストになる**。「可能性」ではなく確定制約として扱う。

**確定対処（`Line.Messaging` 内部）:**

1. `--include-path` / `--exclude-path` で **制御系と data 系を 2 クライアントに分離生成**する。**G1 実確認済みの重要事実:** data 系は 5 件（`getMessageContent` / `getMessageContentPreview` / `getMessageContentTranscodingByMessageId` / `getRichMenuImage` / `setRichMenuImage`）で、**全て `/v2/bot/` 配下**にあり制御系とプレフィックスを共有する。したがって `--include-path '/v2/bot/**'` では分離できない。**分離キーは共通サフィックス `/content`**（`/content/preview`, `/content/transcoding` を含む）。制御系 = `--exclude-path '**/content' '**/content/**'`、data 系 = `--include-path '**/content' '**/content/**'`。`getMessageContentTranscoding` は JSON 応答だが api-data ホストのため **data 側に含める**。
2. データ系クライアントは生成後に `RequestAdapter.BaseUrl = "https://api-data.line.me"` を設定して使う（分離生成だけでは自動でホストが変わらない点に注意）。
3. 利用者にはこの 2 クライアントを**単一ファサード**で束ねて提供し、送信/取得を意識させない。
4. 補助手段として、個別呼び出しは request builder の `WithUrl()` による絶対 URL 指定も可能。

将来の `manage-audience`（api-data 系あり）、`module-attach`（`manager.line.biz`）も同じパターンで対処。

---

## 5. 生成方針（Kiota コマンド）

仕様（≒利用シーン）ごとに生成。**`--structured-mime-types` は仕様別に出し分ける**（G0 指摘②）。

Messaging（制御系, api.line.me — `/content` を除外）:

```bash
kiota generate -l CSharp \
  -d ./openapi/messaging-api.yml \
  --exclude-path '**/content' --exclude-path '**/content/**' \
  -c MessagingApiClient -n Line.Messaging.Generated.Api \
  -o ./src/Line.Messaging/Generated/Api \
  --exclude-backward-compatible \
  --structured-mime-types application/json
```

Messaging（データ系, api-data.line.me — `/content` のみ、生成後に `BaseUrl` を上書き）:

```bash
kiota generate -l CSharp \
  -d ./openapi/messaging-api.yml \
  --include-path '**/content' --include-path '**/content/**' \
  -c MessagingBlobApiClient -n Line.Messaging.Generated.Blob \
  -o ./src/Line.Messaging/Generated/Blob \
  --exclude-backward-compatible
```

（blob 応答は `*/*` の生バイナリ = Stream。`getMessageContentTranscoding` は JSON 応答だが api-data ホストのため data 側に含まれる。両クライアントは単一ファサードで統合する。）

ChannelAccessToken（**form-urlencoded を含める**）:

```bash
kiota generate -l CSharp \
  -d ./openapi/channel-access-token.yml \
  -c ChannelAccessTokenClient -n Line.ChannelAccessToken.Generated \
  -o ./src/Line.ChannelAccessToken/Generated \
  --exclude-backward-compatible \
  --structured-mime-types application/json --structured-mime-types application/x-www-form-urlencoded
```

Webhook（モデルのみ）／LIFF も同様に個別生成。

### オプション方針

- `--exclude-backward-compatible`: 有効（obsolete コードを生成しない）。
- `--additional-data`: 既定 true 維持（仕様先行追加の取りこぼし防止）。
- `--structured-mime-types`: **仕様別**。messaging=json、channel-access-token=json+form-urlencoded。**blob（コンテンツ取得/リッチメニュー画像）は `*/*` の生バイナリで、構造化 mime 非該当のため Stream として扱われる**（multipart ではない）。data クライアントは Stream I/O 前提で設計する。
- **webhook のモデル生成:** webhook.yml はモデルを唯一のオペレーション `/callback` 経由で参照するため、**`/callback` を除外するとモデルが一切生成されない**。したがって `/callback` は残し、生成される callback メソッド（server は example.com のダミー）は使わず**モデルのみ消費**する。
- `--include-path` / `--exclude-path`: ホスト分離・サブセット生成に使用。
- `--class-name` / `--namespace-name`: 衝突回避のため各クライアントに明示。
- `kiota-lock.json` は Git にコミットし、`kiota update` と差分検知の基準にする。

---

## 6. 依存パッケージとターゲットフレームワーク

- **必須参照:** `Microsoft.Kiota.Bundle`（`Microsoft.Kiota.Abstractions` を含む）。
- **`.csproj`:** `TargetFramework` は **`net10.0`（単一）**、`LangVersion=latest`、`Nullable=enable`。
- 生成物と `Microsoft.Kiota.*` ランタイムの**バージョン整合**が重要。`kiota info` 推奨版に合わせ、CI で Kiota バージョンと生成物をロック・検証（§9、R3）。
- **netstandard2.0 / .NET Framework は対象外**（rev.3, 2026-07-10）。理由: LINE 連携はモダン .NET を想定。net10.0 単一化で `CryptographicOperations.FixedTimeEquals` 等の標準 API を直接利用でき、`#if NETSTANDARD2_0` シムが不要になる。到達範囲より簡潔さ・最新最適化を優先する判断。ロールフォワード互換により net10.0 以上のランタイムで動作するが、net8.0/net9.0 の利用側からは参照不可（意図した線引き）。

---

## 7. 認証設計（チャネルアクセストークン）

- **方式:** `IAccessTokenProvider` 実装 + Kiota 標準 `BaseBearerTokenAuthenticationProvider`。ヘッダ組立は基底に委譲し、トークン取得ロジックのみ実装。
- **依存方向（重要）:** 循環依存を避けるため配置を分ける。**抽象（`IAccessTokenProvider` 等）と静的トークンプロバイダは `Line.Core`** に置く。**`Line.ChannelAccessToken` クライアントを消費して更新する「更新型プロバイダ」は `Line.ChannelAccessToken`（または上位パッケージ）** に置く。`Line.Core → Line.ChannelAccessToken` の逆依存を作らない。
- **静的トークン（長期）:** 保持して返す単純プロバイダ（`Line.Core`）。
- **短期トークン（v2.1 / JWT）:** `Line.ChannelAccessToken` クライアントで発行し、有効期限を見て**実行時に取得・更新**する更新型プロバイダ（`Line.ChannelAccessToken` 側）。期限切れ判定と**並行更新の二重発行防止**を実装しテストする（中程度指摘）。
- **`AllowedHostsValidator`:** `api.line.me` / `api-data.line.me` を許可。**許可ホストはハードコードせずパッケージ側から注入・拡張可能**にし、将来の `manager.line.biz`（Module）追加に備える。許可外へトークンを付与しない**負側テスト**を用意（中程度指摘）。
- **DI 統合:** クライアントファクトリを DI 登録し、`HttpClientRequestAdapter(authProvider)` → 各クライアントを構築。

---

## 8. パッケージング・配布

- **パッケージ命名（rev.5・確定）:** 公開 NuGet ID と C# ルート名前空間は **`Line.OpenApi.*` プレフィックス**で統一する。
  - **理由:** ①`Line.Messaging` は既に NuGet 公開済み（`pierre3` / `kenakamu` 所有の旧 C# SDK「SDK for the LINE Messaging API」、v1.4.5・16 万 DL）で **ID が衝突**する。②旧 SDK は **`Line.Messaging` 名前空間**も占有するため、素の `Line.*` 名前空間だと両参照時に型が衝突する。③`Line.OpenApi.*` は `Line.` ルートを保ちつつ本ライブラリ（LINE 公開 OpenAPI からの生成）を旧 SDK と明確に分離し、リポジトリ/ソリューション名（`line-openapi-dotnet` / `LineOpenApi.slnx`）とも整合する。
  - **確認:** `Line.OpenApi.{Core,ChannelAccessToken,Messaging,Messaging.Webhook,Liff,Bot}` は 2026-07-13 時点で NuGet 全件未使用（空き）を確認済み。
  - **適用範囲:** NuGet `PackageId`、アセンブリ名、ルート名前空間、DocFX の `filterConfig.yml`（`Line.OpenApi.*.Generated` 除外）、公開 API snapshot の approved、README/マニュアルの `using`・コード例。
- **配布:** NuGet。SourceLink・XML ドキュメント・決定的ビルドを有効化。
- **バージョニング:** 各パッケージ SemVer。仕様スナップショットの上流コミット SHA/取得日は `openapi/upstream-manifest.json` に記録（§9・実装済み）。
- **回帰の baseline:** 公開 API 表面（public 型/シグネチャ）に snapshot 対象を限定し、内部生成差分のノイズを避ける（中程度指摘）。

---

## 9. 仕様更新への追従と CI/CD（実装済み）

上流 `line/line-openapi` はタグ/リリースを持たず、spec の `info.version` も実質固定値（`0.0.1`/`1.0.0`）のため、**取り込み世代の唯一の信頼できるアンカーは上流コミット SHA**。これを軸に以下を実装した。

**バージョンアンカー = `openapi/upstream-manifest.json`:** 取り込んだ上流コミット `ref`（SHA）・取得日・各 spec の**LF 正規化後 sha256** を記録する単一の真実源。設計当初の意図（「仕様スナップショットのコミットハッシュ/取得日を記録」§8）の実装。同梱 `openapi/*.yml` はその `ref` を LF + urn 正規化した確定スナップショット（`.gitattributes` の `openapi/*.yml text eol=lf` で改行を固定）。

**⚠️ CRLF 落とし穴:** 手元 spec が CRLF・上流 raw が LF だと生バイト比較で全行が差分に見える（messaging-api だけで約 11,800 行の誤検知）。よってハッシュ/比較の前に必ず改行を LF 正規化する。正規化ロジック（LF + フロー配列内 urn 引用符化）は `scripts/lib/SpecNormalization.ps1` に一元化し、取り込み（`generate.ps1`）と検知（`check-spec-drift.ps1`）で共有する（乖離すると永久誤検知になるため）。

**週次自動追従（`.github/workflows/spec-sync.yml`、cron + 手動）:**
1. **検知** `scripts/check-spec-drift.ps1` — manifest 基準で上流 `main` と内容ハッシュ比較（純検知・非破壊）。imported spec に差分があれば drift。
2. **追跡 Issue upsert** — drift 時、`spec-sync` ラベルの Issue を作成/更新（compare リンク・spec 別状態・上流コミット一覧）。同期回復時は自動クローズ。
3. **再生成** `scripts/generate.ps1 -Update -Ref <latestSha>` — SHA ピンで再取得 → 正規化 → manifest 更新 → Kiota 再生成 → build + test。**Kiota CLI 版は 1.34.1 にピン**（R3・生成物との整合）。
4. **下書き PR** — 再生成コード＋正規化 spec＋manifest を含む draft PR を自動作成。**マージは常に人＋4役ゲート**（自動マージしない）。

破壊的変更は公開 API snapshot テストが捕捉。**生成コードだけの追加（新オペレーション/モデル）は snapshot に出ない**ため、PR 本文のレビュアーチェックリストで人手確認する（手書きファサードの要否判断）。全パッケージは `Line.Core` + Kiota ランタイム版にロックステップ連動。

> ローカル運用: `pwsh scripts/check-spec-drift.ps1`（ドリフト有無）→ `pwsh scripts/generate.ps1 -Update`（再取得＋再生成）→ `dotnet build`/`dotnet test`。手動でも同じ経路。

---

## 10. テスト方針

- 生成物スモーク（ビルド・主要ビルダー/モデルの存在）。
- **Webhook 多態デシリアライズ:** 単一/複数/未知イベント混在の実ペイロードで型解決を検証。
- シリアライズ往復（round-trip）。
- HTTP モックで URL テンプレート・クエリ・**認証ヘッダ**・**正しいホスト（api-data 系）**組み立てを検証。実 API は叩かない。
- `AllowedHostsValidator` の負側テスト、短期トークンの期限/並行更新テスト。
- 回帰: 公開 API 表面の snapshot テスト。
- **自己完結性（グローバル状態非依存）の回帰は、参照を最小化した独立テストアセンブリで保証する。** 例: Webhook 受信ヘルパがデシリアライザのグローバル既定レジストリに依存しないことは、`Line.Messaging.Webhook` のみ参照する `Line.Messaging.Webhook.IsolationTests`（他クライアントや `RegisterDefaultDeserializer` を含まない＝クリーンなプロセス）で検証する。通常のテストアセンブリは他テストがプロセス共有レジストリを汚染しうるため、この種の主張を証明できない。同種の「グローバル非依存」主張が他パッケージで生じた場合も同方式を横展開する。

---

## 11. リスク・留意点

| ID | リスク | 影響 | 対応 |
|---|---|---|---|
| R1 | 複数 base URL（api.line.me/api-data.line.me） | data 系が誤ホスト | **確定対処済み**（§4.4: 分離生成 + BaseUrl 上書き）。PoC で実測確認 |
| R2 | 生成メソッド/型名が仕様命名に依存し不自然 | 使い勝手低下 | 主要 API で命名確認、手書きファサードで補う。**G4④で対処済み:** ①`Action`→`ActionObject`（Kiota が `System.Action` 衝突回避で基底多態型を改名。生成物はリネーム不可のため公開ドキュメントで周知＝下記§命名周知）②`/oauth2/v3/token` の oneOf 合成ボディを `StatelessJwtAssertionTokenSource` で隠蔽 |
| R3 | Kiota バージョン更新で source-breaking / ランタイム不整合 | ビルド破壊・実行時例外 | バージョン固定、CI で生成物とロック検証 |
| R4 | 仕様更新への追従漏れ | 実 API と乖離 | CI で定期再生成 + 差分 PR |
| R5 | 将来の module-attach 等のホスト/認証差異 | 認証失敗 | §4.4 と同パターンで対処（将来） |
| R6 | Webhook 署名検証は仕様外 | セキュリティ | `Line.Core` で HMAC-SHA256 実装 + テスト |
| R7 | 多態の discriminator 不足 | 復元不完全 | **G1確認済(解消):** webhook / messaging とも discriminator+mapping 完備。messaging は多態20型すべて完備（Message=11, Action=9, Template=4, FlexComponent=9 等）。残るは channel-access-token `/oauth2/v3/token` の oneOf ボディ（軽微）のみ → **G4④で対処済み**（`StatelessJwtAssertionTokenSource` が合成ボディを隠蔽） |
| R8 | 生成コードは可読性非目標 | デバッグ困難 | opaque box 前提で合意（済） |

---

## 12. 実装ロードマップ（段階的）

1. **PoC（最優先・スコープ拡張済み）:** 次を含めて検証する。
   - `Line.Core` の最小認証（**静的トークン**）。
   - `messaging-api.yml` を制御系 + **data 系（api-da
---

## 13. ドキュメント / ユーザーマニュアル（rev.4）

### 13.1 方針

- **生成ツール:** DocFX。手書き公開表面の XML doc コメントから **API リファレンス**を自動生成し、その上に**概念記事**（チュートリアル / ガイド / 利用例）を Markdown で重ねる。
- **API リファレンスの対象範囲:** 手書き公開表面のみ。`filterConfig.yml` で `Line.*.Generated` 名前空間を除外する（opaque box 方針と整合）。生成ビルダーパス（`client.Api.V2.Bot.Message.Push.PostAsync` 等）は概念記事のコード例として提示する。
- **言語:**
  - ソースコードのコメントは全て英語（§13.2）。したがって XML doc コメント由来の **API リファレンスは英語（単一言語）**。
  - **概念記事・チュートリアル・利用例は英語・日本語の 2 系統**を用意する。DocFX に一級の i18n 機能がないため、1 サイト内に言語別記事ツリー（`en/` と `ja/`）を持ち、共通の英語 API リファレンスを両ガイドから参照する構成とする。`ja/` は `en/` の対訳（構成・見出し・コード例を対応させ内容一致を保つ）。
- **レイアウト:** `docs/manual/` 配下（`docfx.json` / `filterConfig.yml` / `index.md` / `toc.yml` / `en/` / `ja/` / `api/`）。ビルド出力 `_site/` は Git 追跡外（`.gitignore`）。
- **スコープ外:** 公開ホスティング / CI 発行フロー（GitHub Pages 等）は G5 リリース準備で扱う。API リファレンス本体の日本語化（doc コメント二言語運用）は保守コスト大のため非採用。

### 13.2 コメント言語ポリシー（確定）

- **ソースコード中のコメントは全て英語で記述する。** XML doc コメント（`///`）に加え、インライン `//` コメントも対象。既存の日本語コメントは英語へ置き直す。
- **目的:** 公開ライブラリとして国際的な利用・貢献に耐える一貫した英語コードベースとし、XML doc コメント由来の API リファレンスを英語で単一提供する。
- **翻訳時の必須事項:** 技術的警告の**内容を欠落させない**。特に `MessagingClient` の BaseUrl 構築順序（構築前に設定）、`/oauth2/v3/token` の form での入れ子直列化の落とし穴、Webhook 署名は生バイト列に対して検証する点、`KiotaJsonSerializer` のグローバルレジストリ依存など、既存コメントに含まれる注意書きは訳し漏らさない。
- **適用範囲はソースコードのコメントに限る。** 設計ドキュメント（`docs/**`）・`CLAUDE.md`・レビュー記録等の**プロジェクト文書は日本語のまま**とする。
