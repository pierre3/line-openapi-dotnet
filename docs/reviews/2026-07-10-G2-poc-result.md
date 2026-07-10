# G2 PoC 実行結果（生成 → ビルド → テスト）

- 日付: 2026-07-10
- 実行環境: Windows 11 / .NET SDK 8.0.421（同 10.x も併存）/ Kiota CLI 1.34.1
- 対象: `poc/`（messaging-api / channel-access-token / webhook / liff）
- 判定用まとめ（G2 ゲート = コード＋テスト・アーキ観点）。**最終 go/no-go は人**。

## サマリ

**PoC チェックリスト 6 項目すべてクリア。ビルド 0 警告 / 0 エラー、テスト 4/4 合格。**
設計方針 rev.2 の核心（複数 base URL の 2 クライアント分離、form-urlencoded 型付け、webhook 多態、netstandard2.0 実サポート）は実挙動で成立を確認した。

## 実行手順と結果

### 1. Kiota 生成

`scripts/generate.ps1` で 5 クライアント生成。全て `Generation completed successfully`。

- 制御系 `MessagingApiClient`（`Generated/Api`）… base url = `https://api.line.me`
- データ系 `MessagingBlobApiClient`（`Generated/Blob`）… base url = `https://api.line.me`（実行時に `api-data.line.me` へ上書き）
- `ChannelAccessTokenClient`（`Generated`）
- `WebhookModels`（`Generated`）… base url = `https://example.com`（ダミー。callback メソッドは不使用、モデルのみ利用）
- `LiffApiClient`（`Generated`）

#### 生成時の警告・エラー（要確認だが良性）

- **`InconsistentTypeFormat` 系 warning 多数**: `format uri`/`format ^[0-9]{8}$` が非対応で string 化。実害なし（URL/日付を string で扱うだけ）。
- **OpenAPI `error` 3 件**（`Recipient` / `DemographicFilter` / `Action` の discriminator）: 「discriminator プロパティが required に含まれない」という**検証エラー表示だが生成は継続**。生成物を確認したところ **discriminator マッピングは完全生成**されている（下記「多態」参照）。→ 実害なし。
- `MultipleServerEntries` は今回のログでは顕在化せず（`--exclude-path`/`--include-path` による 2 分割生成で回避済み）。

#### 仕様スナップショット 1 箇所を修正（記録）

`openapi/channel-access-token.yml` L240 のフロー配列内 `urn:ietf:params:oauth:client-assertion-type:jwt-bearer` が**未引用**で、SharpYaml（Microsoft.OpenApi の YAML リーダ）がプレーンスカラー内のコロンを解釈できずパース失敗した。値を二重引用符で囲んで解消。
→ **要フォローアップ**: 上流 line-openapi の同ファイルが同記法なら `generate.ps1` の自動ダウンロード経路でも再発する。生成前に引用符正規化を挟むか、上流へ報告するかを G3 で判断。

### 2. ビルド（`dotnet build`）

全 5 ライブラリが **`net8.0` と `netstandard2.0` の両方**でビルド成功。**0 警告 / 0 エラー**。
→ `Microsoft.Kiota.Bundle 1.16.0`（`Directory.Build.props` の `KiotaBundleVersion`）で netstandard2.0 実サポートを確認。
（注: `kiota info` は 2.0.0 を推奨表示するが、生成物は 1.16.0 と互換でビルド・テストとも問題なし。版の追従は G3 で方針決定。）

### 3. テスト（`dotnet test`）

- 既定（`CoreTests`）: **3/3 合格**（署名検証の正/改竄、AllowedHosts 負側）。
- webhook 多態有効化（`-p:DefineConstants=WEBHOOK_DESERIALIZATION_READY`）: **合計 4/4 合格**。

`WebhookDeserializationTests.cs` を 1.16.0 の API に合わせて調整:
- `KiotaJsonSerializer`（当版に存在しない）→ `KiotaSerializer.DeserializeAsync<T>("application/json", ...)` に変更。
- 生成クライアントを構築しないため、静的デシリアライザのレジストリへ `ApiClientBuilder.RegisterDefaultDeserializer<JsonParseNodeFactory>()` を明示登録。

## PoC チェックリスト検証

| # | 項目 | 結果 |
|---|---|---|
| 1 | Kiota 検証警告 | `InconsistentTypeFormat` のみ（良性）。`MissingDiscriminator` は出ず、discriminator は全マッピング生成。 |
| 2 | ホスト分離 | ✅（**当初バグ→修正済**）。`Generated/Api` に送信系、`Generated/Blob` に `content` 系のみ。**当初の `MessagingClient` は Blob クライアント構築後に BaseUrl を上書きしており、生成コンストラクタが構築時に `baseurl` を `PathParameters` へ確定するため上書きが無効で、コンテンツ取得が `api.line.me`（誤）へ飛んでいた**（G2 レビューで両レビュアーが検出）。BaseUrl 設定を構築前へ移動して修正し、実リクエスト URL を検査する回帰テスト（`MessagingHostRoutingTests`）で `api-data.line.me` へのルーティングを実挙動確認済み。 |
| 3 | form-urlencoded | ✅ `oauth2/v3/token` は型付き `TokenPostRequestBody` を受け取り、`Task<IssueStatelessChannelAccessTokenResponse>` を返す（stream 退化なし）。 |
| 4 | webhook 多態 | ✅ `CallbackRequest` / `MessageEvent` / `TextMessageContent` ほか 66 型生成。discriminator 復元テスト合格。`Event` は 20+ 種を完全マッピング。 |
| 5 | netstandard2.0 ビルド | ✅ 全ライブラリ両 TFM 成功。署名検証の `#if NETSTANDARD2_0` 定数時間比較分岐もコンパイル通過。 |
| 6 | 使い勝手（R2） | 概ね良好（下記）。 |

## 使い勝手（R2）と命名の所見

- **送信**: `client.Api.V2.Bot.Message.Push.PostAsync(PushMessageRequest)` → `PushMessageResponse`。素直。
- **コンテンツ取得**: `client.Blob.V2.Bot.Message[messageId].Content.GetAsync()` → `Task<Stream?>`。素直。
- **`Action` → `ActionObject` に改名**: Kiota が `System.Action` 衝突回避のため base 多態型を `ActionObject` に改名。派生（`MessageAction`/`PostbackAction`/`URIAction` 等）は素直。公開ドキュメントで要周知。
- **`/oauth2/v3/token` の oneOf（G1 残論点）**: `TokenPostRequestBody` が `IssueStatelessChannelTokenByClientSecretRequest` と `IssueStatelessChannelTokenByJWTAssertionRequest` の**2 つの nullable プロパティを持つ合成ラッパ**として生成される。型付きで利用可能だが「どちらか一方だけ設定」を利用者に委ねる形でやや不親切。→ **G3 で手書きヘルパ**（用途別メソッド）を用意する候補。

## 次アクション

1. 本結果を入力に **G2 サブエージェントレビュー（コード / テスト・アーキ観点）** を実施。
2. レビュー指摘を集約し、人が go/no-go を判断。
3. go なら G3（手書き実装: 認証プロバイダ更新型・DI・Webhook 署名の本実装）へ。
