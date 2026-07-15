using System;

namespace Line.OpenApi.Insight.DependencyInjection;

/// <summary>
/// Options for
/// <see cref="ServiceCollectionExtensions.AddLineInsight(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{LineInsightOptions})"/>.
/// Used when constructing with a static (long-lived) channel access token.
/// </summary>
public sealed class LineInsightOptions
{
    /// <summary>Long-lived channel access token.</summary>
    public string ChannelAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Hosts the token may be attached to. When unspecified, the default (api.line.me) is used.
    /// Insight does not use a data-plane host, so the default is the control plane only.
    /// </summary>
    public string[]? AllowedHosts { get; set; }
}
