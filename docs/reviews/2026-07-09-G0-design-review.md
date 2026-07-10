# G0 設計方針レビュー結果

- **ゲート:** G0（設計）
- **担当:** アーキテクチャ/テスト観点レビュアー（サブエージェント）
- **対象:** LINE-dotnet-client-design.md
- **日付:** 2026-07-09
- **総合判定:** **CONCERNS**

方針の骨格は妥当で、認証設計とKiotaのAPI理解は概ね正確。ただし本設計の中核リスクである「複数base URL問題」の扱いが、事実上ほぼ確定している問題を「可能性/PoCで確認」レベルに過小評価しており、G1前に設計を具体化すべき箇所が複数ある。

---

## 重大な指摘

### 1. `api-data.line.me` 問題は「可能性」ではなく「ほぼ確定した制約」。対処が抽象的すぎる
- **なぜ問題か:** Kiota公式ドキュメント（`using` の validation rules）に **`MultipleServerEntries`（複数serverが定義されていると警告）** と明記されており、Kiotaは **1クライアント=root `servers` の先頭1件のみ** を base URL として採用する。オペレーション単位/パス単位の `servers` オーバーライドは尊重されない。LINEの `messaging-api.yml` はコンテンツ取得系（`getMessageContent` 等）を operation-level `servers` で `api-data.line.me` に振っている構造のため、単一クライアント生成では **これらのエンドポイントが誤って `api.line.me` に対して生成される**。つまり R1 の「呼べない可能性」は実質「確実に誤ホストになる」。
- **推奨対応:** PoC前に対処方針を確定する。現実的な選択肢は2つ。(a) `--include-path` で `api.line.me` 系と `api-data.line.me` 系を別クライアントとして分離生成し、後者は生成後に `RequestAdapter.BaseUrl` を `https://api-data.line.me` に設定して使う。(b) 単一クライアントのまま、data系呼び出しだけ request builder の raw URL コンストラクタ / `WithUrl()` で `api-data.line.me` の絶対URLを渡す。いずれも「分離生成すれば自動で正しいホストになる」わけではない点に注意（生成後もBaseURL上書き or WithUrlが必要）。`manager.line.biz`（module-attach）も同じく別クライアント必須。

### 2. `--structured-mime-types application/json` のみ指定は `channel-access-token.yml` を壊す
- **なぜ問題か:** Kiotaの `--structured-mime-types` の既定は `application/json;q=1, application/x-www-form-urlencoded;q=0.2, multipart/form-data;q=0.1, text/plain;q=0.9`。設計の §5 例では `application/json` のみに絞っている。しかしチャネルアクセストークン発行エンドポイント（`/oauth2/v2.1/token`）のリクエストボディは **`application/x-www-form-urlencoded`**。application/json のみに絞ると、この認証エンドポイントの本体が構造化モデルとして生成されず stream/byte 配列にフォールバックし、型安全なトークン発行ができなくなる。§7で短期トークン発行を同クライアントに依存させている設計と矛盾する。
- **推奨対応:** 少なくとも token 系仕様では `application/x-www-form-urlencoded` を含める。仕様ごとに structured-mime-types を出し分ける（messaging は json 中心、channel-access-token は form-urlencoded を追加）。multipart（コンテンツアップロード）が絡む仕様では `multipart/form-data` も検討。

### 3. ロードマップの順序矛盾: PoCの「サンプル呼び出し」に認証が前提
- **なぜ問題か:** §12 で step1「messaging を生成してサンプル呼び出しで検証」→ step2「認証基盤」の順だが、実API呼び出しにはチャネルアクセストークン（=認証）が必須。また PoC 対象を messaging 単体にすると、本設計最大のリスクである複数ホスト(R1)・form-urlencoded(指摘2)・webhook多態デシリアライズがPoCで検証されない。
- **推奨対応:** PoCのスコープに (a) 静的トークンによる最小認証、(b) `api-data.line.me` を含むdata系呼び出し、(c) `channel-access-token.yml` のform-urlencoded生成、(d) `webhook.yml` の多態イベントのデシリアライズ を含める。認証の最小形はstep1に前倒し。

---

## 中程度の指摘

