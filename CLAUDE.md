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

- **（消化済）パッケージ/名前空間リネーム `Line.*` → `Line.OpenApi.*` の適用完了。** プロジェクト名/ディレクトリ・`AssemblyName`/`RootNamespace`（csproj 名から既定継承）・全 `src`/`tests` の `namespace`/`using`・`generate.ps1`＋`generate.sh` の Kiota `-n`/`-o`（**再生成済**）・`LineOpenApi.slnx`・`docfx.json` パス・公開 API snapshot approved（ファイル名＋中身）・README/マニュアル/レビュアー agent md を更新。`filterConfig.yml` の正規表現 `^Line\..*\.Generated` は新名にも一致するため変更不要。テスト 92/92＋Isolation 1/1、docfx 0 warnings、監査クリーン。
- **（消化済）NuGet パッケージング設定完了（G5）。** 共通メタデータは `Directory.Build.props`（`Authors=pierre3`、`RepositoryUrl=https://github.com/pierre3/line-openapi-dotnet`、`PackageLicenseExpression=MIT`、`PackageReadmeFile=README.md`、`Version=0.1.0-preview`、SourceLink/決定的ビルド/snupkg シンボル）。`Description` は各 src csproj。`PackageId` はプロジェクト名から既定継承（=`Line.OpenApi.*`）。ルート `LICENSE`（MIT）追加。`dotnet pack` で 5 パッケージ＋snupkg を警告なく生成、パッケージ間依存（`Line.OpenApi.Core` 等）・README・SourceLink（commit/branch）を nuspec で確認済み。CI/公開ワークフロー `.github/workflows/ci.yml`（build+test+audit）/`release.yml`（tag `v*` で pack→NuGet push、要 `NUGET_API_KEY` secret）を追加。
- **（消化済）G5 ゲートレビュー完了。** 3 役 = code PASS / security PASS / test-arch CONCERNS（全て非ブロッキング）。共通指摘の CI 監査ゲート実効化（restore の NuGetAudit を warnings-as-errors 化）・release.yml のバージョン一致（build にも `-p:Version`）・入力の env 経由化・publish の `environment: nuget` 付与を反映済み。記録 `docs/reviews/2026-07-13-G5-rename-and-packaging-review.md`。**GO 推奨、人の go/no-go 待ち。**
- **未実施:** 実際の NuGet.org 公開（`NUGET_API_KEY` 投入・タグ `v*` push）。follow-up: Actions の commit SHA ピン留め（正式版前）、pack スモークテスト（任意）。
- **`Line.Bot`（任意メタパッケージ）:** 未作成。
- **将来パッケージ:** insight / manage-audience / module / shop（仕様未取得）。
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
# ドキュメント生成（DocFX。ローカルツール＝.config/dotnet-tools.json にピン留め）
dotnet tool restore                        # 初回のみ
dotnet docfx docs/manual/docfx.json        # metadata + build → docs/manual/_site/（--serve でプレビュー）
```

- Kiota CLI は `dotnet tool install --global Microsoft.OpenApi.Kiota` で導入。`generate.ps1` が版ピンを照合し、`channel-access-token.yml` の未引用 `urn:...` を**冪等に引用符化**する（master 再取得時も安全）。
- ビルド/テストは PowerShell 実行が安定。

## 規約

- 生成コードは `src/**/Generated/`。`kiota-lock.json` はコミットする。
- 全パッケージは `Line.Core` + Kiota ランタイム版にロックステップで追従（`Directory.Build.props` の `KiotaBundleVersion`、現状 **2.0.0**）。CLI（`Microsoft.OpenApi.Kiota`）は **1.34.1** 据え置き（2.x CLI 未リリース）。Kiota は CLI とランタイムを別系統でバージョニングするため、ランタイムのみ 2.0.0（詳細は `docs/R3-kiota-version-policy.md`）。
- **セキュリティ最低版:** `Microsoft.Kiota.Abstractions >= 1.22.0`（CVE-2026-44503 / GHSA-7j59-v9qr-6fq9 = RedirectHandler のクロスホスト時の機密ヘッダ漏洩, CVSS 7.0 High の修正版）。1.16.0 は影響を受けるため使用不可。現行 2.0.0 は修正を継承。RedirectHandler の実体は `Microsoft.Kiota.Http.HttpClientLibrary` にあるが、`Microsoft.Kiota.Bundle` が全サブパッケージを同版にロックステップ固定するため下限は Abstractions の名指しで足りる（Bundle を経由しない直接参照を足す場合のみ Http 側下限も明示）。net10.0 SDK の NuGet 監査（推移的依存含む）で検知される。
- 破壊的変更は公開 API 表面の差分で検知。生成物内部の差分はレビュー対象外。
- **コメントは全て英語**（XML doc `///` ＋インライン `//`、手書きコードのみ。生成物は対象外）。API リファレンスを英語で単一提供するため。設計 §13.2 準拠。プロジェクト文書（`docs/**`・本ファイル・レビュー記録）は日本語のまま。
- **ドキュメント:** DocFX で英語 API リファレンス自動生成＋概念記事は英語/日本語 2 系統（`docs/manual/`）。設計 §13 準拠。
- **公開パッケージ命名（rev.5・確定）:** NuGet `PackageId` と C# ルート名前空間は **`Line.OpenApi.*`**（`Line.OpenApi.Core`/`.ChannelAccessToken`/`.Messaging`/`.Messaging.Webhook`/`.Liff`/`.Bot`）。既公開の旧 SDK `Line.Messaging`（pierre3/kenakamu 所有）との ID・名前空間衝突回避。設計 §8「パッケージ命名」準拠。**適用済み（G5 リリース準備で実施）**: プロジェクト名/名前空間/`PackageId` は全て `Line.OpenApi.*`。`PackageId`・`AssemblyName`・`RootNamespace` はプロジェクト名から既定継承する（csproj で個別指定しない）。
