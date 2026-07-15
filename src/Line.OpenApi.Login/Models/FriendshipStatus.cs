using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.Login.Models;

/// <summary>
/// Response of <c>GET /friendship/v1/status</c> (Social API; requires a user access token with
/// the <c>profile</c> scope). Indicates whether the user has added the LINE Official Account
/// linked to the LINE Login channel as a friend and has not blocked it.
/// See https://developers.line.biz/en/reference/social-api/.
/// </summary>
public sealed class FriendshipStatus : IParsable
{
    /// <summary>
    /// <c>true</c> if the user has added the linked Official Account as a friend and has not
    /// blocked it; otherwise <c>false</c>.
    /// </summary>
    public bool? FriendFlag { get; set; }

    public static FriendshipStatus CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new FriendshipStatus();
    }

    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "friendFlag", n => FriendFlag = n.GetBoolValue() },
        };

    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteBoolValue("friendFlag", FriendFlag);
    }
}
