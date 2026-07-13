using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Line.Core.Webhook;
using Line.Messaging.Webhook.Generated.Models;
using Microsoft.Kiota.Serialization.Json;

namespace Line.Messaging.Webhook;

/// <summary>
/// LINE Webhook の受信入口。<c>x-line-signature</c> 署名検証（<see cref="WebhookSignatureValidator"/>）と
/// 本文の <see cref="CallbackRequest"/> 逆直列化を 1 呼び出しに束ねる薄いヘルパ。
///
/// <para>
/// 逆直列化は <see cref="JsonParseNodeFactory"/> を直接インスタンス化して行い、Kiota のグローバルな
/// 既定シリアライザレジストリ（<c>ParseNodeFactoryRegistry.DefaultInstance</c>／
/// <c>ApiClientBuilder.RegisterDefaultDeserializer</c>）に一切依存しない。
/// （<c>KiotaJsonSerializer</c> は内部でこの既定レジストリを参照するため、JSON ファクトリ未登録の
/// クリーンなプロセスでは失敗する。ファクトリ直使用ならその前提を持たない。）
/// そのため Messaging クライアント等を構築していないアプリでも単独で動作し、副作用も無い
/// （回帰は独立アセンブリ <c>Line.Messaging.Webhook.IsolationTests</c> で保証）。
/// </para>
/// <para>
/// イベント配列の多態復元（<c>type</c> discriminator による <see cref="MessageEvent"/> 等への振り分け、
/// 未知 type の基底 <see cref="Event"/> フォールバック）は生成コードが担う。本ヘルパは
/// <see cref="CallbackRequest"/> を返すのみで、以降のイベント分岐は利用側で行う（README 参照）。
/// </para>
///
/// ASP.NET Core での利用例（生ボディと署名ヘッダの取得は利用側の責務）:
/// <code>
///   var body = await ReadRawBodyBytesAsync(Request);          // 生バイト（署名対象と同一）
///   var sig  = Request.Headers["x-line-signature"];
///   CallbackRequest callback = await parser.ParseAsync(body, sig);  // 署名 NG は例外
/// </code>
/// </summary>
public sealed class WebhookRequestParser
{
    private readonly string _channelSecret;

    /// <param name="channelSecret">チャネルシークレット（署名検証の鍵）。</param>
    /// <exception cref="ArgumentException"><paramref name="channelSecret"/> が空または空白のみ。</exception>
    public WebhookRequestParser(string channelSecret)
    {
        // DI 側 Validate（IsNullOrWhiteSpace）と判定を揃える（空白のみも拒否）。
        if (string.IsNullOrWhiteSpace(channelSecret))
            throw new ArgumentException("channel secret is required", nameof(channelSecret));
        _channelSecret = channelSecret;
    }

    /// <summary>
    /// 構築時のチャネルシークレットで署名検証し、本文を <see cref="CallbackRequest"/> へ逆直列化する。
    /// </summary>
    /// <param name="body">リクエスト生ボディ（署名検証と逆直列化はこの同一バイト列に対して行う）。</param>
    /// <param name="signatureHeader"><c>x-line-signature</c> ヘッダ値。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> が null。</exception>
    /// <exception cref="WebhookSignatureException">署名検証に失敗した。</exception>
    /// <exception cref="WebhookPayloadException">署名は正当だが本文を逆直列化できなかった。</exception>
    public Task<CallbackRequest> ParseAsync(
        byte[] body, string? signatureHeader, CancellationToken cancellationToken = default)
        => ParseAsync(_channelSecret, body, signatureHeader, cancellationToken);

    /// <summary>
    /// チャネルシークレットを都度指定して署名検証・逆直列化する（マルチテナント向け）。
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> が null。</exception>
    /// <exception cref="ArgumentException"><paramref name="channelSecret"/> が空または空白のみ。</exception>
    /// <exception cref="WebhookSignatureException">署名検証に失敗した。</exception>
    /// <exception cref="WebhookPayloadException">署名は正当だが本文を逆直列化できなかった。</exception>
    public static async Task<CallbackRequest> ParseAsync(
        string channelSecret, byte[] body, string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        if (body is null) throw new ArgumentNullException(nameof(body));

        // 署名検証は生バイト列に対して同期実行する（WebhookSignatureValidator が secret 空を弾く）。
        if (!WebhookSignatureValidator.IsValid(channelSecret, body, signatureHeader))
            throw new WebhookSignatureException("x-line-signature verification failed.");

        CallbackRequest? callback;
        try
        {
            // 既定レジストリを介さず、Json ファクトリを直接生成して同一バイト列を逆直列化する。
            // これによりデシリアライザのグローバル登録に依存しない（クリーンなプロセスでも動く）。
            using var stream = new MemoryStream(body, writable: false);
            var rootNode = await new JsonParseNodeFactory()
                .GetRootParseNodeAsync("application/json", stream, cancellationToken)
                .ConfigureAwait(false);
            // 多態復元（events の type discriminator による派生型選択）は生成コードが担う。
            callback = rootNode.GetObjectValue(CallbackRequest.CreateFromDiscriminatorValue);
        }
        // キャンセルは呼び出し側へそのまま伝播させる（PayloadException に包まない）。
        catch (Exception ex) when (ex is not WebhookException && ex is not OperationCanceledException)
        {
            throw new WebhookPayloadException("Failed to deserialize webhook payload.", ex);
        }

        // 防御的ガード（CallbackRequest.CreateFromDiscriminatorValue は常に実体を返すため
        // 通常入力では到達しないが、将来の生成変更や空ストリーム等に備える）。
        if (callback is null)
            throw new WebhookPayloadException("Webhook payload deserialized to null.");

        return callback;
    }
}
