# はじめに

このライブラリを使うと、.NET アプリケーションから LINE Messaging API の呼び出し、LIFF アプリの
管理、Webhook の受信（強く型付けされたモデルで）が行えます。

## 前提

- .NET SDK 10 以降（`dotnet --version`）。
- LINE チャネルとその資格情報:
  - **チャネルアクセストークン**（Messaging / LIFF API の呼び出し用）、および/または
  - **チャネルシークレット**（受信 Webhook の署名検証用）。

## パッケージ

必要な利用シーンに応じてパッケージを参照します。いずれも `Line.Core` に依存します。

| パッケージ | 用途 |
|---|---|
| `Line.Messaging` | プッシュ/応答メッセージの送信、メッセージコンテンツの取得。 |
| `Line.Messaging.Webhook` | Webhook イベントの受信と検証。 |
| `Line.Liff` | LIFF アプリの作成・管理。 |
| `Line.ChannelAccessToken` | 短期/ステートレスなチャネルアクセストークンの発行。 |

## 最初のメッセージ送信

最も手早い方法は、長期チャネルアクセストークンを使うものです:

```csharp
using Line.Messaging;
using Line.Messaging.Generated.Api.Models;

var client = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

await client.Api.V2.Bot.Message.Push.PostAsync(new PushMessageRequest
{
    To = "U0123456789abcdef...",
    Messages = new()
    {
        new TextMessage { Text = "Hello, world" },
    },
});
```

`CreateWithStaticToken` はクイックスタートや簡易なアプリ向けの便利メソッドです。本番では
[DI によるセットアップ](di-and-hosting.md)を推奨します。`IHttpClientFactory` が管理する
ハンドラプールを共有し、Kiota の既定ミドルウェアを適用します。

## 次に読む

- [認証](authentication.md) — 静的/JWT/ステートレストークンと更新型プロバイダ。
- [メッセージ送信](messaging.md) — プッシュ/応答/マルチキャストとコンテンツ取得。
- [Webhook 受信](webhook.md) — 署名検証とイベント分岐。
- [LIFF アプリ管理](liff.md) — LIFF アプリの一覧取得/追加/更新/削除。
- [DI とホスティング](di-and-hosting.md) — 推奨される組み込み方法。
- [セキュリティ](security.md) — 許可ホスト、署名検証、シークレットの取り扱い。

## 生成された表面についての注意

`client.Api.V2.Bot.Message.Push.PostAsync(...)` のような流れるようなビルダーパスは、Kiota が
生成したコードに由来します。これは意図的な「opaque box（不透明な箱）」です。リクエストは
これらのビルダーを通じて組み立て、[API リファレンス](xref:Line.Messaging)ではそれらへの
アクセスを提供する手書きファサードを解説します（トップのナビゲーションバーから閲覧できます）。
最初に知っておくべき命名の癖: Kiota は多態のアクション基底型を（`System.Action` との衝突を
避けるため）`ActionObject` に改名します。`MessageAction` / `PostbackAction` / `URIAction` などの
具体アクションは自然な名前のままです。
