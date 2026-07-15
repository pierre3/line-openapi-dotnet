using System;
using System.Threading.Tasks;
using Line.OpenApi.Shop;
using Line.OpenApi.Shop.Generated.Models;
using Xunit;

namespace Line.OpenApi.Tests;

// Path verification for the ShopClient facade against the single host (api.line.me).
// The HTTP path (body, response discarding) is verified separately in ShopClientHttpTests.
public class ShopClientTests
{
    [Fact]
    public void SendMissionSticker_BuildsPost_ToApiLineMe()
    {
        var client = ShopClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.Shop.V3.Mission.ToPostRequestInformation(new MissionStickerRequest());

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/shop/v3/mission", req.URI.AbsolutePath);
    }

    [Fact]
    public async Task SendMissionStickerAsync_NullRequest_Throws()
    {
        var client = ShopClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendMissionStickerAsync(null!));
    }
}
