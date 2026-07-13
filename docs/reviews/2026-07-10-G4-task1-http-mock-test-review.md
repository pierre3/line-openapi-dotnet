# G4 タスク① 実 HTTP モックテスト ゲート レビュー記録

- 日付: 2026-07-10
- ゲート: **G4 リリース前 / タスク①（トークン発行の実 HTTP 検証）**（担当: テスト・アーキレビュアー。`docs/REVIEW-WORKFLOW.md` §ゲート定義）
- 対象: 新規テスト `poc/tests/Line.Poc.Tests/JwtAssertionTokenSourceHttpTests.cs`（および補完で `JwtAssertionTokenSourceTests.cs` へ 1 観点追加）。被験コードは `Line.ChannelAccessToken/JwtAssertionTokenSource.cs`（生成コードは opaque box 前提で対象外）。
- 方式: テスト・アーキレビュアーをサブエージェントで実行 → 指摘反映 → 再テスト。
- **最終 go/no-go は人（小林さん）**。

## 背景

既存 `JwtAssertionTokenSourceTests` は `IRequestAdapter.SendAsync<T>` を差し替えるフェイクで発行ロジック＋応答検証のみを検証しており、実 `HttpClientRequestAdapter` が担うトランスポート層（`POST /oauth2/v2.1/token` の URL/メソッド組み立て、`application/x-www-form-urlencoded` ボディ直列化、Accept ヘッダ、JSON レスポンス逆直列化）は未検証だった。本タスクは `HttpMessageHandler` をモックし、実アダプタ経由でこの層を通す。

## 初回判定

**CONCERNS**（提出テスト自体は健全でトランスポート層を確実にカバー＝単独では PASS 相当。ただし G4① 完了と記録するにはトランスポート層でしか踏めない失敗モードの欠落あり）。

## 指摘と対応

### High: HTTP エラー応答（非 2xx）経路が未検証 — **修正済み**
- 事象: 生成 `TokenRequestBuilder.PostAsync` は `errorMapping=default` で発行するため、400（`invalid_grant`/鍵不一致）・401・429（レート制限）は `JwtAssertionTokenSource` の `InvalidOperationException` 正規化に到達せず、Kiota の `ApiException` が呼び出し側へ抜ける。運用上最も現実的な失敗モードであり HTTP 経路でしか踏めない。
- 対応: `IssueAsync_ErrorStatus_Surfaces_ApiException`（400/401/429 の Theory）を追加。`ApiException.ResponseStatusCode` が該当ステータスであることをピン止め。

### Medium: 生 JSON の欠損フィールド逆直列化が未検証 — **修正済み**
- 事象: 既存異常系は構築済みモデルを stub が返すため実 JSON 逆直列化を経ていなかった。
- 対応: `IssueAsync_MissingFieldsInRawJson_Throws_InvalidOperation`（`{}` / access_token 欠落 / expires_in 欠落）を追加。実アダプタの逆直列化でフィールドが null に落ち、応答検証が `InvalidOperationException` に正規化されることを実証。

### Medium: 空アサーション分岐が未カバー — **修正済み**
- 事象: `JwtAssertionTokenSource.cs:41-42`（assertionFactory が空文字/null を返したときの例外化）を突くテストが皆無だった。
- 対応: ロジック側 `JwtAssertionTokenSourceTests` に `IssueAsync_Empty_Assertion_Throws_Before_Issuing`（null / "" の Theory）を追加。外部送信前に安全側で止まることを確認。

### Low: CancellationToken 伝播の未検証 — **修正済み**
- 対応: `IssueAsync_CanceledToken_Propagates_OperationCanceled` を追加。`RecordingHandler` 冒頭で `ThrowIfCancellationRequested()` を行い、`HttpClient` 内部実装に依存せず決定的に伝播を確認。

### Low: 単一レスポンスインスタンス再利用の注意 — **対応（コメント/構造で明示）**
- エラー系は「ケースごとに新 handler」を Theory で生成する構造とし、レスポンス content ストリームの二重消費を回避。

### Info: `IssuedToken` が `token_type`/`key_id` を保持しない
- 現状 Bearer 固定・kid 不要のため設計判断として妥当。将来 kid を扱う要件が出た場合は公開 API 表面変更になる点をメモ（テストの穴ではない）。

## 結果

- `dotnet test`: **34/34 合格・警告なし**（本タスク前は 25）。追加 9 ケース（error-status 3 / missing-field 3 / cancellation 1 / empty-assertion 2）。
- トランスポート層（メソッド/URL/form 直列化/Accept/JSON 逆直列化）＋現実的失敗モード（非 2xx・欠損・キャンセル）を実 `HttpClientRequestAdapter` 経由でカバー。

**判定: 指摘対応済みにつき GO 推奨。人の go/no-go 待ち。**
