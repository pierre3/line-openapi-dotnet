# Webhook 受信ヘルパ ゲート レビュー記録

- 日付: 2026-07-13
- ゲート: **手書き実装ゲート（G3 相当の 3 役: コード / セキュリティ / テスト・アーキ）**。`docs/REVIEW-WORKFLOW.md` §ゲート定義。
- 対象（ブランチ `feat-webhook-receive`、`main` c67b983 → d19cca7 からの差分）:
  - **新規** `poc/src/Line.Messaging.Webhook/WebhookRequestParser.cs` — 署名検証（Core）＋本文の `CallbackRequest` 逆直列化を `ParseAsync` に束ねる受信ヘルパ。インスタンス／静的（マルチテナント）両オーバーロード。
  - **新規** `poc/src/Line.Messaging.Webhook/WebhookException.cs` — 基底 `WebhookException` ＋ `WebhookSignatureException` / `WebhookPayloadException`。
  - **新規** `poc/src/Line.Messaging.Webhook/DependencyInjection/`（`ServiceCollectionExtensions.AddLineWebhook` / `LineWebhookOptions`）。HTTP 非依存＝`IHttpClientFactory` なし、`ValidateOnStart()` 付き。
  - **更新** `Line.Messaging.Webhook.csproj`（`Microsoft.Extensions.Options` / `DependencyInjection.Abstractions` / `Kiota.Serialization.Json` 追加）。
  - **新規テスト** `WebhookRequestParserTests`（署名×パースの組合せ）/ `WebhookDiIntegrationTests`（DI・last-wins）/ **独立アセンブリ** `Line.Messaging.Webhook.IsolationTests`（レジストリ非依存の回帰）。公開 API snapshot `Line.Messaging.Webhook.approved.txt` 新規＋登録。
  - **更新** `poc/README.md`（利用チュートリアル刷新）、`docs/LINE-dotnet-client-design.md`（§4.3 受信グルー・§10 独立アセンブリ方針）、`CLAUDE.md`（実仕様追補）。
- 方式: 実装 → build/test 緑 → 3 役をサブエージェントで並列実行 → **コード=FAIL(HIGH)** を修正・再検証 → 3 役 PASS。
- **最終 go/no-go は人（小林さん）= 待ち。**

## 初回ゲートで検出した HIGH（ブロッカー）と是正

**code-reviewer = FAIL（HIGH、実プロセスで再現）:**
- 当初実装は逆直列化に `KiotaJsonSerializer.DeserializeAsync` を用い「グローバル既定レジストリ非依存」と主張していたが、**実際は逆**。`KiotaJsonSerializer` は内部で `ParseNodeFactoryRegistry.DefaultInstance`（空初期化・自動登録なし）に委譲するため、生成クライアントを構築しないクリーンなプロセス（＝本パッケージの想定利用シーン）では正当な payload が `InvalidOperationException("Content type application/json does not have a factory registered")` → `WebhookPayloadException` になる。
- 初回 test 92/92 通過は**偽陽性**（同一プロセス内の他テスト `WebhookDeserializationTests` 静的 ctor がプロセス共有レジストリを汚染していたため。テスト順序依存）。
- **是正:** `new JsonParseNodeFactory().GetRootParseNodeAsync(...)` → `rootNode.GetObjectValue(CallbackRequest.CreateFromDiscriminatorValue)` へ変更（ファクトリ直使用で真にレジストリ非依存）。`csproj` に `Microsoft.Kiota.Serialization.Json`（`$(KiotaBundleVersion)`）明示参照。
- **回帰の恒久化:** 独立テストアセンブリ `Line.Messaging.Webhook.IsolationTests`（`Line.Messaging.Webhook` のみ参照＝クリーンなレジストリ）を新設。**修正前は FAIL 再現、修正後 PASS**（message/follow/postback/未知の多態復元まで確認）を実証。設計 §10 に「自己完結性は参照最小の独立アセンブリで保証」を方針として明文化。

## ゲート結果（再検証後）

| 役 | 判定 | 備考 |
|---|---|---|
| コード | 初回 **FAIL(HIGH)** → 再検証 **PASS** | HIGH 是正をクリーンなプロセスで実証。署名の生バイト同一性・例外階層・キャンセル非ラップ・引数検証は退行なし。 |
| セキュリティ | **PASS** | 定数時間比較・検証バイパス不能・TOCTOU なし・シークレット非漏洩。低（`ValidateOnStart()`／ctor と DI の判定統一／本文サイズ上限は上流責務）を反映。 |
| テスト・アーキ | 初回 **CONCERNS** → 再確認 **PASS** | 中（DI last-wins の齟齬／パーサ実パスの多態カバレッジ）・低（自己完結コメント／null 分岐／OCE guard／XML doc）をすべて反映。配置（Webhook パッケージ）・HTTP 非依存 DI は妥当と評価。 |

## 反映した指摘（サマリ）

- **HIGH:** レジストリ非依存化（`JsonParseNodeFactory` 直使用）＋独立アセンブリ回帰。
- **DI last-wins:** コメントを実態（パーサ登録は初回優先、Options は Configure 累積で last-wins）に是正。`AddLineWebhook_MultipleRegistrations_NotDuplicated_And_LastSecretWins` で「S2 署名成功・S1 失敗」を実アサート。`ValidateOnStart()` で設定漏れを起動時に落とす。
- **判定統一:** ctor を `IsNullOrWhiteSpace` に（DI `Validate` と一致、空白のみも拒否）。
- **多態カバレッジ:** 独立テストが message/follow/postback/未知をパーサ実パスで確認。
- **ドキュメント:** 自己完結コメント正確化、防御ガード／キャンセル伝播のコメント、XML doc に `ArgumentNullException`/`ArgumentException` 追記、README に本文サイズ上限注記。

## 検証結果（as-of 2026-07-13）

- ビルド: **0 警告 / 0 エラー**。
- テスト: **`Line.Poc.Tests` 92/92**＋**`Line.Messaging.Webhook.IsolationTests` 1/1**（クリーンなプロセス）合格。
- 脆弱性監査: クリーン（Kiota 全 2.0.0）。

## 総括

3 役すべて PASS（コードは HIGH 是正後に再検証で PASS）。優先利用シーン①「メッセージ送受信」の受信側が `WebhookRequestParser` で完成。**GO 推奨、人の go/no-go 待ち。**

### 学び（恒久メモは CLAUDE.md へ反映済み）

「テスト緑」でも**設計上の主張（グローバル非依存）が誤っていれば FAIL になり得る**。プロセス共有のグローバル状態（Kiota シリアライザレジストリ）に絡む自己完結性は、参照最小の独立アセンブリでしか証明できない。ゲート（人的レビュー相当のサブエージェント）が機能したケース。
