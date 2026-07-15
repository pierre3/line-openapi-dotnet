using System;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.ManageAudience.Generated.Api;    // control plane (api.line.me)
using Line.OpenApi.ManageAudience.Generated.Blob;   // data plane (api-data.line.me)
using ApiModels = Line.OpenApi.ManageAudience.Generated.Api.Models;
using BlobModels = Line.OpenApi.ManageAudience.Generated.Blob.Models;

namespace Line.OpenApi.ManageAudience;

/// <summary>
/// Facade for the Manage Audience API. Like Messaging, it unifies two Kiota clients: the control
/// plane (<see cref="Api"/>, api.line.me) for the JSON audience-group operations, and the data
/// plane (<see cref="Blob"/>, api-data.line.me) for the two by-file user-ID upload operations,
/// which use <c>multipart/form-data</c>.
///
/// The full control surface (create / get / list / delete audience groups, click &amp; imp
/// retargeting, description update, shared audiences) is reached through <see cref="Api"/>; a few
/// common operations have convenience methods. The by-file uploads are wrapped by
/// <see cref="UploadUserIdsByFileAsync"/> / <see cref="AddUserIdsByFileAsync"/>, which build the
/// multipart body (with the required <c>text/plain</c> file part) so callers do not have to.
///
/// Usage:
///   var ma = ManageAudienceClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
///   using var file = File.OpenRead("user-ids.txt");
///   var created = await ma.UploadUserIdsByFileAsync(file, description: "my audience");
///   await ma.AddUserIdsByFileAsync(created!.AudienceGroupId!.Value, moreIds);
/// </summary>
public sealed class ManageAudienceClient
{
    // Held so the multipart helpers can set MultipartBody.RequestAdapter (required to serialize
    // the parts) and so uploads go through the data-plane (api-data.line.me) adapter.
    private readonly IRequestAdapter _blobAdapter;

    /// <summary>Control-plane client (audience-group CRUD, click/imp, shared; api.line.me).</summary>
    public ManageAudienceApiClient Api { get; }

    /// <summary>Data-plane client (by-file user-ID upload; api-data.line.me).</summary>
    public ManageAudienceBlobApiClient Blob { get; }

    /// <param name="authProvider">Authentication provider (static or refreshing, either works).</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> shared by the two adapters (control plane and data plane).
    /// Supplied by <c>IHttpClientFactory</c> via DI. When null, each adapter creates its own
    /// default <see cref="HttpClient"/>. Since the adapters build the URLs themselves,
    /// <see cref="HttpClient.BaseAddress"/> is not used, so sharing one is fine.
    /// </param>
    public ManageAudienceClient(IAuthenticationProvider authProvider, HttpClient? httpClient = null)
    {
        if (authProvider is null) throw new ArgumentNullException(nameof(authProvider));

        // Control plane: use the spec's server (api.line.me) as-is.
        var apiAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        Api = new ManageAudienceApiClient(apiAdapter);

        // Data plane: separate adapter with BaseUrl set to api-data.line.me *before* construction.
        // The generated client fixes baseurl into PathParameters in its constructor (defaulting to
        // api.line.me when empty), so setting BaseUrl afterward would not take effect. See R1.
        var blobAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        blobAdapter.BaseUrl = $"https://{LineHosts.ApiData}";
        Blob = new ManageAudienceBlobApiClient(blobAdapter);
        _blobAdapter = blobAdapter;
    }

    /// <summary>Helper to quickly construct from a long-lived channel access token.</summary>
    public static ManageAudienceClient CreateWithStaticToken(string channelAccessToken)
    {
        // Allow both the control plane and the data plane (the by-file upload uses api-data.line.me).
        var provider = new StaticChannelAccessTokenProvider(
            channelAccessToken, LineHosts.Api, LineHosts.ApiData);
        var auth = new BaseBearerTokenAuthenticationProvider(provider);
        return new ManageAudienceClient(auth);
    }

    // --- Control-plane convenience (JSON, api.line.me) ---

