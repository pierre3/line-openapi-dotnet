# LINE .NET クライアント AI ツール連携 設計方針（G0 ドラフト）

**対象:** LLM tool-calling（Function Calling）向けの LINE 操作ツール群を .NET アプリへ組み込み可能にする新規パッケージ
**関連:** `docs/LINE-dotnet-client-design.md`（本体設計）、`docs/CLI-MCP-tool-spec.md`（既存 CLI/MCP ツール）、`docs/REVIEW-WORKFLOW.md`（ゲート運用）
**最終更新:** 2026-08-19（rev.2 — G0 レビュー指摘反映）
**ステータス:** ドラフト（G0 レビュー済み・人の go/no-go 待ち・コードは含まない）

## 変更履歴

- **rev.2 (2026-08-19):** G0 レビュー（`docs/reviews/2026-08-19-G0-ai-plugin-design-review.md`）の MUST 指摘を反映。主な変更: 抽出は「挙動不変」でなく**二層アダプタによる API 変更リファクタ**と是正（§3.2・§6）／結合点を補完（exit 3 `CredentialException`・`MessageJson` 内の入力例外契約、§3.2）／static メモ化の維持義務を明記（§3.2）／pack-verify の一方向 ADR ガードを確定（§3.4）／`Extensions.AI` の公開 API snapshot を必須化（§6）／宛先制約を述語から**ポリシー型へ格上げ**＋broadcast 独立 opt-in（§5）／送信を**明示 opt-in・安全側既定**（§4・§5）／**送信前フック（human-in-the-loop）** を第一級 API 化（§5）／安全ゲートは LLM 可視引数にしない不変条件（§5・ADR-4）／M.E.AI 版ポリシーを別軸で明記（§8 ADR-6）。
- **rev.1 (2026-08-19):** 初版ドラフト。

---

## 1. 目的とスコープ

`Line.OpenApi.*` ライブラリを **LLM の tool（関数）としてラップ**し、Semantic Kernel をはじめとする .NET の AI オーケストレーションから、LLM が自律的に LINE を操作できるようにする。

想定ユースケース（旗艦）:

> ユーザー「佐藤さん（U12345）に『明日 10 時から会議』と LINE して」→ LLM が `line_message_push` ツールを選択、`to`/`text` を抽出して実行。

### スコープ（初期）

- メッセージ送信系: `push` / `multicast` / `broadcast` / `reply`
- Bot 照会系（読み取り）: `getProfile` / `getBotInfo` / `getQuota`
- dry-run（送信せず型検証）

### 非スコープ（初期）

- リッチメニュー切替・メディア送信（バイナリ）・LIFF/Login/Insight 等の他シーン → 有用性確認後に段階追加
- CLI/MCP の再実装（既存 `Line.OpenApi.Tools` が担う。§3 参照）
- **⚠️ 将来スコープ拡大時の注意（G0 R5）:** 初期スコープは送信＋read 照会に限定し、任意 URL 書き込み（`SetWebhookEndpoint` 等）・content ダウンロード（ファイル書き込み）を**含まない**（安全）。将来これらを追加する際は、MCP の `webhook replay` ループバック限定/`--allow-remote-replay` と同等の **SSRF 緩和**、およびファイルパス書き込み制御が必要。

---

## 2. 抽象化対象の決定：Microsoft.Extensions.AI 主軸

提案元は `Microsoft.SemanticKernel` の `[KernelFunction]` 直結だったが、**`Microsoft.Extensions.AI`（`AIFunction` / `AIFunctionFactory.Create` + 標準 `System.ComponentModel.DescriptionAttribute`）を主軸**とする。

- **理由:** M.E.AI は .NET の tool-calling の下位標準で、Semantic Kernel もこれをネイティブに消費する。M.E.AI を対象にすれば SK でも、M.E.AI を用いる任意のフレームワークでも使える。依存が軽く net10 と相性が良い。
- **SK 固有連携** が必要になった場合のみ、薄い `Line.OpenApi.SemanticKernel` を別途追加（初期は作らない）。
- SK は API 変動が激しいため、安定した net10 ライブラリ本体と密結合させない。

