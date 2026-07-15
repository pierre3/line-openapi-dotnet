using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.ManageAudience;
using ApiModels = Line.OpenApi.ManageAudience.Generated.Api.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

namespace Line.OpenApi.Tools.Services;

/// <summary>
/// Manage Audience operations. Thin wrapper over the <see cref="ManageAudienceClient"/> facade,
/// which already unifies the control plane (api.line.me, JSON) and the data plane
/// (api-data.line.me, multipart by-file upload; R1). Request bodies are accepted as JSON parsed
/// into the generated request models. The by-file uploads take a file path and are intended for
/// the CLI (binary/file input is impractical over MCP, same policy as rich menu images).
/// </summary>
public sealed class AudienceService
{
    // Memoized per token to avoid HttpClient accumulation in the long-running MCP server
    // (code gate Medium#1). CreateWithStaticToken allows both api.line.me and api-data.line.me.
    private static readonly ConcurrentDictionary<string, ManageAudienceClient> Clients = new(StringComparer.Ordinal);

    private static ManageAudienceClient Create(ResolvedCredentials credentials) =>
        Clients.GetOrAdd(credentials.RequireAccessToken(), static token => ManageAudienceClient.CreateWithStaticToken(token));

    /// <summary>Lists audience groups (paginated). LINE requires page &gt;= 1; size defaults to 20 (max 40).</summary>
    public async Task<IReadOnlyList<AudienceGroupSummary>> ListAsync(
        ResolvedCredentials credentials, long page, long size, CancellationToken cancellationToken)
    {
        var res = await Create(credentials).Api.V2.Bot.AudienceGroup.List
            .GetAsync(config =>
            {
                config.QueryParameters.Page = page;
                config.QueryParameters.Size = size;
            }, cancellationToken).ConfigureAwait(false);

        return res?.AudienceGroups?
            .Select(a => new AudienceGroupSummary(
                a.AudienceGroupId, a.Description, a.Type?.ToString(), a.Status?.ToString(), a.AudienceCount))
            .ToList()
            ?? new List<AudienceGroupSummary>();
    }

    /// <summary>Gets an audience group and its jobs.</summary>
    public Task<ApiModels.GetAudienceDataResponse?> GetAsync(
        ResolvedCredentials credentials, long audienceGroupId, CancellationToken cancellationToken) =>
        Create(credentials).GetAudienceDataAsync(audienceGroupId, cancellationToken);

    /// <summary>Creates an audience group and adds the initial user IDs from a JSON request body. Returns the new group id.</summary>
    public async Task<long?> CreateAsync(ResolvedCredentials credentials, string requestJson, CancellationToken cancellationToken)
    {
        var request = await ParseAsync(requestJson, ApiModels.CreateAudienceGroupRequest.CreateFromDiscriminatorValue, cancellationToken).ConfigureAwait(false);
        var res = await Create(credentials).CreateForUploadingUserIdsAsync(request, cancellationToken).ConfigureAwait(false);
        return res?.AudienceGroupId;
    }

    /// <summary>Adds user IDs to an existing upload audience group from a JSON request body (which carries the audienceGroupId).</summary>
    public async Task AddUsersAsync(ResolvedCredentials credentials, string requestJson, CancellationToken cancellationToken)
    {
        var request = await ParseAsync(requestJson, ApiModels.AddAudienceToAudienceGroupRequest.CreateFromDiscriminatorValue, cancellationToken).ConfigureAwait(false);
        await Create(credentials).AddUserIdsAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes an audience group.</summary>
    public Task DeleteAsync(ResolvedCredentials credentials, long audienceGroupId, CancellationToken cancellationToken) =>
        Create(credentials).DeleteAudienceGroupAsync(audienceGroupId, cancellationToken);

    /// <summary>Creates an audience group by uploading user IDs (or IFAs) from a text file (multipart, data plane). Returns the new group id. CLI use.</summary>
    public async Task<long?> UploadFileAsync(
        ResolvedCredentials credentials, string filePath, string? description, bool isIfa, string? uploadDescription, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(filePath);
        // Pass isIfaAudience only when true; leave it null when false so the API applies its default.
        var res = await Create(credentials)
            .UploadUserIdsByFileAsync(file, description, isIfa ? true : null, uploadDescription, cancellationToken)
            .ConfigureAwait(false);
        return res?.AudienceGroupId;
    }

    /// <summary>Adds user IDs (or IFAs) from a text file to an existing upload audience group (multipart, data plane). CLI use.</summary>
    public async Task AddFileAsync(
        ResolvedCredentials credentials, long audienceGroupId, string filePath, string? uploadDescription, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(filePath);
        await Create(credentials)
            .AddUserIdsByFileAsync(audienceGroupId, file, uploadDescription, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<T> ParseAsync<T>(string json, ParsableFactory<T> factory, CancellationToken cancellationToken)
        where T : IParsable
    {
        T? request;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var node = await new JsonParseNodeFactory()
                .GetRootParseNodeAsync("application/json", stream, cancellationToken)
                .ConfigureAwait(false);
            request = node is null ? default : node.GetObjectValue(factory);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            // Malformed JSON, or valid JSON of the wrong shape (Kiota throws InvalidOperationException
            // from GetObjectValue for an array/scalar where an object is expected). Both are input
            // errors (exit 2), not internal faults. Mirrors RichMenuService.ParseAsync.
            throw new MessageInputException($"Request JSON could not be parsed: {ex.Message}", ex);
        }

        return request ?? throw new MessageInputException("Could not parse the request JSON.");
    }
}

/// <summary>Summary of an audience group for display / listing.</summary>
public sealed record AudienceGroupSummary(long? AudienceGroupId, string? Description, string? Type, string? Status, long? AudienceCount);
