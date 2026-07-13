using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.ChannelAccessToken;
using Line.ChannelAccessToken.Generated;
using Line.ChannelAccessToken.Generated.Models;
using Line.ChannelAccessToken.Generated.Oauth2.V3.Token;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Line.Poc.Tests;

// StatelessJwtAssertionTokenSource（/oauth2/v3/token）の実発行経路をトランスポート層まで
// 含めて検証する。特に oneOf 合成ボディ（TokenPostRequestBody / IComposedTypeWrapper）が
// 実 HttpClientRequestAdapter 経由で application/x-www-form-urlencoded の平坦なフォーム本体に
// 直列化されること（＝手書きヘルパが合成ラッパを正しく隠蔽できていること）を実証する。
public class StatelessJwtAssertionTokenSourceHttpTests
{
    private const string ExpectedAssertionType =
        "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private static StatelessJwtAssertionTokenSource CreateSource(RecordingHandler handler)
    {
        // BaseUrl はクライアント構築時に確定するため、アダプタ→クライアントの順で構築する
        // （既定 https://api.line.me が採用される）。
        var adapter = new HttpClientRequestAdapter(
            new AnonymousAuthenticationProvider(),
            httpClient: new HttpClient(handler));
        var client = new ChannelAccessTokenClient(adapter);
        return new StatelessJwtAssertionTokenSource(client, _ => Task.FromResult("SIGNED.JWT.VALUE"));
    }

    [Fact]
    public async Task IssueAsync_SendsFormEncodedPost_To_V3_And_ParsesJsonResponse()
    {
        // ステートレストークンは 15 分（900 秒）寿命。
        var json =
            "{\"access_token\":\"stateless-token\",\"expires_in\":900,\"token_type\":\"Bearer\"}";
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var source = CreateSource(handler);

        var issued = await source.IssueAsync();

        // --- リクエスト（トランスポート層）の検証 ---
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/oauth2/v3/token", handler.Request.RequestUri!.ToString());
        Assert.Equal(
            "application/x-www-form-urlencoded",
            handler.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("application/json", handler.Request.Headers.Accept.ToString());

        // 合成ラッパが平坦なフォームキーに展開されていること（JSON 入れ子でないこと）。
        var form = ParseForm(handler.RequestBody!);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal(ExpectedAssertionType, form["client_assertion_type"]);
        Assert.Equal("SIGNED.JWT.VALUE", form["client_assertion"]);
        Assert.DoesNotContain("{", handler.RequestBody);

        // --- レスポンス（JSON 逆直列化 → IssuedToken）の検証 ---
        Assert.Equal("stateless-token", issued.AccessToken);
        Assert.Equal(TimeSpan.FromSeconds(900), issued.Lifetime);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]   // invalid_grant / 鍵不一致
    [InlineData(HttpStatusCode.Unauthorized)] // 認証失敗
    [InlineData((HttpStatusCode)429)]         // レート制限
    public async Task IssueAsync_ErrorStatus_Surfaces_ApiException(HttpStatusCode status)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(
                "{\"error\":\"invalid_grant\"}", Encoding.UTF8, "application/json"),
        });
        var source = CreateSource(handler);

        var ex = await Assert.ThrowsAsync<ApiException>(() => source.IssueAsync());
        Assert.Equal((int)status, ex.ResponseStatusCode);
    }

    [Theory]
    [InlineData("{}")]                                            // 全欠落
    [InlineData("{\"expires_in\":900}")]                          // access_token 欠落
    [InlineData("{\"access_token\":\"token-value\"}")]            // expires_in 欠落
    [InlineData("{\"access_token\":\"token-value\",\"expires_in\":0}")]   // 非正の expires_in
    [InlineData("{\"access_token\":\"token-value\",\"expires_in\":-1}")]  // 負の expires_in
    public async Task IssueAsync_MissingFieldsInRawJson_Throws_InvalidOperation(string json)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var source = CreateSource(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.IssueAsync());
    }

    [Fact]
    public async Task IssueAsync_EmptyAssertion_Throws_InvalidOperation_Before_Http()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var adapter = new HttpClientRequestAdapter(
            new AnonymousAuthenticationProvider(), httpClient: new HttpClient(handler));
        var client = new ChannelAccessTokenClient(adapter);
        var source = new StatelessJwtAssertionTokenSource(client, _ => Task.FromResult(""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.IssueAsync());
        Assert.Null(handler.Request); // アサーション空なら HTTP には出ない
    }

    [Fact]
    public async Task IssueAsync_CanceledToken_Propagates_OperationCanceled()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var source = CreateSource(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.IssueAsync(cts.Token));
    }

    [Fact]
    public void GeneratedComposedBody_Cannot_Be_Form_Serialized_ByDesign()
    {
        // RATIONALE の特性化: 生成の合成ラッパ TokenPostRequestBody を素直に使い form で送ると、
        // Kiota Form シリアライザが入れ子オブジェクトを拒否する。StatelessJwtAssertionTokenSource が
        // 合成ラッパを回避して平坦モデルを直送している「理由」をテストとして固定する。
        // 将来 Kiota が合成ボディの form 直列化に対応したら本テストが落ち、回避策の撤去を検討できる。
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider());
        var client = new ChannelAccessTokenClient(adapter);
        var composed = new TokenRequestBuilder.TokenPostRequestBody
        {
            IssueStatelessChannelTokenByJWTAssertionRequest = new IssueStatelessChannelTokenByJWTAssertionRequest
            {
                GrantType = IssueStatelessChannelTokenByJWTAssertionRequest_grant_type.Client_credentials,
                ClientAssertionType =
                    IssueStatelessChannelTokenByJWTAssertionRequest_client_assertion_type
                        .UrnIetfParamsOauthClientAssertionTypeJwtBearer,
                ClientAssertion = "SIGNED.JWT.VALUE",
            },
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => client.Oauth2.V3.Token.ToPostRequestInformation(composed));
        Assert.Contains("nested objects", ex.Message);
    }

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                kv => Uri.UnescapeDataString(kv[0]),
                kv => kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty);

    // 送信された HttpRequestMessage と（読み取り済みの）ボディ文字列を記録し、
    // 固定レスポンスを返すモックハンドラ。実ネットワークには出ない。
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHandler(HttpResponseMessage response) => _response = response;

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            if (request.Content is not null)
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            return _response;
        }
    }
}
