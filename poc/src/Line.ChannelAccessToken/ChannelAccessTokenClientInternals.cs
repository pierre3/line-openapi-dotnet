using System.Collections.Generic;
using Microsoft.Kiota.Abstractions;

namespace Line.ChannelAccessToken.Generated;

// Hand-written glue that exposes the generated client's (BaseRequestBuilder-derived)
// protected RequestAdapter / PathParameters to the rest of the assembly only, via a partial
// of the same class.
//
// Needed so that StatelessJwtAssertionTokenSource can avoid the oneOf composed body of
// /oauth2/v3/token (nested serialization, unsupported by form-urlencoded) and hand-build a
// flat request model for low-level dispatch. Being internal, it never appears on the public
// API surface (the PublicApi snapshot). The file lives outside Generated/ so it is not
// overwritten on regeneration.
public partial class ChannelAccessTokenClient
{
    /// <summary>The underlying <see cref="IRequestAdapter"/> (baseurl default and serializers already registered).</summary>
    internal IRequestAdapter InternalRequestAdapter => RequestAdapter;

    /// <summary>
    /// A <b>defensive copy</b> of the path parameters (including baseurl).
    /// Exposing the client's raw <c>PathParameters</c> would let other code in the assembly
    /// rewrite baseurl and change the destination, so we return a fresh copy each time to
    /// preserve immutability (the cost is negligible).
    /// </summary>
    internal Dictionary<string, object> InternalPathParameters => new(PathParameters);
}
