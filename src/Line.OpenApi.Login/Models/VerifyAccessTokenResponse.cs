using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.Login.Models;

/// <summary>
/// Response of <c>GET /oauth2/v2.1/verify</c> (access-token validity check).
/// See https://developers.line.biz/en/reference/line-login/.
/// </summary>
public sealed class VerifyAccessTokenResponse : IParsable
{
    /// <summary>Space-separated list of permissions (scopes) granted to the token.</summary>
    public string? Scope { get; set; }

    /// <summary>Channel ID for which the access token was issued.</summary>
    public string? ClientId { get; set; }

    /// <summary>Number of seconds until the access token expires.</summary>
    public long? ExpiresIn { get; set; }

    public static VerifyAccessTokenResponse CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new VerifyAccessTokenResponse();
    }

    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "scope", n => Scope = n.GetStringValue() },
            { "client_id", n => ClientId = n.GetStringValue() },
            { "expires_in", n => ExpiresIn = n.GetLongValue() },
        };

    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("scope", Scope);
        writer.WriteStringValue("client_id", ClientId);
        writer.WriteLongValue("expires_in", ExpiresIn);
    }
}