    /// <summary>
    /// Creates an audience group for uploading user IDs and adds the initial IDs in the request body
    /// (POST /v2/bot/audienceGroup/upload). To upload IDs from a file instead, use
    /// <see cref="UploadUserIdsByFileAsync"/>.
    /// </summary>
    public Task<ApiModels.CreateAudienceGroupResponse?> CreateForUploadingUserIdsAsync(
        ApiModels.CreateAudienceGroupRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return Api.V2.Bot.AudienceGroup.Upload.PostAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Adds user IDs to an existing upload audience group via the request body
    /// (PUT /v2/bot/audienceGroup/upload). To add IDs from a file instead, use
    /// <see cref="AddUserIdsByFileAsync"/>.
    /// </summary>
    public Task AddUserIdsAsync(
        ApiModels.AddAudienceToAudienceGroupRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return Api.V2.Bot.AudienceGroup.Upload.PutAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>Gets an audience group and its jobs (GET /v2/bot/audienceGroup/{audienceGroupId}).</summary>
    public Task<ApiModels.GetAudienceDataResponse?> GetAudienceDataAsync(
        long audienceGroupId, CancellationToken cancellationToken = default)
        => Api.V2.Bot.AudienceGroup[audienceGroupId].GetAsync(cancellationToken: cancellationToken);

    /// <summary>Deletes an audience group (DELETE /v2/bot/audienceGroup/{audienceGroupId}).</summary>
    public async Task DeleteAudienceGroupAsync(
        long audienceGroupId, CancellationToken cancellationToken = default)
    {
        // The response body is empty. Dispose the Stream the generated code returns.
        using var _ = await Api.V2.Bot.AudienceGroup[audienceGroupId]
            .DeleteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // --- Data-plane convenience (multipart/form-data, api-data.line.me) ---

    /// <summary>
    /// Creates an audience group by uploading user IDs (or IFAs) from a file
    /// (POST /v2/bot/audienceGroup/upload/byFile, data plane). The file is a text file with one
    /// user ID or IFA per line; it is sent as the <c>text/plain</c> <c>file</c> part.
    /// </summary>
    /// <param name="fileContent">The text file content (one ID/IFA per line). Not disposed by this method.</param>
    /// <param name="description">Audience name (optional, max 120 chars).</param>
    /// <param name="isIfaAudience">Set true when the file contains IFAs; false/omitted for user IDs.</param>
    /// <param name="uploadDescription">Description registered for the upload job (optional).</param>
    public Task<BlobModels.CreateAudienceGroupResponse?> UploadUserIdsByFileAsync(
        Stream fileContent,
        string? description = null,
        bool? isIfaAudience = null,
        string? uploadDescription = null,
        CancellationToken cancellationToken = default)
    {
        if (fileContent is null) throw new ArgumentNullException(nameof(fileContent));

        var body = new MultipartBody { RequestAdapter = _blobAdapter };
        if (description is not null) body.AddOrReplacePart("description", "text/plain", description);
        if (isIfaAudience is not null)
            body.AddOrReplacePart("isIfaAudience", "text/plain", isIfaAudience.Value ? "true" : "false");
        if (uploadDescription is not null) body.AddOrReplacePart("uploadDescription", "text/plain", uploadDescription);
        body.AddOrReplacePart("file", "text/plain", fileContent);

        return Blob.V2.Bot.AudienceGroup.Upload.ByFile.PostAsync(body, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Adds user IDs (or IFAs) from a file to an existing upload audience group
    /// (PUT /v2/bot/audienceGroup/upload/byFile, data plane).
    /// </summary>
    /// <param name="audienceGroupId">The target audience group ID.</param>
    /// <param name="fileContent">The text file content (one ID/IFA per line). Not disposed by this method.</param>
    /// <param name="uploadDescription">Description registered for the upload job (optional).</param>
    public Task AddUserIdsByFileAsync(
        long audienceGroupId,
        Stream fileContent,
        string? uploadDescription = null,
        CancellationToken cancellationToken = default)
    {
        if (fileContent is null) throw new ArgumentNullException(nameof(fileContent));

        var body = new MultipartBody { RequestAdapter = _blobAdapter };
        body.AddOrReplacePart("audienceGroupId", "text/plain",
            audienceGroupId.ToString(CultureInfo.InvariantCulture));
        if (uploadDescription is not null) body.AddOrReplacePart("uploadDescription", "text/plain", uploadDescription);
        body.AddOrReplacePart("file", "text/plain", fileContent);

        return Blob.V2.Bot.AudienceGroup.Upload.ByFile.PutAsync(body, cancellationToken: cancellationToken);
    }
}
