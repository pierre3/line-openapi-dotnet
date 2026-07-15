# レビュー記録: カバレッジ 3 パッケージの CLI/MCP ツール露出

- **日付:** 2026-07-15
- **対象:** `Line.OpenApi.Tools`（CLI/MCP）へ Insight / ManageAudience / Shop を露出
- **スコープ:** `docs/CLI-MCP-tool-spec.md` §4.8。Module は意図的に見送り（パートナー限定・概念難・ローカル開発ツールに不適）。
- **前提:** ビルド 0 警告・Tools テスト 75/75 緑（既存 72 → +3）。生成コード（`src/**/Generated/`）は対象外。

## 追加サーフェス

- **Insight**（CLI `line insight` 7 コマンド／MCP `line_insight_*` 7 本＝**全 read-only**）
- **ManageAudience**（CLI `line audience` 7／MCP read `list`・`get`＋write `create`・`add_users`・`delete`。**by-file 2 本は CLI 専用**＝バイナリ非公開方針）
- **Shop**（CLI `line shop mission`／MCP `line_shop_mission`）

実装は既存パターン（サービス層＝唯一の実体、CLI/MCP 薄ラッパ、トークン単位メモ化、JSON 解析ガード→`MessageInputException`(exit2)、DTO 境界、ホスト制限）を踏襲。ManageAudience の control/data 2 ホストはファサードが R1 分離を解決済み。

## ゲート結果（3 役サブエージェント）

| 役 | 判定 | 要点 |
|---|---|---|
| code | **PASS** | 既存 RichMenu/Liff/Message と高い一貫性。BLOCK/High/Medium なし。Low/nit のみ。 |
| security | **PASS** | read-only 分類正・トークン/シークレット漏洩経路なし・ホスト制限最小・by-file MCP 非公開徹底・既存不変条件（token issue 非露出／replay SSRF 緩和）健在。Low 2（既存受容パターン）。 |
| test-arch | **PASS** | MCP 表面スナップショット exact-set＋disjoint で退行防止。read-only/write 分離ガード網羅。Low 3＋Info（非ブロッキング）。 |

**人の go/no-go: 待ち。**

## 反映した指摘

- **[test-arch Low#3] Insight 可用性ガード:** 7 本のいずれかを WriteTools へ誤配置すると `--read-only` 時に使えなくなる可用性リグレッション（exact-set/disjoint では検知不可）。→ `McpToolRegistrationTests` に「Insight 全 7 本 ⊆ read-only」の `Assert.All` を追加。
- **[code nit] `isIfa ? true : null`:** 意図（false 時は API 既定に委ねる）を 1 行コメントで明記（`AudienceService.cs`）。

## 非ブロッキング follow-up（未対応・記録のみ）

- **[test-arch Low#1 / code Low] list 射影の HTTP 正常系未検証:** `AudienceService.ListAsync` の DTO 射影は StubHttpMessageHandler で突けない（transport 注入シーム無し）。RichMenuService.ListAsync も同様＝前例踏襲で許容。将来 seam 追加で低コスト化可。
- **[test-arch Low#2] 解析ガードは malformed のみ:** wrong-shape（配列/スカラー）や JSON `null` 分岐は未到達。Kiota は wrong-shape で例外を投げず空オブジェクトを返すため確実な throw テストにできず、RichMenuServiceTests と同水準で許容。
- **[test-arch Info / DRY] Kiota JSON 解析 try/catch が 3 サービスに複製:** 共通ヘルパ（`KiotaJson.ParseObject<T>`）へ寄せ候補。
- **[code Low] MCP に audience スキーマ補助なし:** `line_message_schema`/`line_richmenu_schema` 相当が audience にはない（記述文のみ）。将来 follow-up 候補。
- **[security Low] トークン単位クライアントキャッシュのプロセス寿命保持／入力 JSON PII の例外エコー:** いずれも既存受容パターン・軽微。
- **[実機確認・GA 前] ManageAudience の multipart `file` パートに `filename` 属性が付かない（Kiota 仕様）** ため by-file アップロードの実 LINE 受理を要スモーク（ライブラリ側既知事項の踏襲）。
