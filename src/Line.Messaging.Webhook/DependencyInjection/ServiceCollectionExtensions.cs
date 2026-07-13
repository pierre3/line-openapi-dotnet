using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Line.Messaging.Webhook.DependencyInjection;

/// <summary>
/// Extensions that register <see cref="WebhookRequestParser"/> with the DI container.
///
/// Webhook receiving involves no HTTP sending (only signature validation + deserialization),
/// so unlike Messaging/Liff, <c>IHttpClientFactory</c> is not needed. It takes the channel
/// secret from <see cref="LineWebhookOptions"/> and registers a singleton
/// <see cref="WebhookRequestParser"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WebhookRequestParser"/> with the channel secret.
    /// </summary>
    public static IServiceCollection AddLineWebhook(
        this IServiceCollection services,
        Action<LineWebhookOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        services.AddOptions<LineWebhookOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChannelSecret),
                "LineWebhookOptions.ChannelSecret is required.")
            .ValidateOnStart(); // Fail at startup on a missing setting (receiving with an invalid key would otherwise pass through).

        // Registration of the parser service is first-wins (TryAddSingleton). However, Options
        // values are applied cumulatively by Configure, so on multiple calls the effective
        // ChannelSecret is "the last setting" (last-wins). Double registration is an unexpected
        // configuration; its semantics are pinned by AddLineWebhook_Is_Idempotent.
        services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LineWebhookOptions>>().Value;
            return new WebhookRequestParser(opts.ChannelSecret);
        });

        return services;
    }
}