### 既存 MCP サーバとの棲み分け（重複ではない）

`Line.OpenApi.Tools` は既に MCP サーバとして同種の操作を LLM に公開済み。本パッケージは**ホスティングモデルが異なり補完関係**にある。

| | MCP サーバ（既存 Tools） | 本パッケージ（新規） |
|---|---|---|
| 形態 | 別プロセス（`dotnet tool`） | アプリ内 in-process C# オブジェクト |
| 消費側 | Claude Desktop 等の MCP クライアント | SK/M.E.AI で組んだ自作 .NET アプリ |
| 用途 | 開発者の手元・IDE 連携 | プロダクトに組み込む AI エージェント |

SK は MCP サーバを間接消費もできるが、in-process の方が transport オーバーヘッドとデプロイが軽い。

---

## 3. パッケージ構成と依存グラフ（操作コアの抽出）

### 3.1 現状の重複の実態

AI ツール層が必要とする実体は、**既に `Line.OpenApi.Tools/Services/MessageService.cs` にほぼ全部ある**:

- `PushTextAsync`/`PushRawAsync`/`MulticastAsync`/`BroadcastAsync`/`ReplyAsync`/`GetProfileAsync`/`GetBotInfoAsync`/`GetQuotaAsync`
- `ValidateMessagesAsync`（dry-run）
- LLM に返す**平坦 DTO**（`SendResult`/`ProfileInfo`/`BotInfo`/`QuotaInfo`。Kiota 生成モデルを露出しない）
- `MessageJson.ParseMessagesAsync`（非配列/空/不正 JSON の穴を塞ぐ非自明ロジック）

これを AI 側にコピーすると ~200 行の非自明ロジックを 2 箇所で保守することになり、**CLI・LLM・アプリで同じ結果形状を共有すべき**という要件に反して乖離しやすい。2 人目の消費者が現れた今、rule-of-three 的に抽出が正当化される。

### 3.2 現状のサービス層はドロップイン再利用できない（＝挙動不変ではない）

CLI/MCP ホスト固有の結合があり、抽出時に切断する。**重要（G0 是正）: これは「挙動不変リファクタ」ではなく、被テストシグネチャと例外型を変える API 変更リファクタ**である。テスト温存のため**二層構え**を採る（§6 参照）:

> **二層アダプタ方針:** core は「注入済み `MessagingClient` を受け、平坦 DTO を返し、プレーンな入力例外を投げる」ホスト中立層。Tools 側に `ResolvedCredentials` → `MessagingClient` 変換と exit-code 写像を行う**薄アダプタ**を残す。これにより既存 Tools テストの多くを温存しつつ core を切り出す。

切断する結合点（G0 で補完）:

1. **`ResolvedCredentials`（config/env 解決）** — 利用者は `MessagingClient`／トークンを直接渡したい。→ core は注入された `MessagingClient` に対して動作。資格情報解決は Tools 薄アダプタに残す。
   - **補完:** `credentials.RequireAccessToken()` は未設定時に `CredentialException`（**exit 3**）を投げる。exit-code 意味論は exit 2 だけでなく **exit 3 も**結合しており、両方 Tools 薄アダプタ側に残す。
2. **static クライアントメモ化辞書** — 長寿命 MCP サーバ向け（トークン別に `MessagingClient`/HttpClient を再利用＝code gate Medium#1 対応）。DI 管理のアプリでは利用者が自分の `MessagingClient` を持つため core では持たない。
   - **⚠️ 維持義務（G0 MUST）:** メモ化を core から外すと、Tools が呼び出し毎に新 `MessagingClient` を作った瞬間 Medium#1 がサイレント退行する。→ **Tools 薄アダプタ側でトークン別メモ化（または `IHttpClientFactory` 管理）を維持**する。可能なら「同一トークンでクライアント同一性」を観測する回帰テストを追加。
