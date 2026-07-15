using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.ManageAudience;
using Line.OpenApi.ManageAudience.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// DI integration verification for Manage Audience. Confirms the ManageAudienceClient registered via
// AddLineManageAudience: resolves, routes the control plane to api.line.me and the data plane to
// api-data.line.me (R1 regression), uses IHttpClientFactory, is idempotent, and validates the token.
public class ManageAudienceDiIntegrationTests
{
    [Fact]
    public void AddLineManageAudience_StaticToken_Resolves_And_Routes_BothPlanes()
    {
        var services = new ServiceCollection();
        services.AddLineManageAudience(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<ManageAudienceClient>();

        var control = client.Api.V2.Bot.AudienceGroup.List.ToGetRequestInformation();
        Assert.Equal("api.line.me", control.URI.Host);

        // Data plane must route to api-data.line.me (BaseUrl override set before construction).
        // MultipartBody needs at least one part to serialize.
        var mp = new Microsoft.Kiota.Abstractions.MultipartBody();
        mp.AddOrReplacePart("file", "text/plain", new System.IO.MemoryStream(new byte[] { 1 }));
        var data = client.Blob.V2.Bot.AudienceGroup.Upload.ByFile.ToPostRequestInformation(mp);
        Assert.Equal("api-data.line.me", data.URI.Host);
    }

    [Fact]
    public void AddLineManageAudience_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineManageAudience(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineManageAudience_CustomAuthProvider_Resolves()
    {
        var services = new ServiceCollection();
        services.AddLineManageAudience(_ =>
            new BaseBearerTokenAuthenticationProvider(new StaticChannelAccessTokenProvider("TOKEN")));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<ManageAudienceClient>());
    }

    [Fact]
    public void AddLineManageAudience_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineManageAudience(o => o.ChannelAccessToken = "T1");
        services.AddLineManageAudience(o => o.ChannelAccessToken = "T2");

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(ManageAudienceClient)));
    }

    [Fact]
    public void AddLineManageAudience_Missing_Token_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineManageAudience(o => { /* ChannelAccessToken not set */ });
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<ManageAudienceClient>());
    }
}
