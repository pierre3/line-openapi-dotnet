namespace Line.Messaging.Webhook.DependencyInjection;

/// <summary>
/// Options for
/// <see cref="ServiceCollectionExtensions.AddLineWebhook(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{LineWebhookOptions})"/>.
/// </summary>
public sealed class LineWebhookOptions
{
    /// <summary>The channel secret (the key for webhook signature validation).</summary>
    public string ChannelSecret { get; set; } = string.Empty;
}
