using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Insight.Generated;
using Line.OpenApi.Insight.Generated.Models;

namespace Line.OpenApi.Insight;

/// <summary>
/// Facade for the Insight (statistics) API. It wraps a single-host (api.line.me) Kiota client and
/// provides convenience methods for the seven read operations the spec defines (friend
/// demographics, number of message deliveries, number of followers, message events, per-unit
/// statistics, and rich menu insight summary / daily).
///
/// All operations are GET; date parameters use the <c>yyyyMMdd</c> format LINE expects.
/// For lower-level operations, the generated builders are directly accessible via <see cref="Api"/>.
///
/// Usage:
///   var insight = InsightClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
///   var demographics = await insight.GetFriendsDemographicsAsync();
///   var followers = await insight.GetNumberOfFollowersAsync("20260715");
/// </summary>
public sealed class InsightClient
{
    /// <summary>The generated client (exposed for low-level operations).</summary>
    public InsightApiClient Api { get; }

    /// <param name="authProvider">Authentication provider (static or refreshing, either works).</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> shared by the adapter. Supplied by <c>IHttpClientFactory</c>
    /// via DI. When null, the adapter creates its own default <see cref="HttpClient"/>.
    /// </param>
    public InsightClient(IAuthenticationProvider authProvider, HttpClient? httpClient = null)
    {
        if (authProvider is null) throw new ArgumentNullException(nameof(authProvider));

        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        Api = new InsightApiClient(adapter);
    }

    /// <summary>Helper to quickly construct from a long-lived channel access token.</summary>
    public static InsightClient CreateWithStaticToken(string channelAccessToken)
    {
        var provider = new StaticChannelAccessTokenProvider(channelAccessToken, LineHosts.Api);
        var auth = new BaseBearerTokenAuthenticationProvider(provider);
        return new InsightClient(auth);
    }

    /// <summary>Gets the demographic attributes of the bot's friends (GET /v2/bot/insight/demographic).</summary>
    public Task<GetFriendsDemographicsResponse?> GetFriendsDemographicsAsync(
        CancellationToken cancellationToken = default)
        => Api.V2.Bot.Insight.Demographic.GetAsync(cancellationToken: cancellationToken);

    /// <summary>Gets the number of messages sent on <paramref name="date"/> (yyyyMMdd) (GET /v2/bot/insight/message/delivery).</summary>
    public Task<GetNumberOfMessageDeliveriesResponse?> GetNumberOfMessageDeliveriesAsync(
        string date, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(date)) throw new ArgumentException("date is required", nameof(date));
        return Api.V2.Bot.Insight.Message.Delivery.GetAsync(
            config => config.QueryParameters.Date = date, cancellationToken);
    }

    /// <summary>Gets the number of followers as of <paramref name="date"/> (yyyyMMdd) (GET /v2/bot/insight/followers).</summary>
    public Task<GetNumberOfFollowersResponse?> GetNumberOfFollowersAsync(
        string date, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(date)) throw new ArgumentException("date is required", nameof(date));
        return Api.V2.Bot.Insight.Followers.GetAsync(
            config => config.QueryParameters.Date = date, cancellationToken);
    }

    /// <summary>Gets the open/click statistics of a narrowcast/broadcast message by <paramref name="requestId"/> (GET /v2/bot/insight/message/event).</summary>
    public Task<GetMessageEventResponse?> GetMessageEventAsync(
        string requestId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(requestId)) throw new ArgumentException("requestId is required", nameof(requestId));
        return Api.V2.Bot.Insight.Message.Event.GetAsync(
            config => config.QueryParameters.RequestId = requestId, cancellationToken);
    }

    /// <summary>
    /// Gets aggregated statistics of messages sent with a custom aggregation unit
    /// (GET /v2/bot/insight/message/event/aggregation). <paramref name="from"/> and
    /// <paramref name="to"/> use the yyyyMMdd format.
    /// </summary>
    public Task<GetStatisticsPerUnitResponse?> GetStatisticsPerUnitAsync(
        string customAggregationUnit, string from, string to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(customAggregationUnit)) throw new ArgumentException("customAggregationUnit is required", nameof(customAggregationUnit));
        if (string.IsNullOrEmpty(from)) throw new ArgumentException("from is required", nameof(from));
        if (string.IsNullOrEmpty(to)) throw new ArgumentException("to is required", nameof(to));
        return Api.V2.Bot.Insight.Message.Event.Aggregation.GetAsync(config =>
        {
            config.QueryParameters.CustomAggregationUnit = customAggregationUnit;
            config.QueryParameters.From = from;
            config.QueryParameters.To = to;
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the aggregate display/click statistics of a rich menu over a period
    /// (GET /v2/bot/insight/richmenu/{richMenuId}/summary). Dates use the yyyyMMdd format.
    /// </summary>
    public Task<GetRichMenuInsightSummaryResponse?> GetRichMenuInsightSummaryAsync(
        string richMenuId, string from, string to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(richMenuId)) throw new ArgumentException("richMenuId is required", nameof(richMenuId));
        if (string.IsNullOrEmpty(from)) throw new ArgumentException("from is required", nameof(from));
        if (string.IsNullOrEmpty(to)) throw new ArgumentException("to is required", nameof(to));
        return Api.V2.Bot.Insight.Richmenu[richMenuId].Summary.GetAsync(config =>
        {
            config.QueryParameters.From = from;
            config.QueryParameters.To = to;
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the daily display/click statistics of a rich menu over a period
    /// (GET /v2/bot/insight/richmenu/{richMenuId}/daily). Dates use the yyyyMMdd format.
    /// </summary>
    public Task<GetRichMenuInsightDailyResponse?> GetRichMenuInsightDailyAsync(
        string richMenuId, string from, string to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(richMenuId)) throw new ArgumentException("richMenuId is required", nameof(richMenuId));
        if (string.IsNullOrEmpty(from)) throw new ArgumentException("from is required", nameof(from));
        if (string.IsNullOrEmpty(to)) throw new ArgumentException("to is required", nameof(to));
        return Api.V2.Bot.Insight.Richmenu[richMenuId].Daily.GetAsync(config =>
        {
            config.QueryParameters.From = from;
            config.QueryParameters.To = to;
        }, cancellationToken);
    }
}
