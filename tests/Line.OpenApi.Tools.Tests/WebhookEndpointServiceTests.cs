using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Tools.Services;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Verifies the URL guard on the webhook-endpoint and LIFF-url operations. Non-https / malformed
/// URLs are rejected as a MessageInputException (exit 2) before any network call, so this is
/// exercised without HTTP (same seam as RichMenuServiceTests).
/// </summary>
public sealed class WebhookEndpointServiceTests
{
    private static readonly ResolvedCredentials Credentials =
        new("default", ProfileExists: true, ChannelAccessToken: "token", ChannelId: null, ChannelSecret: null, PrivateKeyPath: null, Kid: null);

    [Theory]
    [InlineData("http://example.com/callback")] // not https
    [InlineData("ftp://example.com")]           // wrong scheme
    [InlineData("not a url")]                    // not absolute
    [InlineData("/relative/path")]               // not absolute
    public async Task SetWebhookEndpointAsync_RejectsNonHttpsUrl_BeforeAnyNetworkCall(string url)
        => await Assert.ThrowsAsync<MessageInputException>(() =>
            new MessageService().SetWebhookEndpointAsync(Credentials, url, CancellationToken.None));

    [Theory]
    [InlineData("http://example.com/callback")]
    [InlineData("not a url")]
    public async Task TestWebhookEndpointAsync_RejectsNonHttpsUrl_BeforeAnyNetworkCall(string url)
        => await Assert.ThrowsAsync<MessageInputException>(() =>
            new MessageService().TestWebhookEndpointAsync(Credentials, url, CancellationToken.None));

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("not a url")]
    public async Task LiffUpdateUrlAsync_RejectsNonHttpsUrl_BeforeAnyNetworkCall(string url)
        => await Assert.ThrowsAsync<MessageInputException>(() =>
            new LiffService().UpdateUrlAsync(Credentials, "1234567890-abcdefgh", url, CancellationToken.None));
}
