using Line.OpenApi.Tools.Configuration;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"line-cli-cfg-{Guid.NewGuid():N}", "config.json");

    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        var store = new ConfigStore(_path);
        var config = store.Load();
        Assert.Empty(config.Profiles);
        Assert.Null(config.DefaultProfile);
    }

    [Fact]
    public void Save_then_load_round_trips_and_omits_nulls()
    {
        var store = new ConfigStore(_path);
        store.Save(new CliConfig
        {
            DefaultProfile = "p",
            Profiles = { ["p"] = new LineProfile { ChannelAccessToken = "tok", ChannelId = "1" } },
        });

        var json = File.ReadAllText(_path);
        Assert.DoesNotContain("channelSecret", json); // null fields are omitted
        Assert.Contains("\"channelAccessToken\": \"tok\"", json);

        var loaded = new ConfigStore(_path).Load();
        Assert.Equal("p", loaded.DefaultProfile);
        Assert.Equal("tok", loaded.Profiles["p"].ChannelAccessToken);
    }

    [Fact]
    public void Save_restricts_unix_permissions_to_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix-mode assertion not applicable on Windows.
        }

        var store = new ConfigStore(_path);
        store.Save(new CliConfig { Profiles = { ["p"] = new LineProfile() } });

        var mode = File.GetUnixFileMode(_path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void Save_warns_about_plaintext_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var warning = new ConfigStore(_path).Save(new CliConfig { Profiles = { ["p"] = new LineProfile() } });
        Assert.NotNull(warning);
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
