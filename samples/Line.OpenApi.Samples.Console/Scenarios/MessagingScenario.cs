using System.Collections.Generic;
using System.Threading.Tasks;
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;

using Con = System.Console;

namespace Line.OpenApi.Samples.Console.Scenarios;

/// <summary>
/// Shows how to send a push message with <see cref="MessagingClient"/>. Offline it prints the
/// request that would be sent; with a token (and a destination user id) it performs a real push.
/// </summary>
internal static class MessagingScenario
{
    public static async Task RunAsync()
    {
        Con.WriteLine("== Messaging: push a text message ==\n");

        // Build the request exactly as you would in production. TextMessage derives from the
        // polymorphic base Message, so it goes straight into the Messages list.
        var request = new PushMessageRequest
        {
            To = DemoEnv.ToUserId ?? "U0123456789abcdef0123456789abcdef",
            Messages = new List<Message>
            {
                new TextMessage { Text = "Hello from Line.OpenApi .NET samples 👋" },
            },
        };

        Con.WriteLine($"  to       : {request.To}");
        Con.WriteLine($"  messages : 1 text message");
        Con.WriteLine($"  endpoint : POST https://api.line.me/v2/bot/message/push\n");

        if (!DemoEnv.HasToken)
        {
            Con.WriteLine("[offline] LINE_CHANNEL_ACCESS_TOKEN is not set — request not sent.");
            Con.WriteLine("          Set LINE_CHANNEL_ACCESS_TOKEN (and LINE_TO_USER_ID) to send for real.");
            return;
        }

        if (DemoEnv.ToUserId is null)
        {
            Con.WriteLine("[skip] LINE_TO_USER_ID is not set — refusing to push to a placeholder id.");
            return;
        }

        var client = MessagingClient.CreateWithStaticToken(DemoEnv.ChannelAccessToken!);
        await client.Api.V2.Bot.Message.Push.PostAsync(request);
        Con.WriteLine("[live] Push message sent.");
    }
}
