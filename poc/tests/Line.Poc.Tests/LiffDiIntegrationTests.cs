using System;
using System.Linq;
using System.Net.Http;
using Line.Core.Authentication;
using Line.Liff;
using Line.Liff.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.Poc.Tests;

// LIFF の DI 統合検証。AddLineLiff で登録した LiffClient が
//  - 解決できること
//  - IHttpClientFactory 経由の共有 HttpClient を使うこと
//  - CRUD 経路が api.line.me へルーティングされること
//  - 冪等（複数回登録で重複しない）／未設定トークンで検証失敗すること
// を確認する。実 HTTP は叩かない。
public class LiffDiIntegrationTests
{
    [Fact]
    public void AddLineLiff_StaticToken_Resolves_And_Routes()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<LiffClient>();

        var req = client.Api.Liff.V1.Apps.ToGetRequestInformation();
        Assert.Equal("api.line.me", req.URI.Host);
    }

    [Fact]
    public void AddLineLiff_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineLiff_CustomAuthProvider_Resolves()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(_ =>
            new BaseBearerTokenAuthenticationProvider(new StaticChannelAccessTokenProvider("TOKEN")));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<LiffClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddLineLiff_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(o => o.ChannelAccessToken = "T1");
        services.AddLineLiff(o => o.ChannelAccessToken = "T2"); // 2 回目は重複登録しない

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(LiffClient)));

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<LiffClient>();
        var req = client.Api.Liff.V1.Apps.ToGetRequestInformation();
        Assert.Equal("api.line.me", req.URI.Host);
    }

    [Fact]
    public void AddLineLiff_Missing_Token_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineLiff(o => { /* ChannelAccessToken 未設定 */ });
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<LiffClient>());
    }
}
