using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.Core.Authentication;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Line.ChannelAccessToken;

/// <summary>
/// 短期チャネルアクセストークン（v2.1 / JWT）を実行時に発行・キャッシュし、
/// 期限が近づいたら再発行する <see cref="IAccessTokenProvider"/>。
///
/// 設計方針 §7:
/// - 逆依存回避のため Core ではなく本パッケージ（Line.ChannelAccessToken）に置く。
/// - 許可ホストはハードコードせず注入・拡張可能にする（負側で空文字を返す）。
/// - 並行更新の二重発行を防止する（<see cref="SemaphoreSlim"/> + double-check）。
/// </summary>
public sealed class RefreshingChannelAccessTokenProvider : IAccessTokenProvider, IDisposable
{
    private readonly IChannelAccessTokenSource _source;
    private readonly TimeSpan _refreshMargin;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    // 高速パス（ロック外）でも torn read を避けるため ticks(long) で保持し Volatile で読み書きする。
    private long _refreshAtTicks;

    /// <param name="source">トークン発行元（実運用は <see cref="JwtAssertionTokenSource"/>）。</param>
    /// <param name="refreshMargin">
    /// 期限のどれだけ手前で再発行するかのマージン。既定 5 分。時計のずれや発行遅延に備える。
    /// </param>
    /// <param name="allowedHosts">
    /// トークンを付与してよいホスト。未指定時は <see cref="LineHosts.Default"/>（api / api-data）。
    /// </param>
    /// <param name="clock">時刻源（テスト用に差し替え可能）。既定は <see cref="DateTimeOffset.UtcNow"/>。</param>
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

        // 許可ホスト外にはトークンを付与しない（RedirectHandler 経由のクロスホスト漏洩対策の保険）。
        if (!AllowedHostsValidator.IsUrlHostValid(uri))
            return string.Empty;

        // 高速パス: 未期限のキャッシュがあればロック無しで返す。
        var cached = Volatile.Read(ref _cachedToken);
        if (cached is not null && _clock().UtcTicks < Volatile.Read(ref _refreshAtTicks))
            return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // double-check: 待機中に別スレッドが更新した可能性がある。
            if (_cachedToken is not null && _clock().UtcTicks < Volatile.Read(ref _refreshAtTicks))
                return _cachedToken;

            var issuedAt = _clock();
            var issued = await _source.IssueAsync(cancellationToken).ConfigureAwait(false);

            // マージンが寿命以上でも最低限キャッシュが機能するよう、下限を発行時刻に張り付ける。
            var refreshAt = issuedAt + issued.Lifetime - _refreshMargin;
            var effective = refreshAt > issuedAt ? refreshAt : issuedAt;
            // 期限を先に公開してからトークンを公開する（新トークンを見たら必ず新期限も見える順序）。
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
