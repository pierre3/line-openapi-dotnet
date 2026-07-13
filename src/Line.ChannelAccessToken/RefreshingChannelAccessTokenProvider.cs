using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.Core.Authentication;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Line.ChannelAccessToken;

/// <summary>
/// An <see cref="IAccessTokenProvider"/> that issues and caches a short-lived channel access
/// token (v2.1 / JWT) at runtime and re-issues it as expiry approaches.
///
/// Design section 7:
/// - Placed in this package (Line.ChannelAccessToken) rather than Core, to avoid a reverse
///   dependency.
/// - Allowed hosts are injectable/extensible rather than hard-coded (returns an empty string
///   on the negative path).
/// - Prevents duplicate issuance under concurrent refresh (<see cref="SemaphoreSlim"/> +
///   double-check).
/// </summary>
public sealed class RefreshingChannelAccessTokenProvider : IAccessTokenProvider, IDisposable
{
    private readonly IChannelAccessTokenSource _source;
    private readonly TimeSpan _refreshMargin;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    // Stored as ticks (long) and read/written via Volatile so the fast path (outside the lock)
    // avoids torn reads.
    private long _refreshAtTicks;

    /// <param name="source">The token issuer (in production, <see cref="JwtAssertionTokenSource"/>).</param>
    /// <param name="refreshMargin">
    /// How far ahead of expiry to re-issue. Defaults to 5 minutes, to absorb clock skew and
    /// issuance latency.
    /// </param>
    /// <param name="allowedHosts">
    /// Hosts the token may be attached to. When unspecified, <see cref="LineHosts.Default"/>
    /// (api / api-data) is used.
    /// </param>
    /// <param name="clock">Time source (replaceable in tests). Defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    public RefreshingChannelAccessTokenProvider(
        IChannelAccessTokenSource source,
        TimeSpan? refreshMargin = null,
        IEnumerable<string>? allowedHosts = null,
        Func<DateTimeOffset>? clock = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _refreshMargin = refreshMargin ?? TimeSpan.FromMinutes(5);
        if (_refreshMargin < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(refreshMargin), "refresh margin must not be negative");
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        var hosts = allowedHosts is null ? LineHosts.Default : new List<string>(allowedHosts).ToArray();
        AllowedHostsValidator = new AllowedHostsValidator(hosts is { Length: > 0 } ? hosts : LineHosts.Default);
    }

    public AllowedHostsValidator AllowedHostsValidator { get; }

    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        if (uri is null) throw new ArgumentNullException(nameof(uri));

        // Do not attach the token to hosts outside the allow list (a safety net against
        // cross-host leakage via the RedirectHandler).
        if (!AllowedHostsValidator.IsUrlHostValid(uri))
            return string.Empty;

        // Fast path: if there is an unexpired cached token, return it without taking the lock.
        var cached = Volatile.Read(ref _cachedToken);
        if (cached is not null && _clock().UtcTicks < Volatile.Read(ref _refreshAtTicks))
            return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check: another thread may have refreshed while we were waiting.
            if (_cachedToken is not null && _clock().UtcTicks < Volatile.Read(ref _refreshAtTicks))
                return _cachedToken;

            var issuedAt = _clock();
            var issued = await _source.IssueAsync(cancellationToken).ConfigureAwait(false);

            // Even when the margin is greater than or equal to the lifetime, keep the cache
            // minimally functional by clamping the lower bound to the issuance time.
            var refreshAt = issuedAt + issued.Lifetime - _refreshMargin;
            var effective = refreshAt > issuedAt ? refreshAt : issuedAt;
            // Publish the expiry before the token, so that whoever sees the new token is
            // guaranteed to also see the new expiry.
            Volatile.Write(ref _refreshAtTicks, effective.UtcTicks);
            Volatile.Write(ref _cachedToken, issued.AccessToken);
            return issued.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
