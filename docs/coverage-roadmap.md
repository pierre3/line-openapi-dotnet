# 機能カバレッジ / ロードマップ — LINE Platform 対応状況

このドキュメントは、LINE Platform の開発者向け機能に対する当ライブラリ（`Line.OpenApi.*`）のカバレッジと、将来追加候補の優先順位を記録する。2026-07-15 時点で一次情報（https://developers.line.biz/ 、 https://github.com/line/line-openapi ）で裏取りした調査に基づく。

## 現状のカバレッジ

line-openapi リポジトリの OpenAPI spec は **9 本**。うち **4 本を取り込み済み**（Kiota 生成＋手書きファサード）:

| spec | パッケージ | 状態 |
|---|---|---|
| `messaging-api.yml` | `Line.OpenApi.Messaging` | ✅ 取り込み済み（**Rich Menu 全操作を含む**、下記） |
| `channel-access-token.yml` | `Line.OpenApi.ChannelAccessToken` | ✅ 取り込み済み |
| `webhook.yml` | `Line.OpenApi.Messaging.Webhook` | ✅ 取り込み済み |
| `liff.yml` | `Line.OpenApi.Liff` | ✅ 取り込み済み |

### Rich Menu は既に生成済み（重要）

「Rich Menu は OpenAPI 仕様に無い」は**誤り**。`messaging-api.yml` に全 23 操作が定義され Kiota 生成済みで、現状のまま利用可能:
- **制御系**（`MessagingClient.Api.V2.Bot.Richmenu...`, `api.line.me`）: create/get/delete/list/validate、default 設定、alias CRUD、per-user link/unlink、bulk link/unlink、batch。
- **画像系**（`MessagingClient.Blob.V2.Bot.Richmenu[id].Content`, `api-data.line.me`）: `getRichMenuImage`/`setRichMenuImage`（`/content` サフィックスで既存のデータプレーン分離に自動収容）。

**残ギャップは capability ではなく使い勝手**:
1. 画像アップロード `setRichMenuImage` は spec 上 `*/*`（binary）だが LINE は `Content-Type: image/png`/`image/jpeg` を必須とする。生成コードは content-type を明示せず落とし穴になる（form-urlencoded の `StatelessJwtAssertionTokenSource` と同種）→ 手書きヘルパが要る。
2. 利用シーン用の便利ファサード（`LiffClient` 相当）が無く、生の生成ビルダーは辿りにくい。

## 第1部: 未取り込みの OpenAPI spec 5 本

| spec | 機能 | 需要 | 実装難所 | 見立て |
|---|---|---|---|---|
| **insight** | 統計・分析（友だち属性 demographic、配信数、フォロワー数、開封/クリック interaction、Rich Menu 表示/クリック統計。`/v2/bot/insight/*` GET 7 本） | 中 | なし（`api.line.me` 単一・全 GET・Bearer・R1 非該当） | **ほぼ生成で完了**。薄いファサードのみ |
| **manage-audience** | オーディエンス管理（userId アップロード JSON/ファイル、click/imp リターゲ、取得/削除/一覧/共有） | 中 | ⚠️ **R1 該当**: `upload/byFile`・`addUserIdsToAudience` の 2 本が `api-data.line.me` + `multipart/form-data` | 生成 + Messaging 型の control/data 分離グルー＋multipart 手当て |
| **module** | モジュールチャネル（LOA 代理運用、chat control acquire/release、detach、attach 済み bot 一覧。4 本） | 低 | 小（`api.line.me` 単一だが概念が難解、webhook standby イベント連携前提） | 生成で済むが需要薄（パートナー/SaaS 限定） |
| **module-attach** | モジュール attach 認可 1 本（`POST /module/auth/v1/token`） | 低 | ⚠️ **最難**: 別ホスト `manager.line.biz` + form-urlencoded + Basic 認証 + PKCE（`code_verifier`） | 手書き必須・既存 Bearer/AllowedHosts 基盤に載らない・コスト対効果悪 |
| **shop** | ミッションスタンプ送信 1 本（`POST /shop/v3/mission`, productType=STICKER） | 低 | なし（`api.line.me`・JSON・Bearer） | 生成で済むが実質 1 メソッド・ニッチ |

