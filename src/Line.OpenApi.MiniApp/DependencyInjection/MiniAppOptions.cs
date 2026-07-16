namespace Line.OpenApi.MiniApp.DependencyInjection;

/// <summary>
/// Options for
/// <see cref="ServiceCollectionExtensions.AddLineMiniApp(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{MiniAppOptions})"/>.
/// </summary>
public sealed class MiniAppOptions
{
    /// <summary>
    /// Hosts a channel/user access token may be attached to. When unspecified, the default
    /// (api.line.me) is used. LINE MINI App has no data-plane host, so the default is the
    /// control plane only.
    /// </summary>
    public string[]? AllowedHosts { get; set; }
}
