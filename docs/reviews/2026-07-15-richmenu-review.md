# 実装ゲートレビュー: リッチメニュー（ライブラリ `RichMenuClient` + CLI/MCP 開発サイクル）

- **日付:** 2026-07-15
- **対象:** `src/Line.OpenApi.Messaging/RichMenuClient.cs`（新規ファサード）＋ Tools（`Services/RichMenuService.cs`・`Cli/RichMenuCommands.cs`・`Mcp/ReadTools.cs`/`WriteTools.cs`・`Services/MessageSchemaService.cs` の richmenu root 追加）＋テスト＋ドキュメント
- **ブランチ:** `feat-richmenu`（未コミット→コミット）
- **ゲート:** code / security / test-arch（3 役サブエージェント）
- **判定:** **code=CONCERNS / security=PASS / test-arch=CONCERNS（BLOCK / High なし）＝ main マージ可。** 指摘は本セッションで反映済み。
- **人の go/no-go:** 未（本記録時点）

## 背景

「Rich Menu は OpenAPI 仕様に無い」という前提は誤りで、`messaging-api.yml` に全 23 操作が含まれ Kiota 生成済み（制御系 `api.line.me`＋画像 `api-data.line.me` の `/content`）。したがって追加したのは capability ではなく **使い勝手（ファサード）と CLI/MCP 開発サイクル**。設計・調査の詳細は `docs/coverage-roadmap.md`。

- ライブラリ: `RichMenuClient`（`MessagingClient` をラップ＝R1 control/data 分離を再利用。CRUD/validate/default/link/unlink/id-of-user＋画像ヘルパ `SetImageFromFileAsync`＝拡張子から `image/png`/`jpeg` 推論）。
- 開発サイクル（MCP+CLI）: MCP `line_richmenu_schema`→組立→`line_richmenu_create`（dryRun=online validate）→**CLI `line richmenu image`（画像アップロード＝MCP 非公開）**→`set_default`/`link`→実機確認。

## 結果サマリ

| 役 | 判定 | 要点 |
|---|---|---|
| code | CONCERNS（非ブロッキング） | 生成ビルダー対応・Stream 破棄・メモ化・read/write 分離すべて健全。Medium 1 件＝`Get*Id` の「null if none」契約が 404→ApiException で崩れる |
| security | PASS | ホスト分離・トークン非露出・SSRF・定数時間比較すべて無退行。画像 MCP 非公開・read/write 分離適切。Low 情報 3 件 |
| test-arch | CONCERNS（非ブロッキング） | R1 ホスト分離をトランスポート層で検証済み・表面スナップショット機能。網羅の非対称（online dryRun/parse 異常系未テスト）が指摘 |

## 反映した指摘

- **[code Medium] `GetDefaultIdAsync`/`GetIdOfUserAsync`/`GetAsync` の「null if none」契約違反** → 該当エンドポイントは 200 のみ定義で、LINE は未設定時 404 を返し Kiota が `ApiException` を投げる（null 分岐がデッドコード）。`NullOn404` ヘルパを追加し 404 のみ null へ写像（他ステータスは伝播）。契約が真になり CLI/MCP の null 分岐も live 化。回帰テスト（404→null／非 404 は伝播）追加。
- **[test-arch 中] online dryRun 経路（`ValidateAsync`）のトランスポート未検証** → `POST api.line.me/v2/bot/richmenu/validate` を確認するテスト追加。
- **[test-arch 中] `RichMenuService.ParseAsync` 異常系未テスト** → 不正 JSON→`MessageInputException` テスト追加。あわせて parse の catch に `InvalidOperationException`（valid JSON だが object でない形状）を含め exit 2 へ写像。
- **[test-arch 低] mutating ツールの read-only 除外が暗黙** → `McpToolRegistrationTests` に richmenu mutating（create/delete/set_default/link/unlink）の明示 `DoesNotContain` を追加。
- **[test-arch 低] transport 未カバー便利メソッド** → `GetImageAsync`（データプレーン GET）・`LinkToUserAsync`（2 セグメント URL `/user/{userId}/richmenu/{richMenuId}`）のテスト追加。
- **[test-arch 低] dryRun 非対称の利用者誤解余地** → `line_richmenu_create` の `dryRun` description に「message dryRun と異なり要トークン・API 呼び出し発生」を明記。

## 未対応（follow-up・非ブロッキング）

- **[code/CLAUDE.md 既存] CancellationToken 固定（`CancellationToken.None`）** — richmenu CLI/MCP も既存方針に踏襲（listen 以外の CT 配線は横断 follow-up）。
- **[code 低] `SetImageFromFileAsync` の `FileNotFoundException` 未ラップ** — CLI ローカル用途で許容。将来 `InferImageContentType` の `ArgumentException` とエラー体験を揃える余地。
- **[security 低] トークンキー辞書のプロセス寿命保持**（既存 Message/Liff と同一の受容済みパターン）／**`line_richmenu_schema` と `line_message_schema` が互いの type を受理**（機能的な緩さ・非セキュリティ）／**CLI `image-download` の無確認上書き**（ローカル CLI で許容）。
- **[test-arch 低] CLI コマンド自動テストなし**（既存 CLI 方針と一貫・受容）／`DownloadImageAsync` の null-content 分岐は将来 transport シームで。

## 検証

- ビルド 0 警告。テスト: ライブラリ **113**（+21）／ Tools **72**（+12）／ Isolation 1、全緑。
- pack スモークテスト PASS（6 パッケージ・新規依存なし）。DocFX **0 warnings**（`RichMenuClient` を API リファレンスに反映＝api/ は gitignore で CI 再生成）。
- CLI e2e（`richmenu --help` で 13 サブコマンド確認）・richmenu schema 出力確認（8.5KB、RichMenuArea/Bounds/Action 閉包）。
- 公開 API snapshot approved 更新。
