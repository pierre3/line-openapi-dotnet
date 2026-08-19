# CLAUDE.md — LINE OpenAPI → .NET クライアントライブラリ

このファイルは Claude Code が各セッションで自動読み込みするプロジェクト文脈です。作業前に必ず `docs/LINE-dotnet-client-design.md`（設計方針 rev.2）と `docs/REVIEW-WORKFLOW.md`（レビュー運用）を参照すること。

**このファイルには恒久的な文脈のみを書く。** 特定セッション時点の一時的な引継ぎ（作業中断点・未確定判断・as-of の状態）は下記でインポートする専用ファイルへ。

@docs/SESSION-HANDOFF.md

> 引継ぎの運用: セッション終了時に `/handoff` で `docs/SESSION-HANDOFF.md` に一時状態を保存し、次セッションでこの import 経由で自動読み込みして再開する。内容を消化したら `/handoff-clear` で空テンプレートへ戻す（手動クリア）。
>
> `docs/SESSION-HANDOFF.md` は **Git 追跡対象外（`.gitignore` 済み）のローカル専用ファイル**。ローカル作業のセッションリフレッシュに使う想定で共有しない。存在しない環境（新規クローン等）では上の import は空になり、`/handoff` が標準テンプレートから自動生成する。

## プロジェクト概要

LINE 公開 OpenAPI 仕様（https://github.com/line/line-openapi）から、**Kiota** で .NET/C# クライアントライブラリを生成し、NuGet 配布・保守する。

## 確定している設計方針

- **生成ツール:** Kiota。生成コードは「opaque box（中身は読まない）」前提。レビュー主眼は手書きコードと公開 API の使い勝手。
- **パッケージ分割:** 利用シーン単位。共通基盤 `Line.Core` に、優先構築 `Line.ChannelAccessToken` / `Line.Messaging` / `Line.Messaging.Webhook` / `Line.Liff` を一方向依存。任意メタ `Line.Bot`。insight/manage-audience/module/shop は将来追加。
- **優先利用シーン:** ①メッセージ送受信(Bot) ②LIFF 管理。
- **TFM:** **`net10.0` 単一**（`Nullable=enable`、`LangVersion=latest`）。サポート対象はモダン .NET のみ。**netstandard2.0 / .NET Framework は対象外**（rev.3。理由: LINE 連携はモダン .NET を想定、`#if` シム不要で簡潔化。net8/9 利用側からは参照不可という線引きを了承済み）。

## G1 で確定した重要な実仕様事実（必ず順守）

- **複数 base URL:** `messaging-api.yml` は制御系 `api.line.me` と data 系 `api-data.line.me` が混在。Kiota は 1 クライアント=先頭 server のみ採用。data 系は 5 件（`getMessageContent` 等の blob）で**全て `/v2/bot/` 配下、共通サフィックス `/content` で識別**。→ 制御系(`--exclude-path **/content ...`)と data 系(`--include-path **/content ...`)を 2 クライアント分離生成し、data 側は `RequestAdapter.BaseUrl = https://api-data.line.me` を設定。ファサード `MessagingClient` で統合。
  - **⚠️ 順序が重要（G2 で判明したバグ）:** 生成クライアントはコンストラクタで `baseurl` を `PathParameters` へ確定させる（空なら `api.line.me` を既定採用）。よって `BaseUrl` は必ず**クライアント構築前**に設定すること。構築後に設定しても `PathParameters` に反映されず、リクエストが `api.line.me` に飛ぶ。実装は `Line.Messaging/MessagingClient.cs` 参照、`MessagingHostRoutingTests` で回帰防止。
- **form-urlencoded:** `channel-access-token.yml` のトークン発行は `application/x-www-form-urlencoded`。生成時 `--structured-mime-types` に form-urlencoded を含める。
  - **⚠️ oneOf 合成ボディは form で送れない（G4④で判明）:** `/oauth2/v3/token`（ステートレストークン）の form ボディは discriminator 無し oneOf で、生成コードでは合成ラッパ `TokenRequestBuilder.TokenPostRequestBody`（`IComposedTypeWrapper`）になる。このラッパは内側要求を**入れ子オブジェクト**として直列化するため、そのまま `PostAsync` すると Kiota の Form シリアライザが `"Form serialization does not support nested objects."` で失敗する。→ 手書きヘルパ `Line.ChannelAccessToken/StatelessJwtAssertionTokenSource.cs` が合成ラッパを使わず、平坦な要求モデル（`IssueStatelessChannelTokenByJWTAssertionRequest`）を自前で `RequestInformation` に載せて送る。生成物の protected な `RequestAdapter`/`PathParameters` へは同一クラスの partial（`ChannelAccessTokenClientInternals.cs`、Generated 外・internal 公開）でアクセス。`StatelessJwtAssertionTokenSourceHttpTests` で回帰防止（平坦 form 展開を実証）。v2.1 の非ステートレス発行は合成ボディでないため `JwtAssertionTokenSource` が生成ビルダーをそのまま利用。
