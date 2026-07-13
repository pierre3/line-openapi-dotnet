using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Line.Core.Authentication;
using Line.Liff.Generated;
using Line.Liff.Generated.Models;

namespace Line.Liff;

/// <summary>
/// LIFF 管理 API のファサード。単一ホスト(api.line.me)の Kiota クライアントを内包し、
/// 優先利用シーン「LIFF アプリの一覧取得・追加・更新・削除」を薄い便利メソッドで提供する。
///
/// Messaging と異なり data 系ホストは無いため、ホスト上書き（BaseUrl）は不要で
/// 生成既定の api.line.me をそのまま使う。より低レベルな操作が必要なら <see cref="Api"/>
/// から生成ビルダーへ直接アクセスできる。
///
/// 設計方針（Messaging との非対称の意図）: LIFF は 2 パス・4 操作の閉じた小さな表面のため、
/// 便利メソッドで完全被覆できる。エンドポイント多数の Messaging は完全被覆が非現実的で
/// 生成ビルダー直公開に留める。この差は一貫性の欠如ではなく表面規模に応じた判断。
///
/// 使い方:
///   var liff = LiffClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
///   var apps = await liff.GetAppsAsync();
///   var added = await liff.AddAppAsync(new AddLiffAppRequest { /* ... */ });
///   await liff.UpdateAppAsync(added!.LiffId!, new UpdateLiffAppRequest { /* ... */ });
///   await liff.DeleteAppAsync(added.LiffId!);
/// </summary>
public sealed class LiffClient
{
    /// <summary>生成クライアント（低レベル操作用に公開）。</summary>
    public LiffApiClient Api { get; }

    /// <param name="authProvider">認証プロバイダ（静的トークン / 更新型のいずれでも可）。</param>
    /// <param name="httpClient">
    /// アダプタが共有する <see cref="HttpClient"/>。DI 経由で <c>IHttpClientFactory</c> が供給する
    /// （ハンドラプール共有・Kiota 既定ミドルウェア適用）。null の場合はアダプタが既定
    /// <see cref="HttpClient"/> を内部生成する（簡易用）。
    /// </param>
    public LiffClient(IAuthenticationProvider authProvider, HttpClient? httpClient = null)
    {
        if (authProvider is null) throw new ArgumentNullException(nameof(authProvider));

        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        Api = new LiffApiClient(adapter);
    }

    /// <summary>長期チャネルアクセストークンから手早く生成するヘルパ。</summary>
    public static LiffClient CreateWithStaticToken(string channelAccessToken)
    {
        var provider = new StaticChannelAccessTokenProvider(channelAccessToken, LineHosts.Api);
        var auth = new BaseBearerTokenAuthenticationProvider(provider);
        return new LiffClient(auth);
    }

    /// <summary>チャネルに追加済みの全 LIFF アプリを取得する（GET /liff/v1/apps）。</summary>
    public Task<GetAllLiffAppsResponse?> GetAppsAsync(CancellationToken cancellationToken = default)
        => Api.Liff.V1.Apps.GetAsync(cancellationToken: cancellationToken);

    /// <summary>LIFF アプリをチャネルへ追加する（POST /liff/v1/apps）。発行された LIFF ID を含む応答を返す。</summary>
    public Task<AddLiffAppResponse?> AddAppAsync(
        AddLiffAppRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return Api.Liff.V1.Apps.PostAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>既存 LIFF アプリの設定を更新する（PUT /liff/v1/apps/{liffId}）。</summary>
    public async Task UpdateAppAsync(
        string liffId, UpdateLiffAppRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(liffId)) throw new ArgumentException("liffId is required", nameof(liffId));
        if (request is null) throw new ArgumentNullException(nameof(request));

        // 応答ボディは空。生成側が返す Stream は破棄する。
        using var _ = await Api.Liff.V1.Apps[liffId]
            .PutAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>LIFF アプリをチャネルから削除する（DELETE /liff/v1/apps/{liffId}）。</summary>
    public async Task DeleteAppAsync(string liffId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(liffId)) throw new ArgumentException("liffId is required", nameof(liffId));

        using var _ = await Api.Liff.V1.Apps[liffId]
            .DeleteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
