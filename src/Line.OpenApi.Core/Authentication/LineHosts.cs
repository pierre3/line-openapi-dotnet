namespace Line.OpenApi.Core.Authentication;

/// <summary>
/// LINE API hosts. To prepare for future additions (e.g. manager.line.biz), the set of
/// allowed hosts is centralized here rather than hard-coded, so it can be injected and
/// extended when a provider is created.
/// </summary>
public static class LineHosts
{
    public const string Api = "api.line.me";
    public const string ApiData = "api-data.line.me";

    /// <summary>Default allowed hosts for Bot/Messaging (control plane + data plane).</summary>
    public static readonly string[] Default = { Api, ApiData };
}
