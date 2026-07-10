# LINE .NET クライアント — PoC (G2)

Kiota で LINE OpenAPI から .NET クライアントを生成し、設計方針（利用シーン単位分割・複数 base URL 対処・form-urlencoded・webhook 多態）が実際に成立するかを検証する最小構成です。**このサンドボックスでは .NET SDK を導入できないため、ローカル（Windows/.NET）で実行してください。**

## 前提

- .NET SDK 8 以降（`dotnet --version`）
- Kiota CLI:
  ```
  dotnet tool install --global Microsoft.OpenApi.Kiota
  ```

## 手順

`poc/` 直下で:

**1) 生成**（specs は `openapi/` に同梱。無い分は自動取得）

```powershell
# Windows
./scripts/generate.ps1
```
```bash
# macOS / Linux
bash scripts/generate.sh
```

**2) ソリューション作成 & ビルド**

```bash
dotnet new sln -n Line.Poc
dotnet sln add src/Line.Core/Line.Core.csproj `
               src/Line.ChannelAccessToken/Line.ChannelAccessToken.csproj `
               src/Line.Messaging/Line.Messaging.csproj `
               src/Line.Messaging.Webhook/Line.Messaging.Webhook.csproj `
               src/Line.Liff/Line.Liff.csproj `
               tests/Line.Poc.Tests/Line.Poc.Tests.csproj
dotnet build
```
（PowerShell 以外では行継続子 `` ` `` を各 csproj をスペース区切りに置き換えてください。）

**3) テスト**

```bash
dotnet test
```

`CoreTests`（署名検証・許可ホスト負側）は生成物に依存せず必ず動きます。webhook 多態デシリアライズテストは生成後に有効化します（下記）。

## 検証してほしいこと（PoC チェックリスト）

1. **Kiota 検証警告** — 生成時ログの警告。特に messaging で `MultipleServerEntries`（想定内）、`MissingDiscriminator` が出ないか。
2. **ホスト分離** — `src/Line.Messaging/Generated/Api` に送信系、`.../Generated/Blob` に `content` 系のみが分かれて生成されるか。`MessagingClient` が両方を構築でき、Blob 側 BaseUrl が `api-data.line.me` になるか。
3. **form-urlencoded** — `Line.ChannelAccessToken/Generated` でトークン発行の本体が型付きモデルになっているか（stream に退化していないか）。
4. **webhook 多態** — `Line.Messaging.Webhook/Generated/Models` に `CallbackRequest` と各イベント派生型（`MessageEvent` 等）が生成され、下記テストで discriminator 復元が成功するか。
5. **net10.0 ビルド** — 全ライブラリが `net10.0`（単一 TFM）でビルドできるか（`Microsoft.Kiota.Bundle` の net10.0 動作確認）。netstandard2.0 / .NET Framework は対象外。
6. **使い勝手（R2）** — 生成された主要メソッドのシグネチャ（例: メッセージ送信、コンテンツ取得）が実用的か。fluent ビルダーのパスが不自然でないか。

### webhook 多態テストの有効化

生成後、`Generated/Models` の実際の型名を確認し（多くは `CallbackRequest` / `MessageEvent` / `TextMessageContent`）、必要なら `WebhookDeserializationTests.cs` の型名を調整のうえ:

```
dotnet test -p:DefineConstants=WEBHOOK_DESERIALIZATION_READY
```

## フィードバックとして共有してほしい出力

G2 コードレビュー（次ゲート）のため、以下を貼り付けていただけると精緻に進められます。

- `generate` 実行時の**警告ログ全文**。
- `dotnet build`（`net10.0`）の結果。エラー/警告があればその内容。
- `dotnet test` の結果。
- 生成された**メッセージ送信**と**コンテンツ取得**のメソッドシグネチャ（`MessagingClient` の使い方が確定できるように）。
- webhook のルート型名とイベント派生型名。

## 既知の注意点

- **`MessagingClient` のビルダーパス** — `MessagingClient.cs` の使用例コメント（`.V2.Bot.Message.Push...` 等）は生成結果に依存する仮のパスです。生成後に実パスへ調整してください。構築ロジック（2 アダプタ + BaseUrl 上書き）自体は確定です。
- **webhook `/callback`** — モデルは唯一のオペレーション `/callback` 経由で生成されるため、これを除外しません。生成される `callback` メソッド（server は example.com のダミー）は使わず、モデルのみ利用します。
- **`Microsoft.Kiota.Bundle` のバージョン** — `Directory.Build.props` の `KiotaBundleVersion` を、`kiota info -l CSharp` が推奨する版に合わせてください。

## 構成

```
poc/
├── Directory.Build.props        # 共通 TFM(net10.0)/nullable/Kiota版
├── openapi/                     # 仕様スナップショット
├── scripts/generate.ps1 / .sh   # Kiota 生成コマンド
├── src/
│   ├── Line.Core/               # 認証プロバイダ・署名検証・許可ホスト（手書き）
│   ├── Line.ChannelAccessToken/ # トークン発行（form-urlencoded 込み生成）
│   ├── Line.Messaging/          # 制御系+データ系2クライアント + MessagingClient ファサード
│   ├── Line.Messaging.Webhook/  # webhook モデル専用
│   └── Line.Liff/               # LIFF
└── tests/Line.Poc.Tests/        # 署名/許可ホスト（常時）+ webhook 多態（生成後有効化）
```
