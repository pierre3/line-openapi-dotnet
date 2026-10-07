# 2026-10-07 spec-sync 上流追従（line-openapi @ fa5577c9）レビュー記録

## 概要

週次 spec-sync（2026-10-05 cron・run [37309200951](https://github.com/pierre3/line-openapi-dotnet/actions/runs/37309200951)）が上流ドリフトを検知し Issue #10 を起票。再生成・build・test・ブランチ push までは成功したが、**draft PR 作成が失敗**した。

- 原因: リポジトリ設定 "Allow GitHub Actions to create and approve pull requests" がオフ（`GitHub Actions is not permitted to create or approve pull requests`）。spec-sync 稼働後、実ドリフトが出たのは今回が初めてで、初めて顕在化した。**設定はオンに変更済み（人）**。
- PR は push 済みブランチ `spec-sync/update-fa5577c9` から手動で作成 → **PR #11**（`Closes #10`）。

## 上流差分（`de8bd9e` → `fa5577c9`、上流コミット日 2026-09-29）

`messaging-api.yml` のみ変化。他 7 本の取り込み spec と見送りの `module-attach.yml` は不変。**追加のみ（非破壊）**。

- 新規 op: `POST /v2/bot/message/pnp/templated/push`（`pushTemplatedMessagesByPhone`、任意ヘッダ `X-Line-Delivery-Tag`、202/422）、`GET /v2/bot/message/delivery/pnp/templated`（`getPNPTemplatedMessageStatistics`、`NumberOfMessagesResponse`）
- 新規モデル: `PnpTemplatedMessageRequest` / `PnpTemplatedMessageBody` / `PnpTemplatedEmphasizedItem` / `PnpTemplatedItem` / `PnpTemplatedButton`
- 既存 `PnpMessagesRequest` に任意プロパティ `customAggregationUnits` を追加
- 生成物の変化は `Generated/Api` のみ。`Generated/Blob` は `kiota-lock.json` の descriptionHash だけ（spec 全体ハッシュのため当然）

## 検証

- ローカル build（Release）エラー 0、`dotnet test` 全 422 件合格（lib 264 / Tools 131 / AI 26 / Webhook Isolation 1）
- `verify-spec-manifest.ps1` 8 本 ok
- PR #11 の CI（build-test / extension-test / pack-verify）pass

## 4 役ゲート結果（すべて PASS・BLOCKING なし）

- **spec-reviewer = PASS**: YAML 妥当。R1 = 新 op は op 単位 `servers` 無し＝`api.line.me`、Blob 側への混入なし（include/exclude `**/content` 不変）。R2 = 改名・衝突なし。manifest のハッシュは LF 正規化 spec と一致。
- **code-reviewer = PASS**: 手書きコード・公開 API snapshot 影響なし。新 op は `MessagingClient.Api` 経由で到達可能。既存 PNP にも手書きラッパ・CLI/MCP/AI 露出はないため、合わせて追加するものは無い（パートナー限定機能でもあり今回は露出しない）。
- **security-reviewer = PASS**: 新 op は `api.line.me` 固定・AllowedHosts 内・data 系混入なし・R1 順序影響なし。`X-Line-Delivery-Tag` は非機密の相関 ID。
- **test-arch-reviewer = PASS**: op 単位のホストテストは不要（全 op が `{+baseurl}` 共通で既存 `MessagingHostRoutingTests` が構造的に担保）。

## 本 PR で対応した指摘

- **refDate 据え置きバグ（spec / test-arch が独立に検出・再現確認済み）**: `scripts/generate.ps1` が `commit.committer.date.Substring(0,10)` を呼んでいたが、pwsh 7 の `ConvertFrom-Json` / `Invoke-RestMethod` は ISO 日付を `DateTime` に自動変換するため例外 → `catch` が警告にするだけで `refDate` が**黙って旧値のまま**残っていた。`DateTime` なら UTC 日付へ整形、文字列なら従来どおりに修正。manifest の `refDate` を `2026-07-28` → `2026-09-29` に手修正。
- **CHANGELOG**（code）: ライブラリ系統に `Unreleased` 節を英日で追加（次のライブラリリースは minor 相当）。

## 非ブロッキング follow-up（次サイクル）

1. **spec-sync の push 不具合（test-arch・中）**: checkout は bot ブランチの追跡 ref を持たないため、同名ブランチが既存（PR マージ前に翌週 cron が同じドリフトを再検知／run の再実行）だと `--force-with-lease` が `stale info` で拒否される。pwsh はネイティブコマンドの非 0 終了で止まらないため、**run は緑のまま黙って失敗**する。方針: `git ls-remote --exit-code --heads` で既存なら push をスキップし PR 作成/更新のみ（SHA 命名で内容は決定的・レビュアー fixup 保護）、スキップを PR 本文に明記、`$PSNativeCommandUseErrorActionPreference = $true` で無言失敗を排除。
2. **build/test 失敗が run 結果に出ない（test-arch・中）**: `continue-on-error` のため。PR 作成後の最終ステップで outcome が success 以外なら `exit 1`。
3. **Issue と PR 結果の非連動（test-arch・低）**: PR 番号・run URL・build/test 結果を追跡 Issue にコメント。失敗時は `if: failure()` で run URL を残す。
4. **spec 駆動の R1 ガードテスト（test-arch・中）**: `servers` が api-data のパス集合と Blob 生成フィルタ（`**/content`）の一致集合が等しいことを spec から検証（`manage-audience.yml` の `**/upload/byFile` も同型）。`/content` で終わらない data 系 op が上流に足された場合の黙った誤ルーティングを防ぐ。
5. **`ci.yml` に `permissions:` 未指定（security・中）**: 今回の設定オンで PR 作成/承認が可能になったため明示 `contents: read` を推奨。ただしリポジトリ既定は `default_workflow_permissions: read` を API で確認済みで、現状の実リスクは低い。main のブランチ保護で bot 承認だけではマージ条件を満たさないことも確認推奨。
6. **GITHUB_TOKEN で作った PR では `ci.yml` が起動しない（security・低）**: 今後自動作成される spec-sync PR には CI 結果が付かない。マージ前に close/reopen 等で CI を走らせる運用、または App token 化を検討。
7. **ドキュメント注意喚起（security・低）**: PNP を概念記事で扱う／ファサード化する場合、`to` はソルト無しのハッシュで元の電話番号に戻せる（ログや例外に出さない）、パートナー契約前提、`X-Line-Delivery-Tag` は `requestConfiguration.Headers` で付ける、を明記。
8. **露出判断の追跡（test-arch・低）**: PNP テンプレート送信の CLI/MCP 露出は `docs/coverage-roadmap.md` に候補として記録。

既知 follow-up（security L1: 特権ジョブ内の build/test）は未解決のまま継続。

## 判定

4 役 PASS。マージの go/no-go は人。
