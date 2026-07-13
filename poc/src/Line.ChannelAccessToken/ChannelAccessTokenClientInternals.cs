using System.Collections.Generic;
using Microsoft.Kiota.Abstractions;

namespace Line.ChannelAccessToken.Generated;

// 生成クライアント（BaseRequestBuilder 派生）の protected な RequestAdapter / PathParameters を、
// 同一クラスの partial から assembly 内部にだけ公開する手書きグルー。
//
// StatelessJwtAssertionTokenSource が /oauth2/v3/token の oneOf 合成ボディ（入れ子直列化で
// form-urlencoded 非対応）を回避し、平坦な要求モデルを自前で組んで低レベル送出するために必要。
// internal なので公開 API 表面（PublicApi snapshot）には現れない。ファイルは Generated/ の外に
// 置くため再生成で上書きされない。
public partial class ChannelAccessTokenClient
{
    /// <summary>基盤の <see cref="IRequestAdapter"/>（baseurl 既定・シリアライザ登録済み）。</summary>
    internal IRequestAdapter InternalRequestAdapter => RequestAdapter;

    /// <summary>
    /// baseurl を含むパスパラメータの<b>防御的コピー</b>。
    /// クライアントの生の <c>PathParameters</c> を露出すると assembly 内の別コードが baseurl を
    /// 書き換えて送出先を変え得るため、都度コピーを返して不変性を守る（コスト微小）。
    /// </summary>
    internal Dictionary<string, object> InternalPathParameters => new(PathParameters);
}
