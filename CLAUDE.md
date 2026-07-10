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
- **webhook:** モデル専用。ただし `/callback` を除外するとモデルが生成されないため**除外しない**（生成される callback メソッドは使わない）。多態は discriminator+mapping 完備（`Message`/`Action`/`Template`/`Flex` 含む 20 型）。
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
- **G4 リリース前:** 後続（下記「次にやること」）。

## 次にやること（G4 リリース前）

1. 短期トークン発行の**実 HTTP モックテスト**（`JwtAssertionTokenSource` の実発行経路。現状は `IChannelAccessTokenSource` シーム経由でロジックのみ検証）。
2. 公開 API 表面の **snapshot 回帰テスト**（設計 §8）。
3. **Kiota 2.0 メジャー移行の是非判断**（独立タスク。現状 1.x 継続。手順は `docs/R3-kiota-version-policy.md`）。
4. R2 使い勝手: `Action`→`ActionObject` 改名・`/oauth2/v3/token` oneOf 合成ボディの手書きヘルパ。
5. LIFF クライアントの利用シーン実装（現状は生成のみ）。

## 再生成・ビルド・テスト

```
# 生成（Kiota CLI は ~/.dotnet/tools。PowerShell では $env:PATH += ";$env:USERPROFILE\.dotnet\tools"）
pwsh poc/scripts/generate.ps1
# ビルド（net10.0 単一）
dotnet build
# テスト（webhook 多態含め既定で全実行。opt-in フラグ不要）
dotnet test
```

- Kiota CLI は `dotnet tool install --global Microsoft.OpenApi.Kiota` で導入。`generate.ps1` が版ピンを照合し、`channel-access-token.yml` の未引用 `urn:...` を**冪等に引用符化**する（master 再取得時も安全）。
- ビルド/テストは PowerShell 実行が安定。

## 規約

- 生成コードは `src/**/Generated/`。`kiota-lock.json` はコミットする。
- 全パッケージは `Line.Core` + Kiota ランタイム版にロックステップで追従（`Directory.Build.props` の `KiotaBundleVersion`、現状 **1.22.2**）。
- **セキュリティ最低版:** `Microsoft.Kiota.Abstractions >= 1.22.0`（CVE-2026-44503 / GHSA-7j59-v9qr-6fq9 = RedirectHandler のクロスホスト時の機密ヘッダ漏洩, CVSS 7.0 High の修正版）。1.16.0 は影響を受けるため使用不可。net10.0 SDK の NuGet 監査（推移的依存含む）で検知される。
- 破壊的変更は公開 API 表面の差分で検知。生成物内部の差分はレビュー対象外。
