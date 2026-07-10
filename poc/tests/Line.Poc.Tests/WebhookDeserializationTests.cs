// Webhook 多態デシリアライズの検証（看板機能）。
//
// G2 では生成後の型名未確定のため #if WEBHOOK_DESERIALIZATION_READY で opt-in だったが、
// G3 で型名を確定（CallbackRequest / MessageEvent / FollowEvent / PostbackEvent /
// TextMessageContent、未知は基底 Event へフォールバック）したため既定/CI で常時実行する（§2-D）。
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
        // 生成クライアントを構築しないため、JSON デシリアライザを既定レジストリへ明示登録する。
        ApiClientBuilder.RegisterDefaultDeserializer<JsonParseNodeFactory>();
    }

    // message(text) 単一イベント。
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

    // message / follow / postback / 未知(future) の混在。
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

        // 未知 type は discriminator の default で基底 Event に復元される（派生型ではない）。
        var unknown = callback!.Events![3];
        Assert.Equal(typeof(Event), unknown.GetType());
    }
}
