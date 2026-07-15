using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Insight;
using Line.OpenApi.Insight.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// DI integration verification for Insight. Confirms the InsightClient registered via AddLineInsight:
//  - can be resolved, routes to api.line.me, uses IHttpClientFactory
//  - is idempotent / fails validation when the token is not set
// No real HTTP calls are made.
public class InsightDiIntegrationTests
{
    [Fact]
    public void AddLineInsight_StaticToken_Resolves_And_Routes()
    {
        var services = new ServiceCollection();
        services.AddLineInsight(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<InsightClient>();

        var req = client.Api.V2.Bot.Insight.Demographic.ToGetRequestInformation();
        Assert.Equal("api.line.me", req.URI.Host);
    }

    [Fact]
    public void AddLineInsight_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineInsight(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineInsight_CustomAuthProvider_Resolves()
    {
        var services = new ServiceCollection();
        services.AddLineInsight(_ =>
            new BaseBearerTokenAuthenticationProvider(new StaticChannelAccessTokenProvider("TOKEN")));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<InsightClient>());
    }

    [Fact]
    public void AddLineInsight_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineInsight(o => o.ChannelAccessToken = "T1");
        services.AddLineInsight(o => o.ChannelAccessToken = "T2"); // the second call does not re-register

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(InsightClient)));
    }

    [Fact]
    public void AddLineInsight_Missing_Token_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineInsight(o => { /* ChannelAccessToken not set */ });
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<InsightClient>());
    }
}