- **命名周知 `Action`→`ActionObject`（R2）:** Kiota は `System.Action` 衝突回避で messaging の多態基底型を `ActionObject` に改名する（派生 `MessageAction`/`PostbackAction`/`URIAction` 等は素直）。生成物のためリネーム不可。利用側は基底を `ActionObject`、具体アクションは各派生型で構築する。公開ドキュメントで周知する事項であり公開 API の手書き変更は伴わない。
- **webhook:** モデル＋受信グルー。生成は**モデル専用**（`/callback` を除外するとモデルが生成されないため除外しない。生成される callback メソッドは使わない）。多態は discriminator+mapping 完備（`Message`/`Action`/`Template`/`Flex` 含む 20 型）。
  - **受信ヘルパ `WebhookRequestParser`（`Line.Messaging.Webhook`）:** 署名検証（Core の `WebhookSignatureValidator`）＋本文の `CallbackRequest` 逆直列化を `ParseAsync(body, signature)` に束ねる（署名 NG=`WebhookSignatureException` / 本文 NG=`WebhookPayloadException`、基底 `WebhookException`）。**⚠️ 逆直列化は JSON 自己完結の `KiotaJsonSerializer.DeserializeAsync` を使い、グローバル既定レジストリ（`ApiClientBuilder.RegisterDefaultDeserializer`）に依存しない**（他クライアント未構築でも単独動作・副作用なし）。Kiota 2.0 は同期逆直列化 API を廃止したため `ParseAsync` は非同期のみ。イベント多態復元は生成 discriminator に委譲（ヘルパは `CallbackRequest` を返すのみ、分岐は利用側）。DI は `AddLineWebhook`（HTTP 非依存＝`IHttpClientFactory` 不要）。回帰は `WebhookRequestParserTests`/`WebhookDiIntegrationTests`。
- **blob mime:** `*/*` の生バイナリ（Stream）。multipart ではない。
- **署名検証の定数時間比較:** `net10.0` 単一化により `CryptographicOperations.FixedTimeEquals` を直接使用（`Line.Core/Webhook/WebhookSignatureValidator.cs`）。旧 `#if NETSTANDARD2_0` の手実装分岐は netstandard2.0 対象外化に伴い削除済み。

## レビュー運用（ゲート）

`docs/REVIEW-WORKFLOW.md` 準拠。4 役（仕様/コード/セキュリティ/テスト・アーキ）を**各段階のゲート**とし、サブエージェントで実行、**最終 go/no-go は人**。結果は `docs/reviews/` に日付付きで記録。実装完了時点で必ず先にゲートへ回す（実装→コミット→マージを先行させない）。

- **レビュアーサブエージェント:** `.claude/agents/*.md` の 4 役（`code-reviewer` / `security-reviewer` / `spec-reviewer` / `test-arch-reviewer`）を Agent ツールの `subagent_type` で直接起動できる（インタラクティブでは `@code-reviewer` 等）。

**ゲート進捗:**

- **G0 設計:** PASS（`docs/reviews/2026-07-09-G0-design-review-rev2.md`）
- **G1 仕様:** 実質 PASS（`docs/reviews/2026-07-09-G1-spec-review.md`）。
- **G2 PoC:** 生成→ビルド→テスト実行済（`docs/reviews/2026-07-10-G2-poc-result.md`）。両レビュー = CONCERNS → 高重大度の R1 BaseUrl 順序バグ等を修正・回帰テスト追加済（`docs/reviews/2026-07-10-G2-review.md`）。**人の go/no-go = GO。**
- **G3 手書き実装:** 実装＋ゲートレビュー完了（`docs/reviews/2026-07-10-G3-implementation.md` / `-G3-review.md`。コード=CONCERNS / セキュリティ=PASS）。中位「DI 二重登録の非冪等」と低位「`_refreshAt` アトミック性」を修正・回帰テスト追加。**GO 推奨、人の go/no-go 待ち。**
  - G3 受容項目（シングルトン HttpClient のハンドラローテーション、更新型プロバイダの IDisposable 破棄、refreshMargin≥寿命の下限クランプ）を G4 スコープへ持ち越し。
- **G4 リリース前:** ①〜⑤ すべて実装・レビュー完了・**main マージ済み**。
  - **①実 HTTP モックテスト:** 完了。test-arch = PASS（`docs/reviews/2026-07-10-G4-task1-http-mock-test-review.md`）。**GO（人の go/no-go 済み・main マージ済み）。**
  - **②公開 API 表面 snapshot 回帰テスト:** 完了。test-arch = PASS（`docs/reviews/2026-07-13-G4-task2-public-api-snapshot-review.md`）。`PublicApiGenerator` で手書き表面のみ snapshot 化（Generated 除外）＋完全性ガード。**GO（人の go/no-go 済み・main マージ済み）。**
  - **③Kiota 2.0 移行の是非判断:** 完了 → **移行実施**（ランタイムのみ 1.22.2→2.0.0、CLI は 1.34.1 据え置き）。`docs/R3-kiota-version-policy.md` 改訂。破壊的変更は当方無影響と実証、テスト 38/38・脆弱性監査クリーン。security = PASS（`docs/reviews/2026-07-13-G4-task3-kiota-2.0-migration-review.md`）。**GO（人の go/no-go 済み）。**
  - **④R2 使い勝手:** 完了。`Action`→`ActionObject` はドキュメント周知、`/oauth2/v3/token` は手書きヘルパ `StatelessJwtAssertionTokenSource` を追加（生成の oneOf 合成ボディが form 非対応=入れ子直列化で失敗する落とし穴を回避）。3 役ゲート = コード/セキュリティ PASS・テスト・アーキ CONCERNS 非ブロッキング（指摘反映済み）。テスト 50/50・監査クリーン（`docs/reviews/2026-07-13-G4-task4-r2-usability-review.md`）。**GO（人の go/no-go 済み・main マージ済み）。**
  - **⑤LIFF 利用シーン実装:** 完了。ファサード `LiffClient`（`Api` 低レベル公開＋CRUD 便利メソッド `GetApps/AddApp/UpdateApp/DeleteApp`＋`CreateWithStaticToken`）＋DI `AddLineLiff`（2 オーバーロード・冪等化・`IHttpClientFactory`＋Kiota 既定ハンドラ）。単一ホスト api.line.me（data 系なし=BaseUrl 上書き不要、R1 非該当）、許可ホストは制御系のみに限定。3 役ゲート = コード/セキュリティ/テスト・アーキ すべて PASS（test-arch CONCERNS 非ブロッキング、指摘反映済み）。テスト 76/76・監査クリーン（`docs/reviews/2026-07-13-G4-task5-liff-usage-review.md`）。**GO（人の go/no-go 済み・main マージ済み）。**
