using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Line.OpenApi.Messaging.Webhook;
using Line.OpenApi.Messaging.Webhook.Generated.Models;

using Con = System.Console;

namespace Line.OpenApi.Samples.Console.Scenarios;

/// <summary>
/// Fully offline demo of the receive entry point <see cref="WebhookRequestParser"/>: it signs a
/// bundled sample payload with a demo secret (the same HMAC-SHA256 LINE uses), parses it — which
/// validates the signature and deserializes the body — and then branches on the reconstructed
/// event types. No network and no real credentials are involved.
/// </summary>
internal static class WebhookParseScenario
{
    private const string DemoChannelSecret = "demo-channel-secret";

    // A representative webhook body: one text message event and one follow event.
    private const string SamplePayload = """
    {
      "destination": "U0123456789abcdef",
      "events": [
        {
          "type": "message",
          "message": { "type": "text", "id": "14353798921116", "text": "Hello, world" },
          "timestamp": 1625665242211,
          "source": { "type": "user", "userId": "U80696558e1aa831..." },
          "replyToken": "757913772c4646b784d4b7ce46d12671",
          "mode": "active",
          "webhookEventId": "01FZ74A0TDDPYRVKNK77XKC3ZR",
          "deliveryContext": { "isRedelivery": false }
        },
        {
          "type": "follow",
          "timestamp": 1625665242212,
          "source": { "type": "user", "userId": "U80696558e1aa831..." },
          "replyToken": "8cf9239d56244f4197887e939187e19e",
          "mode": "active",
          "webhookEventId": "01FZ74ASS536FW1JV0N6TNCC7T",
          "deliveryContext": { "isRedelivery": false }
        }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        Con.WriteLine("== Webhook: parse a sample payload (offline) ==\n");

        var body = Encoding.UTF8.GetBytes(SamplePayload);
        var signature = Sign(DemoChannelSecret, body);

        Con.WriteLine($"  demo secret : {DemoChannelSecret}");
        Con.WriteLine($"  x-line-signature (computed): {signature}\n");

        var parser = new WebhookRequestParser(DemoChannelSecret);

        // Throws WebhookSignatureException on a bad signature, WebhookPayloadException on a bad body.
        CallbackRequest callback = await parser.ParseAsync(body, signature);

        Con.WriteLine($"[parsed] destination = {callback.Destination}, {callback.Events?.Count ?? 0} event(s):");

        foreach (var ev in callback.Events ?? new())
        {
            // Events are already reconstructed to their concrete type by the discriminator;
            // unknown types arrive as the base Event.
            switch (ev)
            {
                case MessageEvent m when m.Message is TextMessageContent t:
                    Con.WriteLine($"  - message (text): \"{t.Text}\"");
                    break;
                case FollowEvent:
                    Con.WriteLine("  - follow (a user added the bot)");
                    break;
                default:
                    Con.WriteLine($"  - {ev.GetType().Name} (not handled in this sample)");
                    break;
            }
        }

        Con.WriteLine("\nTip: to receive real webhooks, run the Line.OpenApi.Samples.Webhook web app behind a dev tunnel.");
    }

    // Same computation LINE performs: Base64(HMAC-SHA256(channelSecret, rawBody)).
    private static string Sign(string channelSecret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }
}
