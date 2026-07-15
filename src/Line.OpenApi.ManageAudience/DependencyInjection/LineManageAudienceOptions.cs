using System;

namespace Line.OpenApi.ManageAudience.DependencyInjection;

/// <summary>
/// Options for
/// <see cref="ServiceCollectionExtensions.AddLineManageAudience(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{LineManageAudienceOptions})"/>.
/// Used when constructing with a static (long-lived) channel access token.
/// </summary>
public sealed class LineManageAudienceOptions
{
    /// <summary>Long-lived channel access token.</summary>
    public string ChannelAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Hosts the token may be attached to. When unspecified, both the control plane (api.line.me)
    /// and the data plane (api-data.line.me) are allowed, since the by-file upload uses the data plane.
    /// </summary>
    public string[]? AllowedHosts { get; set; }
}
