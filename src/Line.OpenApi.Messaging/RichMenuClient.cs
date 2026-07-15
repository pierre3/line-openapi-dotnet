using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Line.OpenApi.Messaging.Generated.Api.Models;

namespace Line.OpenApi.Messaging;

/// <summary>
/// Facade for the Rich Menu use case. Rich Menu spans both Messaging hosts — control operations
/// on <c>api.line.me</c> and image upload/download on <c>api-data.line.me</c> — so this facade
/// wraps a <see cref="MessagingClient"/> (which already solves the host split / BaseUrl handling)
/// and exposes the common create → image → set-default → link flow through thin convenience
/// methods.
///
/// The generated builders remain available via <see cref="Messaging"/> (<c>.Api</c> for control,
/// <c>.Blob</c> for image data plane) for the less common operations (alias CRUD, bulk link/unlink,
/// batch) that are intentionally not surfaced here.
///
/// The image helpers are the main ergonomic value: <see cref="SetImageFromFileAsync"/> infers the
/// required <c>image/png</c> / <c>image/jpeg</c> content type from the file extension, which LINE
/// mandates for the upload (the generated builder requires it as an explicit argument).
///
/// Usage:
///   var rich = RichMenuClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
///   var id = await rich.CreateAsync(new RichMenuRequest { /* ... */ });
///   await rich.SetImageFromFileAsync(id!, "menu.png");
///   await rich.SetDefaultAsync(id!);
/// </summary>
public sealed class RichMenuClient
{
    /// <summary>The underlying Messaging client (exposed for low-level operations: alias / bulk / batch).</summary>
    public MessagingClient Messaging { get; }

    /// <summary>Wraps an existing <see cref="MessagingClient"/>.</summary>
    public RichMenuClient(MessagingClient messaging)
    {
        Messaging = messaging ?? throw new ArgumentNullException(nameof(messaging));
    }

    /// <param name="authProvider">Authentication provider (static or refreshing, either works).</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> shared by the adapters. Supplied by <c>IHttpClientFactory</c>
    /// via DI. When null, a default <see cref="HttpClient"/> is created (for quick use).
    /// </param>
    public RichMenuClient(IAuthenticationProvider authProvider, HttpClient? httpClient = null)
        : this(new MessagingClient(authProvider, httpClient))
    {
    }

    /// <summary>Helper to quickly construct from a long-lived channel access token.</summary>
    public static RichMenuClient CreateWithStaticToken(string channelAccessToken)
        => new(MessagingClient.CreateWithStaticToken(channelAccessToken));

    // --- Create / read / delete (control plane) ---

    /// <summary>Creates a rich menu (POST /v2/bot/richmenu). Returns the new rich menu id.</summary>
    public async Task<string?> CreateAsync(RichMenuRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var res = await Messaging.Api.V2.Bot.Richmenu.PostAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        return res?.RichMenuId;
    }

    /// <summary>Validates a rich menu object without creating it (POST /v2/bot/richmenu/validate). Throws on an invalid object.</summary>
    public async Task ValidateAsync(RichMenuRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        // Success returns an empty body; dispose the Stream the generated code returns.
        using var _ = await Messaging.Api.V2.Bot.Richmenu.Validate
            .PostAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets a rich menu by id (GET /v2/bot/richmenu/{richMenuId}). Returns null if the id does not exist.</summary>
    public Task<RichMenuResponse?> GetAsync(string richMenuId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(richMenuId)) throw new ArgumentException("richMenuId is required", nameof(richMenuId));
        return NullOn404(() => Messaging.Api.V2.Bot.Richmenu[richMenuId].GetAsync(cancellationToken: cancellationToken));
    }

    /// <summary>Lists the channel's rich menus (GET /v2/bot/richmenu/list).</summary>
    public Task<RichMenuListResponse?> ListAsync(CancellationToken cancellationToken = default)
        => Messaging.Api.V2.Bot.Richmenu.List.GetAsync(cancellationToken: cancellationToken);

    /// <summary>Deletes a rich menu (DELETE /v2/bot/richmenu/{richMenuId}).</summary>
    public async Task DeleteAsync(string richMenuId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(richMenuId)) throw new ArgumentException("richMenuId is required", nameof(richMenuId));
        using var _ = await Messaging.Api.V2.Bot.Richmenu[richMenuId]
            .DeleteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // --- Default rich menu ---

