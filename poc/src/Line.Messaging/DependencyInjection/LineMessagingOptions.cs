using System;

namespace Line.Messaging.DependencyInjection;

/// <summary>
/// <see cref="ServiceCollectionExtensions.AddLineMessaging(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{LineMessagingOptions})"/>
/// の設定。静的（長期）チャネルアクセストークンでの構築に使う。
/// </summary>
public sealed class LineMessagingOptions
{
    /// <summary>長期チャネルアクセストークン。</summary>
    public string ChannelAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// トークンを付与してよいホスト。未指定なら既定（api.line.me / api-data.line.me）。
    /// 将来のホスト追加（例: manager.line.biz）に備えて注入可能にしている。
    /// </summary>
    public string[]? AllowedHosts { get; set; }
}
