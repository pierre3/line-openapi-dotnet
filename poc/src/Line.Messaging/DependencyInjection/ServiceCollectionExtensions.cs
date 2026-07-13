using System;
using System.Linq;
using System.Net.Http;
using Line.Core.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Line.Messaging.DependencyInjection;

/// <summary>
/// Extensions that register <see cref="MessagingClient"/> with the DI container.
///
/// M-3 fix: resolves the problem of the two adapters each creating their own default
/// <see cref="HttpClient"/>. It applies a named <c>IHttpClientFactory</c> client (shared
/// handler pool) plus the Kiota default middleware (including the CVE-fixed RedirectHandler,
/// via <c>KiotaClientFactory.GetDefaultHandlerActivatableTypes()</c>), and injects the allowed
/// hosts from <see cref="LineMessagingOptions.AllowedHosts"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the named HttpClient used internally by DI.</summary>
    public const string HttpClientName = "Line.Messaging";

    /// <summary>
    /// Registers <see cref="MessagingClient"/> with a static (long-lived) channel access token.
    /// </summary>
    public static IServiceCollection AddLineMessaging(
        this IServiceCollection services,
        Action<LineMessagingOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        services.AddOptions<LineMessagingOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChannelAccessToken),
                "LineMessagingOptions.ChannelAccessToken is required.");

        return services.AddLineMessaging(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LineMessagingOptions>>().Value;
            var tokenProvider = new StaticChannelAccessTokenProvider(
                opts.ChannelAccessToken, opts.AllowedHosts ?? Array.Empty<string>());
            return new BaseBearerTokenAuthenticationProvider(tokenProvider);
        });
    }

    /// <summary>
    /// Registers <see cref="MessagingClient"/> with an arbitrary authentication provider.
    /// Use this overload to inject a refreshing token provider (Line.ChannelAccessToken); it is
    /// the injection path that avoids a Line.Messaging -> Line.ChannelAccessToken dependency.
    /// </summary>
    public static IServiceCollection AddLineMessaging(
        this IServiceCollection services,
        Func<IServiceProvider, IAuthenticationProvider> authProviderFactory)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (authProviderFactory is null) throw new ArgumentNullException(nameof(authProviderFactory));

        // Idempotency: even if called multiple times, do not add the Kiota default handlers to
        // the named client more than once. (Duplicates would multiply retries/redirects. A
        // marker ensures they are inserted only on the first call.)
        if (!services.Any(d => d.ServiceType == typeof(LineMessagingMarker)))
        {
            services.AddSingleton<LineMessagingMarker>();

            // Named HttpClient + Kiota default handlers (including CVE-fixed RedirectHandler etc.).
            // 1.22.2 has no IHttpClientBuilder.AttachKiotaHandlers, so we instantiate the default
            // handler types ourselves and insert them the DI-native way (consistent with
            // IHttpClientFactory's pooling/rotation).
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
            return new MessagingClient(authProvider, httpClient);
        });

        return services;
    }

    // Internal marker used to decide the one-time handler insertion.
    private sealed class LineMessagingMarker { }
}
