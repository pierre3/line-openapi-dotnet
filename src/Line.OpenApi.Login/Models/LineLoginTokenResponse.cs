using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.Login.Models;

/// <summary>
/// Response of <c>POST /oauth2/v2.1/token</c> (both the authorization-code exchange and the
/// refresh grant). See https://developers.line.biz/en/reference/line-login/.
/// </summary>
public sealed class LineLoginTokenResponse : IParsable
{
    /// <summary>Access token. Valid for 30 days.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Number of seconds until the access token expires.</summary>
    public long? ExpiresIn { get; set; }

    /// <summary>ID token (JWT). Present only when the <c>openid</c> scope was requested.</summary>
    public string? IdToken { get; set; }

    /// <summary>Refresh token. Valid for up to 90 days.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Space-separated list of granted permissions (scopes).</summary>
    public string? Scope { get; set; }

    /// <summary>Token type. Always <c>Bearer</c>.</summary>
    public string? TokenType { get; set; }

    public static LineLoginTokenResponse CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new LineLoginTokenResponse();
    }

    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "access_token", n => AccessToken = n.GetStringValue() },
            { "expires_in", n => ExpiresIn = n.GetLongValue() },
            { "id_token", n => IdToken = n.GetStringValue() },
            { "refresh_token", n => RefreshToken = n.GetStringValue() },
            { "scope", n => Scope = n.GetStringValue() },
            { "token_type", n => TokenType = n.GetStringValue() },
        };

    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("access_token", AccessToken);
        writer.WriteLongValue("expires_in", ExpiresIn);
        writer.WriteStringValue("id_token", IdToken);
        writer.WriteStringValue("refresh_token", RefreshToken);
        writer.WriteStringValue("scope", Scope);
        writer.WriteStringValue("token_type", TokenType);
    }
}
