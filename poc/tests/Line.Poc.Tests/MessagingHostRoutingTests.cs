using Line.Messaging;
using Line.Messaging.Generated.Api.Models;
using Xunit;

namespace Line.Poc.Tests;

// R1（複数 base URL）の実挙動検証。ファサード MessagingClient が
//  - 制御系(送信)を api.line.me
//  - データ系(コンテンツ取得)を api-data.line.me
// へ実際にルーティングするかを、生成ビルダーが組み立てる RequestInformation.URI で確認する。
// 実 HTTP は不要。生成クライアントは構築時に baseurl を PathParameters へ確定するため、
// このテストは「BaseUrl 上書きが構築前に行われているか」を実効的に検証する。
public class MessagingHostRoutingTests
{
    [Fact]
    public void ControlPlane_Push_GoesTo_ApiLineMe()
    {
        var client = MessagingClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.V2.Bot.Message.Push.ToPostRequestInformation(new PushMessageRequest());

        Assert.Equal("api.line.me", req.URI.Host);
    }

    [Fact]
    public void DataPlane_Content_GoesTo_ApiDataLineMe()
    {
        var client = MessagingClient.CreateWithStaticToken("TOKEN");

        var req = client.Blob.V2.Bot.Message["14353798921116"].Content.ToGetRequestInformation();

        Assert.Equal("api-data.line.me", req.URI.Host);
        Assert.Equal("/v2/bot/message/14353798921116/content", req.URI.AbsolutePath);
    }
}
