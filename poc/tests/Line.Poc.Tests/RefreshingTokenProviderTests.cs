using System;
using System.Threading;
using System.Threading.Tasks;
using Line.ChannelAccessToken;
using Xunit;

namespace Line.Poc.Tests;

// 更新型トークンプロバイダ（短期/JWT）のキャッシュ・期限・並行更新二重発行防止の検証。
// HTTP を伴わないフェイク IChannelAccessTokenSource に差し替えて純粋なロジックを確認する。
public class RefreshingTokenProviderTests
{
    private sealed class MutableClock
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public DateTimeOffset Now => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    // 発行回数を数え、任意で発行完了を外部から制御できるソース。
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
                await _gate.Task.ConfigureAwait(false); // 発行完了を外部トリガまで待たせる
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
        Assert.Equal(1, source.IssueCount); // 2 回目はキャッシュヒット
    }

    [Fact]
    public async Task Refreshes_After_Expiry()
    {
        var clock = new MutableClock();
        var source = new CountingSource(TimeSpan.FromMinutes(30));
        var provider = new RefreshingChannelAccessTokenProvider(
            source, refreshMargin: TimeSpan.FromMinutes(5), clock: () => clock.Now);

        var t1 = await provider.GetAuthorizationTokenAsync(ApiUri); // token-1, refreshAt = +25m
        clock.Advance(TimeSpan.FromMinutes(26));                    // マージン込みで期限超過
        var t2 = await provider.GetAuthorizationTokenAsync(ApiUri); // 再発行 token-2

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

        // 発行を保留したまま多数の並行要求を起動 → 全員がゲートで待つ。
        var tasks = new Task<string>[32];
        for (var i = 0; i < tasks.Length; i++)
            tasks[i] = provider.GetAuthorizationTokenAsync(ApiUri);

        gate.SetResult(true);            // 発行を完了させる
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("token-1", r));
        Assert.Equal(1, source.IssueCount); // 二重発行していない
    }

    [Fact]
    public async Task Disallowed_Host_Returns_Empty_And_Does_Not_Issue()
    {
        var source = new CountingSource(TimeSpan.FromMinutes(30));
        var provider = new RefreshingChannelAccessTokenProvider(source);

        var token = await provider.GetAuthorizationTokenAsync(new Uri("https://evil.example.com/"));

        Assert.Equal(string.Empty, token);
        Assert.Equal(0, source.IssueCount); // 許可外にはトークン発行すらしない
    }
}
