using Line.OpenApi.Tools.Configuration;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

public sealed class CredentialResolverTests : IDisposable
{
    private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"line-cli-test-{Guid.NewGuid():N}.json");

    private CredentialResolver CreateResolver(CliConfig config)
    {
        var store = new ConfigStore(_configPath);
        store.Save(config);
        return new CredentialResolver(store);
    }

    [Fact]
    public void Resolves_from_profile_when_no_override_or_env()
    {
        var resolver = CreateResolver(new CliConfig
        {
            DefaultProfile = "p1",
            Profiles = { ["p1"] = new LineProfile { ChannelAccessToken = "from-profile" } },
        });

        var result = resolver.Resolve();

        Assert.Equal("p1", result.ProfileName);
        Assert.Equal("from-profile", result.ChannelAccessToken);
        Assert.True(result.ProfileExists);
    }

    [Fact]
    public void Override_wins_over_profile()
    {
        var resolver = CreateResolver(new CliConfig
        {
            DefaultProfile = "p1",
            Profiles = { ["p1"] = new LineProfile { ChannelAccessToken = "from-profile" } },
        });

        var result = resolver.Resolve(new CredentialOverrides { ChannelAccessToken = "from-arg" });

        Assert.Equal("from-arg", result.ChannelAccessToken);
    }

    [Fact]
    public void Env_wins_over_profile_but_loses_to_override()
    {
        var resolver = CreateResolver(new CliConfig
        {
            DefaultProfile = "p1",
            Profiles = { ["p1"] = new LineProfile { ChannelAccessToken = "from-profile" } },
        });

        Environment.SetEnvironmentVariable(CredentialResolver.EnvChannelAccessToken, "from-env");
        try
        {
            Assert.Equal("from-env", resolver.Resolve().ChannelAccessToken);
            Assert.Equal("from-arg", resolver.Resolve(new CredentialOverrides { ChannelAccessToken = "from-arg" }).ChannelAccessToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CredentialResolver.EnvChannelAccessToken, null);
        }
    }

    [Fact]
    public void Explicit_profile_name_overrides_default()
    {
        var resolver = CreateResolver(new CliConfig
        {
            DefaultProfile = "p1",
            Profiles =
            {
                ["p1"] = new LineProfile { ChannelAccessToken = "t1" },
                ["p2"] = new LineProfile { ChannelAccessToken = "t2" },
            },
        });

        Assert.Equal("t2", resolver.Resolve(new CredentialOverrides { ProfileName = "p2" }).ChannelAccessToken);
    }

    [Fact]
    public void Missing_required_token_throws_credential_exception()
    {
        var resolver = CreateResolver(new CliConfig());
        var result = resolver.Resolve(new CredentialOverrides { ProfileName = "nope" });

        Assert.False(result.ProfileExists);
        Assert.Throws<CredentialException>(() => result.RequireAccessToken());
    }

    public void Dispose()
    {
        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }
    }
}
