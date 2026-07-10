# CLAUDE.md — LINE OpenAPI → .NET クライアントライブラリ

このファイルは Claude Code が各セッションで自動読み込みするプロジェクト文脈です。作業前に必ず `docs/LINE-dotnet-client-design.md`（設計方針 rev.2）と `docs/REVIEW-WORKFLOW.md`（レビュー運用）を参照すること。

## プロジェクト概要

LINE 公開 OpenAPI 仕様（https://github.com/line/line-openapi）から、**Kiota** で .NET/C# クライアントライブラリを生成し、NuGet 配布・保守する。

## 確定している設計方針

- **生成ツール:** Kiota。生成コードは「opaque box（中身は読まない）」前提。レビュー主眼は手書きコードと公開 API の使い勝手。
- **パッケージ分割:** 利用シーン単位。共通基盤 `Line.Core` に、優先構築 `Line.ChannelAccessToken` / `Line.Messaging` / `Line.Messaging.Webhook` / `Line.Liff` を一方向依存。任意メタ `Line.Bot`。insight/manage-audience/module/shop は将来追加。
- **優先利用シーン:** ①メッセージ送受信(Bot) ②LIFF 管理。
- **TFM:** **`net10.0` 単一**（`Nullable=enable`、`LangVersion=latest`）。サポート対象はモダン .NET のみ。**netstandard2.0 / .NET Framework は対象外**（rev.3, 2026-07-10。理由: LINE 連携はモダン .NET を想定、`#if` シム不要で簡潔化。net8/9 利用側からは参照不可という線引きを了承済み）。

## G1 で確定した重要な実仕様事実（必ず順守）

- **複数 base URL:** `messaging-api.yml` は制御系 `api.line.me` と data 系 `api-data.line.me` が混在。Kiota は 1 クライアント=先頭 server のみ採用。data 系は 5 件（`getMessageContent` 等の blob）で**全て `/v2/bot/` 配下、共通サフィックス `/content` で識別**。→ 制御系(`--exclude-path **/content ...`)と data 系(`--include-path **/content ...`)を 2 クライアント分離生成し、data 側は `RequestAdapter.BaseUrl = https://api-data.line.me` を設定。ファサード `MessagingClient` で統合。
  - **⚠️ 順序が重要（G2 で判明したバグ）:** 生成クライアントはコンストラクタで `baseurl` を `PathParameters` へ確定させる（空なら `api.line.me` を既定採用）。よって `BaseUrl` は必ず**クライアント構築前**に設定すること。構築後に設定しても `PathParameters` に反映されず、リクエストが `api.line.me` に飛ぶ。実装は `Line.Messaging/MessagingClient.cs` 参照、`MessagingHostRoutingTests` で回帰防止。
- **form-urlencoded:** `channel-access-token.yml` のトークン発行は `application/x-www-form-urlencoded`。生成時 `--structured-mime-types` に form-urlencoded を含める。
- **webhook:** モデル専用。ただし `/callback` を除外するとモデルが生成されないため**除外しない**（生成される callback メソッドは使わない）。多態は discriminator+mapping 完備（`Message`/`Action`/`Template`/`Flex` 含む 20 型）。
- **blob mime:** `*/*` の生バイナリ（Stream）。multipart ではない。
- **署名検証の定数時間比較:** `net10.0` 単一化により `CryptographicOperations.FixedTimeEquals` を直接使用（`Line.Core/Webhook/WebhookSignatureValidator.cs`）。旧 `#if NETSTANDARD2_0` の手実装分岐は netstandard2.0 対象外化に伴い削除済み。

## レビュー運用（ゲート）

`docs/REVIEW-WORKFLOW.md` 準拠。4 役（仕様/コード/セキュリティ/テスト・アーキ）を**各段階のゲート**とし、サブエージェントで実行、**最終 go/no-go は人**。結果は `docs/reviews/` に日付付きで記録。

- **G0 設計:** PASS（`docs/reviews/2026-07-09-G0-design-review-rev2.md`）
- **G1 仕様:** 実質 PASS（`docs/reviews/2026-07-09-G1-spec-review.md`）。残 PoC 確認 `/oauth2/v3/token` の oneOf → G2 で確認済（型付き合成ボディ、G3 で手書きヘルパ候補）。
- **G2 PoC:** 生成→ビルド→テスト実行済（`docs/reviews/2026-07-10-G2-poc-result.md`）。コード＋テスト・アーキ両レビュー = CONCERNS → 高重大度の R1 BaseUrl 順序バグ等を修正・回帰テスト追加済（`docs/reviews/2026-07-10-G2-review.md`）。**条件付き GO を推奨、人の go/no-go 待ち。** ビルド 0 警告、テスト既定 5/5・webhook 込み 6/6。
- G3 手書き実装 / G4 リリース前 が後続。G2 の中重大度指摘（DI・form-urlencoded/ns2.0 実行時/多態テスト拡張・版整合 R3）は G3 スコープ。

## 次にやること

