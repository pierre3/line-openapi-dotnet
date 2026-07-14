[English](README.md) | **日本語**

# サンプル (samples)

`Line.OpenApi.*` パッケージの使い方を示すデモアプリです。**既定はオフライン**（環境変数が無ければ実通信せず、リクエストの組み立て方だけを表示）。環境変数を設定すると実際の LINE API に接続します。

| プロジェクト | 種別 | 内容 |
|---|---|---|
| `Line.OpenApi.Samples.Console` | コンソール | 送信 / LIFF 管理 / トークン発行 / Webhook パース（オフライン） |
| `Line.OpenApi.Samples.Webhook` | minimal web api | 実 Webhook 受信 → エコー返信（dev トンネルでライブデモ） |

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

---

## トラブルシュート

- **返信が来ない:** `LINE_CHANNEL_ACCESS_TOKEN` 未設定、または reply token の期限切れ（発行から約 1 分）。`GET /` の `reply` が `enabled` か確認。
- **401 が返る:** `LINE_CHANNEL_SECRET` がチャネルのものと不一致。
- **検証ボタンが失敗:** dev トンネルが起動しているか、URL 末尾が `/webhook` か、`--allow-anonymous` で公開しているかを確認。
