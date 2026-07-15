# ゲートレビュー記録 — `Line.OpenApi.Login`（LINE Login v2.1 + OIDC）

- **日付:** 2026-07-15
- **対象:** 新規手書きパッケージ `Line.OpenApi.Login`（ブランチ `feat-login`）
- **ゲート方式:** 手書き実装ゲート（G3/G4 前例に倣い 3 役: code / security / test-arch。spec-reviewer は OpenAPI spec/Kiota 生成向けで LINE Login は spec 非存在のため非該当）
- **判定サマリ:** **BLOCKING なし = main マージ可。** code = CONCERNS / security = PASS / test-arch = PASS。主要な非ブロッキング指摘は本コミットで反映済み。

## スコープ

- ライブラリ `LoginClient`（`Line.OpenApi.Core` 依存・`Bot` メタ非包含）:
  - 認可 URL 生成（`BuildAuthorizationUrl` + PKCE/state ヘルパ `LineLoginSecurity`）
  - トークン: `ExchangeCodeAsync`（PKCE 対応）/ `RefreshTokenAsync` / `RevokeTokenAsync` / `VerifyAccessTokenAsync`
  - OIDC: `VerifyIdTokenAsync`（**サーバ委譲 `POST /oauth2/v2.1/verify`**）
  - プロフィール/友だち: `GetUserInfoAsync` / `GetProfileAsync` / `GetFriendshipStatusAsync`
  - `DeauthorizeAsync`（ヘッダ=Messaging channel token / ボディ=user token・疎結合）
- `Line.Core` 追加: 汎用 `StaticBearerTokenProvider`（ホスト制限付き Bearer）、`LineHosts.AccessLine` 定数
- DI `AddLineLogin` + `LineLoginOptions`
- HTTP は Kiota `Microsoft.Kiota.Bundle.DefaultRequestAdapter`（生成クライアント無し・グローバルレジストリ非依存）
- **ローカル ID Token 検証（Web=HS256 / ネイティブ・LIFF=ES256+JWKS）は次サイクルへ持ち越し**

## 各役の結果

### security = PASS（BLOCKING なし）
- トークン egress: `StaticBearerTokenProvider` が許可外ホストで空文字を返す fail-closed。既定 `api.line.me` のみ。負側テストあり。
- PKCE/state: `RandomNumberGenerator`（CSPRNG）、state 256bit、S256 導出正しい。
- client_secret はボディのみ・URL/クエリ/ログに載らない。認可 URL は `client_id` のみ露出。
- deauthorize の 2 系統分離正しく、`ChannelAccessToken` への逆依存なし。
- ID Token はサーバ委譲のみ＝未検証トークン受理経路なし。
- CVE 修正 RedirectHandler は quick パス（`KiotaClientFactory.Create()`）/DI パス両方に存在。
- 非ブロッキング: 匿名フォーム経路は host-gate 無し（URL 固定＋RedirectHandler で実質無害。理論上のみ body 転送リスク）／`VerifyIdTokenAsync` の nonce 任意はドキュメント事項。

### test-arch = PASS（BLOCKING なし）
- 一方向依存 ADR 適合（`verify-packages.ps1` に登録・pack ガード）。Bot 非包含（LIFF と同扱い）。
- `DefaultRequestAdapter` 採用は隔離性が高く妥当。`PathParams()` 毎回新規＋アダプタ BaseUrl 確定で R1 類似バグを構造的に回避。
- `StaticBearerTokenProvider` の ~15 行複製は自己記述性優先で許容。
- 推奨追加テスト 3 本（bearer GET 401 / NoContent 非2xx / client-level host gating）→ **反映済み**。
- 受容事項: 実 API 契約 fixture 無し（モデルのサイレントドリフト検知不可）＝spec 非存在ゆえの既知制約。

### code = CONCERNS（BLOCKING なし・マージ可）
- 良: BaseUrl 二重固定が正しくコメントも正確／エンドポイント・Content-Type が LINE 公式と一致／逆依存回避／PKCE 暗号強度／英語コメント。
- **[Medium] OAuth エラーボディが型化されない** → `LoginErrorResponse`（`ApiException` 派生）を追加し全 send にエラーマッピング登録で **反映済み**（`error`/`error_description` を表面化、テスト追加）。
- [Low] ユーザートークン呼び出し毎のアダプタ生成 → リーク/スレッド安全性問題なし・高頻度時のみ軽微。将来のトークン単位キャッシュ候補として**受容**。
- [Low] deauthorize のボディ形式（JSON）は LINE 公式が明記せず → **GA 前に実機確認**（サンドボックスは外部 HTTP 遮断のため未実施）。調査は JSON `{"userAccessToken":...}` を採用。
- [Low] 公開メソッドの XML doc `<param>/<returns>` 欠落 → **反映済み**（補完）。

## 反映した指摘（本コミット）
1. `LoginErrorResponse` 追加＋全 send のエラーマッピング（code Medium）
2. 負系テスト 3 本＋匿名経路 Authorization 無しアサート（test-arch）
3. 公開メソッドの XML doc 補完（code Low）

## 未反映（受容 / follow-up）
- 匿名経路の host-gate 追加（defense-in-depth・任意）
- ユーザートークン呼び出しのアダプタ/トークン単位キャッシュ（性能・任意）
- deauthorize ボディ形式の GA 前実機確認
- ローカル ID Token 検証（次サイクル・別スコープ）

## as-of 検証（2026-07-15）
- テスト 155/155 緑（新規 Login 関連 42）／全ソリューションビルド 0 警告／pack スモーク 7 パッケージ PASS／DocFX 0 warnings。
- 公開 API snapshot approved 更新: `Line.OpenApi.Login`（新規）＋`Line.OpenApi.Core`（`StaticBearerTokenProvider`/`AccessLine` 追加差分）。

**GO 推奨、人の go/no-go 待ち（未コミット）。**
