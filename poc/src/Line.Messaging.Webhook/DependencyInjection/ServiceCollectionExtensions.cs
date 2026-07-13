using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Line.Messaging.Webhook.DependencyInjection;

/// <summary>
/// <see cref="WebhookRequestParser"/> を DI コンテナへ登録する拡張。
///
/// Webhook 受信は HTTP 送信を伴わない（署名検証＋逆直列化のみ）ため、Messaging/Liff と異なり
/// <c>IHttpClientFactory</c> は不要。チャネルシークレットを <see cref="LineWebhookOptions"/> から
/// 受け、シングルトンの <see cref="WebhookRequestParser"/> を登録する。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// チャネルシークレットで <see cref="WebhookRequestParser"/> を登録する。
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
            .ValidateOnStart(); // 設定漏れ（無効な鍵での受信素通し）を起動時に落とす。

        // パーサのサービス登録は初回優先（TryAddSingleton）。ただし Options 値は Configure が累積
        // 適用されるため、複数回呼び出し時の実効 ChannelSecret は「最後の設定」が採用される（last-wins）。
        // 二重登録は想定外構成であり、意味論は AddLineWebhook_Is_Idempotent で固定している。
        services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LineWebhookOptions>>().Value;
            return new WebhookRequestParser(opts.ChannelSecret);
        });

        return services;
    }
}
