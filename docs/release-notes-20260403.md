# リリースノート（2026-04-03 以降）

## 売上登録（bmcs_app.Sales）

### 明細行をユーザーコントロール（SaleLineControl）に変更
DataGrid から ItemsControl + UserControl 方式に移行。明細行ごとに独立したコマンドを持ち、フォーカス制御・条件付き入力可否・ComboBox バインディングが容易になった。

### 原価表示・粗利表示
明細に原価列を追加（商品マスタから自動セット、編集不可）。フッターに粗利額を表示。

### 納品書プリンタ設定対応
`bmcs_printer_settings.json` に保存されたプリンタ設定を使用してダイアログなし印刷。未設定時は従来の印刷ダイアログにフォールバック。

---

## 入金登録（bmcs_app.Receipt）

### 明細行をユーザーコントロール（ReceiptLineControl）に変更
売上登録と同じ ItemsControl + UserControl 方式を採用。

---

## 締処理・請求書（bmcs_app.Closing）

### 請求書プリンタ設定対応
請求書印刷でも `bmcs_printer_settings.json` の設定プリンタへダイアログなし印刷。

### 売掛金集計（ArClosingViewModel）改修
売掛金集計処理の更新。

---

## マスタ保守（bmcs_app.Master）

### 得意先マスタに住所フィールドを追加
得意先登録画面に郵便番号・住所1・住所2の入力欄を追加。

---

## ランチャー（bmcs_app）

### プリンタ設定画面を追加
ランチャーからプリンタ設定ウィンドウを開き、納品書・請求書ごとに使用プリンタを選択して保存できる。設定は `bmcs_printer_settings.json` に保存。

---

## 共通（bmcs_app.Shared / bmcs_app.Infrastructure）

### 伝票検索ダイアログを専用クラスに分離（SlipSearchDialog）
マスタ検索ダイアログ（MasterSearchDialog）とは独立した SlipSearchDialog を新設。非正規化フラットビューで全明細を一覧表示し、全列をキーワード絞り込みの対象とする。起動時にデータをキャッシュしてダイアログ表示を高速化。

### 売上・入金伝票に GetAllFlatAsync() を追加
伝票検索ダイアログ用に非正規化済みの全明細データを一括取得するメソッドをリポジトリに追加。

### 得意先住所を売上・入金伝票に保存
売上・入金保存時に得意先マスタから住所（郵便番号・住所1・住所2）を自動取得して伝票テーブルに保存。印刷用途で使用。

### 請求書ヘッダーに得意先住所を保存
invoice_headers テーブルに得意先住所を保持するよう対応。

### MasterSearchDialog に日付列を追加（オプション）
SearchItem の Date フィールドに値が設定されている場合のみ日付列を自動表示。
