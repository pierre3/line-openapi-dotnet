using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Line.Messaging.Webhook;
using Line.Messaging.Webhook.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Line.Poc.Tests;

// Webhook の DI 統合検証。AddLineWebhook で登録した WebhookRequestParser が
//  - 解決できること
//  - 設定したチャネルシークレットで実際に署名検証・逆直列化できること
//  - 冪等（複数回登録で重複しない）／未設定シークレットで検証失敗すること
// を確認する。HTTP は伴わない。
public class WebhookDiIntegrationTests
{
    private const string ChannelSecret = "di-channel-secret";
    private const string Payload =
        "{\"destination\":\"U0\",\"events\":[]}";

    private static string Sign(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }

    [Fact]
    public async Task AddLineWebhook_Resolves_And_Parses()
    {
        var services = new ServiceCollection();
        services.AddLineWebhook(o => o.ChannelSecret = ChannelSecret);
        using var sp = services.BuildServiceProvider();

        var parser = sp.GetRequiredService<WebhookRequestParser>();

        var body = Encoding.UTF8.GetBytes(Payload);
        var callback = await parser.ParseAsync(body, Sign(ChannelSecret, body));
        Assert.Equal("U0", callback.Destination);
    }

    [Fact]
    public async Task AddLineWebhook_MultipleRegistrations_NotDuplicated_And_LastSecretWins()
    {
        var services = new ServiceCollection();
        services.AddLineWebhook(o => o.ChannelSecret = "S1");
        services.AddLineWebhook(o => o.ChannelSecret = "S2"); // パーサ登録は重複しない

        // パーサのサービス登録は初回優先で重複しない。
        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(WebhookRequestParser)));

        using var sp = services.BuildServiceProvider();
        var parser = sp.GetRequiredService<WebhookRequestParser>();

        // Options は Configure 累積で last-wins。実効シークレットは S2 になる:
        //  S2 署名は成功し、S1 署名は失敗する。
        var body = Encoding.UTF8.GetBytes(Payload);
        var callback = await parser.ParseAsync(body, Sign("S2", body));
        Assert.Equal("U0", callback.Destination);
        await Assert.ThrowsAsync<WebhookSignatureException>(() => parser.ParseAsync(body, Sign("S1", body)));
    }

    [Fact]
    public void AddLineWebhook_Missing_Secret_Fails_Validation()
    {
        var services = new ServiceCollection();
        services.AddLineWebhook(o => { /* ChannelSecret 未設定 */ });
        using var sp = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<WebhookRequestParser>());
    }
}
