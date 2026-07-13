using System;
using System.Text;
using System.Security.Cryptography;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Core.Webhook;
using Xunit;

namespace Line.OpenApi.Tests;

// Verification of Line.OpenApi.Core's hand-written logic. Independent of generated code, so it always builds and runs.
public class CoreTests
{
    // --- Signature validation (known-value test) ---
    [Fact]
    public void Signature_Valid_ReturnsTrue()
    {
        const string secret = "test-channel-secret";
        var body = Encoding.UTF8.GetBytes("{\"events\":[]}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(body));

        Assert.True(WebhookSignatureValidator.IsValid(secret, body, signature));
    }

    [Fact]
    public void Signature_Tampered_ReturnsFalse()
    {
        const string secret = "test-channel-secret";
        var body = Encoding.UTF8.GetBytes("{\"events\":[]}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(body));

        var tampered = Encoding.UTF8.GetBytes("{\"events\":[{}]}");
        Assert.False(WebhookSignatureValidator.IsValid(secret, tampered, signature));
        Assert.False(WebhookSignatureValidator.IsValid(secret, body, "not-base64!!"));
        Assert.False(WebhookSignatureValidator.IsValid(secret, body, null));
    }

    // --- Negative-side tests for AllowedHostsValidator ---
    [Fact]
    public async System.Threading.Tasks.Task Token_NotAttached_ToDisallowedHost()
    {
        var provider = new StaticChannelAccessTokenProvider("TOKEN"); // default: api.line.me / api-data.line.me

        var apiToken = await provider.GetAuthorizationTokenAsync(new Uri("https://api.line.me/v2/bot/message/push"));
        var dataToken = await provider.GetAuthorizationTokenAsync(new Uri("https://api-data.line.me/v2/bot/message/x/content"));
        var evilToken = await provider.GetAuthorizationTokenAsync(new Uri("https://evil.example.com/"));

        Assert.Equal("TOKEN", apiToken);
        Assert.Equal("TOKEN", dataToken);   // also attached for data-plane hosts
        Assert.Equal(string.Empty, evilToken); // not attached for non-allowed hosts
    }
}
