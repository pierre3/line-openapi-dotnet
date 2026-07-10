using System;
using System.Net.Http;
using Line.Core.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Line.Messaging.DependencyInjection;

/// <summary>
/// <see cref="MessagingClient"/> を DI コンテナへ登録する拡張。
///
/// M-3 対応: 2 アダプタが各々既定 <see cref="HttpClient"/> を内部生成する問題を解消する。
/// <c>IHttpClientFactory</c> の名前付きクライアント（ハンドラプール共有）＋ Kiota 既定ミドルウェア
/// （<c>KiotaClientFactory.GetDefaultHandlerActivatableTypes()</c> 経由で CVE 修正版 RedirectHandler を含む）
/// を適用し、許可ホストは <see cref="LineMessagingOptions.AllowedHosts"/> から注入する。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>DI 内部で用いる名前付き HttpClient 名。</summary>
    public const string HttpClientName = "Line.Messaging";

    /// <summary>
    /// 静的（長期）チャネルアクセストークンで <see cref="MessagingClient"/> を登録する。
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
    /// 任意の認証プロバイダで <see cref="MessagingClient"/> を登録する。
    /// 更新型トークンプロバイダ（Line.ChannelAccessToken）を注入する場合はこちらを使う
    /// （Line.Messaging → Line.ChannelAccessToken の依存を作らないための注入経路）。
    /// </summary>
    public static IServiceCollection AddLineMessaging(
        this IServiceCollection services,
        Func<IServiceProvider, IAuthenticationProvider> authProviderFactory)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (authProviderFactory is null) throw new ArgumentNullException(nameof(authProviderFactory));

        // 名前付き HttpClient + Kiota 既定ハンドラ（RedirectHandler 等の CVE 修正版を含む）。
        // 1.22.2 には IHttpClientBuilder.AttachKiotaHandlers が無いため、DI ネイティブに
        // 既定ハンドラ型を都度生成して差し込む（IHttpClientFactory のプール/ローテーションと整合）。
        var builder = services.AddHttpClient(HttpClientName);
        foreach (var handlerType in KiotaClientFactory.GetDefaultHandlerActivatableTypes())
        {
            builder.AddHttpMessageHandler(sp =>
                (DelegatingHandler)ActivatorUtilities.CreateInstance(sp, handlerType));
        }

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var authProvider = authProviderFactory(sp);
            return new MessagingClient(authProvider, httpClient);
        });

        return services;
    }
}
