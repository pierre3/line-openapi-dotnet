# 機能カバレッジ / ロードマップ — LINE Platform 対応状況

このドキュメントは、LINE Platform の開発者向け機能に対する当ライブラリ（`Line.OpenApi.*`）のカバレッジと、将来追加候補の優先順位を記録する。2026-07-15 時点で一次情報（https://developers.line.biz/ 、 https://github.com/line/line-openapi ）で裏取りした調査に基づく。

## 現状のカバレッジ

line-openapi リポジトリの OpenAPI spec は **9 本**。うち **8 本を取り込み済み**（Kiota 生成＋手書きファサード。2026-07-15 のカバレッジ拡充で insight/manage-audience/module/shop を追加）。残り 1 本（module-attach）のみ見送り:

| spec | パッケージ | 状態 |
|---|---|---|
| `messaging-api.yml` | `Line.OpenApi.Messaging` | ✅ 取り込み済み（**Rich Menu 全操作を含む**、下記） |
| `channel-access-token.yml` | `Line.OpenApi.ChannelAccessToken` | ✅ 取り込み済み |
| `webhook.yml` | `Line.OpenApi.Messaging.Webhook` | ✅ 取り込み済み |
| `liff.yml` | `Line.OpenApi.Liff` | ✅ 取り込み済み |
| `insight.yml` | `Line.OpenApi.Insight` | ✅ 取り込み済み（`InsightClient`・7 GET） |
| `manage-audience.yml` | `Line.OpenApi.ManageAudience` | ✅ 取り込み済み（`ManageAudienceClient`・control/data 分離＋multipart by-file） |
| `module.yml` | `Line.OpenApi.Module` | ✅ 取り込み済み（`ModuleClient`・4 ops） |
| `shop.yml` | `Line.OpenApi.Shop` | ✅ 取り込み済み（`ShopClient`・1 op） |
| `module-attach.yml` | — | ⏸ 見送り（下記・パートナー限定 1 op / manager.line.biz + Basic + PKCE で最難） |

### Rich Menu は既に生成済み（重要）

「Rich Menu は OpenAPI 仕様に無い」は**誤り**。`messaging-api.yml` に全 23 操作が定義され Kiota 生成済みで、現状のまま利用可能:
- **制御系**（`MessagingClient.Api.V2.Bot.Richmenu...`, `api.line.me`）: create/get/delete/list/validate、default 設定、alias CRUD、per-user link/unlink、bulk link/unlink、batch。
- **画像系**（`MessagingClient.Blob.V2.Bot.Richmenu[id].Content`, `api-data.line.me`）: `getRichMenuImage`/`setRichMenuImage`（`/content` サフィックスで既存のデータプレーン分離に自動収容）。

**残ギャップは capability ではなく使い勝手**:
1. 画像アップロード `setRichMenuImage` は spec 上 `*/*`（binary）だが LINE は `Content-Type: image/png`/`image/jpeg` を必須とする。生成コードは content-type を明示せず落とし穴になる（form-urlencoded の `StatelessJwtAssertionTokenSource` と同種）→ 手書きヘルパが要る。
2. 利用シーン用の便利ファサード（`LiffClient` 相当）が無く、生の生成ビルダーは辿りにくい。

## 第1部: 未取り込みの OpenAPI spec（残 1 本＝module-attach のみ）

> **更新（2026-07-15）:** カバレッジ拡充で **insight / manage-audience / module / shop の 4 本を取り込み完了**（`Line.OpenApi.Insight` / `.ManageAudience` / `.Module` / `.Shop`。ラウンド 1＝易 3 本、ラウンド 2＝manage-audience。記録は `docs/reviews/2026-07-15-coverage-round{1,2}-review.md`）。**module-attach のみ見送り**（下表・パートナー限定 1 op でコスト対効果最低）。

| spec | 機能 | 需要 | 実装難所 | 状態 |
|---|---|---|---|---|
| **insight** | 統計・分析（友だち属性 demographic、配信数、フォロワー数、開封/クリック interaction、Rich Menu 表示/クリック統計。`/v2/bot/insight/*` GET 7 本） | 中 | なし（`api.line.me` 単一・全 GET・Bearer・R1 非該当） | ✅ `Line.OpenApi.Insight`（薄ファサード） |
| **manage-audience** | オーディエンス管理（userId アップロード JSON/ファイル、click/imp リターゲ、取得/削除/一覧/共有） | 中 | ⚠️ **R1 該当**: `upload/byFile`・`addUserIdsToAudience` の 2 本が `api-data.line.me` + `multipart/form-data` | ✅ `Line.OpenApi.ManageAudience`（control/data 分離＋multipart 手書きヘルパ） |
| **module** | モジュールチャネル（LOA 代理運用、chat control acquire/release、detach、attach 済み bot 一覧。4 本） | 低 | 小（`api.line.me` 単一だが概念が難解、webhook standby イベント連携前提） | ✅ `Line.OpenApi.Module`（module.yml のみ） |
| **module-attach** | モジュール attach 認可 1 本（`POST /module/auth/v1/token`） | 低 | ⚠️ **最難**: 別ホスト `manager.line.biz` + form-urlencoded + Basic 認証 + PKCE（`code_verifier`） | ⏸ **見送り**（手書き必須・既存 Bearer/AllowedHosts 基盤に載らない・コスト対効果悪。実需が出た時点で追加＝Core に Basic 認証プロバイダ＋`manager.line.biz` 定数＋form/PKCE 手書きヘルパが必要） |
| **shop** | ミッションスタンプ送信 1 本（`POST /shop/v3/mission`, productType=STICKER） | 低 | なし（`api.line.me`・JSON・Bearer） | ✅ `Line.OpenApi.Shop`（1 op） |

