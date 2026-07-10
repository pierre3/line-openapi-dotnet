# R3: Kiota バージョン整合ポリシー

- 日付: 2026-07-10（G3）
- 対象リスク: R3（生成 CLI / ランタイム / 推奨版の三者不一致による source-breaking・実行時不整合・非再現生成）

## 現状の三者

| 要素 | 値 | 管理場所 |
|---|---|---|
| 生成 CLI（Microsoft.OpenApi.Kiota） | **1.34.1** | `poc/scripts/generate.ps1` の `$ExpectedKiotaCliVersion` |
| ランタイム（Microsoft.Kiota.Bundle） | **1.22.2** | `poc/Directory.Build.props` の `KiotaBundleVersion` |
| `kiota info -l CSharp` の推奨 | 2.0.0 | （情報のみ） |

## 決定: **1.x 系を継続**（当面）

- **理由:**
  - ランタイム 1.22.2 は **CVE-2026-44503 / GHSA-7j59-v9qr-6fq9（RedirectHandler クロスホスト機密ヘッダ漏洩）の修正版**であり、セキュリティ要件（`>= 1.22.0`）を満たす。
  - 2.0.0 は**メジャー版**で、生成コード表面・ランタイム API（例: DI ヘルパ、ハンドラ構成）に破壊的変更が入りうる。看板機能の回帰リスクを G3 と切り離す。
  - CLI 1.34.1 が生成するコードは 1.x ランタイムと整合して動作することを PoC/G3 のビルド・テストで確認済み。
- **2.0 移行は独立タスク**として後日判断（G4 前後）。移行時は下記「更新手順」をそのまま適用し、公開 API 差分を人手レビューする。

## ロックステップ運用ルール

1. **CLI とランタイムは同時に上げる。** 片方だけ更新しない。
2. CLI 版は `generate.ps1` の `$ExpectedKiotaCliVersion` で固定。生成時に `kiota --version` と照合し、不一致なら **既定でエラー**（`-AllowKiotaVersionMismatch` で意図的に回避可能）。
3. ランタイム版は `Directory.Build.props` の `KiotaBundleVersion` で一元管理。全パッケージが参照。
4. **セキュリティ下限を割らない:** `Microsoft.Kiota.Abstractions >= 1.22.0`。net10.0 SDK の NuGet 監査（推移的依存含む）で検知。

## 更新手順（版を上げる時）

1. `dotnet tool update --global Microsoft.OpenApi.Kiota --version <new>`。
2. `generate.ps1` の `$ExpectedKiotaCliVersion` を `<new>` に更新。
3. `kiota info -l CSharp` の推奨ランタイム版を確認し、`KiotaBundleVersion` を更新。
4. `pwsh scripts/generate.ps1` で再生成（`kiota-lock.json` 更新）。
5. `dotnet build`（0 警告）／`dotnet test`（全緑）。
6. 公開 API 表面の差分を確認。破壊的変更はラベル付けし人手レビュー（設計 §9）。
7. `dotnet list package --vulnerable --include-transitive` で監査。

## 上流 YAML 正規化（付随対応）

`channel-access-token.yml` のフロー配列内 `urn:ietf:...:jwt-bearer` は未引用だと SharpYaml が
コロンを誤認しパースエラーになる。master 再取得時の再発を防ぐため、`generate.ps1` に**冪等な
引用符化正規化**を追加した（既に引用符付きなら no-op）。上流への報告は任意。
