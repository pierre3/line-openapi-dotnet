using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Tools.Mcp;
using Line.OpenApi.Tools.Services;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Verifies the <c>dryRun</c> send path: valid input is parsed and summarized without any network
/// call, malformed / non-array input is rejected, and the dry-run branch returns before credentials
/// are ever resolved (so no send can occur).
/// </summary>
public sealed class MessageDryRunTests
{
    private const string TextMessage = "[{\"type\":\"text\",\"text\":\"hi\"}]";

    private static CredentialResolver EmptyResolver() =>
        new(new ConfigStore(Path.Combine(Path.GetTempPath(), $"line-dryrun-{Guid.NewGuid():N}.json")));

    /// <summary>
    /// Runs an action with all LINE_* credential environment variables cleared, so a send-path
    /// regression fails deterministically (via RequireAccessToken) regardless of the host environment.
    /// Safe because the test collection disables parallelization.
    /// </summary>
    private static async Task<T> WithoutCredentialEnvAsync<T>(Func<Task<T>> action)
    {
        var names = new[]
        {
            CredentialResolver.EnvProfile, CredentialResolver.EnvChannelAccessToken,
            CredentialResolver.EnvChannelId, CredentialResolver.EnvChannelSecret,
            CredentialResolver.EnvPrivateKeyPath, CredentialResolver.EnvKid,
        };
        var saved = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        foreach (var n in names) Environment.SetEnvironmentVariable(n, null);
        try
        {
            return await action();
        }
        finally
        {
            foreach (var (n, v) in saved) Environment.SetEnvironmentVariable(n, v);
        }
    }

    [Fact]
    public async Task ValidateMessagesAsync_reports_count_and_types_without_sending()
    {
        var result = await new MessageService().ValidateMessagesAsync(
            "[{\"type\":\"text\",\"text\":\"hi\"},{\"type\":\"sticker\",\"packageId\":\"1\",\"stickerId\":\"2\"}]",
            CancellationToken.None);

        Assert.True(result.DryRun);
        Assert.True(result.Valid);
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { "TextMessage", "StickerMessage" }, result.MessageTypes);
    }

    [Theory]
    [InlineData("{ not an array")]          // malformed JSON
    [InlineData("{\"type\":\"text\"}")]     // valid JSON but a single object, not an array
    [InlineData("[]")]                        // empty array
    [InlineData("null")]                      // JSON null
    [InlineData("5")]                         // scalar
    public async Task ValidateMessagesAsync_rejects_non_array_or_empty_input(string input)
    {
        // Guards the dryRun contract: Kiota returns an empty collection (no exception) for these,
        // which would otherwise be reported as a spurious "valid, 0 messages" (code gate M1).
        await Assert.ThrowsAsync<MessageInputException>(() =>
            new MessageService().ValidateMessagesAsync(input, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateMessagesAsync_is_a_shape_check_only_unknown_type_falls_back_to_base()
    {
        // Documents that dryRun validates JSON shape/parseability, NOT full schema conformance:
        // an unknown discriminator parses to the base Message rather than being rejected.
        var result = await new MessageService().ValidateMessagesAsync(
            "[{\"type\":\"totally-bogus\"}]", CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Equal(1, result.Count);
        Assert.Equal(new[] { "Message" }, result.MessageTypes);
    }

    [Fact]
    public async Task MessagePush_dryRun_validates_without_resolving_credentials_or_sending()
    {
        // With credentials absent, the real send path would throw a CredentialException the moment
        // it required a token (MessageService.Create -> RequireAccessToken). A clean validation
        // result proves the dryRun branch short-circuits before ReadTools.Resolve is ever called.
        var json = await WithoutCredentialEnvAsync(() => WriteTools.MessagePush(
            new MessageService(), EmptyResolver(), to: "Udeadbeef", messagesJson: TextMessage, dryRun: true));

        Assert.Contains("\"dryRun\": true", json);
        Assert.Contains("TextMessage", json);
    }

    [Fact]
    public async Task MessageMulticast_dryRun_validates_without_resolving_credentials_or_sending()
    {
        var json = await WithoutCredentialEnvAsync(() => WriteTools.MessageMulticast(
            new MessageService(), EmptyResolver(), to: new[] { "Ua", "Ub" }, messagesJson: TextMessage, dryRun: true));

        Assert.Contains("\"dryRun\": true", json);
    }

    [Fact]
    public async Task MessageBroadcast_dryRun_validates_without_resolving_credentials_or_sending()
    {
        var json = await WithoutCredentialEnvAsync(() => WriteTools.MessageBroadcast(
            new MessageService(), EmptyResolver(), messagesJson: TextMessage, dryRun: true));

        Assert.Contains("\"dryRun\": true", json);
    }

    [Fact]
    public async Task MessageReply_dryRun_validates_without_resolving_credentials_or_sending()
    {
        var json = await WithoutCredentialEnvAsync(() => WriteTools.MessageReply(
            new MessageService(), EmptyResolver(), replyToken: "rt", messagesJson: TextMessage, dryRun: true));

        Assert.Contains("\"dryRun\": true", json);
    }
}