出典: [insight](https://raw.githubusercontent.com/line/line-openapi/master/insight.yml) / [manage-audience](https://raw.githubusercontent.com/line/line-openapi/master/manage-audience.yml) / [module](https://raw.githubusercontent.com/line/line-openapi/master/module.yml) / [module-attach](https://raw.githubusercontent.com/line/line-openapi/master/module-attach.yml) / [shop](https://raw.githubusercontent.com/line/line-openapi/master/shop.yml)

## 第2部: OpenAPI spec が存在しない主要機能（手書きが必要）

> **更新（2026-07-15）:** LINE Login v2.1 + OIDC は **`Line.OpenApi.Login` として取り込み済み**（手書きパッケージ・`Line.Core` 依存）。実装＝認可 URL 生成（PKCE/state）・トークン交換/リフレッシュ/失効・アクセストークン検証・**ID Token 検証はサーバ委譲 `POST /oauth2/v2.1/verify`**・userinfo・`/v2/profile`・friendship・deauthorize。**ローカル ID Token 検証（Web=HS256／ネイティブ・LIFF=ES256+JWKS）は次サイクルへ持ち越し**（本表の該当行は capability として実現済み、ローカル検証のみ残）。`Line.Core` に汎用 `StaticBearerTokenProvider` を追加（user access token のホスト制限付き付与）。

| 機能 | 難易度 | 需要 | 一次情報 |
|---|---|---|---|
| **LINE Login v2.1 トークン系**（issue/refresh/revoke token, verify access token） | 中（`api.line.me/oauth2/v2.1/*`、form-urlencoded、認可コード/refresh フロー。ChannelAccessToken の form 送出を流用可） | **高** | [reference/line-login](https://developers.line.biz/en/reference/line-login/) |
| **LINE Login: ID Token 検証（OIDC）** | 中〜高（**Web=HS256/channel secret HMAC、ネイティブ/LIFF=ES256/JWKS 公開鍵**の二系統。ローカル検証 or `/oauth2/v2.1/verify` 委譲） | **高** | [verify-id-token](https://developers.line.biz/en/docs/line-login/verify-id-token/) / JWKS `https://api.line.me/oauth2/v2.1/certs` |
| **userinfo / profile**（`/oauth2/v2.1/userinfo`, `/v2/profile`） | 低（GET + Bearer user access token） | 高 | [reference/line-login](https://developers.line.biz/en/reference/line-login/) |
| **Social API 友だち関係**（`GET /friendship/v1/status`） | 低（GET、要 user access token profile scope） | 中〜高 | [reference/social-api](https://developers.line.biz/en/reference/social-api/) |
| **deauthorize**（`POST /user/v1/deauthorize`） | 低 | 低〜中 | [reference/line-login](https://developers.line.biz/en/reference/line-login/) |
| **LINE MINI App** | 低（独自 REST 無し。LIFF+Login の組合せで足りる） | 中 | [docs/line-mini-app](https://developers.line.biz/en/docs/line-mini-app/) |

> ⚠️ **設計上の重要注意:** LINE Login 系は **user access token**（LINE Login チャネルで発行、`profile`/`openid` scope）で認証し、Messaging の **channel access token** とは別物・非互換。取り込む場合 `Line.Core` の認証抽象を「Bearer だがトークン取得経路が別」として拡張する必要がある。ホストは大半 `api.line.me`（既存 AllowedHosts に載る）だが、認可エンドポイントのみ `access.line.me`（ブラウザリダイレクト先で REST ではない）。

## 推奨優先順位（更新: 2026-07-15）

OpenAPI spec の取り込みは module-attach を除き完了。残る候補:

1. ~~**LINE Login + OIDC ID Token 検証**~~ **→ 実装済み（`Line.OpenApi.Login`）。** 残タスク＝**ローカル ID Token 検証（ES256+JWKS / HS256）**を次サイクルで追加（サーバ委譲 `/verify` は実装済み）。自作リスクの大きい JWT 署名検証部分なのでライブラリ化の恩恵が大きい。**最有力の次テーマ。**
2. ~~**insight / manage-audience / module / shop 取り込み**~~ **→ 実装済み（2026-07-15）。** `Line.OpenApi.Insight` / `.ManageAudience`（control/data＋multipart）/ `.Module` / `.Shop`。
3. **Social API 友だち関係**（`friendship/v1/status`）— Login と相乗効果。既に `Line.OpenApi.Login` の `GetFriendshipStatusAsync` として実装済み（Login パッケージ内）。
4. **module-attach**（`Line.OpenApi.Module` へ後付け）— パートナー限定 1 op。異ホスト `manager.line.biz`＋Basic＋PKCE で難所突出。明確な要望が出てから（Core に Basic 認証プロバイダ＋ホスト定数＋form/PKCE 手書きヘルパを追加）。
5. ~~**CLI/MCP の新パッケージ対応**~~ **→ 実装済み（2026-07-15）。** insight / manage-audience / shop を `line` ツールへ露出（CLI `line insight·audience·shop`／MCP `line_insight_*`〔全 read-only〕・`line_audience_*`・`line_shop_mission`。by-file アップロードは CLI 専用）。**module は見送り**（パートナー限定・概念難でローカル開発ツールに不適）。3 役ゲート PASS（記録 `docs/reviews/2026-07-15-coverage-tools-review.md`）。spec §4.8 参照。

---

_初版 2026-07-15。設計方針は `docs/LINE-dotnet-client-design.md`、CLI/MCP は `docs/CLI-MCP-tool-spec.md` を参照。_
