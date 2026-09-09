# CLAUDE.md — LINE OpenAPI → .NET クライアントライブラリ

このファイルは Claude Code が各セッションで自動読み込みするプロジェクト文脈です。作業前に必ず `docs/LINE-dotnet-client-design.md`（設計方針）と `docs/REVIEW-WORKFLOW.md`（レビュー運用）を参照すること。

**このファイルには恒久的な文脈のみを書く。** 特定セッション時点の一時的な引継ぎ（作業中断点・未確定判断・as-of の状態）は下記でインポートする専用ファイルへ。作業サイクルの経緯・判断根拠は `docs/reviews/`（日付付きレビュー記録）と `CHANGELOG.md` に残す。

@docs/SESSION-HANDOFF.md

> 引継ぎの運用: セッション終了時に `/handoff` で `docs/SESSION-HANDOFF.md` に一時状態を保存し、次セッションでこの import 経由で自動読み込みして再開する。内容を消化したら `/handoff-clear` で空テンプレートへ戻す（手動クリア）。
>
> `docs/SESSION-HANDOFF.md` は **Git 追跡対象外（`.gitignore` 済み）のローカル専用ファイル**。ローカル作業のセッションリフレッシュに使う想定で共有しない。存在しない環境（新規クローン等）では上の import は空になり、`/handoff` が標準テンプレートから自動生成する。

## プロジェクト概要

LINE 公開 OpenAPI 仕様（https://github.com/line/line-openapi）から **Kiota** で .NET/C# クライアントライブラリを生成し、NuGet 配布・保守する。加えて開発支援ティア（CLI/MCP ツール・AI ツール連携・Flex プレビュー）を同一リポジトリで提供する。

**現状: 全パッケージ GA 公開済み・未消化の残課題なし。** 将来追加候補（未着手・コミットではない）は `docs/coverage-roadmap.md` に集約。

## 公開物と現行バージョン

3 系統を**独立採番・独立タグ**でリリースする（`CHANGELOG.md` / `CHANGELOG_ja.md` に系統別の履歴）。

| 系統 | 対象 | タグ | 現行 |
|---|---|---|---|
| ライブラリ | `Line.OpenApi.*`（12 パッケージ = 11 code + 1 meta） | `v*` | 1.0.0 |
| ツール | `Line.OpenApi.Tools`（`dotnet tool`・コマンド `line`） | `tools-v*` | 1.3.0 |
| AI ツール | `Line.OpenApi.Extensions.AI` | `ai-v*` | 1.0.0 |

**ライブラリ 12 パッケージ**（`src/`。全て `Line.OpenApi.Core` のみに依存＝一方向依存 ADR）:
`Core` / `ChannelAccessToken` / `Messaging`（`MessagingClient`＋`RichMenuClient`） / `Messaging.Webhook`（`WebhookRequestParser`） / `Liff` / `Login` / `MiniApp` / `Insight` / `ManageAudience` / `Module` / `Shop` ＋ メタ `Bot`（コードなし・`Messaging`+`Messaging.Webhook`+`ChannelAccessToken` を束ねる／LIFF 非包含）。

**支援ティア**（`tools/`・`extensions/`。ライブラリの pack 契約からは除外）:

- `Line.OpenApi.Tools` — CLI（Cocona 2.2.0）と MCP サーバ（ModelContextProtocol 1.4.0）で同一サービス層を両出し。表面 = `config`/`token`/`message`/`bot`/`webhook`/`liff`/`richmenu`/`insight`/`audience`/`shop` ＋ Flex プレビュー。MCP は `line_<area>_<verb>` で **51 ツール**（`--read-only` で 29）。仕様 = `docs/CLI-MCP-tool-spec.md`。
- `Line.OpenApi.Extensions.AI` — LLM tool-calling（`Microsoft.Extensions.AI` の `AIFunction`）向けに Messaging をラップ＝アプリ内 in-process 公開（既存 MCP サーバ＝別プロセスの補完）。設計 = `docs/LINE-dotnet-AI-plugin-design.md`。
- `extensions/line-flex-viewer` — Flex プレビューの Copilot canvas 拡張＋同梱 Node MCP サーバ（依存ゼロ）。

**サンプル**（`samples/`・非パッケージ・オフライン既定）: `Console` / `Webhook` / `Login` / `Ai`。

## 確定している設計方針

