# G4 タスク④ R2 使い勝手（ActionObject 周知 / v3 トークンヘルパ）ゲート レビュー記録

- 日付: 2026-07-13
- ゲート: **G4 リリース前 / タスク④（R2 使い勝手）**（担当: コード / セキュリティ / テスト・アーキの 3 役。`docs/REVIEW-WORKFLOW.md` §ゲート定義）
- 対象（ブランチ `g4-task4-r2-usability`、`main` 8f2b60c からの差分）:
  - **新規** `poc/src/Line.ChannelAccessToken/StatelessJwtAssertionTokenSource.cs` — `/oauth2/v3/token`（ステートレストークン, JWT アサーション）発行の手書きヘルパ。`IChannelAccessTokenSource` 実装。
  - **新規** `poc/src/Line.ChannelAccessToken/ChannelAccessTokenClientInternals.cs` — 生成クライアントの protected `RequestAdapter`/`PathParameters` を同一クラス partial で assembly 内部に橋渡し（Generated 外・internal）。
  - **新規** `poc/tests/Line.Poc.Tests/StatelessJwtAssertionTokenSourceHttpTests.cs` — 実 HTTP モックテスト（12 ケース）。
  - **更新** `poc/tests/Line.Poc.Tests/PublicApi/Line.ChannelAccessToken.approved.txt` — 新公開型 1 件の反映（最小差分）。
  - **更新** `CLAUDE.md`（実仕様: oneOf/form の落とし穴・ActionObject 命名周知）、`docs/LINE-dotnet-client-design.md`（R2/R7 行を対処済みへ）、`IChannelAccessTokenSource.cs`（doc 併記）。
- 方式: 実装 → build/test 緑 → 3 役をサブエージェントで並列実行 → 指摘反映。
- **最終 go/no-go は人（小林さん）= 待ち。**

## スコープの決定（ユーザー選択）

- **R2①-a `Action`→`ActionObject`:** ドキュメント周知のみ（公開 API の手書き変更なし）。Kiota が `System.Action` 衝突回避で多態基底型を改名。生成物はリネーム不可のため、周知が正しい対処。
- **R2①-b `/oauth2/v3/token` 手書きヘルパ:** JWT アサーションのみ対応（クライアントシークレット枝は将来）。

## 実装中に判明した核心（R2 の「軽微」評価の修正）

G1/G2 では `/oauth2/v3/token` の oneOf ボディを「軽微」としていたが、**生成物そのままでは form 送信できない**ことが実装で判明：

- 生成の合成ラッパ `TokenRequestBuilder.TokenPostRequestBody`（`IComposedTypeWrapper`）は内側要求を**入れ子オブジェクト**として直列化する。
- Kiota の Form シリアライザは入れ子非対応で、`PostAsync`/`ToPostRequestInformation` が `"Form serialization does not support nested objects."`（`InvalidOperationException`）を投げる。
- → ヘルパは合成ラッパを使わず、平坦な要求モデル `IssueStatelessChannelTokenByJWTAssertionRequest` を自前 `RequestInformation` に載せて送出。落とし穴の RATIONALE は特性化テスト `GeneratedComposedBody_Cannot_Be_Form_Serialized_ByDesign` で固定（将来 Kiota が修正したら落ちて回避策撤去を検討できる）。

## ゲート結果

| 役 | 判定 | 主な指摘 |
|---|---|---|
| コード | **PASS** | 合成ボディ回避の根拠・URL/ヘッダ/エラー面の既存 v2.1 との対称性・partial glue の internal 封じ込め（公開表面非露出）を実証。指摘は低〜情報のみ。 |
| セキュリティ | **PASS** | トークン/アサーションの漏洩なし（ログ・例外メッセージ・不要保持いずれも無）。baseurl は既定 `api.line.me` 固定で誤ホストの穴なし。CVE-2026-44503 系リダイレクト漏洩の退行なし（同一ハンドラパイプライン、そもそも Authorization ヘッダ無）。internal 露出でアタックサーフェス増なし。低位: 生 Dictionary 露出の防御。 |
| テスト・アーキ | **CONCERNS（非ブロッキング）** | 懸念1（中低）: `expires_in<=0` 分岐が v2.1 と非対称に未カバー。懸念2/3（低・任意）: ヘルパ重複、特性化テストの追加余地。R2 核心の回帰固定・実ネット非依存・snapshot 最小差分は良好。 |

## 反映した指摘

- **test-arch 懸念1:** `IssueAsync_MissingFieldsInRawJson_Throws_InvalidOperation` に `expires_in:0` / `-1` の `InlineData` を追加（v2.1 とカバレッジ対称化）。
- **test-arch 懸念3（任意→採用）:** 合成ラッパが form で `"nested objects"` を投げることを主張する特性化テストを追加。
- **security 低 / code 情報:** `InternalPathParameters` を生 `Dictionary` 参照から**防御的コピー**返却へ変更（assembly 内の baseurl 誤変更を防ぐ）。
- **code 低:** `IChannelAccessTokenSource` の XML doc に `StatelessJwtAssertionTokenSource` を併記。

見送り（両レビュアーが許容・過剰抽象化回避）: URL テンプレート二重管理の解消、応答検証ロジックの DRY 集約（トークンソース 2 件では時期尚早）。

## 検証（as-of 2026-07-13）

- ビルド **0 警告 / 0 エラー**。
- テスト **50/50 合格**（新規 12 ケース含む）。
- 脆弱性監査（推移的依存含む）クリーン。
- 公開 API snapshot: 新公開型 `StatelessJwtAssertionTokenSource` のみの最小差分。internal グルーは二重フィルタ（internal＋Generated 名前空間）で非露出、完全性ガード通過。

## 総括

3 役すべて BLOCK なし（コード/セキュリティ=PASS、テスト・アーキ=CONCERNS 非ブロッキング→指摘反映済み）。**GO 推奨、人の go/no-go 待ち。**
