using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Liff;
using Line.OpenApi.Liff.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// DI integration verification for LIFF. Confirms that the LiffClient registered via AddLineLiff:
//  - can be resolved
//  - uses a shared HttpClient via IHttpClientFactory
//  - routes the CRUD paths to api.line.me
//  - is idempotent (no duplication when registered multiple times) / fails validation when the token is not set
// No real HTTP calls are made.
public class LiffDiIntegrationTests
{
    [Fact]
    public void AddLineLiff_StaticToken_Resolves_And_Routes()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<LiffClient>();

        var req = client.Api.Liff.V1.Apps.ToGetRequestInformation();
        Assert.Equal("api.line.me", req.URI.Host);
    }

    [Fact]
    public void AddLineLiff_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineLiff_CustomAuthProvider_Resolves()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(_ =>
            new BaseBearerTokenAuthenticationProvider(new StaticChannelAccessTokenProvider("TOKEN")));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<LiffClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddLineLiff_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(o => o.ChannelAccessToken = "T1");
        services.AddLineLiff(o => o.ChannelAccessToken = "T2"); // the second call does not re-register

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(LiffClient)));

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<LiffClient>();
        var req = client.Api.Liff.V1.Apps.ToGetRequestInformation();
        Assert.Equal("api.line.me", req.URI.Host);
    }

    [Fact]
    public void AddLineLiff_Missing_Token_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(o => { /* ChannelAccessToken not set */ });
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<LiffClient>());
    }
}
