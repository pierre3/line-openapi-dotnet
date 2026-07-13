using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.ChannelAccessToken;
using Line.OpenApi.ChannelAccessToken.Generated;
using Line.OpenApi.ChannelAccessToken.Generated.Models;
using Line.OpenApi.ChannelAccessToken.Generated.Oauth2.V3.Token;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies the real issue path of StatelessJwtAssertionTokenSource (/oauth2/v3/token) down to the transport layer.
// In particular, proves that the oneOf composed body (TokenPostRequestBody / IComposedTypeWrapper) is serialized,
// via the real HttpClientRequestAdapter, into a flat application/x-www-form-urlencoded form body
// (i.e. that the hand-written helper correctly hides the composed wrapper).
public class StatelessJwtAssertionTokenSourceHttpTests
{
    private const string ExpectedAssertionType =
        "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private static StatelessJwtAssertionTokenSource CreateSource(RecordingHandler handler)
    {
        // Because BaseUrl is fixed when the client is constructed, build in adapter -> client order
        // (the default https://api.line.me is adopted).
        var adapter = new HttpClientRequestAdapter(
            new AnonymousAuthenticationProvider(),
            httpClient: new HttpClient(handler));
        var client = new ChannelAccessTokenClient(adapter);
        return new StatelessJwtAssertionTokenSource(client, _ => Task.FromResult("SIGNED.JWT.VALUE"));
    }

    [Fact]
    public async Task IssueAsync_SendsFormEncodedPost_To_V3_And_ParsesJsonResponse()
    {
        // Stateless tokens have a 15-minute (900-second) lifetime.
        var json =
            "{\"access_token\":\"stateless-token\",\"expires_in\":900,\"token_type\":\"Bearer\"}";
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var source = CreateSource(handler);

        var issued = await source.IssueAsync();

        // --- Verification of the request (transport layer) ---
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/oauth2/v3/token", handler.Request.RequestUri!.ToString());
        Assert.Equal(
            "application/x-www-form-urlencoded",
            handler.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("application/json", handler.Request.Headers.Accept.ToString());

        // The composed wrapper is expanded into flat form keys (not nested JSON).
        var form = ParseForm(handler.RequestBody!);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal(ExpectedAssertionType, form["client_assertion_type"]);
        Assert.Equal("SIGNED.JWT.VALUE", form["client_assertion"]);
        Assert.DoesNotContain("{", handler.RequestBody);

        // --- Verification of the response (JSON deserialization -> IssuedToken) ---
        Assert.Equal("stateless-token", issued.AccessToken);
        Assert.Equal(TimeSpan.FromSeconds(900), issued.Lifetime);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]   // invalid_grant / key mismatch
    [InlineData(HttpStatusCode.Unauthorized)] // authentication failure
    [InlineData((HttpStatusCode)429)]         // rate limit
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
    [InlineData("{}")]                                            // all missing
    [InlineData("{\"expires_in\":900}")]                          // access_token missing
    [InlineData("{\"access_token\":\"token-value\"}")]            // expires_in missing
    [InlineData("{\"access_token\":\"token-value\",\"expires_in\":0}")]   // non-positive expires_in
    [InlineData("{\"access_token\":\"token-value\",\"expires_in\":-1}")]  // negative expires_in
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
        Assert.Null(handler.Request); // with an empty assertion, nothing goes out over HTTP
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
        // RATIONALE characterization: naively using the generated composed wrapper TokenPostRequestBody and sending it as form causes
        // the Kiota Form serializer to reject the nested object. This test pins down the "reason" why StatelessJwtAssertionTokenSource
        // avoids the composed wrapper and sends a flat model directly.
        // If a future Kiota supports form serialization of composed bodies, this test will fail and removal of the workaround can be considered.
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
