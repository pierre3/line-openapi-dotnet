using System;
using System.Threading.Tasks;
using Line.OpenApi.Core.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies StaticBearerTokenProvider attaches the token only to allowed hosts (the negative-host
// case required by the design's security policy).
public class StaticBearerTokenProviderTests
{
    [Fact]
    public async Task ReturnsToken_ForAllowedHost()
    {
        var provider = new StaticBearerTokenProvider("USER-TOKEN", LineHosts.Api);
        var token = await provider.GetAuthorizationTokenAsync(new Uri("https://api.line.me/v2/profile"));
        Assert.Equal("USER-TOKEN", token);
    }

    [Fact]
    public async Task WithholdsToken_ForDisallowedHost()
    {
        var provider = new StaticBearerTokenProvider("USER-TOKEN", LineHosts.Api);
        var token = await provider.GetAuthorizationTokenAsync(new Uri("https://evil.example.com/v2/profile"));
        Assert.Equal(string.Empty, token);
    }

    [Fact]
    public void Rejects_EmptyToken()
        => Assert.Throws<ArgumentException>(() => new StaticBearerTokenProvider("", LineHosts.Api));

    [Fact]
    public void DefaultsToBotMessagingHosts_WhenNoneSpecified()
    {
        var provider = new StaticBearerTokenProvider("T");
        Assert.True(provider.AllowedHostsValidator.IsUrlHostValid(new Uri("https://api.line.me/x")));
        Assert.True(provider.AllowedHostsValidator.IsUrlHostValid(new Uri("https://api-data.line.me/x")));
    }
}