3. **`MessageInputException` = exit code 2** — CLI 終了コード意味論。**補完（G0 MUST）:** この例外は移設対象の `MessageJson.ParseMessagesAsync` の**内部で発生**する（＝「共有したい非自明ロジック」と「exit-code 例外型」が同じメソッドに絡む）。機械的移設では切れない。→ **core 側で入力例外契約を明示定義**（新 core 例外型 `LineAiInputException` 等、または `ArgumentException`/`FormatException`）し、Tools 薄アダプタの `catch` を新契約へ張り替えて exit 2/3 に写像し直す。

### 3.3 依存グラフ（tools 支援ティア）

published ライブラリの「各パッケージは Core のみ依存」ADR を壊さないため、抽出コアは**ツール支援ティア**として `/tools` 配下に置く（published usage-scene パッケージには混ぜない）。

```
Line.OpenApi.Tools.Core   （新規・ホスト中立の操作コア）
   ├─ 操作: 注入された MessagingClient に対し push/reply/... を実行
   ├─ 平坦 DTO: SendResult / ProfileInfo / BotInfo / QuotaInfo（現状から移設）
   ├─ MessageJson: JSON 解析＋ガード（プレーン例外へ変更）
   └─ 依存: Line.OpenApi.Messaging（初期スコープ）
        ↑                              ↑
Line.OpenApi.Tools              Line.OpenApi.Extensions.AI（新規）
（CLI/MCP: config 解決 +        （AIFunction 注釈 + read-only/dry-run
  exit-code 整形を自層に）          ゲートを被せる薄いシェル）
```

- `Line.OpenApi.Extensions.AI` は NuGet 公開する（tools 支援ティアだが利用者向け）。依存は `Line.OpenApi.Tools.Core` + `Microsoft.Extensions.AI.Abstractions`。
- `Line.OpenApi.Tools.Core` を NuGet 公開するかは要判断（§9 オープン論点）。初期は「公開する」前提（AI パッケージが依存するため）。

### 3.4 リリース系統

`Line.OpenApi.Tools` と同系統の**別サイクル・独立採番**。タグ例 `ai-v*`。ライブラリ本体（`v*`）・Tools（`tools-v*`）とは分ける。`release.yml` に publish ジョブを追加（Trusted Publishing / OIDC は既存踏襲）。

**pack-verify の一方向 ADR ガード（G0 MUST・確定）:** 現行 `scripts/verify-packages.ps1` は「published パッケージは Core のみ依存」を厳密照合する不変条件を守っている。`Tools.Core`/`Extensions.AI` は `Messaging` に依存するため、これらを**ライブラリ本体の pack-verify 対象から明示除外**し（Tools の `ExcludeToolFromPack` と同方式）、別系統として**両パッケージの期待依存形（`Tools.Core`→`Messaging`、`Extensions.AI`→`Tools.Core`＋`Microsoft.Extensions.AI.Abstractions`）を専用に照合**する。これにより本体の一方向 ADR ガードの意味を保ちつつ AI 系の依存も検証する。

---

## 4. 公開 API 案

**送信は明示 opt-in・安全側既定（G0 反映）:** 引数なしの既定生成は **read-only セット**（照会系のみ）を返す。送信系ツールを有効化するには送信オプションを**明示的に**渡す。これにより「無警告で LLM が任意 userId へ送れる初期状態」を避ける。

