using System;
using System.Linq;
using System.Net.Http;
using Line.Core.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Line.Liff.DependencyInjection;

/// <summary>
/// Extensions that register <see cref="LiffClient"/> with the DI container.
///
/// It applies a named <c>IHttpClientFactory</c> client (shared handler pool) plus the Kiota
/// default middleware (including the CVE-fixed RedirectHandler, via
/// <c>KiotaClientFactory.GetDefaultHandlerActivatableTypes()</c>), and injects the allowed
/// hosts from <see cref="LineLiffOptions.AllowedHosts"/>.
/// The implementation mirrors the same-named extension in Line.Messaging (the only difference
/// being the single host).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the named HttpClient used internally by DI.</summary>
    public const string HttpClientName = "Line.Liff";

    /// <summary>
    /// Registers <see cref="LiffClient"/> with a static (long-lived) channel access token.
    /// </summary>
    public static IServiceCollection AddLineLiff(
        this IServiceCollection services,
        Action<LineLiffOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        services.AddOptions<LineLiffOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChannelAccessToken),
                "LineLiffOptions.ChannelAccessToken is required.");

        return services.AddLineLiff(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LineLiffOptions>>().Value;
            var tokenProvider = new StaticChannelAccessTokenProvider(
                opts.ChannelAccessToken, opts.AllowedHosts ?? new[] { LineHosts.Api });
            return new BaseBearerTokenAuthenticationProvider(tokenProvider);
        });
    }

    /// <summary>
    /// Registers <see cref="LiffClient"/> with an arbitrary authentication provider.
    /// Use this overload to inject a refreshing token provider (Line.ChannelAccessToken); it is
    /// the injection path that avoids a Line.Liff -> Line.ChannelAccessToken dependency.
    /// </summary>
    public static IServiceCollection AddLineLiff(
        this IServiceCollection services,
        Func<IServiceProvider, IAuthenticationProvider> authProviderFactory)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (authProviderFactory is null) throw new ArgumentNullException(nameof(authProviderFactory));

        // Idempotency: even if called multiple times, do not add the Kiota default handlers to
        // the named client more than once.
        if (!services.Any(d => d.ServiceType == typeof(LineLiffMarker)))
        {
            services.AddSingleton<LineLiffMarker>();

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
            return new LiffClient(authProvider, httpClient);
        });

        return services;
    }

    // Internal marker used to decide the one-time handler insertion.
    private sealed class LineLiffMarker { }
}