- **G5 リリース準備:** リネーム適用（`Line.*`→`Line.OpenApi.*`）＋NuGet パッケージング実装・3 役ゲート完了（code PASS / security PASS / test-arch CONCERNS 非ブロッキング、共通指摘反映済み）。ビルド 0 警告・テスト 92/92＋1/1・pack 5 パッケージ＋snupkg 警告なし・docfx 0 warnings・監査クリーン（`docs/reviews/2026-07-13-G5-rename-and-packaging-review.md`）。**GO 推奨、人の go/no-go 待ち（未コミット）。**

## 次にやること

G4（①〜⑤）は全て main 反映済み。優先利用シーン ①メッセージ送受信（送信＝Messaging / 受信＝Webhook 受信ヘルパ）/ ②LIFF 管理 の手書き表面が揃った。以降の候補（未着手・優先度は要相談）:

- **（実装完了・3役ゲート済・要 push/PR 判断）AI ツール連携パッケージ `Line.OpenApi.Extensions.AI`。** LLM tool-calling（Microsoft.Extensions.AI の `AIFunction`）向けに Messaging 利用シーンをラップ＝アプリ内 in-process で LLM に LINE 操作を公開（既存 MCP サーバ＝別プロセスの補完）。設計 `docs/LINE-dotnet-AI-plugin-design.md`（rev.4）。
  - **依存は `Line.OpenApi.Messaging`＋`Microsoft.Extensions.AI.Abstractions`（10.9.0）の2本ちょうど**。`AIFunctionFactory` は `.Abstractions` 収録（実装/DI パッケージ不要）。版は `Directory.Build.props` の `MicrosoftExtensionsAIVersion` に集中ピン（ADR-6・Kiota ロックステップ外）。
  - **共有ソース方式（`tools/shared/`）:** `MessageJson`＋平坦 DTO を `Line.OpenApi.Tools` と `Extensions.AI` の両 csproj に `<Compile Include Link>` でリンクコンパイル（名前空間 `Line.OpenApi.Tools.Services` 維持）。**NuGet 依存辺を作らない**＝一方向 ADR（published は Core のみ依存）を壊さない。共有 DTO・`MessageInputException`・`MessageService` は **`internal`**（各消費者が JSON 直列化する実装詳細＝公開表面非露出。Tools はアプリ＝挙動不変）。
  - **公開表面:** `LineMessagingAiTools`（`CreateReadOnly`/`Create`）／`LineAiToolOptions`／`LineSendContext`・`LineSendOperation`・`LineSendPolicy`・`LineBeforeSendHook`・`LineSendRefusedException`。⚠️ **安全ゲート（EnableSending/AllowBroadcast/DryRun/SendPolicy/BeforeSend）は生成時クロージャ束縛で AIFunction 引数スキーマに出さない**（LLM バイパス防止・negative assertion テストで固定）。送信は明示 opt-in・既定 read-only、broadcast は独立 opt-in、DryRun/拒否時は transport 非接触。
  - **配置＝`/tools` 支援ティア**（ライブラリ pack-verify から `ExcludeToolFromPack` で除外＝12 パッケージ契約維持）。`verify-packages.ps1` に AI 専用照合（依存2本・lib＋snupkg）追加。**リリース＝別サイクル・タグ `ai-v*`**（`release.yml` に `publish-ai` ジョブ、Trusted Publishing/OIDC）。
  - **ゲート:** 3役 = code PASS / security PASS / test-arch CONCERNS（BLOCKING なし・指摘反映済み。release ジョブのバージョン整合＝`publish-ai` Test の `--no-build` 化を含む）。記録 `docs/reviews/2026-08-19-ai-extensions-implementation-review.md`。テスト AI 26・Tools 83・ライブラリ 264・Isolation 1 全緑・0 警告・pack-verify PASS・監査クリーン。**GO 済み（人の go/no-go）。**
  - **（消化済）サンプル `samples/Line.OpenApi.Samples.Ai`（設計 段階4）:** スクリプト化した `IChatClient`（`ScriptedChatClient`）を実 `FunctionInvokingChatClient` ループで駆動し、安全ゲート（許可リスト `SendPolicy`・`BeforeSend` 承認・read-only 検証）を end-to-end 実演。**オフラインはローカルスタブ transport でゲートを実行**（dry-run はゲート前で短絡するため不採用）。実 LLM/SK への差し替え方法を README 英日に明記。`Microsoft.Extensions.AI`（実装パッケージ）はサンプルのみ参照（公開パッケージは Abstractions のみ）。ビルド 0 警告・全テスト緑。
  - **残（次サイクル）:** SK 固有連携 `Line.OpenApi.SemanticKernel` は初期は作らない。概念記事（docs/manual）英日。⚠️ 同じアセンブリ版 desync パターンが既存 `publish-tool` にも潜在（別途要検討）。
