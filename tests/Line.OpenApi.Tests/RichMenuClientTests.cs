using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies RichMenuClient's convenience methods down to the transport layer, especially the
// control-plane (api.line.me) vs data-plane (api-data.line.me) split for image upload and the
// required image content type. Mocks HttpMessageHandler; does not hit the real network.
public class RichMenuClientTests
{
    private static RichMenuClient NewClient(RecordingHandler handler)
        => new RichMenuClient(new AnonymousAuthenticationProvider(), new HttpClient(handler));

    [Fact]
    public async Task CreateAsync_SendsPostToControlHost_And_ParsesId()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"richMenuId\":\"richmenu-abc\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var id = await client.CreateAsync(new RichMenuRequest
        {
            Name = "menu", ChatBarText = "tap", Selected = false,
        });

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/richmenu", handler.Request.RequestUri!.ToString());
        Assert.Equal("richmenu-abc", id);
    }

    [Fact]
    public async Task SetImageAsync_UploadsToDataHost_WithGivenContentType()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        using var image = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        await client.SetImageAsync("rm-1", image, "image/png");

        // The image endpoint must go to the DATA plane, and the content type LINE requires must be set.
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("api-data.line.me", handler.Request.RequestUri!.Host);
        Assert.Equal("/v2/bot/richmenu/rm-1/content", handler.Request.RequestUri.AbsolutePath);
        Assert.Equal("image/png", handler.Request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task SetImageFromFileAsync_InfersContentType_And_UploadsToDataHost()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        var path = Path.Combine(Path.GetTempPath(), $"richmenu-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, new byte[] { 9, 9, 9 });
        try
        {
            await client.SetImageFromFileAsync("rm-2", path);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Equal("api-data.line.me", handler.Request!.RequestUri!.Host);
        Assert.Equal("image/jpeg", handler.Request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task ValidateAsync_PostsToValidateEndpoint_OnControlHost()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.ValidateAsync(new RichMenuRequest { Name = "m", ChatBarText = "t", Selected = false });

        // The online dry-run path must hit the LINE validation endpoint (control host), not create.
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/richmenu/validate", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetImageAsync_GetsFromDataHost()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
        });
        var client = NewClient(handler);

        using var stream = await client.GetImageAsync("rm-1");

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("api-data.line.me", handler.Request.RequestUri!.Host);
        Assert.Equal("/v2/bot/richmenu/rm-1/content", handler.Request.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task LinkToUserAsync_PostsToTwoSegmentUserRichmenuUrl()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.LinkToUserAsync("Uabc", "rm-7");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/user/Uabc/richmenu/rm-7", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetDefaultIdAsync_Returns_Null_On_404()
    {
        // "No default rich menu" is a normal state LINE signals with 404; the facade maps it to null.
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"message\":\"no default richmenu\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        Assert.Null(await client.GetDefaultIdAsync());
    }

    [Fact]
    public async Task GetIdOfUserAsync_Returns_Null_On_404()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"message\":\"no richmenu\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        Assert.Null(await client.GetIdOfUserAsync("Uabc"));
    }

    [Fact]
    public async Task GetDefaultIdAsync_Propagates_Non404_Errors()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = NewClient(handler);

        await Assert.ThrowsAsync<Microsoft.Kiota.Abstractions.ApiException>(() => client.GetDefaultIdAsync());
    }

    [Fact]
    public async Task SetDefaultAsync_PostsToUserAllRichmenu()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler);

        await client.SetDefaultAsync("rm-9");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/user/all/richmenu/rm-9", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetIdOfUserAsync_GetsFromUserRichmenu_And_ParsesId()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"richMenuId\":\"rm-linked\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var id = await client.GetIdOfUserAsync("Uabc");

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/user/Uabc/richmenu", handler.Request.RequestUri!.ToString());
        Assert.Equal("rm-linked", id);
    }

    [Theory]
    [InlineData("menu.png", "image/png")]
    [InlineData("MENU.PNG", "image/png")]
    [InlineData("menu.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    public void InferImageContentType_MapsKnownExtensions(string file, string expected)
        => Assert.Equal(expected, RichMenuClient.InferImageContentType(file));

    [Theory]
    [InlineData("menu.gif")]
    [InlineData("menu.webp")]
    [InlineData("menu")]
    public void InferImageContentType_RejectsUnsupported(string file)
        => Assert.Throws<ArgumentException>(() => RichMenuClient.InferImageContentType(file));

    [Fact]
    public async Task CreateAsync_NullRequest_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => RichMenuClient.CreateWithStaticToken("t").CreateAsync(null!));

    [Fact]
    public async Task GetAsync_EmptyId_Throws()
        => await Assert.ThrowsAsync<ArgumentException>(() => RichMenuClient.CreateWithStaticToken("t").GetAsync(""));

    [Fact]
    public void CreateWithStaticToken_BuildsClient()
        => Assert.NotNull(RichMenuClient.CreateWithStaticToken("token").Messaging);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHandler(HttpResponseMessage response) => _response = response;

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return Task.FromResult(_response);
        }
    }
}
