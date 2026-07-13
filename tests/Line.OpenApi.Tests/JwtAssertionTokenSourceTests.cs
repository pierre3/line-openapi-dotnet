using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.ChannelAccessToken;
using Line.OpenApi.ChannelAccessToken.Generated;
using Line.OpenApi.ChannelAccessToken.Generated.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Line.OpenApi.Tests;

// Response validation for JwtAssertionTokenSource.IssueAsync. Makes no real HTTP calls; injects arbitrary
// issue responses via a fake IRequestAdapter that replaces only SendAsync.
// The SerializationWriterFactory / BaseUrl needed for request assembly (form serialization) are
// delegated to the real HttpClientRequestAdapter.
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

        // All invalid responses are normalized to InvalidOperationException (so IssuedToken's
        // ArgumentOutOfRangeException does not leak out).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.IssueAsync());
        // The primary goal is asserting the exception type (InvalidOperationException). The message assertion is an aid
        // to prevent confusion with the null branch, and may be updated when the message changes.
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

        // Both null and empty string are normalized to InvalidOperationException at the response-validation stage
        // (so IssuedToken's ArgumentException does not leak out).
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.IssueAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task IssueAsync_Empty_Assertion_Throws_Before_Issuing(string? assertion)
    {
        // If assertionFactory returns empty/null, it is rejected with InvalidOperationException before making any HTTP call
        // (stopping on the safe side before external transmission). The response content is irrelevant, so any valid response works.
        var client = new ChannelAccessTokenClient(new StubResponseAdapter(
            new IssueChannelAccessTokenResponse { AccessToken = "token-value", ExpiresIn = 3600 }));
        var source = new JwtAssertionTokenSource(client, _ => Task.FromResult(assertion!));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.IssueAsync());
        Assert.Contains("assertion", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    // A fake that returns a fixed response for SendAsync<T> only and delegates everything else to the real adapter.
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
