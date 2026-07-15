using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Module.Generated;
using Line.OpenApi.Module.Generated.Models;

namespace Line.OpenApi.Module;

/// <summary>
/// Facade for the Module channel API (partner/agency operation via LOA). It wraps a single-host
/// (api.line.me) Kiota client and provides convenience methods for the four operations the spec
/// defines: detach, acquire/release chat control, and list attached modules.
///
/// Note: module-attach (manager.line.biz / HTTP Basic auth / form + PKCE) is intentionally not
/// included in this package. For lower-level operations, the generated builders are directly
/// accessible via <see cref="Api"/>.
///
/// Usage:
///   var module = ModuleClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
///   var modules = await module.GetModulesAsync();
///   await module.AcquireChatControlAsync("CHAT_ID", new AcquireChatControlRequest { /* ... */ });
///   await module.ReleaseChatControlAsync("CHAT_ID");
///   await module.DetachAsync(new DetachModuleRequest { /* ... */ });
/// </summary>
public sealed class ModuleClient
{
    /// <summary>The generated client (exposed for low-level operations).</summary>
    public ModuleApiClient Api { get; }

    /// <param name="authProvider">Authentication provider (static or refreshing, either works).</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> shared by the adapter. Supplied by <c>IHttpClientFactory</c>
    /// via DI. When null, the adapter creates its own default <see cref="HttpClient"/>.
    /// </param>
    public ModuleClient(IAuthenticationProvider authProvider, HttpClient? httpClient = null)
    {
        if (authProvider is null) throw new ArgumentNullException(nameof(authProvider));

        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        Api = new ModuleApiClient(adapter);
    }

    /// <summary>Helper to quickly construct from a long-lived channel access token.</summary>
    public static ModuleClient CreateWithStaticToken(string channelAccessToken)
    {
        var provider = new StaticChannelAccessTokenProvider(channelAccessToken, LineHosts.Api);
        var auth = new BaseBearerTokenAuthenticationProvider(provider);
        return new ModuleClient(auth);
    }

    /// <summary>
    /// Gets basic information about the bots of LINE Official Accounts that have attached module
    /// channels (GET /v2/bot/list). <paramref name="start"/> is the pagination token; <paramref name="limit"/>
    /// caps the number of bots returned (LINE default 100).
    /// </summary>
    public Task<GetModulesResponse?> GetModulesAsync(
        string? start = null, int? limit = null, CancellationToken cancellationToken = default)
        => Api.V2.Bot.List.GetAsync(config =>
        {
            if (start is not null) config.QueryParameters.Start = start;
            if (limit is not null) config.QueryParameters.Limit = limit;
        }, cancellationToken);

    /// <summary>Acquires chat control for a chat, transferring it to this module channel (POST /v2/bot/chat/{chatId}/control/acquire).</summary>
    public async Task AcquireChatControlAsync(
        string chatId, AcquireChatControlRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(chatId)) throw new ArgumentException("chatId is required", nameof(chatId));
        if (request is null) throw new ArgumentNullException(nameof(request));

        using var _ = await Api.V2.Bot.Chat[chatId].Control.Acquire
            .PostAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases chat control back to the primary channel (POST /v2/bot/chat/{chatId}/control/release).</summary>
    public async Task ReleaseChatControlAsync(string chatId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(chatId)) throw new ArgumentException("chatId is required", nameof(chatId));

        using var _ = await Api.V2.Bot.Chat[chatId].Control.Release
            .PostAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Detaches the module channel from the LINE Official Account (POST /v2/bot/channel/detach).</summary>
    public async Task DetachAsync(
        DetachModuleRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        using var _ = await Api.V2.Bot.Channel.Detach
            .PostAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
