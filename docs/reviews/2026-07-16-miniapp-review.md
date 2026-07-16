# ゲートレビュー記録 — `Line.OpenApi.MiniApp`（LINE MINI App サービスメッセージ＋IAP）

- **日付:** 2026-07-16
- **対象:** 新規手書きパッケージ `Line.OpenApi.MiniApp`（ブランチ `main`、未コミット）
- **ゲート方式:** 手書き実装ゲート（`Line.OpenApi.Login` の前例に倣い 3 役: code / security / test-arch。spec-reviewer は OpenAPI spec/Kiota 生成向けで LINE MINI App は spec 非存在のため非該当）
- **判定サマリ:** **BLOCKING なし = main マージ可。** code = PASS / security = PASS / test-arch = PASS。指摘は全て非ブロッキングで、テストカバレッジに関する重なる指摘は本レビュー中に反映済み。

## スコープ

- ライブラリ `MiniAppClient`（`Line.OpenApi.Core` のみ依存・`Bot` メタ非包含。`Line.OpenApi.ChannelAccessToken`・`Line.OpenApi.Login` に非依存）:
  - サービスメッセージ: `IssueNotificationTokenAsync`（`POST /message/v3/notifier/token`）/ `SendServiceMessageAsync`（`POST /message/v3/notifier/send?target=service`）— channel access token（stateless/short-lived 限定）
  - IAP: `ReserveProductAsync`（`POST /iap/v1/product/reserve`）— user access token / `GetWebhookEventsAsync`（`GET /iap/v1/webhook/events`）— channel access token
- Login と同型のトークン非保持設計（呼び出しごとの引数）。エラー型を notifier 系（`NotifierErrorResponse`）と IAP 系（`IapErrorResponse`）で分離。
- DI `AddLineMiniApp()`（Login と異なり必須設定なし＝channel id/secret を保持しないため）。
- HTTP は Kiota `Microsoft.Kiota.Bundle.DefaultRequestAdapter`（生成クライアント無し、Login と同一パターン）。
- CLI/MCP 露出（`line miniapp ...`）は今回のスコープ外。

## 各役の結果

### security = PASS（BLOCKING なし）
- トークン egress: `StaticBearerTokenProvider` が許可外ホストで空文字を返す fail-closed。既定 `api.line.me` のみ（`api-data.line.me` を含めない明示的オーバーライド）。負側テストで確認。
- `BaseUrl` はコード内で `https://api.line.me` に固定、呼び出し元入力が URL 組み立てに影響する経路なし＝R1（BaseUrl 順序バグ）/SSRF 的リスクなし。
- 例外メッセージ・エラーモデル（`NotifierErrorResponse`/`IapErrorResponse`）はサーバ応答由来のフィールドのみ表面化、トークンは混入しない。クライアントはトークンをフィールドに保持しない。
- CVE 修正済み RedirectHandler はクイックパス・DI パス両方に存在（監査クリーン確認済み）。
- DI 冪等性確認済み（`MiniAppMarker` ガード＋`TryAddSingleton`）。
- 非ブロッキング: `AddLineMiniApp` を異なる `configure` で複数回呼ぶと `Options.Configure` が積み重なる（Login と同型の既存許容パターン、信頼できるスタートアップコード前提）／stateless/short-lived 制約はドキュメントのみでコード強制なし（機能面の落とし穴、セキュリティ欠陥ではない）。

### test-arch = PASS（BLOCKING なし）
- 一方向依存 ADR 適合（`verify-packages.ps1` に登録・pack ガード）。Bot 非包含（Login と同扱い）。
- `DefaultRequestAdapter` 直利用パターンは Login と同一で妥当。トークン非保持設計の DI 非対称性（必須設定なし）はコメントで明記済み・設計上正しい。
- notifier系/IAP系のエラー型分離を一次情報と照合し、確認できた4面すべてで実装と一致（IAP系は一次情報に例あり、notifier系は類推適用と明記）。
- 横断配線（slnx / test csproj / verify-packages.ps1 / docfx.json / PublicApiSnapshotTests / approved.txt）すべて確認済み。
- 指摘（本レビュー中に反映）: DI の `AddLineMiniApp_AppliesAllowedHosts_FromOptions` がテスト名の主張と裏腹に host gating の実挙動を検証していない／引数ガード節（10箇所以上）が実質未テスト。

### code = PASS（BLOCKING なし）
- BaseUrl 二重固定・`PathParams()` 毎回新規生成など R1 回避パターンを正確に踏襲。エラーマッピングの網羅性、引数検証の一貫性、英語コメント規約、公開 API 命名の LINE 公式ドキュメントとの対応、いずれも確認。
- 指摘: 概念記事が見つからない（**誤検知** — ファイル名を `miniapp.md` で検索していたため。実際は `docs/manual/en/mini-app.md`・`ja/mini-app.md` として存在し、`toc.yml` 登録・DocFX ビルド 0 warnings も確認済み）。
- 指摘（Low・受容）: notifier 系エラー形状は一次情報に実例なく類推適用 → GA 前実機確認リストへ追加（`docs/coverage-roadmap.md` に反映済み）。
- 指摘（本レビュー中に反映）: `cursor`/`status` 省略時の未テスト。

## 反映した指摘（本レビュー中に追加したテスト、`tests/Line.OpenApi.Tests/`）
1. `GetWebhookEventsAsync_OmitsCursorAndStatus_WhenNull`（test-arch Low・code Low、重複指摘）
2. `GetWebhookEventsAsync_Accepts_PageSizeBoundaries`（境界値 1/100 の成功系、test-arch Info）
3. `SendServiceMessage_ErrorStatus_Surfaces_NotifierErrorResponse` / `GetWebhookEvents_ErrorStatus_Surfaces_IapErrorResponse`（test-arch Low、個別エンドポイントのエラーパス）
4. `Methods_Reject_MissingRequiredArguments`（`[Theory]`、test-arch Medium＝引数ガード節の未テスト）
5. `AddLineMiniApp_AppliesAllowedHosts_FromOptions_ToActualRequests`（test-arch Medium＝DI経由で実際に host gating が効くことを、名前付き `HttpClient` にレコーディングハンドラを差し込んで実証）

テストは 253→264（+11）。

## 未反映（受容 / follow-up）
- notifier 系エラーボディ形状の実機確認（GA 前、`docs/coverage-roadmap.md`／`SESSION-HANDOFF.md` の既存リストへ追加）
- `SendServiceMessageAsync` は `PostNotifierAsync` ヘルパーを使わず個別に組み立てているが、エラーマッピング辞書は共有のため実害は小さい（code Low・受容）
- Options 積み重ね（`AddLineMiniApp` 複数呼び出し時）は Login と同型の既存許容パターン
- CLI/MCP 露出（`line miniapp ...`）は今回のスコープ外・後段で相談

## as-of 検証（2026-07-16）
- テスト 264/264 緑（MiniApp 関連 24）／全ソリューションビルド 0 警告／pack スモーク 12 パッケージ（11 code + 1 meta）PASS／DocFX 0 warnings。
- 公開 API snapshot approved 追加: `Line.OpenApi.MiniApp`（新規）。

**GO 推奨、人の go/no-go 待ち（未コミット）。**
