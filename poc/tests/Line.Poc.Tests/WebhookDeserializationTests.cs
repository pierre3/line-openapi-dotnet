// Webhook 多態デシリアライズの検証（生成モデル依存）。
//
// このテストは kiota 生成後の型名に依存するため、既定ではコンパイルされません。
// 手順:
//   1) scripts/generate を実行して src/Line.Messaging.Webhook/Generated/Models を生成。
//   2) 生成された実際のルート型名（多くは CallbackRequest）とイベント派生型名
//      （MessageEvent / TextMessageContent 等）を確認し、下記の型名を必要に応じて修正。
//   3) Line.Poc.Tests.csproj に <DefineConstants>WEBHOOK_DESERIALIZATION_READY</DefineConstants> を
//      追加（または dotnet test -p:DefineConstants=WEBHOOK_DESERIALIZATION_READY）して有効化。
//
// これは「生成された多態モデルが discriminator で正しく復元されるか」を確認する PoC の要。
#if WEBHOOK_DESERIALIZATION_READY
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

    private const string SamplePayload = @"{
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

    [Fact]
    public async System.Threading.Tasks.Task Deserializes_Polymorphic_MessageEvent()
    {
        // 型名は生成結果に合わせて調整すること。
        var callback = await KiotaSerializer.DeserializeAsync<CallbackRequest>(
            "application/json", SamplePayload, CallbackRequest.CreateFromDiscriminatorValue);

        Assert.NotNull(callback);
        Assert.Single(callback!.Events!);

        // discriminator(type=message) により派生型 MessageEvent に復元されることを確認。
        var msgEvent = Assert.IsType<MessageEvent>(callback.Events![0]);
        // message.type=text により TextMessageContent に復元される。
        var text = Assert.IsType<TextMessageContent>(msgEvent.Message);
        Assert.Equal("Hello, world", text.Text);
    }
}
#endif
