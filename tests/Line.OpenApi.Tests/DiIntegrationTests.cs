using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.DependencyInjection;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// Verification of DI integration (M-3). Confirms that the MessagingClient registered via AddLineMessaging:
//  - can be resolved
//  - uses a shared HttpClient via IHttpClientFactory
//  - preserves the two-client routing (api / api-data) even through DI
// No real HTTP calls are made.
public class DiIntegrationTests
{
    [Fact]
    public void AddLineMessaging_StaticToken_Resolves_And_Routes()
    {
        var services = new ServiceCollection();
        services.AddLineMessaging(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<MessagingClient>();

        var push = client.Api.V2.Bot.Message.Push.ToPostRequestInformation(new PushMessageRequest());
        var content = client.Blob.V2.Bot.Message["1"].Content.ToGetRequestInformation();

        Assert.Equal("api.line.me", push.URI.Host);
        Assert.Equal("api-data.line.me", content.URI.Host);
    }

    [Fact]
    public void AddLineMessaging_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineMessaging(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        // A named client can be created (Kiota default handler injection is in effect).
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineMessaging_CustomAuthProvider_Resolves()
    {
        var services = new ServiceCollection();
        // Path for injecting arbitrary authentication such as a refreshing provider (no Line.OpenApi.Messaging -> ChannelAccessToken dependency).
        services.AddLineMessaging(_ =>
            new BaseBearerTokenAuthenticationProvider(new StaticChannelAccessTokenProvider("TOKEN")));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<MessagingClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddLineMessaging_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineMessaging(o => o.ChannelAccessToken = "T1");
        services.AddLineMessaging(o => o.ChannelAccessToken = "T2"); // The second call does not re-register the handler/client

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(MessagingClient)));

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<MessagingClient>();
        var push = client.Api.V2.Bot.Message.Push.ToPostRequestInformation(new PushMessageRequest());
        Assert.Equal("api.line.me", push.URI.Host); // Resolves and routes correctly without breaking even with duplicate handlers
    }

    [Fact]
    public void AddLineMessaging_Missing_Token_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineMessaging(o => { /* ChannelAccessToken not set */ });
        using var sp = services.BuildServiceProvider();

        // Options validation throws at resolution time.
        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<MessagingClient>());
    }
}
