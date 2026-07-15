# Manage Audience

@Line.OpenApi.ManageAudience.ManageAudienceClient is the facade for the Manage Audience API. Like
Messaging, it unifies two Kiota clients: the **control plane** (@Line.OpenApi.ManageAudience.ManageAudienceClient.Api,
`api.line.me`) for the JSON audience-group operations, and the **data plane**
(@Line.OpenApi.ManageAudience.ManageAudienceClient.Blob, `api-data.line.me`) for the two *by-file*
user-ID upload operations, which use `multipart/form-data`.

The full control surface (create / get / list / delete audience groups, click &amp; imp
retargeting, description update, shared audiences) is reached through `Api`; a few common
operations have convenience methods. The by-file uploads are wrapped so you do not have to build
the multipart body yourself.

```csharp
using Line.OpenApi.ManageAudience;

var ma = ManageAudienceClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## Upload user IDs from a file (data plane)

The file is a text file with one user ID (or IFA) per line; it is sent as the `text/plain`
`file` part on `api-data.line.me`.

```csharp
using var file = File.OpenRead("user-ids.txt");
var created = await ma.UploadUserIdsByFileAsync(file, description: "my audience", isIfaAudience: false);
long audienceGroupId = created!.AudienceGroupId!.Value;

// Append more IDs to the same group later.
using var more = File.OpenRead("more-ids.txt");
await ma.AddUserIdsByFileAsync(audienceGroupId, more, uploadDescription: "second batch");
```

## Upload user IDs in the request body (control plane)

```csharp
using Line.OpenApi.ManageAudience.Generated.Api.Models;

var created = await ma.CreateForUploadingUserIdsAsync(new CreateAudienceGroupRequest
{
    Description = "my audience",
    Audiences = new() { new Audience { Id = "U0001" } },
});
```

## Get / delete an audience group

```csharp
var data = await ma.GetAudienceDataAsync(audienceGroupId);
await ma.DeleteAudienceGroupAsync(audienceGroupId);
```

For the rest of the control surface (click/imp retargeting, list, description update, shared
audiences), use the generated builders via `ma.Api.V2.Bot.AudienceGroup...`.

## Dependency injection

```csharp
using Line.OpenApi.ManageAudience.DependencyInjection;

services.AddLineManageAudience(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// resolve: sp.GetRequiredService<ManageAudienceClient>()
```

By default both `api.line.me` and `api-data.line.me` are allowed hosts (the by-file upload uses
the data plane). See [Dependency Injection & Hosting](di-and-hosting.md) for the auth-provider
overload.
