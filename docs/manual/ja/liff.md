# LIFF アプリ管理

@Line.OpenApi.Liff.LiffClient は LIFF 管理 API のファサードです。Messaging と異なり LIFF は単一ホスト
（`api.line.me`）で、表面も小さく閉じている（2 パス・4 操作）ため、クライアントは便利メソッドで
完全にラップしています。より低レベルな操作が必要なら、`LiffClient.Api` から生成ビルダーへ
直接アクセスできます。

```csharp
using Line.OpenApi.Liff;
using Line.OpenApi.Liff.Generated.Models;

var liff = LiffClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## アプリの一覧取得

```csharp
GetAllLiffAppsResponse? apps = await liff.GetAppsAsync();
```

## アプリの追加

```csharp
AddLiffAppResponse? added = await liff.AddAppAsync(new AddLiffAppRequest
{
    View = new LiffView { Type = LiffView_type.Full, Url = "https://example.com" },
});
string liffId = added!.LiffId!;
```

## アプリの更新

```csharp
await liff.UpdateAppAsync(liffId, new UpdateLiffAppRequest { Description = "updated" });
```

## アプリの削除

```csharp
await liff.DeleteAppAsync(liffId);
```

## 依存性注入（DI）

```csharp
using Line.OpenApi.Liff.DependencyInjection;

services.AddLineLiff(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// 解決: sp.GetRequiredService<LiffClient>()
```

認証プロバイダのオーバーロード（例: 更新型トークンプロバイダの注入）については
[DI とホスティング](di-and-hosting.md)を参照してください。
