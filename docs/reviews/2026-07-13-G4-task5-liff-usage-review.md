# G4 タスク⑤ LIFF クライアントの利用シーン実装 ゲート レビュー記録

- 日付: 2026-07-13
- ゲート: **G4 リリース前 / タスク⑤（LIFF 利用シーン実装）**（担当: コード / セキュリティ / テスト・アーキの 3 役。`docs/REVIEW-WORKFLOW.md` §ゲート定義）
- 対象（ブランチ `g4-task5-liff-usage`、`main` 37e6f53 からの差分）:
  - **新規** `poc/src/Line.Liff/LiffClient.cs` — LIFF 管理 API のファサード。生成 `LiffApiClient` を内包し `Api` で低レベル公開＋便利メソッド `GetAppsAsync`/`AddAppAsync`/`UpdateAppAsync`/`DeleteAppAsync`＋`CreateWithStaticToken`。
  - **新規** `poc/src/Line.Liff/DependencyInjection/ServiceCollectionExtensions.cs` — `AddLineLiff`（静的トークン / 任意認証プロバイダの 2 オーバーロード）。冪等化・`IHttpClientFactory` 名前付きクライアント＋Kiota 既定ハンドラ（CVE 修正版 RedirectHandler 含む）。
  - **新規** `poc/src/Line.Liff/DependencyInjection/LineLiffOptions.cs` — 静的トークン設定。`AllowedHosts` 既定は制御系 `api.line.me` のみ（data 系不要）。
  - **更新** `poc/src/Line.Liff/Line.Liff.csproj` — DI パッケージ（`Microsoft.Extensions.Http`/`Options` 10.0.0）追加。
  - **新規テスト** `LiffClientTests.cs`（経路 4＋引数ガード 6）/ `LiffClientHttpTests.cs`（実 HTTP モック 9：CRUD トランスポート・JSON ラウンドトリップ・空ボディ破棄・エラー→ApiException・キャンセル・Bearer 付与・許可外ホストの token withhold）/ `LiffDiIntegrationTests.cs`（DI 5 観点）。
  - **更新** `poc/tests/Line.Poc.Tests/PublicApi/Line.Liff.approved.txt`（新規 snapshot）＋`PublicApiSnapshotTests.cs` へ Line.Liff 登録、`Line.Poc.Tests.csproj` に Line.Liff 参照追加。
- 方式: 実装 → build 0 警告 / test 緑 → 3 役をサブエージェントで並列実行 → 指摘反映 → 再テスト緑。
- **最終 go/no-go は人（小林さん）= 待ち。**

## 設計判断

- **既存 Messaging パターンに準拠:** ファサード＋DI 拡張＋Options を同型で構成。依存は `Line.Liff → Line.Core` のみ（更新型トークンは任意認証プロバイダ経路で逆依存なしに注入可）。
- **単一ホスト（api.line.me）:** `liff.yml` は 1 server・2 パス・4 操作。data 系が無いため BaseUrl 上書きは不要で生成既定を使用 → G2 の R1 BaseUrl 順序バグは構造的に非該当。
- **便利メソッドの追加（Messaging との非対称）:** LIFF は 4 操作の閉じた小表面のため便利メソッドで完全被覆。多エンドポイントの Messaging は生成ビルダー直公開に留める。表面規模に応じた判断であり一貫性の欠如ではない旨を `LiffClient` の XML doc に明記。
- **許可ホストを制御系のみに限定:** `CreateWithStaticToken` / DI 既定とも `api.line.me` 単一。Messaging の `Default`（api＋api-data）より狭く、トークン送出面を最小化。

## ゲート結果

| 役 | 判定 | 主な指摘 |
|---|---|---|
| コード | **PASS** | 認証（逆依存なし）・DI（冪等化・CVE 修正版ハンドラ）・R1 非該当・Stream 破棄・引数検証・TFM いずれも良好。指摘は Info 2 件（`CreateWithStaticToken` は IHttpClientFactory 非経由＝Messaging と同じ割り切り／doc 例の `!` デリファレンス）で修正不要。 |
| セキュリティ | **PASS** | 新規トークン漏洩経路なし。許可ホスト制御が Messaging より締まっており良好。CVE-2026-44503（RedirectHandler クロスホスト漏洩）は DI/直接生成の両経路で修正版適用。低位 1 件（非 null 空配列 `AllowedHosts` が `LineHosts.Default` へ拡大＝Messaging と同一挙動、実害軽微）は非ブロッキング。 |
| テスト・アーキ | **PASS（非ブロッキング CONCERNS）** | アーキ同型・単一ホスト差異の扱い・破壊的変更検知（snapshot 登録＋完全性ガード）・R3 版整合いずれも充足。中 2 件（引数ガード未テスト／LIFF 固有の Bearer 付与・許可外ホスト負側の e2e 未検証）＋低 1 件（非対称の設計意図を明文化）を推奨。 |

## 反映した指摘

- **test-arch 中①（引数ガード）:** `LiffClientTests` に `AddAppAsync(null)` / `UpdateAppAsync("" | null, …)` / `UpdateAppAsync(id, null)` / `DeleteAppAsync("" | null)` の例外テスト 6 件を追加。
- **test-arch 中②（LIFF 認証 e2e）:** `LiffClientHttpTests` に「api.line.me で `Authorization: Bearer` 付与（正側）」「api-data.line.me へ逸れた場合は token withhold（負側）」の 2 件を、`CreateWithStaticToken` と同じ実配線（`StaticChannelAccessTokenProvider(LineHosts.Api)`）で追加。
- **test-arch 低（設計意図）:** Messaging との非対称の理由を `LiffClient` の XML doc に一文追記。
- **security 低 / code Info:** 挙動は Messaging と同一のため今回は据え置き（非ブロッキング）。将来 Messaging 側とまとめて空配列の扱いを再検討する余地として記録に残す。

## 検証結果（as-of 2026-07-13）

- ビルド: **0 警告 / 0 エラー**（ソリューション全体）。
- テスト: **76/76 合格**（実装前 50 → LIFF 追加後 76。うち LIFF 関連 20＋公開 API snapshot 6）。
- 脆弱性監査: `dotnet list package --vulnerable --include-transitive` クリーン（Kiota 全 2.0.0）。

## 総括

3 役すべて実質 PASS（test-arch の CONCERNS は反映済みで非ブロッキング）。LIFF 利用シーン実装は既存パターンに整合し、優先利用シーン②「LIFF 管理」の手書き表面が揃った。**GO 推奨、人の go/no-go 待ち。**
