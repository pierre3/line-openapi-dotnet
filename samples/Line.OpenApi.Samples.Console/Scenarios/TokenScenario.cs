using System;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.ChannelAccessToken;
using Line.OpenApi.ChannelAccessToken.Generated;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

using Con = System.Console;

namespace Line.OpenApi.Samples.Console.Scenarios;

/// <summary>
/// Shows issuing a short-lived channel access token via a JWT assertion
/// (<c>JwtAssertionTokenSource</c> → <c>/oauth2/v2.1/token</c>). Offline it explains the flow;
/// with a signing key (channel id + kid + private key) it performs a real issuance.
///
/// In production you would wrap the source in <c>RefreshingChannelAccessTokenProvider</c> and
/// pass it to <c>AddLineMessaging(sp => ...)</c> so tokens are cached and re-issued near expiry.
/// </summary>
internal static class TokenScenario
{
    public static async Task RunAsync()
    {
        Con.WriteLine("== Channel access token: issue via JWT assertion ==\n");
        Con.WriteLine("  1. build a signed JWT assertion (RS256, signed with the channel private key)");
        Con.WriteLine("  2. POST it to https://api.line.me/oauth2/v2.1/token (form-urlencoded)");
        Con.WriteLine("  3. receive { access_token, expires_in }");
        Con.WriteLine("  → in production, wrap in RefreshingChannelAccessTokenProvider for caching + auto-refresh.\n");

        if (!DemoEnv.HasSigningKey)
        {
            Con.WriteLine("[offline] Signing key not configured — token not issued.");
            Con.WriteLine("          Set LINE_CHANNEL_ID, LINE_KID and LINE_PRIVATE_KEY (or LINE_PRIVATE_KEY_PATH).");
            return;
        }

        // The token endpoint is unauthenticated (the assertion is the credential), so the
        // generated client uses an anonymous auth provider.
        // The adapter owns a default HttpClient (none supplied), so dispose it.
        using var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider());
        var client = new ChannelAccessTokenClient(adapter);

        var source = new JwtAssertionTokenSource(
            client,
            assertionFactory: _ => Task.FromResult(JwtAssertionBuilder.Build(
                DemoEnv.ChannelId!, DemoEnv.Kid!, DemoEnv.PrivateKeyPem!, TimeSpan.FromDays(1))));

        Con.WriteLine("[live] Issuing token...");
        var issued = await source.IssueAsync(CancellationToken.None);

        // Never print the token itself. Show only non-secret metadata.
        Con.WriteLine($"       issued a token ({issued.AccessToken.Length} chars), valid for {issued.Lifetime}.");
    }
}
