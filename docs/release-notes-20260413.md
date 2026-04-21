# リリースノート（2026-04-03 〜 2026-04-13）

> 対象コミット: `0f76b8b` (2026-04-06) 〜 `75f2489` (2026-04-13)  
> ブランチ: develop

---

## 買掛側機能群 — 新規実装（2026-04-13）

売上・入金・請求の「売掛側」に続き、発注〜支払の「買掛側」機能を一括実装。

### 仕入先マスタ保守（bmcs_app.Master）

- `SupplierMaintView` / `SupplierMaintViewModel` を新規追加
- SP: `usp_suppliers_upsert` / `usp_suppliers_delete`
- ランチャーメニューに「仕入先」ボタンを追加

### 発注登録（bmcs_app.PurchaseOrder）— 新規プロジェクト

発注伝票の登録・編集・削除をフルサポート。

- **モデル追加**: `PurchaseOrderSlip` / `PurchaseOrderLine` / `PurchaseOrderLineInput`
- **インターフェース追加**: `IPurchaseOrderRepository`
- **リポジトリ追加**: `PurchaseOrderRepository`
- **ViewModel 追加**: `PurchaseOrderMainViewModel` / `PurchaseOrderLineViewModel`
- **UC 追加**: `PurchaseOrderLineControl.xaml`
- **View 追加**: `PurchaseOrderMainView.xaml`
- **SP 追加**: `usp_purchase_orders_upsert` / `usp_purchase_orders_delete` / `usp_purchase_orders_select` / `usp_purchase_orders_summaries_select`

### 仕入登録（bmcs_app.Purchase）— 新規プロジェクト

仕入伝票の登録・編集・削除をフルサポート。

- **モデル追加**: `PurchaseSlip` / `PurchaseLine` / `PurchaseLineInput`
- **インターフェース追加**: `IPurchaseRepository`
- **リポジトリ追加**: `PurchaseRepository`
- **ViewModel 追加**: `PurchaseMainViewModel` / `PurchaseLineViewModel`
- **UC 追加**: `PurchaseLineControl.xaml`
- **View 追加**: `PurchaseMainView.xaml`
- **SP 追加**: `usp_purchases_upsert` / `usp_purchases_delete` / `usp_purchases_select` / `usp_purchases_summaries_select`

### 支払登録（bmcs_app.Payment）— 新規プロジェクト

支払伝票の登録・編集・削除をフルサポート。

- **モデル追加**: `PaymentSlip` / `PaymentLine` / `PaymentLineInput`
- **インターフェース追加**: `IPaymentRepository`
- **リポジトリ追加**: `PaymentRepository`
- **ViewModel 追加**: `PaymentMainViewModel` / `PaymentLineViewModel`
- **UC 追加**: `PaymentLineControl.xaml`
- **View 追加**: `PaymentMainView.xaml`
- **SP 追加**: `usp_payments_upsert` / `usp_payments_delete` / `usp_payments_select` / `usp_payments_summaries_select`

### 仕入横断検索（bmcs_app.PurchaseSearch）— 新規プロジェクト

発注・仕入・支払の3伝票を横断キーワード検索。

- **インターフェース追加**: `IPurchaseSearchRepository`
- **リポジトリ追加**: `PurchaseSearchRepository`
- **ViewModel 追加**: `PurchaseSearchMainViewModel`
- **View 追加**: `PurchaseSearchMainView.xaml`

### 伝票横断検索（bmcs_app.Search）に買掛側を追加

- `SearchRepository` に発注・仕入・支払の検索ロジックを追加
- `SearchMainViewModel` / `SearchMainView.xaml` を更新

---

## 受注登録（bmcs_app.Order）— 新規実装（2026-04-09）

受注伝票の登録・編集・削除をフルサポート。

- **モデル追加**: `OrderSlip` / `OrderLine` / `OrderLineInput`
- **インターフェース追加**: `IOrderRepository`（`GetAllAsync` / `GetByOrderNoAsync` / `GetAllFlatAsync` / `UpsertAsync` / `DeleteAsync`）
- **リポジトリ追加**: `OrderRepository`
- **ViewModel 追加**: `OrderMainViewModel` / `OrderLineViewModel`
- **UC 追加**: `OrderLineControl.xaml`
- **View 追加**: `OrderMainView.xaml`
- **LookupService 追加**: 受注登録専用
- コマンドライン引数で受注No. を指定すると、起動直後にその伝票を表示

---

## 売上登録（bmcs_app.Sales）

### 受注参照機能を追加（2026-04-10）

ヘッダーに「受注No.」欄を追加。受注No. を入力して Enter を押すと、対応する受注伝票から得意先・担当者・明細行を自動転記。

- `SalesMainViewModel` に `IOrderRepository` を追加
- `ApplyOrder()` メソッドで受注ヘッダー・明細を売上フォームへ転記
- 受注No. 検索ダイアログ（SlipSearchDialog）と Enter による直接補完の両方に対応
- ランチャーから受注No. を引数渡しして連携起動に対応

