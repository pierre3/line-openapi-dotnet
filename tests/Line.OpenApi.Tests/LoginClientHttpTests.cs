using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Line.OpenApi.Login;
using Line.OpenApi.Login.Models;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies LoginClient down to the transport layer via a mock HttpMessageHandler and the real
// Kiota DefaultRequestAdapter: method/URL assembly, form/JSON body content, Bearer attachment
// for user-token calls, and response deserialization. Does not go out to the real network.
public class LoginClientHttpTests
{
    private const string ChannelId = "1234567890";
    private const string ChannelSecret = "channel-secret";

    private static LoginClient NewClient(RecordingHandler handler)
        => new LoginClient(ChannelId, ChannelSecret, new HttpClient(handler));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ExchangeCodeAsync_PostsFormBody_And_ParsesTokens()
    {
        var handler = new RecordingHandler(Json(
            "{\"access_token\":\"AT\",\"expires_in\":2592000,\"refresh_token\":\"RT\"," +
            "\"id_token\":\"IDT\",\"scope\":\"profile openid\",\"token_type\":\"Bearer\"}"));
        var client = NewClient(handler);

        var token = await client.ExchangeCodeAsync("AUTH-CODE", "https://app/cb", "VERIFIER");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/oauth2/v2.1/token", handler.Request.RequestUri!.ToString());
        Assert.Equal(
            "application/x-www-form-urlencoded",
            handler.Request.Content!.Headers.ContentType!.MediaType);

        var form = HttpUtility.ParseQueryString(handler.RequestBody!);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("AUTH-CODE", form["code"]);
        Assert.Equal("https://app/cb", form["redirect_uri"]);
        Assert.Equal(ChannelId, form["client_id"]);
        Assert.Equal(ChannelSecret, form["client_secret"]);
        Assert.Equal("VERIFIER", form["code_verifier"]);

        Assert.Equal("AT", token!.AccessToken);
        Assert.Equal(2592000, token.ExpiresIn);
        Assert.Equal("RT", token.RefreshToken);
        Assert.Equal("IDT", token.IdToken);
    }

    [Fact]
    public async Task ExchangeCodeAsync_WithoutPkce_OmitsCodeVerifier()
    {
        var handler = new RecordingHandler(Json("{\"access_token\":\"AT\",\"expires_in\":1}"));
        var client = NewClient(handler);

        await client.ExchangeCodeAsync("AUTH-CODE", "https://app/cb");

        var form = HttpUtility.ParseQueryString(handler.RequestBody!);
        Assert.Null(form["code_verifier"]);
    }

    [Fact]
    public async Task RefreshTokenAsync_PostsRefreshGrant()
    {
        var handler = new RecordingHandler(Json("{\"access_token\":\"AT2\",\"expires_in\":2592000}"));
        var client = NewClient(handler);

        var token = await client.RefreshTokenAsync("RT");

        Assert.Equal("https://api.line.me/oauth2/v2.1/token", handler.Request!.RequestUri!.ToString());
        var form = HttpUtility.ParseQueryString(handler.RequestBody!);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("RT", form["refresh_token"]);
        Assert.Equal(ChannelId, form["client_id"]);
        Assert.Equal("AT2", token!.AccessToken);
    }

