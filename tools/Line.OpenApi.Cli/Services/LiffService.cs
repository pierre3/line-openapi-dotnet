using System.Collections.Concurrent;
using System.Text;
using Line.OpenApi.Cli.Configuration;
using Line.OpenApi.Liff;
using Line.OpenApi.Liff.Generated.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

namespace Line.OpenApi.Cli.Services;

/// <summary>
/// D. LIFF app management. Thin wrapper over the <see cref="LiffClient"/> facade
/// (single host <c>api.line.me</c>; R1 not applicable). Add/update accept the app
/// definition as JSON, parsed into the generated request models.
/// </summary>
public sealed class LiffService
{
    // Memoized per token to avoid HttpClient accumulation in the long-running MCP server
    // (code gate Medium#1). CreateWithStaticToken preserves the api.line.me host restriction.
    private static readonly ConcurrentDictionary<string, LiffClient> Clients = new(StringComparer.Ordinal);

    private static LiffClient Create(ResolvedCredentials credentials) =>
        Clients.GetOrAdd(credentials.RequireAccessToken(), static token => LiffClient.CreateWithStaticToken(token));

    /// <summary>Lists registered LIFF apps.</summary>
    public async Task<IReadOnlyList<LiffAppSummary>> ListAsync(ResolvedCredentials credentials, CancellationToken cancellationToken)
    {
        var res = await Create(credentials).GetAppsAsync(cancellationToken).ConfigureAwait(false);
        return res?.Apps?
            .Select(a => new LiffAppSummary(a.LiffId, a.View?.Type?.ToString(), a.View?.Url, a.Description))
            .ToList()
            ?? new List<LiffAppSummary>();
    }

    /// <summary>Adds a LIFF app from a JSON definition. Returns the new liffId.</summary>
    public async Task<string?> AddAsync(ResolvedCredentials credentials, string appJson, CancellationToken cancellationToken)
    {
        var request = await ParseAsync(appJson, AddLiffAppRequest.CreateFromDiscriminatorValue, cancellationToken).ConfigureAwait(false);
        var res = await Create(credentials).AddAppAsync(request, cancellationToken).ConfigureAwait(false);
        return res?.LiffId;
    }

    /// <summary>Updates a LIFF app from a JSON definition.</summary>
    public async Task UpdateAsync(ResolvedCredentials credentials, string liffId, string appJson, CancellationToken cancellationToken)
    {
        var request = await ParseAsync(appJson, UpdateLiffAppRequest.CreateFromDiscriminatorValue, cancellationToken).ConfigureAwait(false);
        await Create(credentials).UpdateAppAsync(liffId, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a LIFF app.</summary>
    public Task DeleteAsync(ResolvedCredentials credentials, string liffId, CancellationToken cancellationToken) =>
        Create(credentials).DeleteAppAsync(liffId, cancellationToken);

    private static async Task<T> ParseAsync<T>(string json, ParsableFactory<T> factory, CancellationToken cancellationToken)
        where T : IParsable
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var node = await new JsonParseNodeFactory()
            .GetRootParseNodeAsync("application/json", stream, cancellationToken)
            .ConfigureAwait(false);
        return node.GetObjectValue(factory)
            ?? throw new MessageInputException("Could not parse the LIFF app definition JSON.");
    }
}

/// <summary>Summary of a LIFF app for display.</summary>
public sealed record LiffAppSummary(string? LiffId, string? ViewType, string? Url, string? Description);
