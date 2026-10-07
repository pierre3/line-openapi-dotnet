# 2026-10-07 spec-sync ワークフロー堅牢化 レビュー記録

## 概要

PR #11（上流 `fa5577c9` 取り込み）のゲートで出た spec-sync ワークフローの follow-up を、ブランチ `ci/spec-sync-hardening` で対応した。経緯は `2026-10-07-spec-sync-fa5577c9-review.md`。

- **無言失敗の排除**: Issue upsert / draft PR / 同期回復 close の各ステップで `$PSNativeCommandUseErrorActionPreference = $true`。非 0 終了自体が判定結果のプローブ（`git diff --cached --quiet`、`git ls-remote --exit-code`＝0 存在/2 無し/他は throw）と `gh label create` だけ一時的に除外。
- **既存 bot ブランチへ push しない**: checkout に追跡 ref が無いため `--force-with-lease` は常に stale 拒否だった。同一上流 SHA の再生成は決定的なので push をスキップ（レビュアー fixup を保護）し、PR 作成/本文更新のみ。本文に注記（生成器変更は手動再生成・build/test 結果は今回 run のもの）。
- **却下 PR を作り直さない**: 同じブランチの PR が未マージで close されていれば作成しない（毎週の再作成ループ防止）。
- **追跡 Issue へ結果コメント**（`!cancelled()`）: regenerate/build/test/PR ステップの outcome と PR URL。PR が無い場合は原因候補（却下済み PR・再生成失敗・変更なし/リポジトリ設定/git・gh 失敗）を出し分け。ステップ出力は `env:` 経由で受け渡し。
- **build/test 失敗で run を失敗扱い**（最終ステップ。`continue-on-error` のため `outcome` で判定）。
- **PR 本文**: run URL、`GITHUB_TOKEN` 製 PR では `ci.yml` が走らない注記、`Closes #<追跡 Issue>`。
- **多層防御**: `latestSha` を `^[0-9a-f]{40}$` で検証してから出力。
- **`ci.yml`**: `permissions: contents: read` を明示、checkout に `persist-credentials: false`。
- docs: 設計 §9、CLAUDE.md（前提のリポジトリ設定・挙動・pwsh 日付自動変換の落とし穴）。

## 検証

- YAML パース OK。`gh --jq` で null が空文字になること（却下 PR 判定の前提）を確認。
- pwsh の終了コード（ls-remote: 既存 0 / 無し 2 / 不正 remote 128）と、GHA と同じ `$ErrorActionPreference='stop'` 下で `$PSNativeCommandUseErrorActionPreference=$true` のネイティブ非 0 が終端例外になることを手元で確認。
- ワークフロー自体の実走は未実施（次回ドリフト時または `workflow_dispatch` で確認）。

## 3 役ゲート結果（すべて PASS・BLOCKING なし）

- **security-reviewer = PASS**: 式展開の値はすべて GitHub が決める値（SHA・番号・URL・outcome）で、上流由来文字列は `Get-Content` 経由のみ＝注入経路なし。`ci.yml` の `contents: read` で既存 3 ジョブは動く。force push 撤廃で fixup 上書き経路も消滅。
- **test-arch-reviewer = PASS**: PR #11 で指摘した 3 件（push 拒否の無言失敗・build/test 失敗が run に出ない・Issue と PR の非連動）すべて解消。新規/既存ブランチ・変更なし・build/test 失敗・PR ステップ失敗の各分岐を机上で追跡し正しい。
- **code-reviewer = PASS**: 切替範囲・`$LASTEXITCODE` 退避・早期 `exit 0` の必要性（ls-remote の 2 が残るため必須）・gh 出力 URL の扱いとも正しい。

## ゲート後に反映した非ブロッキング指摘

security N-a（SHA 形式検証）・N-b（`env:` 経由）・N-d（`persist-credentials: false`）、test-arch 1（却下 PR 再作成ループ）・2（再生成失敗を Report に出す）・3（既存ブランチ時の build/test 結果の注記）・4（close ステップの fail-loud）・5（`!cancelled()`）、code 4（コメントの SHA 表記）・5（close ステップ）・6（`env:` 経由）。

code 1（`generate.ps1` の refDate バグ本体）は PR #11 で修正済み。本ブランチは main から分岐しているため、PR #11 のマージで解消する。

## 残る follow-up（次サイクル）

- `actionlint` を `ci.yml` に導入（式・`steps.*` 参照の静的検査）。`workflow_dispatch` で既存ブランチ分岐を一度実走させる。
- spec 駆動の R1 ガードテスト（`servers` が api-data のパス集合と Blob 生成フィルタの一致集合を照合）。PR #11 記録の follow-up 4。
- Issue upsert 失敗時は後続（再生成・PR・Report）が全て skip され何も記録されない（fail-loud の意図どおり・run は赤）。
- main の branch protection / ruleset で bot の approve がマージ条件に数えられないことを確認（security N-c・人による設定確認）。
- 既知: security L1（特権ジョブ内の build/test）。

## 判定

3 役 PASS。マージの go/no-go は人。
