using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

namespace Line.OpenApi.Tools.Services;

/// <summary>
/// Rich Menu management. Thin wrapper over the <see cref="RichMenuClient"/> facade (control host
/// api.line.me + data host api-data.line.me for the image; the facade solves the split). Definitions
/// are accepted as JSON parsed into the generated request models. Image upload/download take/produce
/// files and are intended for the CLI (binary is impractical over MCP).
/// </summary>
public sealed class RichMenuService
{
    // Memoized per token to avoid HttpClient accumulation in the long-running MCP server.
    private static readonly ConcurrentDictionary<string, RichMenuClient> Clients = new(StringComparer.Ordinal);

    private static RichMenuClient Create(ResolvedCredentials credentials) =>
        Clients.GetOrAdd(credentials.RequireAccessToken(), static token => RichMenuClient.CreateWithStaticToken(token));

    /// <summary>Creates a rich menu from a JSON definition. Returns the new rich menu id.</summary>
    public async Task<string?> CreateAsync(ResolvedCredentials credentials, string richMenuJson, CancellationToken cancellationToken)
    {
        var request = await ParseAsync(richMenuJson, cancellationToken).ConfigureAwait(false);
        return await Create(credentials).CreateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates a rich menu JSON definition against the LINE validation endpoint without creating it.
    /// Parses the JSON (throws <see cref="MessageInputException"/> on malformed input) then calls the API
    /// (throws on an invalid object). Returns a small summary on success.
    /// </summary>
    public async Task<RichMenuValidationResult> ValidateAsync(ResolvedCredentials credentials, string richMenuJson, CancellationToken cancellationToken)
    {
        var request = await ParseAsync(richMenuJson, cancellationToken).ConfigureAwait(false);
        await Create(credentials).ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return new RichMenuValidationResult(Valid: true, Name: request.Name, AreaCount: request.Areas?.Count ?? 0);
    }

    /// <summary>Lists the channel's rich menus.</summary>
    public async Task<IReadOnlyList<RichMenuSummary>> ListAsync(ResolvedCredentials credentials, CancellationToken cancellationToken)
    {
        var res = await Create(credentials).ListAsync(cancellationToken).ConfigureAwait(false);
        return res?.Richmenus?
            .Select(m => new RichMenuSummary(m.RichMenuId, m.Name, m.ChatBarText, m.Selected, m.Areas?.Count ?? 0))
            .ToList()
            ?? new List<RichMenuSummary>();
    }

    /// <summary>Gets a rich menu by id.</summary>
    public Task<RichMenuResponse?> GetAsync(ResolvedCredentials credentials, string richMenuId, CancellationToken cancellationToken) =>
        Create(credentials).GetAsync(richMenuId, cancellationToken);

    /// <summary>Deletes a rich menu.</summary>
    public Task DeleteAsync(ResolvedCredentials credentials, string richMenuId, CancellationToken cancellationToken) =>
        Create(credentials).DeleteAsync(richMenuId, cancellationToken);

    /// <summary>Sets the default rich menu for all users.</summary>
    public Task SetDefaultAsync(ResolvedCredentials credentials, string richMenuId, CancellationToken cancellationToken) =>
        Create(credentials).SetDefaultAsync(richMenuId, cancellationToken);

    /// <summary>Gets the default rich menu id (null if none).</summary>
    public Task<string?> GetDefaultIdAsync(ResolvedCredentials credentials, CancellationToken cancellationToken) =>
        Create(credentials).GetDefaultIdAsync(cancellationToken);

    /// <summary>Cancels the default rich menu.</summary>
    public Task CancelDefaultAsync(ResolvedCredentials credentials, CancellationToken cancellationToken) =>
        Create(credentials).CancelDefaultAsync(cancellationToken);

    /// <summary>Links a rich menu to a user.</summary>
    public Task LinkToUserAsync(ResolvedCredentials credentials, string userId, string richMenuId, CancellationToken cancellationToken) =>
        Create(credentials).LinkToUserAsync(userId, richMenuId, cancellationToken);

    /// <summary>Unlinks the rich menu from a user.</summary>
    public Task UnlinkFromUserAsync(ResolvedCredentials credentials, string userId, CancellationToken cancellationToken) =>
        Create(credentials).UnlinkFromUserAsync(userId, cancellationToken);

    /// <summary>Gets the rich menu id linked to a user (null if none).</summary>
    public Task<string?> GetIdOfUserAsync(ResolvedCredentials credentials, string userId, CancellationToken cancellationToken) =>
        Create(credentials).GetIdOfUserAsync(userId, cancellationToken);

    /// <summary>Uploads a rich menu image from a file (content type inferred from the extension). CLI use.</summary>
    public Task SetImageFromFileAsync(ResolvedCredentials credentials, string richMenuId, string filePath, CancellationToken cancellationToken) =>
        Create(credentials).SetImageFromFileAsync(richMenuId, filePath, cancellationToken);

    /// <summary>Downloads a rich menu image to a file. Returns the byte count written. CLI use.</summary>
    public async Task<long> DownloadImageAsync(ResolvedCredentials credentials, string richMenuId, string outputPath, CancellationToken cancellationToken)
    {
        await using var content = await Create(credentials).GetImageAsync(richMenuId, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            throw new InvalidOperationException($"No image returned for rich menu '{richMenuId}'.");
        }

        await using var file = File.Create(outputPath);
        await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        return file.Length;
    }

    private static async Task<RichMenuRequest> ParseAsync(string json, CancellationToken cancellationToken)
    {
        RichMenuRequest? request;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var node = await new JsonParseNodeFactory()
                .GetRootParseNodeAsync("application/json", stream, cancellationToken)
                .ConfigureAwait(false);
            request = node?.GetObjectValue(RichMenuRequest.CreateFromDiscriminatorValue);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            // JsonException/FormatException: malformed JSON. InvalidOperationException: valid JSON of
            // the wrong shape (e.g. an array or scalar where a rich menu object is expected) — Kiota
            // throws this from GetObjectValue. Both are input errors (exit 2), not internal faults.
            throw new MessageInputException($"Rich menu JSON could not be parsed: {ex.Message}", ex);
        }

        return request ?? throw new MessageInputException("Could not parse the rich menu definition JSON.");
    }
}

/// <summary>Summary of a rich menu for display / listing.</summary>
public sealed record RichMenuSummary(string? RichMenuId, string? Name, string? ChatBarText, bool? Selected, int AreaCount);

/// <summary>Outcome of a rich menu validation (dry run).</summary>
public sealed record RichMenuValidationResult(bool Valid, string? Name, int AreaCount);
