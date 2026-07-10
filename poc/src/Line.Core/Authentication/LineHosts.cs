namespace Line.Core.Authentication;

/// <summary>
/// LINE API のホスト。許可ホストは将来の追加（例: manager.line.biz）に備え、
/// ハードコードせずここに集約し、プロバイダ生成時に注入・拡張できるようにする。
/// </summary>
public static class LineHosts
{
    public const string Api = "api.line.me";
    public const string ApiData = "api-data.line.me";

    /// <summary>Bot/Messaging の既定許可ホスト（制御系 + データ系）。</summary>
    public static readonly string[] Default = { Api, ApiData };
}
