using System;
using System.Threading;
using System.Threading.Tasks;
using Line.ChannelAccessToken;
using Xunit;

namespace Line.Tests;

// Verifies the refreshing token provider (short-lived/JWT) for caching, expiry, and prevention of duplicate issuance under concurrent refresh.
// Substitutes a fake IChannelAccessTokenSource with no HTTP to check the pure logic.
public class RefreshingTokenProviderTests
{
    private sealed class MutableClock
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public DateTimeOffset Now => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    // A source that counts issuances and can optionally have issuance completion controlled externally.
    private sealed class CountingSource : IChannelAccessTokenSource
    {
        private readonly TimeSpan _lifetime;
        private readonly TaskCompletionSource<bool>? _gate;
        private int _count;

        public CountingSource(TimeSpan lifetime, TaskCompletionSource<bool>? gate = null)
        {
            _lifetime = lifetime;
            _gate = gate;
        }

        public int IssueCount => Volatile.Read(ref _count);

        public async Task<IssuedToken> IssueAsync(CancellationToken cancellationToken = default)
        {
            var n = Interlocked.Increment(ref _count);
            if (_gate is not null)
                await _gate.Task.ConfigureAwait(false); // make issuance wait until an external trigger
            return new IssuedToken($"token-{n}", _lifetime);
        }
    }

    private static readonly Uri ApiUri = new("https://api.line.me/v2/bot/message/push");

    [Fact]
    public async Task Caches_Token_Across_Calls()
    {
        var clock = new MutableClock();
        var source = new CountingSource(TimeSpan.FromMinutes(30));
        var provider = new RefreshingChannelAccessTokenProvider(
            source, refreshMargin: TimeSpan.FromMinutes(5), clock: () => clock.Now);

        var t1 = await provider.GetAuthorizationTokenAsync(ApiUri);
        var t2 = await provider.GetAuthorizationTokenAsync(ApiUri);

        Assert.Equal("token-1", t1);
        Assert.Equal("token-1", t2);
        Assert.Equal(1, source.IssueCount); // the second call is a cache hit
    }

    [Fact]
    public async Task Refreshes_After_Expiry()
    {
        var clock = new MutableClock();
        var source = new CountingSource(TimeSpan.FromMinutes(30));
        var provider = new RefreshingChannelAccessTokenProvider(
            source, refreshMargin: TimeSpan.FromMinutes(5), clock: () => clock.Now);

        var t1 = await provider.GetAuthorizationTokenAsync(ApiUri); // token-1, refreshAt = +25m
        clock.Advance(TimeSpan.FromMinutes(26));                    // exceed expiry including the margin
        var t2 = await provider.GetAuthorizationTokenAsync(ApiUri); // re-issue token-2

        Assert.Equal("token-1", t1);
        Assert.Equal("token-2", t2);
        Assert.Equal(2, source.IssueCount);
    }

    [Fact]
    public async Task Concurrent_Calls_Issue_Only_Once()
    {
        var clock = new MutableClock();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new CountingSource(TimeSpan.FromMinutes(30), gate);
        var provider = new RefreshingChannelAccessTokenProvider(
            source, refreshMargin: TimeSpan.FromMinutes(5), clock: () => clock.Now);

        // Launch many concurrent requests while issuance is held -> everyone waits at the gate.
        var tasks = new Task<string>[32];
        for (var i = 0; i < tasks.Length; i++)
            tasks[i] = provider.GetAuthorizationTokenAsync(ApiUri);

        gate.SetResult(true);            // complete the issuance
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("token-1", r));
        Assert.Equal(1, source.IssueCount); // no duplicate issuance
    }

    [Fact]
    public async Task Disallowed_Host_Returns_Empty_And_Does_Not_Issue()
    {
        var source = new CountingSource(TimeSpan.FromMinutes(30));
        var provider = new RefreshingChannelAccessTokenProvider(source);

        var token = await provider.GetAuthorizationTokenAsync(new Uri("https://evil.example.com/"));

        Assert.Equal(string.Empty, token);
        Assert.Equal(0, source.IssueCount); // for non-allowed hosts, no token is even issued
    }
}
