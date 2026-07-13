using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Liff;
using Line.OpenApi.Liff.Generated.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies LiffClient's convenience methods down to the transport layer.
// Mocks HttpMessageHandler and confirms, via the real HttpClientRequestAdapter, that:
//   - method/URL assembly (GET/POST /liff/v1/apps, PUT/DELETE /liff/v1/apps/{liffId})
//   - JSON body serialization / response deserialization
//   - discarding of empty-body responses (PUT/DELETE)
// all work correctly. Does not go out to the real network.
public class LiffClientHttpTests
{
    private static LiffClient NewClient(RecordingHandler handler)
        => new LiffClient(new AnonymousAuthenticationProvider(), new HttpClient(handler));

    [Fact]
    public async Task GetAppsAsync_SendsGet_And_ParsesJson()
    {
        var json =
            "{\"apps\":[{\"liffId\":\"liff-1\",\"view\":{\"type\":\"full\",\"url\":\"https://example.com\"}}]}";
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var apps = await client.GetAppsAsync();

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.line.me/liff/v1/apps", handler.Request.RequestUri!.ToString());
        Assert.NotNull(apps);
        Assert.Single(apps!.Apps!);
        Assert.Equal("liff-1", apps.Apps![0].LiffId);
    }

    [Fact]
    public async Task AddAppAsync_SendsPostJson_And_ParsesLiffId()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"liffId\":\"liff-new\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var request = new AddLiffAppRequest
        {
            View = new LiffView { Type = LiffView_type.Full, Url = "https://example.com" },
        };
        var added = await client.AddAppAsync(request);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/liff/v1/apps", handler.Request.RequestUri!.ToString());
        Assert.Equal(
            "application/json",
            handler.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("https://example.com", handler.RequestBody);
        Assert.Equal("liff-new", added!.LiffId);
    }

    [Fact]
    public async Task UpdateAppAsync_SendsPutJson_ToItemUrl()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.UpdateAppAsync("liff-123", new UpdateLiffAppRequest
        {
            Description = "updated",
        });

        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal(
            "https://api.line.me/liff/v1/apps/liff-123",
            handler.Request.RequestUri!.ToString());
        Assert.Contains("updated", handler.RequestBody);
    }

    [Fact]
    public async Task DeleteAppAsync_SendsDelete_ToItemUrl()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.DeleteAppAsync("liff-123");

        Assert.Equal(HttpMethod.Delete, handler.Request!.Method);
        Assert.Equal(
            "https://api.line.me/liff/v1/apps/liff-123",
            handler.Request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData((HttpStatusCode)429)]
    public async Task GetAppsAsync_ErrorStatus_Surfaces_ApiException(HttpStatusCode status)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"message\":\"error\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<Microsoft.Kiota.Abstractions.ApiException>(
            () => client.GetAppsAsync());
        Assert.Equal((int)status, ex.ResponseStatusCode);
    }

    [Fact]
    public async Task DeleteAppAsync_CanceledToken_Propagates_OperationCanceled()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.DeleteAppAsync("liff-123", cts.Token));
    }

    // Builds LiffClient with the same wiring as CreateWithStaticToken (passing LineHosts.Api to StaticChannelAccessTokenProvider)
    // and injects the mock handler. For end-to-end verification of the authentication layer.
    private static LiffClient NewAuthedClient(RecordingHandler handler, params string[] allowedHosts)
    {
        var provider = new StaticChannelAccessTokenProvider(
            "STATIC-TOKEN", allowedHosts is { Length: > 0 } ? allowedHosts : new[] { LineHosts.Api });
        var auth = new BaseBearerTokenAuthenticationProvider(provider);
        return new LiffClient(auth, new HttpClient(handler));
    }

    [Fact]
    public async Task GetAppsAsync_OnApiLineMe_AddsBearerToken()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"apps\":[]}", Encoding.UTF8, "application/json"),
        });
        var client = NewAuthedClient(handler);

        await client.GetAppsAsync();

        // Authorization: Bearer is attached for the allowed host (api.line.me).
        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("STATIC-TOKEN", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Request_ToDisallowedHost_WithholdsToken()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"apps\":[]}", Encoding.UTF8, "application/json"),
        });
        // By default only api.line.me is allowed. If it strays to a data-plane host, no token is attached.
        var client = NewAuthedClient(handler);

        await client.Api.Liff.V1.Apps
            .WithUrl("https://api-data.line.me/liff/v1/apps")
            .GetAsync();

        Assert.Equal("api-data.line.me", handler.Request!.RequestUri!.Host);
        Assert.Null(handler.Request.Headers.Authorization); // Do not send the token to a non-allowed host
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
