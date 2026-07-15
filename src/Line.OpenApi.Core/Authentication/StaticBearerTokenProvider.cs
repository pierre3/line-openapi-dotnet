using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Line.OpenApi.Core.Authentication;

/// <summary>
/// Generic provider that holds and returns a single Bearer token, gated by an allow list of
/// hosts. It is the credential-agnostic counterpart of
/// <see cref="StaticChannelAccessTokenProvider"/>: use it to attach a caller-supplied token
/// (for example a LINE Login <b>user access token</b>) to requests, while ensuring the token is
/// never sent to a host outside the allow list.
///
/// <para>
/// Like <see cref="StaticChannelAccessTokenProvider"/>, it wires into Kiota's
/// <see cref="BaseBearerTokenAuthenticationProvider"/> as an <see cref="IAccessTokenProvider"/>.
/// It is kept as a distinct type (rather than folding the channel-token provider into it) so
/// each usage keeps a self-describing name at the call site.
/// </para>
/// </summary>
public sealed class StaticBearerTokenProvider : IAccessTokenProvider
{
    private readonly string _token;

    /// <param name="token">The Bearer token to attach.</param>
    /// <param name="allowedHosts">
    /// Hosts the token may be attached to. When empty, the Bot/Messaging default
    /// (<see cref="LineHosts.Default"/>) is used.
    /// </param>
    public StaticBearerTokenProvider(string token, params string[] allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("token is required", nameof(token));
        _token = token;
        AllowedHostsValidator = new AllowedHostsValidator(
            allowedHosts is { Length: > 0 } ? allowedHosts : LineHosts.Default);
    }

    public AllowedHostsValidator AllowedHostsValidator { get; }

    public Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        // Do not attach the token to hosts outside the allow list (covered by a negative test).
        if (!AllowedHostsValidator.IsUrlHostValid(uri))
            return Task.FromResult(string.Empty);
        return Task.FromResult(_token);
    }
}
