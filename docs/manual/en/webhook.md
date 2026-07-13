# Receiving Webhooks

@Line.Messaging.Webhook.WebhookRequestParser bundles two steps into one call:

1. verify the `x-line-signature` header against the raw request body, and
2. deserialize the body into a strongly-typed `CallbackRequest`.

On failure it throws: @Line.Messaging.Webhook.WebhookSignatureException when the signature is
invalid, and @Line.Messaging.Webhook.WebhookPayloadException when the body cannot be
deserialized (both derive from @Line.Messaging.Webhook.WebhookException).

## Register the parser

```csharp
using Line.Messaging.Webhook.DependencyInjection;

services.AddLineWebhook(o => o.ChannelSecret = "CHANNEL_SECRET");
// resolve: sp.GetRequiredService<WebhookRequestParser>()
```

Webhook receiving performs no outbound HTTP, so this registration needs no
`IHttpClientFactory`.

## Handle a request (ASP.NET Core)

**Reading the raw body and extracting the signature header are your responsibility.** The
signature is computed over the raw bytes, so you must read the body *before* model binding.

```csharp
using Line.Messaging.Webhook;
using Line.Messaging.Webhook.Generated.Models;

app.MapPost("/webhook", async (HttpRequest request, WebhookRequestParser parser) =>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    var body = ms.ToArray();                       // the exact bytes that were signed
    var signature = request.Headers["x-line-signature"];

    CallbackRequest callback;
    try
    {
        callback = await parser.ParseAsync(body, signature);
    }
    catch (WebhookSignatureException) { return Results.Unauthorized(); }  // invalid signature
    catch (WebhookPayloadException)   { return Results.BadRequest(); }    // invalid body

    // Events are already reconstructed into concrete types by the `type` discriminator
    // (unknown types arrive as the base Event). Branch on the caller side:
    foreach (var ev in callback.Events!)
    {
        switch (ev)
        {
            case MessageEvent m when m.Message is TextMessageContent t:
                Console.WriteLine($"text: {t.Text}");
                break;
            case FollowEvent:      /* user added the bot */   break;
            case PostbackEvent p:  /* p.Postback!.Data */      break;
            // unknown events arrive as the base Event type (safe to ignore)
        }
    }
    return Results.Ok();
});
```

## Multi-tenant secrets

When each channel has its own secret, use the static overload and pass the secret per call:

```csharp
CallbackRequest callback =
    await WebhookRequestParser.ParseAsync(channelSecret, body, signature);
```

## Notes

- **Body size limits (DoS protection) are out of scope for this helper.** Enforce a raw-body
  size limit upstream (for example ASP.NET Core's `MaxRequestBodySize`).
- The parser deserializes without relying on Kiota's global serializer registry, so it works
  standalone even in an app that never constructed the Messaging client.
