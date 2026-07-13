using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Line.Messaging.Webhook;
using Line.Messaging.Webhook.Generated.Models;
using Xunit;

namespace Line.Messaging.Webhook.IsolationTests;

// Regression test that WebhookRequestParser works standalone in a clean process free of registry pollution.
// This assembly references only Line.Messaging.Webhook and contains no code at all that constructs a generated
// client or calls ApiClientBuilder.RegisterDefaultDeserializer.
// -> The Kiota default serializer registry (ParseNodeFactoryRegistry.DefaultInstance) stays empty.
//   It guarantees that a valid Webhook is correctly reconstructed in that state (proof of self-containment).
public class WebhookRequestParserIsolationTests
{
    private const string ChannelSecret = "isolation-secret";

    // A mix of message / follow / postback / unknown. Confirms, under a clean registry, that polymorphic
    // reconstruction works correctly even when run through the parser's real path (direct use of JsonParseNodeFactory).
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
        // Polymorphic reconstruction also works via the registry-independent real path.
        Assert.IsType<MessageEvent>(callback.Events[0]);
        Assert.IsType<FollowEvent>(callback.Events[1]);
        Assert.IsType<PostbackEvent>(callback.Events[2]);
        Assert.Equal(typeof(Event), callback.Events[3].GetType()); // Unknown type falls back to the base type
    }
}
