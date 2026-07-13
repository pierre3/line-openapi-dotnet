using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Line.OpenApi.Messaging.Webhook;
using Line.OpenApi.Messaging.Webhook.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Line.OpenApi.Tests;

// DI integration verification for Webhook. Confirms that the WebhookRequestParser registered via AddLineWebhook:
//  - can be resolved
//  - can actually validate the signature and deserialize with the configured channel secret
//  - is idempotent (no duplication when registered multiple times) / fails validation when the secret is not set
// No HTTP involved.
public class WebhookDiIntegrationTests
{
    private const string ChannelSecret = "di-channel-secret";
    private const string Payload =
        "{\"destination\":\"U0\",\"events\":[]}";

    private static string Sign(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }

    [Fact]
    public async Task AddLineWebhook_Resolves_And_Parses()
    {
        var services = new ServiceCollection();
        services.AddLineWebhook(o => o.ChannelSecret = ChannelSecret);
        using var sp = services.BuildServiceProvider();

        var parser = sp.GetRequiredService<WebhookRequestParser>();

        var body = Encoding.UTF8.GetBytes(Payload);
        var callback = await parser.ParseAsync(body, Sign(ChannelSecret, body));
        Assert.Equal("U0", callback.Destination);
    }

    [Fact]
    public async Task AddLineWebhook_MultipleRegistrations_NotDuplicated_And_LastSecretWins()
    {
        var services = new ServiceCollection();
        services.AddLineWebhook(o => o.ChannelSecret = "S1");
        services.AddLineWebhook(o => o.ChannelSecret = "S2"); // parser registration is not duplicated

        // The parser's service registration is first-wins and not duplicated.
        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(WebhookRequestParser)));

        using var sp = services.BuildServiceProvider();
        var parser = sp.GetRequiredService<WebhookRequestParser>();

        // Options accumulate via Configure and are last-wins. The effective secret becomes S2:
        //  the S2 signature succeeds and the S1 signature fails.
        var body = Encoding.UTF8.GetBytes(Payload);
        var callback = await parser.ParseAsync(body, Sign("S2", body));
        Assert.Equal("U0", callback.Destination);
        await Assert.ThrowsAsync<WebhookSignatureException>(() => parser.ParseAsync(body, Sign("S1", body)));
    }

    [Fact]
    public void AddLineWebhook_Missing_Secret_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineWebhook(o => { /* ChannelSecret not set */ });
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<WebhookRequestParser>());
    }
}
