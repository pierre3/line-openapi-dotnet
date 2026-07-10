using System;
using System.Threading;
using System.Threading.Tasks;

namespace Line.ChannelAccessToken;

/// <summary>
/// 発行済みトークンとその寿命。<see cref="RefreshingChannelAccessTokenProvider"/> が
/// キャッシュ期限を計算するために使う。
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

    /// <summary>発行されたチャネルアクセストークン。</summary>
    public string AccessToken { get; }

    /// <summary>トークンの有効期間（発行時点からの相対）。</summary>
    public TimeSpan Lifetime { get; }
}

/// <summary>
/// チャネルアクセストークンの発行操作を抽象化するシーム。
/// 実運用では <see cref="JwtAssertionTokenSource"/>（生成クライアントを消費）を使い、
/// テストでは HTTP を伴わないフェイク実装に差し替えられる。
/// </summary>
public interface IChannelAccessTokenSource
{
    /// <summary>トークンを 1 件発行する。</summary>
    Task<IssuedToken> IssueAsync(CancellationToken cancellationToken = default);
}
