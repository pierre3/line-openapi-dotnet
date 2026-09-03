# G-gate review — LINE Flex Message live preview (`line_flex_*` MCP + canvas extension)

- **日付:** 2026-09-03
- **ブランチ:** `feat-flex-preview`
- **対象:** `dotnet-flex-preview-mcp/AGENT_TASK.md` の取り込み。Deliverable A（Copilot canvas 拡張 `extensions/line-flex-viewer/`）＋ Deliverable B（`line` CLI/MCP ツールへの `line_flex_*` 追加＝ループバック `HttpListener`+SSE プレビュー `FlexPreviewService`）。
- **レビュー対象ファイル:** `tools/Line.OpenApi.Tools/Services/FlexPreviewService.cs`・`Mcp/FlexPreviewTools.cs`・`Hosting/{ServiceRegistration,McpServerHost}.cs`・`Line.OpenApi.Tools.csproj`・`tests/Line.OpenApi.Tools.Tests/McpToolRegistrationTests.cs`・`extensions/line-flex-viewer/`（配線のみ）。
- **制約:** bundle 由来の web レンダラ・canvas 拡張・`FlexPreviewService.cs` は AGENT_TASK.md で **verbatim（byte-for-byte）コピー**契約。

## 検証結果（実施済み）

- `dotnet build tools/Line.OpenApi.Tools` = 0 warning / 0 error。
- 埋め込みリソース = `Line.OpenApi.Tools.web.{viewer.html,viewer.js,renderer.js,flex.css,samples.js}` 5本を manifest で確認。
- MCP `tools/list` = `line_flex_preview`/`get_content`/`validate`/`open` の4本が既定（51本）でも `--read-only`（29本・変更系は消える）でも広告。
- 機能スモーク = `line_flex_preview`（最小 bubble）→ `{ok:true, valid:true, warnings:[], opened:false}`（`LINE_FLEX_MCP_NO_OPEN=1`）。ループバック URL が `/`（viewer）・`/renderer.js`・`/flex.css`（200）・`/api/state`（内容ラウンドトリップ）を配信。
- 全テスト緑（264 lib + 83 Tools + 26 AI + 1 Isolation）。`pack-verify` = 12パッケージ契約維持（Tools は `ExcludeToolFromPack` で除外）。

## 3役ゲート結論

| 役 | 判定 | BLOCKING |
| --- | --- | --- |
| code-reviewer | CONCERNS | なし（High なし・Medium 複数） |
| security-reviewer | PASS | なし（非ブロッキング CONCERNS のみ） |
| test-arch-reviewer | CONCERNS | なし |

### security（PASS）

- 秘密情報・LINE API・トークンの読取/保存/ログ/返却は皆無 → `--read-only` 公開は妥当。timing 比較・R1(BaseUrl) は該当なし。
- ループバック束縛 `http://127.0.0.1:{port}/` 固定（ワイルドカードなし）。静的配信は厳格ホワイトリスト（`Array.IndexOf`）＋埋め込みリソースの suffix 解決のみ＝パストラバーサル不可。state 書込先は固定 `content.json`（リクエストで経路操作不可）。ブラウザ起動 URL は内部生成（int port）＝コマンド/引数インジェクションなし。
- 非ブロッキング指摘:
  - **[Medium] `/api/state` POST が未認証・Origin/Host 未検証** → 他ローカルページからの CSRF 書換で preview 内容差し替えの可能性（`line_flex_get_content` 経由で LLM 送信フローが攻撃者 JSON を拾う恐れ。送信自体は別 write ツール＋ユーザー操作が必要で影響限定）。
  - **[Low] Host 未検証で DNS リバインドによる下書き読取**／**[Low] POST ボディ無制限（ループバック DoS）**。
  - 推奨: `/api/*` に `Host` ヘッダ（POST は `Origin`）検証を足すと CSRF-write と rebind-read を同時に塞げる。
  - Informational: LLM/CSRF 由来の Flex JSON を `renderer.js` が描画 → renderer のサニタイズは別レビュー（本 PR のスコープ外・verbatim）。

### code（CONCERNS・High なし）

- Positive: ループバック限定・ホワイトリスト・DI singleton・埋め込みリソース配線は既存規約に忠実。read-only 分類は正しくテストで両面ガード。
- Medium:
  1. **`Open()` が閉じたタブを再オープンできない**（`_opened` が初回で永続 latch → `line_flex_open` の「(re)open」説明とデッドコード矛盾。Node 参照 `server.mjs` は常に open）。
  2. **SSE ライフサイクル**: keepalive ping なし・切断時クリーンアップなし・`Dispose` が開いた SSE レスポンスを閉じない（Node 参照にはある）。
  3. **サービス層テスト不在**（`Validate`/`Normalize` は純粋関数で容易。`Open()` バグはテストがあれば捕捉できた）。
  4. **リポジトリ衛生**: 未追跡の `dotnet-flex-preview-mcp/`（`AGENT_TASK.md`・`_verify/bin,obj` 含む）が commit に紛れうる → gitignore/除外。
