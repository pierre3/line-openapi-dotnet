using System;
using System.Threading;
using System.Threading.Tasks;
using Line.ChannelAccessToken.Generated;
using Line.ChannelAccessToken.Generated.Oauth2.V21.Token;

namespace Line.ChannelAccessToken;

/// <summary>
/// 生成クライアント（<see cref="ChannelAccessTokenClient"/>）を消費し、
/// JWT アサーションで短期チャネルアクセストークン（<c>/oauth2/v2.1/token</c>）を発行する
/// <see cref="IChannelAccessTokenSource"/> 実装。
///
/// JWT アサーション自体の生成（チャネルの秘密鍵での署名）はアプリ固有のため、
/// 呼び出し側が assertionFactory で供給する（本ライブラリは署名鍵を扱わない）。
/// </summary>
public sealed class JwtAssertionTokenSource : IChannelAccessTokenSource
{
    // RFC 7523: JWT Bearer client assertion。
    private const string JwtBearerAssertionType =
        "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private readonly ChannelAccessTokenClient _client;
    private readonly Func<CancellationToken, Task<string>> _assertionFactory;

    /// <param name="client">生成済み <see cref="ChannelAccessTokenClient"/>。</param>
    /// <param name="assertionFactory">
    /// 発行のたびに署名済み JWT アサーション文字列を返すファクトリ。
    /// </param>
    public JwtAssertionTokenSource(
        ChannelAccessTokenClient client,
        Func<CancellationToken, Task<string>> assertionFactory)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _assertionFactory = assertionFactory ?? throw new ArgumentNullException(nameof(assertionFactory));
    }

    public async Task<IssuedToken> IssueAsync(CancellationToken cancellationToken = default)
    {
        var assertion = await _assertionFactory(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(assertion))
            throw new InvalidOperationException("JWT assertion factory returned an empty assertion.");

        var body = new TokenPostRequestBody
        {
            GrantType = "client_credentials",
            ClientAssertionType = JwtBearerAssertionType,
            ClientAssertion = assertion,
        };

        var response = await _client.Oauth2.V21.Token
            .PostAsync(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // access_token は空文字も不正応答として扱う（null のみだと空文字が IssuedToken
        // コンストラクタの ArgumentException として漏れ、expires_in と非対称になる）。
        if (string.IsNullOrEmpty(response?.AccessToken) || response.ExpiresIn is null)
            throw new InvalidOperationException(
                "Token issuance response did not contain access_token / expires_in.");

        // expires_in が 0 以下だと IssuedToken コンストラクタが ArgumentOutOfRangeException を
        // 投げ、他の「応答が不正」ケース（InvalidOperationException）とエラー面が食い違う。
        // 応答検証としてここで揃える（expires_in は秘匿値でないため値を含めてよい）。
        if (response.ExpiresIn.Value <= 0)
            throw new InvalidOperationException(
                $"Token issuance response contained a non-positive expires_in ({response.ExpiresIn.Value}).");

        return new IssuedToken(response.AccessToken, TimeSpan.FromSeconds(response.ExpiresIn.Value));
    }
}
