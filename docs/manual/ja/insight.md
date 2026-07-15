# インサイト（統計）

@Line.OpenApi.Insight.InsightClient はインサイト API のファサードです。単一ホスト（`api.line.me`）で、
7 つの読み取り操作を便利メソッドとして公開します。すべて GET で、日付パラメータは LINE が期待する
`yyyyMMdd` 形式を使います。より低レベルな操作が必要なら、`InsightClient.Api` から生成ビルダーへ
直接アクセスできます。

```csharp
using Line.OpenApi.Insight;

var insight = InsightClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## 友だちの属性（デモグラフィック）

```csharp
var demographics = await insight.GetFriendsDemographicsAsync();
```

## メッセージ配信数 / フォロワー数

```csharp
var deliveries = await insight.GetNumberOfMessageDeliveriesAsync("20260715");
var followers = await insight.GetNumberOfFollowersAsync("20260715");
```

## メッセージイベントと単位別統計

```csharp
// ナローキャスト/ブロードキャストの開封・クリック統計（requestId 指定）。
var events = await insight.GetMessageEventAsync("REQUEST_ID");

// カスタム集計単位での期間集計統計。
var stats = await insight.GetStatisticsPerUnitAsync("promotion_A", "20260701", "20260715");
```

## リッチメニューの統計

```csharp
var summary = await insight.GetRichMenuInsightSummaryAsync("RICH_MENU_ID", "20260701", "20260715");
var daily = await insight.GetRichMenuInsightDailyAsync("RICH_MENU_ID", "20260701", "20260715");
```

## 依存性注入（DI）

```csharp
using Line.OpenApi.Insight.DependencyInjection;

services.AddLineInsight(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// 解決: sp.GetRequiredService<InsightClient>()
```

認証プロバイダのオーバーロード（例: 更新型トークンプロバイダの注入）については
[DI とホスティング](di-and-hosting.md)を参照してください。