- **（実装完了・ゲート済・main マージ済み）CLI / MCP ツール `Line.OpenApi.Tools` の構築。** ローカル PC 用 `dotnet tool`（コマンド名 `line`）。同一ロジックを CLI（**Cocona 2.2.0**）と MCP サーバ（公式 **ModelContextProtocol 1.4.0 stable**）で両出し。共通サービス層を DI 共有。機能 A. トークン管理 / B. メッセージ送信・Bot 照会 / C. Webhook 開発支援 / D. LIFF 管理 を全実装。**仕様 = `docs/CLI-MCP-tool-spec.md`。**
  - **実装済みサーフェス:** CLI = `config`/`token issue·verify·revoke`/`message push·multicast·broadcast·reply·content`/`bot info·quota·profile`/`webhook verify·replay·listen·get-endpoint·set-endpoint·test-endpoint`/`liff list·add·update·update-url·delete`/`richmenu ...`/`insight ...`/`audience ...`/`shop mission`。MCP = 同機能を `line_<area>_<verb>` で公開（`webhook listen` 除く）、`--read-only`、`token issue` シークレット非露出（C 既定＋`--allow-secret-output`）、`webhook replay` は既定ループバック限定＋`--allow-remote-replay`。
    - **webhook endpoint 設定 / LIFF url 更新（dev トンネル貼り替え自動化）:** `webhook get/set/test-endpoint`（MCP `line_webhook_get_endpoint`·`test_endpoint`=read／`set_endpoint`=write）は署名検証系と別軸で channel access token 認証＝サービス層は `MessageService`（`WebhookService` は資格情報非依存の設計特性を温存するため足さない）。`liff update-url <id> --url`（MCP `line_liff_update_url`=write）は `view.url` のみ部分更新。set/test/update-url の url は絶対 https を要求し送信前に `MessageInputException`(exit2)＝HTTP 不要のテストシーム。liffId は既存 `liff list`/`line_liff_list` で取得＝コマンドだけで貼り替えループ完結。
  - **ゲート:** spec = CONCERNS（`docs/reviews/2026-07-14-cli-mcp-tool-spec-review.md`）／実装 3 役 = code・security・test-arch すべて **CONCERNS・BLOCK なし・main マージ可**、指摘反映済み（`docs/reviews/2026-07-14-cli-mcp-implementation-review.md`）。ビルド 0 警告・CLI テスト 36/36・手動 e2e 済み。**main マージ・push 済み**（`Line.OpenApi.Cli` として追加後、`Line.OpenApi.Tools` へ改名）。
  - **確定事項:** 配置＝`/tools/Line.OpenApi.Tools/`（`/src/` 非汚染）／ NuGet 公開＝別サイクル・独立採番・タグ `tools-v*`／ `webhook listen` トンネルはドキュメント案内のみ・MCP 非公開。実装差分: messages ファイルは `--messages`（`--json` 衝突回避）、CLI トップレベル `push` 別名は未実装。
  - **⚠️ トークン領域は薄ラップ不可（既知）:** `AddLineChannelAccessToken`/ファサード無し→生成 `ChannelAccessTokenClient` 自前配線。JWT 署名器（RS256）は CLI 自作（`Services/JwtAssertionBuilder`）。stateless は `StatelessJwtAssertionTokenSource` 必須。[[cli-token-domain-not-thin-wrap]]
  - **CI/リリース（消化済）:** `build-test` は `LineOpenApi.slnx` 全体対象で CLI ビルド・テストを内包（変更不要）。`pack-verify` は `-p:ExcludeToolFromPack=true` で Tools を除外し 6 パッケージ契約を維持（Tools は別サイクル）。`release.yml` を 2 ジョブ化: `v*`＝ライブラリ（Tools 除外）／`tools-v*`＝Tools のみ publish。除外は Tools csproj の `ExcludeToolFromPack` 条件で `IsPackable=false`。
  - **transport 注入シーム（消化済）:** `TokenService`/`WebhookService` に `HttpMessageHandler` 注入シームを追加し、Token verify（200/400/500）と webhook replay のステータス写像をスタブハンドラで自動テスト化（CLI テスト 36→40）。
  - **MCP メッセージ組立支援（消化済・main マージ済み `a7d920d`）:** 旗艦ユースケース A（Flex 対話試作→dryRun 検証→実機 push で見た目確認）向けに **`line_message_schema`（読み取りツール）** と **send 4 ツールの `dryRun` 引数**を追加。schema は埋込 `messaging-api.yml`（Kiota 生成と同一 spec＝ドリフトなし）から `$ref` 推移閉包を `$defs` 付き JSON Schema で返す（`Services/MessageSchemaService.cs`、SharpYaml 2.1.1、`FlexBox` 自己再帰は visited set 終端）。dryRun は共通サービス層 `MessageService.ValidateMessagesAsync`（送信せず型検証、資格情報解決の前で分岐＝非送信保証）。**バグ修正**: `MessageJson.ParseMessagesAsync` が不正 JSON の生 `JsonReaderException` を漏らす＋非配列/空 JSON が silently 空検証になる穴（Kiota は非配列で例外を出さず非 null 空を返す）を `MessageInputException`(exit 2) に統一。3 役ゲート = code CONCERNS/security PASS/test-arch PASS（BLOCK なし、指摘反映済み）。CLI テスト 40→67、0 警告・pack スモーク PASS・監査クリーン。記録 `docs/reviews/2026-07-15-mcp-message-assembly-review.md`。README 英日・spec §4.6 更新済み。
  - **カバレッジ 3 パッケージ露出（消化済・main マージ済み）:** 新 4 パッケージのうち **Insight / ManageAudience / Shop を CLI/MCP へ露出**（Module は見送り＝パートナー限定・概念難）。CLI `line insight`（統計 7）/`line audience`（list·get·create·add-users·delete＋by-file `upload-file`·`add-file`）/`line shop mission`。MCP = `line_insight_*` 7 本（**全 read-only**）/`line_audience_list·get`（read）＋`create·add_users·delete`（write）/`line_shop_mission`（write）。**by-file 2 本は CLI 専用**（バイナリ＝MCP 非公開、rich menu 画像と同方針）。実装は既存薄ラップ規約踏襲（サービス層メモ化・JSON 解析ガード→exit2・ホスト最小制限・R1 はファサードが解決）。3 役ゲート = code/security/test-arch すべて **PASS**（BLOCK なし、Insight 可用性ガードテスト追加等を反映）。Tools テスト 75/75・0 警告。記録 `docs/reviews/2026-07-15-coverage-tools-review.md`。spec §4.8・README 英日 更新。**⚠️ GA 前:** ManageAudience multipart の `filename` 属性欠落（by-file 受理を要スモーク）。
  - **follow-up（非ブロッキング・残）:** Cocona パースエラーの exit 2 化（既定 1）／CancellationToken 配線（`listen` 以外は `None`）／Windows 明示 ACL（現状は継承 ACL＋正確な警告）／**CLI パリティ: `message schema` サブコマンド・`message ... --dry-run`（検証本体は共通サービス層にあり容易）**／list 射影の HTTP 正常系テスト（transport 注入シーム追加で低コスト化可）／Kiota JSON 解析 try-catch の DRY 共通化／audience 用 MCP スキーマ補助（`line_message_schema` 相当）。

