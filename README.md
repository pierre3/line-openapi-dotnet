# LINE .NET クライアント

LINE 公開 OpenAPI 仕様から **Kiota** で生成した .NET/C# クライアントに、利用シーン単位の手書きファサード／DI／受信グルーを重ねたクライアントライブラリ群です。**TFM は `net10.0` 単一**（netstandard2.0 / .NET Framework は対象外）。

> ローカル（Windows/.NET）での実行を想定しています。設計方針は `docs/LINE-dotnet-client-design.md`、開発文脈は `CLAUDE.md` を参照。

> 📚 **ユーザーマニュアル（DocFX）:** 概念記事（英語・日本語）＋英語 API リファレンスを `docs/manual/` に用意しています。ビルドは `dotnet docfx docs/manual/docfx.json --serve`（下記「ドキュメント生成」参照）。設計方針は `docs/LINE-dotnet-client-design.md` §13。

## パッケージ

| パッケージ | 役割 |
|---|---|
| `Line.OpenApi.Core` | 共通基盤（認証プロバイダ・Webhook 署名検証・許可ホスト） |
| `Line.OpenApi.ChannelAccessToken` | チャネルアクセストークン発行（v2.1 JWT / v3 ステートレス・更新型プロバイダ） |
| `Line.OpenApi.Messaging` | メッセージ送受信（`MessagingClient` ファサード＝制御系＋データ系 2 クライアント統合） |
| `Line.OpenApi.Messaging.Webhook` | Webhook モデル＋受信グルー（`WebhookRequestParser`＝署名検証＋逆直列化） |
| `Line.OpenApi.Liff` | LIFF アプリ管理（`LiffClient` ファサード） |

## 前提

- .NET SDK 10 以降（`dotnet --version`）
- （再生成する場合のみ）Kiota CLI: `dotnet tool install --global Microsoft.OpenApi.Kiota`

---

## 利用チュートリアル

### 1. メッセージ送信（Line.OpenApi.Messaging）

```csharp
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;

// 簡易生成（長期チャネルアクセストークン）
var client = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

await client.Api.V2.Bot.Message.Push.PostAsync(new PushMessageRequest
{
    To = "U0123456789abcdef...",
    Messages = new()
    {
        new TextMessage { Text = "Hello, world" },
    },
});

// コンテンツ取得はデータ系(api-data.line.me)へ自動ルーティングされる
var stream = await client.Blob.V2.Bot.Message["<messageId>"].Content.GetAsync();
```

DI（推奨。`IHttpClientFactory` によるハンドラ共有・CVE 修正版ミドルウェア適用）:

```csharp
using Line.OpenApi.Messaging.DependencyInjection;

services.AddLineMessaging(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// 解決: sp.GetRequiredService<MessagingClient>()
```

短期トークン（v2.1 JWT アサーション等）を使う場合は、更新型プロバイダを認証プロバイダ注入経路で渡す:

```csharp
services.AddLineMessaging(sp => /* IAuthenticationProvider を返す（Line.OpenApi.ChannelAccessToken の更新型プロバイダ等） */);
```

### 2. LIFF アプリ管理（Line.OpenApi.Liff）

```csharp
using Line.OpenApi.Liff;
using Line.OpenApi.Liff.Generated.Models;

var liff = LiffClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

var apps  = await liff.GetAppsAsync();
var added = await liff.AddAppAsync(new AddLiffAppRequest
{
    View = new LiffView { Type = LiffView_type.Full, Url = "https://example.com" },
});
await liff.UpdateAppAsync(added!.LiffId!, new UpdateLiffAppRequest { Description = "updated" });
await liff.DeleteAppAsync(added.LiffId!);
```

DI: `services.AddLineLiff(o => o.ChannelAccessToken = "…");`

### 3. Webhook 受信（Line.OpenApi.Messaging.Webhook）

`WebhookRequestParser` が **署名検証（`x-line-signature`）＋本文の逆直列化**を 1 呼び出しに束ねます。署名 NG は `WebhookSignatureException`、本文不正は `WebhookPayloadException`（どちらも基底 `WebhookException`）を投げます。

```csharp
using Line.OpenApi.Messaging.Webhook.DependencyInjection;

services.AddLineWebhook(o => o.ChannelSecret = "CHANNEL_SECRET");
// 解決: sp.GetRequiredService<WebhookRequestParser>()
```

ASP.NET Core での受信例（**生ボディの取得と署名ヘッダの抽出は利用側の責務**。署名は生バイト列に対して検証するため、モデルバインド前の生ボディを読むこと）:

