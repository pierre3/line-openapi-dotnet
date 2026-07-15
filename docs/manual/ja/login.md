# LINE Login と OpenID Connect

@Line.OpenApi.Login.LoginClient は LINE Login v2.1 とその OpenID Connect 機能のファサードです。
LINE Login には OpenAPI 仕様が公開されていないため、このクライアントは Kiota ランタイムの上に
手書きで実装しています。

> **資格情報についての注意。** LINE Login はユーザーを **user access token** で認証します。これは
> Messaging の **channel access token** とは別系統の資格情報です。トークン発行にはリクエストボディで
> LINE Login チャネルの ID（`client_id`）とシークレット（`client_secret`）を使います。REST 呼び出しは
> すべて `api.line.me`、認可ページのみ `access.line.me`（REST ではなくブラウザのリダイレクト先）です。

```csharp
using Line.OpenApi.Login;

var login = new LoginClient("LOGIN_CHANNEL_ID", "LOGIN_CHANNEL_SECRET");
```

## 1. 認可 URL の生成

`BuildAuthorizationUrl` は URL を組み立てるだけで HTTP は呼びません。CSRF 用の `state` と
（推奨）PKCE チャレンジを生成してセッションに保存し、ブラウザをリダイレクトします。

```csharp
PkceChallenge pkce = LineLoginSecurity.CreatePkceChallenge();
string state       = LineLoginSecurity.GenerateState();

string url = login.BuildAuthorizationUrl(new AuthorizationUrlParameters
{
    RedirectUri   = "https://app.example.com/callback",
    Scopes        = new[] { "openid", "profile" },
    State         = state,
    Nonce         = "server-generated-nonce",   // ID トークンへ反映される
    CodeChallenge = pkce.CodeChallenge,          // CodeChallengeMethod の既定は S256
});
// `url` へリダイレクトし、`state` と `pkce.CodeVerifier` はセッションに保持する。
```

## 2. 認可コードをトークンに交換

コールバックでは、返ってきた `state` を保存値と照合してから交換します。PKCE を使った場合は
保存した `CodeVerifier` を渡します。

```csharp
LineLoginTokenResponse? token =
    await login.ExchangeCodeAsync("<code>", "https://app.example.com/callback", pkce.CodeVerifier);

string accessToken  = token!.AccessToken!;   // 有効期間 30 日
string refreshToken = token.RefreshToken!;   // 有効期間 最大 90 日
string? idToken     = token.IdToken;         // openid スコープ許諾時のみ
```

## 3. ID トークンの検証（OpenID Connect）

`VerifyIdTokenAsync` は署名と claim の検証を LINE に委譲し（`POST /oauth2/v2.1/verify`）、検証済みの
claim を返します。最も単純で常に正しい経路です。

```csharp
VerifiedIdToken? claims = await login.VerifyIdTokenAsync(
    idToken!, nonce: "server-generated-nonce", expectedUserId: null);

string userId = claims!.Sub!;   // subject
string? name  = claims.Name;    // profile スコープ許諾時に存在
```

> ローカル検証（Web フロー=HS256、ネイティブ/LIFF フロー=ES256+JWKS）は本リリースには含みません。
> 上記のサーバ委譲を使ってください。

## 4. アクセストークンのリフレッシュ・失効・検証

```csharp
LineLoginTokenResponse? refreshed = await login.RefreshTokenAsync(refreshToken);
VerifyAccessTokenResponse? info    = await login.VerifyAccessTokenAsync(accessToken); // scope / 有効期限
await login.RevokeTokenAsync(accessToken);
```

## 5. プロフィール・userinfo・友だち関係の取得

これらは呼び出しごとに **user access token** を受け取ります（ホスト制限付きで、`api.line.me` 以外へは
トークンを送りません）。

```csharp
LineUserProfile? profile = await login.GetProfileAsync(accessToken);          // profile スコープが必要
UserInfo?        userinfo = await login.GetUserInfoAsync(accessToken);        // openid スコープが必要
FriendshipStatus? friend  = await login.GetFriendshipStatusAsync(accessToken); // friend.FriendFlag
```

## 6. 認可解除（deauthorize）

ユーザーがアプリに許可した権限をすべて取り消します。系統をまたぐ点に注意してください。
**Authorization ヘッダは Messaging の channel access token**、user access token はボディで送ります。
channel token は文字列で渡すため、`Line.OpenApi.Login` は `Line.OpenApi.ChannelAccessToken` に
依存しません。

```csharp
await login.DeauthorizeAsync("MESSAGING_CHANNEL_ACCESS_TOKEN", userAccessToken);
```

## 依存性注入（DI）

```csharp
using Line.OpenApi.Login.DependencyInjection;

services.AddLineLogin(o =>
{
    o.ChannelId     = "LOGIN_CHANNEL_ID";
    o.ChannelSecret = "LOGIN_CHANNEL_SECRET";
});
// 解決: sp.GetRequiredService<LoginClient>()
```

共有 `HttpClient` と Kiota 既定ミドルウェア（CVE 修正済みのリダイレクトハンドラを含む）の配線に
ついては [DI とホスティング](di-and-hosting.md)を参照してください。
