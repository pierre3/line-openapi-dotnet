[English](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/Line.OpenApi.Extensions.AI/README.md) | **日本語**

# Line.OpenApi.Extensions.AI — LLM tool-calling 向け LINE メッセージングツール

[![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Extensions.AI.svg)](https://www.nuget.org/packages/Line.OpenApi.Extensions.AI)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/pierre3/line-openapi-dotnet/blob/main/LICENSE)

LINE の **Messaging** 利用シーンを、アプリ内蔵の [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/) `AIFunction` ツールとして公開します。Semantic Kernel や任意の Microsoft.Extensions.AI ホスト上に構築した AI エージェントが、ツール呼び出しで LINE ボットを操作できます — メッセージ送信、bot info / quota / ユーザープロフィールの照会、メッセージペイロードの検証。

別プロセスの [`Line.OpenApi.Tools`](https://www.nuget.org/packages/Line.OpenApi.Tools) MCP サーバを補完します。外部エージェント（Claude Desktop / Claude Code）には MCP サーバを、**自作の .NET エージェント**にツールを**アプリ内（in-process）**で組み込みたい場合は本パッケージを使います。

- **既定で安全** — 明示的に送信を有効化しない限り読み取り専用。
- **ゲート付き送信** — 送信ポリシーと human-in-the-loop フック。いずれも開発者が設定し、モデルには見えません。
- **極小の依存** — `Line.OpenApi.Messaging` と `Microsoft.Extensions.AI.Abstractions` の 2 本のみ。実装 / DI パッケージは引き込みません。

対象フレームワーク: **`net10.0`**。

## インストール

```sh
dotnet add package Line.OpenApi.Extensions.AI
```

## クイックスタート

```csharp
using Line.OpenApi.Extensions.AI;
using Line.OpenApi.Messaging;
using Microsoft.Extensions.AI;

// 呼び出し側が MessagingClient を構築（非 AI のライブラリコードと同じクライアント）。
var line = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

// 既定で安全: 読み取り専用ツールのみ（bot info / quota / profile / message-validate）。
IReadOnlyList<AIFunction> tools = LineMessagingAiTools.CreateReadOnly(line);

// 任意の Microsoft.Extensions.AI チャットクライアントに渡す:
var chatOptions = new ChatOptions { Tools = [.. tools] };
IChatClient agent = chatClient.AsBuilder().UseFunctionInvocation().Build();
var response = await agent.GetResponseAsync("今月あと何通送れる？", chatOptions);
```

モデルに**送信**させるには、明示的に opt-in してゲートを設定します。

```csharp
IReadOnlyList<AIFunction> tools = LineMessagingAiTools.Create(line, new LineAiToolOptions
{
    EnableSending  = true,                 // push / multicast / reply を有効化（既定 false）
    AllowBroadcast = false,                // broadcast は最大射程。独立した opt-in

    // 構造ゲート: 射程（操作 / 宛先 / 件数）を制限。false で拒否。
    SendPolicy = (ctx, ct) => new(
        ctx.Operation != LineSendOperation.Broadcast &&
        ctx.Recipients.All(id => myAllowList.Contains(id))),

    // human-in-the-loop / 監査: 送信前に実際の内容を確認。false で拒否。
    BeforeSend = async (ctx, ct) =>
    {
        Console.WriteLine($"送信しようとしています {ctx.Operation}: {ctx.MessagesJson}");
        return await AskForApprovalAsync(ct);
    },
});
```

## 安全モデル

すべての安全ゲートは、ツール生成時に**開発者**が [`LineAiToolOptions`](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/Line.OpenApi.Extensions.AI/LineAiToolOptions.cs) で設定します。**どれもツール引数には出ません**＝プロンプトインジェクションに従うモデルでも、ツール呼び出しでゲートを反転できません。

| ゲート | 既定 | 効果 |
|---|---|---|
| `EnableSending` | `false` | 送信ツール（`push` / `multicast` / `reply`）を生成。OFF なら読み取り専用。 |
| `AllowBroadcast` | `false` | `broadcast`（全友だち送信）も生成。`EnableSending` が必要。 |
| `SendPolicy` | `null` | 各送信前に評価し射程を制限。`false` で拒否。 |
| `BeforeSend` | `null` | ポリシーの後に走る human-in-the-loop / 監査フック。メッセージ**内容**を確認する場所。 |
| `DryRun` | `false` | 送信ツールはペイロード検証のみで API に接触しない（ポリシー / 承認はスキップ）。 |

拒否された送信は API に届かず、`LineSendRefusedException` を送出します。

## ツール

read / validate 系は常に生成され、send 系は対応するゲートを設定したときだけ生成されます。「引数」列はモデルに見える*全*引数で、安全ゲートはそこに含まれません。

| ツール | 引数 | 種別 | 生成される条件 |
|---|---|---|---|
| `line_bot_info` | *(なし)* | read | 常に |
| `line_bot_quota` | *(なし)* | read | 常に |
| `line_bot_profile` | `userId` | read | 常に |
| `line_message_validate` | `messagesJson` | 検証（送信しない） | 常に |
| `line_message_push` | `to`, `messagesJson` | send | `EnableSending = true` |
| `line_message_multicast` | `to`, `messagesJson` | send | `EnableSending = true` |
| `line_message_reply` | `replyToken`, `messagesJson` | send | `EnableSending = true` |
| `line_message_broadcast` | `messagesJson` | send（全友だち） | `EnableSending = true` **かつ** `AllowBroadcast = true` |

ツール名は [`Line.OpenApi.Tools`](https://www.nuget.org/packages/Line.OpenApi.Tools) の MCP ツールと揃えています。`messagesJson` は LINE メッセージオブジェクトの JSON 配列（1〜5 件）で、Messaging API が受け付けるのと同じ形です。

## Semantic Kernel

ツールは素の `AIFunction` なので、Semantic Kernel からそのまま使えます。

```csharp
kernel.Plugins.AddFromFunctions("Line", tools);
```

## 注意

- **参照は Abstractions のみ。** `AIFunctionFactory` は `Microsoft.Extensions.AI.Abstractions` に含まれます。本パッケージは実装 / DI パッケージを引き込みません。`IChatClient` プロバイダは利用側で用意してください。
- **内容は LLM プロバイダに渡ります。** メッセージ本文や read ツールの戻り値は、配線したチャットクライアントに送られ、`ctx.MessagesJson` はゲートに渡ります。ログではツール引数を PII として扱ってください。
- **レート / 累積回数の制限**はホスト側パイプラインの責務で、本パッケージは扱いません。
- **リリースは独立サイクル**（タグ `ai-v*`）で、クライアントライブラリ（`v*`）や CLI / MCP ツール（`tools-v*`）とは別です。

## サンプル

スクリプト**または**実モデルがツールをゲート越しに駆動する動作例（オフライン既定）は [`samples/Line.OpenApi.Samples.Ai`](https://github.com/pierre3/line-openapi-dotnet/blob/main/samples/README_ja.md#4-ai-ツールエージェント-lineopenapisamplesai) を参照。

## 関連ドキュメント

- リポジトリ & クライアントライブラリ: [`README_ja.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/README_ja.md)
- CLI / MCP ツール: [`Line.OpenApi.Tools`](https://www.nuget.org/packages/Line.OpenApi.Tools)（[ドキュメント](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/README_ja.md)）
- 設計: [`docs/LINE-dotnet-AI-plugin-design.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/docs/LINE-dotnet-AI-plugin-design.md)

## ライセンス

MIT（リポジトリルートの [`LICENSE`](https://github.com/pierre3/line-openapi-dotnet/blob/main/LICENSE) を参照）。