    [Fact]
    public async Task RevokeTokenAsync_PostsForm_NoContent()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.RevokeTokenAsync("AT");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/oauth2/v2.1/revoke", handler.Request.RequestUri!.ToString());
        var form = HttpUtility.ParseQueryString(handler.RequestBody!);
        Assert.Equal("AT", form["access_token"]);
        Assert.Equal(ChannelId, form["client_id"]);
        Assert.Equal(ChannelSecret, form["client_secret"]);
    }

    [Fact]
    public async Task VerifyAccessTokenAsync_SendsGetWithQuery()
    {
        var handler = new RecordingHandler(Json(
            "{\"scope\":\"profile\",\"client_id\":\"1234567890\",\"expires_in\":123}"));
        var client = NewClient(handler);

        var result = await client.VerifyAccessTokenAsync("AT");

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal(
            "https://api.line.me/oauth2/v2.1/verify?access_token=AT",
            handler.Request.RequestUri!.ToString());
        Assert.Equal("profile", result!.Scope);
        Assert.Equal(123, result.ExpiresIn);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_PostsForm_And_ParsesClaims()
    {
        var handler = new RecordingHandler(Json(
            "{\"iss\":\"https://access.line.me\",\"sub\":\"U123\",\"aud\":\"1234567890\"," +
            "\"exp\":1700000000,\"iat\":1699999000,\"name\":\"Alice\",\"amr\":[\"pwd\"]}"));
        var client = NewClient(handler);

        var claims = await client.VerifyIdTokenAsync("ID-TOKEN", nonce: "N1", expectedUserId: "U123");

        Assert.Equal("https://api.line.me/oauth2/v2.1/verify", handler.Request!.RequestUri!.ToString());
        var form = HttpUtility.ParseQueryString(handler.RequestBody!);
        Assert.Equal("ID-TOKEN", form["id_token"]);
        Assert.Equal(ChannelId, form["client_id"]);
        Assert.Equal("N1", form["nonce"]);
        Assert.Equal("U123", form["user_id"]);

        Assert.Equal("https://access.line.me", claims!.Iss);
        Assert.Equal("U123", claims.Sub);
        Assert.Equal("Alice", claims.Name);
        Assert.Equal(new[] { "pwd" }, claims.Amr!);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_OmitsOptionalFields_WhenNull()
    {
        var handler = new RecordingHandler(Json("{\"sub\":\"U1\"}"));
        var client = NewClient(handler);

        await client.VerifyIdTokenAsync("ID-TOKEN");

        var form = HttpUtility.ParseQueryString(handler.RequestBody!);
        Assert.Null(form["nonce"]);
        Assert.Null(form["user_id"]);
    }

    [Fact]
    public async Task GetProfileAsync_AttachesBearer_And_ParsesProfile()
    {
        var handler = new RecordingHandler(Json(
            "{\"userId\":\"U1\",\"displayName\":\"Alice\",\"pictureUrl\":\"https://img\"}"));
        var client = NewClient(handler);

        var profile = await client.GetProfileAsync("USER-TOKEN");

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/profile", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("USER-TOKEN", handler.Request.Headers.Authorization.Parameter);
        Assert.Equal("U1", profile!.UserId);
        Assert.Equal("Alice", profile.DisplayName);
    }

    [Fact]
    public async Task GetUserInfoAsync_AttachesBearer_ToUserInfoEndpoint()
    {
        var handler = new RecordingHandler(Json("{\"sub\":\"U1\",\"name\":\"Alice\"}"));
        var client = NewClient(handler);

        var info = await client.GetUserInfoAsync("USER-TOKEN");

        Assert.Equal("https://api.line.me/oauth2/v2.1/userinfo", handler.Request!.RequestUri!.ToString());
        Assert.Equal("USER-TOKEN", handler.Request.Headers.Authorization!.Parameter);
        Assert.Equal("U1", info!.Sub);
    }

    [Fact]
    public async Task GetFriendshipStatusAsync_AttachesBearer_And_ParsesFlag()
    {
        var handler = new RecordingHandler(Json("{\"friendFlag\":true}"));
        var client = NewClient(handler);

        var status = await client.GetFriendshipStatusAsync("USER-TOKEN");

        Assert.Equal("https://api.line.me/friendship/v1/status", handler.Request!.RequestUri!.ToString());
        Assert.Equal("USER-TOKEN", handler.Request.Headers.Authorization!.Parameter);
        Assert.True(status!.FriendFlag);
    }

    [Fact]
    public async Task DeauthorizeAsync_UsesChannelTokenHeader_And_UserTokenBody()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = NewClient(handler);

        await client.DeauthorizeAsync("CHANNEL-TOKEN", "USER-TOKEN");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/user/v1/deauthorize", handler.Request.RequestUri!.ToString());
        // The Authorization header is the channel access token, not the user token.
        Assert.Equal("CHANNEL-TOKEN", handler.Request.Headers.Authorization!.Parameter);
        Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("USER-TOKEN", handler.RequestBody);
        Assert.Contains("userAccessToken", handler.RequestBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData((HttpStatusCode)429)]
    public async Task ExchangeCode_ErrorStatus_Surfaces_OAuthError(HttpStatusCode status)
    {
        var handler = new RecordingHandler(Json(
            "{\"error\":\"invalid_grant\",\"error_description\":\"expired code\"}", status));
        var client = NewClient(handler);

        // The OAuth error body is surfaced as a typed LoginErrorResponse (derives from ApiException),
        // so error/error_description are not lost.
        var ex = await Assert.ThrowsAsync<LoginErrorResponse>(
            () => client.ExchangeCodeAsync("CODE", "https://app/cb"));
        Assert.Equal((int)status, ex.ResponseStatusCode);
        Assert.Equal("invalid_grant", ex.Error);
        Assert.Equal("expired code", ex.ErrorDescription);
        Assert.Contains("expired code", ex.Message);
    }

    [Fact]
    public async Task GetProfile_Unauthorized_Surfaces_OAuthError()
    {
        // Bearer GET path (most realistic failure: an expired/invalid user access token -> 401).
        var handler = new RecordingHandler(Json(
            "{\"error\":\"invalid_token\"}", HttpStatusCode.Unauthorized));
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<LoginErrorResponse>(() => client.GetProfileAsync("USER-TOKEN"));
        Assert.Equal(401, ex.ResponseStatusCode);
        Assert.Equal("invalid_token", ex.Error);
    }

    [Fact]
    public async Task RevokeToken_ErrorStatus_Surfaces_ApiException()
    {
        // SendNoContentAsync path (revoke/deauthorize) also maps non-2xx to the error type.
        var handler = new RecordingHandler(Json("{\"error\":\"invalid_request\"}", HttpStatusCode.BadRequest));
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<LoginErrorResponse>(() => client.RevokeTokenAsync("AT"));
        Assert.Equal(400, ex.ResponseStatusCode);
    }

    [Fact]
    public async Task TokenEndpoints_DoNotAttachAuthorizationHeader()
    {
        // Client credentials live in the body only; no Authorization header on the anonymous path.
        var handler = new RecordingHandler(Json("{\"access_token\":\"AT\",\"expires_in\":1}"));
        var client = NewClient(handler);

        await client.ExchangeCodeAsync("CODE", "https://app/cb");
        Assert.Null(handler.Request!.Headers.Authorization);
    }

    [Fact]
    public async Task GetProfile_WithDisallowedHost_WithholdsBearerToken()
    {
        // Client-level host gating: if api.line.me is not in the allow list, the user token is
        // never attached (wiring from LoginClient -> StaticBearerTokenProvider).
        var handler = new RecordingHandler(Json("{\"userId\":\"U1\"}"));
        var client = new LoginClient(
            ChannelId, ChannelSecret, new HttpClient(handler), new[] { "other.example.com" });

        await client.GetProfileAsync("USER-TOKEN");

        Assert.Equal("api.line.me", handler.Request!.RequestUri!.Host);
        Assert.Null(handler.Request.Headers.Authorization); // token withheld for non-allowed host
    }

    [Fact]
    public async Task CanceledToken_Propagates_OperationCanceled()
    {
        var handler = new RecordingHandler(Json("{\"access_token\":\"AT\",\"expires_in\":1}"));
        var client = NewClient(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.RefreshTokenAsync("RT", cts.Token));
    }

    [Fact]
    public void Constructor_Rejects_MissingCredentials()
    {
        Assert.Throws<ArgumentException>(() => new LoginClient("", ChannelSecret));
        Assert.Throws<ArgumentException>(() => new LoginClient(ChannelId, ""));
    }

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
