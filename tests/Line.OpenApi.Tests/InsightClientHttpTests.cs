using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Insight;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies InsightClient's convenience methods down to the transport layer via a mock HttpMessageHandler:
//   - method/URL assembly and query-parameter placement (date, requestId, from/to, richMenuId path)
//   - JSON response deserialization
//   - bearer-token host allow-listing
// Does not go out to the real network.
public class InsightClientHttpTests
{
    private static InsightClient NewClient(RecordingHandler handler)
        => new InsightClient(new AnonymousAuthenticationProvider(), new HttpClient(handler));

    [Fact]
    public async Task GetFriendsDemographicsAsync_SendsGet()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"available\":true}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var res = await client.GetFriendsDemographicsAsync();

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/insight/demographic", handler.Request.RequestUri!.ToString());
        Assert.NotNull(res);
    }

    [Fact]
    public async Task GetNumberOfFollowersAsync_PutsDateQuery_And_ParsesJson()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"status\":\"ready\",\"followers\":42,\"targetedReaches\":40,\"blocks\":2}",
                Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var res = await client.GetNumberOfFollowersAsync("20260715");

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("api.line.me", handler.Request.RequestUri!.Host);
        Assert.Equal("/v2/bot/insight/followers", handler.Request.RequestUri.AbsolutePath);
        Assert.Contains("date=20260715", handler.Request.RequestUri.Query);
        Assert.Equal(42, res!.Followers);
    }

    [Fact]
    public async Task GetStatisticsPerUnitAsync_PutsAllQueryParams()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        await client.GetStatisticsPerUnitAsync("promotion_A", "20260701", "20260715");

        var query = handler.Request!.RequestUri!.Query;
        Assert.Contains("customAggregationUnit=promotion_A", query);
        Assert.Contains("from=20260701", query);
        Assert.Contains("to=20260715", query);
    }

    [Fact]
    public async Task GetRichMenuInsightDailyAsync_PutsRichMenuIdInPath()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        await client.GetRichMenuInsightDailyAsync("richmenu-abc", "20260701", "20260715");

        Assert.Equal("/v2/bot/insight/richmenu/richmenu-abc/daily", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Contains("from=20260701", handler.Request.RequestUri.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData((HttpStatusCode)429)]
    public async Task GetFriendsDemographicsAsync_ErrorStatus_Surfaces_ApiException(HttpStatusCode status)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"message\":\"error\"}", Encoding.UTF8, "application/json"),
        });
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<Microsoft.Kiota.Abstractions.ApiException>(
            () => client.GetFriendsDemographicsAsync());
        Assert.Equal((int)status, ex.ResponseStatusCode);
    }

    [Fact]
    public async Task GetFriendsDemographicsAsync_OnApiLineMe_AddsBearerToken()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var provider = new StaticChannelAccessTokenProvider("STATIC-TOKEN", LineHosts.Api);
        var client = new InsightClient(
            new BaseBearerTokenAuthenticationProvider(provider), new HttpClient(handler));

        await client.GetFriendsDemographicsAsync();

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("STATIC-TOKEN", handler.Request.Headers.Authorization.Parameter);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public RecordingHandler(HttpResponseMessage response) => _response = response;
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return Task.FromResult(_response);
        }
    }
}
