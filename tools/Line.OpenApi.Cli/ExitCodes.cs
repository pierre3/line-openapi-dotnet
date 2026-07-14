namespace Line.OpenApi.Cli;

/// <summary>Process exit codes (spec §6).</summary>
internal static class ExitCodes
{
    /// <summary>Success.</summary>
    public const int Success = 0;

    /// <summary>General/unexpected error.</summary>
    public const int GeneralError = 1;

    /// <summary>Invalid arguments.</summary>
    public const int ArgumentError = 2;

    /// <summary>Authentication / credential resolution error.</summary>
    public const int CredentialError = 3;

    /// <summary>LINE API error (HTTP 4xx/5xx).</summary>
    public const int ApiError = 4;
}
