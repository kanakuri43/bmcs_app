# リリースノート（2026-04-06 以降）

> 対象コミット: `729fa54` (2026-04-06) 〜 `4f37f49` (2026-04-10)  
> ブランチ: develop

---

## 受注登録（bmcs_app.Order）— 新機能

### 受注登録機能を新規実装
受注伝票の登録・編集・削除・印刷をフルサポート。

- **モデル追加**: `OrderSlip` / `OrderLine` / `OrderLineInput`
- **インターフェース追加**: `IOrderRepository`（`GetAllAsync` / `GetByOrderNoAsync` / `GetAllFlatAsync` / `UpsertAsync` / `DeleteAsync`）
- **リポジトリ追加**: `OrderRepository`（Infrastructure）
- **ViewModel 追加**: `OrderMainViewModel` / `OrderLineViewModel`
- **UC 追加**: `OrderLineControl.xaml`（明細行ユーザーコントロール）
- **View 追加**: `OrderMainView.xaml`（フル伝票レイアウト）
- **LookupService 追加**: 受注登録専用の `LookupService`

**App.xaml.cs の変更点**:
- 起動時に得意先・社員・商品・受注伝票をキャッシュ
- コマンドライン引数で受注No.を指定すると、起動直後にその伝票を表示

---

## 売上登録（bmcs_app.Sales）

### 受注参照機能を追加
ヘッダーに「受注No.」欄を追加。受注No. を入力して Enter を押すと、対応する受注伝票から得意先・担当者・明細行を自動転記する。

- `SalesMainViewModel` に `IOrderRepository` を追加
- `ApplyOrder()` メソッドで受注ヘッダー・明細を売上フォームへ転記
- 受注No. 検索ダイアログ（SlipSearchDialog）と Enter による直接補完の両方に対応

### ランチャーから受注No.渡しに対応
`bmcs_app.exe` から売上登録を起動するとき、コマンドライン引数で受注No.を渡して直接連携起動できる。

---

## 締処理・請求書（bmcs_app.Closing）

### 請求書印刷を「請求日指定」のみに変更
従来は請求日 + 締め日（closingDay）の組み合わせで請求書を特定していたが、請求日のみで特定できるよう変更。

- `GetInvoiceHeadersAsync` のシグネチャから `closingDay` パラメータを削除
- SP `usp_invoice_headers_select` の `@closing_day` パラメータ廃止
- `InvoiceHistorySummary` の `ClosingDay` プロパティ・`ClosingDayLabel` を削除

### 請求書レイアウトの改修
- `InvoicePrintData` に `CustomerCode`・`SalesTotalStr`（売上合計）・`TaxTotalStr`（消費税合計）を追加
- 売上明細と入金明細を日付順にまとめた `InvoiceMixedLine` リストで出力（従来は売上・入金を別リストで管理）
- 従来の `InvoiceSlipLine` / `InvoiceReceiptLine` クラスを統合

---

## ランチャー（bmcs_app）

### 自社情報管理画面を新設（「サーバ」設定を置き換え）
ランチャーメニューの「サーバ」ボタンを「自社情報」に変更し、機能未実装の `ServerSettingsWindow` を削除。  
代わりに `CompanyInfoSettingsWindow` を新設し、以下の項目を編集・保存できる。

| 項目 | 説明 |
|---|---|
| 会社名 | 請求書・納品書の差出人名 |
| 住所 | 同上 |
| 電話 / FAX | 同上 |
| インボイス登録番号 | T + 13桁 |
| 振込先口座 1〜3 | 請求書の振込先として印字（任意） |

- 保存先: `company_info` テーブル（SP `usp_company_info_upsert` 経由）
- `CompanyInfo` モデルに `BankAccountNumber1〜3` を追加
- `CompanyInfoRepository.UpsertAsync` を新規追加

---

## 共通（bmcs_app.Shared / bmcs_app.Infrastructure）

### 売上登録の明細行をユーザーコントロール化（SaleLineControl）
DataGrid から ItemsControl + UserControl 方式に変更（受注登録の明細行 UC と同一パターン）。

### 入金登録の明細行をユーザーコントロール化（ReceiptLineControl）
売上登録と同じ ItemsControl + UserControl 方式を採用。

### 伝票検索ダイアログを共通ライブラリに移管（SlipSearchDialog）
従来は `bmcs_app.Sales` 内に配置していたが、`bmcs_app.Shared` に移管。  
売上・受注・入金それぞれで共有利用できる。

---

## DB スキーマ変更（scripts/）

| ファイル | 内容 |
|---|---|
| `alter_cost_price.sql` | 商品マスタ・明細テーブルに原価カラムを追加 |
| `alter_customers_address.sql` | 得意先マスタに郵便番号・住所1・住所2を追加 |
| `alter_invoice_headers_address.sql` | `invoice_headers` に得意先住所カラムを追加 |
| `alter_journal_address.sql` | 売上・入金ジャーナルに得意先住所カラムを追加 |

> `company_info` テーブルへの `bank_account_number1〜3` カラム追加は SP 側での対応が必要。  
> `usp_invoice_headers_select` から `@closing_day` パラメータを削除すること。
