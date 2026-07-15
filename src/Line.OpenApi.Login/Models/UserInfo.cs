using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.Login.Models;

/// <summary>
/// Response of <c>GET /oauth2/v2.1/userinfo</c> (OpenID Connect userinfo; requires a user
/// access token with the <c>openid</c> scope). <see cref="Name"/> and <see cref="Picture"/> are
/// present only when the <c>profile</c> scope was also granted.
/// See https://developers.line.biz/en/reference/line-login/.
/// </summary>
public sealed class UserInfo : IParsable
{
    /// <summary>User ID.</summary>
    public string? Sub { get; set; }

    /// <summary>Display name. Present only when the <c>profile</c> scope was granted.</summary>
    public string? Name { get; set; }

    /// <summary>Profile image URL. Present only when the <c>profile</c> scope was granted.</summary>
    public string? Picture { get; set; }

    public static UserInfo CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new UserInfo();
    }

    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "sub", n => Sub = n.GetStringValue() },
            { "name", n => Name = n.GetStringValue() },
            { "picture", n => Picture = n.GetStringValue() },
        };

    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("sub", Sub);
        writer.WriteStringValue("name", Name);
        writer.WriteStringValue("picture", Picture);
    }
}
