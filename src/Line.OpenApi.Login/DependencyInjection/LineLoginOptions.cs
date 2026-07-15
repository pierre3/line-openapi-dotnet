using Line.OpenApi.Core.Authentication;

namespace Line.OpenApi.Login.DependencyInjection;

/// <summary>
/// Options for
/// <see cref="ServiceCollectionExtensions.AddLineLogin(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{LineLoginOptions})"/>.
/// </summary>
public sealed class LineLoginOptions
{
    /// <summary>LINE Login channel ID (used as <c>client_id</c>).</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>LINE Login channel secret (used as <c>client_secret</c>).</summary>
    public string ChannelSecret { get; set; } = string.Empty;

    /// <summary>
    /// Hosts an access token may be attached to. When unspecified, the default (api.line.me) is
    /// used. LINE Login has no data-plane host, so the default is the control plane only.
    /// </summary>
    public string[]? AllowedHosts { get; set; }
}