- **webhook.yml の多態(oneOf+discriminator)デシリアライズ検証が抜けている:** WebhookイベントはイベントタイプごとのoneOf/discriminator構造。Kiotaは discriminator が無いと `MissingDiscriminator` 警告を出し、多態の復元が不完全になりうる。§10の「round-trip」だけでなく、未知イベントタイプ/複数イベントタイプ混在の実ペイロードでの型解決を明示的にテストすべき。生成時に discriminator 警告が出ないかもG1で確認。
- **AllowedHostsValidator の負側テストが無い:** §7で許可ホスト限定を掲げるが、「許可外ホストにトークンが付与されない」検証が無い。`api.line.me`/`api-data.line.me` 両方を許可リストに入れるテストを追加。
- **短期トークンの期限管理・並行更新のテストが無い:** 「実行時取得」は正しいが、期限切れ判定・トークン更新の競合（同時多発リクエスト時の二重発行）に対するテスト観点が欠落。
- **依存バージョン整合の運用が曖昧:** §6は「kiota info推奨に合わせる」とあるが、生成物と `Microsoft.Kiota.*` ランタイム版の整合が崩れると実行時例外になりやすい。CI(§9)で Kiota本体バージョンと生成物のロックを明示的に固定・検証する手順が必要（R3を運用に落とし込む）。
- **`netstandard2.0` ターゲットの実現性未確認:** 新しめの `Microsoft.Kiota.Bundle`/シリアライザが netstandard2.0 を実際にサポートするか要確認。サポート外なら「広く互換」の前提が崩れる。G1で `dotnet build` により実証を。
- **回帰テストのbaseline管理:** 「公開API差分をsnapshotで検出」は良いが、生成物はopaque box前提のため、snapshot対象を公開API表面（public型/シグネチャ）に限定する方針を明記しないと、内部生成差分でノイズが増える。`--type-access-modifier Internal`（分割時）との相互作用も要整理。
- **middleware/可観測性・リトライ:** レート制限(429)・リトライ・ロギングはKiotaのHTTPミドルウェアで扱えるが設計に言及なし。LINE APIはレート制限があるため方針を一言。

---

## 良い点

- 認証設計（`IAccessTokenProvider` + `BaseBearerTokenAuthenticationProvider`、ヘッダ組立を基底に委譲、トークンは登録時でなく実行時取得、DIファクトリ、`AllowedHostsValidator`）は公式ドキュメント・DIチュートリアルの推奨と完全に一致しており的確。
- `webhook.yml` をモデル専用として扱い、署名検証(HMAC-SHA256)を仕様外の手書きユーティリティとする切り分けは正しい。
- 仕様ごとの独立クライアント生成＋`--class-name`/`--namespace-name` での衝突回避、`kiota-lock.json` のコミットと `kiota update` 基準化、週次再生成＋差分PRのCI設計は保守運用として妥当。
- `--exclude-backward-compatible` 有効化、`--additional-data` 既定true維持の判断は正確。
- 単一パッケージで立ち上げて実績を見て分割(案B)へ、というインクリメンタル方針は現実的。

---

## G1（仕様レビュー）前に必須の対応事項

1. **複数base URL方針の確定（指摘1）:** `api-data.line.me`/`manager.line.biz` をどう分離生成し、生成後に `RequestAdapter.BaseUrl` 上書き or `WithUrl()` でどう振り分けるかを、コード方針レベルで明記。「PoCで確認」ではなく前提として設計に織り込む。
2. **structured-mime-types の仕様別出し分け（指摘2）:** 少なくとも token系に `application/x-www-form-urlencoded` を含める方針をG1確定。
3. **PoCスコープの拡張（指摘3）:** messaging単体でなく、data系・token(form-urlencoded)・webhook多態・最小認証をPoC対象に含める。
4. **webhook多態デシリアライズ**とKiota生成時の discriminator 警告の扱いを検証項目として明記。

---

## 補足（事実と推論の区別）

事実確認の出典はKiota公式（`using`/`authentication`/`request-builders`/`dependencies`）。「Kiotaが複数serverを尊重しない＝先頭1件のみ」は `MultipleServerEntries` validation rule と request adapter が単一BaseURLを供給する仕様に基づく事実。operation-level servers を無視する挙動は当該仕様からの強い推論であり、PoCでの実測確認は依然推奨。
