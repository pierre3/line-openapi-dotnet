using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.ManageAudience;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies ManageAudienceClient down to the transport layer via a mock HttpMessageHandler.
// The important cases for this package (control/data split + multipart, no repo precedent):
//   (a) the by-file uploads route to the DATA plane (api-data.line.me), control ops to api.line.me
//   (b) the multipart body actually contains the file part and the scalar parts
//   (c) JSON response deserialization (POST returns audienceGroupId)
//   (d) bearer-token host allow-listing on both planes
public class ManageAudienceClientHttpTests
{
    private static ManageAudienceClient NewClient(RecordingHandler handler)
        => new ManageAudienceClient(new AnonymousAuthenticationProvider(), new HttpClient(handler));

    private static Stream TextFile(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task UploadUserIdsByFileAsync_PostsMultipart_ToDataPlane_And_ParsesResponse()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                "{\"audienceGroupId\":1234567890123,\"type\":\"UPLOAD\",\"description\":\"aud\"}",
                Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var res = await client.UploadUserIdsByFileAsync(
            TextFile("U0001\nU0002\n"), description: "my audience", isIfaAudience: false);

        // (a) routes to the data plane, correct method + path.
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("api-data.line.me", handler.Request.RequestUri!.Host);
        Assert.Equal("/v2/bot/audienceGroup/upload/byFile", handler.Request.RequestUri.AbsolutePath);
        // (b) multipart body carries the file content and the scalar parts.
        Assert.StartsWith("multipart/form-data", handler.Request.Content!.Headers.ContentType!.MediaType!);
        Assert.Contains("name=\"file\"", handler.RequestBody);
        Assert.Contains("U0001", handler.RequestBody);
        Assert.Contains("name=\"description\"", handler.RequestBody);
        Assert.Contains("my audience", handler.RequestBody);
        Assert.Contains("name=\"isIfaAudience\"", handler.RequestBody);
        // (c) response deserialized.
        Assert.Equal(1234567890123L, res!.AudienceGroupId);
    }

    [Fact]
    public async Task AddUserIdsByFileAsync_PutsMultipart_ToDataPlane_WithAudienceGroupId()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Accepted));
        var client = NewClient(handler);

        await client.AddUserIdsByFileAsync(1234567890123L, TextFile("U0003\n"), uploadDescription: "job-1");

        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal("https://api-data.line.me/v2/bot/audienceGroup/upload/byFile",
            handler.Request.RequestUri!.ToString());
        Assert.StartsWith("multipart/form-data", handler.Request.Content!.Headers.ContentType!.MediaType!);
        Assert.Contains("name=\"audienceGroupId\"", handler.RequestBody);
        Assert.Contains("1234567890123", handler.RequestBody);
        Assert.Contains("name=\"file\"", handler.RequestBody);
        Assert.Contains("U0003", handler.RequestBody);
    }

    [Fact]
    public async Task GetAudienceDataAsync_RoutesToControlPlane()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        await client.GetAudienceDataAsync(1234567890123L);

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("api.line.me", handler.Request.RequestUri!.Host);
        Assert.Equal("/v2/bot/audienceGroup/1234567890123", handler.Request.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task DeleteAudienceGroupAsync_RoutesToControlPlane_Delete()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.DeleteAudienceGroupAsync(1234567890123L);

        Assert.Equal(HttpMethod.Delete, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/audienceGroup/1234567890123",
            handler.Request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task UploadUserIdsByFileAsync_ErrorStatus_Surfaces_ApiException(HttpStatusCode status)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"message\":\"error\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<Microsoft.Kiota.Abstractions.ApiException>(
            () => client.UploadUserIdsByFileAsync(TextFile("U1")));
        Assert.Equal((int)status, ex.ResponseStatusCode);
    }

    private static ManageAudienceClient NewAuthedClient(RecordingHandler handler)
    {
        var provider = new StaticChannelAccessTokenProvider(
            "STATIC-TOKEN", LineHosts.Api, LineHosts.ApiData);
        return new ManageAudienceClient(new BaseBearerTokenAuthenticationProvider(provider), new HttpClient(handler));
    }

    [Fact]
    public async Task UploadByFile_OnDataPlane_AddsBearerToken()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("{\"audienceGroupId\":1}", Encoding.UTF8, "application/json"),
        });
        var client = NewAuthedClient(handler);

        await client.UploadUserIdsByFileAsync(TextFile("U1"));

        // api-data.line.me is allowed, so the bearer token is attached on the data plane too.
        Assert.Equal("api-data.line.me", handler.Request!.RequestUri!.Host);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("STATIC-TOKEN", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Request_ToDisallowedHost_WithholdsToken()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        // Only api.line.me / api-data.line.me are allowed. A stray host gets no token.
        var client = NewAuthedClient(handler);

        await client.Api.V2.Bot.AudienceGroup.List
            .WithUrl("https://example.com/v2/bot/audienceGroup/list")
            .GetAsync();

        Assert.Equal("example.com", handler.Request!.RequestUri!.Host);
        Assert.Null(handler.Request.Headers.Authorization);
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
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return _response;
        }
    }
}
