# メッセージ送信

@Line.OpenApi.Messaging.MessagingClient は Messaging API のファサードです。2 つの Kiota クライアント —
**制御系**（`api.line.me`、送信と大半の操作用）と**データ系**（`api-data.line.me`、バイナリ
コンテンツ用）— を統合し、どのホストに向かう呼び出しかを意識せずに済むようにします。

```csharp
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;

var client = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

- `client.Api` — 制御系ビルダー（`MessagingApiClient`）。
- `client.Blob` — データ系ビルダー（`MessagingBlobApiClient`）。

## メッセージのプッシュ

```csharp
await client.Api.V2.Bot.Message.Push.PostAsync(new PushMessageRequest
{
    To = "U0123456789abcdef...",
    Messages = new()
    {
        new TextMessage { Text = "Hello, world" },
    },
});
```

## イベントへの応答

Webhook イベントから受け取った応答トークンを使います（[Webhook 受信](webhook.md)参照）:

```csharp
await client.Api.V2.Bot.Message.Reply.PostAsync(new ReplyMessageRequest
{
    ReplyToken = replyToken,
    Messages = new() { new TextMessage { Text = "Thanks!" } },
});
```

## メッセージコンテンツの取得（データ系）

バイナリコンテンツ（ユーザーが送った画像など）はデータ系ホストにあります。`client.Blob` が
自動的にそちらへルーティングするため、ホストの扱いは不要です:

```csharp
Stream stream = await client.Blob.V2.Bot.Message["<messageId>"].Content.GetAsync();
```

## メッセージとアクションの構築

メッセージとアクションは強く型付けされています。Kiota の命名の癖に注意してください: 多態の
**アクション基底型**は（`System.Action` との衝突回避のため）`ActionObject` として生成されます。
具体アクションは自然な名前のままです:

```csharp
var buttons = new TemplateMessage
{
    AltText = "menu",
    Template = new ButtonsTemplate
    {
        Text = "Pick one",
        Actions = new()
        {
            new MessageAction  { Label = "Say hi", Text = "hi" },
            new PostbackAction { Label = "Buy",     Data = "action=buy" },
            new URIAction      { Label = "Open",    Uri  = "https://example.com" },
        },
    },
};
```

## なぜ単一の生成クライアントではなくファサードなのか

Messaging 仕様は 2 つの base URL を混在させます: 制御操作は `api.line.me`、blob コンテンツは
`api-data.line.me` です。Kiota は先頭 server ごとに 1 クライアントを構築するため、本ライブラリは
2 つのクライアントを生成し、ファサードが構築前にデータ系の `BaseUrl` を `api-data.line.me` に
設定します。（全エンドポイントをラップせず）生成ビルダーを直接公開しているのは意図的です:
Messaging の表面は大きく、便利メソッドで完全に被覆するのは非現実的だからです。小さな表面を
完全にラップしている [LIFF](liff.md) と対照的です。
