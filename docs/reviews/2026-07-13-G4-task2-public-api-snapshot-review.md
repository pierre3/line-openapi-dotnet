# G4 タスク② 公開 API 表面 snapshot 回帰テスト ゲート レビュー記録

- 日付: 2026-07-13
- ゲート: **G4 リリース前 / タスク②（公開 API 表面の snapshot 回帰テスト）**（担当: テスト・アーキレビュアー。`docs/REVIEW-WORKFLOW.md` §ゲート定義）
- 対象:
  - 新規 `poc/tests/Line.Poc.Tests/PublicApiSnapshotTests.cs`
  - 新規 `poc/tests/Line.Poc.Tests/PublicApi/{Line.Core,Line.ChannelAccessToken,Line.Messaging}.approved.txt`
  - 変更 `poc/tests/Line.Poc.Tests/Line.Poc.Tests.csproj`（`PublicApiGenerator` 11.5.4 追加）
  - 変更 `.gitignore`（`*.received.txt` 追跡除外）
- 方式: テスト・アーキレビュアーをサブエージェントで実行 → 指摘反映 → 再テスト。
- **最終 go/no-go は人（小林さん）**。

## 背景 / 設計意図

設計 §8「回帰の baseline は公開 API 表面（public 型/シグネチャ）に snapshot 対象を限定し、内部生成差分のノイズを避ける」・§10「回帰: 公開 API 表面の snapshot テスト」を実装。`PublicApiGenerator` で手書き公開型のみを対象にし、Kiota 生成型（名前空間に `Generated` セグメントを含むもの）はトップレベルから除外する。ただし手書き型の公開シグネチャに露出する生成型（例: `MessagingClient.Api` → `Generated.Api.MessagingApiClient`）は公開契約の一部として検知対象に残す。

承認方式は ApprovalTests 準拠（approved.txt と突合、不一致で received.txt を書き出して FAIL、`[CallerFilePath]` で承認ファイルを解決）。

## 判定

**PASS**（軽微な改善余地あり・いずれも非ブロッキング）。手書き public 型はすべて Line.Core / Line.ChannelAccessToken / Line.Messaging の 3 アセンブリに存在し、この 3 つで 100% カバー。Line.Messaging.Webhook / Line.Liff は手書きソース 0 件のため対象外が正当。誤変更検知は approved 改変 → 該当テスト FAIL を実地確認済み（その後 revert）。

## 指摘と対応

### Medium: 新規パッケージへの前方ガードが無い — **修正済み**
- 事象: snapshot 対象が 3 アセンブリのハードコード。将来 Webhook/Liff や新パッケージに手書き public API が追加されても、承認ファイルを追加し忘れれば無警告で保護漏れになる。
- 対応: 完全性ガード `All_Handwritten_Line_Assemblies_Are_Registered` を追加。テストが参照する `Line.*` アセンブリのうち「手書き public 型を持つのに未登録」を検知して FAIL させる。

### Low: `IsGenerated` の冗長条件（dead code）＋ `.Contains(".Generated")` の理論的誤検知 — **修正済み**
- 事象: 旧実装は `Contains(".Generated") || EndsWith(".Generated")` で第 2 節が常に dead。かつ `Line.Core.GeneratedHelpers` のような手書き名前空間を誤除外し得た。
- 対応: 名前空間を `.` 分割し `"Generated"` セグメントの完全一致で判定するよう変更。

### Medium: build/test 分離パイプラインでの `[CallerFilePath]` 依存 — **受容（現行 CI では非問題）**
- 事象: 同一チェックアウトでない CI（成果物だけ別ランナー実行等）へ移行すると承認ファイル解決に失敗し誤 FAIL の可能性。
- 判断: 設計 §9 の GitHub Actions は同一ジョブで build→test するため実害なし。将来分離時は approved を `CopyToOutputDirectory` + `AppContext.BaseDirectory` 解決へ切替の頭出しのみ記録し、本タスクでは対応しない。

### Low: read-only チェックアウトでの received 書き出し — **受容**
- 事象: 差分時に `File.WriteAllText` をソース配下へ行うため、read-only CI では親切メッセージの代わりに IOException。fail 方向は安全なので実害小。

### 情報（scope 外）
- `src/**/obj` に net8.0 / netstandard2.0 の旧 obj 成果物が残存（net10 単一化前の残骸）。本 PR と無関係。別途 `dotnet clean` 相当で掃除推奨。

## テスト結果

- **38/38 合格・警告なし**（本タスク前は 34: snapshot 3 = Theory 3 ケース + 完全性ガード 1 を追加）。as-of 2026-07-13。

## 結論

設計 §8/§10 の意図を満たす。GO 推奨、人の go/no-go 待ち。
