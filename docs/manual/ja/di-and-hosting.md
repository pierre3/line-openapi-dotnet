# DI とホスティング

簡単なスクリプトを超える用途では、クライアントを依存性注入で登録してください。
`CreateWithStaticToken` と比べ、DI セットアップは名前付き `IHttpClientFactory` クライアントを
使うため、ハンドラがプールされ・ローテーションされ、さらに CVE 修正版の `RedirectHandler` を
含む Kiota の既定ミドルウェアが適用されます。

## Messaging

静的トークン:

```csharp
using Line.OpenApi.Messaging.DependencyInjection;

services.AddLineMessaging(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// 解決: sp.GetRequiredService<MessagingClient>()
```

カスタム認証プロバイダを使う場合（例: `Line.OpenApi.ChannelAccessToken` の更新型トークンプロバイダ）:

```csharp
services.AddLineMessaging(sp =>
{
    // IAuthenticationProvider を返す。例: RefreshingChannelAccessTokenProvider から構築
    return BuildAuthProvider(sp);
});
```

この認証プロバイダのオーバーロードは、`Line.OpenApi.Messaging` が `Line.OpenApi.ChannelAccessToken` へ依存
しないようにするための注入経路です。

## LIFF

```csharp
using Line.OpenApi.Liff.DependencyInjection;

services.AddLineLiff(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// 解決: sp.GetRequiredService<LiffClient>()
```

`AddLineLiff` にも、`AddLineMessaging` と同様の認証プロバイダオーバーロードがあります。

## Webhook

```csharp
using Line.OpenApi.Messaging.Webhook.DependencyInjection;

services.AddLineWebhook(o => o.ChannelSecret = "CHANNEL_SECRET");
// 解決: sp.GetRequiredService<WebhookRequestParser>()
```

Webhook 受信は送信 HTTP を伴わないため、`AddLineWebhook` は `IHttpClientFactory` を使いません。

## 冪等性と複数回の登録

- `AddLineMessaging` / `AddLineLiff` は**冪等**です: 繰り返し呼んでも、名前付きクライアントに
  Kiota の既定ハンドラを重複追加しません（重複するとリトライ/リダイレクトが多重化します）。
  クライアント自体は `TryAdd` で登録されるため、**最初**の認証プロバイダ設定が採用されます。
- `AddLineWebhook` はパーサを `TryAdd`（先勝ち）で登録しますが、オプションは累積適用されるため、
  実効の `ChannelSecret` は**最後**に設定されたもの（後勝ち）になります。また `ValidateOnStart`
  を呼ぶため、シークレット未設定は「未検証リクエストの素通し」ではなく起動時失敗になります。

## 許可ホスト

`LineMessagingOptions.AllowedHosts` / `LineLiffOptions.AllowedHosts` は、トークンを付与してよい
ホストを制御します。未設定なら既定（`api.line.me`［Messaging はさらに `api-data.line.me`］）を
使います。[セキュリティ](security.md)を参照してください。