- **生成ツール:** Kiota。生成コードは「opaque box（中身は読まない）」前提。レビュー主眼は手書きコードと公開 API の使い勝手。
- **パッケージ分割:** 利用シーン単位。共通基盤 `Line.OpenApi.Core` へ一方向依存（横依存禁止・`verify-packages.ps1` が nuspec で強制）。
- **TFM:** **`net10.0` 単一**（`Nullable=enable`、`LangVersion=latest`）。**netstandard2.0 / .NET Framework は対象外**（rev.3。理由: LINE 連携はモダン .NET を想定、`#if` シム不要で簡潔化。net8/9 利用側からは参照不可という線引きを了承済み）。
- **公開命名（rev.5）:** NuGet `PackageId` と C# ルート名前空間は **`Line.OpenApi.*`**（既公開の旧 SDK `Line.Messaging` との衝突回避）。`PackageId`/`AssemblyName`/`RootNamespace` はプロジェクト名から既定継承する（csproj で個別指定しない）。
- **各パッケージの型:** 生成クライアント＋薄い手書きファサード（`XxxClient`）＋DI 拡張（`AddLineXxx`・2 オーバーロード・冪等）＋`XxxOptions`。spec 非存在の領域（`Login`・`MiniApp`）は生成コードなしの全手書き。

## 実仕様の落とし穴（必ず順守）

過去に踏んだ実バグ・実仕様。回帰テストで固定済みのため**壊さないこと**。

- **複数 base URL（R1）:** `messaging-api.yml` は制御系 `api.line.me` と data 系 `api-data.line.me` が混在。Kiota は 1 クライアント=先頭 server のみ採用。data 系は blob 5 件で**全て `/v2/bot/` 配下・共通サフィックス `/content`**。→ `--exclude-path`/`--include-path` で 2 クライアント分離生成し、data 側は `RequestAdapter.BaseUrl = https://api-data.line.me`。ファサードで統合。`manage-audience.yml` も同型。
  - **⚠️ 順序が重要:** 生成クライアントはコンストラクタで `baseurl` を `PathParameters` へ確定させる（空なら `api.line.me` を既定採用）。`BaseUrl` は必ず**クライアント構築前**に設定する。構築後だと `PathParameters` に反映されず `api.line.me` に飛ぶ。実装 `Line.OpenApi.Messaging/MessagingClient.cs`、回帰 `MessagingHostRoutingTests`。
- **form-urlencoded:** `channel-access-token.yml` のトークン発行は `application/x-www-form-urlencoded`。生成時 `--structured-mime-types` に含める。
  - **⚠️ oneOf 合成ボディは form で送れない:** `/oauth2/v3/token`（ステートレス）の form ボディは discriminator 無し oneOf → 生成物は合成ラッパ（`IComposedTypeWrapper`）で内側を**入れ子オブジェクト**として直列化するため Kiota の Form シリアライザが `"Form serialization does not support nested objects."` で失敗する。→ 手書き `ChannelAccessToken/StatelessJwtAssertionTokenSource.cs` が平坦な要求モデルを自前で `RequestInformation` に載せる。生成物の protected な `RequestAdapter`/`PathParameters` へは同一クラスの partial（`ChannelAccessTokenClientInternals.cs`・Generated 外・internal 公開）でアクセス。回帰 `StatelessJwtAssertionTokenSourceHttpTests`。v2.1 の非ステートレス発行は合成ボディでないため `JwtAssertionTokenSource` が生成ビルダーをそのまま利用。
- **命名 `Action`→`ActionObject`（R2）:** Kiota は `System.Action` 衝突回避で messaging の多態基底型を `ActionObject` に改名する（派生 `MessageAction`/`PostbackAction`/`URIAction` 等は素直）。生成物のためリネーム不可＝ドキュメントで周知する事項。
- **webhook:** 生成は**モデル専用**（`/callback` を除外するとモデルが生成されないため除外しない。生成される callback メソッドは使わない）。多態は discriminator+mapping 完備（20 型）。受信ヘルパ `WebhookRequestParser` が署名検証（Core の `WebhookSignatureValidator`）＋`CallbackRequest` 逆直列化を `ParseAsync(body, signature)` に束ねる（署名 NG=`WebhookSignatureException` / 本文 NG=`WebhookPayloadException`、基底 `WebhookException`）。イベント多態復元は生成 discriminator に委譲（分岐は利用側）。DI は `AddLineWebhook`（HTTP 非依存＝`IHttpClientFactory` 不要）。
  - **⚠️ 逆直列化は JSON 自己完結の `KiotaJsonSerializer.DeserializeAsync` を使い、グローバル既定レジストリ（`ApiClientBuilder.RegisterDefaultDeserializer`）に依存しない**（他クライアント未構築でも単独動作・副作用なし）。これを固定するのが専用テストプロジェクト `Line.OpenApi.Messaging.Webhook.IsolationTests`（1 件）。Kiota 2.0 は同期逆直列化 API を廃止したため `ParseAsync` は非同期のみ。
