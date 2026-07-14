using System.Text.Json;

namespace Line.OpenApi.Cli.Configuration;

/// <summary>
/// Reads and writes the CLI configuration file (spec §5). On save the file is
/// permission-restricted because it may hold plaintext secrets:
/// <list type="bullet">
///   <item><description>Unix: mode <c>0600</c> (owner read/write only).</description></item>
///   <item><description>Windows: relies on the inherited <c>%USERPROFILE%</c> ACL (owner + SYSTEM + Administrators); a plaintext-storage warning is emitted to the caller.</description></item>
/// </list>
/// </summary>
public sealed class ConfigStore
{
    private readonly string _path;

    /// <summary>Creates a store at the default location, or an override via <c>LINE_CONFIG</c>.</summary>
    public ConfigStore()
        : this(ResolveDefaultPath())
    {
    }

    /// <summary>Creates a store at an explicit path (used by tests).</summary>
    public ConfigStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>Absolute path of the backing config file.</summary>
    public string Path => _path;

    private static string ResolveDefaultPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("LINE_CONFIG");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(home, ".line", "config.json");
    }

    /// <summary>Loads the config, returning an empty one if the file does not exist.</summary>
    public CliConfig Load()
    {
        if (!File.Exists(_path))
        {
            return new CliConfig();
        }

        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CliConfig();
        }

        try
        {
            return JsonSerializer.Deserialize(json, CliConfigJsonContext.Default.CliConfig)
                ?? new CliConfig();
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Configuration file is not valid JSON: {_path}", ex);
        }
    }

    /// <summary>
    /// Persists the config and restricts file permissions. Returns a non-null warning
    /// string when secrets are stored in plaintext without a strong OS-level guarantee
    /// (e.g. Windows), so the caller can surface it.
    /// </summary>
    public string? Save(CliConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(config, CliConfigJsonContext.Default.CliConfig);

        // On Unix create the file with 0600 up-front (no world-readable window between write and
        // chmod). On Windows FileStreamOptions ignores UnixCreateMode.
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(_path, options))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
        }

        return PlaintextWarning();
    }

    /// <summary>Convenience: stores an access token into a profile (creating it if absent).</summary>
    public string? StoreAccessToken(string profileName, string accessToken)
    {
        var config = Load();
        if (!config.Profiles.TryGetValue(profileName, out var profile))
        {
            profile = new LineProfile();
            config.Profiles[profileName] = profile;
        }

        profile.ChannelAccessToken = accessToken;
        config.DefaultProfile ??= profileName;
        return Save(config);
    }

    private string? PlaintextWarning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null; // 0600 applied at creation.
        }

        // Windows: we do not set an explicit ACL here (avoids a Windows-only ACL dependency).
        // Under %USERPROFILE% the inherited ACL restricts to the current user; elsewhere it may
        // not — so the warning is accurate about the actual location.
        var profileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var underProfile = !string.IsNullOrEmpty(profileRoot)
            && System.IO.Path.GetFullPath(_path)
                .StartsWith(System.IO.Path.GetFullPath(profileRoot), StringComparison.OrdinalIgnoreCase);

        return underProfile
            ? "config stores secrets in plaintext; it inherits your user-profile ACL. "
                + "Prefer environment variables or --private-key path references for sensitive values."
            : $"config stores secrets in plaintext OUTSIDE your user profile ('{_path}'); its ACL may allow "
                + $"other users to read it. Prefer a location under '{profileRoot}', or use environment variables.";
    }
}

/// <summary>Thrown when the configuration file cannot be read or parsed.</summary>
public sealed class ConfigException : Exception
{
    public ConfigException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