出典: [insight](https://raw.githubusercontent.com/line/line-openapi/master/insight.yml) / [manage-audience](https://raw.githubusercontent.com/line/line-openapi/master/manage-audience.yml) / [module](https://raw.githubusercontent.com/line/line-openapi/master/module.yml) / [module-attach](https://raw.githubusercontent.com/line/line-openapi/master/module-attach.yml) / [shop](https://raw.githubusercontent.com/line/line-openapi/master/shop.yml)

## 第2部: OpenAPI spec が存在しない主要機能（手書きが必要）

| 機能 | 難易度 | 需要 | 一次情報 |
|---|---|---|---|
| **LINE Login v2.1 トークン系**（issue/refresh/revoke token, verify access token） | 中（`api.line.me/oauth2/v2.1/*`、form-urlencoded、認可コード/refresh フロー。ChannelAccessToken の form 送出を流用可） | **高** | [reference/line-login](https://developers.line.biz/en/reference/line-login/) |
| **LINE Login: ID Token 検証（OIDC）** | 中〜高（**Web=HS256/channel secret HMAC、ネイティブ/LIFF=ES256/JWKS 公開鍵**の二系統。ローカル検証 or `/oauth2/v2.1/verify` 委譲） | **高** | [verify-id-token](https://developers.line.biz/en/docs/line-login/verify-id-token/) / JWKS `https://api.line.me/oauth2/v2.1/certs` |
| **userinfo / profile**（`/oauth2/v2.1/userinfo`, `/v2/profile`） | 低（GET + Bearer user access token） | 高 | [reference/line-login](https://developers.line.biz/en/reference/line-login/) |
| **Social API 友だち関係**（`GET /friendship/v1/status`） | 低（GET、要 user access token profile scope） | 中〜高 | [reference/social-api](https://developers.line.biz/en/reference/social-api/) |
| **deauthorize**（`POST /user/v1/deauthorize`） | 低 | 低〜中 | [reference/line-login](https://developers.line.biz/en/reference/line-login/) |
| **LINE MINI App** | 低（独自 REST 無し。LIFF+Login の組合せで足りる） | 中 | [docs/line-mini-app](https://developers.line.biz/en/docs/line-mini-app/) |

> ⚠️ **設計上の重要注意:** LINE Login 系は **user access token**（LINE Login チャネルで発行、`profile`/`openid` scope）で認証し、Messaging の **channel access token** とは別物・非互換。取り込む場合 `Line.Core` の認証抽象を「Bearer だがトークン取得経路が別」として拡張する必要がある。ホストは大半 `api.line.me`（既存 AllowedHosts に載る）だが、認可エンドポイントのみ `access.line.me`（ブラウザリダイレクト先で REST ではない）。

## 推奨優先順位（Rich Menu 便利層は別途着手前提）

1. **LINE Login + OIDC ID Token 検証**（新規手書きパッケージ、例 `Line.OpenApi.Login`）— 需要最大。Bot 送信に次ぐ第二の主要シーン。spec が無く他所で生成できないため差別化価値が最も高い。OIDC 検証（ES256/HS256 二系統、JWKS）は自作リスクが大きくライブラリ化の恩恵が明確。既存 form-urlencoded 基盤を流用でき、ホストも `api.line.me` 中心。難所は user access token の抽象化と JWT 検証。
2. **Social API 友だち関係**（`friendship/v1/status`）— 1 とセットで相乗効果（bot_prompt→友だち判定の定番導線）。単純 GET で軽く、1 の user access token 基盤に相乗り。単独では小粒なので 1 に同梱推奨。
3. **insight 取り込み**（`Line.OpenApi.Insight`）— 5 spec 中で最も「生成即完了」かつ需要中。Messaging と同一の Bearer/単一ホストで R1 非該当、手書きグルー最小。低コスト高カバレッジ。
4. **manage-audience 取り込み**（`Line.OpenApi.ManageAudience`）— 需要中。Messaging で確立済みの control/data 2 クライアント分離＋multipart パターンを再利用でき実装コストが読める。
5. **module / module-attach / shop は保留（低優先）** — module 系はパートナー限定で需要低、module-attach は異ホスト+Basic+PKCE で難所突出、shop はニッチ 1 本。明確な要望が出てから。

---

_初版 2026-07-15。設計方針は `docs/LINE-dotnet-client-design.md`、CLI/MCP は `docs/CLI-MCP-tool-spec.md` を参照。_
