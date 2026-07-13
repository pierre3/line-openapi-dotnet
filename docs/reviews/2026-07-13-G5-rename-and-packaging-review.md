# G5 リリース準備 — リネーム適用＋NuGet パッケージング ゲートレビュー

**日付:** 2026-07-13
**対象:** 未コミットの作業ツリー変更（ブランチ `main`）
**スコープ:**
1. パッケージ/名前空間リネーム `Line.*` → `Line.OpenApi.*` の適用（プロジェクト/ディレクトリ/`namespace`/`using`・Kiota 再生成・`LineOpenApi.slnx`・`docfx.json`・公開 API snapshot approved・README/manual・レビュアー agent md・`generate.ps1`/`generate.sh`）
2. NuGet パッケージング（`Directory.Build.props` 共通メタデータ、各 src csproj `Description`、ルート `LICENSE`(MIT)、`.github/workflows/ci.yml`・`release.yml`）

**確定パラメータ:** ライセンス MIT / 初期バージョン `0.1.0-preview` / `Authors=pierre3` / `RepositoryUrl=https://github.com/pierre3/line-openapi-dotnet`（ユーザー確認済み）。

## 検証（実装完了時点）

| 項目 | 結果 |
|---|---|
| `dotnet build LineOpenApi.slnx` | 0 警告 / 0 エラー |
| `dotnet test LineOpenApi.slnx` | Line.OpenApi.Tests 92/92 ＋ IsolationTests 1/1 |
| `dotnet pack` | 5 nupkg ＋ 5 snupkg、警告なし。nuspec で ID/MIT/README/パッケージ間依存/SourceLink(commit) 確認 |
| `dotnet docfx docs/manual/docfx.json` | 0 warnings |
| NuGet 監査（`--vulnerable --include-transitive`） | クリーン |

## ゲート結果（サブエージェント 3 役）

- **code-reviewer = PASS（非ブロッキング指摘あり）**
- **security-reviewer = PASS（低ハードニングのみ・非ブロッキング）**
- **test-arch-reviewer = CONCERNS（全て非ブロッキング）**

必須観点はいずれも問題なし: Webhook 署名の定数時間比較（`FixedTimeEquals`）維持、R1 BaseUrl 順序維持、トークン非露出、許可ホスト制御不変、公開 API 表面不変（snapshot PASS）、IsolationTests の隔離維持、一方向依存維持。手書き `.cs` の実質差分は namespace/コメント/`HttpClientName` const 値のみ。

## 指摘と対応

| # | 重大度 | 指摘 | 対応 |
|---|---|---|---|
| 1 | MEDIUM | `ci.yml` の `dotnet list --vulnerable` は勧告検出でも exit 0 でゲートにならない | **修正済**: restore を `-p:NuGetAuditMode=all -warnaserror:NU1901-1904` に変更し実効ゲート化。`list` は可読サマリ用に残置。ローカルで強制 restore し誤検知なし・exit 0 を実証 |
| 2 | MEDIUM | `release.yml` が `pack --no-build -p:Version` のみで DLL 版と nupkg 版が乖離 | **修正済**: 解決版を build step にも `-p:Version` で適用し一致化 |
| 3 | LOW | `release.yml` の `${{ inputs.version }}` を run へ直接補間（注入面） | **修正済**: `env:` 経由（`$INPUT_VERSION`/`$VERSION`）に変更 |
| 4 | LOW | publish ジョブに保護なし | **修正済**: publish ジョブに `environment: nuget` を付与（GitHub 側で reviewer/保護ルール設定可能なフック） |
| 5 | INFO | `generate.ps1` の R3 コメントが「1.x 継続」で陳腐化 | **修正済**: 「ランタイム 2.0.0 移行済、CLI は 1.34.1 据え置き」に更新 |
| 6 | LOW | Actions が SHA 未ピン（`@v4`） | **follow-up**（preview 段階。正式版前にサプライチェーン強化として commit SHA ピン留め） |
| 7 | LOW | `Directory.Build.props` の README 同梱 ItemGroup 条件がテストにも名目付与（無害） | 受容（テストは `IsPackable=false` でパックされず機能的に無害） |
| 8 | INFO | `HttpClientName` const 値変更（名前付き HttpClient のキー） | 受容（パッケージ名整合・snapshot 反映済・preview 段階） |
| 9 | LOW | パッケージメタデータ自体の自動検証（pack スモーク）なし | follow-up 候補（必須でない） |

## 判定

**GO 推奨（人の go/no-go 待ち）。** ブロッカーなし。MEDIUM 2 件（CI 監査ゲート実効化・release バージョン一致）は本コミットで反映済み。正式リリースタグを打つ前の残タスク: NuGet.org への実公開（`NUGET_API_KEY` 投入・タグ push）、Actions の SHA ピン留め（#6）。
