# LINE MINI App

@Line.OpenApi.MiniApp.MiniAppClient is the facade for the LINE MINI App server REST surface.
LINE publishes no OpenAPI spec for LINE MINI App, so this client is hand-written on top of the
Kiota runtime.

> **Credential note.** Like `LoginClient`, tokens are taken as plain string arguments per call
> rather than stored, so this package depends on neither `Line.OpenApi.ChannelAccessToken` nor
> `Line.OpenApi.Login`. All REST calls target `api.line.me`.

```csharp
using Line.OpenApi.MiniApp;

var miniApp = new MiniAppClient();
```

The client covers two independent feature areas.

## 1. Service messages

Notify a user in response to an action they took in the MINI App. Requires a channel access
token — **stateless/short-lived only**; long-lived v2.1 tokens are rejected by these endpoints.

First, issue a notification token using the LIFF access token obtained by the front-end's
`liff.getAccessToken()`:

```csharp
NotifierToken? issued = await miniApp.IssueNotificationTokenAsync(
    "CHANNEL_ACCESS_TOKEN", liffAccessToken);

string token = issued!.NotificationToken!;   // valid 1 year; up to 5 sends per user action
```

Then send the message with a reviewed template and its parameters:

```csharp
NotifierToken? sent = await miniApp.SendServiceMessageAsync(
    "CHANNEL_ACCESS_TOKEN",
    token,
    templateName: "order-complete_en",       // {template-name}_{BCP-47 language}
    parameters: new Dictionary<string, string> { ["orderName"] = "Widget" });

token = sent!.NotificationToken!;   // renewed on every send — save it for the next call
```

> Templates require review by LY Corporation before production use. See LINE's
> [service message documentation](https://developers.line.biz/en/docs/line-mini-app/develop/service-messages/)
> for template format and character limits.

## 2. In-app purchase (IAP)

Reserve a purchase with the **purchasing user's user access token**:

```csharp
IapReserveResult? reserved = await miniApp.ReserveProductAsync(
    userAccessToken,
    clientIp: "203.0.113.1",
    clientOs: "ios",             // "ios" or "android"
    productId: "PRODUCT1",
    shopProductName: "Gold Pack" /* max 20 UTF-16 chars, no emoji/symbols */);

string orderId = reserved!.OrderId!;   // hand this to the in-app purchase SDK
```

Read the platform's purchase/refund webhook history (past 7 days, cursor-paginated) with a
channel access token:

```csharp
MiniAppWebhookEventPage? page = await miniApp.GetWebhookEventsAsync(
    "CHANNEL_ACCESS_TOKEN",
    startEpochSeconds, endEpochSeconds,
    pageSize: 50, cursor: null, status: "SUCCESS");

foreach (var entry in page!.Events!)
{
    MiniAppWebhookEvent ev = entry.Event!;
    // ev.Type is "purchaseComplete" or "refundComplete"; both share the same field shape.
}

string? nextCursor = page.NextCursor;   // pass to the next call, or null when done
```

## Errors

Non-2xx responses are thrown as typed exceptions (both derive from `ApiException`, so the HTTP
status code is preserved):

- Service-message endpoints throw `NotifierErrorResponse` (`Message`, `Details`).
- IAP endpoints throw `IapErrorResponse` (`ErrorCode` — for example `PRODUCT_ID_NOT_FOUND`,
  `BLOCKED_USER`, `TERMS_AGREEMENT_ERROR` — plus `Message` and `Details`).

## Dependency injection

```csharp
using Line.OpenApi.MiniApp.DependencyInjection;

services.AddLineMiniApp();
// resolve: sp.GetRequiredService<MiniAppClient>()
```

No configuration is required at registration time (tokens are supplied per call). Pass
`o => o.AllowedHosts = […]` only to override the default host allow list (`api.line.me`).

See [Dependency Injection & Hosting](di-and-hosting.md) for how the shared `HttpClient` and the
Kiota default middleware (including the CVE-fixed redirect handler) are wired.
