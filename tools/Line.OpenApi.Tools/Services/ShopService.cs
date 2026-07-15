using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Shop;
using Line.OpenApi.Shop.Generated.Models;
using Microsoft.Kiota.Serialization.Json;

namespace Line.OpenApi.Tools.Services;

/// <summary>
/// Shop (mission sticker) operations. Thin wrapper over the <see cref="ShopClient"/> facade
/// (single host <c>api.line.me</c>; R1 not applicable). The request body is accepted as JSON
/// parsed into the generated <see cref="MissionStickerRequest"/> model.
/// </summary>
public sealed class ShopService
{
    // Memoized per token to avoid HttpClient accumulation in the long-running MCP server
    // (code gate Medium#1). CreateWithStaticToken preserves the api.line.me host restriction.
    private static readonly ConcurrentDictionary<string, ShopClient> Clients = new(StringComparer.Ordinal);

    private static ShopClient Create(ResolvedCredentials credentials) =>
        Clients.GetOrAdd(credentials.RequireAccessToken(), static token => ShopClient.CreateWithStaticToken(token));

    /// <summary>Sends a mission sticker to a user from a JSON request body.</summary>
    public async Task SendMissionAsync(ResolvedCredentials credentials, string requestJson, CancellationToken cancellationToken)
    {
        var request = await ParseAsync(requestJson, cancellationToken).ConfigureAwait(false);
        await Create(credentials).SendMissionStickerAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<MissionStickerRequest> ParseAsync(string json, CancellationToken cancellationToken)
    {
        MissionStickerRequest? request;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var node = await new JsonParseNodeFactory()
                .GetRootParseNodeAsync("application/json", stream, cancellationToken)
                .ConfigureAwait(false);
            request = node?.GetObjectValue(MissionStickerRequest.CreateFromDiscriminatorValue);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new MessageInputException($"Mission sticker JSON could not be parsed: {ex.Message}", ex);
        }

        return request ?? throw new MessageInputException("Could not parse the mission sticker request JSON.");
    }
}
