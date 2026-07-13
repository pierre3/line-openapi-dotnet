using System;
using System.Threading;
using System.Threading.Tasks;

namespace Line.OpenApi.ChannelAccessToken;

/// <summary>
/// An issued token together with its lifetime. Used by
/// <see cref="RefreshingChannelAccessTokenProvider"/> to compute the cache expiry.
/// </summary>
public readonly struct IssuedToken
{
    public IssuedToken(string accessToken, TimeSpan lifetime)
    {
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentException("access token is required", nameof(accessToken));
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "lifetime must be positive");
        AccessToken = accessToken;
        Lifetime = lifetime;
    }

    /// <summary>The issued channel access token.</summary>
    public string AccessToken { get; }

    /// <summary>The token's validity period (relative to the moment of issuance).</summary>
    public TimeSpan Lifetime { get; }
}

/// <summary>
/// Seam that abstracts the channel-access-token issuance operation.
/// In production use <see cref="JwtAssertionTokenSource"/> (short-lived tokens,
/// <c>/oauth2/v2.1/token</c>) or <see cref="StatelessJwtAssertionTokenSource"/>
/// (stateless tokens, <c>/oauth2/v3/token</c>); in tests it can be replaced with a fake
/// implementation that performs no HTTP.
/// </summary>
public interface IChannelAccessTokenSource
{
    /// <summary>Issues a single token.</summary>
    Task<IssuedToken> IssueAsync(CancellationToken cancellationToken = default);
}
