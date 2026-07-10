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
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Line.Poc.Tests;

// JwtAssertionTokenSource の「実発行経路」をトランスポート層まで含めて検証する。
// 既存の JwtAssertionTokenSourceTests は IRequestAdapter.SendAsync<T> を差し替えるため
// 発行ロジック＋応答検証は見るが、実 HttpClientRequestAdapter が担う
//   ・POST /oauth2/v2.1/token へのメソッド/URL 組み立て
//   ・application/x-www-form-urlencoded によるボディ直列化
//   ・Accept ヘッダ
//   ・JSON レスポンスの逆直列化
// は通らない。ここでは HttpMessageHandler をモックし、実アダプタ経由で上記を通す。
public class JwtAssertionTokenSourceHttpTests
{
    private const string ExpectedAssertionType =
        "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private static JwtAssertionTokenSource CreateSource(RecordingHandler handler)
    {
        // BaseUrl はクライアント構築時に確定するため、アダプタ→クライアントの順で構築する
        // （既定 https://api.line.me が採用される）。
        var adapter = new HttpClientRequestAdapter(
            new AnonymousAuthenticationProvider(),
            httpClient: new HttpClient(handler));
        var client = new ChannelAccessTokenClient(adapter);
        return new JwtAssertionTokenSource(client, _ => Task.FromResult("SIGNED.JWT.VALUE"));
    }

    [Fact]
    public async Task IssueAsync_SendsFormEncodedPost_And_ParsesJsonResponse()
    {
        var json =
            "{\"access_token\":\"real-token\",\"expires_in\":2592000,\"token_type\":\"Bearer\",\"key_id\":\"kid-1\"}";
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var source = CreateSource(handler);

        var issued = await source.IssueAsync();

        // --- リクエスト（トランスポート層）の検証 ---
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/oauth2/v2.1/token", handler.Request.RequestUri!.ToString());
        Assert.Equal(
            "application/x-www-form-urlencoded",
            handler.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("application/json", handler.Request.Headers.Accept.ToString());

        var form = ParseForm(handler.RequestBody!);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal(ExpectedAssertionType, form["client_assertion_type"]);
        Assert.Equal("SIGNED.JWT.VALUE", form["client_assertion"]);

        // --- レスポンス（JSON 逆直列化 → IssuedToken）の検証 ---
        Assert.Equal("real-token", issued.AccessToken);
        Assert.Equal(TimeSpan.FromSeconds(2592000), issued.Lifetime);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]   // invalid_grant / 鍵不一致
    [InlineData(HttpStatusCode.Unauthorized)] // 認証失敗
    [InlineData((HttpStatusCode)429)]         // レート制限
    public async Task IssueAsync_ErrorStatus_Surfaces_ApiException(HttpStatusCode status)
    {
        // 生成 PostAsync は errorMapping=default で発行するため、非 2xx は
        // JwtAssertionTokenSource の InvalidOperationException 正規化には到達せず、
        // Kiota の ApiException がそのまま呼び出し側へ抜ける。この挙動は HTTP 経路でしか踏めない。
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
    [InlineData("{}")]                                   // 全欠落
    [InlineData("{\"expires_in\":3600}")]                // access_token 欠落
    [InlineData("{\"access_token\":\"token-value\"}")]   // expires_in 欠落
    public async Task IssueAsync_MissingFieldsInRawJson_Throws_InvalidOperation(string json)
    {
        // 既存 JwtAssertionTokenSourceTests は構築済みモデルを stub が返すため、実 JSON
        // 逆直列化を経ていない。生 JSON を実アダプタで逆直列化した際に欠落フィールドが
        // null に落ち、応答検証が InvalidOperationException に正規化されることをここで実証する。
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var source = CreateSource(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.IssueAsync());
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