- **人の go/no-go**（G2）: `docs/reviews/2026-07-10-G2-review.md` を確認し、G3 通過可否を判断。
- **G3 手書き実装**（go の場合）: 更新型トークンプロバイダ（短期/JWT、Core への逆依存回避）・DI 統合（`IHttpClientFactory`/共有ハンドラ/`AllowedHosts` 注入）・Webhook 署名の本実装。G2 の中重大度指摘を解消:
  1. form-urlencoded シリアライズのラウンドトリップテスト。
  2. webhook 多態テストを複数/未知/非メッセージイベントへ拡張し、既定/CI で常時実行。
  3. 版整合 R3: 生成 CLI 版のピン止め＋`KiotaBundleVersion` 追従方針（現状 **1.22.2**、`kiota info` 推奨 2.0.0）。2.0.0 メジャーへ上げるか 1.x に留まるかを判断。
  4. 上流 YAML 引用符問題（`channel-access-token.yml`）の生成前正規化 or 上流報告。
  - （※ netstandard2.0 対象外化により「ns2.0 テスト実行」タスクは消滅。TFM は net10.0 単一。）

### 再現用（G2 PoC を再実行する場合）

1. `dotnet tool install --global Microsoft.OpenApi.Kiota`（導入済 CLI 1.34.1）。
2. `poc/` で `pwsh scripts/generate.ps1`（kiota は `~/.dotnet/tools` を PATH に）。
3. `dotnet build`（net8.0 / netstandard2.0）→ `dotnet test`。
4. webhook 多態: `dotnet test -p:DefineConstants=WEBHOOK_DESERIALIZATION_READY`。

## セッション引き継ぎ（2026-07-10 時点の状態）

- **進捗:** G2 PoC を実行完了。生成→ビルド(0 警告)→テスト(既定 5/5、webhook 込み 6/6)が全て通る状態。G2 レビュー(コード＋テスト・アーキ)実施済で高重大度バグ修正済。**人の go/no-go 待ち**（→ G3 or 追加対応）。
- **環境（このマシン）:** .NET SDK 8.0.421 ほか（8/9/10 併存）。Kiota CLI **1.34.1** 導入済（`~/.dotnet/tools`。bash では PATH 追加、PowerShell では `$env:PATH += ";$env:USERPROFILE\.dotnet\tools"`）。ビルド/テストは PowerShell 実行が安定。
- **⚠️ `poc/` はまだ git リポジトリではない**（`git init` 未実施）。規約「`kiota-lock.json` をコミット」を回すには、G3 冒頭で `git init` ＋ `.gitignore`（`bin/`・`obj/` 除外、`Generated/` と `kiota-lock.json` は追跡）整備が必要。`poc/Line.Poc.sln` は作成済。
- **このセッションで変更したファイル:**
  - `poc/src/Line.Messaging/MessagingClient.cs` — BaseUrl 設定を構築前へ移動（R1 バグ修正）。
  - `poc/src/Line.Core/Webhook/WebhookSignatureValidator.cs` — `ArgumentException` の paramName 修正。
  - `poc/tests/Line.Poc.Tests/Line.Poc.Tests.csproj` — `Line.Messaging` 参照追加。
  - `poc/tests/Line.Poc.Tests/MessagingHostRoutingTests.cs` — 新規（R1 ルーティング回帰テスト）。
  - `poc/tests/Line.Poc.Tests/WebhookDeserializationTests.cs` — Kiota 1.x の API へ調整（`KiotaSerializer.DeserializeAsync` ＋ 明示デシリアライザ登録）。
  - `poc/openapi/channel-access-token.yml` — L240 の `urn:...` を引用符化（下記の再発注意）。
  - 生成物一式（`src/**/Generated/`、各 `kiota-lock.json`）を生成。
- **⚠️ spec 修正の再発注意:** `generate.ps1` は spec を master から自動 DL（既存ファイルがあれば再取得しない）。`channel-access-token.yml` を消して再取得すると `urn:ietf:...` 未引用の YAML パースエラーが再発する（SharpYaml がフロー配列内のコロンを解釈できない）。生成前の引用符正規化 or 上流報告を G3 で決める（既存ファイルを残す限りは問題なし）。

## ビルド/テストコマンド

```
# 生成
pwsh poc/scripts/generate.ps1
# ビルド
dotnet build
# テスト
dotnet test
# webhook 多態テスト有効化
dotnet test -p:DefineConstants=WEBHOOK_DESERIALIZATION_READY
```

## 規約

- 生成コードは `src/**/Generated/`。`kiota-lock.json` はコミットする。
- 全パッケージは `Line.Core` + Kiota ランタイム版にロックステップで追従（`Directory.Build.props` の `KiotaBundleVersion`、現状 **1.22.2**）。
- **セキュリティ最低版:** `Microsoft.Kiota.Abstractions >= 1.22.0`（CVE-2026-44503 / GHSA-7j59-v9qr-6fq9 = RedirectHandler のクロスホスト時の機密ヘッダ漏洩, CVSS 7.0 High の修正版）。1.16.0 は影響を受けるため使用不可。net10.0 SDK の NuGet 監査（推移的依存含む）で検知される。
- 破壊的変更は公開 API 表面の差分で検知。生成物内部の差分はレビュー対象外。
