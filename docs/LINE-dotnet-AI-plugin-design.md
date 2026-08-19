# LINE .NET クライアント AI ツール連携 設計方針（G0 ドラフト）

**対象:** LLM tool-calling（Function Calling）向けの LINE 操作ツール群を .NET アプリへ組み込み可能にする新規パッケージ
**関連:** `docs/LINE-dotnet-client-design.md`（本体設計）、`docs/CLI-MCP-tool-spec.md`（既存 CLI/MCP ツール）、`docs/REVIEW-WORKFLOW.md`（ゲート運用）
**最終更新:** 2026-08-19（rev.2 — G0 レビュー指摘反映）
**ステータス:** ドラフト（G0 レビュー済み・人の go/no-go 待ち・コードは含まない）

## 変更履歴

- **rev.3 (2026-08-19):** G0 後の人の判断3点を確定。①**コア抽出は最小抽出案を採用**（`MessageJson`＋平坦 DTO のみ共有・操作は各消費者が薄く自前。§3.1-3.2）＝操作を共有しないことで CLI/MCP 結合・二層アダプタ・テスト破壊が丸ごと不要に。②**`Tools.Core` は NuGet 非公開＝共有ソース方式**（`tools/shared/` を両 csproj にリンクコンパイル。§3.3）。③**パッケージ名 `Line.OpenApi.Extensions.AI` で確定**。これに伴い依存グラフ・pack-verify・テスト計画・ADR-2/3 を更新。
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

AI ツール層が必要とする実体は、**既に `Line.OpenApi.Tools/Services/` にほぼ全部ある**:

- 操作: `PushTextAsync`/`PushRawAsync`/`MulticastAsync`/`BroadcastAsync`/`ReplyAsync`/`GetProfileAsync`/`GetBotInfoAsync`/`GetQuotaAsync`（`MessageService.cs`）
- `ValidateMessagesAsync`（dry-run）
- LLM に返す**平坦 DTO**（`SendResult`/`ProfileInfo`/`BotInfo`/`QuotaInfo`/`MessageValidationResult`。Kiota 生成モデルを露出しない）
- `MessageJson.ParseMessagesAsync`（非配列/空/不正 JSON の穴を塞ぐ非自明ロジック）

ただし G0 の観察どおり、**共有価値の内訳には大きな差がある**:

- **高い（＝共有すべき）:** `MessageJson`（解析＋非自明ガード。~50 行）と平坦 DTO（結果形状の契約。CLI・LLM・アプリで揃えたい）。
- **低い（＝共有不要）:** 操作メソッド本体は `client.Api.V2.Bot.Message.Push.PostAsync(...)` の**ほぼ 1 行パススルー**（`MessageService.cs` L47-89）。ここに CLI/MCP 固有の結合（`ResolvedCredentials`・static メモ化・exit-code 例外）が絡む。

### 3.2 決定: 最小抽出（`MessageJson` + 平坦 DTO のみ）を共有ソースで（G0 対案採用・rev.3）

**操作メソッドは共有しない。** 高共有価値の `MessageJson`＋平坦 DTO **だけ**を共有し、操作の 1 行パススルーは各消費者（Tools / AI）が薄く自前で書く。理由:

- 操作を共有しようとすると `ResolvedCredentials`・static メモ化・exit-code 例外という CLI/MCP 結合を全部引き受け、G0 で問題化した「二層アダプタ・API 変更リファクタ・テスト破壊」が必要になる。
- 対して 1 行パススルー（`Api.V2.Bot.Message.Push.PostAsync`）を AI 側で書き直すコストは些少。→ **結合を丸ごと回避できる。**

**帰結（G0 CONCERNS の大半が解消）:**

- **`MessageService.cs` は Tools 内にそのまま残す**（`ResolvedCredentials`・static メモ化・`MessageInputException`＝exit 2/3 も残る）。→ **既存 Tools テストは被テストシグネチャが変わらず温存**（G0 test-arch MUST①「挙動不変ではない」問題・「二層アダプタ」・メモ化退行の懸念がいずれも不要になる）。
- 移すのは `MessageJson`＋平坦 DTO のみ。これらは**共有ソース**として両アセンブリにコンパイルする（下記 §3.3）。名前空間を保てば Tools 側からは従来と同一の型に見えるため、`CliRuntime` の `catch (MessageInputException)` → exit 2 もそのまま機能する。

### 3.3 決定: 共有は NuGet パッケージでなく「共有ソース」（Tools.Core 非公開・rev.3）

`Tools.Core` を**独立した NuGet パッケージにはしない**（非公開）。代わりに `MessageJson`＋平坦 DTO を **共有ソース**（`tools/shared/` の `.cs` を各 csproj が `<Compile Include ... Link=... />` でリンク）として、`Line.OpenApi.Tools` と `Line.OpenApi.Extensions.AI` の**両方にコンパイル**する。

