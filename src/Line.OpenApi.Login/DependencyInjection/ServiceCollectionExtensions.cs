using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Core.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Line.OpenApi.Login.DependencyInjection;

/// <summary>
/// Extensions that register <see cref="LoginClient"/> with the DI container.
///
/// It applies a named <c>IHttpClientFactory</c> client (shared handler pool) plus the Kiota
/// default middleware (including the CVE-fixed RedirectHandler, via
/// <c>KiotaClientFactory.GetDefaultHandlerActivatableTypes()</c>). The implementation mirrors
/// the same-named extension in Line.OpenApi.Liff.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the named HttpClient used internally by DI.</summary>
    public const string HttpClientName = "Line.OpenApi.Login";

    /// <summary>
    /// Registers <see cref="LoginClient"/> configured from <see cref="LineLoginOptions"/>
    /// (channel ID / channel secret).
    /// </summary>
    public static IServiceCollection AddLineLogin(
        this IServiceCollection services,
        Action<LineLoginOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        services.AddOptions<LineLoginOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChannelId),
                "LineLoginOptions.ChannelId is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChannelSecret),
                "LineLoginOptions.ChannelSecret is required.");

        // Idempotency: even if called multiple times, do not add the Kiota default handlers to
        // the named client more than once.
        if (!services.Any(d => d.ServiceType == typeof(LineLoginMarker)))
        {
            services.AddSingleton<LineLoginMarker>();

            var builder = services.AddHttpClient(HttpClientName);
            foreach (var handlerType in KiotaClientFactory.GetDefaultHandlerActivatableTypes())
            {
                builder.AddHttpMessageHandler(sp =>
                    (DelegatingHandler)ActivatorUtilities.CreateInstance(sp, handlerType));
            }
        }

        // First registration wins (TryAdd).
        services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LineLoginOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new LoginClient(
                opts.ChannelId,
                opts.ChannelSecret,
                httpClient,
                opts.AllowedHosts ?? new[] { LineHosts.Api });
        });

        return services;
    }

    // Internal marker used to decide the one-time handler insertion.
    private sealed class LineLoginMarker { }
}