```csharp
// (a) 既定＝read-only。照会系ツール（getProfile/getBotInfo/getQuota）のみ。
IReadOnlyList<AIFunction> readTools = LineMessagingAiTools.CreateReadOnly(messagingClient);

// (b) 送信を有効化する場合は明示的にオプションを渡す。
IReadOnlyList<AIFunction> tools = LineMessagingAiTools.Create(
    messagingClient,                 // 利用者が DI で構築した MessagingClient を注入
    new LineAiToolOptions
    {
        EnableSending  = true,       // push/multicast/reply を有効化（既定 false）
        AllowBroadcast = false,      // broadcast は最大ブラスト半径＝独立 opt-in（既定 false）
        DryRun         = false,      // true なら送信せず型検証のみ
        SendPolicy     = myPolicy,   // 送信ポリシー（操作種別×宛先×件数）を評価（§5）
        BeforeSend     = myApproval, // 送信前フック（human-in-the-loop / 監査）（§5）
    });

// Semantic Kernel への組み込み例
var kernel = Kernel.CreateBuilder()
    .AddOpenAIChatCompletion("gpt-4o", apiKey)
    .Build();
kernel.Plugins.AddFromFunctions("Line", tools);
```

- 各ツールは `AIFunctionFactory.Create` + メソッド/引数への `[Description]` で生成。名前は既存 MCP と揃え `line_message_push` 等。
- 戻り値は §3.1 の平坦 DTO（LLM が読みやすい・シークレット非露出）。
- 生成 Kiota モデルは公開表面に一切出さない。
- **⚠️ 安全ゲート（`EnableSending`/`AllowBroadcast`/`DryRun`/`SendPolicy`/`BeforeSend`）は `LineAiToolOptions`＝開発者が生成時にクロージャ束縛する。ツールメソッドの引数にはしない**（LLM から上書き不可＝バイパス防止。§5・ADR-4）。

---

## 5. 安全設計（第一級）

LLM に**実ユーザーへの送信を自律実行**させるため、安全機構を設計の第一級要素とする。既存 MCP の姿勢（`--read-only`／`dryRun`／シークレット非露出）を踏襲しつつ、**MCP より一段高いリスク水準**（プロダクト組込の自律エージェント）に合わせて強化する。

1. **read/write 分離・送信明示 opt-in（安全側既定）** — 既定 `CreateReadOnly` は照会系のみ。送信は `EnableSending=true` の明示が必要。broadcast は `AllowBroadcast=true` の**独立 opt-in**（下記 3）。
2. **dry-run** — `DryRun=true` で送信せず型検証（`ValidateMessagesAsync` 相当）に短絡。
   - **core 語彙での不変条件（G0 是正）:** core には資格情報解決ステップが無いため「解決前で分岐」ではなく「**注入 `MessagingClient`／transport に一切触れない＝送信リクエスト 0 件**」を保証とする。§6 で transport スタブにより検証。
3. **送信ポリシー `SendPolicy`（述語からポリシー型へ格上げ・G0 MUST）** — `Func<string,bool>` の宛先述語は **broadcast（宛先なし・全友だち配信＝最大ブラスト半径）を構造的に表現できず**、multicast の宛先数・メッセージ件数も抑えられない。→ **操作コンテキストを受けるポリシー型**にする:
   - 受け取る情報: 操作種別（push/multicast/reply/broadcast）・宛先集合・メッセージ件数。
   - 戻り値: allow/deny（**非同期対応** `ValueTask<bool>`＋`CancellationToken`。許可リストが DB/リモート参照になりうるため）。
   - broadcast は「宛先なし」を明示的に判定対象にし、かつ 1 の `AllowBroadcast` で二重に無効化可能。
   - **送信量/コスト濫用の責務（G0 M2）:** 1 回の呼び出しで送る宛先数・メッセージ件数の上限は `SendPolicy` で表現可能にする。**レート/累積回数の制限は本層の責務外**とし、呼び出し側ミドルウェア（M.E.AI パイプライン等）に委ねる旨を README で必須ガイダンス化する。
