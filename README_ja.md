[English](https://github.com/pierre3/line-openapi-dotnet/blob/main/README.md) | **日本語**

# LINE .NET クライアント (Line.OpenApi.*)

[![CI](https://github.com/pierre3/line-openapi-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/pierre3/line-openapi-dotnet/actions/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-blue)](https://pierre3.github.io/line-openapi-dotnet/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/pierre3/line-openapi-dotnet/blob/main/LICENSE)
[![NuGet](https://img.shields.io/badge/NuGet-Line.OpenApi.*-004880?logo=nuget)](https://www.nuget.org/packages?q=tags%3A%22LINE-API%22)

LINE 公開 OpenAPI 仕様から [Kiota](https://learn.microsoft.com/openapi/kiota/) で生成した .NET/C# クライアントに、利用シーン単位の手書きファサード／DI／受信グルーを重ねたクライアントライブラリ群です。

- **メッセージ送受信（Bot）** と **LIFF アプリ管理** を主要ユースケースとしてサポート
- 制御系（`api.line.me`）／データ系（`api-data.line.me`）の 2 ホストを `MessagingClient` ファサードで自動ルーティング
- Webhook 受信（署名検証＋逆直列化）を `WebhookRequestParser` に集約
- `IHttpClientFactory` ベースの DI 統合

対象フレームワークは **`net10.0` 単一**です（netstandard2.0 / .NET Framework は対象外）。

## パッケージ

| パッケージ | 役割 |
|---|---|
| `Line.OpenApi.Core` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Core.svg)](https://www.nuget.org/packages/Line.OpenApi.Core) | 共通基盤（認証プロバイダ・Webhook 署名検証・許可ホスト） |
| `Line.OpenApi.ChannelAccessToken` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.ChannelAccessToken.svg)](https://www.nuget.org/packages/Line.OpenApi.ChannelAccessToken) | チャネルアクセストークン発行（v2.1 JWT / v3 ステートレス・更新型プロバイダ） |
| `Line.OpenApi.Messaging` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Messaging.svg)](https://www.nuget.org/packages/Line.OpenApi.Messaging) | メッセージ送受信（`MessagingClient` ファサード＝制御系＋データ系 2 クライアント統合） |
| `Line.OpenApi.Messaging.Webhook` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Messaging.Webhook.svg)](https://www.nuget.org/packages/Line.OpenApi.Messaging.Webhook) | Webhook モデル＋受信グルー（`WebhookRequestParser`＝署名検証＋逆直列化） |
| `Line.OpenApi.Liff` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Liff.svg)](https://www.nuget.org/packages/Line.OpenApi.Liff) | LIFF アプリ管理（`LiffClient` ファサード） |
| `Line.OpenApi.Login` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Login.svg)](https://www.nuget.org/packages/Line.OpenApi.Login) | LINE Login v2.1 + OpenID Connect（`LoginClient` ファサード＝認可 URL／トークン交換／ID トークン・アクセストークン検証／プロフィール／友だち関係） |
| `Line.OpenApi.MiniApp` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.MiniApp.svg)](https://www.nuget.org/packages/Line.OpenApi.MiniApp) | LINE MINI App サービスメッセージ＋アプリ内課金（`MiniAppClient` ファサード＝通知トークン発行/送信・IAP 予約・IAP Webhook 履歴） |
| `Line.OpenApi.Insight` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Insight.svg)](https://www.nuget.org/packages/Line.OpenApi.Insight) | インサイト／統計（`InsightClient` ファサード＝友だち属性・配信数・フォロワー数・メッセージイベント・リッチメニュー統計） |
| `Line.OpenApi.ManageAudience` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.ManageAudience.svg)](https://www.nuget.org/packages/Line.OpenApi.ManageAudience) | オーディエンス管理（`ManageAudienceClient` ファサード＝オーディエンスグループ作成/取得/一覧/削除・click/imp リターゲ・データ系でのファイルによるユーザー ID アップロード） |
| `Line.OpenApi.Module` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Module.svg)](https://www.nuget.org/packages/Line.OpenApi.Module) | パートナー／代理店運用向けモジュールチャネル（`ModuleClient` ファサード＝detach・chat control・attach 済みモジュール一覧） |
| `Line.OpenApi.Shop` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Shop.svg)](https://www.nuget.org/packages/Line.OpenApi.Shop) | ミッションスタンプ送信（`ShopClient` ファサード） |
| `Line.OpenApi.Bot` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Bot.svg)](https://www.nuget.org/packages/Line.OpenApi.Bot) | 便宜メタパッケージ（任意）＝Bot 一式を 1 参照で導入（`Messaging` + `Messaging.Webhook` + `ChannelAccessToken` を束ねる。コードなし・依存束ねのみ） |
| `Line.OpenApi.Extensions.AI` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Extensions.AI.svg)](https://www.nuget.org/packages/Line.OpenApi.Extensions.AI) | LLM tool-calling：Messaging 利用シーンを [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/) の `AIFunction` ツールとして公開（Semantic Kernel／任意の M.E.AI ホスト）。既定で安全・送信は opt-in でゲート付き。独立採番（タグ `ai-v*`）。[`tools/Line.OpenApi.Extensions.AI/README_ja.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/Line.OpenApi.Extensions.AI/README_ja.md) 参照 |

## dotnet ツール

ローカル用のコマンドライン／MCP ツール。[.NET グローバルツール](https://learn.microsoft.com/dotnet/core/tools/global-tools)として配布し、独立採番（タグ `tools-v*`）で公開します。

| ツール | 役割 |
|---|---|
| `Line.OpenApi.Tools` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Tools.svg)](https://www.nuget.org/packages/Line.OpenApi.Tools) | LINE プラットフォームをローカル PC から操作する CLI／MCP グローバルツール（コマンド `line`）：トークン管理・メッセージ送信／Bot 照会・Webhook 開発支援・LIFF／リッチメニュー／insight／audience／shop。同じ機能を CLI サブコマンドと MCP サーバツールの両方で提供。[`tools/README_ja.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/README_ja.md) 参照 |

```sh
dotnet tool install -g Line.OpenApi.Tools
```

## インストール

> 全パッケージ NuGet.org 公開済み（現在 `1.0.0`）。[`Line.OpenApi.*` パッケージ一覧](https://www.nuget.org/packages?q=tags%3A%22LINE-API%22)。

```sh
# Bot 一式（送信＋受信＋トークン発行）をまとめて導入
dotnet add package Line.OpenApi.Bot

# または利用シーン単位で個別に導入
dotnet add package Line.OpenApi.Messaging
dotnet add package Line.OpenApi.Liff
dotnet add package Line.OpenApi.Login
dotnet add package Line.OpenApi.MiniApp
dotnet add package Line.OpenApi.Insight
dotnet add package Line.OpenApi.ManageAudience
dotnet add package Line.OpenApi.Module
dotnet add package Line.OpenApi.Shop
```

## 必要要件

- .NET SDK 10 以降（`dotnet --version` で確認）

## 使い方

### メッセージ送信（`Line.OpenApi.Messaging`）

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

短期トークン（v2.1 JWT アサーション等）を使う場合は、更新型プロバイダを認証プロバイダ注入経路で渡します:

```csharp
services.AddLineMessaging(sp => /* IAuthenticationProvider を返す（Line.OpenApi.ChannelAccessToken の更新型プロバイダ等） */);
```

### LIFF アプリ管理（`Line.OpenApi.Liff`）

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

### LINE Login + OpenID Connect（`Line.OpenApi.Login`）

`LoginClient` はブラウザの認可コードフロー（PKCE 任意）とその後続処理をカバーします。Messaging と異なり、LINE Login は **user access token**（Messaging の channel access token とは別系統の資格情報）で認証し、トークン発行には LINE Login の **チャネル ID＋チャネルシークレット**を使います。

```csharp
using Line.OpenApi.Login;

var login = new LoginClient("LOGIN_CHANNEL_ID", "LOGIN_CHANNEL_SECRET");

// 1) ブラウザを認可 URL へリダイレクト（組立のみ・HTTP は呼ばない）。
var pkce  = LineLoginSecurity.CreatePkceChallenge();
var state = LineLoginSecurity.GenerateState();          // state と pkce.CodeVerifier はセッションに保存
var url   = login.BuildAuthorizationUrl(new AuthorizationUrlParameters
{
    RedirectUri   = "https://app.example.com/callback",
    Scopes        = new[] { "openid", "profile" },
    State         = state,
    Nonce         = "server-generated-nonce",
    CodeChallenge = pkce.CodeChallenge,
});

// 2) コールバックで（state 検証後）認可コードをトークンに交換。
var token = await login.ExchangeCodeAsync("<code>", "https://app.example.com/callback", pkce.CodeVerifier);

// 3) ID トークンを検証（LINE へ委譲）し、user access token でプロフィールを取得。
var claims  = await login.VerifyIdTokenAsync(token!.IdToken!, nonce: "server-generated-nonce");
var profile = await login.GetProfileAsync(token.AccessToken!);
var friend  = await login.GetFriendshipStatusAsync(token.AccessToken!);   // friend.FriendFlag
```

DI: `services.AddLineLogin(o => { o.ChannelId = "…"; o.ChannelSecret = "…"; });`

> ローカルでの ID トークン検証（Web=HS256／ネイティブ・LIFF=ES256+JWKS）は本リリースには含みません。当面は `VerifyIdTokenAsync`（LINE へのサーバ委譲）を使ってください。

### LINE MINI App（`Line.OpenApi.MiniApp`）

`MiniAppClient` は `api.line.me` 上の 2 つの独立機能（LINE MINI App は spec 非公開のため全て手書き）をカバーします。トークンは保持せず呼び出しごとに渡すため、`Line.OpenApi.ChannelAccessToken` にも `Line.OpenApi.Login` にも依存しません。

```csharp
using Line.OpenApi.MiniApp;

var miniApp = new MiniAppClient();

// サービスメッセージ：MINI App 内でのユーザーの操作に応じて通知する。
// liffAccessToken はフロント側の liff.getAccessToken() で取得したもの。
var issued = await miniApp.IssueNotificationTokenAsync("CHANNEL_ACCESS_TOKEN", liffAccessToken);
var sent = await miniApp.SendServiceMessageAsync(
    "CHANNEL_ACCESS_TOKEN", issued!.NotificationToken!, "order-complete_ja",
    new Dictionary<string, string> { ["orderName"] = "Widget" });
// sent.NotificationToken は送信毎に更新されるので、次回呼び出し用に保存する。

// アプリ内課金（IAP）：購入者の user access token で予約する。
var reserved = await miniApp.ReserveProductAsync(
    userAccessToken, clientIp: "203.0.113.1", clientOs: "ios",
    productId: "PRODUCT1", shopProductName: "Gold Pack");

// プラットフォームの購入/返金 Webhook 履歴を取得（過去 7 日分・cursor ページング）。
var events = await miniApp.GetWebhookEventsAsync(
    "CHANNEL_ACCESS_TOKEN", startEpochSeconds, endEpochSeconds, pageSize: 50);
```

DI: `services.AddLineMiniApp();`（必須設定なし。許可ホストの既定を上書きする場合のみ `o => o.AllowedHosts = …` を渡す）

### インサイト／統計（`Line.OpenApi.Insight`）

```csharp
using Line.OpenApi.Insight;

var insight = InsightClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
var followers = await insight.GetNumberOfFollowersAsync("20260715");   // yyyyMMdd
var summary = await insight.GetRichMenuInsightSummaryAsync("RICH_MENU_ID", "20260701", "20260715");
```

DI: `services.AddLineInsight(o => o.ChannelAccessToken = "…");`

### オーディエンス管理（`Line.OpenApi.ManageAudience`）

制御系（`api.line.me`）＋データ系（`api-data.line.me`）。ファイルアップロードはラップ済みで multipart ボディを自分で組む必要はありません。

```csharp
using Line.OpenApi.ManageAudience;

var ma = ManageAudienceClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
using var file = File.OpenRead("user-ids.txt");   // 1 行 1 ユーザー ID / IFA
var created = await ma.UploadUserIdsByFileAsync(file, description: "my audience");
await ma.AddUserIdsByFileAsync(created!.AudienceGroupId!.Value, File.OpenRead("more-ids.txt"));
```

DI: `services.AddLineManageAudience(o => o.ChannelAccessToken = "…");`

### モジュールチャネル（`Line.OpenApi.Module`）

```csharp
using Line.OpenApi.Module;

var module = ModuleClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
var modules = await module.GetModulesAsync(limit: 100);
await module.ReleaseChatControlAsync("CHAT_ID");
```

DI: `services.AddLineModule(o => o.ChannelAccessToken = "…");`
モジュールの attach（`module-attach`。`manager.line.biz` 上で Basic 認証 + PKCE）は本パッケージのスコープ外です。

### ミッションスタンプ（`Line.OpenApi.Shop`）

```csharp
using Line.OpenApi.Shop;
using Line.OpenApi.Shop.Generated.Models;

var shop = ShopClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
await shop.SendMissionStickerAsync(new MissionStickerRequest
{
    To = "USER_ID", ProductType = "STICKER", ProductId = "PRODUCT_ID",
});
```

DI: `services.AddLineShop(o => o.ChannelAccessToken = "…");`

### Webhook 受信（`Line.OpenApi.Messaging.Webhook`）

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
> `WebhookRequestParser.ParseAsync(channelSecret, body, signature)` を使います。
>
> 本文サイズの上限（DoS 対策）は本ヘルパの責務外です。ASP.NET Core の `MaxRequestBodySize` 等、
> 上流で生ボディのサイズ制限を設けてください。

## CLI / MCP ツール（`line`）

ローカル PC から LINE を操作する CLI／MCP ツール `line`（`Line.OpenApi.Tools`）を `tools/` に同梱しています。トークン発行・メッセージ送信・Webhook 開発支援・LIFF 管理を、**CLI サブコマンド**と **MCP サーバのツール**（Claude Desktop / Claude Code から利用）の両方で提供します。

```sh
dotnet tool install -g Line.OpenApi.Tools   # 公開後
line message push --to <id> --text "Hello"
line mcp                                   # MCP サーバとして起動
```

詳細は [`tools/README.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/README.md)（[日本語](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/README_ja.md)）を参照してください。

### LINE Flex Message ビューア（Copilot canvas 拡張）

LINE Flex Message を、LINE アプリに近い見た目でブラウザにライブプレビューできます。`line` MCP ツールは
これを `line_flex_*`（read-only 安全・LINE API/シークレット非使用）として公開します。**同じ**レンダラは
Copilot CLI の canvas 拡張としても提供しており、このリポジトリのフォルダ URL から導入できます。

```
install_extension https://github.com/pierre3/line-openapi-dotnet/tree/main/extensions/line-flex-viewer
```

`line` ツールを使わない場合の代替として、依存パッケージのない `mcp/server.mjs` を Claude
Desktop/Code から利用することもできます。詳細は
[`extensions/line-flex-viewer/`](https://github.com/pierre3/line-openapi-dotnet/tree/main/extensions/line-flex-viewer) を参照してください。

## AI ツール（`Line.OpenApi.Extensions.AI`）

`Line.OpenApi.Extensions.AI` は Messaging 利用シーンを [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/) の `AIFunction` ツールとしてラップし、Semantic Kernel や任意の Microsoft.Extensions.AI ホストから LLM エージェントが LINE を **アプリ内 in-process** で操作できるようにします（別プロセスで動く上記 CLI/MCP ツールを補完する関係です）。依存は `Line.OpenApi.Messaging` と `Microsoft.Extensions.AI.Abstractions` の 2 本のみです。

```csharp
using Line.OpenApi.Extensions.AI;
using Line.OpenApi.Messaging;

var line = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

// 安全側の既定：読み取り専用ツールのみ（bot info / quota / profile / message-validate）。
IReadOnlyList<AIFunction> readTools = LineMessagingAiTools.CreateReadOnly(line);

// 送信は明示 opt-in、かつゲート越し。
IReadOnlyList<AIFunction> tools = LineMessagingAiTools.Create(line, new LineAiToolOptions
{
    EnableSending = true,                 // push / multicast / reply を有効化（既定 false）
    AllowBroadcast = false,               // broadcast は最大ブラスト半径＝独立 opt-in
    SendPolicy = (ctx, ct) =>             // ブラスト半径を制限（操作種別 / 宛先 / 件数）
        new(ctx.Operation != LineSendOperation.Broadcast),
    BeforeSend = (ctx, ct) => /* human-in-the-loop / 監査。ctx.MessagesJson を検査 */ new(true),
});

// Semantic Kernel は Microsoft.Extensions.AI の関数をそのまま消費できます:
// kernel.Plugins.AddFromFunctions("Line", tools);
```

**安全モデル。** 送信は既定オフ、broadcast は独立 opt-in、`SendPolicy` と `BeforeSend` が全送信をゲートし、いずれも生成時に開発者が設定します＝**ツール引数には一切出ない**ためモデルからは変更不可。戻り値は非機密で、チャネルアクセストークンは戻り値・説明・例外のいずれにも出ません。レート／累積回数の制限はホスト側パイプラインの責務です。メッセージ本文は `SendPolicy` / `BeforeSend` に渡り `LineSendContext.MessagesJson`（`LineSendRefusedException` 上を含む）に保持され、read ツールの戻り値（表示名など）は LLM プロバイダに渡るため、ログや監査証跡ではツール引数・戻り値を PII として扱ってください。

## サンプル

`samples/` に動くデモアプリを同梱しています（NuGet パッケージには含みません）。**既定はオフライン**で、環境変数を設定すると実 API に接続します。

- **`Line.OpenApi.Samples.Console`** — 送信 / LIFF 管理 / トークン発行 / Webhook パース（`dotnet run -- webhook` は資格情報不要で動作）
- **`Line.OpenApi.Samples.Webhook`** — minimal API の Webhook 受信＋エコー返信（dev トンネルでライブデモ）
- **`Line.OpenApi.Samples.Login`** — LINE Login + OpenID Connect 認可コードフロー（PKCE・localhost コールバック）
- **`Line.OpenApi.Samples.Ai`** — LLM tool-calling：スクリプト（または実モデル）が `Line.OpenApi.Extensions.AI` のツールを安全ゲート越しに操作（完全オフラインで動作）

実行手順・環境変数・dev トンネル設定は [`samples/README_ja.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/samples/README_ja.md) を参照してください。

## チュートリアル

実地で一通り動かす手引きとして、コンパニオンプロジェクト
**[pierre3/line-companion-bot](https://github.com/pierre3/line-companion-bot)** を用意しています。
バーチャルペット育成 LINE ボットと LINE MINI App ショップを組み合わせ、`Line.OpenApi.*` ライブラリ群を
一体で動かす題材です（メッセージ送受信 / Webhook / リッチメニュー / MINI App サービスメッセージ＋アプリ内課金）。
バイリンガルのチュートリアルが `dotnet new` からエンドツーエンドの一連の流れまでを 1 章ずつ実装していき、
LINE Developers Console の設定・dev トンネルの構成・`line` CLI によるリッチメニュー登録までカバーします。

- チュートリアル（英語）: [`docs/manual/en/`](https://github.com/pierre3/line-companion-bot/blob/main/docs/manual/en/README.md)
- チュートリアル（日本語）: [`docs/manual/ja/`](https://github.com/pierre3/line-companion-bot/blob/main/docs/manual/ja/README.md)

## ショーケース

このライブラリを使って作られたアプリ:

- **[pierre3/line-hf-bot](https://github.com/pierre3/line-hf-bot)** — Hugging Face モデルで AI チャット・画像生成/編集・動画生成を行う LINE bot（ASP.NET / .NET 10）。`Line.OpenApi.Bot` パッケージ（メッセージ送受信 / Webhook / リッチメニュー）を利用し、Docker イメージで配布。

## ソースからのビルド

リポジトリルートで:

```sh
dotnet build            # net10.0 単一
dotnet test             # webhook 多態含め既定で全実行（opt-in フラグ不要）
```

### 仕様からの再生成（任意）

OpenAPI 仕様（`openapi/` に同梱）から Kiota クライアントを再生成する場合のみ、Kiota CLI が必要です:

```sh
dotnet tool install --global Microsoft.OpenApi.Kiota

./scripts/generate.ps1        # Windows / PowerShell
bash scripts/generate.sh      # macOS / Linux
```

生成コードは `src/**/Generated/`（`kiota-lock.json` はコミット対象）。`Microsoft.Kiota.Bundle` の版は `Directory.Build.props` の `KiotaBundleVersion` で一元管理しています（現状 2.0.0）。

### 上流仕様への追従

`openapi/` に同梱している spec は [`line/line-openapi`](https://github.com/line/line-openapi) の**ピン留めスナップショット**です。上流はタグ/リリースを持たず `info.version` も固定値のため、取り込み世代は **上流コミット SHA** で管理します（`openapi/upstream-manifest.json` に ref・取得日・spec 別 LF 正規化 sha256 を記録）。

週次ワークフロー（[`.github/workflows/spec-sync.yml`](.github/workflows/spec-sync.yml)）がドリフトを検知し、追跡 Issue を立て、再生成した**下書き PR** を自動作成します（マージは常に人が判断）。ローカルでは:

```sh
pwsh scripts/check-spec-drift.ps1        # manifest 基準でドリフト検知（ドリフト時 exit 1）
pwsh scripts/generate.ps1 -Update        # 上流 HEAD で再取得→正規化→manifest 更新→再生成
```

ハッシュ/差分の前に改行を LF 正規化します（`.gitattributes` 参照）。生バイトのまま LF の上流と比較すると全ファイルが誤ってドリフト判定されるためです。

## ドキュメント

- **📖 ユーザーマニュアル（公開中）: https://pierre3.github.io/line-openapi-dotnet/** — 概念記事（英語 / 日本語）＋英語 API リファレンス。
- 変更履歴: [`CHANGELOG_ja.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/CHANGELOG_ja.md)
- 設計方針: [`docs/LINE-dotnet-client-design.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/docs/LINE-dotnet-client-design.md)
- マニュアルは [DocFX](https://dotnet.github.io/docfx/) で `docs/manual/` に生成し、`docs` ワークフローで GitHub Pages へ発行します。DocFX はローカルツール（`.config/dotnet-tools.json`）としてピン留め済み。ローカルビルドは以下:

```sh
dotnet tool restore                              # 初回のみ（DocFX を復元）
dotnet docfx docs/manual/docfx.json              # metadata 抽出 + サイトビルド → docs/manual/_site/
dotnet docfx docs/manual/docfx.json --serve      # ローカルプレビュー（http://localhost:8080）
```

API リファレンスは手書き公開表面の XML doc コメントから英語で自動生成します（生成物 `Line.*.Generated` は `filterConfig.yml` で除外）。生成物（`docs/manual/api/`・`docs/manual/_site/`）は Git 追跡外です。詳細は設計 §13 を参照。

## プロジェクト構成

```
（リポジトリルート）
├── LineOpenApi.slnx             # ソリューション
├── Directory.Build.props        # 共通 TFM(net10.0)/nullable/Kiota版
├── openapi/                     # 仕様スナップショット
├── scripts/                     # Kiota 生成・パッケージ検証スクリプト
├── src/
│   ├── Line.OpenApi.Core/               # 認証プロバイダ・署名検証・許可ホスト（手書き）
│   ├── Line.OpenApi.ChannelAccessToken/ # トークン発行（form-urlencoded 込み生成＋手書きヘルパ）
│   ├── Line.OpenApi.Messaging/          # 制御系+データ系2クライアント + MessagingClient ファサード
│   ├── Line.OpenApi.Messaging.Webhook/  # webhook モデル + WebhookRequestParser（受信グルー）
│   ├── Line.OpenApi.Liff/               # LIFF + LiffClient ファサード
│   ├── Line.OpenApi.Login/              # LINE Login v2.1 + OIDC（spec 非存在の手書き）+ LoginClient ファサード
│   ├── Line.OpenApi.MiniApp/            # MINI App サービスメッセージ + IAP（spec 非存在の手書き）+ MiniAppClient ファサード
│   ├── Line.OpenApi.Insight/            # インサイト／統計 + InsightClient ファサード
│   ├── Line.OpenApi.ManageAudience/     # オーディエンス管理（制御系＋データ系）+ ManageAudienceClient ファサード
│   ├── Line.OpenApi.Module/             # モジュールチャネル + ModuleClient ファサード
│   ├── Line.OpenApi.Shop/               # ミッションスタンプ + ShopClient ファサード
│   └── Line.OpenApi.Bot/                # 便宜メタパッケージ（依存束ねのみ・コードなし）
├── tools/
│   ├── Line.OpenApi.Tools/              # CLI / MCP ツール（コマンド名 line）
│   ├── Line.OpenApi.Extensions.AI/      # LLM tool-calling 向け Microsoft.Extensions.AI ツール
│   └── shared/                          # Tools と Extensions.AI の共有ソース（非パッケージ）
├── samples/                     # 同梱デモアプリ（コンソール / Webhook Web API）
├── tests/                       # 手書き表面のテスト（署名/受信/ルーティング/DI/snapshot 等）
└── docs/                        # 設計・レビュー記録・ユーザーマニュアル（manual/）
```

## ライセンス

[MIT](https://github.com/pierre3/line-openapi-dotnet/blob/main/LICENSE) © pierre3