- **（消化済）パッケージ/名前空間リネーム `Line.*` → `Line.OpenApi.*` の適用完了。** プロジェクト名/ディレクトリ・`AssemblyName`/`RootNamespace`（csproj 名から既定継承）・全 `src`/`tests` の `namespace`/`using`・`generate.ps1`＋`generate.sh` の Kiota `-n`/`-o`（**再生成済**）・`LineOpenApi.slnx`・`docfx.json` パス・公開 API snapshot approved（ファイル名＋中身）・README/マニュアル/レビュアー agent md を更新。`filterConfig.yml` の正規表現 `^Line\..*\.Generated` は新名にも一致するため変更不要。テスト 92/92＋Isolation 1/1、docfx 0 warnings、監査クリーン。
- **（消化済）NuGet パッケージング設定完了（G5）。** 共通メタデータは `Directory.Build.props`（`Authors=pierre3`、`RepositoryUrl=https://github.com/pierre3/line-openapi-dotnet`、`PackageLicenseExpression=MIT`、`PackageReadmeFile=README.md`、`Version=0.1.0-preview`、SourceLink/決定的ビルド/snupkg シンボル）。`Description` は各 src csproj。`PackageId` はプロジェクト名から既定継承（=`Line.OpenApi.*`）。ルート `LICENSE`（MIT）追加。`dotnet pack` で 5 パッケージ＋snupkg を警告なく生成、パッケージ間依存（`Line.OpenApi.Core` 等）・README・SourceLink（commit/branch）を nuspec で確認済み。CI/公開ワークフロー `.github/workflows/ci.yml`（build+test+audit）/`release.yml`（tag `v*` で pack→NuGet push、要 `NUGET_API_KEY` secret）を追加。
- **（消化済）G5 ゲートレビュー完了。** 3 役 = code PASS / security PASS / test-arch CONCERNS（全て非ブロッキング）。共通指摘の CI 監査ゲート実効化（restore の NuGetAudit を warnings-as-errors 化）・release.yml のバージョン一致（build にも `-p:Version`）・入力の env 経由化・publish の `environment: nuget` 付与を反映済み。記録 `docs/reviews/2026-07-13-G5-rename-and-packaging-review.md`。**GO 推奨、人の go/no-go 待ち。**
- **（消化済）`Line.OpenApi.Bot`（任意メタパッケージ）追加完了。** コードなし・依存束ねのみで `Line.OpenApi.Messaging` + `Line.OpenApi.Messaging.Webhook` + `Line.OpenApi.ChannelAccessToken` を束ねる（LIFF 非包含・設計 §4.2）。csproj は `IncludeBuildOutput=false`/`IncludeSymbols=false`（空アセンブリ・空 snupkg 回避）/`GenerateDocumentationFile=false`/`NoWarn NU5128`。`LineOpenApi.slnx`・README 更新。3 役ゲート = code/security/test-arch すべて PASS。`dotnet pack` で 6 パッケージ目（lib なし・snupkg なし・net10.0 依存 3 本）を確認。記録 `docs/reviews/2026-07-14-line-bot-meta-package-review.md`。**GO 済み・main マージ済み（`8ad4ad8`）。**
- **（消化済）pack スモークテスト追加完了（旧 follow-up）。** `scripts/verify-packages.ps1`（ローカル/CI 両用・失敗時 exit 1）が `dotnet pack` 後に 6 パッケージのレイアウトを検証: 総数=6／samples・tests 非混入／全 README 同梱／**内部依存グラフ（Line.OpenApi.*）を厳密照合**（code 5 本は `Core` のみ＝一方向依存 ADR 保護、Bot は 3 依存）／code は `lib/net10.0/*.dll`＋snupkg あり・Bot は両方なし。`.github/workflows/ci.yml` に `pack-verify` ジョブ追加。negative test で退行捕捉を実証（`IncludeSymbols` を戻すと exit 1）。外部依存バージョン下限は NuGet 監査ゲートが担当・スコープ外。code/test-arch 再ゲート PASS。**main マージ済み（`8ad4ad8`）。**
- **（消化済）README を標準的な OSS 構成へ整理（`a31501b`）。** Windows 限定記述・PoC 検証メモを削除、バッジ／インストール／必要要件／使い方／ビルド／ドキュメント／構成／ライセンスの標準節構成へ。NuGet 埋め込み README 向けにリポジトリ内リンクを絶対 URL 化。
- **（消化済）実 NuGet.org 初回公開完了（`0.1.0-preview`, `fef0c4a`／タグ `v0.1.0-preview`・`tools-v0.1.0-preview`）。** 全 7 パッケージ（ライブラリ 6＝Core/ChannelAccessToken/Messaging/Messaging.Webhook/Liff/Bot ＋ Tools）を NuGet.org へ公開。ワークフロー実行成功（両 run とも push=`Created`）。**公開方式は Trusted Publishing (OIDC)** へ移行済み（下記）。所有者アカウント `pierre3`。予約プレフィックス `Line.OpenApi.*` 申請は任意で未実施。
- **（消化済）`release.yml` を Trusted Publishing (OIDC) へ移行。** 長寿命 `NUGET_API_KEY` secret を廃止し、両公開ジョブに `id-token: write` を付与、`NuGet/login@v1.2.0`（commit SHA `8d19675…` ピン留め、`user: pierre3`）で短命 API キー（1h）を pack 直後に取得して push。nuget.org 側に Trusted Publishing ポリシー（owner=`pierre3` / repo=`line-openapi-dotnet` / workflow=`release.yml` / env=`nuget`）を登録済み。`nuget` environment 作成済み。Actions の commit SHA ピン留め follow-up はこれで完了。
- **（消化済・要 go/no-go＆マージ）リッチメニュー開発サイクル（ライブラリ `RichMenuClient` + CLI/MCP）。** Rich Menu は messaging-api.yml に含まれ生成済み＝追加したのは使い勝手。ライブラリ `RichMenuClient`（`Line.OpenApi.Messaging`、`MessagingClient` ラップで R1 control/data 分離再利用、CRUD/default/link＋画像ヘルパ `SetImageFromFileAsync`＝拡張子から content-type 推論、`Get*Id` は 404→null 契約を `NullOn404` で担保）。Tools: CLI `line richmenu`（13 サブコマンド・**画像アップロード/DL 含む**）／MCP `line_richmenu_*`（11 ツール・**画像は MCP 非公開＝CLI 誘導**、create は dryRun=online validateRichMenuObject）／schema は `MessageSchemaService` 共用（richmenu root 追加）。3 役ゲート = code CONCERNS/security PASS/test-arch CONCERNS（BLOCK なし・指摘反映済み）。テスト ライブラリ 113・Tools 72 全緑・0 警告・pack PASS・docfx 0 warnings。記録 `docs/reviews/2026-07-15-richmenu-review.md`。README 英日・spec §4.7・概念記事 messaging.md 英日 更新済み。
- **（消化済・main マージ済み）LINE Login v2.1 + OIDC = 新規手書きパッケージ `Line.OpenApi.Login`。** spec 非存在のため全手書き（生成コードなし）・`Line.OpenApi.Core` 依存・`Bot` メタ非包含。ファサード `LoginClient`＝認可 URL 生成（`BuildAuthorizationUrl`＋PKCE/state ヘルパ `LineLoginSecurity`）／トークン `ExchangeCodeAsync`(PKCE)・`RefreshTokenAsync`・`RevokeTokenAsync`・`VerifyAccessTokenAsync`／OIDC `VerifyIdTokenAsync`（**サーバ委譲 `POST /oauth2/v2.1/verify` のみ**）／`GetUserInfoAsync`・`GetProfileAsync`・`GetFriendshipStatusAsync`／`DeauthorizeAsync`（ヘッダ=Messaging channel token・ボディ=user token の疎結合＝`ChannelAccessToken` 非依存）。DI `AddLineLogin`。HTTP は Kiota `Microsoft.Kiota.Bundle.DefaultRequestAdapter`（生成クライアント無し・グローバルレジストリ非依存）＋`adapter.BaseUrl` 設定で `{+baseurl}` 解決（空 BaseUrl 上書きバグを回避・HTTP テストで回帰防止）。エラーは `LoginErrorResponse`(`ApiException` 派生・全 send にマッピング) で `error`/`error_description` を表面化。**⚠️ user access token は channel access token と別系統**＝`Line.Core` に汎用 `StaticBearerTokenProvider`（ホスト制限付き Bearer）＋`LineHosts.AccessLine` を追加。3 役ゲート = security PASS / test-arch PASS / code CONCERNS（BLOCK なし・指摘反映済み）。テスト 155 全緑・0 警告・pack 7 パッケージ PASS・docfx 0 warnings（`docfx.json` に Login 追加＝API リファレンス生成対象）。記録 `docs/reviews/2026-07-15-login-review.md`。README 英日・概念記事 login.md 英日・coverage-roadmap 更新済み。**残: ローカル ID Token 検証（Web=HS256／ネイティブ・LIFF=ES256+JWKS）は次サイクル。deauthorize ボディ形式（JSON 採用）は GA 前に実機確認推奨。**
  - **サンプル Web アプリ `samples/Line.OpenApi.Samples.Login`（消化済・main マージ済み）:** minimal API で認可コードフロー（PKCE）を実機 e2e 実演（`/login`→`/callback`→`/logout`）。`AddLineLogin`（DI）使用・localhost コールバックのみ（トンネル不要）・オフライン既定。3 役ゲート全 PASS（BLOCK なし・指摘反映済み、記録 `docs/reviews/2026-07-15-login-sample-review.md`）。samples README 英日更新。**deauthorize の実機ボディ形式確認はこのサンプルを一度動かせば消化できる。**
