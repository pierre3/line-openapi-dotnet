using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Line.OpenApi.MiniApp;
using Line.OpenApi.MiniApp.Models;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies MiniAppClient down to the transport layer via a mock HttpMessageHandler and the real
// Kiota DefaultRequestAdapter: method/URL/query assembly, JSON body content, Bearer attachment
// for channel/user-token calls, and response/error deserialization. Does not go out to the real network.
public class MiniAppClientHttpTests
{
    private static MiniAppClient NewClient(RecordingHandler handler, string[]? allowedHosts = null)
        => new MiniAppClient(new HttpClient(handler), allowedHosts);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task IssueNotificationTokenAsync_PostsJsonBody_And_ParsesToken()
    {
        var handler = new RecordingHandler(Json(
            "{\"notificationToken\":\"NT1\",\"expiresIn\":31536000,\"remainingCount\":5,\"sessionId\":\"S1\"}"));
        var client = NewClient(handler);

        var token = await client.IssueNotificationTokenAsync("CHANNEL-TOKEN", "LIFF-TOKEN");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/message/v3/notifier/token", handler.Request.RequestUri!.ToString());
        Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("CHANNEL-TOKEN", handler.Request.Headers.Authorization.Parameter);
        Assert.Contains("\"liffAccessToken\":\"LIFF-TOKEN\"", handler.RequestBody);

        Assert.Equal("NT1", token!.NotificationToken);
        Assert.Equal(31536000, token.ExpiresIn);
        Assert.Equal(5, token.RemainingCount);
        Assert.Equal("S1", token.SessionId);
    }

    [Fact]
    public async Task SendServiceMessageAsync_PostsTemplateAndParams_WithTargetQuery()
    {
        var handler = new RecordingHandler(Json(
            "{\"notificationToken\":\"NT2\",\"expiresIn\":100,\"remainingCount\":4,\"sessionId\":\"S1\"}"));
        var client = NewClient(handler);

        var parameters = new Dictionary<string, string> { ["orderName"] = "Widget", ["price"] = "1000" };
        var token = await client.SendServiceMessageAsync(
            "CHANNEL-TOKEN", "NT1", "order-complete_en", parameters);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            "https://api.line.me/message/v3/notifier/send?target=service",
            handler.Request.RequestUri!.ToString());
        Assert.Contains("\"templateName\":\"order-complete_en\"", handler.RequestBody);
        Assert.Contains("\"notificationToken\":\"NT1\"", handler.RequestBody);
        Assert.Contains("\"params\":{", handler.RequestBody);
        Assert.Contains("\"orderName\":\"Widget\"", handler.RequestBody);
        Assert.Contains("\"price\":\"1000\"", handler.RequestBody);

