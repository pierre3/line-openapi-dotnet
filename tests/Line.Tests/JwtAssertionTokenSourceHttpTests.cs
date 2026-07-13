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

namespace Line.Tests;

// Verifies JwtAssertionTokenSource's "real issue path" down to the transport layer.
// The existing JwtAssertionTokenSourceTests replaces IRequestAdapter.SendAsync<T>, so it
// exercises the issue logic + response validation but does not cover what the real HttpClientRequestAdapter handles:
//   - method/URL assembly for POST /oauth2/v2.1/token
//   - body serialization via application/x-www-form-urlencoded
//   - the Accept header
//   - deserialization of the JSON response
// Here we mock HttpMessageHandler and exercise the above through the real adapter.
public class JwtAssertionTokenSourceHttpTests
{
    private const string ExpectedAssertionType =
        "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private static JwtAssertionTokenSource CreateSource(RecordingHandler handler)
    {
        // Because BaseUrl is fixed when the client is constructed, build in adapter -> client order
        // (the default https://api.line.me is adopted).
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

        // --- Verification of the request (transport layer) ---
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

        // --- Verification of the response (JSON deserialization -> IssuedToken) ---
        Assert.Equal("real-token", issued.AccessToken);
        Assert.Equal(TimeSpan.FromSeconds(2592000), issued.Lifetime);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]   // invalid_grant / key mismatch
    [InlineData(HttpStatusCode.Unauthorized)] // authentication failure
    [InlineData((HttpStatusCode)429)]         // rate limit
    public async Task IssueAsync_ErrorStatus_Surfaces_ApiException(HttpStatusCode status)
    {
        // Because the generated PostAsync issues with errorMapping=default, non-2xx responses
        // do not reach JwtAssertionTokenSource's InvalidOperationException normalization;
        // Kiota's ApiException propagates directly to the caller. This behavior can only be exercised on the HTTP path.
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
    [InlineData("{}")]                                   // all missing
    [InlineData("{\"expires_in\":3600}")]                // access_token missing
    [InlineData("{\"access_token\":\"token-value\"}")]   // expires_in missing
    public async Task IssueAsync_MissingFieldsInRawJson_Throws_InvalidOperation(string json)
    {
        // The existing JwtAssertionTokenSourceTests has its stub return a pre-built model, so it does not go through real JSON
        // deserialization. Here we prove that when raw JSON is deserialized by the real adapter, missing fields
        // fall to null and the response validation is normalized to InvalidOperationException.
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

    // A mock handler that records the sent HttpRequestMessage and the (already read) body string,
    // and returns a fixed response. Does not go out to the real network.
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