- **（消化済・main マージ済み）未取り込み OpenAPI をまとめて取り込み（4 新規パッケージ）。** ラウンド 1（易 3 本・単一ホスト・純生成＋薄ファサード）= `Line.OpenApi.Insight`（insight.yml・`InsightClient`・7 GET）/ `Line.OpenApi.Module`（module.yml のみ・`ModuleClient`・4 ops）/ `Line.OpenApi.Shop`（shop.yml・`ShopClient`・1 op）。ラウンド 2 = `Line.OpenApi.ManageAudience`（manage-audience.yml・`ManageAudienceClient`）＝Messaging と同じ control/data 2 クライアント分離（R1）＋**本リポジトリ初の multipart/form-data ファイルアップロード**（`UploadUserIdsByFileAsync`/`AddUserIdsByFileAsync`＝Kiota `MultipartBody` に file=text/plain パート、`RequestAdapter` を data 系アダプタに設定）。各パッケージ facade+DI（2 オーバーロード・冪等）+Options+テスト（unit/http-mock/DI）+公開 API snapshot。**`module-attach` は見送り**（manager.line.biz+Basic+PKCE・パートナー限定 1 op・コスト対効果最低）。pack **11 パッケージ**（10 code + 1 meta）。全パッケージ Core のみ依存（一方向 ADR 保持）・`Bot` メタ非包含。3 役ゲート = 各ラウンド code/security/test-arch すべて GO（非ブロッキング指摘反映済み）。記録 `docs/reviews/2026-07-15-coverage-round{1,2}-review.md`。README 英日・docfx・概念記事 英日（insight/manage-audience/module/shop）更新。**⚠️ GA 前実機確認:** manage-audience の multipart `file` パートに `filename` 属性が付かない（Kiota 仕様）ため実 LINE 受理を要スモーク。
- **（実装完了・ゲート済み・要 go/no-go＆コミット）新規手書きパッケージ `Line.OpenApi.MiniApp`（LINE MINI App サービスメッセージ＋IAP）。** Login と同型（`Line.OpenApi.Core` のみ依存・`Bot` メタ非包含・spec 非存在で全手書き・単一ホスト `api.line.me`）。ファサード `MiniAppClient`＝**トークンは保持せず呼び出しごとの引数**（`IssueNotificationTokenAsync`/`SendServiceMessageAsync` は channel access token＝stateless/short-lived 限定、`ReserveProductAsync` は user access token、`GetWebhookEventsAsync` は channel access token）。`Line.OpenApi.ChannelAccessToken`・`Line.OpenApi.Login` に非依存（Login の `DeauthorizeAsync` と同じ設計）。エラー型は 2 種に分離: notifier 系（`/message/v3/notifier/*`）は Messaging 標準形状の `NotifierErrorResponse`（`Message`/`Details`）、IAP 系（`/iap/v1/*`）は `IapErrorResponse`（`ErrorCode`＋`Message`/`Details`）。DI `AddLineMiniApp()` は必須設定なし（Login と違い channel id/secret 不要）。3 役ゲート = code/security/test-arch すべて **PASS**（BLOCKING なし、テストカバレッジの重複指摘＝引数ガード節・`cursor`/`status` 省略時・DI の `AllowedHosts` 実挙動検証を反映済み。記録 `docs/reviews/2026-07-16-miniapp-review.md`）。テスト 264/264（ライブラリ、MiniApp 関連 24）・pack 12 パッケージ（11 code + 1 meta）PASS・ビルド 0 警告・docfx 0 warnings。README 英日・概念記事 mini-app.md 英日・coverage-roadmap 更新済み。**GO 推奨、人の go/no-go 待ち（未コミット）。** CLI/MCP 露出（`line miniapp ...`）は今回のスコープ外・後段で相談。**⚠️ 未実機確認:** notifier 系のエラーボディ形状は一次情報に例が無く Messaging 標準形状を類推適用（`docs/coverage-roadmap.md` 参照）。
- **将来パッケージ・カバレッジ / ロードマップ:** `docs/coverage-roadmap.md`（2026-07-16 更新）に集約。要点: line-openapi の spec 9 本中 **8 本取り込み済み**（module-attach のみ見送り）／**Rich Menu は messaging-api.yml に含まれ生成済み**／spec 非存在の候補は LINE MINI App 実装済み・残る主要候補は **LINE Login のローカル ID Token 検証（Web=HS256／ネイティブ・LIFF=ES256+JWKS）**（最有力の次テーマ）。推奨優先順位も同ファイル参照。
- （消化済）Webhook 受信の利用シーンヘルパ = `WebhookRequestParser` を追加（`feat-webhook-receive`。ゲート・go/no-go は当該セッション参照）。
- （消化済）ユーザーマニュアル = DocFX で英語 API リファレンス＋概念記事 英/日 2 系統を `docs/manual/` に構築（`docs-manual-bilingual`）。あわせて**手書きコードの全コメントを英語化**（設計 §13）。公開ホスティング/CI 発行は G5 へ持ち越し。

