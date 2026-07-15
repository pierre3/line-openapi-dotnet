using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Core.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Line.OpenApi.Module.DependencyInjection;

/// <summary>
/// Extensions that register <see cref="ModuleClient"/> with the DI container.
///
/// It applies a named <c>IHttpClientFactory</c> client (shared handler pool) plus the Kiota
/// default middleware (including the CVE-fixed RedirectHandler, via
/// <c>KiotaClientFactory.GetDefaultHandlerActivatableTypes()</c>), and injects the allowed
/// hosts from <see cref="LineModuleOptions.AllowedHosts"/>.
/// The implementation mirrors the same-named extension in Line.OpenApi.Liff (single host).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the named HttpClient used internally by DI.</summary>
    public const string HttpClientName = "Line.OpenApi.Module";

    /// <summary>
    /// Registers <see cref="ModuleClient"/> with a static (long-lived) channel access token.
    /// </summary>
    public static IServiceCollection AddLineModule(
        this IServiceCollection services,
        Action<LineModuleOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        services.AddOptions<LineModuleOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChannelAccessToken),
                "LineModuleOptions.ChannelAccessToken is required.");

        return services.AddLineModule(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LineModuleOptions>>().Value;
            var tokenProvider = new StaticChannelAccessTokenProvider(
                opts.ChannelAccessToken, opts.AllowedHosts ?? new[] { LineHosts.Api });
            return new BaseBearerTokenAuthenticationProvider(tokenProvider);
        });
    }

    /// <summary>
    /// Registers <see cref="ModuleClient"/> with an arbitrary authentication provider.
    /// Use this overload to inject a refreshing token provider (Line.OpenApi.ChannelAccessToken); it is
    /// the injection path that avoids a Line.OpenApi.Module -> Line.OpenApi.ChannelAccessToken dependency.
    /// </summary>
    public static IServiceCollection AddLineModule(
        this IServiceCollection services,
        Func<IServiceProvider, IAuthenticationProvider> authProviderFactory)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (authProviderFactory is null) throw new ArgumentNullException(nameof(authProviderFactory));

        // Idempotency: even if called multiple times, do not add the Kiota default handlers to
        // the named client more than once.
        if (!services.Any(d => d.ServiceType == typeof(LineModuleMarker)))
        {
            services.AddSingleton<LineModuleMarker>();

            var builder = services.AddHttpClient(HttpClientName);
            foreach (var handlerType in KiotaClientFactory.GetDefaultHandlerActivatableTypes())
            {
                builder.AddHttpMessageHandler(sp =>
                    (DelegatingHandler)ActivatorUtilities.CreateInstance(sp, handlerType));
            }
        }

        // First registration wins (TryAdd). On multiple calls, the first auth-provider setup is used.
        services.TryAddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var authProvider = authProviderFactory(sp);
            return new ModuleClient(authProvider, httpClient);
        });

        return services;
    }

    // Internal marker used to decide the one-time handler insertion.
    private sealed class LineModuleMarker { }
}
