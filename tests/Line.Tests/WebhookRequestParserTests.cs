using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Line.Messaging.Webhook;
using Line.Messaging.Webhook.Generated.Models;
using Xunit;

namespace Line.Tests;

// Verifies the receive entry point of WebhookRequestParser (signature validation + deserialization).
// Focus:
//  - whether combinations of signature OK/NG x parse OK/NG produce the correct exception/return value
//  - polymorphic reconstruction itself is covered by the existing WebhookDeserializationTests, so only one case is checked here
//
// Note: "working standalone without depending on the global default registry" cannot be proven within this
// assembly (Line.Tests) because other tests may pollute ParseNodeFactoryRegistry.DefaultInstance. That self-containment
// is guaranteed structurally by an implementation that uses JsonParseNodeFactory directly, and regression is guaranteed by the
// independent assembly Line.Messaging.Webhook.IsolationTests (references only Webhook = a clean registry).
public class WebhookRequestParserTests
{
    private const string ChannelSecret = "test-channel-secret";

    private const string ValidPayload = @"{
      ""destination"": ""U0123456789abcdef"",
      ""events"": [
        {
          ""type"": ""message"",
          ""message"": { ""type"": ""text"", ""id"": ""14353798921116"", ""text"": ""Hello, world"" },
          ""timestamp"": 1625665242211,
          ""source"": { ""type"": ""user"", ""userId"": ""U80696558e1aa831..."" },
          ""replyToken"": ""757913772c4646b784d4b7ce46d12671"",
          ""mode"": ""active"",
          ""webhookEventId"": ""01FZ74A0TDDPYRVKNK77XKC3ZR"",
          ""deliveryContext"": { ""isRedelivery"": false }
        }
      ]
    }";

    // Computes the signature the same way LINE does (Base64 of HMAC-SHA256 with the channelSecret key).
    private static string Sign(string channelSecret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }

    [Fact]
    public async Task ParseAsync_ValidSignatureAndPayload_ReturnsCallback()
    {
        var body = Encoding.UTF8.GetBytes(ValidPayload);
        var sig = Sign(ChannelSecret, body);
        var parser = new WebhookRequestParser(ChannelSecret);

        var callback = await parser.ParseAsync(body, sig);

        Assert.Equal("U0123456789abcdef", callback.Destination);
        // Polymorphic reconstruction is delegated to the generated side. Here we check concrete-type reconstruction for just one case.
        var msg = Assert.IsType<MessageEvent>(Assert.Single(callback.Events!));
        var text = Assert.IsType<TextMessageContent>(msg.Message);
        Assert.Equal("Hello, world", text.Text);
    }

    [Fact]
    public async Task ParseAsync_StaticOverload_Works()
    {
        var body = Encoding.UTF8.GetBytes(ValidPayload);
        var sig = Sign(ChannelSecret, body);

        var callback = await WebhookRequestParser.ParseAsync(ChannelSecret, body, sig);

        Assert.Equal("U0123456789abcdef", callback.Destination);
    }

    [Fact]
    public async Task ParseAsync_WrongSecret_ThrowsSignatureException()
    {
        var body = Encoding.UTF8.GetBytes(ValidPayload);
        var sig = Sign("some-other-secret", body); // signed with a different key
        var parser = new WebhookRequestParser(ChannelSecret);

        await Assert.ThrowsAsync<WebhookSignatureException>(() => parser.ParseAsync(body, sig));
    }

    [Fact]
    public async Task ParseAsync_TamperedBody_ThrowsSignatureException()
    {
        var original = Encoding.UTF8.GetBytes(ValidPayload);
        var sig = Sign(ChannelSecret, original);
        var tampered = Encoding.UTF8.GetBytes(ValidPayload.Replace("Hello, world", "Tampered"));
        var parser = new WebhookRequestParser(ChannelSecret);

        await Assert.ThrowsAsync<WebhookSignatureException>(() => parser.ParseAsync(tampered, sig));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!")]
    public async Task ParseAsync_MissingOrMalformedSignature_ThrowsSignatureException(string? sig)
    {
        var body = Encoding.UTF8.GetBytes(ValidPayload);
        var parser = new WebhookRequestParser(ChannelSecret);

        await Assert.ThrowsAsync<WebhookSignatureException>(() => parser.ParseAsync(body, sig));
    }

    [Fact]
    public async Task ParseAsync_ValidSignatureButMalformedJson_ThrowsPayloadException()
    {
        var body = Encoding.UTF8.GetBytes("{ this is not valid json ");
        var sig = Sign(ChannelSecret, body); // the signature itself is valid (computed over the body)
        var parser = new WebhookRequestParser(ChannelSecret);

        await Assert.ThrowsAsync<WebhookPayloadException>(() => parser.ParseAsync(body, sig));
    }

    [Fact]
    public async Task ParseAsync_NullBody_ThrowsArgumentNullException()
    {
        var parser = new WebhookRequestParser(ChannelSecret);
        await Assert.ThrowsAsync<ArgumentNullException>(() => parser.ParseAsync(null!, "sig"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Ctor_MissingSecret_Throws(string? secret)
    {
        Assert.Throws<ArgumentException>(() => new WebhookRequestParser(secret!));
    }

    [Fact]
    public async Task WebhookSignatureException_And_PayloadException_ShareBase()
    {
        // Confirms that the caller can catch everything with the base WebhookException.
        var body = Encoding.UTF8.GetBytes(ValidPayload);
        var parser = new WebhookRequestParser(ChannelSecret);

        var ex = await Assert.ThrowsAnyAsync<WebhookException>(
            () => parser.ParseAsync(body, "invalid"));
        Assert.IsType<WebhookSignatureException>(ex);
    }
}