        Assert.Equal("NT2", token!.NotificationToken);
        Assert.Equal(4, token.RemainingCount);
    }

    [Fact]
    public async Task ReserveProductAsync_UsesUserToken_And_PostsFields()
    {
        var handler = new RecordingHandler(Json("{\"orderId\":\"ORDER1\"}"));
        var client = NewClient(handler);

        var result = await client.ReserveProductAsync(
            "USER-TOKEN", "203.0.113.1", "ios", "PRODUCT1", "Gold Pack");

        Assert.Equal("https://api.line.me/iap/v1/product/reserve", handler.Request!.RequestUri!.ToString());
        Assert.Equal("USER-TOKEN", handler.Request.Headers.Authorization!.Parameter);
        Assert.Contains("\"clientIp\":\"203.0.113.1\"", handler.RequestBody);
        Assert.Contains("\"clientOs\":\"ios\"", handler.RequestBody);
        Assert.Contains("\"productId\":\"PRODUCT1\"", handler.RequestBody);
        Assert.Contains("\"shopProductName\":\"Gold Pack\"", handler.RequestBody);
        Assert.Equal("ORDER1", result!.OrderId);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_SendsGetWithQuery_And_ParsesEvents()
    {
        var handler = new RecordingHandler(Json(
            "{\"events\":[{\"transactionType\":\"PRODUCT\",\"event\":{\"type\":\"purchaseComplete\"," +
            "\"orderId\":\"O1\",\"productId\":\"P1\",\"userId\":\"U1\",\"purchaseTimestamp\":1700000000," +
            "\"channelId\":\"C1\"}}],\"nextCursor\":\"CUR2\"}"));
        var client = NewClient(handler);

        var page = await client.GetWebhookEventsAsync(
            "CHANNEL-TOKEN", 1700000000, 1700086400, 50, cursor: "CUR1", status: "SUCCESS");

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        var uri = handler.Request.RequestUri!;
        Assert.Equal("/iap/v1/webhook/events", uri.AbsolutePath);
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("1700000000", query["startEpochSeconds"]);
        Assert.Equal("1700086400", query["endEpochSeconds"]);
        Assert.Equal("50", query["pageSize"]);
        Assert.Equal("CUR1", query["cursor"]);
        Assert.Equal("SUCCESS", query["status"]);

        Assert.Equal("CHANNEL-TOKEN", handler.Request.Headers.Authorization!.Parameter);
        var entry = Assert.Single(page!.Events!);
        Assert.Equal("PRODUCT", entry.TransactionType);
        Assert.Equal("purchaseComplete", entry.Event!.Type);
        Assert.Equal("O1", entry.Event.OrderId);
        Assert.Equal("CUR2", page.NextCursor);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_OmitsCursorAndStatus_WhenNull()
    {
        var handler = new RecordingHandler(Json("{\"events\":[]}"));
        var client = NewClient(handler);

        await client.GetWebhookEventsAsync("CHANNEL-TOKEN", 1700000000, 1700086400, 50);

        var query = HttpUtility.ParseQueryString(handler.Request!.RequestUri!.Query);
        Assert.Null(query["cursor"]);
        Assert.Null(query["status"]);
        Assert.Equal("50", query["pageSize"]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task GetWebhookEventsAsync_Accepts_PageSizeBoundaries(int pageSize)
    {
        var handler = new RecordingHandler(Json("{\"events\":[]}"));
        var client = NewClient(handler);

        var page = await client.GetWebhookEventsAsync("CHANNEL-TOKEN", 1, 2, pageSize);

        Assert.NotNull(page);
        Assert.Equal(pageSize.ToString(), HttpUtility.ParseQueryString(handler.Request!.RequestUri!.Query)["pageSize"]);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_Rejects_PageSizeOutOfRange()
    {
        var handler = new RecordingHandler(Json("{}"));
        var client = NewClient(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetWebhookEventsAsync("CHANNEL-TOKEN", 1, 2, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetWebhookEventsAsync("CHANNEL-TOKEN", 1, 2, 101));
    }

    [Fact]
    public async Task SendServiceMessage_ErrorStatus_Surfaces_NotifierErrorResponse()
    {
        var handler = new RecordingHandler(Json(
            "{\"message\":\"template not found\"}", HttpStatusCode.Forbidden));
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<NotifierErrorResponse>(() => client.SendServiceMessageAsync(
            "CHANNEL-TOKEN", "NT1", "bad-template_en", new Dictionary<string, string>()));
        Assert.Equal(403, ex.ResponseStatusCode);
        Assert.Equal("template not found", ex.Message);
    }

    [Fact]
    public async Task GetWebhookEvents_ErrorStatus_Surfaces_IapErrorResponse()
    {
        var handler = new RecordingHandler(Json(
            "{\"errorCode\":\"VALIDATION_ERROR\",\"message\":\"bad range\"}", HttpStatusCode.BadRequest));
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<IapErrorResponse>(
            () => client.GetWebhookEventsAsync("CHANNEL-TOKEN", 2, 1, 10));
        Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
    }

    public static IEnumerable<object[]> ArgumentGuardCases()
    {
        yield return new object[]
        {
            (Func<MiniAppClient, Task>)(c => c.IssueNotificationTokenAsync("", "liff")),
        };
        yield return new object[]
        {
            (Func<MiniAppClient, Task>)(c => c.IssueNotificationTokenAsync("token", "")),
        };
        yield return new object[]
        {
            (Func<MiniAppClient, Task>)(c => c.SendServiceMessageAsync(
                "", "nt", "tpl_en", new Dictionary<string, string>())),
        };
        yield return new object[]
        {
            (Func<MiniAppClient, Task>)(c => c.ReserveProductAsync("", "1.2.3.4", "ios", "P1", "Name")),
        };
        yield return new object[]
        {
            (Func<MiniAppClient, Task>)(c => c.ReserveProductAsync("token", "", "ios", "P1", "Name")),
        };
        yield return new object[]
        {
            (Func<MiniAppClient, Task>)(c => c.GetWebhookEventsAsync("", 1, 2, 10)),
        };
    }

    [Theory]
    [MemberData(nameof(ArgumentGuardCases))]
    public async Task Methods_Reject_MissingRequiredArguments(Func<MiniAppClient, Task> call)
    {
        var handler = new RecordingHandler(Json("{}"));
        var client = NewClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => call(client));
        Assert.Null(handler.Request); // rejected before any HTTP call was made
    }

    [Fact]
    public async Task IssueNotificationToken_ErrorStatus_Surfaces_NotifierErrorResponse()
    {
        var handler = new RecordingHandler(Json(
            "{\"message\":\"invalid liff access token\"}", HttpStatusCode.Unauthorized));
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<NotifierErrorResponse>(
            () => client.IssueNotificationTokenAsync("CHANNEL-TOKEN", "BAD-LIFF-TOKEN"));
        Assert.Equal(401, ex.ResponseStatusCode);
        Assert.Equal("invalid liff access token", ex.Message);
    }

    [Fact]
    public async Task ReserveProduct_ErrorStatus_Surfaces_IapErrorResponse_WithErrorCode()
    {
        var handler = new RecordingHandler(Json(
            "{\"errorCode\":\"PRODUCT_ID_NOT_FOUND\",\"message\":\"no such product\"}",
            HttpStatusCode.BadRequest));
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<IapErrorResponse>(
            () => client.ReserveProductAsync("USER-TOKEN", "203.0.113.1", "ios", "BAD", "Name"));
        Assert.Equal(400, ex.ResponseStatusCode);
        Assert.Equal("PRODUCT_ID_NOT_FOUND", ex.ErrorCode);
        Assert.Contains("PRODUCT_ID_NOT_FOUND", ex.Message);
        Assert.Contains("no such product", ex.Message);
    }

    [Fact]
    public async Task ReserveProduct_WithDisallowedHost_WithholdsBearerToken()
    {
        // Client-level host gating: if api.line.me is not in the allow list, the user token is
        // never attached (wiring from MiniAppClient -> StaticBearerTokenProvider).
        var handler = new RecordingHandler(Json("{\"orderId\":\"O1\"}"));
        var client = NewClient(handler, new[] { "other.example.com" });

        await client.ReserveProductAsync("USER-TOKEN", "203.0.113.1", "ios", "P1", "Name");

        Assert.Equal("api.line.me", handler.Request!.RequestUri!.Host);
        Assert.Null(handler.Request.Headers.Authorization); // token withheld for non-allowed host
    }

    [Fact]
    public async Task CanceledToken_Propagates_OperationCanceled()
    {
        var handler = new RecordingHandler(Json("{\"orderId\":\"O1\"}"));
        var client = NewClient(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ReserveProductAsync(
                "USER-TOKEN", "203.0.113.1", "ios", "P1", "Name", cts.Token));
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
