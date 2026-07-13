# Sending Messages

@Line.Messaging.MessagingClient is the facade for the Messaging API. It unifies two
Kiota clients — the **control plane** (`api.line.me`, for sending and most operations) and the
**data plane** (`api-data.line.me`, for binary content) — so you do not have to think about
which host a call targets.

```csharp
using Line.Messaging;
using Line.Messaging.Generated.Api.Models;

var client = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

- `client.Api` — control-plane builders (`MessagingApiClient`).
- `client.Blob` — data-plane builders (`MessagingBlobApiClient`).

## Push a message

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

## Reply to an event

Use the reply token you received from a webhook event (see [Receiving Webhooks](webhook.md)):

```csharp
await client.Api.V2.Bot.Message.Reply.PostAsync(new ReplyMessageRequest
{
    ReplyToken = replyToken,
    Messages = new() { new TextMessage { Text = "Thanks!" } },
});
```

## Fetch message content (data plane)

Binary content (for example an image a user sent) lives on the data-plane host. `client.Blob`
routes there automatically — no host handling on your part:

```csharp
Stream stream = await client.Blob.V2.Bot.Message["<messageId>"].Content.GetAsync();
```

## Building messages and actions

Messages and actions are strongly typed. Note one Kiota naming quirk: the polymorphic **action
base type** is generated as `ActionObject` (to avoid a clash with `System.Action`). Concrete
actions keep their natural names:

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

## Why a facade instead of one generated client?

The Messaging spec mixes two base URLs: control operations on `api.line.me` and blob content on
`api-data.line.me`. Kiota builds one client per leading server, so the library generates two
clients and the facade sets the data-plane `BaseUrl` to `api-data.line.me` before construction.
Exposing the generated builders directly (rather than wrapping every endpoint) is deliberate:
the Messaging surface is large, so complete convenience-method coverage would be impractical.
Contrast this with [LIFF](liff.md), whose small surface *is* fully wrapped.