- **blob mime:** `*/*` の生バイナリ（Stream）。multipart ではない。リッチメニュー画像アップロードは LINE 側が `image/png`/`image/jpeg` を必須とするため手書きヘルパ `RichMenuClient.SetImageFromFileAsync` が拡張子から content-type を推論する。
- **multipart:** `ManageAudience` の by-file 2 本のみ（Kiota `MultipartBody`・data 系アダプタ）。**⚠️ `file` パートに `filename` 属性が付かない**（Kiota 仕様）。実運用で受理されなければここを疑う。
- **user access token は channel access token と別系統:** `Login`/`MiniApp` の一部 API は user access token。`Core` の汎用 `StaticBearerTokenProvider`（ホスト制限付き Bearer）＋`LineHosts.AccessLine` を使う。
- **`Login` の ID Token 検証はサーバ委譲のみ:** `POST /oauth2/v2.1/verify`（`LoginClient.VerifyIdTokenAsync`）。ローカル検証（Web=HS256／ネイティブ・LIFF=ES256+JWKS）は実装していない。
- **`MiniApp` はトークンを保持しない:** 呼び出しごとの引数（notifier 系＝channel access token（stateless/short-lived 限定）、IAP 予約＝user access token）。エラー型は notifier 系 `NotifierErrorResponse`（`message`/`details`）と IAP 系 `IapErrorResponse`（`errorCode` 付き）の 2 種に分離。
- **トークン領域は薄ラップしない（既知の非対称）:** `AddLineChannelAccessToken`/ファサードは無く、CLI は生成 `ChannelAccessTokenClient` を自前配線し、JWT 署名器（RS256）も自作（`Services/JwtAssertionBuilder`）。stateless は `StatelessJwtAssertionTokenSource` 必須。[[cli-token-domain-not-thin-wrap]]
- **署名検証の定数時間比較:** `CryptographicOperations.FixedTimeEquals` を直接使用（`Core/Webhook/WebhookSignatureValidator.cs`）。netstandard2.0 用の手実装分岐は削除済み。
- **⚠️ CRLF 落とし穴（spec 比較）:** 手元 CRLF・上流 LF の生バイト比較は全行誤検知する（messaging-api だけで約 11,800 行）。ハッシュ/比較の前に必ず LF 正規化する（`.gitattributes` の `openapi/*.yml text eol=lf` と併用）。
- **⚠️ 同一ロジックの二重持ち禁止（過去バグの教訓）:** spec 正規化は `scripts/lib/SpecNormalization.ps1` に一元化（取り込み `generate.ps1` と検知 `check-spec-drift.ps1` で共有／乖離すると永久誤検知）。Flex プレビューのローカルメディア封じ込めは Node 側 `extensions/line-flex-viewer/lib/assets.mjs` に集約し 2 サーバが共有。`tools/Line.OpenApi.Tools/web` と `extensions/line-flex-viewer/web` の共有アセットは **byte 一致を `FlexWebAssetsParityTests` が検査**する。

### 安全設計（変更時に壊さないこと）

- **AI ツールの安全ゲートは生成時クロージャ束縛**（`EnableSending`/`AllowBroadcast`/`DryRun`/`SendPolicy`/`BeforeSend`）で、**AIFunction の引数スキーマに出さない**＝LLM からバイパス不可（negative assertion テストで固定）。送信は明示 opt-in・既定 read-only、broadcast は独立 opt-in、DryRun/拒否時は transport 非接触。
- **MCP の安全フラグ:** `--read-only`（変更系ツールを広告しない）／`line_token_issue` は既定でシークレット非露出（`--allow-secret-output`）／`webhook replay` は既定ループバック限定（`--allow-remote-replay`）／バイナリ授受（rich menu 画像・audience by-file）は CLI 専用＝MCP 非公開／`webhook listen` も MCP 非公開。
- **Flex プレビュー:** ループバック `127.0.0.1` 固定・静的配信は埋込リソースのホワイトリスト・`/api/*` に Host/Origin 検証（DNS リバインド読取／CSRF 書換の遮断）。ローカルメディア配信は環境変数 `LINE_FLEX_MCP_ASSET_DIR`（**人が out-of-band 設定・LLM 非制御**）の opt-in で、JPEG/PNG＋mp4 のみ・拡張子 allowlist・パス正規化の前置比較・symlink 物理封じ込め。LINE 本体はローカル/`data:` URL を描画しないためプレビュー専用の利便機能。
- **Kiota セキュリティ最低版:** `Microsoft.Kiota.Abstractions >= 1.22.0`（CVE-2026-44503 / GHSA-7j59-v9qr-6fq9 = RedirectHandler のクロスホスト時の機密ヘッダ漏洩, CVSS 7.0 High。1.16.0 は影響あり）。現行 2.0.0 は修正を継承。`Microsoft.Kiota.Bundle` が全サブパッケージを同版にロックステップ固定するため下限は Abstractions の名指しで足る（Bundle を経由しない直接参照を足す場合のみ Http 側下限も明示）。