## 再生成・ビルド・テスト

```
# 生成（Kiota CLI は ~/.dotnet/tools。PowerShell では $env:PATH += ";$env:USERPROFILE\.dotnet\tools"）
pwsh scripts/generate.ps1
# ビルド（net10.0 単一）
dotnet build
# テスト（webhook 多態含め既定で全実行。opt-in フラグ不要）
dotnet test
# pack スモークテスト（6 パッケージのレイアウト・内部依存グラフを検証。CI の pack-verify ジョブと同一）
pwsh scripts/verify-packages.ps1
# ドキュメント生成（DocFX。ローカルツール＝.config/dotnet-tools.json にピン留め）
dotnet tool restore                        # 初回のみ
dotnet docfx docs/manual/docfx.json        # metadata + build → docs/manual/_site/（--serve でプレビュー）
```

- Kiota CLI は `dotnet tool install --global Microsoft.OpenApi.Kiota` で導入。`generate.ps1` が版ピンを照合し、`channel-access-token.yml` の未引用 `urn:...` を**冪等に引用符化**する（再取得時も安全）。
- ビルド/テストは PowerShell 実行が安定。

## 上流仕様追従（spec-sync・実装済み）

上流 `line/line-openapi` は**タグ/リリースを持たず** spec の `info.version` も固定値のため、取り込み世代は**上流コミット SHA** で管理する。設計 §9 に詳細。

