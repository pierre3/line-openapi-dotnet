using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.ChannelAccessToken;
using Line.ChannelAccessToken.Generated;
using Line.ChannelAccessToken.Generated.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Line.Poc.Tests;

// JwtAssertionTokenSource.IssueAsync の応答検証。実 HTTP は叩かず、SendAsync だけを
// 差し替えたフェイク IRequestAdapter で任意の発行レスポンスを注入する。
// リクエスト組み立て（form 直列化）に必要な SerializationWriterFactory / BaseUrl は
// 実 HttpClientRequestAdapter に委譲する。
public class JwtAssertionTokenSourceTests
{
    private static JwtAssertionTokenSource CreateSource(IssueChannelAccessTokenResponse response)
    {
        var client = new ChannelAccessTokenClient(new StubResponseAdapter(response));
        return new JwtAssertionTokenSource(client, _ => Task.FromResult("SIGNED.JWT.VALUE"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task IssueAsync_NonPositive_ExpiresIn_Throws_InvalidOperation(int expiresIn)
    {
        var source = CreateSource(new IssueChannelAccessTokenResponse
        {
            AccessToken = "token-value",
            ExpiresIn = expiresIn,
        });

        // 応答不正はすべて InvalidOperationException に揃える（IssuedToken の
        // ArgumentOutOfRangeException が漏れ出さないこと）。
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.IssueAsync());
        // 主目的は例外型（InvalidOperationException）の主張。文言 assertion は null 分岐との
        // 取り違え防止のための補助であり、メッセージ変更時は追随してよい。
        Assert.Contains("non-positive expires_in", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task IssueAsync_Missing_AccessToken_Throws_InvalidOperation(string? accessToken)
    {
        var source = CreateSource(new IssueChannelAccessTokenResponse
        {
            AccessToken = accessToken,
            ExpiresIn = 3600,
        });

        // null も空文字も応答検証段階で InvalidOperationException に揃える
        // （IssuedToken の ArgumentException が漏れ出さないこと）。
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.IssueAsync());
    }

    [Fact]
    public async Task IssueAsync_Valid_Response_Returns_IssuedToken()
    {
        var source = CreateSource(new IssueChannelAccessTokenResponse
        {
            AccessToken = "token-value",
            ExpiresIn = 3600,
        });

        var issued = await source.IssueAsync();

        Assert.Equal("token-value", issued.AccessToken);
        Assert.Equal(TimeSpan.FromSeconds(3600), issued.Lifetime);
    }

    // SendAsync<T> のみ固定レスポンスを返し、それ以外は実アダプタへ委譲するフェイク。
    private sealed class StubResponseAdapter : IRequestAdapter
    {
        private readonly IRequestAdapter _inner =
            new HttpClientRequestAdapter(new AnonymousAuthenticationProvider());
        private readonly IssueChannelAccessTokenResponse _response;

        public StubResponseAdapter(IssueChannelAccessTokenResponse response) => _response = response;

        public ISerializationWriterFactory SerializationWriterFactory => _inner.SerializationWriterFactory;

        public string? BaseUrl
        {
            get => _inner.BaseUrl;
            set => _inner.BaseUrl = value;
        }

        public void EnableBackingStore(IBackingStoreFactory backingStoreFactory) =>
            _inner.EnableBackingStore(backingStoreFactory);

        public Task<ModelType?> SendAsync<ModelType>(
            RequestInformation requestInfo,
            ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) where ModelType : IParsable =>
            Task.FromResult((ModelType?)(object?)_response);

        public Task<IEnumerable<ModelType>?> SendCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) where ModelType : IParsable =>
            throw new NotImplementedException();

        public Task<ModelType?> SendPrimitiveAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IEnumerable<ModelType>?> SendPrimitiveCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SendNoContentAsync(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<T?> ConvertToNativeRequestAsync<T>(
            RequestInformation requestInfo,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
