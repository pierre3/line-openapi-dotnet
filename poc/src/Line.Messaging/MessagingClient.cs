using System;
using System.Net.Http;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Line.Core.Authentication;
using Line.Messaging.Generated.Api;   // 制御系 (api.line.me)   ※ generate 後に生成される
using Line.Messaging.Generated.Blob;  // データ系 (api-data.line.me) ※ generate 後に生成される

namespace Line.Messaging;

/// <summary>
/// Messaging API のファサード。制御系(api.line.me)とデータ系(api-data.line.me)の
/// 2 つの Kiota クライアントを統合し、利用者にホストの違いを意識させない。
///
/// 使い方（生成後のイメージ）:
///   var line = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
///   await line.Api.V2.Bot.Message.Push.PostAsync(pushRequest);         // 送信（制御系）
///   var stream = await line.Blob.V2.Bot.Message[messageId].Content.GetAsync(); // 取得（データ系）
/// ※ 上記のビルダーパスは Kiota 生成結果に依存するため、生成後に実パスへ調整すること。
/// </summary>
public sealed class MessagingClient
{
    /// <summary>制御系クライアント（送信・応答・リッチメニュー操作など、api.line.me）。</summary>
    public MessagingApiClient Api { get; }

    /// <summary>データ系クライアント（コンテンツ取得・画像アップロードなど、api-data.line.me）。</summary>
    public MessagingBlobApiClient Blob { get; }

    /// <param name="authProvider">認証プロバイダ（静的トークン / 更新型のいずれでも可）。</param>
    /// <param name="httpClient">
    /// 制御系・データ系の 2 アダプタで共有する <see cref="HttpClient"/>。
    /// DI 経由で <c>IHttpClientFactory</c> が供給する（ハンドラプール共有・Kiota 既定ミドルウェア適用）。
    /// null の場合は各アダプタが既定 <see cref="HttpClient"/> を内部生成する（PoC/簡易用）。
    /// アダプタが URL を組み立てるため、<see cref="HttpClient.BaseAddress"/> は使用されず共有で問題ない。
    /// </param>
    public MessagingClient(IAuthenticationProvider authProvider, HttpClient? httpClient = null)
    {
        if (authProvider is null) throw new ArgumentNullException(nameof(authProvider));

        // 制御系: 仕様の server(api.line.me) をそのまま使う。
        var apiAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        Api = new MessagingApiClient(apiAdapter);

        // データ系: 別アダプタを用意し、BaseUrl を api-data.line.me へ明示設定する。
        // （分離生成しても root server は api.line.me のままのため、この上書きが必須。）
        // 重要: 生成クライアントはコンストラクタで baseurl を PathParameters へ確定させる
        // （空なら api.line.me を既定採用）。よって BaseUrl は必ず「構築前」に設定すること。
        // 構築後に設定しても PathParameters には反映されず、リクエストは api.line.me に飛ぶ。
        var blobAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        blobAdapter.BaseUrl = $"https://{LineHosts.ApiData}";
        Blob = new MessagingBlobApiClient(blobAdapter);
    }

    /// <summary>長期チャネルアクセストークンから手早く生成するヘルパ（PoC 用）。</summary>
    public static MessagingClient CreateWithStaticToken(string channelAccessToken)
    {
        var provider = new StaticChannelAccessTokenProvider(channelAccessToken);
        var auth = new BaseBearerTokenAuthenticationProvider(provider);
        return new MessagingClient(auth);
    }
}
