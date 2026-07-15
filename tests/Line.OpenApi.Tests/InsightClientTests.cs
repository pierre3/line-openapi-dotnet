using System;
using System.Threading.Tasks;
using Line.OpenApi.Insight;
using Xunit;

namespace Line.OpenApi.Tests;

// Path verification for the InsightClient facade. Uses the generated builder's RequestInformation to confirm
// each read operation is assembled with the correct method/URL against the single host (api.line.me).
// The HTTP path (query params, deserialization) is verified separately in InsightClientHttpTests.
public class InsightClientTests
{
    [Fact]
    public void Demographic_BuildsGet_ToApiLineMe()
    {
        var client = InsightClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.V2.Bot.Insight.Demographic.ToGetRequestInformation();

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/v2/bot/insight/demographic", req.URI.AbsolutePath);
    }

    [Fact]
    public void RichMenuSummary_BuildsGet_WithRichMenuId()
    {
        var client = InsightClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.V2.Bot.Insight.Richmenu["rm-123"].Summary.ToGetRequestInformation();

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/v2/bot/insight/richmenu/rm-123/summary", req.URI.AbsolutePath);
    }

    // --- Argument guards for convenience methods (regression protection for the hand-written public contract) ---

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetNumberOfFollowersAsync_MissingDate_Throws(string? date)
    {
        var client = InsightClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetNumberOfFollowersAsync(date!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetMessageEventAsync_MissingRequestId_Throws(string? requestId)
    {
        var client = InsightClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetMessageEventAsync(requestId!));
    }

    [Fact]
    public async Task GetRichMenuInsightSummaryAsync_MissingRichMenuId_Throws()
    {
        var client = InsightClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetRichMenuInsightSummaryAsync("", "20260701", "20260715"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetNumberOfMessageDeliveriesAsync_MissingDate_Throws(string? date)
    {
        var client = InsightClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetNumberOfMessageDeliveriesAsync(date!));
    }

    [Fact]
    public async Task GetStatisticsPerUnitAsync_MissingArgs_Throws()
    {
        var client = InsightClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetStatisticsPerUnitAsync("unit", "", "20260715"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetStatisticsPerUnitAsync("unit", "20260701", ""));
    }

    [Fact]
    public async Task GetRichMenuInsightDailyAsync_MissingRichMenuId_Throws()
    {
        var client = InsightClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetRichMenuInsightDailyAsync("", "20260701", "20260715"));
    }
}
