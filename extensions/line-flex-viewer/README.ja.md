# LINE Flex Message ビューア（Canvas 拡張）

*English version: [README.md](./README.md).*

[LINE Flex Message](https://developers.line.biz/ja/docs/messaging-api/using-flex-messages/)
の JSON を、アプリのサイドパネルで LINE 風にライブレンダリングしながらプレビュー・調整できる
Copilot CLI の **Canvas 拡張**です。

同じレンダラを 3 通りの形で再利用しています。

- **Canvas 拡張** — Copilot App のサイドパネル内でライブプレビュー。
- **standalone ページ** — Copilot App 不要の 100% クライアントサイドなブラウザプレビュー。
- **MCP サーバ** — 任意の MCP クライアント（Claude Desktop / Claude Code など）向けのライブブラウザプレビュー。

## 想定ワークフロー

1. AI／エージェントが Flex Message JSON を組み立てる（例:
   [`Line.OpenApi.Tools`](https://github.com/pierre3/line-openapi-dotnet) の MCP サーバ経由）。
2. エージェントが **`set_content`** アクションで Canvas に push すると、パネルが即座に描画。
3. パネルのエディタで JSON を直接調整（ライブ再描画＋自動保存）。
4. エージェントが **`get_content`** で編集結果を読み戻し、反復を続ける。
5. 拡張をプライベート gist で公開／共有（下記参照）。

## Canvas アクション（エージェント向け）

| アクション    | 用途 |
| ------------- | ---- |
| `set_content` | Canvas に表示する Flex JSON を設定して再描画。flex メッセージ全体（`{type:"flex",altText,contents}`）、`bubble`/`carousel` コンテナ単体、または JSON 文字列を受け付ける。`{ ok, valid, warnings }` を返す。 |
| `get_content` | 現在 Canvas にある JSON を返す（**パネル上でのユーザー編集も反映**）。 |
| `validate`    | 現在（または渡した）JSON の軽量な構造チェック。 |

### open 入力

```jsonc
{
  "docId": "my-doc",        // 任意: 安定した id。再オープン時に内容を復元
  "content": { /* ... */ }, // 任意: 初期 Flex JSON
  "altText": "..."          // 任意
}
```

## パネル UI

- **左**: JSON エディタ（ライブ再描画、`Ctrl/Cmd+Enter` で強制描画、`Tab` でスペース挿入）。
- **右**: チャット背景上の LINE 風プレビュー（light/dark 切替）。
- ツールバー: **Render**（プレビュー更新）/ **Format**（整形）/ **Copy JSON**（JSON コピー）/
  **Load sample**（サンプル読込）/ **Toggle background**（背景切替）。

## レンダリング対応範囲

コンテナ `bubble`（nano〜giga）と `carousel`、ブロック `header`/`hero`/`body`/`footer`（`styles` 対応）、
コンポーネント `box`（horizontal/vertical/baseline）、`text`、`span`、`image`、`button`、`icon`、
`separator`、`filler`、`video`（プレビュー）。多くのレイアウト／スタイルプロパティに対応
（`flex`、`spacing`、`margin`、padding、borders、`cornerRadius`、`justifyContent`、`alignItems`、
`position`/`offset`、`gravity`、`align`、`wrap`、`maxLines`、`aspectRatio`/`aspectMode` など）。

> プレビューは LINE レンダラの **CSS による近似**です。キーワード→px のサイズは LINE の
> ドキュメント上の尺度に従いますが、厳密なピクセル値は LINE アプリと多少異なる場合があります。

## 状態／ストレージ

内容は `$COPILOT_HOME/extensions/line-flex-viewer/artifacts/<docId>.json`
（リポジトリ内には書きません）に `docId` をキーとして永続化され、再オープン時に復元されます。

## Copilot App 不要のブラウザプレビュー（standalone）

Canvas プレビューの実体は **ローカル Web アプリ**で、Copilot App の実行中は開いているパネルごとに
ループバック HTTP サーバ（`127.0.0.1:<ランダムポート>`）が立ち上がります。**Copilot App なし**で
プレビューしたい場合は、100% クライアントサイド（サーバもエージェントも不要）で同じレンダラを使う
standalone ページを利用します。

- **直接開く**: `web/standalone.html` をダブルクリック（`file://` で動作）。
- **または静的配信**（推奨。一部ブラウザは `file://` を制限）:

  ```bash
  cd .github/extensions/line-flex-viewer/web
  python -m http.server 8791          # → http://127.0.0.1:8791/standalone.html
  # または: npx serve .
  ```

standalone の機能:

- ライブエディタ＋ LINE 風プレビュー、**Format** / **Copy JSON** / **Load sample** / **Toggle background**。
- **Open file / Download** — Flex JSON を `.json` ファイルとして入出力。
- **Share link** — 現在の JSON を URL（`#json=<base64>`）にエンコードしてクリップボードにコピー。
  アプリもサーバ永続化も不要で、内容はリンク自体に含まれるため、誰とでもプレビューリンクを共有可能。
- `localStorage` に自動保存し、リロード時に直近の編集を復元。

読み込み時のシード優先順位: URL の `#json=` ハッシュ ＞ `localStorage` ＞ 先頭サンプル。

Copilot App が起動している間は、同じ standalone ページに
`http://127.0.0.1:<パネルポート>/standalone.html`（パネル URL ＋ `/standalone.html`）でもアクセスできます。

## Claude（Desktop / Code）など任意の MCP クライアントで使う

Claude Desktop / Claude Code は Canvas ではなく **MCP サーバ**で拡張します。本リポジトリには
同じレンダラを使う MCP サーバ（`mcp/server.mjs`、**Node 標準機能のみ・依存パッケージゼロ**）を同梱しています。
AI が Flex JSON を組み立てて `preview_flex_message` を呼ぶと、ローカルにプレビュー用サーバを立ち上げ、
**既定のブラウザでライブプレビュー**を開きます（以降の更新は SSE で自動反映）。

### 提供する MCP ツール

| ツール | 説明 |
| ---- | ---- |
| `preview_flex_message` | Flex JSON（flex メッセージ / bubble / carousel / JSON 文字列）をブラウザでプレビュー。初回はブラウザを自動で開き、以降はライブ更新。`{ url, valid, warnings }` を返す。 |
| `get_flex_content` | プレビュー中の JSON を返す（**ブラウザ上でのユーザー編集も反映**）。 |
| `validate_flex_message` | JSON を構造検証（ブラウザ不要）。`{ valid, warnings }`。 |
| `open_preview` | 内容を変えずにプレビュータブを開き直し URL を返す。 |

### セットアップ

必要なのは Node.js 18+ だけです（`npm install` 不要）。`<REPO>` は本リポジトリを配置した絶対パスに置き換えてください。

**Claude Desktop** — `claude_desktop_config.json`（macOS: `~/Library/Application Support/Claude/`、
Windows: `%APPDATA%\Claude\`）に追記:

```jsonc
{
  "mcpServers": {
    "line-flex-viewer": {
      "command": "node",
      "args": ["<REPO>/.github/extensions/line-flex-viewer/mcp/server.mjs"]
    }
  }
}
```

**Claude Code** — CLI で登録:

```bash
claude mcp add line-flex-viewer -- node <REPO>/.github/extensions/line-flex-viewer/mcp/server.mjs
```

登録後、Claude に「この Flex Message をプレビューして」と頼めば `preview_flex_message` が呼ばれ、
ブラウザにプレビューが表示されます。ブラウザ側で JSON を微調整すると、Claude は
`get_flex_content` で編集後の内容を読み戻せます。

### 環境変数（任意）

| 変数 | 既定 | 用途 |
| ---- | ---- | ---- |
| `LINE_FLEX_MCP_NO_OPEN` | (未設定) | セットするとブラウザ自動起動を無効化（URL は返す）。 |
| `LINE_FLEX_MCP_STATE_DIR` | OS の一時ディレクトリ | プレビュー内容の保存先。 |
| `LINE_FLEX_MCP_HTML` | `viewer.html` | 配信するページ。`standalone.html` に変更も可。 |

> **MCP に興味がない場合**は、上記「Copilot App 不要のブラウザプレビュー（standalone）」だけでも
> ブラウザ単体で完結します。MCP サーバは「AI ↔ プレビュー」を自動連携したいとき向けです。

## 公開／共有

このフォルダには `copilot-extension.json` が含まれているため、コマンドパレットの
「Share extension as gist…」または `share_extension` ツールでプライベート gist として共有でき、
別環境で「Install extension from gist…」／ `install_extension` からインストールできます。
