using System;
using System.Linq;
using System.Net.Http;
using Line.Core.Authentication;
using Line.Messaging;
using Line.Messaging.DependencyInjection;
using Line.Messaging.Generated.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.Poc.Tests;

// DI 統合（M-3）の検証。AddLineMessaging で登録した MessagingClient が
//  - 解決できること
//  - IHttpClientFactory 経由の共有 HttpClient を使うこと
//  - 2 クライアントのルーティング（api / api-data）が DI 経由でも維持されること
// を確認する。実 HTTP は叩かない。
public class DiIntegrationTests
{
    [Fact]
    public void AddLineMessaging_StaticToken_Resolves_And_Routes()
    {
        var services = new ServiceCollection();
        services.AddLineMessaging(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<MessagingClient>();

        var push = client.Api.V2.Bot.Message.Push.ToPostRequestInformation(new PushMessageRequest());
        var content = client.Blob.V2.Bot.Message["1"].Content.ToGetRequestInformation();

        Assert.Equal("api.line.me", push.URI.Host);
        Assert.Equal("api-data.line.me", content.URI.Host);
    }

    [Fact]
    public void AddLineMessaging_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineMessaging(o => o.ChannelAccessToken = "TOKEN");
        using var sp = services.BuildServiceProvider();

        // 名前付きクライアントが生成できる（Kiota 既定ハンドラ差し込みが成立している）。
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineMessaging_CustomAuthProvider_Resolves()
    {
        var services = new ServiceCollection();
        // 更新型プロバイダなど任意の認証を注入する経路（Line.Messaging→ChannelAccessToken 依存なし）。
        services.AddLineMessaging(_ =>
            new BaseBearerTokenAuthenticationProvider(new StaticChannelAccessTokenProvider("TOKEN")));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<MessagingClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddLineMessaging_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineMessaging(o => o.ChannelAccessToken = "T1");
        services.AddLineMessaging(o => o.ChannelAccessToken = "T2"); // 2 回目はハンドラ/クライアント重複登録しない

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(MessagingClient)));

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<MessagingClient>();
        var push = client.Api.V2.Bot.Message.Push.ToPostRequestInformation(new PushMessageRequest());
        Assert.Equal("api.line.me", push.URI.Host); // 多重ハンドラでも壊れず解決・ルーティングできる
    }

    [Fact]
    public void AddLineMessaging_Missing_Token_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineMessaging(o => { /* ChannelAccessToken 未設定 */ });
        using var sp = services.BuildServiceProvider();

        // Options 検証により解決時に例外。
        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<MessagingClient>());
    }
}
