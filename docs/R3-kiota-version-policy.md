# R3: Kiota バージョン整合ポリシー

- 日付: 2026-07-10（G3・初版「1.x 継続」）→ **2026-07-13（G4③で 2.0.0 移行に改訂）**
- 対象リスク: R3（生成 CLI / ランタイム / 推奨版の三者不一致による source-breaking・実行時不整合・非再現生成）

## 現状の三者（2026-07-13 更新）

| 要素 | 値 | 管理場所 |
|---|---|---|
| 生成 CLI（Microsoft.OpenApi.Kiota） | **1.34.1**（2.x CLI 未リリース＝最新） | `poc/scripts/generate.ps1` の `$ExpectedKiotaCliVersion` |
| ランタイム（Microsoft.Kiota.Bundle） | **2.0.0** | `poc/Directory.Build.props` の `KiotaBundleVersion` |
| `kiota info -l CSharp` の推奨 | 2.0.0（CLI 1.34.1 自身の推奨） | （情報のみ） |

> **重要（版体系の実態）:** Kiota は **CLI とランタイムを別系統でバージョニング**する。CLI は 1.34.1 が最新で 2.x は未リリース。ランタイム（Bundle/Abstractions）のみ 2.0.0 が最新 stable。したがって「2.0 移行」は **CLI 据え置き＋ランタイムのみのバンプ**であり、生成コード表面は変わらない（再生成不要）。

## 決定: **ランタイム 2.0.0 へ移行**（2026-07-13, G4③）

初版（G3）は「1.x 継続」だったが、下記により方針転換。プロジェクトはリリース前であり、可能な限り最新パッケージで進める方針。

- **転換理由:**
  - **1.22.2 は 1.x 系の最終リリース**（1.x は今後出ない）。留まると将来の CVE 修正が届かず、セキュリティ保守上 2.x が唯一の前進線。
  - G3 初版の 1.x 継続理由（①2.0 破壊的変更が不明／②G3 と回帰リスク分離）は両方解消：①破壊的変更は当方に一切当たらないと実証（下記）、②G3/G4 完了済み。
  - CLI が 1.34.1 据え置きのため生成コード表面は不変。**G4② の公開 API snapshot テストが差分ゼロを機械的に裏付け**。
- **2.0.0 破壊的変更（2026-05-06 リリース）の影響評価 — いずれも当方は無影響:**
  - net5/net6 TFM 削除・net8/net10 追加 → 当方 net10.0 単一で影響なし。
  - `IAsyncParseNodeFactory` 削除 → 実装なし。
  - `KiotaSerializer` 同期 Deserialize 削除（`DeserializeAsync` へ）→ 既に async 使用（`WebhookDeserializationTests.cs`）。
  - `MultipartBody` メソッド変更 → 未使用（LINE blob は Stream）。
- **実地検証（スパイク＋本移行）:** ビルド 0 警告・テスト 38/38 合格（snapshot 含む）・`dotnet list package --vulnerable` クリーン（Abstractions 2.0.0 は `>= 1.22.0` を満たし CVE 修正継承）。
- **セキュリティ:** 下限 `Microsoft.Kiota.Abstractions >= 1.22.0` は 2.0.0 で満たす。CVE-2026-44503 / GHSA-7j59-v9qr-6fq9 修正を継承。

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
