# レビュー記録: 上流 LINE OpenAPI 追従の仕組み（spec-sync）

- **日付:** 2026-08-19
- **ブランチ:** `feat-spec-sync`
- **対象:** 上流 `line/line-openapi` の spec 更新に追従する仕組みの新規実装。
  - `openapi/upstream-manifest.json`（バージョンアンカー：ref=コミット SHA・取得日・spec 別 LF 正規化 sha256）
  - `.gitattributes`（`openapi/*.yml`・`*.json` を `eol=lf`）
  - `scripts/lib/SpecNormalization.ps1`（LF＋urn 引用符化の共有正規化）
  - `scripts/check-spec-drift.ps1`（純検知・JSON/サマリ・exit code・新規 spec 検出）
  - `scripts/verify-spec-manifest.ps1`（manifest↔同梱ファイルのハッシュ整合ガード）
  - `scripts/generate.ps1`（`-Update`/`-Ref` ピン対応へ改修）
  - `.github/workflows/spec-sync.yml`（週次 cron＋手動：検知→Issue upsert→再生成→draft PR）
  - `.github/workflows/ci.yml`（整合ガードステップ追加）
  - ドキュメント（design §9・coverage-roadmap・README 英日・CLAUDE.md）

## 事前の実測（設計根拠）

- 上流はタグ/リリースを持たず、spec の `info.version` は固定値（`0.0.1`/`1.0.0`）→ 追従基準は**コミット SHA**のみ。
- **CRLF 落とし穴**: 手元 CRLF・上流 LF の生バイト比較は全行誤検知（messaging-api だけで約 11,800 行）。改行正規化すると実ドリフトは `channel-access-token.yml`（#130・ドキュメントのみ）1 本で、他 7 本は最新 `de8bd9e` と一致。
- 上流の既定ブランチは **`main`**（`master` は存在せず）＝旧 `generate.*` の `raw/master/` は潜在バグ。SHA ピンで解消。

## ゲート結果

| 役 | 判定 | 概要 |
|---|---|---|
| security | **PASS** | ブロッカー無し。script injection 回避（上流自由文字列は `${{ }}` でなく PS 変数経由）・action SHA ピン一致・トークン egress は api.github.com 限定・権限最小・PR は draft 固定/自動マージ無しを確認。低リスク follow-up L1〜L4。 |
| code | **CONCERNS**（非ブロッキング） | M-1（`git add -A` が CI 一時ファイル混入）＋ L-1〜L-4。DRY 一元化・冪等性・後方互換は良好と評価。 |
| test-arch | **CONCERNS**（非ブロッキング・BLOCK なし） | ADR 妥当（SHA アンカー・正規化一元化・CRLF 恒久対策）。本命指摘＝manifest↔同梱の整合ガード欠如、新規 spec 検出の盲点、`git add -A` 混入。 |

## 反映した指摘

- **[M-1 / test-arch] `git add -A` の混入** → CI スクラッチを `$RUNNER_TEMP` へ退避＋`git add openapi src` にパス限定。
- **[test-arch 本命] manifest↔同梱の整合ガード欠如** → `scripts/verify-spec-manifest.ps1` を新設し `ci.yml` の PR ゲートに追加（共有正規化を再利用・ネットワーク不要）。
- **[test-arch] 新規上流 spec の検出盲点** → `check-spec-drift.ps1` に上流ルート `*.yml` 列挙＋未追跡検出（`unknownSpecs`）を追加。非 spec の `docker-compose.yml` は manifest の `ignoredUpstreamFiles` でデータ駆動除外。
- **[code L-1] gh フォールバックが Stop 下で不発** → `Invoke-GhApiRaw`（代入→`$LASTEXITCODE` 判定→parse）へ統一。
- **[code L-2] manifest の CRLF churn** → `.gitattributes` に `openapi/*.json text eol=lf`＋`generate.ps1` は `WriteAllText` で LF 明示書き込み。同梱 manifest も LF 化。
- **[code/test-arch] urn 正規表現の複数要素癒着** → 文字クラスからカンマ除外（現行 spec は挙動不変）。
- **[security L2] force push でレビュアー修正消失** → `--force` を `--force-with-lease` へ。
- **[code L-4] stale コメント** → `check-spec-drift.ps1`（既定 master→main）・`generate.ps1`（PoC/.NET 8→net10・追従説明）を修正。

## follow-up（非ブロッキング・GA 前に検討）

- **[security L1]** 特権トークン（contents/issues/pull-requests write）と「上流派生コードの build/test」が同一ジョブに同居。生成/build/test を `permissions: {}` 別ジョブ＋artifact 受け渡し、または `persist-credentials: false` に分離すると供給鏈的に堅い。line/line-openapi は一次ソース（LINE Corp）ゆえ非ブロッキング。
- **[test-arch]** `-Update` が awareness（module-attach）の sha256 も無言でリベースする（ref 全体が動くため整合的だが、awareness 変化を人へ強制表示したいなら imported のみ hash 更新にする案）。
- **[security L3/L4]** 上流コミットメッセージの markdown 混入（GitHub サニタイズ下で影響極小）・`git add` 対象限定（反映済み）。
- **[test-arch]** 生成コードのみの追加（新オペレーション/モデル）は公開 API snapshot に出ず PR チェックリスト依存＝ADR 受容済みの残余リスク。
- 正規化の冪等性＋urn マルチ値エッジの pester ユニット（低優先）。

## 検証

- 検知: up-to-date で exit 0・ドリフト（`-Branch 99df56b7`）で該当 spec DRIFT・exit 1。新規 spec 検出は非 spec を除外し誤検知なし。
- `generate.ps1 -Update`: `de8bd9e` 再同期で内容不変・manifest LF 更新・`ignoredUpstreamFiles` 保持。
- 整合ガード: imported 8 本 ok。
- ビルド 0 警告・テスト 264＋83＋1 全緑。全 PS スクリプト構文パス。

**判定: GO 推奨（人の go/no-go 待ち）。** ブロッカー無し、収束した中位・低位指摘は反映済み、L1 等はコミット後 follow-up として記録。
