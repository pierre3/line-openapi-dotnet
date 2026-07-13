using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Line.ChannelAccessToken.Generated;
using Line.ChannelAccessToken.Generated.Oauth2.V21.Token;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Line.Tests;

// Verifies that channel-access-token token issuance is serialized correctly as
// application/x-www-form-urlencoded (section 2-B). Makes no real HTTP calls; checks the Content-Type and
// body keys of the RequestInformation assembled by the generated builder.
public class FormUrlEncodedTests
{
    private static ChannelAccessTokenClient CreateClient()
    {
        // The endpoint requires no authentication, so Anonymous is fine.
        // The constructor registers the Form serializer into the default registry (a precondition of this test).
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider());
        return new ChannelAccessTokenClient(adapter);
    }

    [Fact]
    public void Token_Request_Uses_FormUrlEncoded_ContentType()
    {
        var client = CreateClient();
        var body = new TokenPostRequestBody
        {
            GrantType = "client_credentials",
            ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ClientAssertion = "SIGNED.JWT.VALUE",
        };

        var req = client.Oauth2.V21.Token.ToPostRequestInformation(body);

        Assert.True(req.Headers.TryGetValue("Content-Type", out var contentTypes));
        Assert.Contains("application/x-www-form-urlencoded", contentTypes!.Single());
    }

    [Fact]
    public async Task Token_Request_Body_Contains_Form_Keys()
    {
        var client = CreateClient();
        var body = new TokenPostRequestBody
        {
            GrantType = "client_credentials",
            ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ClientAssertion = "SIGNED.JWT.VALUE",
        };

        var req = client.Oauth2.V21.Token.ToPostRequestInformation(body);

        using var reader = new StreamReader(req.Content);
        var payload = await reader.ReadToEndAsync();

        // form-urlencoded: key=value joined by &, values URL-encoded.
        Assert.Contains("grant_type=client_credentials", payload);
        Assert.Contains("client_assertion_type=urn%3Aietf", payload); // ':' is encoded
        Assert.Contains("client_assertion=SIGNED.JWT.VALUE", payload);
        Assert.Contains("&", payload);
        Assert.DoesNotContain("{", payload); // confirm it is not JSON
    }
}
