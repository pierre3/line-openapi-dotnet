# ゲートレビュー記録 — `Line.OpenApi.Samples.Login`（LINE Login サンプル Web アプリ）

- **日付:** 2026-07-15
- **対象:** 新規サンプル `samples/Line.OpenApi.Samples.Login/`（ブランチ `feat-login-sample`）
- **ゲート方式:** 3 役（code / security / test-arch）。※サンプルはデモコード（`IsPackable=false`・非配布・公開 API 表面でない）だが、OAuth セキュリティを扱うためユーザー希望でレビュー実施。
- **判定サマリ:** **3 役すべて PASS・BLOCKING なし。** 非ブロッキング指摘は本コミットで反映済み。

## スコープ

minimal API で LINE Login 認可コードフロー（PKCE）を実機 e2e 実演: `GET /login`（認可 URL リダイレクト）→ `GET /callback`（state 照合・`ExchangeCodeAsync`・`VerifyIdTokenAsync`・`GetProfileAsync`/`GetFriendshipStatusAsync`）→ `GET /logout`（deauthorize/revoke）。オフライン既定・localhost コールバックのみでトンネル不要。

## 各役の結果

- **security = PASS:** CSRF `state` 照合（空/不一致を棄却）・PKCE（verifier はサーバ保持・S256 送出）・OIDC `nonce` 委譲検証・全 HTML 出力 `HtmlEncode`・トークンはサーバ側のみ・open redirect なし。すべて正しい手本。
- **code = PASS:** 3-leg フローと `LoginClient` API 呼び出しが正確。オフライン既定・503 縮退あり。
- **test-arch = PASS:** 既存サンプルのアーキパターンに忠実。`IsPackable=false`・slnx 登録・パッケージ非混入・ユニットテスト不要方針すべて整合。

## 反映した指摘（本コミット）

- **[test-arch 中] DI 未実演** → `new LoginClient(...)` を **`AddLineLogin`（DI）** に変更。Web 向け推奨経路（`IHttpClientFactory` 共有ハンドラ＋CVE 修正 RedirectHandler）を実演。
- **[code 低] `/logout` の revoke→deauthorize 順で失効済みトークン送信** → deauthorize/revoke を**排他化**（Messaging トークン有=deauthorize・無=revoke）＋ try/catch で 500 化回避。
- **[code 低] 不正 `LINE_LOGIN_REDIRECT_URI` で起動クラッシュ** → `Uri.TryCreate` で明確なメッセージに。非 localhost では `UseUrls` を回避し `ASPNETCORE_URLS` に委ねる。
- **[code 低] 空トークンで NRE** → 成功 2xx 空ボディをガード。
- **[security 低] Cookie/セッション** → `SameSite=Lax` 設定＋本番 `SecurePolicy.Always` コメント。state/verifier/nonce をコード交換後にクリア（使い切り）。`FriendFlag` も一律 `HtmlEncode`。
- **[test-arch 低] 誤解コメント／README** → 「deauthorize は offline 検証不可」の誤りを修正（スタブ HTTP テスト済み）。README 英日に「Login は offline 不可＝disabled 表示のみ」注記・未実演メソッド（`RefreshTokenAsync`/`VerifyAccessTokenAsync`/`GetUserInfoAsync`）案内・Login トラブルシュート（redirect_uri 不一致・state mismatch・OA 連携）を追記。

## as-of 検証（2026-07-15）

- 全ソリューションビルド 0 警告。サンプルのオフライン起動を実機確認（`/`＝200 disabled・`/login`/`/callback`＝503）。pack は 7 パッケージ維持（サンプル `IsPackable=false` で除外）。ライブラリ表面の変更なし（公開 API snapshot 影響なし）。

**GO 済み（人の go/no-go）。**
