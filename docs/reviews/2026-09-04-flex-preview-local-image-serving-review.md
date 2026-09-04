# 2026-09-04 Flex プレビュー ローカル画像配信 レビュー記録

## 概要

`Line.OpenApi.Tools` の Flex プレビューサーバ（ループバック限定 `HttpListener`、`Services/FlexPreviewService.cs`）に、環境変数 `LINE_FLEX_MCP_IMAGE_DIR` で指定したフォルダ内の画像ファイル（`.png`/`.jpg`/`.jpeg`/`.gif`/`.webp`）を配信する機能を追加。

**利用シーン:** 用意した画像をフォルダに配置 → Flex JSON からは相対 `url`（例 `"assets/hero.png"`）で参照 → 本番移行時は origin だけ HTTPS の CDN/ホストに差し替える（相対パス部分は不変＝1:1 マッピング）。あくまでプレビュー用の利便機能（LINE 本体はローカル URL も `data:` URL もレンダリングしない）。

## 設計判断（ADR）

- **配信ディレクトリは環境変数（人が out-of-band 設定・LLM 非制御）** を採用。runtime MCP tool 方式（LLM が任意ローカルパスを配信面にできる＝プレビュー経路経由の任意ファイル読み取り）と default folder 方式（暗黙的で危険）を退けた。プロジェクトの「安全ゲートはクロージャ束縛で LLM に出さない」方針と一致。
- 未設定なら完全無効（opt-in、404 フォールスルー）。
- パス解決を純粋関数 `ResolveImagePath` に切り出し、HTTP を介さず封じ込めを単体テスト可能に。
- `renderer.js` は無改修（ブラウザが相対 URL をページ origin に解決）＝ `FlexWebAssetsParityTests`（`tools/web` ⇄ `extensions/web` byte 一致）に無影響。

## 封じ込め（多層防御）

1. `IsLoopbackHost(req.UserHostName)` ガード（`/api` と同軸の DNS リバインド対策）
2. 拡張子ホワイトリスト（画像種別のみ）＋ content-type も `image/*` か `application/octet-stream` に限定（XSS 誘発の text/html 等を返せない）
3. 制御文字（`char.IsControl`）拒否＝`%00` トリック無効
4. `Path.GetFullPath` による字句正規化 → `fullBase + DirectorySeparatorChar` の Ordinal 前置比較（`../`・`..\`・`%2e%2e`・`..%2f`・`%5c`・rooted/UNC/ドライブレターをすべて捕捉。末尾セパレータ付与で兄弟プレフィックス誤許可も防止）
5. **シンボリックリンク物理封じ込め**（レビュー反映）：`File.ResolveLinkTarget(returnFinalTarget:true)` で最終ターゲットを解決し、ベース配下でなければ拒否。検証不能なら refuse。

## 3 役ゲート結果（すべて PASS・BLOCKING なし）

- **code-reviewer = PASS**（Low 4：e2e decode 経路未検証／シンボリックリンク／相対 env の CWD 解決／サイズ上限なし）
- **security-reviewer = PASS**（Low：シンボリックリンク／8.3 短縮名／サイズ上限なし。封じ込め中核は堅牢と実証。タイミング攻撃・トークン漏洩・ホスト誤送出・DNS リバインド/CSRF いずれも該当なし or 十分ガード）
- **test-arch-reviewer = PASS**（中：シンボリックリンク未カバー／e2e トラバーサルがクライアント正規化で退化。低：content-type 網羅／rooted・backslash／大文字拡張子）

## 指摘への対応

- **シンボリックリンク（3 役収束・中）:** `File.ResolveLinkTarget` で物理封じ込めを追加。テスト追加（作成不可環境では skip）。
- **e2e トラバーサル退化（code L1 / test-arch T1・中）:** クライアント `Uri`/`HttpClient` が dot-segment を送信前正規化するため、**raw ソケットで非正規化パス `%2e%2e%2f` を送る e2e** に置換（http.sys のコネクションリセットも「非配信」として許容）。あわせて**正常系のサブディレクトリ＋`%20` エンコード e2e** を追加（使い勝手の回帰固定）。
- **content-type 網羅（T2）:** `ImageContentType` を internal 化し png/jpg/jpeg/gif/webp/大文字/未知 をパラメタ化検証。
- **rooted/backslash/大文字拡張子（T3/T4）:** 主張裏取りの回帰ケースを追加。
- **相対 env の CWD 解決（L3）:** ctor コメント＋README 英日に「絶対パス推奨」を明記。

**見送り（follow-up）:** ファイルサイズ上限なし（`File.ReadAllBytes`）— ループバック・人設定フォルダ・プレビュー用途で実害限定。既存の「POST ボディ上限未実装」follow-up と同カテゴリで将来まとめて対応。

## 検証

- ビルド 0 警告
- `Line.OpenApi.Tools.Tests` 127/127 緑（+13：純粋関数封じ込め＋ループバック/raw ソケット e2e＋content-type）
- 変更は `/tools` 支援ティア内の HTTP プレビュー機能。生成コード・R1 ルーティング・form-urlencoded・webhook 多態・公開 API snapshot・Kiota 版ピンに非接触（pack 12 パッケージ契約は Tools 除外で不変）。

## 判定

**GO 推奨・人の go/no-go 待ち（未コミット）。**

## 変更ファイル

- 実装: `tools/Line.OpenApi.Tools/Services/FlexPreviewService.cs`
- テスト: `tests/Line.OpenApi.Tools.Tests/FlexPreviewAssetServingTests.cs`（新規）
- ドキュメント: `tools/README.md` / `tools/README_ja.md`

## 追補（2026-09-04・LINE Flex 実仕様への準拠）

一次情報（https://developers.line.biz/ja/reference/messaging-api/#flex-message）に合わせ、配信対象を LINE が Flex メッセージで実際にレンダリングする形式に厳密化。ゲート後・未公開のため破壊的変更の懸念なし。

- **画像は JPEG/PNG（APNG=`.png`）のみ**に限定＝`.gif`/`.webp` を配信対象から除外（LINE 非対応のため）。
- **動画 `type:"video"`（mp4）に対応**＝`.mp4` を配信対象に追加（content-type `video/mp4`）。renderer は video を `previewUrl`（JPEG/PNG のポスター）＋▶ で描画し mp4 本体は取りに行かない（LINE アプリと同じ）ため、プレビュー表示は previewUrl 配信で足りるが、`url`（mp4）も相対参照で解決可能にした（アセット一式をフォルダに置く前提と一貫）。`renderer.js` は無改修（parity 維持）。
- **名称を中立化**（対象が画像＋動画になったため）＝環境変数 `LINE_FLEX_MCP_IMAGE_DIR` → **`LINE_FLEX_MCP_ASSET_DIR`**、`ResolveImagePath`→`ResolveAssetPath`、`ImageContentType`→`AssetContentType`、`ImageExtensions`→`AssetExtensions`、`_imageDir`→`_assetDir`。
- テスト更新: gif/webp は拒否・mp4 は受理・content-type に mp4 を追加、ループバック e2e に mp4（video/mp4）配信を追加。ファイルを `FlexPreviewAssetServingTests.cs` にリネーム。
- ビルド 0 警告・Tools テスト **131/131** 緑（+4）。README 英日を JPEG/PNG＋mp4・env var 改名・video コンポーネント例で更新。
