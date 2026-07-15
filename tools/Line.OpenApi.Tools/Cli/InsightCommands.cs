using Cocona;
using Line.OpenApi.Tools.Output;
using Line.OpenApi.Tools.Services;

namespace Line.OpenApi.Tools.Cli;

/// <summary>
/// <c>line insight ...</c> — statistics lookup (all read-only). Dates use the yyyyMMdd format.
/// </summary>
internal sealed class InsightCommands
{
    private readonly CliRuntime _runtime;
    private readonly InsightService _insight;

    public InsightCommands(CliRuntime runtime, InsightService insight)
    {
        _runtime = runtime;
        _insight = insight;
    }

    [Command("demographic", Description = "Get the demographic attributes of the bot's friends.")]
    public Task<int> Demographic(GlobalOptions g) =>
        _runtime.ExecuteAsync(g, async () =>
            Json.Print(await _insight.GetDemographicsAsync(_runtime.Resolve(g), CancellationToken.None)));

    [Command("deliveries", Description = "Get the number of messages sent on a date (yyyyMMdd).")]
    public Task<int> Deliveries(GlobalOptions g, [Argument(Description = "Date (yyyyMMdd).")] string date) =>
        _runtime.ExecuteAsync(g, async () =>
            Json.Print(await _insight.GetDeliveriesAsync(_runtime.Resolve(g), date, CancellationToken.None)));

    [Command("followers", Description = "Get the number of followers as of a date (yyyyMMdd).")]
    public Task<int> Followers(GlobalOptions g, [Argument(Description = "Date (yyyyMMdd).")] string date) =>
        _runtime.ExecuteAsync(g, async () =>
            Json.Print(await _insight.GetFollowersAsync(_runtime.Resolve(g), date, CancellationToken.None)));

    [Command("events", Description = "Get the open/click statistics of a message by its request id.")]
    public Task<int> Events(GlobalOptions g, [Argument(Description = "Request id of a narrowcast/broadcast message.")] string requestId) =>
        _runtime.ExecuteAsync(g, async () =>
            Json.Print(await _insight.GetEventsAsync(_runtime.Resolve(g), requestId, CancellationToken.None)));

    [Command("per-unit", Description = "Get aggregated statistics for a custom aggregation unit over a period (yyyyMMdd).")]
    public Task<int> PerUnit(GlobalOptions g,
        [Argument(Description = "Custom aggregation unit name.")] string unit,
        [Option("from", Description = "Start date (yyyyMMdd).")] string from,
        [Option("to", Description = "End date (yyyyMMdd).")] string to) =>
        _runtime.ExecuteAsync(g, async () =>
            Json.Print(await _insight.GetPerUnitAsync(_runtime.Resolve(g), unit, from, to, CancellationToken.None)));

    [Command("richmenu-summary", Description = "Get the aggregate display/click statistics of a rich menu over a period (yyyyMMdd).")]
    public Task<int> RichMenuSummary(GlobalOptions g,
        [Argument(Description = "Rich menu id.")] string richMenuId,
        [Option("from", Description = "Start date (yyyyMMdd).")] string from,
        [Option("to", Description = "End date (yyyyMMdd).")] string to) =>
        _runtime.ExecuteAsync(g, async () =>
            Json.Print(await _insight.GetRichMenuSummaryAsync(_runtime.Resolve(g), richMenuId, from, to, CancellationToken.None)));

    [Command("richmenu-daily", Description = "Get the daily display/click statistics of a rich menu over a period (yyyyMMdd).")]
    public Task<int> RichMenuDaily(GlobalOptions g,
        [Argument(Description = "Rich menu id.")] string richMenuId,
        [Option("from", Description = "Start date (yyyyMMdd).")] string from,
        [Option("to", Description = "End date (yyyyMMdd).")] string to) =>
        _runtime.ExecuteAsync(g, async () =>
            Json.Print(await _insight.GetRichMenuDailyAsync(_runtime.Resolve(g), richMenuId, from, to, CancellationToken.None)));
}
