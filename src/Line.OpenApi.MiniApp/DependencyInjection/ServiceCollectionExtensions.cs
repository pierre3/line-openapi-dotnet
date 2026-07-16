using System;
using System.Linq;
using System.Net.Http;
using Line.OpenApi.Core.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Line.OpenApi.MiniApp.DependencyInjection;

/// <summary>
/// Extensions that register <see cref="MiniAppClient"/> with the DI container.
///
/// It applies a named <c>IHttpClientFactory</c> client (shared handler pool) plus the Kiota
/// default middleware (including the CVE-fixed RedirectHandler, via
/// <c>KiotaClientFactory.GetDefaultHandlerActivatableTypes()</c>). The implementation mirrors
/// the same-named extension in Line.OpenApi.Login. Unlike Login, no channel ID/secret is
/// required at registration time: MiniAppClient takes tokens per call, not at construction.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the named HttpClient used internally by DI.</summary>
    public const string HttpClientName = "Line.OpenApi.MiniApp";

    /// <summary>
    /// Registers <see cref="MiniAppClient"/>, optionally configured from
    /// <see cref="MiniAppOptions"/> (allowed hosts).
    /// </summary>
    public static IServiceCollection AddLineMiniApp(
        this IServiceCollection services,
        Action<MiniAppOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.AddOptions<MiniAppOptions>().Configure(configure ?? (_ => { }));

        // Idempotency: even if called multiple times, do not add the Kiota default handlers to
        // the named client more than once.
        if (!services.Any(d => d.ServiceType == typeof(MiniAppMarker)))
        {
            services.AddSingleton<MiniAppMarker>();

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
            var opts = sp.GetRequiredService<IOptions<MiniAppOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MiniAppClient(httpClient, opts.AllowedHosts ?? new[] { LineHosts.Api });
        });

        return services;
    }

    // Internal marker used to decide the one-time handler insertion.
    private sealed class MiniAppMarker { }
}
