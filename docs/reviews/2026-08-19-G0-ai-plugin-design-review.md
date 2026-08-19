# G0 設計ゲート レビュー結果 — AI ツール連携（`Line.OpenApi.Extensions.AI`）

- **ゲート:** G0（設計）
- **対象:** `docs/LINE-dotnet-AI-plugin-design.md`（ドラフト初版）
- **担当:** test-arch-reviewer / security-reviewer（サブエージェント・並行）
- **日付:** 2026-08-19
- **総合判定:** **CONCERNS（FAIL なし）** — 方向性は妥当。MUST 指摘を設計文書へ反映後に G1/実装着手可。
- **人の go/no-go:** 待ち

方向性（M.E.AI 主軸／操作コア抽出／安全機構第一級／一方向依存維持／別リリース系統）は両レビュアーとも妥当と確認。依存グラフは非循環・本体 ADR と非矛盾。ただし **①テスト計画の中核前提の事実誤認（挙動不変ではない）** と **②LLM 自律送信の安全機構が述語ベースで粗く broadcast/大量送信/injection を塞げない** の 2 点が主要 CONCERNS。

---

## テスト・アーキ観点（判定 CONCERNS）

### MUST（G0 通過前に設計文書を是正）
1. **「挙動不変リファクタ＝既存テストが回帰網」は成立しない。** 既存 Tools テストは `MessageService` を直接 `new` し `ResolvedCredentials` を渡して検証。core を「注入済み `MessagingClient` 受け」に変えると被テストシグネチャが壊れる。→ **Tools 側に `ResolvedCredentials` 薄アダプタを残し core は `MessagingClient` 受け**の二層構えでテスト温存を明記。
2. **結合点の列挙が不足。** ①に加え `RequireAccessToken()` が投げる `CredentialException`（exit 3）、および `MessageInputException` が**移設対象 `MessageJson` 内で発生**する点。core の入力例外契約を定義し Tools 側で exit 2/3 へ写像し直す計画を明記。
3. **static メモ化（code gate Medium#1）の維持義務と回帰網の不在。** 抽出後 Tools が呼び出し毎に新 `MessagingClient` を作るとサイレント退行。Tools 側メモ化 or `IHttpClientFactory` 管理の維持と退行検知策を計画に。
4. **pack-verify の一方向 ADR ガードに空く穴を「検討」でなく確定。** Tools 同様の明示除外 or 期待依存形の追記。
5. **`Extensions.AI` の公開 API snapshot を必須化。** `Tools.Core` の安定性契約（snapshot or 無保証宣言）を決着。現行 snapshot 対象は `src/**` のみで Tools 非対象。

### SHOULD
- broadcast は `RecipientPolicy` で塞げない＝独立許可スイッチ化＋テスト固定。multicast 配列適用テスト。
- DryRun 不変条件を core 語彙（transport 非接触・リクエスト 0 件）へ言い換え。
- M.E.AI.Abstractions の版ポリシー（Kiota ロックステップ外の新軸・最小版ピン）を明記。
- `AIFunction` メタデータは M.E.AI 版に脆い→版ピン＋寛容 assert 方針。
- write セット有効化を明示的選択に（`CreateReadOnly` 分離 or オプション必須）。
- §9.1 対案「共有を `MessageJson`＋DTO に絞る最小抽出」を比較検討。命名 `.Tools.Core` の紛らわしさ再考。

### 問題なし
- 依存グラフ非循環・逆依存なし。ADR-3 は本体「Core のみ依存」ADR と非矛盾。ADR-1/5・シークレット非露出は妥当。

---

## セキュリティ観点（判定 CONCERNS）

### MUST（G1 前に設計へ反映）
- **M1. `RecipientPolicy`（述語）は broadcast を構造的にカバー不能。** broadcast は宛先を取らず全友だち配信＝最大ブラスト半径。multicast の宛先数・メッセージ件数も述語では抑えられない。→ **操作コンテキスト（操作種別・宛先集合・件数）を受けるポリシー型へ格上げ**。broadcast は既定無効の別建て opt-in。
- **M2. 大量送信/コスト濫用・スパム制御が §5 に不在。** レート/回数/バジェット上限の責務（本層 or 呼び出し側ミドルウェア）を明記して決める。
- **M3. 安全ゲートが LLM 可視引数にならない不変条件を明記。** `AIFunctionFactory.Create` はメソッド引数からスキーマを自動生成するため、ゲートをうっかり引数にすると LLM がバイパス可能。ADR-4 に「ゲートはクロージャ束縛・引数に露出させない」を追記＋negative test（引数スキーマにゲートが存在しないこと）。
- **M4. prompt injection 経由の誤送信緩和（human-in-the-loop）が未設計。** 送信前に必ず呼ばれる同期/非同期フック（操作種別・宛先・メッセージを渡し承認/拒否）を第一級 API に。監査任せ（M.E.AI ミドルウェア＝任意）では保証がない。

### SHOULD
- 既定の安全性: 送信を明示 opt-in（`ReadOnly=true` 既定 or `EnableSending` 明示必須）。broadcast は別 opt-in。
- `RecipientPolicy` を非同期対応（`ValueTask<bool>`＋`CancellationToken`）。
- read 戻り値の PII（`ProfileInfo` の表示名/画像/ステメ/言語）がプロバイダ/ログへ渡る点をドキュメント化。監査ログにメッセージ本文が残る点も注記。
- M.E.AI.Abstractions の下限版ピン＋NuGetAudit ゲート、`.Abstractions` 以外を引き込まない方針を ADR に。
- 将来 `SetWebhookEndpoint`/content DL 追加時の SSRF/ファイル書き込み緩和を非スコープ節に先出し。
- `Tools.Core` 公開時は AI 層が要する最小 API のみ public（`WrapFlex` 等は internal）。

### 結論で明示の 3 点
- **タイミング攻撃:** 新規の署名検証を持たず新たな攻撃面なし＝問題なし。
- **トークン漏洩:** トークンは呼び出し側 `MessagingClient` に閉じ、引数/戻り値/例外に出ない。監査ログと read 戻り値 PII の要ドキュメント化（上記）。M3 が守られる限り低リスク。
- **ホスト誤送出:** R1 ホスト固定＋`AllowedHostsValidator` 継承。AI 層は任意 URL を受けない＝既存水準維持。将来 SSRF 面追加時のみ再評価。

---

## 両レビュアーの収束点（優先対応）

1. **宛先制約を述語からポリシー型へ格上げ＋broadcast 独立 opt-in**（test-arch SHOULD ＝ security M1、強い一致）。
2. **write（送信）有効化を明示的選択に・安全側既定**（test-arch SHOULD ＝ security SHOULD）。
3. **M.E.AI 版ポリシーを別軸で明記**（両者）。
4. **`Tools.Core` の公開表面最小化・命名再考**（両者）。

---

## 対応方針

MUST（両観点）を設計文書 rev.2 として反映済み（本レビューと同日）。反映点: 二層アダプタによるテスト温存・結合点補完（exit3/例外契約）・メモ化維持義務・pack-verify 確定・snapshot 必須化・ポリシー型格上げ・broadcast 別 opt-in・送信明示 opt-in・send-before フック（human-in-the-loop）・ゲート非引数不変条件・M.E.AI 版ポリシー・§9.1 最小抽出対案。SHOULD は文書へ反映または実装ゲート（G3/G4）へ持ち越し。

**次:** 人の go/no-go → GO なら §7 段階案の (2) コア抽出（挙動不変でない API 変更リファクタ・二層アダプタ）から着手。
