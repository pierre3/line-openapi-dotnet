[English](README.md) | **日本語**

# サンプル (samples)

`Line.OpenApi.*` パッケージの使い方を示すデモアプリです。**既定はオフライン**（環境変数が無ければ実通信せず、リクエストの組み立て方だけを表示）。環境変数を設定すると実際の LINE API に接続します。

| プロジェクト | 種別 | 内容 |
|---|---|---|
| `Line.OpenApi.Samples.Console` | コンソール | 送信 / LIFF 管理 / トークン発行 / Webhook パース（オフライン） |
| `Line.OpenApi.Samples.Webhook` | minimal web api | 実 Webhook 受信 → エコー返信（dev トンネルでライブデモ） |
| `Line.OpenApi.Samples.Login` | minimal web api | LINE Login + OpenID Connect：認可コードフロー（PKCE）→ プロフィール / 友だち関係 |
| `Line.OpenApi.Samples.Ai` | コンソール | LLM tool-calling：スクリプト化したモデルが `Line.OpenApi.Extensions.AI` のツールを安全ゲート（許可リストポリシー・承認フック）越しに操作 |

> これらのサンプルは `IsPackable=false` で、NuGet パッケージには含まれません。`src/` をプロジェクト参照します。

## 環境変数

| 変数 | 用途 | 使うサンプル |
|---|---|---|
| `LINE_CHANNEL_ACCESS_TOKEN` | 長期チャネルアクセストークン | Console（送信・LIFF）/ Webhook（返信） |
| `LINE_TO_USER_ID` | push 送信先ユーザー ID | Console（送信） |
| `LINE_CHANNEL_SECRET` | 署名検証キー | Webhook（受信） |
| `LINE_CHANNEL_ID` | チャネル ID（トークン発行の iss/sub） | Console（トークン） |
| `LINE_KID` | アサーション署名鍵の kid | Console（トークン） |
| `LINE_PRIVATE_KEY` / `LINE_PRIVATE_KEY_PATH` | RSA 秘密鍵（PEM 本体 / ファイルパス。**ファイルパス推奨**） | Console（トークン） |
| `LINE_LOGIN_CHANNEL_ID` / `LINE_LOGIN_CHANNEL_SECRET` | LINE Login チャネル ID / シークレット | Login |
| `LINE_LOGIN_REDIRECT_URI` | コンソール登録のコールバック URL（既定 `http://localhost:5000/callback`） | Login |
| `LLM_MODEL` / `LLM_API_KEY` | 実モデルでツールを駆動するモデル id ＋ API キー（未設定＝スクリプト） | AI |
| `LLM_BASE_URL` | OpenAI 互換エンドポイントのベース URL（例 `https://api.groq.com/openai/v1`）。未設定＝OpenAI | AI |

> 秘密鍵は環境変数へインライン投入するとプロセス一覧やクラッシュダンプ経由で漏れうるため、`LINE_PRIVATE_KEY_PATH`（ファイル参照）を推奨します。

---

## 1. コンソール (`Line.OpenApi.Samples.Console`)

```powershell
cd samples/Line.OpenApi.Samples.Console

# 対話メニュー
dotnet run

# 単発実行（send | liff | token | webhook）
dotnet run -- webhook          # 完全オフライン：同梱 payload を署名→検証→パース
dotnet run -- send             # 送信リクエストの組み立てを表示（トークンがあれば実送信）
dotnet run -- liff             # LIFF 一覧（トークンがあれば実取得）
dotnet run -- liff crud        # add/update/delete まで実演（チャネルを変更するので注意）
dotnet run -- token            # トークン発行の流れ（署名鍵があれば実発行）
```

実送信の例（PowerShell）:

```powershell
$env:LINE_CHANNEL_ACCESS_TOKEN = "<channel access token>"
$env:LINE_TO_USER_ID           = "<宛先 userId>"
dotnet run -- send
```

- `webhook` シナリオは資格情報不要で常に動きます（デモ秘密鍵で署名を自作し、`WebhookRequestParser` で検証・逆直列化してイベント分岐を実演）。
- `token` シナリオは JWT アサーション署名（RS256）を `JwtAssertionBuilder`（サンプル内）で生成します。署名はアプリ固有処理のためライブラリには含めず、サンプル側に置いています。

---

## 2. Webhook 受信 Web アプリ (`Line.OpenApi.Samples.Webhook`)

