# ゲートレビュー記録: `Line.OpenApi.Extensions.AI`（段階2＋段階3 実装）

**日付:** 2026-08-19
**ブランチ:** `docs/g0-ai-plugin-design`（未コミット作業ツリー）
**対象:** AI ツール連携パッケージの実装（設計 `docs/LINE-dotnet-AI-plugin-design.md` rev.4 の段階2 共有ソース化＋段階3 本体）
**ゲート:** 3 役（code / security / test-arch）をサブエージェントで並行実行

## 総括

| 役 | 判定 | BLOCKING |
|---|---|---|
| code-reviewer | **PASS** | なし |
| security-reviewer | **PASS** | なし |
| test-arch-reviewer | **CONCERNS** | なし |

**BLOCKING ゼロ。** 指摘はすべて Low〜中低かつ非ブロッキング。主要な実効指摘（release ジョブのバージョン整合）を含め反映済み。**GO 推奨、人の go/no-go 待ち。**

検証: 全ソリューション ビルド 0 警告 / テスト AI 26・Tools 83・ライブラリ 264・Isolation 1 = 全緑 / pack-verify（12 ライブラリ契約維持＋AI 専用照合 PASS）/ NuGet 監査クリーン。

## code-reviewer = PASS

健全性を確認: ゲートパイプライン（DryRun 短絡→SendPolicy→BeforeSend→送信の順序・非送信保証・二重 parse 回避）、ゲートの LLM 非可視化（クロージャ束縛）、シークレット/本文の非漏洩、共有ソース internal 化の整合（CS0050 非発生・重複コンパイルなし）、delegate キャストでの `[Description]` 保持、公開 API 表面の明快さ。

指摘（すべて Low・反映状況）:
- DryRun がゲートを skip する挙動が doc 未記載 → **反映**（`LineAiToolOptions.DryRun` の XML doc に追記）。
- `ReadOnly_Produces_Only_Read_Tools` が superset のみで弱い → **反映**（厳密一致 `Assert.Equal` へ強化＋説明非空 assertion 追加）。
- `[Description]` 生存の明示検証なし → **反映**（description 非空 assertion 追加）。
- 送信ツール戻り値が `Task<object>`（異種） → 許容（CLI/MCP 同方針）。
- Tools 側 public record（`ContentResult` 等）残存 → 非パッケージゆえ実害なし・任意整理（未変更）。

## security-reviewer = PASS

MCP より一段高いリスク水準で点検し、設計 §5・ADR-4 の不変条件が実装で守られていることを確認:
- 送信は明示 opt-in・安全側既定（`CreateReadOnly`/引数なしは read-only、broadcast は二重 opt-in）。
- 安全ゲートは AIFunction 引数スキーマ非露出＝バイパス不可（negative assertion テスト妥当）。
- DryRun/拒否時の非送信保証（`ExplodingHandler` で送信 0 件を実証）。
- チャネルアクセストークンは AI 層に露出せず、戻り DTO・説明・例外に出ない。
- 送出先は `MessagingClient` のホスト固定＋`AllowedHostsValidator` 継承に委譲。任意 URL・content DL・path 書き込みは初期スコープ外（G0 R5 準拠）。
- 依存は `Messaging`＋`.Abstractions` 10.9.0 の 2 本ちょうど（実装/DI 非引き込み）。

指摘（Low・反映）:
- `LineSendRefusedException.Context.MessagesJson` 経由で本文が監査ログに残りうる旨のドキュメント明示（設計 §5.8 の文書化要件） → **反映**（README 安全モデル節に PII フローを明記）。

## test-arch-reviewer = CONCERNS（非ブロッキング）

ADR 整合（PASS）: 公開依存 2 本ちょうど・pack-verify の AI 専用照合が依存集合を厳密一致で検証・ライブラリ 12 パッケージ契約不変・共有ソースは NuGet 依存辺を作らない・snapshot に共有 internal DTO 非表出。internal 化の回帰網（PASS）・公開 API snapshot（PASS）。

主要 CONCERN（中低・反映）:
- **`release.yml` の `publish-ai` ジョブの `Test` に `--no-build` 欠落** → テストが AI アセンブリを `-p:Version` 無しで再ビルドし、`Pack --no-build` が版無しアセンブリを梱包＝`AssemblyVersion`/`FileVersion` がパッケージ版と desync（ライブラリジョブが設計コメントで明示的に避けている事象）。**反映**: テストプロジェクトを `-p:Version` 付きでビルド → `dotnet test --no-build` → `pack --no-build` に再構成（ライブラリジョブに整合）。

テスト穴（Low・反映）:
- read ツール（bot_info/quota/profile）の HTTP 正常系・DTO 写像が未テスト → **反映**（`TransportAndContextTests` に追加）。
- multicast/reply/broadcast の空ボディ経路が transport 未確認 → **反映**（endpoint theory 追加）。
- push の `SendPolicy` コンテキスト `Recipients=[to]` 未アサート → **反映**（追加）。
- DryRun がゲートより前に短絡する挙動が未固定 → **反映**（回帰テスト追加＋doc 追記）。

補足（低・任意・未変更）: `publish-ai` の Restore 明示性、AI 専用 README の是非（現状ルート README に AI 節あり）。

**（スコープ外の既知事項）** 同じバージョン desync パターンは既存 `publish-tool` ジョブにも潜在（本サイクルのスコープ外・別途要検討）。

## 反映後の最終状態

- ビルド 0 警告 / テスト 全緑（AI 26・Tools 83・ライブラリ 264・Isolation 1） / pack-verify PASS / 監査クリーン。
- **GO 推奨、人の go/no-go 待ち（未コミット）。**
