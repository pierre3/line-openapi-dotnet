# G0 設計方針レビュー結果（rev.2 再レビュー）

- **ゲート:** G0（設計・再レビュー）
- **担当:** アーキテクチャ/テスト観点レビュアー（サブエージェント）
- **対象:** LINE-dotnet-client-design.md rev.2
- **日付:** 2026-07-09
- **総合判定:** **PASS**（実装/G1 で解消すべき中程度指摘あり）

前回の必須4件はいずれも事実に即した確定対処として明記され、新パッケージ構成にも致命的破綻なし。残る指摘は依存方向1点（中程度）と軽微数点で、G1 進行の妨げにはならない。

---

## 前回必須指摘 4件の解消状況

1. **複数 base URL — 解消。** §4.4 で確定制約として明記。分離生成 + `RequestAdapter.BaseUrl` 上書き + ファサード統合 + 補助 `WithUrl`。「分離生成だけではホストが変わらない」但し書きも正確。
2. **structured-mime-types（form-urlencoded）— 解消。** §5 で token 系のみ form-urlencoded を追加。`-m` はデフォルトを上書きするため json 単独だと form が stream に退化する、という理解も正しい。
3. **ロードマップ/PoC スコープ — 解消。** §12 に最小認証・messaging 制御系+data系・form-urlencoded トークン・webhook 多態・LIFF スモークが全て含まれる。
4. **webhook 多態 + MissingDiscriminator — 解消。** §4.3・§10・R7 で検証項目化。

---

## 新パッケージ構成へのレビュー

### 中程度（要解消）

- **短期トークンプロバイダの依存方向の矛盾:** 宣言依存は `Line.ChannelAccessToken → Line.Core`（一方向）。一方 §7 は「短期トークンは Line.ChannelAccessToken クライアントで発行」しつつプロバイダ実体を Core とする。更新型プロバイダを Core に置くと `Core → ChannelAccessToken` の逆依存（循環）が発生。整理: **抽象（`IAccessTokenProvider` 等）と静的トークンプロバイダは Core、ChannelAccessToken クライアントを消費する更新型プロバイダは Line.ChannelAccessToken（または上位）に配置**と明記すべき。

### 軽微〜中程度

- **AllowedHostsValidator の Core 固定:** 将来 Module（`manager.line.biz`）追加時に許可ホスト拡張が必要。ハードコードでなくパッケージ側から注入・拡張可能にする旨を残す。
- **--include-path によるホスト分離の前提:** パスフィルタでありホスト直接指定ではない。コンテンツ取得系（`getMessageContent` 等）が識別可能な固有パスを持つ前提で成立。実仕様上は成立するが前提として一言。
- **命名の含意:** `Line.Messaging.Webhook` は名前上 `Line.Messaging` 下位に見えるが依存は Core のみ（送受信独立）。許容範囲だが誤解余地あり。
- **多パッケージの SemVer/Kiota ランタイム版そろえ:** 全パッケージが Core+Kiota 版に連動。パッケージ横断のロックステップ運用を明文化推奨。

### 問題なしと確認した点

- 一方向依存で Core 集約、循環なし（上記トークン依存の整理を除く）。
- messaging を json 単独生成 → バイナリが stream に退化するのはむしろ正しい挙動。
- メタパッケージ `Line.Bot`（依存束ねのみ、LIFF 除外）は NuGet 慣行として妥当。将来4仕様も Core 依存で後付け可能、拡張余地確保。

---

## 残る指摘

- 短期トークン更新プロバイダの配置を明記し Core への逆依存を回避（中）。
- AllowedHosts の許可ホストを注入可能にし将来ホスト（`manager.line.biz`）へ拡張余地を残す。
- ホスト分離が「data 系操作の固有パス存在」に依存する前提を明記。
- 多パッケージのバージョン協調（Core+Kiota）運用を明文化。

---

## G1（仕様レビュー）への結論

**進んでよい。** 前回 CONCERNS の必須4件はすべて事実に即して解消済み。新パッケージ構成も根本的破綻なし。残る中程度指摘（トークンプロバイダの依存方向）は設計文言の整理事項で、G1（MultipleServerEntries の実発生箇所、oneOf/discriminator の有無、form-urlencoded スキーマ、netstandard2.0 ビルド実証）と並行で足りる。
