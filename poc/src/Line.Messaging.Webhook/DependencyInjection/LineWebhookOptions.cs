namespace Line.Messaging.Webhook.DependencyInjection;

/// <summary>
/// <see cref="ServiceCollectionExtensions.AddLineWebhook(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{LineWebhookOptions})"/>
/// の設定。
/// </summary>
public sealed class LineWebhookOptions
{
    /// <summary>チャネルシークレット（Webhook 署名検証の鍵）。</summary>
    public string ChannelSecret { get; set; } = string.Empty;
}
