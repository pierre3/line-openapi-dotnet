# LINE MINI App

@Line.OpenApi.MiniApp.MiniAppClient は LINE MINI App のサーバ REST 表面のファサードです。
LINE MINI App には OpenAPI 仕様が公開されていないため、このクライアントは Kiota ランタイムの上に
手書きで実装しています。

> **資格情報についての注意。** `LoginClient` と同様、トークンは保持せず呼び出しごとに文字列引数で
> 受け取るため、`Line.OpenApi.ChannelAccessToken` にも `Line.OpenApi.Login` にも依存しません。
> REST 呼び出しはすべて `api.line.me` です。

```csharp
using Line.OpenApi.MiniApp;

var miniApp = new MiniAppClient();
```

このクライアントは 2 つの独立した機能領域をカバーします。

## 1. サービスメッセージ

MINI App 内でのユーザーの操作に応じて通知します。channel access token が必要ですが、
**ステートレス/短期トークン限定**です（長期の v2.1 トークンはこれらのエンドポイントで拒否されます）。

まず、フロント側の `liff.getAccessToken()` で取得した LIFF access token を使って通知トークンを
発行します:

```csharp
NotifierToken? issued = await miniApp.IssueNotificationTokenAsync(
    "CHANNEL_ACCESS_TOKEN", liffAccessToken);

string token = issued!.NotificationToken!;   // 有効期間 1 年、1 アクションにつき最大 5 回送信可
```

次に、審査済みのテンプレートとパラメータでメッセージを送信します:

```csharp
NotifierToken? sent = await miniApp.SendServiceMessageAsync(
    "CHANNEL_ACCESS_TOKEN",
    token,
    templateName: "order-complete_ja",       // {テンプレート名}_{BCP-47 言語}
    parameters: new Dictionary<string, string> { ["orderName"] = "Widget" });

token = sent!.NotificationToken!;   // 送信毎に更新される。次回呼び出し用に保存する
```

> テンプレートは本番利用前に LY Corporation の審査が必要です。テンプレート形式や文字数制限は
> LINE の[サービスメッセージ ドキュメント](https://developers.line.biz/en/docs/line-mini-app/develop/service-messages/)
> を参照してください。

## 2. アプリ内課金（IAP）

**購入者本人の user access token** で予約します:

```csharp
IapReserveResult? reserved = await miniApp.ReserveProductAsync(
    userAccessToken,
    clientIp: "203.0.113.1",
    clientOs: "ios",             // "ios" または "android"
    productId: "PRODUCT1",
    shopProductName: "Gold Pack" /* 最大 20 UTF-16 文字、絵文字・記号不可 */);

string orderId = reserved!.OrderId!;   // アプリ内課金 SDK に渡す
```

channel access token でプラットフォームの購入/返金 Webhook 履歴を取得します
（過去 7 日分・cursor ページング）:

```csharp
MiniAppWebhookEventPage? page = await miniApp.GetWebhookEventsAsync(
    "CHANNEL_ACCESS_TOKEN",
    startEpochSeconds, endEpochSeconds,
    pageSize: 50, cursor: null, status: "SUCCESS");

foreach (var entry in page!.Events!)
{
    MiniAppWebhookEvent ev = entry.Event!;
    // ev.Type は "purchaseComplete" または "refundComplete"。どちらも同じフィールド形状を共有する。
}

string? nextCursor = page.NextCursor;   // 次回呼び出しに渡す。null なら最終ページ
```

## エラー

非 2xx レスポンスは型付き例外として投げられます（いずれも `ApiException` 派生のため HTTP
ステータスコードが保持されます）:

- サービスメッセージ系エンドポイントは `NotifierErrorResponse`（`Message`, `Details`）を投げます。
- IAP 系エンドポイントは `IapErrorResponse`（`ErrorCode` — 例: `PRODUCT_ID_NOT_FOUND`,
  `BLOCKED_USER`, `TERMS_AGREEMENT_ERROR` — に加え `Message`, `Details`）を投げます。

## 依存性注入（DI）

```csharp
using Line.OpenApi.MiniApp.DependencyInjection;

services.AddLineMiniApp();
// 解決: sp.GetRequiredService<MiniAppClient>()
```

登録時に必須の設定はありません（トークンは呼び出しごとに渡すため）。許可ホストの既定
（`api.line.me`）を上書きする場合のみ `o => o.AllowedHosts = […]` を渡してください。

共有 `HttpClient` と Kiota 既定ミドルウェア（CVE 修正済みのリダイレクトハンドラを含む）の配線に
ついては [DI とホスティング](di-and-hosting.md)を参照してください。
