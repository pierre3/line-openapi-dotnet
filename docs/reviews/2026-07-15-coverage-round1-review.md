# カバレッジ拡充 ラウンド 1 実装ゲートレビュー — Insight / Module / Shop

- **日付:** 2026-07-15
- **ブランチ:** `feat-coverage-round1`（`main` @ `0996704` から分岐）
- **対象:** 未取り込み OpenAPI spec のうち易 3 本の取り込み
  - `Line.OpenApi.Insight`（insight.yml・7 GET・api.line.me・Bearer・JSON）
  - `Line.OpenApi.Module`（module.yml のみ・4 ops・同上。**module-attach は見送り**）
  - `Line.OpenApi.Shop`（shop.yml・1 op・同上）
- いずれも単一ホスト・純生成＋薄ファサード（`Line.OpenApi.Liff` パターン）。facade + DI（2 オーバーロード・冪等）+ Options。

## 検証

- ビルド 0 警告 / テスト **217**（+ isolation 1 + tools 72）全緑 / pack **10 パッケージ**（9 code + 1 meta）PASS / docfx 0 warnings / NuGet 監査クリーン。
- 公開 API snapshot 3 本（Insight/Module/Shop）を approved 化。verify-packages を 10 パッケージ・内部依存グラフ（各 → Core のみ）で更新。
- README 英日・docfx.json・概念記事 英日（insight/module/shop）・manual TOC/index 更新。

## 3 役ゲート結果

| 役 | 判定 | 要点 |
|---|---|---|
| code-reviewer | **PASS** | Liff パターンの忠実な複製。低位のみ（slnx 並び順・IsNullOrEmpty 慣習・DateOnly 便宜オーバーロード提案）。slnx 並び順は反映済み。 |
| security-reviewer | **PASS** | トークンはホストゲート付き Bearer（既定 api.line.me のみ）。秘匿情報のログ・埋め込みなし。新ホスト定数なし。module-attach 見送りで manager.line.biz 不到達。 |
| test-arch-reviewer | **CONCERNS（非ブロッキング）** | アーキ・snapshot ガード・pack 契約は健全。指摘は網羅性のみ。 |

### test-arch 指摘の反映（コミット `ff2dc25`）

1. **（要対応・反映済）負側ホストゲートテスト**を 3 パッケージに追加（許可外ホストへトークン非付与＝Liff ベースライン・設計 §4.2 line 219 準拠）。
2. Insight `GetMessageEventAsync` の `requestId` クエリを HTTP 層で検証（反映済）。
3. Insight 引数ガードのカバレッジ拡充（反映済）。
4. Module `ReleaseChatControl` の `Content=null` を明示アサート（反映済）。

反映後テスト 209→217 全緑。

## 判定

- 3 役の実質判定 = **GO 推奨**（code/security PASS、test-arch CONCERNS は非ブロッキング指摘を全反映）。
- **人の go/no-go 待ち**（main マージ前）。

## スコープ確定

- module-attach（manager.line.biz / HTTP Basic / form+PKCE / パートナー限定 1 op）はユーザー判断で**今回見送り**。spec は追跡下に残さず（`f2d241a` で除去）、実装着手時に再取得。
- manage-audience（control/data 分離＋multipart）は **ラウンド 2** で別途実装・ゲート。
