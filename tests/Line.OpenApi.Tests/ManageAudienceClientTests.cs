using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Line.OpenApi.ManageAudience;
using Line.OpenApi.ManageAudience.Generated.Api.Models;
using Xunit;

namespace Line.OpenApi.Tests;

// Path/host verification for the ManageAudienceClient facade. The control plane routes to
// api.line.me; the transport-level assertions (data-plane routing + multipart) live in
// ManageAudienceClientHttpTests.
public class ManageAudienceClientTests
{
    [Fact]
    public void GetAudienceData_BuildsGet_ToApiLineMe()
    {
        var client = ManageAudienceClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.V2.Bot.AudienceGroup[123L].ToGetRequestInformation();

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/v2/bot/audienceGroup/123", req.URI.AbsolutePath);
    }

    [Fact]
    public void Upload_BuildsPost_ToApiLineMe()
    {
        var client = ManageAudienceClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.V2.Bot.AudienceGroup.Upload
            .ToPostRequestInformation(new CreateAudienceGroupRequest());

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/v2/bot/audienceGroup/upload", req.URI.AbsolutePath);
    }

    // --- Argument guards ---

    [Fact]
    public async Task CreateForUploadingUserIdsAsync_NullRequest_Throws()
    {
        var client = ManageAudienceClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CreateForUploadingUserIdsAsync(null!));
    }

    [Fact]
    public async Task AddUserIdsAsync_NullRequest_Throws()
    {
        var client = ManageAudienceClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.AddUserIdsAsync(null!));
    }

    [Fact]
    public async Task UploadUserIdsByFileAsync_NullFile_Throws()
    {
        var client = ManageAudienceClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.UploadUserIdsByFileAsync(null!));
    }

    [Fact]
    public async Task AddUserIdsByFileAsync_NullFile_Throws()
    {
        var client = ManageAudienceClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.AddUserIdsByFileAsync(1L, null!));
    }
}
