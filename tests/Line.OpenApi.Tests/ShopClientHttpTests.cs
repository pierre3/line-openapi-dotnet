using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Shop;
using Line.OpenApi.Shop.Generated.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies ShopClient's convenience method down to the transport layer via a mock HttpMessageHandler:
//   - method/URL assembly (POST /shop/v3/mission)
//   - JSON body serialization / discarding of the empty-body response
//   - bearer-token host allow-listing
public class ShopClientHttpTests
{
    private static ShopClient NewClient(RecordingHandler handler)
        => new ShopClient(new AnonymousAuthenticationProvider(), new HttpClient(handler));

    [Fact]
    public async Task SendMissionStickerAsync_SendsPostJson()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.SendMissionStickerAsync(new MissionStickerRequest
        {
            To = "U123",
            ProductType = "STICKER",
            ProductId = "prod-1",
        });

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/shop/v3/mission", handler.Request.RequestUri!.ToString());
        Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("U123", handler.RequestBody);
        Assert.Contains("STICKER", handler.RequestBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task SendMissionStickerAsync_ErrorStatus_Surfaces_ApiException(HttpStatusCode status)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"message\":\"error\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<Microsoft.Kiota.Abstractions.ApiException>(
            () => client.SendMissionStickerAsync(new MissionStickerRequest { To = "U1" }));
        Assert.Equal((int)status, ex.ResponseStatusCode);
    }

    [Fact]
    public async Task SendMissionStickerAsync_OnApiLineMe_AddsBearerToken()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var provider = new StaticChannelAccessTokenProvider("STATIC-TOKEN", LineHosts.Api);
        var client = new ShopClient(
            new BaseBearerTokenAuthenticationProvider(provider), new HttpClient(handler));

        await client.SendMissionStickerAsync(new MissionStickerRequest { To = "U1" });

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
