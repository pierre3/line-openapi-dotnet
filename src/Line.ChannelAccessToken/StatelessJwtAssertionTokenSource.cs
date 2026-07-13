using System;
using System.Threading;
using System.Threading.Tasks;
using Line.ChannelAccessToken.Generated;
using Line.ChannelAccessToken.Generated.Models;
using Microsoft.Kiota.Abstractions;

namespace Line.ChannelAccessToken;

/// <summary>
/// An <see cref="IChannelAccessTokenSource"/> implementation that consumes the generated
/// client (<see cref="ChannelAccessTokenClient"/>) and issues a <b>stateless</b> channel
/// access token (<c>/oauth2/v3/token</c>) via a JWT assertion.
///
/// A stateless token has no limit on the number of concurrently active tokens, but in
/// exchange it lives for only 15 minutes and cannot be revoked before expiry. Because it is
/// short-lived, the intended usage is to combine it with
/// <see cref="RefreshingChannelAccessTokenProvider"/> and issue one on demand.
///
/// <para>
/// R2 usability: the body of <c>/oauth2/v3/token</c> is a discriminator-less oneOf, which in
/// the generated code shows up as the composed wrapper
/// <c>TokenRequestBuilder.TokenPostRequestBody</c> (an <c>IComposedTypeWrapper</c>). That
/// wrapper serializes the inner request model as a <b>nested object</b>, so sending it as-is
/// over form-urlencoded (whose Kiota Form serializer does not support nesting) fails with
/// <c>"Form serialization does not support nested objects."</c> This class avoids the
/// composed wrapper and instead sends the flat request model
/// <see cref="IssueStatelessChannelTokenByJWTAssertionRequest"/> directly as the body,
/// hiding this pitfall and offering the same issuance seam as
/// <see cref="JwtAssertionTokenSource"/> (<c>/oauth2/v2.1/token</c>).
/// </para>
///
/// Producing the JWT assertion itself (signing with the channel's private key) is
/// application-specific, so the caller supplies it through assertionFactory (this library
/// never handles signing keys).
/// </summary>
public sealed class StatelessJwtAssertionTokenSource : IChannelAccessTokenSource
{
    private readonly ChannelAccessTokenClient _client;
    private readonly Func<CancellationToken, Task<string>> _assertionFactory;

    /// <param name="client">A constructed <see cref="ChannelAccessTokenClient"/>.</param>
    /// <param name="assertionFactory">
    /// Factory that returns a signed JWT assertion string on each issuance.
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

        // grant_type / client_assertion_type are single-value enums in the spec, so fill them
        // in with fixed values (do not let the caller choose).
        var request = new IssueStatelessChannelTokenByJWTAssertionRequest
        {
            GrantType = IssueStatelessChannelTokenByJWTAssertionRequest_grant_type.Client_credentials,
            ClientAssertionType =
                IssueStatelessChannelTokenByJWTAssertionRequest_client_assertion_type
                    .UrnIetfParamsOauthClientAssertionTypeJwtBearer,
            ClientAssertion = assertion,
        };

        // Do not use the generated builder's ToPostRequestInformation/PostAsync, since they go
        // through the composed wrapper and fall into nested serialization. Reuse the client's
        // path parameters (including baseurl) and its adapter (with the default serializers
        // already registered), and hand-build the same URL template as the generated builder,
        // carrying the flat request model as a form-urlencoded body.
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

        // Keep response validation symmetric with JwtAssertionTokenSource (consistent error
        // surface). Treat an empty access_token as an invalid response too.
        if (string.IsNullOrEmpty(response?.AccessToken) || response.ExpiresIn is null)
            throw new InvalidOperationException(
                "Token issuance response did not contain access_token / expires_in.");

        if (response.ExpiresIn.Value <= 0)
            throw new InvalidOperationException(
                $"Token issuance response contained a non-positive expires_in ({response.ExpiresIn.Value}).");

        return new IssuedToken(response.AccessToken, TimeSpan.FromSeconds(response.ExpiresIn.Value));
    }
}