## レビュー運用（ゲート）

`docs/REVIEW-WORKFLOW.md` 準拠。4 役（仕様/コード/セキュリティ/テスト・アーキ）を**各段階のゲート**とし、サブエージェントで実行、**最終 go/no-go は人**。結果は `docs/reviews/` に日付付きで記録。**実装完了時点で必ず先にゲートへ回す**（実装→コミット→マージを先行させない）。

- **レビュアーサブエージェント:** `.claude/agents/*.md` の 4 役（`code-reviewer` / `security-reviewer` / `spec-reviewer` / `test-arch-reviewer`）を Agent ツールの `subagent_type` で直接起動できる（インタラクティブでは `@code-reviewer` 等）。
- **履歴:** 段階ゲート G0（設計）〜G5（リリース準備）は全て通過・main 反映済み。以降は機能サイクル単位で 3〜4 役ゲートを実施している。過去の判定・受容項目・非ブロッキング指摘の内容は `docs/reviews/` の日付順の記録を参照。

## 再生成・ビルド・テスト

```
# 生成（Kiota CLI は ~/.dotnet/tools。PowerShell では $env:PATH += ";$env:USERPROFILE\.dotnet\tools"）
pwsh scripts/generate.ps1
# ビルド（net10.0 単一）
dotnet build
# テスト（既定で全実行・opt-in フラグ不要）: lib 264 / Tools 131 / AI 26 / Webhook Isolation 1
dotnet test
# Flex ビューア拡張の Node テスト（依存ゼロ・CI の extension-test ジョブと同一）
node --test extensions/line-flex-viewer/lib/assets.test.mjs
# pack スモークテスト（12 パッケージのレイアウト・内部依存グラフ・Extensions.AI の依存 2 本を検証）
pwsh scripts/verify-packages.ps1
# spec マニフェスト整合（CI の build-test 冒頭と同一）
pwsh scripts/verify-spec-manifest.ps1
# ドキュメント生成（DocFX。ローカルツール＝.config/dotnet-tools.json にピン留め）
dotnet tool restore                        # 初回のみ
dotnet docfx docs/manual/docfx.json        # metadata + build → docs/manual/_site/（--serve でプレビュー）
```

- Kiota CLI は `dotnet tool install --global Microsoft.OpenApi.Kiota --version 1.34.1` で導入。`generate.ps1` が版ピンを照合し、`channel-access-token.yml` の未引用 `urn:...` を**冪等に引用符化**する（再取得時も安全）。
- ビルド/テストは PowerShell 実行が安定。

## CI / リリース

- **`ci.yml`**: `build-test`（spec マニフェスト照合 → restore＝NuGet 監査を warnings-as-errors 化 → build → 脆弱性リスト → `dotnet test`）／`extension-test`（Node の封じ込めテスト）／`pack-verify`（`verify-packages.ps1`）。
- **`release.yml`**: タグ駆動の 3 ジョブ — `v*`＝ライブラリ（支援ティアは `ExcludeToolFromPack` で除外）／`tools-v*`＝Tools のみ／`ai-v*`＝Extensions.AI のみ。**公開は Trusted Publishing (OIDC)**（`id-token: write` ＋ `NuGet/login`＝commit SHA ピン留めで 1h の短命キーを取得。長寿命 `NUGET_API_KEY` は廃止済み。nuget.org 側ポリシー: owner=`pierre3` / repo=`line-openapi-dotnet` / workflow=`release.yml` / env=`nuget`）。
- **`docs.yml`**: `src/**` または `docs/manual/**` の変更で DocFX サイトを GitHub Pages へ発行。
- **`spec-sync.yml`**: 下記「上流仕様追従」。
- Actions は全て commit SHA ピン留め。

