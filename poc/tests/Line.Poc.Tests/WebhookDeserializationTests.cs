// Verification of Webhook polymorphic deserialization (flagship feature).
//
// In G2 the post-generation type names were not yet fixed, so this was opt-in via #if WEBHOOK_DESERIALIZATION_READY, but
// in G3 the type names were fixed (CallbackRequest / MessageEvent / FollowEvent / PostbackEvent /
// TextMessageContent, with unknown falling back to the base Event), so it now always runs by default/in CI (section 2-D).
using System.Threading.Tasks;
using Line.Messaging.Webhook.Generated.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;
using Xunit;

namespace Line.Poc.Tests;

public class WebhookDeserializationTests
{
    static WebhookDeserializationTests()
    {
        // Because no generated client is constructed, the JSON deserializer is explicitly registered into the default registry.
        ApiClientBuilder.RegisterDefaultDeserializer<JsonParseNodeFactory>();
    }

    // A single message(text) event.
    private const string SinglePayload = @"{
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

    // A mix of message / follow / postback / unknown(future).
    private const string MixedPayload = @"{
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
          ""postback"": { ""data"": ""action=buy&id=1"" },
          ""timestamp"": 3, ""mode"": ""active"",
          ""source"": { ""type"": ""group"", ""groupId"": ""G1"" },
          ""replyToken"": ""rt2"",
          ""deliveryContext"": { ""isRedelivery"": false }
        },
        {
          ""type"": ""someFutureEventTypeNotInSpec"",
          ""timestamp"": 4, ""mode"": ""active"",
          ""source"": { ""type"": ""user"", ""userId"": ""U3"" },
          ""deliveryContext"": { ""isRedelivery"": false }
        }
      ]
    }";

    private static Task<CallbackRequest?> Deserialize(string json) =>
        KiotaSerializer.DeserializeAsync<CallbackRequest>(
            "application/json", json, CallbackRequest.CreateFromDiscriminatorValue);

    [Fact]
    public async Task Deserializes_Single_MessageEvent_With_TextContent()
    {
        var callback = await Deserialize(SinglePayload);

        Assert.NotNull(callback);
        Assert.Equal("U0123456789abcdef", callback!.Destination);
        var msgEvent = Assert.IsType<MessageEvent>(Assert.Single(callback.Events!));
        var text = Assert.IsType<TextMessageContent>(msgEvent.Message);
        Assert.Equal("Hello, world", text.Text);
    }

    [Fact]
    public async Task Deserializes_Mixed_Events_To_Correct_Derived_Types()
    {
        var callback = await Deserialize(MixedPayload);

        Assert.NotNull(callback);
        Assert.Equal(4, callback!.Events!.Count);

        Assert.IsType<MessageEvent>(callback.Events[0]);
        Assert.IsType<FollowEvent>(callback.Events[1]);
        var postback = Assert.IsType<PostbackEvent>(callback.Events[2]);
        Assert.Equal("action=buy&id=1", postback.Postback!.Data);
    }

    [Fact]
    public async Task Unknown_Event_Type_Falls_Back_To_Base_Event()
    {
        var callback = await Deserialize(MixedPayload);

        // An unknown type is reconstructed to the base Event via the discriminator's default (not a derived type).
        var unknown = callback!.Events![3];
        Assert.Equal(typeof(Event), unknown.GetType());
    }
}
