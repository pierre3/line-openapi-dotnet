# 2026-09-04 Flex プレビュー拡張 ローカルメディア配信 parity レビュー記録

## 概要

2026-09-04 に .NET 側（`Line.OpenApi.Tools` の `FlexPreviewService.cs`）へ追加したローカルメディア配信（環境変数 `LINE_FLEX_MCP_ASSET_DIR` 配下の画像/動画を相対 `url` で配信）を、**Node/JS 側の 2 サーバへ移植して機能 parity を取った**。

- **Copilot キャンバス拡張** `extensions/line-flex-viewer/extension.mjs`
- **同梱 Node MCP サーバ** `extensions/line-flex-viewer/mcp/server.mjs`

両サーバとも、これまで GET は「静的ファイル whitelist ＋ `/api/*`」しか処理せず相対 `url` のメディアは 404 になっていた（.NET MCP 経由のみ配信可＝非対称）。本変更でこの非対称を解消。

**利用シーン:** 用意したメディアをフォルダに配置 → Flex JSON からは相対 `url`（例 `"assets/hero.png"`）で参照 → 本番移行時は origin だけ HTTPS の CDN に差し替える（相対パス部分は不変）。プレビュー専用の利便機能（LINE 本体はローカル/`data:` URL を描画しない）。

## 設計判断（ADR）

- **封じ込めロジックを共有モジュール `lib/assets.mjs` に集約**し、2 サーバが同一の `resolveMediaRequest` を通す。セキュリティ上重要な封じ込めの二重持ちを避ける（リポジトリの `SpecNormalization.ps1` 二重持ちバグの教訓に倣う）。
- `resolveMediaRequest({ path, host, boundPort, assetDir })` を「実サーバが呼ぶ実コードパス」とし、opt-in 判定 → `isLoopbackHost` ホストガード → `resolveAssetPath` 封じ込めの順（FS 接触前にホスト検証）。
- 純粋関数（`resolveAssetPath`/`assetContentType`/`isLoopbackHost`/`resolveAssetDir`）は .NET の `ResolveAssetPath`/`AssetContentType`/`IsLoopbackHost` を移植し挙動を一致。
- `renderer.js` ほか `web/` は無改修（ブラウザが相対 URL をページ origin に解決）。
- テストは Node 内蔵 `node:test`（依存ゼロ・zero-dependency 方針維持）。

## 封じ込め（多層防御・.NET と parity）

1. `isLoopbackHost(host, boundPort)` ガード（DNS リバインド読み取り対策・両サーバの配線で実バインドポートを使用）
2. 拡張子 allowlist（`.png`/`.jpg`/`.jpeg`/`.mp4`）＋ content-type も `image/*`・`video/mp4`・`application/octet-stream` に限定
3. 制御文字（C0＋DEL）拒否＝`%00` トリック無効
4. `path.resolve` 正規化 → `fullBase + sep` の前置比較（`../`・`..\`・`%2e%2e`・`..%2f`・`%5c`・rooted/UNC をすべて捕捉。末尾セパレータ付与で兄弟プレフィックス誤許可も防止）
5. シンボリックリンク物理封じ込め（`lstatSync` で最終要素が symlink のとき `realpathSync` でベース配下を確認・非リンクは as-is）

## テスト

- 新規 `extensions/line-flex-viewer/lib/assets.test.mjs`（**42 tests 全緑**）。.NET の `FlexPreviewAssetServingTests` の**完全な上位集合**（純粋封じ込め・トラバーサル各種エンコード・rooted/UNC・拡張子 allowlist・大文字拡張子・symlink 越え・content-type・ループバック e2e・未設定時非配信）。
- JS で追加した価値あるケース: `resolveAssetDir` 正規化、制御文字拒否、`isLoopbackHost` の accept/reject（文字列 `host:port` パースの独立検証）、`resolveMediaRequest` 直接テスト、**DNS リバインド e2e**（外部 Host → 404）。
- クリーンアップは単一 async `after()` + `fs.rm` に集約（同期 `rmSync` 再帰が CJK パスの Windows/Node でハードクラッシュする環境バグ回避。CI の ASCII パスでは元々非該当）。
- `mcp/package.json` に `npm test`（`node --test ../lib/assets.test.mjs`）を追加。
- **実 `mcp/server.mjs` を stdio で起動した手動 e2e スモーク**で実配信を確認（画像 200/バイト一致・`.gif`→404・トラバーサル→404・外部 Host→404）。

## 3 役ゲート結果（すべて PASS・BLOCKING なし）

- **security-reviewer = PASS**：単段デコード・ベース配下判定・rooted/UNC/制御文字・ホストガード順序・opt-in・情報漏洩いずれも .NET と等価と実証。指摘は非ブロッキング（Medium=既存 follow-up の `/api/*` 未ガード横展開、Low=中間ディレクトリ symlink 共有限界／`nosniff` 未設定／リソース枯渇既知）。
- **code-reviewer = PASS**：content-type/allowlist/404/ホストガードの parity 厳密・両サーバ配線一貫・共有 import 整合・未使用 import なし・英語コメント規約遵守。指摘は Low/Info のみ（C1 制御文字未拒否／不正 `%` の扱い差＝JS 側が厳格＝安全側／中間 symlink 共有仕様／`lib/` 同梱前提）。
- **test-arch-reviewer = PASS（非ブロッキング CONCERNS）**：カバレッジは .NET の上位集合で穴なし。集約アーキ妥当。CONCERNS=e2e が実サーバでなく共有 `resolveMediaRequest` の最小 harness（フェイルクローズ・拡張子非重複・手動スモーク済みで緩和）。

## 未対応（非ブロッキング follow-up）

- **JS テストの CI 未組込み**（test-arch 推奨・最有力）: `ci.yml` に `node --test extensions/line-flex-viewer/lib/assets.test.mjs` の軽量ジョブ追加（`actions/setup-node` のみ・依存ゼロ）。
- **`/api/*`・静的配信へのホストガード横展開**（security Medium・既存 follow-up と同一）。または per-instance トークン化。
- **実サーバ配線の e2e 化**（test-arch 提案）: `handleRequest`/`startServer`/`sendFile` を SDK 非依存の `lib/preview-http.mjs` へ切り出し、raw ソケット harness で実経路を自動検証。
- C1 制御文字の拒否・`X-Content-Type-Options: nosniff`（.NET も未対応＝両実装同時対応が望ましい）。
- 中間ディレクトリ symlink の無条件 realpath 化（.NET と共有の限界）。
- ファイルサイズ上限なし（既知・プレビュー用途で実害限定）。

## 人の go/no-go

未（本記録は 3 役ゲート完了時点）。BLOCKING なしのため GO 推奨。follow-up は次サイクル可。