LINE からの Webhook を受信し、テキストメッセージをそのままエコー返信する minimal API です。ローカルを **dev トンネル**で公開して LINE プラットフォームから疎通させます。

### 2-1. 資格情報を設定して起動

```powershell
cd samples/Line.OpenApi.Samples.Webhook
$env:LINE_CHANNEL_SECRET       = "<channel secret>"        # 受信（署名検証）に必須
$env:LINE_CHANNEL_ACCESS_TOKEN = "<channel access token>"  # 返信に必要（無くても受信は動く）
dotnet run
```

- `GET /` … ヘルスチェック（設定状況を JSON で返す）
- `POST /webhook` … 署名検証＋パース。テキスト受信時にエコー返信。署名 NG は 401、本文 NG は 400、シークレット未設定は 503。

> シークレット未設定でも起動はします（`GET /` で状態確認可）。返信にはトークンが必要です。

### 2-2. dev トンネルで公開

[dev tunnels CLI](https://learn.microsoft.com/azure/developer/dev-tunnels/) を使う例（アプリが `http://localhost:5000` で待ち受けている前提。ポートは起動ログで確認）:

```powershell
# 初回のみ
devtunnel user login

# アプリのポートを匿名アクセスで公開
devtunnel host -p 5000 --allow-anonymous
```

表示された HTTPS の転送 URL（例 `https://xxxx.devtunnels.ms`）の末尾に `/webhook` を付けたものが Webhook URL です。

> Visual Studio / VS Code の Dev Tunnels 機能でも同等の公開ができます。

> ⚠️ **注意:** `--allow-anonymous` はローカルマシンをインターネットに公開します。`POST /webhook` は署名検証で保護されますが、`GET /` は設定状況（webhook/reply の有効・無効）を無認証で開示します。**デモ用途に限定し、終わったらトンネルを停止**してください（`Ctrl+C`）。

### 2-3. LINE Developers コンソールで Webhook URL を設定

1. [LINE Developers Console](https://developers.line.biz/console/) で対象チャネル（Messaging API）を開く
2. **Messaging API 設定** → **Webhook URL** に `https://xxxx.devtunnels.ms/webhook` を設定
3. **Webhook の利用** を ON、**検証** ボタンで疎通確認（`GET /` ではなく `POST /webhook` に届きます）
4. Bot を友だち追加し、トークルームでテキストを送ると `echo: <本文>` が返れば成功

> 💡 **ヒント — 手順 2/3 はコンソール不要。** [`line` CLI ツール](../tools/README_ja.md) を使えば URL 設定と疎通確認をターミナルから実行できます。dev トンネルは再起動のたびに URL が変わるため特に便利です:
>
> ```powershell
> $env:LINE_CHANNEL_ACCESS_TOKEN = "<channel access token>"
> line webhook set-endpoint --url https://xxxx.devtunnels.ms/webhook
> line webhook test-endpoint    # LINE から実エンドポイントへテスト配信し到達可否を返す
> ```

---

## 3. LINE Login Web アプリ (`Line.OpenApi.Samples.Login`)

LINE Login の**認可コードフロー（PKCE 付き）**を実行し、コールバックで ID トークンを（LINE 側に委譲して）検証してから、ユーザーのプロフィールと友だち関係を表示する minimal API です。Webhook サンプルと違い LINE Login は **localhost コールバックを許可**するため、dev トンネルは不要です。

### 3-1. リダイレクト URI を登録して資格情報を設定

1. [LINE Developers Console](https://developers.line.biz/console/) で **LINE Login** チャネルを開く
2. **LINE Login 設定**でコールバック URL `http://localhost:5000/callback` を追加
3. 起動:

```powershell
cd samples/Line.OpenApi.Samples.Login
$env:LINE_LOGIN_CHANNEL_ID     = "<login channel id>"
$env:LINE_LOGIN_CHANNEL_SECRET = "<login channel secret>"
# 任意: /logout での deauthorize デモを有効化（Messaging チャネルアクセストークン）
$env:LINE_CHANNEL_ACCESS_TOKEN = "<messaging channel access token>"
dotnet run
```

- `GET /` … ホーム。Login の設定状況と「Sign in with LINE」リンクを表示
- `GET /login` … 認可 URL を生成（state + PKCE をセッション保存）し LINE へリダイレクト
- `GET /callback` … `state` を照合 → コード交換（`ExchangeCodeAsync`）→ ID トークン検証（`VerifyIdTokenAsync`）→ プロフィール＋友だち関係を表示
- `GET /logout` … アクセストークンを失効（Messaging チャネルトークン設定時は `DeauthorizeAsync` も実行）

> 資格情報が無くても起動します（`GET /` が "disabled" を表示）。待ち受けは `LINE_LOGIN_REDIRECT_URI` のオリジン（既定 `http://localhost:5000`）です。

### 3-2. 試す

`http://localhost:5000/` を開き **Sign in with LINE** をクリック。LINE で同意すると `/callback` に戻り、userId・表示名・画像・ID トークンの claim・Login チャネルに紐づく公式アカウントの友だち状態が表示されます。

> コンソールサンプルと違い、LINE Login はオフラインでは動きません（資格情報が無いと "disabled" ページのみ。フローは実ブラウザ往復のため）。本サンプルは `AddLineLogin`（DI）を使います。表示内容以外にも、同じ `LoginClient` で `RefreshTokenAsync`・`VerifyAccessTokenAsync`・`GetUserInfoAsync`（OIDC userinfo）が利用できます。

---

## 4. AI ツールエージェント (`Line.OpenApi.Samples.Ai`)

LINE の Messaging 利用シーンを [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/)
の `AIFunction` ツール（`Line.OpenApi.Extensions.AI`）として LLM に公開し、実際の
`FunctionInvokingChatClient` ループで駆動するコンソールアプリ。本パッケージの安全設計＝送信の
明示 opt-in・許可リスト `SendPolicy`・human-in-the-loop の `BeforeSend` 承認フック・読み取り検証を
実演します。

### 4-1. まず動かす（オフライン・準備不要）

```powershell
cd samples/Line.OpenApi.Samples.Ai
dotnet run                     # オフライン：ローカルスタブ transport でゲートは実行、ネットワークには出ない
```

API キーも LINE トークンもネットワークも不要で、一連の動きをそのまま確認できます。

### 4-2. 変えられるもの・変え方

すべて実行時に切り替えられます（フラグまたは環境変数）。一覧は `dotnet run -- --help` で確認できます。安全ゲートはそれぞれ専用フラグを持ち、既定は安全側の姿勢（送信 ON・許可リストポリシー・コンソール承認・broadcast OFF）です。

| 対象 | 変え方 | 既定 |
|---|---|---|
| **頭脳** — スクリプト vs 実 LLM | `LLM_MODEL` ＋ `LLM_API_KEY`（任意で `LLM_BASE_URL`）を設定 | スクリプト（決定的・キー不要） |
| **送信経路** — ローカルスタブ vs 実送信 | `--send` を渡し **かつ** `LINE_CHANNEL_ACCESS_TOKEN` を設定 | オフラインスタブ（ゲートは動くが送信しない） |
| **許可宛先** | `LINE_TO_USER_ID` を設定 | プレースホルダの `Uallowed…` id |
| **送信の可否** | `--read-only` で読み取り/検証ツールのみに（送信ツールなし） | 送信 ON |
| **許可リストポリシー** | `--no-policy` で `SendPolicy` を外す（任意の宛先を許可） | 許可リストポリシー |
| **承認フック** | `--no-approval` で human-in-the-loop の `BeforeSend` プロンプトを外す | コンソール承認 |
| **broadcast** | `--allow-broadcast` で broadcast ツールも公開 | broadcast OFF |
| **dry-run** | `--dry-run` で送信ツールを検証のみ（API 非接触）に | dry-run OFF |

起動時のバナーに実効ゲート構成が表示されます（`Gates: sending=ON  policy=allow-list …`）。

> **ゲートをフラグで指定しても、ソースに直書きするのと安全性は同じです** — どちらも起動時に人間が設定し、モデルは到達できません。本当のプロンプトインジェクション対策は**別の性質**で、ゲートを LLM に見えるツール引数として出さないこと（4-3 でツールスキーマを表示して確認できます）。そのため、たとえ注入された指示に従うモデルでも、開発者がゲートをどう設定したかによらず、ツール呼び出しでゲートを反転できません。内部的には各フラグが `LineMessagingAiTools.Create(...)` に渡す前の `LineAiToolOptions` のフィールドを設定するだけです（`--read-only` は `LineMessagingAiTools.CreateReadOnly` に対応）。オプションの詳細はパッケージ README を参照。

### 4-3. スクリプト頭脳（既定）

**スクリプト（既定）** は 3 ステップを再現的に再生します。

1. **ツール検出** — モデルに見えるツールを表示。安全ゲートが引数に**現れない**ことを確認。
2. **許可された送信** — 許可リストのユーザーへ push → `SendPolicy` が ALLOW → `BeforeSend` が承認を求める（入力をパイプすると自動承認）→ 送信完了。
3. **拒否された送信** — 許可リスト外へ push → `SendPolicy` が DENY → ツールが `LineSendRefusedException` を送出、それがモデルに返り「送れなかった」と報告。

### 4-4. 実モデル（チャット REPL）

`LLM_MODEL` ＋ `LLM_API_KEY` を設定するとチャット REPL が起動し、モデル自身がツール呼び出しを判断します（安全ゲートはまったく同じように効く）。OpenAI と、`LLM_BASE_URL` を指定した任意の OpenAI 互換エンドポイント（Groq / Together / Ollama の OpenAI 口 / vLLM / LM Studio など）で動作。エンドポイントとモデルが tool/function calling に対応している必要があります。4-5 を行わない限りオフライン（スタブ transport）のままです。

```powershell
# OpenAI
$env:LLM_MODEL   = "gpt-4o"
$env:LLM_API_KEY = "<openai api key>"
dotnet run
# You: Uallowed0000000000000000000000000 に会議は10時とLINEして

# 任意の OpenAI 互換エンドポイント（例: Groq）
$env:LLM_BASE_URL = "https://api.groq.com/openai/v1"
$env:LLM_MODEL    = "llama-3.3-70b-versatile"
$env:LLM_API_KEY  = "<groq api key>"
dotnet run
```

### 4-5. 実送信（ライブ配信）

`--send` は送信経路をローカルスタブから実 LINE API に切り替えます。どちらの頭脳とも併用可。実メッセージが実際に飛ぶのは `SendPolicy` が宛先を許可し **かつ** `BeforeSend` のプロンプトで承認した場合だけなので、`LINE_TO_USER_ID` には本当に送りたい宛先を設定してください。

```powershell
$env:LINE_CHANNEL_ACCESS_TOKEN = "<channel access token>"
$env:LINE_TO_USER_ID           = "<許可リストの宛先 userId>"
dotnet run -- --send
```

- オフラインは dry-run ではなく**ローカルスタブ transport** を使う（ゲートを実行するため。dry-run はゲートより前で短絡する）。メッセージ本文は `SendPolicy` / `BeforeSend` に渡るため、ログではツール引数を PII として扱うこと。
- 拒否時はツールが `LineSendRefusedException` を送出。`FunctionInvokingChatClient` がそれを捕捉してエラーをモデルに返す（＝ステップ3 はモデルの返答が表示される）。サンプルには、例外が呼び出し側へ浮上する版/設定向けの防御的 `catch` も用意。実モデルでは既定 `IncludeDetailedErrors=false` のため、拒否の具体理由でなく汎用エラーが渡る点に注意。
- brain が違うだけで、`AsBuilder().UseFunctionInvocation()` の配線・ツール一覧・ゲートは不変。Semantic Kernel なら同じツールを `kernel.Plugins.AddFromFunctions("Line", tools)` に渡します。`Microsoft.Extensions.AI` / `Microsoft.Extensions.AI.OpenAI` は**サンプルのみ**の参照で、公開パッケージ `Line.OpenApi.Extensions.AI` は Abstractions のみに依存します。

---

## トラブルシュート

### LINE Login サンプル

- **`400 invalid_request` / リダイレクトエラー:** `LINE_LOGIN_REDIRECT_URI` が Login チャネルに登録したコールバック URL と完全一致していない（スキーム・ホスト・ポート・パスまで一致が必要）。
- **`/callback` で `state mismatch`:** セッション Cookie が失われた（期限切れ、またはブラウザが送出していない）。`/login` からやり直す。
- **`friend of the linked OA` が常に false:** Login チャネルに公式アカウントを連携していないと友だち状態は意味を持たない。

### Webhook サンプル

- **返信が来ない:** `LINE_CHANNEL_ACCESS_TOKEN` 未設定、または reply token の期限切れ（発行から約 1 分）。`GET /` の `reply` が `enabled` か確認。
- **401 が返る:** `LINE_CHANNEL_SECRET` がチャネルのものと不一致。
- **検証ボタンが失敗:** dev トンネルが起動しているか、URL 末尾が `/webhook` か、`--allow-anonymous` で公開しているかを確認。
