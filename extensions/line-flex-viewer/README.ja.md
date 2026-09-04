# LINE Flex Message ビューア

*English version: [README.md](./README.md).*

[LINE の Flex Message](https://developers.line.biz/ja/docs/messaging-api/using-flex-messages/)
を作るとき、その JSON を LINE アプリと同じ見た目でその場に表示するツールです。Flex Message の
JSON は記述量が多く、書いただけでは仕上がりが想像しにくいもの。編集するそばからプレビューが
更新されるので、送る前にレイアウトや色をきちんと確認できます。

LINE アカウントや API キー、ネットワーク接続は必要ありません。プレビューはすべて手元の PC の
中だけで動きます。

## できること

- **LINE と同じ見た目で確認できる** — bubble・carousel、画像、ボタンなど、主要なレイアウト／
  スタイルを、チャット風の背景（ライト／ダーク切替あり）の上に描画します。
- **編集しながらリアルタイムに反映** — エディタで JSON を書き換えると、プレビューがすぐ更新
  されます。余白・色・文言を調整して、狙いどおりの見た目に近づけられます。
- **AI エージェントと分担して作れる** — エージェントが JSON を組み立て、細かい調整は手作業で行い、
  その結果をエージェントが読み戻して作業を続けられます。

## 主な使い方

使う場面は大きく 2 つです。どちらも同じレンダラを使っているので、仕上がりは変わりません。

### 1. Copilot App の Canvas

canvas 拡張を、このリポジトリのフォルダ URL から直接入れます。

```
install_extension https://github.com/pierre3/line-openapi-dotnet/tree/main/extensions/line-flex-viewer
```

あとはエージェントに「この Flex Message をプレビューして」と頼むと、canvas が開いて JSON が渡され、
サイドパネルにすぐ描画されます。操作は次のとおりです。

- 左側で JSON を編集すると、右側のプレビューが入力に合わせて更新されます（`Ctrl/Cmd+Enter` で
  手動更新）。
- ツールバーから **整形（Format）**・**JSON をコピー**・**サンプル読込**・**背景の切替**ができます。
- 編集内容は自動で保存され、同じドキュメントを開き直すと復元されます。

内部ではエージェントが 3 つのアクションを使います。`set_content`（JSON を表示・差し替え）、
`get_content`（編集結果を読み戻す）、`validate`（構造の簡易チェック）です。

### 2. Line.OpenApi.Tools の MCP（`line_flex_*`）

すでに [`Line.OpenApi.Tools`](https://github.com/pierre3/line-openapi-dotnet/tree/main/tools) の `line` コマンドライン／
MCP ツールを使っているなら、同じレンダラが `line_flex_*` ツールとして組み込まれています（別途の
インストールは不要）。エージェントが `line_flex_preview` を呼ぶとブラウザにプレビューが開き、編集の
たびにその場で更新されます。ブラウザで加えた編集は `line_flex_get_content` で読み戻せます。詳しくは
そのツールの README を参照してください。Claude Desktop / Claude Code などの MCP クライアントでは、
これが基本の使い方になります。

## 代替: 同梱の Node MCP サーバ

MCP でプレビューしたいけれど .NET の `line` ツールは使わない、という場合のために、このフォルダには
依存パッケージのない小さな MCP サーバ（`mcp/server.mjs`）も同梱しています。上記 2 の代わりに使える
手段で、.NET ツールを入れたくないときに便利です。

登録方法（Node.js 18 以上が必要。`npm install` は不要）。`<REPO>` は、このリポジトリを配置した
パスに置き換えてください。

**Claude Desktop** — `claude_desktop_config.json`（macOS:
`~/Library/Application Support/Claude/`、Windows: `%APPDATA%\Claude\`）に次を追記します。

```jsonc
{
  "mcpServers": {
    "line-flex-viewer": {
      "command": "node",
      "args": ["<REPO>/extensions/line-flex-viewer/mcp/server.mjs"]
    }
  }
}
```

**Claude Code**:

```bash
claude mcp add line-flex-viewer -- node <REPO>/extensions/line-flex-viewer/mcp/server.mjs
```

登録後、Claude に「この Flex Message をプレビューして」と頼むと、次のツールが使えます。

| ツール | 役割 |
| --- | --- |
| `preview_flex_message` | Flex JSON を描画し、ブラウザのプレビューを開く／更新する。 |
| `get_flex_content` | いま表示されている JSON を返す（**ブラウザで加えた編集も含む**）。 |
| `validate_flex_message` | ブラウザを開かずに JSON の構造をチェックする。 |
| `open_preview` | 閉じてしまったプレビュータブを開き直す。 |

任意の設定として、`LINE_FLEX_MCP_NO_OPEN`（ブラウザを自動で開かず URL だけ返す）と
`LINE_FLEX_MCP_STATE_DIR`（現在のプレビュー内容の保存先）があります。

### ローカルの画像・動画をプレビューする

`LINE_FLEX_MCP_ASSET_DIR` に自分で用意したメディアのフォルダを指定すると、プレビューサーバが
その配下のファイルを配信します。Flex メッセージからは **相対** `url`（例 `"assets/hero.png"`）で
参照でき、本番移行時は origin だけ HTTPS の CDN に差し替えれば Flex JSON はそのまま使えます。
これは MCP サーバと Copilot キャンバス拡張の両方で有効です。

- **オプトイン** — `LINE_FLEX_MCP_ASSET_DIR` を設定したときだけ配信します（未設定なら無効、ファイルが
  無ければ 404）。
- **封じ込め** — 配信されるのはそのディレクトリ配下のファイルだけ（パストラバーサル・絶対パス・
  ディレクトリ外へ抜けるシンボリックリンクは拒否）で、ループバックのプレビューサーバ経由に限られます。
- **対応メディア** — LINE が Flex メッセージで実際に描画する形式に一致します。画像は
  `.png`/`.jpg`/`.jpeg`（APNG は `.png`）、`video` コンポーネント用に `.mp4`。その他（GIF・WebP）は
  拒否します。LINE 自身はローカル/`data:` の url を描画しないため、これはプレビュー専用の利便機能です。

## プレビューの対応範囲

| 分類 | 対応するもの |
| --- | --- |
| **コンテナ** | `bubble`（サイズ `nano`〜`giga`）、`carousel` |
| **ブロック** | `header`・`hero`・`body`・`footer`（`styles` 込み） |
| **コンポーネント** | `box`（`horizontal` / `vertical` / `baseline`）、`text`、`span`、`image`、`button`、`icon`、`separator`、`filler`、`video` |
| **レイアウト／スタイル** | `flex`、`spacing`、`margin`、padding、borders、`cornerRadius`、`justifyContent`、`alignItems`、`position` / `offset`、`gravity`、`align`、`wrap`、`maxLines`、`aspectRatio` / `aspectMode` など |

> プレビューは LINE のレンダラを **CSS で近似**したものです。サイズは LINE の資料に沿っていますが、
> 厳密なピクセル値は LINE アプリと多少ずれることがあります。まずここで見た目を固め、最終確認は
> 実機で行うのがおすすめです。

## 開発

キャンバス拡張（`extension.mjs`）と同梱の MCP サーバ（`mcp/server.mjs`）は、ローカルメディア配信の
ロジックを `lib/assets.mjs` で共有しています。パス封じ込めとホストガードの挙動は、依存ゼロの
テストスイート（Node 標準のテストランナー）で検証しています。

```bash
node --test lib/assets.test.mjs   # または: cd mcp && npm test
```
