using Line.Messaging;
using Line.Messaging.Generated.Api.Models;
using Xunit;

namespace Line.Tests;

// Verification of the actual behavior of R1 (multiple base URLs). Confirms whether the MessagingClient facade
//  - routes the control plane (send) to api.line.me
//  - routes the data plane (content retrieval) to api-data.line.me
// using the RequestInformation.URI assembled by the generated builder.
// No real HTTP needed. Because the generated client fixes baseurl into PathParameters at construction time,
// this test effectively verifies that "the BaseUrl override happens before construction".
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
