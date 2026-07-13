# Webhook 受信

@Line.Messaging.Webhook.WebhookRequestParser は 2 つのステップを 1 呼び出しに束ねます:

1. `x-line-signature` ヘッダをリクエスト生ボディに対して検証し、
2. 本文を強く型付けされた `CallbackRequest` へ逆直列化します。

失敗時には例外を投げます: 署名が不正なら @Line.Messaging.Webhook.WebhookSignatureException、
本文を逆直列化できなければ @Line.Messaging.Webhook.WebhookPayloadException（どちらも
@Line.Messaging.Webhook.WebhookException を基底とします）。

## パーサの登録

```csharp
using Line.Messaging.Webhook.DependencyInjection;

services.AddLineWebhook(o => o.ChannelSecret = "CHANNEL_SECRET");
// 解決: sp.GetRequiredService<WebhookRequestParser>()
```

Webhook 受信は送信 HTTP を伴わないため、この登録に `IHttpClientFactory` は不要です。

## リクエストの処理（ASP.NET Core）

**生ボディの取得と署名ヘッダの抽出は利用側の責務です。** 署名は生バイトに対して計算されるため、
モデルバインドの*前*に本文を読む必要があります。

```csharp
using Line.Messaging.Webhook;
using Line.Messaging.Webhook.Generated.Models;

app.MapPost("/webhook", async (HttpRequest request, WebhookRequestParser parser) =>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    var body = ms.ToArray();                       // 署名対象と同一のバイト列
    var signature = request.Headers["x-line-signature"];

    CallbackRequest callback;
    try
    {
        callback = await parser.ParseAsync(body, signature);
    }
    catch (WebhookSignatureException) { return Results.Unauthorized(); }  // 署名 NG
    catch (WebhookPayloadException)   { return Results.BadRequest(); }    // 本文 NG

    // イベントは `type` discriminator により具象型へ復元済みです
    // （未知の type は基底 Event として届きます）。以降の分岐は利用側で行います:
    foreach (var ev in callback.Events!)
    {
        switch (ev)
        {
            case MessageEvent m when m.Message is TextMessageContent t:
                Console.WriteLine($"text: {t.Text}");
                break;
            case FollowEvent:      /* 友だち追加 */          break;
            case PostbackEvent p:  /* p.Postback!.Data */    break;
            // 未知イベントは基底 Event 型で届く（無視して問題ない）
        }
    }
    return Results.Ok();
});
```

## マルチテナントのシークレット

チャネルごとにシークレットが異なる場合は、静的オーバーロードでシークレットを都度渡します:

```csharp
CallbackRequest callback =
    await WebhookRequestParser.ParseAsync(channelSecret, body, signature);
```

## 注意

- **本文サイズの上限（DoS 対策）は本ヘルパの責務外です。** 上流で生ボディのサイズ制限を設けて
  ください（例: ASP.NET Core の `MaxRequestBodySize`）。
- パーサは Kiota のグローバルなシリアライザレジストリに依存せず逆直列化するため、Messaging
  クライアントを一度も構築していないアプリでも単独で動作します。
