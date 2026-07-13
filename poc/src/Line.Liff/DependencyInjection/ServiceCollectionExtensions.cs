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
/// <see cref="LiffClient"/> を DI コンテナへ登録する拡張。
///
/// <c>IHttpClientFactory</c> の名前付きクライアント（ハンドラプール共有）＋ Kiota 既定ミドルウェア
/// （<c>KiotaClientFactory.GetDefaultHandlerActivatableTypes()</c> 経由で CVE 修正版 RedirectHandler を含む）
/// を適用し、許可ホストは <see cref="LineLiffOptions.AllowedHosts"/> から注入する。
/// 実装方針は Line.Messaging の同名拡張と揃えている（単一ホストな点のみ差異）。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>DI 内部で用いる名前付き HttpClient 名。</summary>
    public const string HttpClientName = "Line.Liff";

    /// <summary>
    /// 静的（長期）チャネルアクセストークンで <see cref="LiffClient"/> を登録する。
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
    /// 任意の認証プロバイダで <see cref="LiffClient"/> を登録する。
    /// 更新型トークンプロバイダ（Line.ChannelAccessToken）を注入する場合はこちらを使う
    /// （Line.Liff → Line.ChannelAccessToken の依存を作らないための注入経路）。
    /// </summary>
    public static IServiceCollection AddLineLiff(
        this IServiceCollection services,
        Func<IServiceProvider, IAuthenticationProvider> authProviderFactory)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (authProviderFactory is null) throw new ArgumentNullException(nameof(authProviderFactory));

        // 冪等化: 複数回呼ばれても名前付きクライアントに Kiota 既定ハンドラを重複追記しない。
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

        // 初回登録が有効（TryAdd）。複数回呼び出し時は最初の認証プロバイダ設定が採用される。
        services.TryAddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var authProvider = authProviderFactory(sp);
            return new LiffClient(authProvider, httpClient);
        });

        return services;
    }

    // ハンドラ差し込みの一回性を判定するための内部マーカー。
    private sealed class LineLiffMarker { }
}
