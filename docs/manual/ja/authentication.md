# 認証

Messaging API と LIFF API の呼び出しは、Bearer トークンとして運ばれる**チャネルアクセス
トークン**で認可します。本ライブラリはトークン取得を Kiota の `IAccessTokenProvider` の背後に
モデル化しているため、呼び出し箇所を変えずに静的トークンと実行時更新型トークンを選べます。

## トークン戦略の一覧

| 戦略 | 型 | 使いどころ |
|---|---|---|
| 静的（長期） | @Line.OpenApi.Core.Authentication.StaticChannelAccessTokenProvider | 長期トークンを既に保持している。 |
| 短期（v2.1 JWT） | @Line.OpenApi.ChannelAccessToken.JwtAssertionTokenSource | JWT アサーションで発行（`/oauth2/v2.1/token`）。 |
| ステートレス（v3） | @Line.OpenApi.ChannelAccessToken.StatelessJwtAssertionTokenSource | 15 分のステートレストークンを発行（`/oauth2/v3/token`）。 |
| 自動更新ラッパ | @Line.OpenApi.ChannelAccessToken.RefreshingChannelAccessTokenProvider | 短期/ステートレストークンをキャッシュし期限手前で再発行。 |

## 静的トークン

最も単純なケース: トークンを保持して返します。このプロバイダは `Line.OpenApi.Core` にあり、許可された
ホストにのみトークンを付与します（[セキュリティ](security.md)参照）。

```csharp
using Line.OpenApi.Core.Authentication;
using Microsoft.Kiota.Abstractions.Authentication;

var provider = new StaticChannelAccessTokenProvider("CHANNEL_ACCESS_TOKEN");
var auth = new BaseBearerTokenAuthenticationProvider(provider);
```

`MessagingClient` と `LiffClient` はいずれも、これを組み立てる `CreateWithStaticToken(...)` の
ショートカットを公開しています。

## 短期トークン（JWT アサーション）

@Line.OpenApi.ChannelAccessToken.JwtAssertionTokenSource を使うと、署名済み JWT アサーションから短期
トークンを発行できます。チャネルの秘密鍵での署名はアプリ固有のため、アサーションはファクトリ
経由で供給します — **本ライブラリは署名鍵を扱いません**。

```csharp
using Line.OpenApi.ChannelAccessToken;
using Line.OpenApi.ChannelAccessToken.Generated;

var tokenClient = new ChannelAccessTokenClient(requestAdapter); // Kiota 生成クライアント
var source = new JwtAssertionTokenSource(
    tokenClient,
    async ct => await BuildSignedJwtAssertionAsync(ct)); // あなたの署名ロジック

IssuedToken token = await source.IssueAsync();
```

## ステートレストークン（v3）

@Line.OpenApi.ChannelAccessToken.StatelessJwtAssertionTokenSource は `/oauth2/v3/token` から
**ステートレス**トークンを発行します。ステートレストークンは同時に有効なトークン数に上限が
ありませんが、有効期間は 15 分だけで満了まで失効できません。そのため更新型プロバイダと組み
合わせ、都度発行する運用にします。

> **なぜ専用ヘルパなのか？** `/oauth2/v3/token` のボディは discriminator 無しの `oneOf` です。
> 生成コードはこれを合成ラッパとして表現し、内側モデルを*入れ子オブジェクト*として直列化する
> ため、form-urlencoded シリアライザでは表現できず（"Form serialization does not support nested
> objects." で失敗）ます。このヘルパは平坦な要求モデルを送ることでラッパを回避し、v2.1 の
> ソースと同じ発行シームを提供します。

## 自動更新プロバイダ

@Line.OpenApi.ChannelAccessToken.RefreshingChannelAccessTokenProvider は任意の
`IChannelAccessTokenSource` をラップし、発行したトークンをキャッシュして、期限手前の更新
マージンに達したら再発行します。並行更新時の二重発行を防止し、`IDisposable` です。

```csharp
using Line.OpenApi.ChannelAccessToken;

using var provider = new RefreshingChannelAccessTokenProvider(
    source,                              // 例: JwtAssertionTokenSource / StatelessJwtAssertionTokenSource
    refreshMargin: TimeSpan.FromMinutes(5));

var auth = new BaseBearerTokenAuthenticationProvider(provider);
var messaging = new MessagingClient(auth);
```

更新型プロバイダを DI で使うには、`AddLineMessaging` / `AddLineLiff` の認証プロバイダ
オーバーロード経由で注入します（[DI とホスティング](di-and-hosting.md)参照）。これにより
`Line.OpenApi.Messaging` / `Line.OpenApi.Liff` が `Line.OpenApi.ChannelAccessToken` に依存せずに済みます。
