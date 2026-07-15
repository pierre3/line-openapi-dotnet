using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.Login.Models;

/// <summary>
/// Payload returned by <c>POST /oauth2/v2.1/verify</c> (server-side ID-token verification,
/// where LINE validates the signature and claims). These are the verified ID-token claims.
/// See https://developers.line.biz/en/docs/line-login/verify-id-token/.
/// </summary>
public sealed class VerifiedIdToken : IParsable
{
    /// <summary>Issuer. Always <c>https://access.line.me</c>.</summary>
    public string? Iss { get; set; }

    /// <summary>User ID the ID token was issued for.</summary>
    public string? Sub { get; set; }

    /// <summary>Channel ID (audience).</summary>
    public string? Aud { get; set; }

    /// <summary>Expiry time (UNIX seconds).</summary>
    public long? Exp { get; set; }

    /// <summary>Issued-at time (UNIX seconds).</summary>
    public long? Iat { get; set; }

    /// <summary>Authentication time (UNIX seconds). Present in some flows.</summary>
    public long? AuthTime { get; set; }

    /// <summary>Nonce from the authorization request (present only when a nonce was sent).</summary>
    public string? Nonce { get; set; }

    /// <summary>Authentication methods (for example <c>pwd</c>, <c>lineqr</c>, <c>linesso</c>).</summary>
    public List<string>? Amr { get; set; }

    /// <summary>Display name. Present only when the <c>profile</c> scope was granted.</summary>
    public string? Name { get; set; }

    /// <summary>Profile image URL. Present only when the <c>profile</c> scope was granted.</summary>
    public string? Picture { get; set; }

    /// <summary>Email address. Present only when the <c>email</c> scope was granted.</summary>
    public string? Email { get; set; }

    public static VerifiedIdToken CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new VerifiedIdToken();
    }

    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "iss", n => Iss = n.GetStringValue() },
            { "sub", n => Sub = n.GetStringValue() },
            { "aud", n => Aud = n.GetStringValue() },
            { "exp", n => Exp = n.GetLongValue() },
            { "iat", n => Iat = n.GetLongValue() },
            { "auth_time", n => AuthTime = n.GetLongValue() },
            { "nonce", n => Nonce = n.GetStringValue() },
            { "amr", n => Amr = n.GetCollectionOfPrimitiveValues<string>() is { } v ? new List<string>(v) : null },
            { "name", n => Name = n.GetStringValue() },
            { "picture", n => Picture = n.GetStringValue() },
            { "email", n => Email = n.GetStringValue() },
        };

    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("iss", Iss);
        writer.WriteStringValue("sub", Sub);
        writer.WriteStringValue("aud", Aud);
        writer.WriteLongValue("exp", Exp);
        writer.WriteLongValue("iat", Iat);
        writer.WriteLongValue("auth_time", AuthTime);
        writer.WriteStringValue("nonce", Nonce);
        writer.WriteCollectionOfPrimitiveValues("amr", Amr);
        writer.WriteStringValue("name", Name);
        writer.WriteStringValue("picture", Picture);
        writer.WriteStringValue("email", Email);
    }
}