    /// <summary>Sets the default rich menu for all users (POST /v2/bot/user/all/richmenu/{richMenuId}).</summary>
    public async Task SetDefaultAsync(string richMenuId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(richMenuId)) throw new ArgumentException("richMenuId is required", nameof(richMenuId));
        using var _ = await Messaging.Api.V2.Bot.User.All.Richmenu[richMenuId]
            .PostAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the default rich menu id (GET /v2/bot/user/all/richmenu). Returns null if none is set (LINE returns 404).</summary>
    public async Task<string?> GetDefaultIdAsync(CancellationToken cancellationToken = default)
    {
        var res = await NullOn404(() => Messaging.Api.V2.Bot.User.All.Richmenu.GetAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
        return res?.RichMenuId;
    }

    /// <summary>Cancels the default rich menu (DELETE /v2/bot/user/all/richmenu).</summary>
    public async Task CancelDefaultAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await Messaging.Api.V2.Bot.User.All.Richmenu
            .DeleteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // --- Per-user link ---

    /// <summary>Links a rich menu to a user (POST /v2/bot/user/{userId}/richmenu/{richMenuId}).</summary>
    public async Task LinkToUserAsync(string userId, string richMenuId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("userId is required", nameof(userId));
        if (string.IsNullOrEmpty(richMenuId)) throw new ArgumentException("richMenuId is required", nameof(richMenuId));
        using var _ = await Messaging.Api.V2.Bot.User[userId].Richmenu[richMenuId]
            .PostAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Unlinks the rich menu from a user (DELETE /v2/bot/user/{userId}/richmenu).</summary>
    public async Task UnlinkFromUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("userId is required", nameof(userId));
        using var _ = await Messaging.Api.V2.Bot.User[userId].Richmenu
            .DeleteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the rich menu id linked to a user (GET /v2/bot/user/{userId}/richmenu). Returns null if none (LINE returns 404).</summary>
    public async Task<string?> GetIdOfUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("userId is required", nameof(userId));
        var res = await NullOn404(() => Messaging.Api.V2.Bot.User[userId].Richmenu.GetAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
        return res?.RichMenuId;
    }

    // --- Image (data plane, api-data.line.me) ---

    /// <summary>
    /// Uploads the rich menu image (POST /v2/bot/richmenu/{richMenuId}/content, data plane).
    /// <paramref name="contentType"/> must be <c>image/png</c> or <c>image/jpeg</c>.
    /// </summary>
    public async Task SetImageAsync(string richMenuId, Stream image, string contentType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(richMenuId)) throw new ArgumentException("richMenuId is required", nameof(richMenuId));
        if (image is null) throw new ArgumentNullException(nameof(image));
        if (string.IsNullOrEmpty(contentType)) throw new ArgumentException("contentType is required", nameof(contentType));
        using var _ = await Messaging.Blob.V2.Bot.Richmenu[richMenuId].Content
            .PostAsync(image, contentType, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads the rich menu image from a file, inferring the content type from the extension
    /// (<c>.png</c> → image/png, <c>.jpg</c>/<c>.jpeg</c> → image/jpeg). Other extensions are rejected.
    /// </summary>
    public async Task SetImageFromFileAsync(string richMenuId, string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("filePath is required", nameof(filePath));
        var contentType = InferImageContentType(filePath);
        using var file = File.OpenRead(filePath);
        await SetImageAsync(richMenuId, file, contentType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Downloads the rich menu image (GET /v2/bot/richmenu/{richMenuId}/content, data plane).</summary>
    public Task<Stream?> GetImageAsync(string richMenuId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(richMenuId)) throw new ArgumentException("richMenuId is required", nameof(richMenuId));
        return Messaging.Blob.V2.Bot.Richmenu[richMenuId].Content.GetAsync(cancellationToken: cancellationToken);
    }

    // "No default rich menu" and "no rich menu linked to this user" are normal states that LINE
    // signals with HTTP 404 (these endpoints define only a 200 response, so the generated client
    // throws ApiException). Translate that single case to null so the "returns null if none"
    // contract holds; any other status still surfaces as an exception.
    private static async Task<T?> NullOn404<T>(Func<Task<T?>> operation) where T : class
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Maps a rich menu image file extension to its required content type.</summary>
    public static string InferImageContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => throw new ArgumentException(
                $"Unsupported rich menu image type '{ext}'. LINE accepts PNG (.png) or JPEG (.jpg/.jpeg).", nameof(filePath)),
        };
    }
}
