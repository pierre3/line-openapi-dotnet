using System;

namespace Line.Messaging.DependencyInjection;

/// <summary>
/// Options for
/// <see cref="ServiceCollectionExtensions.AddLineMessaging(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{LineMessagingOptions})"/>.
/// Used when constructing with a static (long-lived) channel access token.
/// </summary>
public sealed class LineMessagingOptions
{
    /// <summary>Long-lived channel access token.</summary>
    public string ChannelAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Hosts the token may be attached to. When unspecified, the defaults
    /// (api.line.me / api-data.line.me) are used. Made injectable to prepare for future host
    /// additions (e.g. manager.line.biz).
    /// </summary>
    public string[]? AllowedHosts { get; set; }
}
