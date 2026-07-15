# ショップ（ミッションスタンプ）

@Line.OpenApi.Shop.ShopClient はショップ API のファサードです。単一ホスト（`api.line.me`）で、
仕様が定義する 1 操作（ミッションスタンプの送信）を便利メソッドとして公開します。より低レベルな
操作が必要なら、`ShopClient.Api` から生成ビルダーへ直接アクセスできます。

```csharp
using Line.OpenApi.Shop;
using Line.OpenApi.Shop.Generated.Models;

var shop = ShopClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## ミッションスタンプの送信

```csharp
await shop.SendMissionStickerAsync(new MissionStickerRequest
{
    To = "USER_ID",
    ProductType = "STICKER",
    ProductId = "PRODUCT_ID",
});
```

## 依存性注入（DI）

```csharp
using Line.OpenApi.Shop.DependencyInjection;

services.AddLineShop(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// 解決: sp.GetRequiredService<ShopClient>()
```

認証プロバイダのオーバーロードについては [DI とホスティング](di-and-hosting.md)を参照してください。
