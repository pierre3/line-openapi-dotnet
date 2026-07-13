using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.DependencyInjection;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Line.OpenApi.Messaging.Webhook;
using Line.OpenApi.Messaging.Webhook.DependencyInjection;
using Line.OpenApi.Messaging.Webhook.Generated.Models;

// LINE OpenApi .NET — webhook receiver sample (minimal API).
//
// A classic echo bot for a live demo over a dev tunnel: message your LINE bot and it echoes the
// text back. It validates the x-line-signature and deserializes the body with
// WebhookRequestParser, then replies via MessagingClient using the event's reply token.
//
// Configuration (environment variables; both optional so the app always starts):
//   LINE_CHANNEL_SECRET        signature validation key  -> enables POST /webhook
//   LINE_CHANNEL_ACCESS_TOKEN  long-lived token          -> enables echo replies
//
// See samples/README.md for dev tunnel setup and wiring the webhook URL in the LINE console.

var builder = WebApplication.CreateBuilder(args);

var channelSecret = Environment.GetEnvironmentVariable("LINE_CHANNEL_SECRET");
var channelAccessToken = Environment.GetEnvironmentVariable("LINE_CHANNEL_ACCESS_TOKEN");

// Register the receive helper only when a secret is configured. AddLineWebhook uses
// ValidateOnStart(), which would otherwise fail startup; keeping the app startable lets you run
// it and see the health endpoint before credentials are in place.
if (!string.IsNullOrWhiteSpace(channelSecret))
{
    builder.Services.AddLineWebhook(o => o.ChannelSecret = channelSecret);
}

// Register the sender only when a token is configured; replies are skipped otherwise.
if (!string.IsNullOrWhiteSpace(channelAccessToken))
{
    builder.Services.AddLineMessaging(o => o.ChannelAccessToken = channelAccessToken);
}

var app = builder.Build();

var hasSecret = !string.IsNullOrWhiteSpace(channelSecret);
var hasToken = !string.IsNullOrWhiteSpace(channelAccessToken);

// Health / sanity endpoint (handy to confirm the dev tunnel reaches the app).
app.MapGet("/", () => Results.Ok(new
{
    service = "Line.OpenApi.Samples.Webhook",
    webhook = hasSecret ? "enabled" : "disabled (set LINE_CHANNEL_SECRET)",
    reply = hasToken ? "enabled" : "disabled (set LINE_CHANNEL_ACCESS_TOKEN)",
}));

app.MapPost("/webhook", async (
    HttpRequest request,
    [FromServices] WebhookRequestParser? parser,
    [FromServices] MessagingClient? messaging) =>
{
    if (parser is null)
        return Results.Problem("LINE_CHANNEL_SECRET is not configured.", statusCode: 503);

    // Read the raw body bytes: the signature is computed over these exact bytes, so read them
    // before any model binding.
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    var signature = request.Headers["x-line-signature"];

    CallbackRequest callback;
    try
    {
        callback = await parser.ParseAsync(body, signature);
    }
    catch (WebhookSignatureException) { return Results.Unauthorized(); }
    catch (WebhookPayloadException) { return Results.BadRequest(); }

    // Reply to each text message with the same text (echo). Requires a configured sender.
    foreach (var ev in callback.Events ?? new())
    {
        if (ev is MessageEvent { Message: TextMessageContent text } message &&
            messaging is not null &&
            !string.IsNullOrEmpty(message.ReplyToken))
        {
            try
            {
                await messaging.Api.V2.Bot.Message.Reply.PostAsync(new ReplyMessageRequest
                {
                    ReplyToken = message.ReplyToken,
                    Messages = new List<Message>
                    {
                        new TextMessage { Text = $"echo: {text.Text}" },
                    },
                });
            }
            catch (Exception ex)
            {
                // A reply can fail (e.g. an expired reply token — valid for ~1 minute). Log it
                // but still return 200 below: LINE retries any non-2xx response, which would
                // duplicate deliveries. "Absorb downstream failures and always ack" is the key
                // idiom for a webhook receiver.
                app.Logger.LogWarning(ex, "Failed to reply to a webhook message event.");
            }
        }
    }

    // Always 200 quickly: LINE retries non-2xx and times out slow responses.
    return Results.Ok();
});

app.Run();