### 原価表示・粗利表示（2026-04-07）

明細に原価列を追加（商品マスタから自動セット・編集不可）。フッターに粗利額を表示。

### 明細行をユーザーコントロール化（SaleLineControl）（2026-04-06）

DataGrid から ItemsControl + UserControl 方式に移行。フォーカス制御・条件付き入力可否・ComboBox バインディングが容易になった。

---

## 入金登録（bmcs_app.Receipt）

### 明細行をユーザーコントロール化（ReceiptLineControl）（2026-04-06）

売上登録と同じ ItemsControl + UserControl 方式を採用。

---

## 締処理・請求書（bmcs_app.Closing）

### 請求書印刷を「請求日指定」のみに変更（2026-04-10）

従来は請求日 + 締め日（closingDay）の組み合わせで請求書を特定していたが、請求日のみで特定できるよう変更。

- `GetInvoiceHeadersAsync` のシグネチャから `closingDay` パラメータを削除
- SP `usp_invoice_headers_select` の `@closing_day` パラメータ廃止
- `InvoiceHistorySummary` の `ClosingDay` プロパティ・`ClosingDayLabel` を削除

### 請求書レイアウトの改修（2026-04-08）

- `InvoicePrintData` に `CustomerCode`・`SalesTotalStr`・`TaxTotalStr` を追加
- 売上明細と入金明細を日付順にまとめた `InvoiceMixedLine` リストで出力
- 従来の `InvoiceSlipLine` / `InvoiceReceiptLine` クラスを統合

### 請求書プリンタ設定対応（2026-04-07）

`bmcs_printer_settings.json` の設定プリンタへダイアログなし印刷。

---

## マスタ保守（bmcs_app.Master）

### 得意先マスタに住所フィールドを追加（2026-04-07）

得意先登録画面に郵便番号・住所1・住所2の入力欄を追加。

---

## ランチャー（bmcs_app）

### 自社情報管理画面を新設（2026-04-10）

ランチャーメニューの「サーバ」ボタンを「自社情報」に変更。`CompanyInfoSettingsWindow` で以下の項目を編集・保存できる。

| 項目 | 説明 |
|---|---|
| 会社名 | 請求書・納品書の差出人名 |
| 住所 | 同上 |
| 電話 / FAX | 同上 |
| インボイス登録番号 | T + 13桁 |
| 振込先口座 1〜3 | 請求書の振込先として印字（任意） |

- 保存先: `company_info` テーブル（SP `usp_company_info_upsert` 経由）

### プリンタ設定画面を追加（2026-04-07）

ランチャーからプリンタ設定ウィンドウを開き、納品書・請求書ごとに使用プリンタを選択して保存できる。設定は `bmcs_printer_settings.json` に保存。

### 買掛側メニューを追加（2026-04-13）

ランチャーに「発注」「仕入」「支払」「仕入横断検索」ボタンを追加。

---

## 共通（bmcs_app.Shared / bmcs_app.Core / bmcs_app.Infrastructure）

### TaxCalculator を Core に共通化（2026-04-13）

`bmcs_app.Core/Services/TaxCalculator.cs` を追加。各プロジェクトで同一の税計算ロジックを共有。

### FocusHelper を Shared に統合（2026-04-13）

各プロジェクト（受注・売上・入金）に個別に存在した `FocusHelper` を `bmcs_app.Shared/Helpers/FocusHelper.cs` に統合。

### 伝票検索ダイアログを共通ライブラリに移管（SlipSearchDialog）（2026-04-07）

従来は `bmcs_app.Sales` 内に配置していたが、`bmcs_app.Shared` に移管。売上・受注・入金で共有利用できる。

### MasterSearchDialog に日付列を追加（2026-04-07）

`SearchItem` の `Date` フィールドに値が設定されている場合のみ日付列を自動表示。

### 得意先住所を売上・入金伝票に保存（2026-04-07）

売上・入金保存時に得意先マスタから住所を自動取得して伝票テーブルに保存（請求書再発行対応）。

---

## DB スキーマ変更（scripts/）

| ファイル | 内容 |
|---|---|
| `alter_cost_price.sql` | 商品マスタ・明細テーブルに原価カラムを追加 |
| `alter_customers_address.sql` | 得意先マスタに郵便番号・住所1・住所2を追加 |
| `alter_invoice_headers_address.sql` | `invoice_headers` に得意先住所カラムを追加 |
| `alter_journal_address.sql` | 売上・入金ジャーナルに得意先住所カラムを追加 |

> **SP の適用が必要な変更**
> - `usp_suppliers_upsert` / `usp_suppliers_delete` を新規適用
> - 発注・仕入・支払の各 SP（`usp_purchase_orders_*` / `usp_purchases_*` / `usp_payments_*`）を新規適用
> - `usp_invoice_headers_select` から `@closing_day` パラメータを削除
> - `usp_company_info_upsert` に銀行口座 1〜3 カラムを追加
