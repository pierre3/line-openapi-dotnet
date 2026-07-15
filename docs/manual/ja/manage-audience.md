# オーディエンス管理

@Line.OpenApi.ManageAudience.ManageAudienceClient はオーディエンス管理 API のファサードです。
Messaging と同様に 2 つの Kiota クライアントを統合します: **制御系**
（@Line.OpenApi.ManageAudience.ManageAudienceClient.Api、`api.line.me`）は JSON の
オーディエンスグループ操作、**データ系**（@Line.OpenApi.ManageAudience.ManageAudienceClient.Blob、
`api-data.line.me`）は `multipart/form-data` を使う 2 つの *by-file* ユーザー ID アップロードです。

制御系のフルサーフェス（作成 / 取得 / 一覧 / 削除、click・imp リターゲティング、説明更新、
共有オーディエンス）は `Api` から辿れます。よく使う操作には便利メソッドを用意しています。
by-file アップロードはラップ済みで、multipart ボディを自分で組み立てる必要はありません。

```csharp
using Line.OpenApi.ManageAudience;

var ma = ManageAudienceClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## ファイルからユーザー ID をアップロード（データ系）

ファイルは 1 行 1 ユーザー ID（または IFA）のテキストファイルで、`api-data.line.me` へ
`text/plain` の `file` パートとして送られます。

```csharp
using var file = File.OpenRead("user-ids.txt");
var created = await ma.UploadUserIdsByFileAsync(file, description: "my audience", isIfaAudience: false);
long audienceGroupId = created!.AudienceGroupId!.Value;

// 同じグループへ後から追加。
using var more = File.OpenRead("more-ids.txt");
await ma.AddUserIdsByFileAsync(audienceGroupId, more, uploadDescription: "second batch");
```

## リクエストボディでユーザー ID をアップロード（制御系）

```csharp
using Line.OpenApi.ManageAudience.Generated.Api.Models;

var created = await ma.CreateForUploadingUserIdsAsync(new CreateAudienceGroupRequest
{
    Description = "my audience",
    Audiences = new() { new Audience { Id = "U0001" } },
});
```

## オーディエンスグループの取得 / 削除

```csharp
var data = await ma.GetAudienceDataAsync(audienceGroupId);
await ma.DeleteAudienceGroupAsync(audienceGroupId);
```

制御系の残り（click/imp リターゲティング、一覧、説明更新、共有オーディエンス）は
`ma.Api.V2.Bot.AudienceGroup...` から生成ビルダー経由で利用してください。

## 依存性注入（DI）

```csharp
using Line.OpenApi.ManageAudience.DependencyInjection;

services.AddLineManageAudience(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// 解決: sp.GetRequiredService<ManageAudienceClient>()
```

既定で `api.line.me` と `api-data.line.me` の両方が許可ホストです（by-file アップロードは
データ系を使うため）。認証プロバイダのオーバーロードについては
[DI とホスティング](di-and-hosting.md)を参照してください。
