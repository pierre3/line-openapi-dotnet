using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Module;
using Line.OpenApi.Module.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// DI integration verification for Module. Confirms the ModuleClient registered via AddLineModule:
//  - can be resolved, routes to api.line.me, uses IHttpClientFactory
//  - is idempotent / fails validation when the token is not set
public class ModuleDiIntegrationTests
{
    [Fact]
    public void AddLineModule_StaticToken_Resolves_And_Routes()
    {
        var services = new ServiceCollection();
        services.AddLineModule(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<ModuleClient>();

        var req = client.Api.V2.Bot.List.ToGetRequestInformation();
        Assert.Equal("api.line.me", req.URI.Host);
    }

    [Fact]
    public void AddLineModule_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineModule(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineModule_CustomAuthProvider_Resolves()
    {
        var services = new ServiceCollection();
        services.AddLineModule(_ =>
            new BaseBearerTokenAuthenticationProvider(new StaticChannelAccessTokenProvider("TOKEN")));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<ModuleClient>());
    }

    [Fact]
    public void AddLineModule_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineModule(o => o.ChannelAccessToken = "T1");
        services.AddLineModule(o => o.ChannelAccessToken = "T2");

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(ModuleClient)));
    }

    [Fact]
    public void AddLineModule_Missing_Token_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineModule(o => { /* ChannelAccessToken not set */ });
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<ModuleClient>());
    }
}
