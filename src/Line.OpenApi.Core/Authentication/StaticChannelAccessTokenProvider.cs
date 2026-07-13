using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Line.OpenApi.Core.Authentication;

/// <summary>
/// Minimal provider that holds and returns a long-lived channel access token.
/// Runtime issuance/refresh of short-lived tokens (v2.1 / JWT) is implemented by the
/// "refreshing provider" in Line.OpenApi.ChannelAccessToken, so that Core takes no reverse
/// dependency on it (design section 7).
/// </summary>
public sealed class StaticChannelAccessTokenProvider : IAccessTokenProvider
{
    private readonly string _token;

    public StaticChannelAccessTokenProvider(string channelAccessToken, params string[] allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(channelAccessToken))
            throw new ArgumentException("channel access token is required", nameof(channelAccessToken));
        _token = channelAccessToken;
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