- published な `Extensions.AI` が非公開プロジェクトへ NuGet 依存する矛盾が生じない（＝共有ソースはアセンブリ依存を作らない）。
- 共有ソースは各アセンブリで別型になるが、Tools と AI は相互運用しない別消費者なので問題なし。**契約はソースレベルで同期**（同一ソースをコンパイル）＝乖離しない。
- 高共有価値ロジック（解析ガード・DTO 形状）の DRY は保たれ、低共有価値の 1 行パススルーだけが各消費者に重複する（許容）。
- 共有ソース内の入力例外型は名称を中立化（例 `LineMessageInputException`、または現行名を維持）。Tools 側は自アセンブリの同名型を catch して exit 2/3 に写像（現状維持）。AI 側は自アセンブリの同型を検証エラーとして扱う。

#### 依存グラフ

```
（共有ソース: tools/shared/  = MessageJson + 平坦 DTO。NuGet 非公開。両者にリンク）
        │ compile-link                         │ compile-link
        ▼                                       ▼
Line.OpenApi.Tools                     Line.OpenApi.Extensions.AI（新規・公開）
（CLI/MCP: MessageService 温存・        （AIFunction 注釈 + 安全ゲート。操作は
  ResolvedCredentials/メモ化/exit-code）   Messaging へ薄く自前パススルー）
        │ 参照                                   │ 参照
        ▼                                       ▼
  各 usage-scene パッケージ            Line.OpenApi.Messaging + Microsoft.Extensions.AI.Abstractions
```

- `Line.OpenApi.Extensions.AI` の**公開 NuGet 依存は `Line.OpenApi.Messaging` + `Microsoft.Extensions.AI.Abstractions` の 2 本のみ**（Tools.Core パッケージ辺は存在しない）。
- 配置は `/tools` 支援ティア（`Extensions.AI` は Messaging に依存＝published usage-scene の「Core のみ依存」ADR に該当しないため `/src` には置かない。ADR-3）。

### 3.4 リリース系統

`Line.OpenApi.Tools` と同系統の**別サイクル・独立採番**。タグ例 `ai-v*`。ライブラリ本体（`v*`）・Tools（`tools-v*`）とは分ける。`release.yml` に publish ジョブを追加（Trusted Publishing / OIDC は既存踏襲）。

**pack-verify の一方向 ADR ガード（G0 MUST・確定）:** 現行 `scripts/verify-packages.ps1` は「published パッケージは Core のみ依存」を厳密照合する。`Extensions.AI` は `Messaging`＋`Microsoft.Extensions.AI.Abstractions` に依存するため、**ライブラリ本体の pack-verify 対象から明示除外**し（Tools の `ExcludeToolFromPack` と同方式）、別系統として **`Extensions.AI` の期待依存形（`Messaging` + `Microsoft.Extensions.AI.Abstractions` の 2 本ちょうど）を専用に照合**する。`Tools.Core` は非公開＝パッケージ辺が無いため照合対象外（共有ソースはアセンブリ依存を作らない）。これにより本体の一方向 ADR ガードの意味を保ちつつ AI パッケージの依存も検証する。

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

- **最小抽出により既存 Tools テストは温存（rev.3）** — 操作（`MessageService`）を移設せず Tools に残すため、`ResolvedCredentials` 受けの被テストシグネチャも static メモ化も変わらない。→ G0 で問題化した「挙動不変ではない API 変更リファクタ」「二層アダプタ」「メモ化退行」の懸念は本方式では**発生しない**。共有ソース化する `MessageJson`＋DTO は名前空間を保てば Tools 側から同一型に見えるため、既存の `MessageJson`/DTO 参照・テストも影響を受けない（移設後に全テスト全緑を確認）。
- **AI 層**:
  - `CreateReadOnly`／`EnableSending=false` 時に**送信系ツールが列挙されない**ことを検証。`AllowBroadcast=false` 時に broadcast ツールが出ないことを検証。
  - `DryRun=true` 時に**transport に触れない（送信リクエスト 0 件）**ことを transport スタブで assert（core 語彙の不変条件）。
  - **negative assertion（G0 M3）:** 生成 `AIFunction` の引数スキーマに安全ゲート（`EnableSending`/`DryRun`/`SendPolicy`/`BeforeSend` 等）が**存在しない**ことを検証。
  - `SendPolicy` が deny 時／`BeforeSend` が拒否時に**送信前で弾く**ことを検証。broadcast が `SendPolicy` で拒否可能なこと・multicast の宛先配列に述語が全適用されることを検証。
  - `AIFunction` の name/description/引数スキーマは M.E.AI 版で揺れうるため、**厳密 snapshot は避け版ピン＋寛容 assert**（name・必須引数の存在確認等）に留める。
  - HTTP 正常系は既存 Tools と同じスタブハンドラ方式を流用。
- **公開 API snapshot は必須（G0 MUST）** — `Extensions.AI` は利用者向け公開表面なので `PublicApiGenerator` で snapshot 化（現行 snapshot 対象は `src/**` のみで Tools 非対象のため、AI 系は別途）。`Tools.Core` は**非公開＝共有ソース**なので独立した公開表面を持たず、安定性契約は不要（rev.3）。ただし AI 層が要さない共有ソース（例 `WrapFlex`）はアクセス修飾子を最小化。

