# レビュー記録: `Line.OpenApi.Bot` 便宜メタパッケージ追加

- **日付:** 2026-07-14
- **対象:** 任意メタパッケージ `Line.OpenApi.Bot` の新規追加（Bot 一式を 1 参照で導入）
- **ブランチ:** `main`（実装は未コミット状態でゲート実施）
- **ゲート:** 実装完了後の 3 役レビュー（code / security / test-arch）。spec は OpenAPI 仕様変更を伴わないため対象外。

## 変更内容

- 新規 `src/Line.OpenApi.Bot/Line.OpenApi.Bot.csproj`。コードを持たず、`Line.OpenApi.Messaging` + `Line.OpenApi.Messaging.Webhook` + `Line.OpenApi.ChannelAccessToken` を `ProjectReference` で束ねる依存束ねメタパッケージ（LIFF 非包含。設計 §4.2 のパッケージ一覧 `Line.OpenApi.Bot` に準拠）。
- csproj 設定: `IncludeBuildOutput=false`（空アセンブリ非同梱）/ `IncludeSymbols=false`（空 snupkg 回避・`Directory.Build.props` の `true` をローカル上書き）/ `GenerateDocumentationFile=false` / `NoWarn` に `NU5128` 追加。`PrivateAssets` 未設定＝3 依存を推移的に流す。
- `LineOpenApi.slnx` にプロジェクト追加、`README.md` のパッケージ表とディレクトリツリーを更新。

## 実測

- `dotnet pack`（Release）: 警告 0。nuspec は `id=Line.OpenApi.Bot`、`net10.0` 依存グループに 3 パッケージ（`Line.OpenApi.ChannelAccessToken` / `Line.OpenApi.Messaging.Webhook` / `Line.OpenApi.Messaging`、全 `0.1.0-preview`）、README.md 同梱、`lib/` アセンブリなし、snupkg 非生成。
- ソリューション全体 build 0 警告 / test 92/92 ＋ Isolation 1/1。

## 判定

| 役 | 判定 | 備考 |
|---|---|---|
| code | **PASS** | ブロッキングなし。csproj 設定はメタパッケージの定石、設計 §4.2 と完全一致、Directory.Build.props との噛み合わせ良好。pack 実測で裏付け。 |
| security | **PASS** | コード追加ゼロ＝新規コード由来攻撃面なし。Kiota 下限（Abstractions ≥ 1.22.0 / CVE-2026-44503）はサブパッケージ経由で伝播、メタ層が引き下げる経路は構造的に存在しない。NuGetAudit カバレッジ有効。 |
| test-arch | **PASS**（非ブロッキング CONCERNS） | 設計 ADR 整合・一方向依存維持。「コードなしゆえテスト不要」は妥当（snapshot 完全性ガードは Bot を対象外＝誤検知/見逃しなし）。 |

## 非ブロッキング CONCERNS

- **[中] メタパッケージの nuspec 依存構成に対する自動回帰が無い。** 「3 依存が展開／lib なし／snupkg 非生成」は手動確認のみで CI（`ci.yml`）は pack を実行しない。将来の静かなリグレッション（依存ドロップ、`PrivateAssets=all` 誤付与、`IncludeBuildOutput`/`IncludeSymbols` の復活）を検知できない。
  - → **本セッションで対応（下記「pack スモークテスト追加」）。**
- **[低] Bot が将来誤ってコードを獲得しても完全性ガードは沈黙する**（テストが Bot を参照しないため）。Bot は恒久的にコードレス前提のため実害は薄いが記録として残す。

## pack スモークテスト追加（上記 CONCERNS 対応）

- **`scripts/verify-packages.ps1`（新規）** — `dotnet pack` を実行し、生成物を検証する再利用可能スクリプト（ローカル/CI 両用、失敗時 exit 1）。検証内容:
  - nupkg 総数＝6（5 code + Bot）、samples/tests（IsPackable=false）の非混入。
  - 全パッケージ README 同梱。
  - **内部依存グラフ（Line.OpenApi.*）を厳密照合** — code 5 本は `Line.OpenApi.Core` のみ（一方向依存 ADR を保護）、Bot は 3 依存（ChannelAccessToken / Messaging / Messaging.Webhook）。
  - code パッケージは `lib/net10.0/*.dll`＋snupkg あり、Bot は lib なし＋snupkg なし。
- **`.github/workflows/ci.yml`** に `pack-verify` ジョブ追加（pack → スクリプト実行）。
- **再ゲート（code / test-arch）= 両 PASS**（Low/Info 指摘のみ）。反映済み: 内部依存グラフの照合追加（両者が挙げた最有用改善）／パッケージ id を nuspec `<id>` から取得／冗長な `Add-Type` 削除／CI の二重 pwsh 解消。
- **negative test 実施** — Bot の `IncludeSymbols=false` を一時的に外すと、スモークテストは `must not produce a snupkg` で exit 1 する（＝退行を実際に捕捉することを実証）。復元後グリーン。
- **スコープ外（記録）:** 外部依存（Kiota 等）のバージョン下限は NuGet 監査ゲート（build-test ジョブ）が担当。パッケージ version 値そのものはレイアウト非依存のため未検証。将来パッケージ横断 SemVer 分岐時に依存バージョン照合を再検討。

## 結論

`Line.OpenApi.Bot` = 3 役すべて PASS。pack スモークテスト = 2 役（code / test-arch）再ゲート PASS。非ブロッキング CONCERNS（自動回帰欠如）は本セッションで解消。**GO 推奨、人の go/no-go 待ち。**
