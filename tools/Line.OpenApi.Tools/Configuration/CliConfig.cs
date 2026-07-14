using System.Text.Json.Serialization;

namespace Line.OpenApi.Tools.Configuration;

/// <summary>
/// Root of the on-disk configuration file (<c>~/.line/config.json</c>, spec §5).
/// Holds named profiles so a user can switch between multiple LINE channels.
/// </summary>
public sealed class CliConfig
{
    /// <summary>Name of the profile used when none is specified on the command line or via environment.</summary>
    public string? DefaultProfile { get; set; }

    /// <summary>Profiles keyed by name (case-insensitive).</summary>
    public Dictionary<string, LineProfile> Profiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A single credential profile. Secrets are stored in plain text, so the file is
/// permission-restricted on save (<see cref="ConfigStore"/>); the private key itself
/// is referenced by path only (<see cref="PrivateKeyPath"/>), never inlined.
/// </summary>
public sealed class LineProfile
{
    /// <summary>Optional static channel access token (long-lived token workflow).</summary>
    public string? ChannelAccessToken { get; set; }

    /// <summary>Channel id (used for token issuance and JWT assertion <c>iss</c>).</summary>
    public string? ChannelId { get; set; }

    /// <summary>Channel secret (webhook signature verification and token issuance).</summary>
    public string? ChannelSecret { get; set; }

    /// <summary>Path to the RSA private key (PEM) used to sign JWT assertions for token issuance.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Key id (<c>kid</c>) associated with the assertion signing key.</summary>
    public string? Kid { get; set; }
}

/// <summary>Source-generated JSON context for trimming/AOT-friendly (de)serialization.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliConfig))]
internal sealed partial class CliConfigJsonContext : JsonSerializerContext
{
}
