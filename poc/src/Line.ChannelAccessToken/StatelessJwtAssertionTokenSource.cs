using System;
using System.Threading;
using System.Threading.Tasks;
using Line.ChannelAccessToken.Generated;
using Line.ChannelAccessToken.Generated.Models;
using Microsoft.Kiota.Abstractions;

namespace Line.ChannelAccessToken;

/// <summary>
/// 生成クライアント（<see cref="ChannelAccessTokenClient"/>）を消費し、
/// JWT アサーションで<b>ステートレス</b>チャネルアクセストークン（<c>/oauth2/v3/token</c>）を
/// 発行する <see cref="IChannelAccessTokenSource"/> 実装。
///
/// ステートレストークンは有効アクティブトークン数の上限が無い代わりに、有効期間は 15 分で
/// 満了まで失効できない。短命なので <see cref="RefreshingChannelAccessTokenProvider"/> と
/// 組み合わせて都度発行する運用を想定する。
///
/// <para>
/// R2 使い勝手: <c>/oauth2/v3/token</c> のボディは discriminator 無しの oneOf で、生成コードでは
/// 合成ラッパ <c>TokenRequestBuilder.TokenPostRequestBody</c>（<c>IComposedTypeWrapper</c>）として
/// 現れる。このラッパは内側の要求モデルを<b>入れ子オブジェクト</b>として直列化するため、
/// form-urlencoded（Kiota の Form シリアライザは入れ子非対応）ではそのまま送ると
/// <c>"Form serialization does not support nested objects."</c> で失敗する。本クラスは合成ラッパを
/// 使わず、平坦な要求モデル <see cref="IssueStatelessChannelTokenByJWTAssertionRequest"/> を
/// 直接ボディに載せて送出することで、この落とし穴を隠蔽し
/// <see cref="JwtAssertionTokenSource"/>（<c>/oauth2/v2.1/token</c>）と同じ発行シームを提供する。
/// </para>
///
/// JWT アサーション自体の生成（チャネルの秘密鍵での署名）はアプリ固有のため、
/// 呼び出し側が assertionFactory で供給する（本ライブラリは署名鍵を扱わない）。
/// </summary>
public sealed class StatelessJwtAssertionTokenSource : IChannelAccessTokenSource
{
    private readonly ChannelAccessTokenClient _client;
    private readonly Func<CancellationToken, Task<string>> _assertionFactory;

    /// <param name="client">生成済み <see cref="ChannelAccessTokenClient"/>。</param>
    /// <param name="assertionFactory">
    /// 発行のたびに署名済み JWT アサーション文字列を返すファクトリ。
    /// </param>
    public StatelessJwtAssertionTokenSource(
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

        // grant_type / client_assertion_type は仕様上単一値の enum なので固定で埋める
        // （呼び出し側に選ばせない）。
        var request = new IssueStatelessChannelTokenByJWTAssertionRequest
        {
            GrantType = IssueStatelessChannelTokenByJWTAssertionRequest_grant_type.Client_credentials,
            ClientAssertionType =
                IssueStatelessChannelTokenByJWTAssertionRequest_client_assertion_type
                    .UrnIetfParamsOauthClientAssertionTypeJwtBearer,
            ClientAssertion = assertion,
        };

        // 生成ビルダーの ToPostRequestInformation/PostAsync は合成ラッパ経由で入れ子直列化に
        // 陥るため使わない。クライアントの baseurl 込みパスパラメータとアダプタ（既定シリアライザ
        // 登録済み）を流用し、生成ビルダーと同じ URL テンプレートへ平坦な要求モデルを
        // form-urlencoded ボディとして自前で載せる。
        var adapter = _client.InternalRequestAdapter;
        var requestInfo = new RequestInformation(
            Method.POST, "{+baseurl}/oauth2/v3/token", _client.InternalPathParameters);
        requestInfo.Headers.TryAdd("Accept", "application/json");
        requestInfo.SetContentFromParsable(
            adapter, "application/x-www-form-urlencoded", request);

        var response = await adapter
            .SendAsync<IssueStatelessChannelAccessTokenResponse>(
                requestInfo,
                IssueStatelessChannelAccessTokenResponse.CreateFromDiscriminatorValue,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // 応答検証は JwtAssertionTokenSource と対称に揃える（エラー面の一貫性）。
        // access_token は空文字も不正応答として扱う。
        if (string.IsNullOrEmpty(response?.AccessToken) || response.ExpiresIn is null)
            throw new InvalidOperationException(
                "Token issuance response did not contain access_token / expires_in.");

        if (response.ExpiresIn.Value <= 0)
            throw new InvalidOperationException(
                $"Token issuance response contained a non-positive expires_in ({response.ExpiresIn.Value}).");

        return new IssuedToken(response.AccessToken, TimeSpan.FromSeconds(response.ExpiresIn.Value));
    }
}
