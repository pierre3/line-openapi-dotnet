using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.Login.Models;

/// <summary>
/// Response of <c>GET /v2/profile</c> (requires a user access token with the <c>profile</c>
/// scope). See https://developers.line.biz/en/reference/line-login/.
/// </summary>
public sealed class LineUserProfile : IParsable
{
    /// <summary>User ID.</summary>
    public string? UserId { get; set; }

    /// <summary>Display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Profile image URL (absent when the user has no image).</summary>
    public string? PictureUrl { get; set; }

    /// <summary>Status message (absent when the user has none).</summary>
    public string? StatusMessage { get; set; }

    public static LineUserProfile CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new LineUserProfile();
    }

    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "userId", n => UserId = n.GetStringValue() },
            { "displayName", n => DisplayName = n.GetStringValue() },
            { "pictureUrl", n => PictureUrl = n.GetStringValue() },
            { "statusMessage", n => StatusMessage = n.GetStringValue() },
        };

    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("userId", UserId);
        writer.WriteStringValue("displayName", DisplayName);
        writer.WriteStringValue("pictureUrl", PictureUrl);
        writer.WriteStringValue("statusMessage", StatusMessage);
    }
}
