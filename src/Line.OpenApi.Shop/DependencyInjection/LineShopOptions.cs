using System;

namespace Line.OpenApi.Shop.DependencyInjection;

/// <summary>
/// Options for
/// <see cref="ServiceCollectionExtensions.AddLineShop(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{LineShopOptions})"/>.
/// Used when constructing with a static (long-lived) channel access token.
/// </summary>
public sealed class LineShopOptions
{
    /// <summary>Long-lived channel access token.</summary>
    public string ChannelAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Hosts the token may be attached to. When unspecified, the default (api.line.me) is used.
    /// Shop does not use a data-plane host, so the default is the control plane only.
    /// </summary>
    public string[]? AllowedHosts { get; set; }
}