- **アンカー = `openapi/upstream-manifest.json`**: 取り込んだ上流コミット `ref`（SHA）・取得日・spec 別 **LF 正規化 sha256** を記録。同梱 `openapi/*.yml` はその ref を正規化した確定スナップショット。現行 ref = `de8bd9e`（2026-07-28）。上流の既定ブランチは **`main`**（`master` は存在しない＝旧 URL は潜在バグ、SHA ピンで解消済み）。
- **⚠️ CRLF 落とし穴**: 手元 CRLF・上流 LF の生バイト比較は全行誤検知（messaging-api だけで約 11,800 行）。ハッシュ/比較の前に必ず LF 正規化する。`.gitattributes` の `openapi/*.yml text eol=lf` と併用。正規化（LF＋フロー配列内 urn 引用符化）は **`scripts/lib/SpecNormalization.ps1` に一元化**し、取り込み（`generate.ps1`）と検知（`check-spec-drift.ps1`）で共有（二重持ち禁止＝乖離すると永久誤検知）。
- **検知**: `pwsh scripts/check-spec-drift.ps1`（manifest 基準・純検知・ドリフト時 exit 1・`-Json`/`-FailOnAwareness` あり）。gh 優先・無ければ REST。
- **再取得＋再生成**: `pwsh scripts/generate.ps1 -Update [-Ref <sha>]`（SHA ピン再取得→正規化→manifest 更新→Kiota 生成）。既定（`-Update` 無し）は同梱 spec を使う再現生成で挙動不変。
- **週次自動化**: `.github/workflows/spec-sync.yml`（cron＋手動）が 検知→`spec-sync` ラベルの Issue upsert（回復時 close）→再生成→**draft PR** 自動作成。**マージは常に人＋4役ゲート**（自動マージしない）。破壊的変更は公開 API snapshot が捕捉、生成コードのみの追加は PR チェックリストで人手確認。
- **module-attach.yml**（見送り）は manifest に `imported:false`＋awareness 用ハッシュのみ記録＝変化は検知するが再生成しない。

## 規約

- 生成コードは `src/**/Generated/`。`kiota-lock.json` はコミットする。
- 全パッケージは `Line.Core` + Kiota ランタイム版にロックステップで追従（`Directory.Build.props` の `KiotaBundleVersion`、現状 **2.0.0**）。CLI（`Microsoft.OpenApi.Kiota`）は **1.34.1** 据え置き（2.x CLI 未リリース）。Kiota は CLI とランタイムを別系統でバージョニングするため、ランタイムのみ 2.0.0（詳細は `docs/R3-kiota-version-policy.md`）。
- **セキュリティ最低版:** `Microsoft.Kiota.Abstractions >= 1.22.0`（CVE-2026-44503 / GHSA-7j59-v9qr-6fq9 = RedirectHandler のクロスホスト時の機密ヘッダ漏洩, CVSS 7.0 High の修正版）。1.16.0 は影響を受けるため使用不可。現行 2.0.0 は修正を継承。RedirectHandler の実体は `Microsoft.Kiota.Http.HttpClientLibrary` にあるが、`Microsoft.Kiota.Bundle` が全サブパッケージを同版にロックステップ固定するため下限は Abstractions の名指しで足りる（Bundle を経由しない直接参照を足す場合のみ Http 側下限も明示）。net10.0 SDK の NuGet 監査（推移的依存含む）で検知される。
- 破壊的変更は公開 API 表面の差分で検知。生成物内部の差分はレビュー対象外。
- **コメントは全て英語**（XML doc `///` ＋インライン `//`、手書きコードのみ。生成物は対象外）。API リファレンスを英語で単一提供するため。設計 §13.2 準拠。プロジェクト文書（`docs/**`・本ファイル・レビュー記録）は日本語のまま。
- **ドキュメント:** DocFX で英語 API リファレンス自動生成＋概念記事は英語/日本語 2 系統（`docs/manual/`）。設計 §13 準拠。
- **公開パッケージ命名（rev.5・確定）:** NuGet `PackageId` と C# ルート名前空間は **`Line.OpenApi.*`**（`Line.OpenApi.Core`/`.ChannelAccessToken`/`.Messaging`/`.Messaging.Webhook`/`.Liff`/`.Bot`）。既公開の旧 SDK `Line.Messaging`（pierre3/kenakamu 所有）との ID・名前空間衝突回避。設計 §8「パッケージ命名」準拠。**適用済み（G5 リリース準備で実施）**: プロジェクト名/名前空間/`PackageId` は全て `Line.OpenApi.*`。`PackageId`・`AssemblyName`・`RootNamespace` はプロジェクト名から既定継承する（csproj で個別指定しない）。