## 上流仕様追従（spec-sync）

上流 `line/line-openapi` は**タグ/リリースを持たず** spec の `info.version` も固定値のため、取り込み世代は**上流コミット SHA** で管理する。設計 §9 に詳細。

- **アンカー = `openapi/upstream-manifest.json`**: 取り込んだ上流コミット `ref`（SHA）・取得日・spec 別 **LF 正規化 sha256** を記録。同梱 `openapi/*.yml` はその ref を正規化した確定スナップショット。上流の既定ブランチは **`main`**（`master` は存在しない＝旧 URL は潜在バグ、SHA ピンで解消済み）。
- **spec 9 本中 8 本を取り込み済み。`module-attach.yml` のみ見送り**（別ホスト `manager.line.biz`＋form-urlencoded＋Basic 認証＋PKCE のパートナー限定 1 op で、現行 Bearer/AllowedHosts 基盤に載らない）＝manifest に `imported:false`＋awareness 用ハッシュのみ記録し、変化は検知するが再生成しない。
- **検知**: `pwsh scripts/check-spec-drift.ps1`（manifest 基準・純検知・ドリフト時 exit 1・`-Json`/`-FailOnAwareness` あり）。gh 優先・無ければ REST。
- **再取得＋再生成**: `pwsh scripts/generate.ps1 -Update [-Ref <sha>]`（SHA ピン再取得→正規化→manifest 更新→Kiota 生成）。既定（`-Update` 無し）は同梱 spec を使う再現生成で挙動不変。
- **週次自動化**: `.github/workflows/spec-sync.yml`（cron＋手動）が 検知→`spec-sync` ラベルの Issue upsert（回復時 close）→再生成→**draft PR** 自動作成。**マージは常に人＋4 役ゲート**（自動マージしない）。破壊的変更は公開 API snapshot が捕捉、生成コードのみの追加は PR チェックリストで人手確認。

## 規約

- 生成コードは `src/**/Generated/`。`kiota-lock.json` はコミットする。
- 全パッケージは `Line.OpenApi.Core` + Kiota ランタイム版にロックステップで追従（`Directory.Build.props` の `KiotaBundleVersion`、現状 **2.0.0**）。CLI（`Microsoft.OpenApi.Kiota`）は **1.34.1** 据え置き（2.x CLI 未リリース）。Kiota は CLI とランタイムを別系統でバージョニングする（詳細は `docs/R3-kiota-version-policy.md`）。
- `Microsoft.Extensions.AI.Abstractions` は Kiota ロックステップ**外**の独立軸（ADR-6）。版は `Directory.Build.props` の `MicrosoftExtensionsAIVersion`（現状 **10.9.0**）に集中ピン。実装/DI パッケージ（`Microsoft.Extensions.AI`）は公開パッケージから参照しない（`AIFunctionFactory` は Abstractions 収録。実装パッケージはサンプルのみ）。
- **共有ソース方式（`tools/shared/`）:** `MessageJson`＋平坦 DTO を `Line.OpenApi.Tools` と `Extensions.AI` の両 csproj に `<Compile Include Link>` でリンクコンパイル（名前空間 `Line.OpenApi.Tools.Services` 維持）。**NuGet 依存辺を作らない**＝一方向 ADR を壊さない。共有 DTO・`MessageInputException`・`MessageService` は **`internal`**（各消費者が JSON 直列化する実装詳細＝公開表面非露出）。
- 破壊的変更は**公開 API 表面の snapshot 差分**で検知（`PublicApiGenerator`・手書き表面のみ／`Generated` 除外＋完全性ガード）。生成物内部の差分はレビュー対象外。
- **コメントは全て英語**（XML doc `///` ＋インライン `//`、手書きコードのみ。生成物は対象外）。API リファレンスを英語で単一提供するため。設計 §13.2 準拠。プロジェクト文書（`docs/**`・本ファイル・レビュー記録）と `README_ja`/`CHANGELOG_ja` は日本語。
- **ドキュメント:** DocFX で英語 API リファレンス自動生成＋概念記事は英語/日本語 2 系統（`docs/manual/{en,ja}/`）。設計 §13 準拠。ユーザー向け変更では `README.md`/`README_ja.md`・該当概念記事・`CHANGELOG*.md` を、ツール系なら `docs/CLI-MCP-tool-spec.md` も更新する。
