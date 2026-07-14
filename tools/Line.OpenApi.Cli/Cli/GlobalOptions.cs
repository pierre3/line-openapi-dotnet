using Cocona;

namespace Line.OpenApi.Cli.Cli;

/// <summary>
/// Options shared by every command (spec §4). Implemented as a Cocona parameter set so each
/// command method can accept it alongside its own arguments.
/// </summary>
public sealed record GlobalOptions(
    [Option("profile", Description = "Credential profile to use.")] string? Profile = null,
    [Option("channel-token", Description = "Channel access token override (wins over env/profile).")] string? ChannelToken = null,
    [Option("json", Description = "Emit machine-readable JSON.")] bool Json = false,
    [Option("verbose", Description = "Include verbose diagnostics on error.")] bool Verbose = false)
    : ICommandParameterSet;
