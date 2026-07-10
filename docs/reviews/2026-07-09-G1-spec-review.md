# G1 仕様レビュー結果（優先4仕様）

- **ゲート:** G1（仕様）
- **担当:** 仕様レビュアー（サブエージェント）
- **対象:** messaging-api.yml / channel-access-token.yml / webhook.yml / liff.yml（master）
- **日付:** 2026-07-09
- **総合判定:** **CONCERNS → 事後解消**（コマンド例2点は設計に修正反映済み。残っていた messaging 多態は完全版仕様の受領で確認済み＝下記追記）

## 追記（2026-07-09、messaging-api.yml 完全版で確認）

G1 で唯一未確認だった **messaging の Message/Action/Flex/Template 多態が解消**。完全版（5900行）を精査した結果、**多態20型すべてが `discriminator: {propertyName: type, mapping: ...}` を完備**：Recipient(3) / DemographicFilter(6) / RichMenuBatchOperation(3) / **Message(11)** / SubstitutionObject(2) / MentionTarget(2) / ImagemapAction(3) / **Template(4)** / **FlexContainer(2)** / **FlexComponent(9)** / FlexBoxBackground(1) / **Action(9)** / Coupon 系(Request/Response 各)。→ **MissingDiscriminator は messaging でも発火しない見込み**。`oneOf` 出現3件はスキーマ合成ではなく Demographic フィルタ内の**「oneOf という名前のプロパティ」**（LINE 仕様の癖、`OneOf` プロパティが生成されるのみで無害）。

これで R7（多態）は解消。残る PoC 確認事項は channel-access-token `/oauth2/v3/token` の oneOf ボディの使い勝手（軽微）のみ。完全版は `outputs/openapi/messaging-api.yml` に保存済み。

## 実確認範囲と限界

- **完全取得・精査:** channel-access-token.yml / webhook.yml / liff.yml。
- **messaging-api.yml:** `paths` セクションは全取得（全オペレーション・全 servers・operationId・content-type を含む）。ただし `components.schemas` 後半（Message / Action / FlexContainer / Template / QuickReply / Sticker 等の**定義本体**）は web_fetch のトークン上限で切断され**未取得**。web_fetch 以外の URL 取得は禁止のため、完全取得は PoC 時に別手段で実施。

## 各項目

### 1. 複数 base URL（R1）— 前提成立、ただしコマンド例に要修正
- root `servers` = `https://api.line.me` 単一。`api-data.line.me` は **operation-level `servers` オーバーライド**で、該当は**5件（全数確定）**：`getMessageContent` / `getMessageContentPreview` / `getMessageContentTranscodingByMessageId` / `getRichMenuImage` / `setRichMenuImage`（tag: messaging-api-blob）。
- **重大: data系5件は全て `/v2/bot/` 配下**で制御系とプレフィックス共有。したがって `--include-path '/v2/bot/**'` では**分離できない**。分離キーは共通サフィックス **`/content`**（`/content/preview`, `/content/transcoding` 含む）。制御系に `/content` 終端パスは無く、サフィックス方式で成立。
- `getMessageContentTranscoding` は **JSON応答だが api-data ホスト** → data クライアント側に含める。

### 2. form-urlencoded（指摘②）— 前提成立
- channel-access-token: 8中6（全POST）が `application/x-www-form-urlencoded`。→ token系に form-urlencoded 追加は正当・必須。
- `/oauth2/v3/token` の form ボディは **oneOf（discriminator 無し）** → MissingDiscriminator 相当の警告可能性。PoCで生成型の使い勝手確認。
- messaging（paths全取得）: form/multipart は 0件。blob は **`*/*`（string/binary）= 生バイナリ（Stream）**。→ 設計 §5 の「multipart/octet 検討」は不正確、Stream 前提に修正。
- liff: json のみ。

### 3. webhook 多態（R7）— 前提成立（良好）
- oneOf ではなく **discriminator + allOf 継承**で、`Event`(mapping 20件)・`Source`・`MessageContent`・`Mentionee`・`MembershipContent`・`ModuleContent` すべて propertyName + mapping 完備。→ **MissingDiscriminator は発火しない見込み**。懸念より軽い。
- 注意: webhook.yml にダミー `servers: https://example.com` とダミー POST `/callback`（operationId: callback）あり。モデル専用なら `--exclude-path /callback` で除外推奨。

### 4. Kiota 検証警告の予測
- **MultipleServerEntries: 発火確実**（messaging、root+5 op-level）。R1 の中核、対処方針は妥当。
- **MissingDiscriminator:** channel-access-token の /oauth2/v3/token oneOf で可能性あり／webhook は発火せず／**messaging の Message・Action 系は未確認（切断）**＝残リスク最大。取得済み messaging 多態（Recipient, DemographicFilter, RichMenuBatchOperation）は discriminator+mapping 完備のため、Message/Action も整備されている蓋然性は高い（推測）。
- InconsistentTypeFormat: 軽微（expires_in の int32/int64 混在等）。GetWithBody / DivergentResponseSchema: 取得範囲で該当なし。

### 5. 命名品質（R2）— 良好
- operationId は全件付与・camelCase・欠落/重複なし。生成メソッド名は自然になる見込み。軽微な癖（`getsAll...`、LIFF が `addLIFFApp` 等の全大文字）は許容範囲。`LiffScope` の enum 値にドット（`chat_message.write`）→ Kiota がサニタイズ。

### 6. liff.yml — 単一ホストで問題なし（PASS）

## 設計(rev.2)の要修正点（重大順）
1. **[要修正] §4.4/§5 コマンド例:** 制御/データ分離は `/v2/bot/**` ではなく **`**/content` 系の include/exclude** で行う。`getMessageContentTranscoding`（JSON・api-data）を data 側に含める。
2. **[要修正] §5 mime 記述:** blob は multipart ではなく **`*/*` 生バイナリ（Stream）**。data クライアントは Stream I/O 前提。
3. **[追確認/PoC] messaging の Message/Action/Flex/Template 多態:** 切断で未確認。MissingDiscriminator 有無と多態生成品質を PoC 実生成で確認（残リスク最大）。
4. **[軽微] channel-access-token /oauth2/v3/token の oneOf ボディ**の生成型の使い勝手確認。
5. **[軽微] webhook のダミー /callback・example.com** を生成コマンドで除外。

## PoC 実装前に確定すべき事項
- messaging-api.yml を完全取得し Message/Action/Flex/Template の discriminator を実確認。
- 制御/データ2クライアントの include/exclude を `**/content` 基準で確定し、`kiota show`/`generate` で分離成立を検証。
- data クライアントの `*/*` バイナリ（Stream）と BaseUrl 上書きの動作確認。
- channel-access-token の form-urlencoded + oneOf ボディ生成の実挙動確認。

## 保存 YAML
- `outputs/openapi/messaging-api.PARTIAL.yml`（web_fetch 切断版・要完全再取得）
- `outputs/openapi/channel-access-token.yml`（完全）
