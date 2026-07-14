# spec ゲートレビュー: Line.OpenApi.Tools（CLI / MCP ツール仕様）

- **日付:** 2026-07-14
- **対象:** `docs/CLI-MCP-tool-spec.md`（実装前の仕様）
- **ゲート:** spec（`spec-reviewer` サブエージェント）
- **判定:** **CONCERNS**（BLOCK 事由なし＝実装フェーズ進行可。Medium 2 件を spec 反映のうえ着手）
- **人の go/no-go:** 未（本記録時点）

## スコープ

既存ライブラリ `Line.OpenApi.*`（Messaging / Messaging.Webhook / ChannelAccessToken / Liff）の上に、ローカル PC 用 `dotnet tool`（`line`）を構築。同一ロジックを CLI（Cocona）と MCP サーバ（公式 ModelContextProtocol）で両出しする構成。機能 A. トークン管理 / B. メッセージ送信・Bot 照会 / C. Webhook 開発支援 / D. LIFF 管理。生成コード本体（`src/**/Generated/`）は対象外。

## 所見サマリ

### PASS 相当
- **API 実在性:** spec が前提とする全エンドポイント・全公開メンバが実在（`tests/.../PublicApi/*.approved.txt`・`openapi/*.yml` と突合）。存在しない API への依存なし。
- **既知の実仕様の踏襲:** R1（複数 base URL / `getMessageContent` は `api-data.line.me`）は `MessagingClient.Blob` ファサード経由で解決済み＝CLI 側で再実装不要。form-urlencoded / webhook 自己完結逆直列化も踏襲前提。
- **破壊系識別・シークレットポリシー:** 送信/削除/revoke/replay を破壊的と明示、`--read-only` 切替、`token issue` の非露出（C 既定＋`--allow-secret-output`）は MCP 戻り値がモデル文脈に載る前提を踏まえ妥当。

### Medium（実装スコープに反映すべき → 本改訂で反映済み）
1. **トークン領域に DI ヘルパ／ファサードが無い。** `AddLineChannelAccessToken` は存在せず、verify/revoke を出すファサードも無い。issue/verify/revoke は CLI の `TokenService` が生成 `ChannelAccessTokenClient(IRequestAdapter)` を自前配線して使う必要。→ spec §2・§4.1 に明記。
2. **JWT アサーション署名器はライブラリ未提供。** `JwtAssertionTokenSource`/`StatelessJwtAssertionTokenSource` は `assertionFactory`（署名済み JWT を返す委譲）を呼び出し側から受け取る設計。`--private-key`/`--kid`/`--channel-id` から表明 JWT（RS256）を生成する署名コンポーネントを CLI が新規実装する必要。→ spec §4.1 に実装責務として明記。

### Low（本改訂で反映済み）
- B 系命名不整合（`push` トップレベル vs 他グループ化）→ `message`/`bot` グループへ統一、MCP 名は `line_<area>_<verb>` に統一（CLI トップレベル別名は MCP へ持ち込まない）。§4.2・§4.5。
- `webhook replay --to <url>` の宛先は AllowedHostsValidator 対象外＝専用の素の HttpClient を使う旨を明記（ローカルツール前提で SSRF 懸念は許容）。§4.3。
- v2.0 チャネルシークレット短命トークン（`issueChannelToken`）・鍵 ID 列挙（`getsAllValidChannelAccessTokenKeyIds`）はスコープ外→将来候補として保持。§4.1。

## 結論

生成／既存 API 面での致命的破綻なし。トークン領域の実装責務（生成クライアント手配線・JWT 署名器自作）は実装可能で設計崩壊ではないため BLOCK ではないが、着手前に spec 明記・工数見積り反映が必要 → 本改訂で反映済み。**実装フェーズ進行可。** 実装後は code / security / test-arch の 3 役ゲートへ（security 重視）。
