using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Line.ChannelAccessToken.Generated;
using Line.ChannelAccessToken.Generated.Oauth2.V21.Token;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Line.Poc.Tests;

// channel-access-token のトークン発行が application/x-www-form-urlencoded で
// 正しくシリアライズされるかの検証（§2-B）。実 HTTP は叩かず、生成ビルダーが組み立てる
// RequestInformation の Content-Type と本体キーを確認する。
public class FormUrlEncodedTests
{
    private static ChannelAccessTokenClient CreateClient()
    {
        // 認証不要のエンドポイントなので Anonymous でよい。
        // コンストラクタが Form シリアライザを既定レジストリへ登録する（これが本テストの前提）。
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider());
        return new ChannelAccessTokenClient(adapter);
    }

    [Fact]
    public async Task Token_Request_Uses_FormUrlEncoded_ContentType()
    {
        var client = CreateClient();
        var body = new TokenPostRequestBody
        {
            GrantType = "client_credentials",
            ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ClientAssertion = "SIGNED.JWT.VALUE",
        };

        var req = client.Oauth2.V21.Token.ToPostRequestInformation(body);

        Assert.True(req.Headers.TryGetValue("Content-Type", out var contentTypes));
        Assert.Contains("application/x-www-form-urlencoded", contentTypes!.Single());
    }

    [Fact]
    public async Task Token_Request_Body_Contains_Form_Keys()
    {
        var client = CreateClient();
        var body = new TokenPostRequestBody
        {
            GrantType = "client_credentials",
            ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ClientAssertion = "SIGNED.JWT.VALUE",
        };

        var req = client.Oauth2.V21.Token.ToPostRequestInformation(body);

        using var reader = new StreamReader(req.Content);
        var payload = await reader.ReadToEndAsync();

        // form-urlencoded: key=value を & 連結、値は URL エンコード。
        Assert.Contains("grant_type=client_credentials", payload);
        Assert.Contains("client_assertion_type=urn%3Aietf", payload); // ':' はエンコードされる
        Assert.Contains("client_assertion=SIGNED.JWT.VALUE", payload);
        Assert.Contains("&", payload);
        Assert.DoesNotContain("{", payload); // JSON になっていないこと
    }
}
