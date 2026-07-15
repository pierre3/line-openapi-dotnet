using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Module;
using Line.OpenApi.Module.Generated.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies ModuleClient's convenience methods down to the transport layer via a mock HttpMessageHandler:
//   - method/URL assembly (GET /v2/bot/list, POST chat control acquire/release, POST channel/detach)
//   - JSON body serialization / response deserialization
//   - discarding of empty-body POST responses
//   - bearer-token host allow-listing
public class ModuleClientHttpTests
{
    private static ModuleClient NewClient(RecordingHandler handler)
        => new ModuleClient(new AnonymousAuthenticationProvider(), new HttpClient(handler));

    [Fact]
    public async Task GetModulesAsync_PutsPagingQuery_And_ParsesJson()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"bots\":[{\"name\":\"bot-1\",\"basicId\":\"@abc\"}],\"next\":\"tok\"}",
                Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var res = await client.GetModulesAsync(start: "cursor", limit: 50);

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("/v2/bot/list", handler.Request.RequestUri!.AbsolutePath);
        Assert.Contains("start=cursor", handler.Request.RequestUri.Query);
        Assert.Contains("limit=50", handler.Request.RequestUri.Query);
        Assert.Single(res!.Bots!);
        Assert.Equal("tok", res.Next);
    }

    [Fact]
    public async Task AcquireChatControlAsync_SendsPostJson_ToChatUrl()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.AcquireChatControlAsync("chat-42", new AcquireChatControlRequest { Expired = true, Ttl = 3600 });

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/chat/chat-42/control/acquire", handler.Request.RequestUri!.ToString());
        Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("3600", handler.RequestBody);
    }

    [Fact]
    public async Task ReleaseChatControlAsync_SendsPost_NoBody()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.ReleaseChatControlAsync("chat-42");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/chat/chat-42/control/release", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task DetachAsync_SendsPostJson()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.DetachAsync(new DetachModuleRequest { BotId = "bot-9" });

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/channel/detach", handler.Request.RequestUri!.ToString());
        Assert.Contains("bot-9", handler.RequestBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task GetModulesAsync_ErrorStatus_Surfaces_ApiException(HttpStatusCode status)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"message\":\"error\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<Microsoft.Kiota.Abstractions.ApiException>(
            () => client.GetModulesAsync());
        Assert.Equal((int)status, ex.ResponseStatusCode);
    }

    [Fact]
    public async Task GetModulesAsync_OnApiLineMe_AddsBearerToken()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"bots\":[]}", Encoding.UTF8, "application/json"),
        });
        var provider = new StaticChannelAccessTokenProvider("STATIC-TOKEN", LineHosts.Api);
        var client = new ModuleClient(
            new BaseBearerTokenAuthenticationProvider(provider), new HttpClient(handler));

        await client.GetModulesAsync();

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("STATIC-TOKEN", handler.Request.Headers.Authorization.Parameter);
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
