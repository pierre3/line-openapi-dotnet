# ゲートレビュー記録: `samples/Line.OpenApi.Samples.Ai`（AI ツールエージェント サンプル）

**日付:** 2026-08-19
**ブランチ:** `docs/g0-ai-plugin-design`
**対象:** `Line.OpenApi.Extensions.AI` の end-to-end 実演サンプル（設計 `docs/LINE-dotnet-AI-plugin-design.md` 段階4）。コミット `3c29531`＋本レビュー反映。
**ゲート:** 3 役（code / security / test-arch）をサブエージェントで並行実行

## 総括

| 役 | 判定 | BLOCKING |
|---|---|---|
| code-reviewer | **PASS** | なし |
| security-reviewer | **PASS** | なし |
| test-arch-reviewer | **PASS**（CONCERNS は低〜情報レベル・非ブロッキング） | なし |

**BLOCKING ゼロ。** 指摘はすべて Low〜Info。実効的なもの（live モードでの承認自動化・変数名・catch/README の説明）を反映済み。**GO 推奨、人の go/no-go 待ち。**

検証: サンプル ビルド 0 警告・オフライン実行で 3 シナリオ（ツール検出／ALLOW→承認→送信／DENY→拒否）動作確認。既存テスト（AI 26・Tools 83・ライブラリ 264・Isolation 1）・公開 API snapshot は不変。

## code-reviewer = PASS

公開 API（`LineMessagingAiTools.Create`/`LineAiToolOptions`/`LineSendContext`/`LineSendRefusedException`）・`MessagingClient` の使い方は正しく、`ScriptedChatClient` は M.E.AI の `IChatClient` 契約に準拠、`FunctionInvokingChatClient` 配線（`ChatOptions.Tools`・`AsBuilder().UseFunctionInvocation()`）も妥当。「オフライン=dry-run でなくスタブ transport でゲートを実行」の判断が実装（`ShortCircuitAsync` の dry-run 短絡）と整合していることを確認。

指摘（Low・反映）:
- 変数名 `flex` が誤解を招く（中身はテキスト）→ **反映**（`textMessages` に改名）。
- `catch (LineSendRefusedException)` は現行 10.9.0 既定では到達しない（例外はモデルへ返る）→ **反映**（コメントに「現行既定はモデル返却経路、catch は版差異への防御」と明記）。
- 実 LLM 使用時は `IncludeDetailedErrors=false` で拒否理由がぼやける旨の補足 → **反映**（README 英日に追記）。

## security-reviewer = PASS

トークンは env のみ・ログ/出力/例外に非混入（ツール一覧はプロパティ名のみ表示）。offline は `AnonymousAuthenticationProvider`＋`StubTransport` の二重防御で egress・トークン送出とも発生し得ない。実送信は「トークン明示 かつ `--send`」の AND のみ。安全ゲート（`EnableSending`/`SendPolicy`/`BeforeSend`）は構築時設定で LLM 非露出、`AllowBroadcast` 既定 false＋`SendPolicy` が broadcast/空宛先を DENY＝二重に封じる正しい実演。誤用例・ゲートバイパスの記述なし。タイミング攻撃/トークン漏洩/ホスト誤送出＝いずれも非該当または安全側。

指摘（Low・反映）:
- live モードで piped 入力時に BeforeSend が無条件 auto-approve → 実送信が人手確認なしで通り得る → **反映**（`ApproveOnConsole` を live 時は auto-approve 抑止＝非対話時は refuse、offline のみ auto-approve）。

## test-arch-reviewer = PASS（CONCERNS 非ブロッキング）

アーキ整合: 実装パッケージ `Microsoft.Extensions.AI` はサンプル csproj のみ参照＝公開パッケージの「Messaging＋Abstractions の2本」原則を汚さない。`$(MicrosoftExtensionsAIVersion)` ロックステップ・`IsPackable=false`・slnx 登録・API 表面一致を確認。観測可能性: gate の ALLOW/DENY・承認フローが実出力で確認でき、決定的スクリプト＋非対話自動承認で再現的に完走。ドキュメント整合（README 英日と実挙動一致）。

指摘（低・情報・一部反映）:
- README 手順3 が DENY の二経路（モデル返却／`[refused]` catch）の片方のみ記載 → **反映**（README 英日に両経路＋実モデル注意を追記）。
- スクリプト固定ゆえ「モデルが失敗を理由づけした」ように見える余地／push のみ実演 → README が deterministic を明示済み・デモとして十分（未変更）。
- サンプルにユニットテスト無しは通例として許容（Login サンプル前例と同方針。安全ゲート本体は AI 26 件でカバー済み）。

## 反映後の最終状態

- サンプル ビルド 0 警告・オフライン 3 シナリオ動作。既存全テスト緑・公開 API snapshot 不変。
- **GO 推奨、人の go/no-go 待ち。**
