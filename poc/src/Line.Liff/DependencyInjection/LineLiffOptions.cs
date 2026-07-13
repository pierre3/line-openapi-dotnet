using System;

namespace Line.Liff.DependencyInjection;

/// <summary>
/// <see cref="ServiceCollectionExtensions.AddLineLiff(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{LineLiffOptions})"/>
/// の設定。静的（長期）チャネルアクセストークンでの構築に使う。
/// </summary>
public sealed class LineLiffOptions
{
    /// <summary>長期チャネルアクセストークン。</summary>
    public string ChannelAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// トークンを付与してよいホスト。未指定なら既定（api.line.me）。
    /// LIFF は data 系ホストを使わないため既定は制御系のみ。
    /// </summary>
    public string[]? AllowedHosts { get; set; }
}
