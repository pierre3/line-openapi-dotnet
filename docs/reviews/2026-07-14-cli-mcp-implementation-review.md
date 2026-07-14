# 実装ゲートレビュー: Line.OpenApi.Tools（CLI / MCP ツール）

- **日付:** 2026-07-14
- **対象:** `tools/Line.OpenApi.Tools/`（実装）＋ `tests/Line.OpenApi.Tools.Tests/`
- **ブランチ:** `feat-cli-mcp-tool`
- **ゲート:** code / security / test-arch（3 役サブエージェント）
- **判定:** **3 役すべて CONCERNS（BLOCK / High なし）＝ main マージ可。** 指摘は本セッションで反映済み。
- **人の go/no-go:** 未（本記録時点）

## 結果サマリ

| 役 | 判定 | 要点 |
|---|---|---|
| code | CONCERNS（非ブロッキング） | High なし。DTO 境界 PASS。Medium は MCP 常駐時の HttpClient リーク・verify の全例外丸め |
| security | CONCERNS（BLOCK なし） | 最重要3点（MCP 非露出ゲート・Webhook 定数時間比較・ホスト誤送出なし）は設計どおり。Medium は Windows ACL/警告・MCP replay の SSRF |
| test-arch | CONCERNS（非ブロッキング） | ADR 妥当。Medium は reveal ゲート/終了コード/ツール網羅の未テスト |

## 反映した指摘（本セッションで対応）

- **[code M1] MCP 常駐時の HttpClient リーク** → `MessageService`/`LiffService` をトークン単位で `ConcurrentDictionary` メモ化（呼び出しごとの HttpClient 累積を停止・ファサード構築＝Liff ホスト制限を温存）。
- **[code M2] `TokenService.VerifyAsync` が全 ApiException を無効トークンに丸め** → `when (ex.ResponseStatusCode == 400)` に限定、他は伝播（exit 4）。
- **[code L3] エラー文言が廃止 `--json` を案内** → `--messages` に修正。
- **[code L4] 入力ファイル不在の終了コード不統一** → `CliRuntime` に `FileNotFoundException`/`DirectoryNotFoundException`→exit 2 を追加。
- **[code L6] `--days` 未検証** → `TokenService.IssueAsync` で 1..30 日を検証（CLI/MCP 共通）。
- **[code L8] 陳腐化コメント** → 修正。
- **[security M1] Windows ACL 未制限＋`LINE_CONFIG` で警告が虚偽** → 保存先が `%USERPROFILE%` 配下か判定し警告文を正確化（配下＝継承 ACL／配下外＝他ユーザー可読の恐れを明示）。
- **[security M2] MCP `line_webhook_replay` の SSRF 面** → 既定でループバック宛先のみ許可、非ループバックはサーバ起動 `--allow-remote-replay` で opt-in（`McpToolOptions.AllowRemoteReplay`）。
- **[security L3] Unix 権限の TOCTOU 窓** → `FileStreamOptions.UnixCreateMode` で 0600 作成に変更（書き込み前から制限）。
- **[security L4] verbose/エラー出力の秘密混入余地** → `SecretScrubber`（`access_token=`/`Bearer` をマスク）を `CliRuntime.Fail` に適用。
- **[test-arch M1] reveal ゲート未テスト** → `WriteTools.BuildIssueResponse` を抽出し `TokenIssueRevealGateTests`（C 既定=非返却/`reveal+allow` のみ生返却/`revealDenied`）で固定。
- **[test-arch M2] 終了コード写像未テスト** → `CliRuntimeTests`（0/1/2/3/4 の写像）。
- **[test-arch M3] ツール表面が部分一致のみ** → 全ツール名の網羅集合一致＋`[Description]` 非空ガードを追加。
- **[test-arch L5 / code DRY] `StoreToken` 二重実装** → `ConfigStore.StoreAccessToken` に集約（CLI/MCP 双方が利用）。

## follow-up 追加対応（2026-07-14）

- **[test-arch M4] サービス層の transport 注入シーム（対応済み）** → `TokenService`/`WebhookService` に `HttpMessageHandler` 注入シームを追加。`StubHttpMessageHandler` で Token verify（200 有効 / 400 無効 / 500 は ApiException 伝播）と webhook replay のステータス写像を自動テスト化。CLI テスト 36→40。
- **CI/リリース（対応済み）** → `pack-verify` は `-p:ExcludeToolFromPack=true` で Tools を除外し 6 パッケージ契約を維持。`release.yml` を `v*`（ライブラリ）/`tools-v*`（Tools）の 2 ジョブに分離。build-test は slnx 全体で CLI を内包。

## 未対応（follow-up・非ブロッキング）

- **[code L5] Cocona パースエラーが exit 2 にならない**（必須オプション欠落等は exit 1）。仕様許容範囲として記録。
- **[code L7] CancellationToken 未配線**（`listen` は独自 cts で対応済み、他は Ctrl+C 即死）。
- **[security L5] .NET string の秘密メモリ滞留**（情報提供・費用対効果低）。
- Windows の**明示 ACL 設定**（現状は継承 ACL＋正確な警告）。

## 検証

- 全ソリューションビルド 0 警告 / 0 エラー。
- CLI テスト **40/40 合格**（24→36 で reveal ゲート・終了コード・ツール網羅を追加、→40 で transport 注入シーム経路を追加）。
- 手動 e2e: `config` CRUD、`webhook verify`（署名 OK/NG・JSON）、`webhook listen`（実 POST 受信 200）、MCP `initialize`＋`tools/list`（フル 18／`--read-only` 8）。