- Low: `validate` が不正 JSON で throw（`{valid:false,...}` を返さない）／`Normalize` が array を弾くのにメッセージは "JSON object"／`RenderIndex` の marker 依存が無言 no-op 化しうる／port TOCTOU（ループバックで実害軽微）。

### test-arch（CONCERNS・非ブロッキング）

- read-only 安全性（変更系が `--read-only` で出ない）は良く守られており、依存・pack 契約・read-only 登録は健全。
- ギャップ: `FlexPreviewService` の純粋な検証/永続ロジックに直接の単体テストが無い（`internal`＋`InternalsVisibleTo` で到達可能・HTTP/ブラウザ不要でテスト可）。推奨（高→低）: `Validate`/`ValidateInput`（bubble/carousel 1..12 境界/非 bubble/flex ラッパ/null）・`Normalize` ガード・state ラウンドトリップ・（任意）ループバック route スモーク。
- web アセット重複（`tools/.../web` と `extensions/.../web` が byte 同一）は DRY ハザード。task が verbatim を要求するためブロッキングではないが、**両 `web/` の共有サブセットが byte 同一であることを検査する CI/テストガード**を推奨（単一ソース化より低コストで退行検知）。
- 第3のツール型（`FlexPreviewTools`）として登録するのは妥当（LINE の read でも write でもない）。`WithTools<FlexPreviewTools>()` 付近に「Read/Write 二分の外に置く理由」を1行コメント推奨。

## 人の go/no-go → 指摘反映（人の判断で修正実施）

3役とも BLOCKING なし。人の go/no-go で「verbatim 契約下の `FlexPreviewService.cs` も修正する／非 verbatim 改善も全て PR 前に実施」を選択。以下を反映済み。

**`FlexPreviewService.cs`（.NET 専用ファイル＝4面共有の web レンダラとは別。bundle 側コピーも同期しドリフト回避）:**

- **[code Medium] `Open()` 再オープン不可を修正** — 明示ユーザー操作として常にブラウザを開く（`_autoOpen`＝`LINE_FLEX_MCP_NO_OPEN` は尊重）。once-only latch は `Preview` 専用に限定。
- **[security Medium/Low] `/api/*` に Host/Origin 検証を追加** — Host ヘッダがバインド先ループバック権限（`127.0.0.1:<port>`／`localhost` 別名）でなければ 403（DNS リバインド読取を遮断）。POST は Origin があり同一オリジンでなければ 403（CSRF 書換を遮断）。`GET /`・静的アセットは非対象（機密なし）。
- **[code Low] `line_flex_validate` を非 throw 化** — 不正 JSON/スカラーを `{valid:false, warnings:[<msg>]}` で返す（ツール契約順守）。
- **[code Low] `Normalize` メッセージ修正**（"JSON object or array"）。
- **[code Medium 一部] `Dispose` が開いた SSE レスポンスを閉じる**ように。

**非 verbatim（テスト・CI ガード・衛生）:**

- **[test-arch HIGH] サービス層単体テスト追加** `tests/.../FlexPreviewServiceTests.cs`（13 件）= `ValidateInput`/`Validate`/`Normalize`（bubble/flex ラッパ/carousel 1・12・0・13/非 bubble/任意オブジェクト/不正 JSON/スカラー/null）＋ state ラウンドトリップ。
- **[test-arch] 両 `web/` byte 同一 CI ガード** `tests/.../FlexWebAssetsParityTests.cs`（共有5ファイルの SHA-256 一致を検査）。
- **[code Medium] staging バンドル gitignore** — `.gitignore` に `dotnet-flex-preview-mcp/` を追加（`AGENT_TASK.md`・`_verify/bin,obj` 等が commit に紛れない）。

**修正後の再検証:**

- ビルド 0 warning / 0 error。全テスト緑（264 lib + **97** Tools + 26 AI + 1 Isolation）。
- Host/Origin ガード e2e 確認: 正常 GET `/api/state`=200／Host 詐称=403／同一オリジン POST=200／クロスオリジン POST=403／`/`・`/renderer.js`=200。

**残（follow-up・非ブロッキング）:**

- SSE keepalive ping（Node 参照 `server.mjs` にはある定期ハートビート＝切断検知兼用）。今回は `Dispose` クリーンアップのみ実施。
- `RenderIndex` の marker 未検出時の無言 no-op（drift 検知の throw/log）／port 選択 TOCTOU（ループバックで実害軽微）。
- `/api/*` の per-instance トークン化・POST ボディサイズ上限（GA 前の追加ハードニング候補）。
- renderer.js の XSS サニタイズは renderer 自体のレビュー範囲（本 PR は verbatim・スコープ外）。
- web レンダラの単一ソース化（現状は byte 同一ガードで drift を検知）。

**総合:** 3役 BLOCKING なし＋指摘反映済み → **GO（実装完了・PR 可）。**
