using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Line.Core.Authentication;

/// <summary>
/// 長期チャネルアクセストークンを保持して返す最小プロバイダ（PoC 用）。
/// 短期トークン（v2.1 / JWT）の実行時取得・更新は Line.ChannelAccessToken 側の
/// 「更新型プロバイダ」で実装し、Core への逆依存を作らない（設計 §7）。
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
        // 許可ホスト外にはトークンを付与しない（負側テスト対象）。
        if (!AllowedHostsValidator.IsUrlHostValid(uri))
            return Task.FromResult(string.Empty);
        return Task.FromResult(_token);
    }
}