4. **送信前フック `BeforeSend`（human-in-the-loop・G0 M4）** — 送信操作の直前に**必ず経路上通る**同期/非同期コールバック（操作種別・宛先・メッセージを渡し承認 or 拒否）。prompt injection 由来の「正当な宛先へ悪意ある内容」を `SendPolicy`（宛先制約）では塞げないため、内容確認・承認と監査を「任意でなく必須経路」として提供する。既定は no-op（許可）。
5. **安全ゲートを LLM 可視引数にしない不変条件（G0 M3）** — `AIFunctionFactory.Create` はメソッド引数から自動で JSON スキーマを起こす。ゲート（`EnableSending`/`AllowBroadcast`/`DryRun`/`SendPolicy`/`BeforeSend`）を誤ってツール引数にすると LLM が上書き＝バイパスできる。→ **全ゲートは生成時にクロージャ束縛し、生成 `AIFunction` の引数スキーマに現れない**ことを不変条件とし、§6 で negative assertion により固定。
6. **シークレット非露出** — チャネルアクセストークンは戻り値・ツール説明・例外メッセージに出さない。`getBotInfo` 等は非機密フィールドのみ返す（現行 DTO が既に準拠）。
7. **ホスト制限** — 送出先は `MessagingClient` が制御系/データ系ホストに固定（R1）＋`AllowedHostsValidator` 継承。AI 層で任意 URL を受けない。
8. **監査容易性・PII 注意** — ツール実行は M.E.AI のミドルウェア／ロギングで捕捉可能（本層は副作用を隠さない）。**ドキュメント化必須（G0 R3）:** read ツール戻り値（`ProfileInfo` の表示名/画像/ステメ/言語＝**個人情報**）は LLM プロバイダへ送られ会話履歴/ログに残る。監査ログにツール引数を残すとメッセージ本文（PII 含みうる）も残る。DTO 自体は非機密だが、この情報フローを README で明示する。

---

## 6. テスト方針

- **コア抽出は API 変更リファクタ（G0 是正）** — 「挙動不変で既存テストが全緑」は成立しない（既存 Tools テストは `MessageService` を `ResolvedCredentials` 受けで直接検証しているため、`MessagingClient` 受けへの変更でシグネチャが壊れる）。→ **二層アダプタ**（§3.2）で、Tools 側に `ResolvedCredentials` 薄アダプタを残して既存テストの多くを温存しつつ core を切り出す。抽出後に全テスト全緑を確認。
- **静的メモ化の退行検知（G0 MUST）** — Tools 薄アダプタで「同一トークン → 同一 `MessagingClient` インスタンス」を観測する回帰テストを追加し、Medium#1 の退行をガード。
- **AI 層**:
  - `CreateReadOnly`／`EnableSending=false` 時に**送信系ツールが列挙されない**ことを検証。`AllowBroadcast=false` 時に broadcast ツールが出ないことを検証。
  - `DryRun=true` 時に**transport に触れない（送信リクエスト 0 件）**ことを transport スタブで assert（core 語彙の不変条件）。
  - **negative assertion（G0 M3）:** 生成 `AIFunction` の引数スキーマに安全ゲート（`EnableSending`/`DryRun`/`SendPolicy`/`BeforeSend` 等）が**存在しない**ことを検証。
  - `SendPolicy` が deny 時／`BeforeSend` が拒否時に**送信前で弾く**ことを検証。broadcast が `SendPolicy` で拒否可能なこと・multicast の宛先配列に述語が全適用されることを検証。
  - `AIFunction` の name/description/引数スキーマは M.E.AI 版で揺れうるため、**厳密 snapshot は避け版ピン＋寛容 assert**（name・必須引数の存在確認等）に留める。
  - HTTP 正常系は既存 Tools と同じスタブハンドラ方式を流用。
- **公開 API snapshot は必須（G0 MUST）** — `Extensions.AI` は利用者向け公開表面なので `PublicApiGenerator` で snapshot 化（現行 snapshot 対象は `src/**` のみで Tools 非対象のため、AI 系は別途）。`Tools.Core` は公開する場合、安定性契約を明示（snapshot 化 or 「実装詳細・無保証」宣言のいずれかに決着）。

---

## 7. 段階案（安全順）

1. **G0 設計**（本書）を 4 役ゲートへ。
2. `MessageService`/`MessageJson`/平坦 DTO を `Line.OpenApi.Tools.Core` へ機械的に分割（挙動不変）。Tools は薄層で config 解決＋exit-code を被せ直す。テスト全緑を確認。
3. `Line.OpenApi.Extensions.AI` をコア上に実装（初期スコープ）。3 役ゲート（code/security/test-arch）。
4. サンプル（`samples/`）にエンドツーエンドのエージェント実例を追加、README で訴求。

