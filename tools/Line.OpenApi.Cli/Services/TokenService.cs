using Line.OpenApi.Cli.Configuration;
using Line.OpenApi.ChannelAccessToken;
using Line.OpenApi.ChannelAccessToken.Generated;
using Line.OpenApi.ChannelAccessToken.Generated.Oauth2.V21.Revoke;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Line.OpenApi.Cli.Services;

/// <summary>Channel access token kinds the CLI can issue (spec §4.1).</summary>
public enum TokenKind
{
    /// <summary>v2.1 channel access token (JWT assertion → <c>/oauth2/v2.1/token</c>).</summary>
    V21,

    /// <summary>Stateless channel access token (<c>/oauth2/v3/token</c>).</summary>
    Stateless,
}

/// <summary>
/// A. Token management. Because the ChannelAccessToken library exposes no DI helper or facade
/// (spec §4.1, Medium①), this service constructs the generated client itself and drives the
/// hand-written token sources; the JWT assertion is signed by <see cref="JwtAssertionBuilder"/>.
/// </summary>
public sealed class TokenService
{
    /// <summary>Issues a channel access token via a signed JWT assertion.</summary>
    public async Task<TokenIssueResult> IssueAsync(
        ResolvedCredentials credentials, TokenKind kind, TimeSpan tokenLifetime, CancellationToken cancellationToken)
    {
        if (tokenLifetime <= TimeSpan.Zero || tokenLifetime > TimeSpan.FromDays(30))
        {
            throw new MessageInputException("Token lifetime must be between 1 and 30 days.");
        }

        var channelId = credentials.RequireChannelId();
        var kid = credentials.Kid
            ?? throw new CredentialException($"No key id (kid) available. Provide it via --kid, ${CredentialResolver.EnvKid}, or profile '{credentials.ProfileName}'.");
        var keyPath = credentials.RequirePrivateKeyPath();

        string pem;
        try
        {
            pem = await File.ReadAllTextAsync(keyPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CredentialException($"Cannot read private key at '{keyPath}': {ex.Message}");
        }

        // The token endpoint is unauthenticated (the assertion is the credential).
        using var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider());
        var client = new ChannelAccessTokenClient(adapter);

        Task<string> AssertionFactory(CancellationToken _) =>
            Task.FromResult(JwtAssertionBuilder.Build(channelId, kid, pem, tokenLifetime));

        IChannelAccessTokenSource source = kind == TokenKind.Stateless
            ? new StatelessJwtAssertionTokenSource(client, AssertionFactory)
            : new JwtAssertionTokenSource(client, AssertionFactory);

        var issued = await source.IssueAsync(cancellationToken).ConfigureAwait(false);
        return new TokenIssueResult(issued.AccessToken, issued.Lifetime, kind, kid);
    }

    /// <summary>Verifies a token's validity and remaining lifetime.</summary>
    public async Task<TokenVerifyResult> VerifyAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider());
        var client = new ChannelAccessTokenClient(adapter);

        try
        {
            var res = await client.Oauth2.V21.Verify
                .GetAsync(c => c.QueryParameters.AccessToken = accessToken, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new TokenVerifyResult(true, res?.ExpiresIn, res?.ClientId, res?.Scope);
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 400)
        {
            // Only 400 means the token itself is invalid/expired; 5xx / network / rate-limit
            // errors must not be misreported as "invalid" — let them propagate to exit 4.
            return new TokenVerifyResult(false, null, null, null);
        }
    }

    /// <summary>Revokes a token. v2.1 revocation requires the channel id and secret.</summary>
    public async Task RevokeAsync(ResolvedCredentials credentials, string accessToken, CancellationToken cancellationToken)
    {
        var clientId = credentials.RequireChannelId();
        var clientSecret = credentials.RequireChannelSecret();

        using var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider());
        var client = new ChannelAccessTokenClient(adapter);

        await client.Oauth2.V21.Revoke
            .PostAsync(new RevokePostRequestBody
            {
                AccessToken = accessToken,
                ClientId = clientId,
                ClientSecret = clientSecret,
            }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Result of issuing a token. Carries the raw token; adapters decide exposure (spec §4.5).</summary>
public sealed record TokenIssueResult(string AccessToken, TimeSpan? Lifetime, TokenKind Kind, string? KeyId);

/// <summary>Result of verifying a token. Contains no secret.</summary>
public sealed record TokenVerifyResult(bool Valid, long? ExpiresInSeconds, string? ClientId, string? Scope);
