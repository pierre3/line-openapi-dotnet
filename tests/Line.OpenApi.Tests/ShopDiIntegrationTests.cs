using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Shop;
using Line.OpenApi.Shop.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// DI integration verification for Shop. Confirms the ShopClient registered via AddLineShop:
//  - can be resolved, routes to api.line.me, uses IHttpClientFactory
//  - is idempotent / fails validation when the token is not set
public class ShopDiIntegrationTests
{
    [Fact]
    public void AddLineShop_StaticToken_Resolves_And_Routes()
    {
        var services = new ServiceCollection();
        services.AddLineShop(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<ShopClient>();

        var req = client.Api.Shop.V3.Mission.ToPostRequestInformation(
            new Line.OpenApi.Shop.Generated.Models.MissionStickerRequest());
        Assert.Equal("api.line.me", req.URI.Host);
    }

    [Fact]
    public void AddLineShop_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineShop(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineShop_CustomAuthProvider_Resolves()
    {
        var services = new ServiceCollection();
        services.AddLineShop(_ =>
            new BaseBearerTokenAuthenticationProvider(new StaticChannelAccessTokenProvider("TOKEN")));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<ShopClient>());
    }

    [Fact]
    public void AddLineShop_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineShop(o => o.ChannelAccessToken = "T1");
        services.AddLineShop(o => o.ChannelAccessToken = "T2");

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(ShopClient)));
    }

    [Fact]
    public void AddLineShop_Missing_Token_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineShop(o => { /* ChannelAccessToken not set */ });
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<ShopClient>());
    }
}
