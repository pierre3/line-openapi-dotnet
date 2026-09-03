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
- **AI エージェントと分担して作れる** — エージェント（例:
  [`Line.OpenApi.Tools`](https://github.com/pierre3/line-openapi-dotnet) の MCP サーバ）が JSON を
  組み立て、細かい調整は手作業で行い、その結果をエージェントが読み戻して作業を続けられます。

## 3 通りの使い方

| | 向いている場面 |
| --- | --- |
| **Copilot canvas** | Copilot App のサイドパネルで、エージェントが作ったメッセージをその場で確認する。 |
| **MCP サーバ** | Claude Desktop / Claude Code など。プレビューがブラウザで開く。 |
| **standalone ページ** | アプリも設定もなしで手早く確認したいとき。HTML を開くだけ。 |

3 つとも同じレンダラを使っているので、どこで見ても仕上がりは同じです。

## インストール

公開リポジトリなので、canvas 拡張はフォルダの URL から直接入れるのが一番簡単です。

```
install_extension https://github.com/pierre3/line-openapi-dotnet/tree/main/extensions/line-flex-viewer
```

Copilot canvas はこれだけで使えます。MCP サーバと standalone ページは、それぞれ以下のとおりで、
別途インストールは不要です。

## 使い方

### Copilot App（canvas）

エージェントに「この Flex Message をプレビューして」と頼むと、canvas が開いて JSON が渡され、
サイドパネルにすぐ描画されます。あとは次のように操作します。

- 左側で JSON を編集すると、右側のプレビューが入力に合わせて更新されます（`Ctrl/Cmd+Enter` で
  手動更新）。
- ツールバーから **整形（Format）**・**JSON をコピー**・**サンプル読込**・**背景の切替**ができます。
- 編集内容は自動で保存され、同じドキュメントを開き直すと復元されます。

内部ではエージェントが 3 つのアクションを使います。`set_content`（JSON を表示・差し替え）、
`get_content`（編集結果を読み戻す）、`validate`（構造の簡易チェック）です。

### Claude Desktop / Claude Code（MCP）

Claude は canvas ではなく MCP サーバで拡張します。そこで、この拡張には依存パッケージのない小さな
MCP サーバ（`mcp/server.mjs`）を同梱しています。AI がメッセージをプレビューすると、ローカルで
プレビューを立ち上げて既定のブラウザで開き、その後の変更もそのまま反映されます。

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

任意の設定:

| 変数 | 既定値 | 用途 |
| --- | --- | --- |
| `LINE_FLEX_MCP_NO_OPEN` | （未設定） | ブラウザを自動で開かない（URL は返す）。 |
| `LINE_FLEX_MCP_STATE_DIR` | OS の一時ディレクトリ | 現在のプレビュー内容の保存先。 |
| `LINE_FLEX_MCP_HTML` | `viewer.html` | 配信するページ。`standalone.html` にすると client-side 版になる。 |

### 単体で使う（standalone ページ）

アプリを使わず手早く見たいときは、`web/standalone.html` をブラウザで開きます。サーバもエージェント
も使わず、ブラウザだけで完結します。

- ファイルをダブルクリックで開けます（`file://` で動作）。ローカルファイルの扱いが厳しいブラウザ
  では、フォルダを配信してください。

  ```bash
  cd extensions/line-flex-viewer/web
  python -m http.server 8791     # → http://127.0.0.1:8791/standalone.html
  ```

- ライブ編集やツールバーに加えて、JSON ファイルの**読み込み・書き出し**や、**共有リンク**の作成が
  できます。共有リンクはメッセージ全体を URL（`#json=...`）に埋め込むので、リンクを開いた人は同じ
  プレビューを見られます。直前の編集はブラウザに記憶されます。

## プレビューの対応範囲

コンテナは `bubble`（nano〜giga）と `carousel`。ブロックは `header` / `hero` / `body` / `footer`
（`styles` 込み）。コンポーネントは `box`（horizontal / vertical / baseline）、`text`、`span`、
`image`、`button`、`icon`、`separator`、`filler`、`video`。レイアウトやスタイルの多くに対応します
（`flex`、`spacing`、`margin`、padding、borders、`cornerRadius`、`justifyContent`、`alignItems`、
`position` / `offset`、`gravity`、`align`、`wrap`、`maxLines`、`aspectRatio` / `aspectMode` など）。

> プレビューは LINE のレンダラを **CSS で近似**したものです。サイズは LINE の資料に沿っていますが、
> 厳密なピクセル値は LINE アプリと多少ずれることがあります。まずここで見た目を固め、最終確認は
> 実機で行うのがおすすめです。

## 関連

この拡張ではなく、`line` コマンドライン／MCP ツールからプレビューを動かしたい場合は、同じレンダラを
[`Line.OpenApi.Tools`](https://github.com/pierre3/line-openapi-dotnet) が `line_flex_*` ツールとして
提供しています。

## gist で共有する（任意）

フォルダには `copilot-extension.json` が含まれているので、プライベート gist としての共有
（「Share extension as gist…」／ `share_extension`）と、他環境での `install_extension` によるインストール
も可能です。ただし公開リポジトリなら、上記のフォルダ URL からのインストールのほうが簡単です。
