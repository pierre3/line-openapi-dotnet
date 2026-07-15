using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.Login.Models;

/// <summary>
/// Error body returned by the LINE Login OAuth endpoints on a non-2xx response (for example
/// <c>{"error":"invalid_grant","error_description":"..."}</c>). It derives from
/// <see cref="ApiException"/> so it is thrown by the client with the HTTP status code
/// (<see cref="ApiException.ResponseStatusCode"/>) preserved, while also exposing the OAuth
/// <see cref="Error"/> / <see cref="ErrorDescription"/> — the most common failure modes of the
/// login flow (expired/invalid code, invalid_grant, etc.).
/// </summary>
public sealed class LoginErrorResponse : ApiException, IParsable
{
    /// <summary>OAuth error code (for example <c>invalid_grant</c>, <c>invalid_request</c>).</summary>
    public string? Error { get; set; }

    /// <summary>Human-readable description of the error, when provided by LINE.</summary>
    public string? ErrorDescription { get; set; }

    /// <inheritdoc/>
    public override string Message =>
        Error is null
            ? base.Message
            : ErrorDescription is null ? Error : $"{Error}: {ErrorDescription}";

    /// <summary>Creates a new instance for Kiota error deserialization.</summary>
    public static LoginErrorResponse CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new LoginErrorResponse();
    }

    /// <inheritdoc/>
    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "error", n => Error = n.GetStringValue() },
            { "error_description", n => ErrorDescription = n.GetStringValue() },
        };

    /// <inheritdoc/>
    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("error", Error);
        writer.WriteStringValue("error_description", ErrorDescription);
    }
}
