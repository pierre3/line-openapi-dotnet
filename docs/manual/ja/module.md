# モジュールチャネル

@Line.OpenApi.Module.ModuleClient はモジュールチャネル API のファサードです。パートナー / 代理店が
LINE 公式アカウントをオーナーに代わって運用する（LOA）ためのものです。単一ホスト（`api.line.me`）で、
4 つの操作（detach、chat control の acquire / release、attach 済みモジュール一覧）を便利メソッドとして
提供します。より低レベルな操作が必要なら、`ModuleClient.Api` から生成ビルダーへ直接アクセスできます。

> **非対応:** モジュールの attach（`module-attach`。`manager.line.biz` 上で HTTP Basic 認証と PKCE を
> 使用）は本パッケージのスコープ外です。実需が出た時点で追加する可能性があります。

```csharp
using Line.OpenApi.Module;
using Line.OpenApi.Module.Generated.Models;

var module = ModuleClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## attach 済みモジュールの一覧

```csharp
// start: ページングトークン, limit: 最大 bot 数（LINE 既定 100）。
var modules = await module.GetModulesAsync(start: null, limit: 100);
```

## chat control の取得 / 解放

```csharp
await module.AcquireChatControlAsync("CHAT_ID", new AcquireChatControlRequest
{
    Expired = true,
    Ttl = 3600,
});

await module.ReleaseChatControlAsync("CHAT_ID");
```

## detach

```csharp
await module.DetachAsync(new DetachModuleRequest { BotId = "BOT_ID" });
```

## 依存性注入（DI）

```csharp
using Line.OpenApi.Module.DependencyInjection;

services.AddLineModule(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// 解決: sp.GetRequiredService<ModuleClient>()
```

認証プロバイダのオーバーロードについては [DI とホスティング](di-and-hosting.md)を参照してください。
