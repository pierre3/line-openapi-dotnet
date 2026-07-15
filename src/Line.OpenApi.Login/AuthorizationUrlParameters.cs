using System.Collections.Generic;

namespace Line.OpenApi.Login;

/// <summary>
/// Parameters for building the LINE Login authorization URL
/// (<c>GET https://access.line.me/oauth2/v2.1/authorize</c>). The client ID and the fixed
/// <c>response_type=code</c> are supplied by <see cref="LoginClient.BuildAuthorizationUrl"/>.
/// See https://developers.line.biz/en/docs/line-login/integrate-line-login/.
/// </summary>
public sealed class AuthorizationUrlParameters
{
    /// <summary>Callback URL registered in the LINE Developers Console. Required.</summary>
    public required string RedirectUri { get; set; }

    /// <summary>
    /// Requested scopes (for example <c>openid</c>, <c>profile</c>, <c>email</c>). Serialized as
    /// a space-separated list. Required (at least one).
    /// </summary>
    public required IReadOnlyCollection<string> Scopes { get; set; }

    /// <summary>
    /// Opaque CSRF token. Store it in the session and verify it on the callback. Required.
    /// Use <see cref="LineLoginSecurity.GenerateState"/> to produce a secure value.
    /// </summary>
    public required string State { get; set; }

    /// <summary>
    /// Nonce echoed into the ID token to guard against replay. Optional but recommended when
    /// requesting <c>openid</c>.
    /// </summary>
    public string? Nonce { get; set; }

    /// <summary>PKCE code challenge (base64url of the S256 hash). Optional.</summary>
    public string? CodeChallenge { get; set; }

    /// <summary>PKCE challenge method. Defaults to <c>S256</c> (the only value LINE supports).</summary>
    public string CodeChallengeMethod { get; set; } = "S256";

    /// <summary>Forces the consent screen behavior: <c>consent</c>, <c>none</c>, or <c>login</c>. Optional.</summary>
    public string? Prompt { get; set; }

    /// <summary>Add-friend option shown on consent: <c>normal</c> or <c>aggressive</c>. Optional.</summary>
    public string? BotPrompt { get; set; }

    /// <summary>Preferred UI language(s), for example <c>ja-JP</c>. Optional.</summary>
    public string? UiLocales { get; set; }

    /// <summary>
    /// How the response is returned: <c>query</c>, <c>form_post</c>, or the JWT variants. Optional.
    /// </summary>
    public string? ResponseMode { get; set; }
}
