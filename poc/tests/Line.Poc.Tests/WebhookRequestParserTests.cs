using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Line.Messaging.Webhook;
using Line.Messaging.Webhook.Generated.Models;
using Xunit;

namespace Line.Poc.Tests;

// WebhookRequestParser の受信入口（署名検証＋逆直列化）を検証する。
// 重点:
//  - 署名 OK/NG × パース OK/NG の組み合わせで正しい例外/戻り値になるか
//  - 多態復元そのものは既存 WebhookDeserializationTests が担保するため、ここでは 1 件のみ確認
//
// 注: 「グローバル既定レジストリ非依存で単独動作する」ことは、本アセンブリ（Line.Poc.Tests）内では
// 他テストが ParseNodeFactoryRegistry.DefaultInstance を汚染しうるため証明できない。その自己完結性は
// JsonParseNodeFactory を直接使う実装で構造的に担保し、回帰は独立アセンブリ
// Line.Messaging.Webhook.IsolationTests（Webhook のみ参照＝クリーンなレジストリ）で保証する。
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

    // LINE と同じ方式で署名を計算する（channelSecret 鍵の HMAC-SHA256 を Base64）。
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
        // 多態復元は生成側に委譲。ここでは 1 件だけ具象型復元を確認する。
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
        var sig = Sign("some-other-secret", body); // 別の鍵で署名
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
        var sig = Sign(ChannelSecret, body); // 署名自体は正当（本文に対して計算）
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
        // 呼び出し側が基底 WebhookException で一括捕捉できることを確認する。
        var body = Encoding.UTF8.GetBytes(ValidPayload);
        var parser = new WebhookRequestParser(ChannelSecret);

        var ex = await Assert.ThrowsAnyAsync<WebhookException>(
            () => parser.ParseAsync(body, "invalid"));
        Assert.IsType<WebhookSignatureException>(ex);
    }
}
