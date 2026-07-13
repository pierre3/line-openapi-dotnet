using System;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.ChannelAccessToken.Generated;
using Line.OpenApi.ChannelAccessToken.Generated.Oauth2.V21.Token;

namespace Line.OpenApi.ChannelAccessToken;

/// <summary>
/// An <see cref="IChannelAccessTokenSource"/> implementation that consumes the generated
/// client (<see cref="ChannelAccessTokenClient"/>) and issues a short-lived channel access
/// token (<c>/oauth2/v2.1/token</c>) via a JWT assertion.
///
/// Producing the JWT assertion itself (signing with the channel's private key) is
/// application-specific, so the caller supplies it through assertionFactory (this library
/// never handles signing keys).
/// </summary>
public sealed class JwtAssertionTokenSource : IChannelAccessTokenSource
{
    // RFC 7523: JWT Bearer client assertion.
    private const string JwtBearerAssertionType =
        "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private readonly ChannelAccessTokenClient _client;
    private readonly Func<CancellationToken, Task<string>> _assertionFactory;

    /// <param name="client">A constructed <see cref="ChannelAccessTokenClient"/>.</param>
    /// <param name="assertionFactory">
    /// Factory that returns a signed JWT assertion string on each issuance.
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

        // Treat an empty access_token as an invalid response too (otherwise an empty string
        // would leak out as the ArgumentException from the IssuedToken constructor, which is
        // asymmetric with expires_in handling).
        if (string.IsNullOrEmpty(response?.AccessToken) || response.ExpiresIn is null)
            throw new InvalidOperationException(
                "Token issuance response did not contain access_token / expires_in.");

        // A non-positive expires_in would make the IssuedToken constructor throw
        // ArgumentOutOfRangeException, giving a different error surface than the other
        // "invalid response" cases (InvalidOperationException). Normalize it here as part of
        // response validation (expires_in is not a secret, so it is fine to include the value).
        if (response.ExpiresIn.Value <= 0)
            throw new InvalidOperationException(
                $"Token issuance response contained a non-positive expires_in ({response.ExpiresIn.Value}).");

        return new IssuedToken(response.AccessToken, TimeSpan.FromSeconds(response.ExpiresIn.Value));
    }
}
