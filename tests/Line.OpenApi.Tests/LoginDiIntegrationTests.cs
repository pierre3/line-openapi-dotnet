using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Login;
using Line.OpenApi.Login.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Line.OpenApi.Tests;

// DI integration verification for LINE Login. Confirms that the LoginClient registered via
// AddLineLogin can be resolved, uses IHttpClientFactory, is idempotent, and fails validation
// when required options are missing. No real HTTP calls are made.
public class LoginDiIntegrationTests
{
    private static Action<LineLoginOptions> Valid()
        => o => { o.ChannelId = "1234567890"; o.ChannelSecret = "secret"; };

    [Fact]
    public void AddLineLogin_Resolves()
    {
        var services = new ServiceCollection();
        services.AddLineLogin(Valid());
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<LoginClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddLineLogin_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineLogin(Valid());
        using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineLogin_BuildsAuthorizationUrl_WithConfiguredChannelId()
    {
        var services = new ServiceCollection();
        services.AddLineLogin(Valid());
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<LoginClient>();
        var url = client.BuildAuthorizationUrl(new AuthorizationUrlParameters
        {
            RedirectUri = "https://app/cb",
            Scopes = new[] { "profile" },
            State = "S",
        });
        Assert.Contains("client_id=1234567890", url);
    }

    [Fact]
    public void AddLineLogin_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineLogin(Valid());
        services.AddLineLogin(Valid()); // the second call does not re-register

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(LoginClient)));
    }

    [Fact]
    public void AddLineLogin_Missing_ChannelId_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineLogin(o => o.ChannelSecret = "secret"); // ChannelId not set
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<LoginClient>());
    }

    [Fact]
    public void AddLineLogin_Missing_ChannelSecret_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineLogin(o => o.ChannelId = "1234567890"); // ChannelSecret not set
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<LoginClient>());
    }
}
