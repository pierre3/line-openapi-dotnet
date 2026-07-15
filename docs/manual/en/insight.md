# Insight (Statistics)

@Line.OpenApi.Insight.InsightClient is the facade for the Insight API. It uses a single host
(`api.line.me`) and exposes the seven read operations as convenience methods. All operations are
GET; date parameters use the `yyyyMMdd` format LINE expects. For anything lower-level,
`InsightClient.Api` exposes the generated builders directly.

```csharp
using Line.OpenApi.Insight;

var insight = InsightClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## Friend demographics

```csharp
var demographics = await insight.GetFriendsDemographicsAsync();
```

## Number of message deliveries / followers

```csharp
var deliveries = await insight.GetNumberOfMessageDeliveriesAsync("20260715");
var followers = await insight.GetNumberOfFollowersAsync("20260715");
```

## Message events and per-unit statistics

```csharp
// Open/click statistics for a narrowcast/broadcast message (by requestId).
var events = await insight.GetMessageEventAsync("REQUEST_ID");

// Aggregated statistics with a custom aggregation unit over a period.
var stats = await insight.GetStatisticsPerUnitAsync("promotion_A", "20260701", "20260715");
```

## Rich menu insights

```csharp
var summary = await insight.GetRichMenuInsightSummaryAsync("RICH_MENU_ID", "20260701", "20260715");
var daily = await insight.GetRichMenuInsightDailyAsync("RICH_MENU_ID", "20260701", "20260715");
```

## Dependency injection

```csharp
using Line.OpenApi.Insight.DependencyInjection;

services.AddLineInsight(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// resolve: sp.GetRequiredService<InsightClient>()
```

See [Dependency Injection & Hosting](di-and-hosting.md) for the auth-provider overload (for
example to inject a refreshing token provider).