```csharp
using Line.OpenApi.Messaging.Webhook;
using Line.OpenApi.Messaging.Webhook.Generated.Models;

app.MapPost("/webhook", async (HttpRequest request, WebhookRequestParser parser) =>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    var body = ms.ToArray();                                  // 署名対象の生バイト
    var signature = request.Headers["x-line-signature"];

    CallbackRequest callback;
    try
    {
        callback = await parser.ParseAsync(body, signature);
    }
    catch (WebhookSignatureException) { return Results.Unauthorized(); }  // 署名 NG
    catch (WebhookPayloadException)   { return Results.BadRequest(); }    // 本文 NG

    // イベントは type discriminator で具象型に復元済み（未知 type は基底 Event）。
    // ここから先の分岐は利用側で行う:
    foreach (var ev in callback.Events!)
    {
        switch (ev)
        {
            case MessageEvent m when m.Message is TextMessageContent t:
                Console.WriteLine($"text: {t.Text}");
                break;
            case FollowEvent:                 /* 友だち追加 */          break;
            case PostbackEvent p:             /* p.Postback!.Data */    break;
            // 未知イベントは基底 Event 型のまま届く（無視も可）
        }
    }
    return Results.Ok();
});
```

> マルチテナント（チャネルごとに異なるシークレット）では、静的オーバーロード
> `WebhookRequestParser.ParseAsync(channelSecret, body, signature)` を使う。
>
> 本文サイズの上限（DoS 対策）は本ヘルパの責務外。ASP.NET Core の `MaxRequestBodySize` 等、
> 上流で生ボディのサイズ制限を設けること。

---

## 再生成・ビルド・テスト

リポジトリルートで:

```powershell
# 生成（specs は openapi/ に同梱。channel-access-token.yml の未引用 urn を冪等に引用符化）
./scripts/generate.ps1        # macOS/Linux は bash scripts/generate.sh
dotnet build                  # net10.0 単一
dotnet test                   # webhook 多態含め既定で全実行（opt-in フラグ不要）
```

生成コードは `src/**/Generated/`（`kiota-lock.json` はコミット対象）。`Microsoft.Kiota.Bundle` の版は `Directory.Build.props` の `KiotaBundleVersion` で一元管理（現状 2.0.0）。

---

## ドキュメント生成（DocFX）

ユーザーマニュアル（`docs/manual/`）は [DocFX](https://dotnet.github.io/docfx/) で生成します。DocFX はローカルツール（`.config/dotnet-tools.json`、現状 2.78.5）としてピン留め済み。リポジトリルートで:

```powershell
dotnet tool restore                              # 初回のみ（DocFX を復元）
dotnet docfx docs/manual/docfx.json              # metadata 抽出 + サイトビルド → docs/manual/_site/
dotnet docfx docs/manual/docfx.json --serve      # ローカルプレビュー（http://localhost:8080）
```

- **API リファレンスは英語のみ**（手書き公開表面の XML doc コメントから自動生成。`filterConfig.yml` で `Line.*.Generated` を除外）。
- **概念記事は英語（`en/`）・日本語（`ja/`）の 2 系統**。
- 生成物（`docs/manual/api/`・`docs/manual/_site/`）は Git 追跡外。設定・記事のみ追跡。詳細は設計 §13。

---

## 付録: PoC 検証メモ

G0〜G4 で以下を実機確認済み（詳細は `docs/reviews/`）:

1. **ホスト分離** — `Line.OpenApi.Messaging/Generated/Api`(制御系) と `.../Generated/Blob`(`content` 系) を 2 クライアント分離生成し、`MessagingClient` が Blob 側 BaseUrl を `api-data.line.me` に設定。回帰は `MessagingHostRoutingTests`。
2. **form-urlencoded** — `Line.OpenApi.ChannelAccessToken` のトークン発行が型付きモデルで送出（`/oauth2/v3/token` の oneOf 合成ボディは form 非対応のため手書きヘルパで平坦化）。
3. **webhook 多態** — `CallbackRequest` と各イベント派生型を discriminator で復元。回帰は `WebhookDeserializationTests`。
4. **net10.0 単一ビルド** — 全ライブラリが `net10.0` でビルド。
5. **公開 API 表面 snapshot** — 手書き表面のみ `PublicApiSnapshotTests` で回帰検知（Generated 除外＋完全性ガード）。

## 構成

```
（リポジトリルート）
├── LineOpenApi.slnx             # ソリューション
├── Directory.Build.props        # 共通 TFM(net10.0)/nullable/Kiota版
├── openapi/                     # 仕様スナップショット
├── scripts/generate.ps1 / .sh   # Kiota 生成コマンド
├── src/
│   ├── Line.OpenApi.Core/               # 認証プロバイダ・署名検証・許可ホスト（手書き）
│   ├── Line.OpenApi.ChannelAccessToken/ # トークン発行（form-urlencoded 込み生成＋手書きヘルパ）
│   ├── Line.OpenApi.Messaging/          # 制御系+データ系2クライアント + MessagingClient ファサード
│   ├── Line.OpenApi.Messaging.Webhook/  # webhook モデル + WebhookRequestParser（受信グルー）
│   └── Line.OpenApi.Liff/               # LIFF + LiffClient ファサード
├── tests/
│   ├── Line.OpenApi.Tests/      # 手書き表面のテスト（署名/受信/ルーティング/DI/snapshot 等）
│   └── Line.OpenApi.Messaging.Webhook.IsolationTests/  # レジストリ非依存の独立検証
└── docs/                        # 設計・レビュー記録・ユーザーマニュアル（manual/）
```
