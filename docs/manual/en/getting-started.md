# Getting Started

This library lets a .NET application call the LINE Messaging API, manage LIFF apps, and
receive webhooks with strongly-typed models.

## Prerequisites

- .NET SDK 10 or later (`dotnet --version`).
- A LINE channel and its credentials:
  - a **channel access token** (for calling the Messaging / LIFF APIs), and/or
  - a **channel secret** (for verifying incoming webhook signatures).

## Packages

Reference the packages for the use cases you need. All of them depend on `Line.OpenApi.Core`.

| Package | Use it for |
|---|---|
| `Line.OpenApi.Messaging` | Sending push/reply messages, fetching message content. |
| `Line.OpenApi.Messaging.Webhook` | Receiving and verifying webhook events. |
| `Line.OpenApi.Liff` | Creating and managing LIFF apps. |
| `Line.OpenApi.ChannelAccessToken` | Issuing short-lived / stateless channel access tokens. |

## Your first message

The quickest way to send a message is with a long-lived channel access token:

```csharp
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;

var client = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

await client.Api.V2.Bot.Message.Push.PostAsync(new PushMessageRequest
{
    To = "U0123456789abcdef...",
    Messages = new()
    {
        new TextMessage { Text = "Hello, world" },
    },
});
```

`CreateWithStaticToken` is a convenience for quick starts and simple apps. For production,
prefer the [dependency-injection setup](di-and-hosting.md), which shares an
`IHttpClientFactory`-managed handler pool and applies Kiota's default middleware.

## Where to go next

- [Authentication](authentication.md) — static, JWT, and stateless tokens, and the refreshing provider.
- [Sending Messages](messaging.md) — push/reply/multicast and fetching content.
- [Receiving Webhooks](webhook.md) — signature verification and event dispatch.
- [LIFF App Management](liff.md) — list/add/update/delete LIFF apps.
- [Dependency Injection & Hosting](di-and-hosting.md) — the recommended wiring.
- [Security](security.md) — allowed hosts, signature validation, secret handling.

## A note on the generated surface

The fluent builder paths such as `client.Api.V2.Bot.Message.Push.PostAsync(...)` come from the
Kiota-generated code. This is an intentional "opaque box": you compose requests through those
builders, and the [API reference](xref:Line.OpenApi.Messaging) documents the hand-written facades that give you
access to them (browse it from the top navigation bar). One naming quirk to know up front:
Kiota renames the polymorphic action base type to `ActionObject` (to avoid clashing with
`System.Action`); concrete actions such as `MessageAction` / `PostbackAction` / `URIAction`
keep their natural names.
