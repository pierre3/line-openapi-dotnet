using System;
using System.Threading.Tasks;
using Line.Liff;
using Line.Liff.Generated.Models;
using Xunit;

namespace Line.Poc.Tests;

// LiffClient ファサードの経路検証。単一ホスト(api.line.me)へ CRUD 各操作が
// 正しい メソッド/URL で組み立てられるかを、生成ビルダーの RequestInformation で確認する。
// 実 HTTP は不要（HTTP 経路は LiffClientHttpTests で別途検証）。
public class LiffClientTests
{
    [Fact]
    public void GetApps_BuildsGet_ToApiLineMe()
    {
        var client = LiffClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.Liff.V1.Apps.ToGetRequestInformation();

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/liff/v1/apps", req.URI.AbsolutePath);
    }

    [Fact]
    public void AddApp_BuildsPost_ToApiLineMe()
    {
        var client = LiffClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.Liff.V1.Apps.ToPostRequestInformation(new AddLiffAppRequest());

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/liff/v1/apps", req.URI.AbsolutePath);
    }

    [Fact]
    public void UpdateApp_BuildsPut_WithLiffId()
    {
        var client = LiffClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.Liff.V1.Apps["liff-123"]
            .ToPutRequestInformation(new UpdateLiffAppRequest());

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/liff/v1/apps/liff-123", req.URI.AbsolutePath);
    }

    [Fact]
    public void DeleteApp_BuildsDelete_WithLiffId()
    {
        var client = LiffClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.Liff.V1.Apps["liff-123"].ToDeleteRequestInformation();

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/liff/v1/apps/liff-123", req.URI.AbsolutePath);
    }

    // --- 便利メソッドの引数ガード（手書き公開契約の回帰防止） ---

    [Fact]
    public async Task AddAppAsync_NullRequest_Throws()
    {
        var client = LiffClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.AddAppAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task UpdateAppAsync_MissingLiffId_Throws(string? liffId)
    {
        var client = LiffClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.UpdateAppAsync(liffId!, new UpdateLiffAppRequest()));
    }

    [Fact]
    public async Task UpdateAppAsync_NullRequest_Throws()
    {
        var client = LiffClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.UpdateAppAsync("liff-123", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task DeleteAppAsync_MissingLiffId_Throws(string? liffId)
    {
        var client = LiffClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(() => client.DeleteAppAsync(liffId!));
    }
}