---

## 8. ADR（今回の設計判断）

- **ADR-1:** 抽象化は `Microsoft.Extensions.AI` 主軸（SK 直結にしない）。理由 §2。
- **ADR-2:** 操作ロジックを `Line.OpenApi.Tools.Core` に抽出し、Tools と AI で共有。CLI/MCP ホスト固有の結合（資格情報解決・static メモ化・exit-code 例外）はコアに持ち込まない。理由 §3。
- **ADR-3:** コアは `/tools` 支援ティアに置き、published usage-scene パッケージの「Core のみ依存」ADR を保持。
- **ADR-4:** LLM 送信の安全機構を第一級要素とする（送信明示 opt-in・安全側既定・`SendPolicy` ポリシー型・broadcast 独立 opt-in・`BeforeSend` human-in-the-loop・シークレット非露出）。**安全ゲートは生成時クロージャ束縛し、ツール引数スキーマに露出させない**（LLM バイパス防止）。理由 §5。
- **ADR-5:** リリースは Tools と同系統の別サイクル（`ai-v*`）。
- **ADR-6:** `Microsoft.Extensions.AI.Abstractions` は Kiota ロックステップ**外**の新規外部依存軸。`Directory.Build.props` で下限版を集中ピンし、NuGetAudit ゲート（restore の warnings-as-errors）で推移的脆弱性を検知。`.Abstractions` 以外（実装/DI パッケージ）は引き込まない。理由: M.E.AI は版動が速いため本体安定性から隔離。

---

## 9. オープン論点（G0 レビュー後の状態）

1. **コア抽出の粒度**（G0 で対案提示・**未決**）— 現案は `MessageService` 全体を移す。test-arch の対案: **共有価値が高いのは `MessageJson`（解析＋非自明ガード）＋平坦 DTO** であり、操作メソッド自体は `client.Api.V2.Bot.Message.Push.PostAsync(...)` の 1 行パススルー。**「JSON 解析＋DTO」だけを共有プリミティブとして抽出**すれば結合の大半を回避でき、AI 層は `Messaging` に薄く直接乗せられる。→ **実装着手時（段階 2）に最小抽出案 vs 全体抽出案を最終判断**。配置は tools 支援ティアで両者合意。
2. **`Tools.Core` の NuGet 公開・命名**（**要判断**）— AI パッケージが依存するため公開が要る。公開する場合 **AI 層が要する最小 API のみ public、他は internal**（`WrapFlex` 等）。命名 `.Tools.Core` は「CLI 内部」を連想させ紛らわしい懸念あり（対案 1 で公開範囲を絞れば緩和）。安定性契約（snapshot or 無保証宣言）を §6 のとおり決着。
3. ~~抽出のタイミング~~（**決着**）— 段階 2 で先行。ただし「挙動不変」ではなく二層アダプタによる API 変更リファクタ（§3.2・§6）。
4. ~~安全既定値~~（**決着**）— 送信は明示 opt-in・既定 read-only（§4・§5・ADR-4）。broadcast は別 opt-in。
5. ~~宛先制約フックの形状~~（**決着**）— 述語ではなくポリシー型（操作種別×宛先×件数・非同期）＋送信前フック（§5）。
6. **パッケージ名**（**要確認**）— `Line.OpenApi.Extensions.AI` で妥当か（`Microsoft.Extensions.AI` 命名に寄せた）。

---

## 10. G0 通過基準（本ゲート）

- 設計判断（特に ADR-2 コア抽出・ADR-4 安全設計）に致命的な穴がないこと。
- 依存グラフが本体 ADR（一方向・Core 集約）と矛盾しないこと。
- テスト計画（抽出の回帰網・AI 層のゲーティング検証）が現実的であること。