---

## 7. 段階案（安全順）

1. **G0 設計**（本書）を 4 役ゲートへ。→ 完了（CONCERNS・MUST 反映済み・人の go/no-go = GO）。
2. **共有ソース化（最小抽出・rev.3）:** `MessageJson`＋平坦 DTO（`SendResult`/`ProfileInfo`/`BotInfo`/`QuotaInfo`/`MessageValidationResult`）を `MessageService.cs` から切り出し `tools/shared/` へ移設、`Line.OpenApi.Tools.csproj` にリンクコンパイル（名前空間維持＝Tools は無改変）。入力例外型を中立化。`MessageService` の操作は Tools に残す。テスト全緑を確認（無改変のため差分小）。
3. **`Line.OpenApi.Extensions.AI` 実装（初期スコープ）:** `tools/shared/` をリンクコンパイル、`Messaging` へ薄く操作をパススルー、`AIFunction` 注釈＋安全ゲート（§4・§5）。公開 API snapshot・ゲーティングテスト（§6）。3 役ゲート（code/security/test-arch）。
4. サンプル（`samples/`）にエンドツーエンドのエージェント実例を追加、README で訴求。

---

## 8. ADR（今回の設計判断）

- **ADR-1:** 抽象化は `Microsoft.Extensions.AI` 主軸（SK 直結にしない）。理由 §2。
- **ADR-2（rev.3 更新）:** 共有は**最小抽出**とし、`MessageJson`＋平坦 DTO **のみ**を共有ソース（`tools/shared/`）として Tools と AI の両アセンブリにリンクコンパイルする。操作メソッド（1 行パススルー）と CLI/MCP 結合（資格情報解決・static メモ化・exit-code 例外）は共有せず Tools 内の `MessageService` に残す。→ 二層アダプタ・API 変更リファクタ・テスト破壊を回避。理由 §3.1-3.3。
- **ADR-3（rev.3 更新）:** 共有ソース＋`Extensions.AI` は `/tools` 支援ティアに置く。`Tools.Core` は**独立 NuGet パッケージにしない（非公開）**＝共有ソース方式のためアセンブリ依存辺を作らず、published usage-scene パッケージの「Core のみ依存」ADR を保持。`Extensions.AI`（公開）の依存は `Messaging`＋`Microsoft.Extensions.AI.Abstractions` の 2 本のみ。
- **ADR-4:** LLM 送信の安全機構を第一級要素とする（送信明示 opt-in・安全側既定・`SendPolicy` ポリシー型・broadcast 独立 opt-in・`BeforeSend` human-in-the-loop・シークレット非露出）。**安全ゲートは生成時クロージャ束縛し、ツール引数スキーマに露出させない**（LLM バイパス防止）。理由 §5。
- **ADR-5:** リリースは Tools と同系統の別サイクル（`ai-v*`）。
- **ADR-6:** `Microsoft.Extensions.AI.Abstractions` は Kiota ロックステップ**外**の新規外部依存軸。`Directory.Build.props` で下限版を集中ピンし、NuGetAudit ゲート（restore の warnings-as-errors）で推移的脆弱性を検知。`.Abstractions` 以外（実装/DI パッケージ）は引き込まない。理由: M.E.AI は版動が速いため本体安定性から隔離。

---

## 9. オープン論点（すべて決着・rev.3）

1. ~~コア抽出の粒度~~（**決着**）— **最小抽出案を採用**（`MessageJson`＋平坦 DTO のみ共有・操作は各消費者が薄く自前）。§3.1-3.2。
2. ~~`Tools.Core` の NuGet 公開・命名~~（**決着**）— **非公開＝共有ソース方式**。独立パッケージにしない。§3.3。
3. ~~抽出のタイミング~~（**決着**）— 段階 2 で先行。最小抽出のため既存テスト温存（§3.2・§6）。
4. ~~安全既定値~~（**決着**）— 送信は明示 opt-in・既定 read-only（§4・§5・ADR-4）。broadcast は別 opt-in。
5. ~~宛先制約フックの形状~~（**決着**）— 述語ではなくポリシー型（操作種別×宛先×件数・非同期）＋送信前フック（§5）。
6. ~~パッケージ名~~（**決着**）— **`Line.OpenApi.Extensions.AI`** で確定。

**残る実装レベルの細部（G3 で確定）:** 共有ソースの物理配置（`tools/shared/` のディレクトリ名・リンク方式）、中立化する入力例外型の最終名、`SendPolicy`/`BeforeSend` の正確なデリゲート形状。いずれも設計判断ではなく実装時の詳細。

---

## 10. G0 通過基準（本ゲート）

- 設計判断（特に ADR-2 コア抽出・ADR-4 安全設計）に致命的な穴がないこと。
- 依存グラフが本体 ADR（一方向・Core 集約）と矛盾しないこと。
- テスト計画（抽出の回帰網・AI 層のゲーティング検証）が現実的であること。
