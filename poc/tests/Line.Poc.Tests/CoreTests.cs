using System;
using System.Text;
using System.Security.Cryptography;
using Line.Core.Authentication;
using Line.Core.Webhook;
using Xunit;

namespace Line.Poc.Tests;

// Line.Core の手書きロジックの検証。生成コードに依存しないため常にビルド・実行可能。
public class CoreTests
{
    // --- 署名検証（既知値テスト） ---
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

    // --- AllowedHostsValidator の負側テスト ---
    [Fact]
    public async System.Threading.Tasks.Task Token_NotAttached_ToDisallowedHost()
    {
        var provider = new StaticChannelAccessTokenProvider("TOKEN"); // 既定: api.line.me / api-data.line.me

        var apiToken = await provider.GetAuthorizationTokenAsync(new Uri("https://api.line.me/v2/bot/message/push"));
        var dataToken = await provider.GetAuthorizationTokenAsync(new Uri("https://api-data.line.me/v2/bot/message/x/content"));
        var evilToken = await provider.GetAuthorizationTokenAsync(new Uri("https://evil.example.com/"));

        Assert.Equal("TOKEN", apiToken);
        Assert.Equal("TOKEN", dataToken);   // データ系ホストにも付与される
        Assert.Equal(string.Empty, evilToken); // 許可外には付与しない
    }
}
