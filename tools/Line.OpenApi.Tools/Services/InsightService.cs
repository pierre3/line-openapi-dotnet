using System.Collections.Concurrent;
using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Insight;
using Line.OpenApi.Insight.Generated.Models;

namespace Line.OpenApi.Tools.Services;

/// <summary>
/// Statistics (Insight) lookup. Thin wrapper over the <see cref="InsightClient"/> facade
/// (single host <c>api.line.me</c>; R1 not applicable). All operations are read-only GETs;
/// date parameters use the <c>yyyyMMdd</c> format LINE expects. The generated response models are
/// plain data, so they are returned directly and serialized to JSON by the CLI/MCP adapters
/// (same pattern as <see cref="RichMenuService.GetAsync"/>).
/// </summary>
public sealed class InsightService
{
    // Memoized per token to avoid HttpClient accumulation in the long-running MCP server
    // (code gate Medium#1). CreateWithStaticToken preserves the api.line.me host restriction.
    private static readonly ConcurrentDictionary<string, InsightClient> Clients = new(StringComparer.Ordinal);

    private static InsightClient Create(ResolvedCredentials credentials) =>
        Clients.GetOrAdd(credentials.RequireAccessToken(), static token => InsightClient.CreateWithStaticToken(token));

    /// <summary>Gets the demographic attributes of the bot's friends.</summary>
    public Task<GetFriendsDemographicsResponse?> GetDemographicsAsync(ResolvedCredentials credentials, CancellationToken cancellationToken) =>
        Create(credentials).GetFriendsDemographicsAsync(cancellationToken);

    /// <summary>Gets the number of messages sent on a date (yyyyMMdd).</summary>
    public Task<GetNumberOfMessageDeliveriesResponse?> GetDeliveriesAsync(ResolvedCredentials credentials, string date, CancellationToken cancellationToken) =>
        Create(credentials).GetNumberOfMessageDeliveriesAsync(date, cancellationToken);

    /// <summary>Gets the number of followers as of a date (yyyyMMdd).</summary>
    public Task<GetNumberOfFollowersResponse?> GetFollowersAsync(ResolvedCredentials credentials, string date, CancellationToken cancellationToken) =>
        Create(credentials).GetNumberOfFollowersAsync(date, cancellationToken);

    /// <summary>Gets the open/click statistics of a narrowcast/broadcast message by request id.</summary>
    public Task<GetMessageEventResponse?> GetEventsAsync(ResolvedCredentials credentials, string requestId, CancellationToken cancellationToken) =>
        Create(credentials).GetMessageEventAsync(requestId, cancellationToken);

    /// <summary>Gets aggregated statistics for a custom aggregation unit over a period (yyyyMMdd).</summary>
    public Task<GetStatisticsPerUnitResponse?> GetPerUnitAsync(ResolvedCredentials credentials, string unit, string from, string to, CancellationToken cancellationToken) =>
        Create(credentials).GetStatisticsPerUnitAsync(unit, from, to, cancellationToken);

    /// <summary>Gets the aggregate display/click statistics of a rich menu over a period (yyyyMMdd).</summary>
    public Task<GetRichMenuInsightSummaryResponse?> GetRichMenuSummaryAsync(ResolvedCredentials credentials, string richMenuId, string from, string to, CancellationToken cancellationToken) =>
        Create(credentials).GetRichMenuInsightSummaryAsync(richMenuId, from, to, cancellationToken);

    /// <summary>Gets the daily display/click statistics of a rich menu over a period (yyyyMMdd).</summary>
    public Task<GetRichMenuInsightDailyResponse?> GetRichMenuDailyAsync(ResolvedCredentials credentials, string richMenuId, string from, string to, CancellationToken cancellationToken) =>
        Create(credentials).GetRichMenuInsightDailyAsync(richMenuId, from, to, cancellationToken);
}
