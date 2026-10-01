# bmcs_app 配布・リリース

---

## 1. 端末の前提

| 項目 | 内容 |
|---|---|
| OS | Windows 11 以降 |
| ランタイム | .NET 10 Desktop Runtime |
| 印刷 | 使用するA4プリンタ。PDF保存用に「Microsoft Print to PDF」が有効であること |
| フォント | メイリオ（Meiryo UI。Windows標準） |
| ネットワーク | DBサーバに到達できること |

台数・本番DBサーバは未定。決定後に追記する。

---

## 2. publish

```
dotnet publish src/bmcs_app -c Release -r win-x64 --no-self-contained -o <出力先>
```

- 出力先の `appsettings.json`（ログレベル）は常に含まれる。
- `appsettings.Development.json`（接続文字列）は、ビルド時に存在する場合だけ出力へコピーされる。**開発者の接続文字列を含んだまま配布しない。** 配布物からは削除し、配布先ごとに作成する。
- 本番では `Development` 環境名に関係なく `appsettings.Development.json` が読み込まれる。ファイル名はそのままにする。

---

## 3. 配布

共有フォルダに publish 出力を置き、各端末へ手動でコピーする。

1. 利用者にアプリを終了してもらう。
2. 端末の配置先フォルダへコピーする。**`appsettings.Development.json` と `bmcs_config.json` は上書き・削除しない**（コピー対象から除外する）。
3. 起動して、メインメニューのフッターの接続先が正しいことを確認する。

DBスキーマの変更を伴うリリースは、先に `scripts/` の新しい連番SQLを適用し（`docs/database-schema.md` 3.2節）、その後にアプリを配る。旧版のアプリが新スキーマで動くかを確認し、動かない場合は全端末の入れ替え完了まで業務を止める。

---

## 4. 本番の接続

`appsettings.Development.json` の `ConnectionStrings:BmcsDb` を本番DBに向ける（書式は `docs/setup.md` 3章）。本番DBサーバは未定。接続ユーザーは、`bmcs_db` のみに権限を絞った専用ログインを推奨する。

---

## 5. `bmcs_config.json`

プリンタ設定の保存先。**端末ごと**にアプリの実行ファイルと同じフォルダに置かれ、DBには保存しない。

| キー | 内容 |
|---|---|
| `deliverySlipPrinter` | 納品書のプリンタ名 |
| `invoicePrinter` | 請求書のプリンタ名 |
| `lineInvoicePrinter` | 明細請求書のプリンタ名 |

- すべて文字列で、省略可。ファイルが無ければ全項目が未設定になる。
- メインメニューの「プリンタ設定」画面が読み書きする。手で編集しなくてよい。
- 実行ファイルのフォルダに書き込めること。再配布でフォルダを入れ替えると消えるため、上書き除外にする（3章）。
