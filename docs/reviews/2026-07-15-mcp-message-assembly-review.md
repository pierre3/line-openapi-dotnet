# 実装ゲートレビュー: MCP メッセージ組立支援（`line_message_schema` + dryRun）

- **日付:** 2026-07-15
- **対象:** `tools/Line.OpenApi.Tools/`（`Services/MessageSchemaService.cs` 新規・`Services/MessageService.cs`・`Services/MessageJson.cs`・`Mcp/ReadTools.cs`・`Mcp/WriteTools.cs`・`Hosting/ServiceRegistration.cs`・csproj）＋ `tests/Line.OpenApi.Tools.Tests/`（`MessageSchemaServiceTests`・`MessageDryRunTests` 新規、`McpToolRegistrationTests` 更新）＋ `docs/CLI-MCP-tool-spec.md` §4.6
- **ブランチ:** `main`（未コミット）
- **ゲート:** code / security / test-arch（3 役サブエージェント）
- **判定:** **code=CONCERNS / security=PASS / test-arch=PASS（BLOCK / High なし）＝ main マージ可。** 指摘は本セッションで反映済み。
- **人の go/no-go:** 未（本記録時点）

## 背景（旗艦ユースケース A）

ローカル PC の MCP から LINE を操作する需要検討の結論として、旗艦 = 「Bot 開発者が Flex/Template を対話で試作 → 自端末に push → 実機で見た目確認 → 直す」ループに絞る（本番の Webhook 受信起点の自動応答はサーバー常駐が本質で MCP 不適・対象外）。型の非対称性（単純 6 種は軽い / Flex は 44 型・自己再帰で重い）に基づく**非対称ハイブリッド**を実装:
- `line_message_schema`（読み取り）= 埋込 `messaging-api.yml` から `$ref` 推移閉包を `$defs` 付き JSON Schema で返す。
- send 4 ツールに `dryRun` = 送信せず型検証（`MessageService.ValidateMessagesAsync`、CLI/MCP 共通サービス層）。

## 結果サマリ

| 役 | 判定 | 要点 |
|---|---|---|
| code | CONCERNS（非ブロッキング） | High なし。閉包アルゴリズム・dryRun 分岐順序・キャッシュ不変性は健全。Medium M1＝非配列/空 JSON が silently `Valid:true, Count:0` になる dryRun の穴 |
| security | PASS | `line_message_schema` は公開仕様のみ返却＝秘密なし・dryRun はトークン解決前に return・YAML は DOM パースのみ（ガジェット面なし・入力は埋込固定＋type ホワイトリスト）・既存ポリシー無退行 |
| test-arch | PASS（CONCERNS 非ブロッキング） | Option R（実行時抽出）は単一情報源でドリフト排除＝妥当。中核不変条件は網羅。CONCERNS は dryRun の性質明確化・証明強度・カバレッジ |

## 反映した指摘（本セッションで対応）

- **[code M1 / security Low / test-arch #1（収束）] 非配列・`[]`・`null`・スカラー JSON が例外を出さず空検証になる** → Kiota 2.0 の `GetCollectionOfObjectValues` は非配列で例外を投げず非 null 空コレクションを返す（実機実証）。`MessageJson.ParseMessagesAsync` に `messages.Count == 0` ガードを追加し `MessageInputException`（exit 2）へ。dryRun の「送信前に誤りを捕捉」目的と、実送信の空 POST→400 を両方防止。`MessageDryRunTests` に単一オブジェクト/`[]`/`null`/スカラー/構文破損の Theory を追加。
- **[code L1] `GetCollectionOfObjectValues` が try/catch 外** → try 内へ移動（将来 Kiota が非配列で例外を投げる版でも生例外を漏らさない）。
- **[code L2] `SchemaTypes` の実行時型依存キャスト** → `Roots.Keys.ToArray()` に変更。
- **[test-arch #1] dryRun がスキーマ検証と誤解される** → `line_message_schema` description を「parse and shape-check（not full schema validation）」に明確化。未知 type が基底 `Message` にフォールバックする挙動をピン留めするテストを追加。
- **[test-arch #3] multicast/reply の dryRun がツール層で未テスト** → 4 ツール全てのツール層 dryRun テストを追加。
- **[test-arch #4] 「非送信」証明が環境依存＋comment 不正確** → `CredentialResolver.Resolve` は空 config でも throw せず（throw は後段 `RequireAccessToken`）である点を反映し comment を修正。`WithoutCredentialEnvAsync` で LINE_* 環境変数をクリアして negative proof をヘルメティック化。

## 未対応（follow-up・非ブロッキング）

- **[code L3/L4] `ScalarToJson` の数値コアース / `TryReadRef` の接頭辞判定** — 現 `messaging-api.yml` では検証済みで問題なし。将来 spec が「非引用の数値 example/enum を持つ string フィールド」や「`#/components/schemas/` で始まる description」を追加した場合の留意点として記録（スキーマは誘導用途＝実害小）。
- **[test-arch #2] スキーマ出力のメタスキーマ検証** — 出力は OpenAPI 3.0 Schema 方言を含み、厳密な 2020-12 バリデータは未知キーワードを無視するのみ（不正化しない）。ラウンドトリップ検証は任意フォローアップ。
- **[test-arch #4 強化案] `MessageService` への `HttpMessageHandler` 注入シーム** — 現状はヘルメティック env + 分岐順序で非送信を担保。より強い直接証明はシーム追加で可能（`TokenService`/`WebhookService` に前例あり）。
- **[test-arch #5 / spec §4.6] CLI パリティ** — `dryRun` 引数と `message schema` サブコマンドは CLI 未提供（検証本体は共通サービス層にあり容易）。

## 検証

- ビルド 0 警告。テスト: ライブラリ 92 / Tools **67**（従来 ~40 → +27）/ Isolation 1、全緑。
- pack スモークテスト PASS（6 ライブラリパッケージ・Tools は pack 除外を維持）。
- 依存監査クリーン（SharpYaml 2.1.1、NuGetAudit 警告なし）。
- 実出力を目視確認（flex 26KB / all 48KB / template 10KB、root `$ref`・discriminator/allOf 書き換え・FlexBox 自己再帰終端）。
