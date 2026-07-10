# G3 手書き実装 記録

- 日付: 2026-07-10
- ゲート: **G3 手書き実装**
- 前提: G2 = 人の go/no-go で **GO**（条件付き GO 推奨を受理）。残った中重大度指摘を本セッションで解消。
- 状態: ビルド **0 警告 / 0 エラー**、テスト **18/18 合格**（webhook 多態を既定実行化）、NuGet 監査で脆弱性なし。

## 実装した 4 workstream

### 1. 更新型トークンプロバイダ（`Line.ChannelAccessToken`）

- `IChannelAccessTokenSource` / `IssuedToken`: 発行操作のテスト可能シーム。
- `RefreshingChannelAccessTokenProvider`: 短期トークンをキャッシュし、期限マージン（既定 5 分）手前で再発行する `IAccessTokenProvider`。
  - **並行更新の二重発行防止:** `SemaphoreSlim` + double-check。高速パスはロック無しの `Volatile.Read`。
  - **許可ホスト注入:** 未指定時 `LineHosts.Default`。許可外は空文字を返し発行もしない。
  - **時計注入:** テスト用に `Func<DateTimeOffset>` 差し替え可能。
- `JwtAssertionTokenSource`: 生成 `ChannelAccessTokenClient` を消費し `/oauth2/v2.1/token`（JWT アサーション）で発行。JWT 署名はアプリ固有のため `assertionFactory` で外部供給（本ライブラリは署名鍵を扱わない）。
- **依存方向:** すべて `Line.ChannelAccessToken`（→ `Line.Core` 一方向）。Core への逆依存なし（設計 §7 準拠）。

### 2. DI 統合（`Line.Messaging`）— M-3 解消

- `MessagingClient(IAuthenticationProvider, HttpClient?)` オーバーロード追加。2 アダプタで **共有 HttpClient** を使用（アダプタが URL を組み立てるため `BaseAddress` 未使用で共有可）。
- `ServiceCollectionExtensions.AddLineMessaging`:
  - `IHttpClientFactory` の名前付きクライアント（`"Line.Messaging"`）を登録。
  - Kiota 既定ハンドラ（**CVE 修正版 RedirectHandler 含む**）を `KiotaClientFactory.GetDefaultHandlerActivatableTypes()` + `ActivatorUtilities.CreateInstance` で DI ネイティブに差し込み（1.22.2 に `AttachKiotaHandlers` は無い）。
  - `AllowedHosts` を `LineMessagingOptions` から注入。
  - 静的トークン版と、任意 `IAuthenticationProvider` 注入版（更新型プロバイダ用、逆依存回避）の 2 経路。
  - `Options.Validate` で `ChannelAccessToken` 必須を検証。
- 追加パッケージ: `Microsoft.Extensions.Http` / `Microsoft.Extensions.Options`（10.0.0）。

### 3. テスト拡張（+13 件、計 18）

- **form-urlencoded ラウンドトリップ（§2-B）:** `Content-Type = application/x-www-form-urlencoded` と本体キー（`grant_type` / `client_assertion_type`(`:` エンコード) / `client_assertion`）、JSON でないことを検証。
- **webhook 多態（§2-D）:** `#if WEBHOOK_DESERIALIZATION_READY` ガードを撤去し **既定/CI で常時実行**。単一 message、複数混在（message/follow/postback）、**未知イベントの基底 `Event` フォールバック**を検証。
- **更新型プロバイダ:** キャッシュ、期限後再発行、**32 並行での単一発行**、許可外ホストで空・非発行。
- **DI 統合:** 解決・2 系ルーティング維持（api / api-data）・`IHttpClientFactory` 登録・任意認証注入・Options 検証失敗。

### 4. 版整合 R3

- **決定: 1.x 継続**（1.22.2 は CVE 修正版、2.0 メジャー移行は独立タスク）。詳細 `docs/R3-kiota-version-policy.md`。
- `generate.ps1`: 生成 CLI 版を `$ExpectedKiotaCliVersion = 1.34.1` にピン、`kiota --version` と照合し不一致は既定エラー（`-AllowKiotaVersionMismatch` で回避可）。
- `generate.ps1`: `channel-access-token.yml` の未引用 `urn:...` を**冪等に引用符化**する正規化を追加（master 再取得時の再発防止）。

## G2 中重大度指摘の対応状況

| 指摘 | 状態 |
|---|---|
| M-3 DI（IHttpClientFactory/共有ハンドラ/AllowedHosts 注入） | **解消**（workstream 2） |
| §2-B form-urlencoded ラウンドトリップ | **解消**（workstream 3） |
| §2-C ns2.0 定数時間比較テスト | **消滅**（net10.0 単一化, rev.3） |
| §2-D webhook 多態テスト拡張＋常時実行 | **解消**（workstream 3） |
| R3 版整合 | **解消**（workstream 4） |
| L-2 更新型/引数検証テスト | **解消**（更新型プロバイダのテスト整備） |

## 残課題（G4 以降）

- 短期トークン発行の**実 HTTP モックテスト**（`JwtAssertionTokenSource` の実発行経路）。現状は `IChannelAccessTokenSource` シーム経由でロジックのみ検証。
- 2.0 メジャー移行の是非判断（独立タスク）。
- 公開 API 表面の snapshot 回帰テスト（設計 §8）。
- `Action`→`ActionObject` 改名・`/oauth2/v3/token` oneOf 合成ボディの手書きヘルパ（R2 使い勝手）。
- LIFF クライアントの利用シーン実装（現状は生成のみ）。
