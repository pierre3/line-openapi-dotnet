using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Internal;

/// <summary>
/// Request body of <c>POST /message/v3/notifier/send</c>. Unlike the other MINI App request
/// bodies, this one nests a dynamic object (<c>params</c>, the template variable/value pairs)
/// under a fixed field, so it needs its own small <see cref="IParsable"/> rather than the flat
/// <see cref="FlatFields"/>.
/// </summary>
internal sealed class SendServiceMessageRequestBody : IParsable
{
    private readonly string _templateName;
    private readonly IReadOnlyDictionary<string, string> _params;
    private readonly string _notificationToken;

    public SendServiceMessageRequestBody(
        string templateName, IReadOnlyDictionary<string, string> parameters, string notificationToken)
    {
        _templateName = templateName;
        _params = parameters;
        _notificationToken = notificationToken;
    }

    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>();

    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("templateName", _templateName);
        writer.WriteObjectValue<FlatFields>(
            "params",
            new FlatFields(_params.Select(kv => FlatFields.Field(kv.Key, kv.Value))));
        writer.WriteStringValue("notificationToken", _notificationToken);
    }
}
