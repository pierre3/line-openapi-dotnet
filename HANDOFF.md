# 引き継ぎメモ（Cowork → VS Code / Claude Code）

このフォルダは、Cowork で進めた LINE .NET クライアント検討を、ローカル PC の VS Code（Claude Code 拡張）で継続するための一式です。

## 中身

```
line-dotnet/
├── CLAUDE.md                 # Claude Code が自動読込するプロジェクト文脈（最重要）
├── HANDOFF.md                # このファイル
├── docs/
│   ├── LINE-dotnet-client-design.md   # 設計方針 rev.2（構成・生成・認証・CI）
│   ├── REVIEW-WORKFLOW.md             # レビュー運用ルール（4役ゲート）
│   └── reviews/                       # G0/G1 レビュー記録
└── poc/                      # 生成→ビルド→テストの最小構成（G2）
```

## これまでの経緯（要約）

- ツールは **Kiota** に決定（生成コードは opaque box 前提）。
- パッケージは**利用シーン単位**で分割（Bot + LIFF を優先）。
- レビューは **4 役のゲート運用**、最終判断は人。
- **G0 設計 = PASS**、**G1 仕様 = 実質 PASS**（実仕様で複数 base URL / form-urlencoded / webhook 多態を確認済み）。
- **G2 PoC が未実施**。Cowork のサンドボックスは .NET SDK / NuGet が使えず生成・ビルドができないため、ローカル継続に移行。

## VS Code / Claude Code での始め方

1. このフォルダ（`line-dotnet/`）を git リポジトリのルートにして VS Code で開く。
2. `CLAUDE.md` があるので Claude Code が文脈を自動読込。まず「CLAUDE.md と docs を読んで、G2 PoC を進めて」と指示すればよい。
3. 前提ツール: .NET SDK 8+、`dotnet tool install --global Microsoft.OpenApi.Kiota`。
4. `poc/README.md` の手順で 生成 → `dotnet build` → `dotnet test`。
5. 結果をもとに **G2 コード＋セキュリティレビュー**（サブエージェント）を実施 → 人が go/no-go。

## Claude Code セットアップ要点

- VS Code (1.98+) の拡張パネルで「Claude Code」を検索しインストール → サインイン（有料プラン要）。
- ローカルのファイル編集・ターミナル実行（`dotnet build`/`dotnet test`/`kiota`）が可能。編集は Diff で承認。
- `.claude/settings.json` で `dotnet build` 等を事前承認すると確認プロンプトを削減できる。
- プロジェクト文脈は `CLAUDE.md`、ゲートのチェック観点は `docs/REVIEW-WORKFLOW.md` を参照させる。
- 複雑なサブエージェント運用が必要なら、ターミナルの `claude` CLI モードの併用が安定（VS Code 拡張側の対応は段階展開中）。
  - 参考: https://code.claude.com/docs/en/vs-code , https://code.claude.com/docs/en/sub-agents

## 次アクション（そのまま指示に使える）

> CLAUDE.md と docs/ を読み込んで現状を把握し、poc/ で Kiota 生成 → net8.0 と netstandard2.0 でビルド → テストを実行して。警告と生成メソッドのシグネチャを要約し、docs/REVIEW-WORKFLOW.md に沿って G2 のコード＆セキュリティレビューを実施して、go/no-go 判断用にまとめて。
