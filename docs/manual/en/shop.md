# Shop (Mission Stickers)

@Line.OpenApi.Shop.ShopClient is the facade for the Shop API. It uses a single host
(`api.line.me`) and exposes the one operation the spec defines — sending a mission sticker — as a
convenience method. For anything lower-level, `ShopClient.Api` exposes the generated builders
directly.

```csharp
using Line.OpenApi.Shop;
using Line.OpenApi.Shop.Generated.Models;

var shop = ShopClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## Send a mission sticker

```csharp
await shop.SendMissionStickerAsync(new MissionStickerRequest
{
    To = "USER_ID",
    ProductType = "STICKER",
    ProductId = "PRODUCT_ID",
});
```

## Dependency injection

```csharp
using Line.OpenApi.Shop.DependencyInjection;

services.AddLineShop(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// resolve: sp.GetRequiredService<ShopClient>()
```

See [Dependency Injection & Hosting](di-and-hosting.md) for the auth-provider overload.
