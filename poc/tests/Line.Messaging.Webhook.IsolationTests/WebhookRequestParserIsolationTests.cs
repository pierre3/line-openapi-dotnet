using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Line.Messaging.Webhook;
using Line.Messaging.Webhook.Generated.Models;
using Xunit;

namespace Line.Messaging.Webhook.IsolationTests;

// レジストリ汚染の無いクリーンなプロセスで WebhookRequestParser が単独動作することの回帰テスト。
// このアセンブリは Line.Messaging.Webhook のみ参照し、生成クライアント構築や
// ApiClientBuilder.RegisterDefaultDeserializer を行うコードを一切含まない。
// → Kiota 既定シリアライザレジストリ（ParseNodeFactoryRegistry.DefaultInstance）は空のまま。
//   その状態で正当な Webhook が正しく復元できることを保証する（自己完結性の実証）。
public class WebhookRequestParserIsolationTests
{
    private const string ChannelSecret = "isolation-secret";

    // message / follow / postback / 未知 の混在。パーサ実パス（JsonParseNodeFactory 直使用）を
    // 通しても多態復元が正しく機能することを、クリーンなレジストリ下で確認する。
    private const string Payload = @"{
      ""destination"": ""U0123456789abcdef"",
      ""events"": [
        {
          ""type"": ""message"",
          ""message"": { ""type"": ""text"", ""id"": ""1"", ""text"": ""hi"" },
          ""timestamp"": 1, ""mode"": ""active"",
          ""source"": { ""type"": ""user"", ""userId"": ""U1"" },
          ""deliveryContext"": { ""isRedelivery"": false }
        },
        {
          ""type"": ""follow"",
          ""timestamp"": 2, ""mode"": ""active"",
          ""source"": { ""type"": ""user"", ""userId"": ""U2"" },
          ""replyToken"": ""rt"",
          ""deliveryContext"": { ""isRedelivery"": false }
        },
        {
          ""type"": ""postback"",
          ""postback"": { ""data"": ""action=buy"" },
          ""timestamp"": 3, ""mode"": ""active"",
          ""source"": { ""type"": ""group"", ""groupId"": ""G1"" },
          ""replyToken"": ""rt2"",
          ""deliveryContext"": { ""isRedelivery"": false }
        },
        {
          ""type"": ""someFutureType"",
          ""timestamp"": 4, ""mode"": ""active"",
          ""source"": { ""type"": ""user"", ""userId"": ""U3"" },
          ""deliveryContext"": { ""isRedelivery"": false }
        }
      ]
    }";

    private static string Sign(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }

    [Fact]
    public async Task ParseAsync_WithoutAnyDeserializerRegistration_Succeeds_AndResolvesPolymorphism()
    {
        var body = Encoding.UTF8.GetBytes(Payload);
        var parser = new WebhookRequestParser(ChannelSecret);

        var callback = await parser.ParseAsync(body, Sign(ChannelSecret, body));

        Assert.Equal("U0123456789abcdef", callback.Destination);
        Assert.Equal(4, callback.Events!.Count);
        // 多態復元もレジストリ非依存の実パスで機能する。
        Assert.IsType<MessageEvent>(callback.Events[0]);
        Assert.IsType<FollowEvent>(callback.Events[1]);
        Assert.IsType<PostbackEvent>(callback.Events[2]);
        Assert.Equal(typeof(Event), callback.Events[3].GetType()); // 未知 type は基底へフォールバック
    }
}
