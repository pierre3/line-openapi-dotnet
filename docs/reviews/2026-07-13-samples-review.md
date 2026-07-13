# サンプルアプリ レビュー記録（2026-07-13）

対象: `samples/` 配下の同梱デモアプリ（配布パッケージ非混入 = `IsPackable=false`）。

- `samples/Line.OpenApi.Samples.Console`（送信 / LIFF 管理 / トークン発行 / Webhook オフラインパース）
- `samples/Line.OpenApi.Samples.Webhook`（minimal API：実 Webhook 受信＋エコー返信、dev トンネル想定）

レビュアー 2 役を Agent ツール（`subagent_type`）で起動。生成コード（`src/**/Generated/`）は対象外。

## 判定

| 役 | 判定 |
|---|---|
| code-reviewer | **PASS**（軽微 CONCERNS・非ブロッキング） |
| security-reviewer | **PASS**（低〜情報レベルのみ） |

セキュリティ必須 3 観点はいずれも問題なし（高確信）: タイミング攻撃（`WebhookSignatureValidator.FixedTimeEquals` へ正しく委譲）／トークン漏洩（非機密メタのみ表示）／ホスト誤送出（`CreateWithStaticToken` 経由で R1 順序バグ再現余地なし）。

## 反映した指摘

1. **[両者一致 / Low・実益大] Webhook 返信の例外が「常に 200」を破る** — `Line.OpenApi.Samples.Webhook/Program.cs`
   reply token 期限切れ等で `Reply.PostAsync` が例外→500→LINE 再送ストーム。返信呼び出しを try/catch で包み、失敗時はログのみで受信は 200 を返すよう修正（「downstream 失敗を握りつぶしてでも ack」イディオム）。
   検証: 不正トークン＋正署名＋message イベントで **200 返却＋警告ログ**を実測。
2. **[code / Low] `TokenScenario` のアダプタ未破棄** — `using var adapter` に修正。
3. **[security / Low] dev トンネル注意喚起不足** — README に「ローカル公開／`GET /` は無認証で設定状態を開示／デモ後に停止」を追記。
4. **[code / Info] `HasCrudFlag` の非対称性** — 対話メニューでは crud 不可の旨をコメント追記。
5. **[security / Info] 秘密鍵インライン投入** — README で `LINE_PRIVATE_KEY_PATH`（ファイル参照）を推奨と明記。

## 未対応（受容）

- 秘密値の不変 `string` 保持（.NET 既知制約、サンプルでは許容）。
- 例外メッセージのコンソール出力（ライブラリ/Kiota 側にシークレット混入の設計は確認範囲でなし、実害低）。

## 状態

ビルド 0 警告・テスト 92/92＋Isolation 1/1・pack 5 パッケージのみ（サンプル非混入を確認）。
オフライン 4 シナリオ／Web エンドポイント（401・400・503・200、および返信失敗時 200＋ログ）実測済み。
