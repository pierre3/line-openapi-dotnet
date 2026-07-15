# カバレッジ拡充 ラウンド 2 実装ゲートレビュー — ManageAudience

- **日付:** 2026-07-15
- **ブランチ:** `feat-coverage-round2`（`main` @ `4c54018` から分岐）
- **対象:** `Line.OpenApi.ManageAudience`（manage-audience.yml）
  - 制御系（api.line.me・JSON・10 ops）＋ データ系（api-data.line.me・**multipart/form-data** の by-file アップロード 2 ops）。
  - Messaging と同じ control/data 2 クライアント分離（R1）。**本リポジトリ初の multipart 送出。**
  - ファサード `ManageAudienceClient`＝`Api`（control）＋`Blob`（data・BaseUrl を api-data に構築前設定）＋`CreateWithStaticToken`＋multipart ヘルパ（`UploadUserIdsByFileAsync`/`AddUserIdsByFileAsync`）＋制御系便利メソッド（create/add/get/delete）。DI `AddLineManageAudience`。

## 検証

- ビルド 0 警告 / テスト **239**（+ isolation 1 + tools 72）全緑 / pack **11 パッケージ**（10 code + 1 meta）PASS / docfx 0 warnings / NuGet 監査クリーン。
- 公開 API snapshot（ManageAudience）approved 化。verify-packages を 11 パッケージ・内部依存グラフ（→ Core のみ）で更新。
- README 英日・docfx.json・概念記事 英日（manage-audience）・manual TOC/index 更新。
- **multipart 直列化は HTTP モックテストで end-to-end 実証**（file パート・api-data ルーティング・JSON 応答・text/plain・任意パート省略）。

## 3 役ゲート結果

| 役 | 判定 | 要点 |
|---|---|---|
| code-reviewer | **PASS** | multipart 実装は正しい（パート名が spec 一致・file=text/plain・isIfaAudience は小文字 true/false・audienceGroupId は InvariantCulture・Stream は非破棄で呼び出し側所有）。R1 分離は Messaging 準拠。info 2 点。 |
| security-reviewer | **PASS** | トークンはホストゲート付き（{api.line.me, api-data.line.me} のみ）。multipart ボディに秘匿情報混入なし。R1 misrouting なし。負側ホストゲートテストあり。low 1 点（空配列 AllowedHosts の fallback＝Messaging と同挙動）。 |
| test-arch-reviewer | **PASS** | 主リスク（初 multipart）を transport レベルで (a) api-data ルーティング (b) file＋scalar パート存在 (c) JSON 応答 (d) file バイト送出 まで実証。R1 は request-info＋transport の 2 層で回帰。low 3 点（下記反映）。 |

### 指摘の反映（コミット `<gate-fixes>`）

- **test-arch①（反映済）:** file パートの `text/plain` を明示アサート。
- **test-arch②（反映済）:** 任意パート（description/isIfaAudience/uploadDescription）が null 時に省略されることをアサート。
- **test-arch③（反映済）:** 制御系 JSON アップロード（CreateForUploadingUserIds）の transport テストを追加。
- **code info（反映済）:** DI 既定を `LineHosts.Default` に統一（`new[] { Api, ApiData }` の重複解消）。

反映後テスト 237→239 全緑。

## 判定

- 3 役 = **GO 推奨**（全 PASS、非ブロッキング指摘を反映）。**人の go/no-go 待ち**（main マージ前）。

## GA 前の実機確認事項（外部 HTTP 遮断で未実施）

- **by-file アップロードの multipart `file` パートに `filename` 属性が付かない**（Kiota `MultipartBody` は `name="file"` のみ出力）。spec は filename 非要求だが、実 LINE エンドポイントでの受理を GA 前に一度スモーク推奨（deauthorize ボディ形式確認と同クラスの実機検証項目）。
