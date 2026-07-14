namespace Line.OpenApi.Tools.Configuration;

/// <summary>
/// Resolves effective credentials by layering sources in priority order
/// (spec §3): explicit command-line override &gt; environment variable &gt; profile file.
/// </summary>
public sealed class CredentialResolver
{
    /// <summary>Environment variable names honored during resolution.</summary>
    public const string EnvProfile = "LINE_PROFILE";
    public const string EnvChannelAccessToken = "LINE_CHANNEL_ACCESS_TOKEN";
    public const string EnvChannelId = "LINE_CHANNEL_ID";
    public const string EnvChannelSecret = "LINE_CHANNEL_SECRET";
    public const string EnvPrivateKeyPath = "LINE_PRIVATE_KEY_PATH";
    public const string EnvKid = "LINE_KID";

    private const string DefaultProfileName = "default";

    private readonly ConfigStore _store;

    public CredentialResolver(ConfigStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Resolves credentials. Any non-null value in <paramref name="overrides"/> wins over
    /// the environment, which wins over the selected profile.
    /// </summary>
    public ResolvedCredentials Resolve(CredentialOverrides? overrides = null)
    {
        overrides ??= new CredentialOverrides();
        var config = _store.Load();

        var profileName = FirstNonBlank(
            overrides.ProfileName,
            Environment.GetEnvironmentVariable(EnvProfile),
            config.DefaultProfile) ?? DefaultProfileName;

        config.Profiles.TryGetValue(profileName, out var profile);

        return new ResolvedCredentials(
            ProfileName: profileName,
            ProfileExists: profile is not null,
            ChannelAccessToken: FirstNonBlank(overrides.ChannelAccessToken, Environment.GetEnvironmentVariable(EnvChannelAccessToken), profile?.ChannelAccessToken),
            ChannelId: FirstNonBlank(overrides.ChannelId, Environment.GetEnvironmentVariable(EnvChannelId), profile?.ChannelId),
            ChannelSecret: FirstNonBlank(overrides.ChannelSecret, Environment.GetEnvironmentVariable(EnvChannelSecret), profile?.ChannelSecret),
            PrivateKeyPath: FirstNonBlank(overrides.PrivateKeyPath, Environment.GetEnvironmentVariable(EnvPrivateKeyPath), profile?.PrivateKeyPath),
            Kid: FirstNonBlank(overrides.Kid, Environment.GetEnvironmentVariable(EnvKid), profile?.Kid));
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

/// <summary>Command-line overrides that take precedence over environment and profile.</summary>
public sealed class CredentialOverrides
{
    public string? ProfileName { get; init; }
    public string? ChannelAccessToken { get; init; }
    public string? ChannelId { get; init; }
    public string? ChannelSecret { get; init; }
    public string? PrivateKeyPath { get; init; }
    public string? Kid { get; init; }
}

/// <summary>Effective credentials for a single command invocation.</summary>
public sealed record ResolvedCredentials(
    string ProfileName,
    bool ProfileExists,
    string? ChannelAccessToken,
    string? ChannelId,
    string? ChannelSecret,
    string? PrivateKeyPath,
    string? Kid)
{
    /// <summary>Returns the access token or throws a <see cref="CredentialException"/> if unset.</summary>
    public string RequireAccessToken() =>
        ChannelAccessToken ?? throw Missing("channel access token", $"--channel-token, ${CredentialResolver.EnvChannelAccessToken}, or profile '{ProfileName}'");

    /// <summary>Returns the channel secret or throws a <see cref="CredentialException"/> if unset.</summary>
    public string RequireChannelSecret() =>
        ChannelSecret ?? throw Missing("channel secret", $"--secret, ${CredentialResolver.EnvChannelSecret}, or profile '{ProfileName}'");

    /// <summary>Returns the private key path or throws a <see cref="CredentialException"/> if unset.</summary>
    public string RequirePrivateKeyPath() =>
        PrivateKeyPath ?? throw Missing("private key path", $"--private-key, ${CredentialResolver.EnvPrivateKeyPath}, or profile '{ProfileName}'");

    /// <summary>Returns the channel id or throws a <see cref="CredentialException"/> if unset.</summary>
    public string RequireChannelId() =>
        ChannelId ?? throw Missing("channel id", $"--channel-id, ${CredentialResolver.EnvChannelId}, or profile '{ProfileName}'");

    private static CredentialException Missing(string what, string sources) =>
        new($"No {what} available. Provide it via {sources}.");
}

/// <summary>Thrown when a required credential is missing. Maps to exit code 3.</summary>
public sealed class CredentialException : Exception
{
    public CredentialException(string message)
        : base(message)
    {
    }
}
