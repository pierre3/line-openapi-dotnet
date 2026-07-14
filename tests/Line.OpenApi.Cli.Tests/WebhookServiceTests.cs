using System.Security.Cryptography;
using System.Text;
using Line.OpenApi.Cli.Services;
using Line.OpenApi.Messaging.Webhook;
using Xunit;

namespace Line.OpenApi.Cli.Tests;

public sealed class WebhookServiceTests
{
    private const string Secret = "test-channel-secret";

    private static (byte[] body, string signature) SignedPayload(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(body));
        return (body, signature);
    }

    [Fact]
    public async Task VerifyAsync_valid_signature_returns_event_summary()
    {
        var (body, signature) = SignedPayload(
            "{\"destination\":\"U1\",\"events\":[{\"type\":\"message\",\"message\":{\"type\":\"text\",\"id\":\"1\",\"text\":\"hi\"},\"timestamp\":1,\"mode\":\"active\"}]}");

        var result = await new WebhookService().VerifyAsync(Secret, body, signature, CancellationToken.None);

        Assert.Equal("U1", result.Destination);
        Assert.Equal(new[] { "Message" }, result.EventTypes);
    }

    [Fact]
    public async Task VerifyAsync_invalid_signature_throws()
    {
        var (body, _) = SignedPayload("{\"destination\":\"U1\",\"events\":[]}");

        await Assert.ThrowsAsync<WebhookSignatureException>(() =>
            new WebhookService().VerifyAsync(Secret, body, "not-the-right-signature", CancellationToken.None));
    }
}
