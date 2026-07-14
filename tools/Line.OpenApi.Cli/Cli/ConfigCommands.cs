using Cocona;
using Line.OpenApi.Cli.Configuration;
using Line.OpenApi.Cli.Output;

namespace Line.OpenApi.Cli.Cli;

/// <summary>
/// <c>line config ...</c> — manage credential profiles stored in <c>~/.line/config.json</c>
/// (spec §5). Secrets are masked on display.
/// </summary>
internal sealed class ConfigCommands
{
    private readonly ConfigStore _store;

    public ConfigCommands(ConfigStore store)
    {
        _store = store;
    }

    [Command("set", Description = "Create or update a profile's credentials.")]
    public void Set(
        [Argument(Description = "Profile name.")] string profile,
        [Option("token", Description = "Static channel access token.")] string? token = null,
        [Option("channel-id", Description = "Channel id.")] string? channelId = null,
        [Option("secret", Description = "Channel secret.")] string? secret = null,
        [Option("private-key", Description = "Path to the RSA private key (PEM) for JWT assertions.")] string? privateKey = null,
        [Option("kid", Description = "Key id for the assertion signing key.")] string? kid = null,
        [Option("default", Description = "Also set this profile as the default.")] bool makeDefault = false)
    {
        var config = _store.Load();
        if (!config.Profiles.TryGetValue(profile, out var entry))
        {
            entry = new LineProfile();
            config.Profiles[profile] = entry;
        }

        if (token is not null) entry.ChannelAccessToken = token;
        if (channelId is not null) entry.ChannelId = channelId;
        if (secret is not null) entry.ChannelSecret = secret;
        if (privateKey is not null) entry.PrivateKeyPath = privateKey;
        if (kid is not null) entry.Kid = kid;

        if (makeDefault || config.DefaultProfile is null)
        {
            config.DefaultProfile = profile;
        }

        var warning = _store.Save(config);
        Console.WriteLine($"Saved profile '{profile}' to {_store.Path}");
        if (warning is not null)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }
    }

    [Command("get", Description = "Show a profile's credentials (secrets masked).")]
    public int Get([Argument(Description = "Profile name (defaults to the default profile).")] string? profile = null)
    {
        var config = _store.Load();
        var name = profile ?? config.DefaultProfile;
        if (name is null)
        {
            Console.Error.WriteLine("No profile specified and no default profile is set.");
            return ExitCodes.ArgumentError;
        }

        if (!config.Profiles.TryGetValue(name, out var entry))
        {
            Console.Error.WriteLine($"Profile '{name}' not found.");
            return ExitCodes.ArgumentError;
        }

        Console.WriteLine($"profile:            {name}{(name == config.DefaultProfile ? " (default)" : "")}");
        Console.WriteLine($"channelAccessToken: {SecretMasking.Mask(entry.ChannelAccessToken)}");
        Console.WriteLine($"channelId:          {entry.ChannelId ?? "<unset>"}");
        Console.WriteLine($"channelSecret:      {SecretMasking.Mask(entry.ChannelSecret)}");
        Console.WriteLine($"privateKeyPath:     {entry.PrivateKeyPath ?? "<unset>"}");
        Console.WriteLine($"kid:                {entry.Kid ?? "<unset>"}");
        return ExitCodes.Success;
    }

    [Command("list", Description = "List profile names.")]
    public void List()
    {
        var config = _store.Load();
        if (config.Profiles.Count == 0)
        {
            Console.WriteLine($"No profiles. Create one with: line config set <name> --token <t>");
            return;
        }

        foreach (var name in config.Profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var marker = name == config.DefaultProfile ? "* " : "  ";
            Console.WriteLine($"{marker}{name}");
        }
    }

    [Command("use", Description = "Set the default profile.")]
    public int Use([Argument(Description = "Profile name.")] string profile)
    {
        var config = _store.Load();
        if (!config.Profiles.TryGetValue(profile, out _))
        {
            Console.Error.WriteLine($"Profile '{profile}' not found.");
            return ExitCodes.ArgumentError;
        }

        config.DefaultProfile = profile;
        _store.Save(config);
        Console.WriteLine($"Default profile set to '{profile}'.");
        return ExitCodes.Success;
    }
}
