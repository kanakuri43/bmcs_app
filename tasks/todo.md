# タスク管理

## 実装済みモジュール

| モジュール | 状態 |
|---|---|
| bmcs_app（ランチャー） | 完了 |
| bmcs_app.Master（マスタ保守） | 完了（仕入先マスタ含む） |
| bmcs_app.Sales（売上登録） | 完了 |
| bmcs_app.Order（受注登録） | 完了 |
| bmcs_app.Receipt（入金登録） | 完了 |
| bmcs_app.Closing（請求集計・売掛金集計） | 完了 |
| bmcs_app.Search（伝票横断検索） | 完了 |
| bmcs_app.PurchaseOrder（発注登録） | 完了 |
| bmcs_app.Purchase（仕入登録） | 完了 |
| bmcs_app.Payment（支払登録） | 完了 |
| bmcs_app.Shared/Helpers/FocusHelper.cs | 完了（Sales/Order/Receipt/Purchase/PurchaseOrder/Payment 共用） |
| bmcs_app.Core/Services/TaxCalculator.cs | 完了（Sales/Order/Purchase/PurchaseOrder 共用） |

---

## 残タスク（既存）

### [ ] Closing: 得意先指定機能
- 請求集計・売掛金集計で特定得意先のみを対象にする機能
- 現在は「全得意先」のみ動作し「指定」RadioButton は `IsEnabled=False`
- `usp_invoice_closing` / `usp_ar_closing` は `@customer_id` パラメータ対応済み（SP側は完成）
- **残作業**: View の RadioButton 有効化 + 得意先コード欄の入力 → `@customer_id` を ViewModel から SP に渡す
- **残作業**: 請求残高一覧表・売掛金残高一覧表 （レポートおよびCSV出力）

### [ ] Search: 伝票検索
- **残作業**: 発注・仕入 伝票検索  発注・仕入伝票登録実装後に対応

---

## 新規機能: 発注・仕入・支払

受注→売上→入金 の対称処理として、発注→仕入→支払 を追加する。

### 対応関係

| 売上側 | 仕入側 | 備考 |
|---|---|---|
| 受注 (orders / bmcs_app.Order) | 発注 (purchase_orders / bmcs_app.PurchaseOrder) | |
| 売上 (sales / bmcs_app.Sales) | 仕入 (purchases / bmcs_app.Purchase) | |
| 入金 (receipts / bmcs_app.Receipt) | 支払 (payments / bmcs_app.Payment) | |
| 得意先 (customers) | 仕入先 (suppliers) ※新規マスタ | |

---

## STEP 1: テーブル設計 [x]

> **注意**: 既存の orders / sales / receipts はヘッダ・明細を**1テーブルにフラット格納**。
> 新規テーブルも同じ構造にする。

### 1-1. suppliers（仕入先マスタ）[x]
customers テーブルと対称。closing_day（締め日）・tax_fraction_id・tax_calc_unit_id も持つ（将来の買掛金締めで必要）。

```sql
CREATE TABLE dbo.suppliers (
    supplier_id      int           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    supplier_code    nvarchar(20)  NOT NULL,
    supplier_name    nvarchar(100) NOT NULL,
    closing_day      tinyint       NOT NULL DEFAULT 31,  -- 末締め=31
    tax_fraction_id  int           NOT NULL DEFAULT 1,
    tax_calc_unit_id int           NOT NULL DEFAULT 1,
    employee_id      int           NULL,                 -- 担当社員（任意）
    postal_code      nvarchar(8)   NULL,
    address1         nvarchar(100) NULL,
    address2         nvarchar(100) NULL,
    is_deleted       bit           NOT NULL DEFAULT 0,
    row_version      rowversion    NOT NULL,
    CONSTRAINT UQ_suppliers_code UNIQUE (supplier_code)
);
```

### 1-2. purchase_orders（発注）[x]
orders テーブルと対称。ヘッダ・明細フラット格納。
`has_purchases` フラグ（1件でも仕入起票済みなら削除不可）を全行に持つ。

```sql
CREATE TABLE dbo.purchase_orders (
    purchase_order_id    int           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    purchase_order_no    nvarchar(20)  NOT NULL,
    purchase_order_date  date          NOT NULL,
    supplier_id          int           NOT NULL,
    supplier_code        nvarchar(20)  NOT NULL,
    supplier_name        nvarchar(100) NOT NULL,
    employee_id          int           NULL,
    employee_code        nvarchar(20)  NOT NULL DEFAULT '',
    employee_name        nvarchar(100) NOT NULL DEFAULT '',
    line_no              int           NOT NULL,
    product_id           int           NOT NULL,
    product_code         nvarchar(20)  NOT NULL,
    product_name         nvarchar(100) NOT NULL,
    quantity             decimal(10,2) NOT NULL,
    unit_price           decimal(10,2) NOT NULL,
    cost_price           decimal(10,2) NOT NULL DEFAULT 0,
    tax_type_id          int           NOT NULL,
    tax_calc_unit_id     int           NOT NULL,
    tax_fraction_id      int           NOT NULL,
    tax_rate_type        tinyint       NOT NULL,
    applied_tax_rate     decimal(5,4)  NULL,
    tax_amount           decimal(10,0) NULL,
    slip_tax_amount      decimal(10,0) NULL,
    slip_remarks         nvarchar(200) NULL,
    line_remarks         nvarchar(200) NULL,
    has_purchases        bit           NOT NULL DEFAULT 0,
    is_deleted           bit           NOT NULL DEFAULT 0,
    row_version          rowversion    NOT NULL
);

CREATE UNIQUE NONCLUSTERED INDEX UQ_purchase_orders_line
    ON dbo.purchase_orders (purchase_order_no, line_no)
    WHERE (is_deleted = 0);
```

### 1-3. purchases（仕入）[x]
sales テーブルと対称。ヘッダ・明細フラット格納。
ロックカラム: `ap_closing_at`（買掛金締め日付）。sales の `invoiced_at` 相当。

```sql
CREATE TABLE dbo.purchases (
    purchase_id            int           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    purchase_no            nvarchar(20)  NOT NULL,
    purchase_date          date          NOT NULL,
    supplier_id            int           NOT NULL,
    supplier_code          nvarchar(20)  NOT NULL,
    supplier_name          nvarchar(100) NOT NULL,
    supplier_postal_code   nvarchar(8)   NULL,
    supplier_address1      nvarchar(100) NULL,
    supplier_address2      nvarchar(100) NULL,
    purchase_order_id      int           NULL,
    purchase_order_no      nvarchar(20)  NULL,
    employee_id            int           NOT NULL,
    employee_code          nvarchar(20)  NOT NULL,
    employee_name          nvarchar(50)  NOT NULL,
    line_no                int           NOT NULL,
    product_id             int           NOT NULL,
    product_code           nvarchar(20)  NOT NULL,
    product_name           nvarchar(100) NOT NULL,
    quantity               decimal(10,2) NOT NULL,
    unit_price             decimal(10,2) NOT NULL,
    cost_price             decimal(10,2) NOT NULL DEFAULT 0,
    tax_type_id            int           NOT NULL,
    tax_calc_unit_id       int           NOT NULL,
    tax_fraction_id        int           NOT NULL,
    tax_rate_type          tinyint       NOT NULL,
    applied_tax_rate       decimal(5,4)  NULL,
    line_tax_amount        decimal(10,0) NULL,
    slip_tax_amount        decimal(10,0) NULL,
    slip_remarks           nvarchar(200) NULL,
    line_remarks           nvarchar(200) NULL,
    ap_closing_at          date          NULL,   -- 買掛金集計ロック（締め日付）
    is_deleted             bit           NOT NULL DEFAULT 0,
    row_version            rowversion    NOT NULL
);

CREATE UNIQUE NONCLUSTERED INDEX UQ_purchases_line
    ON dbo.purchases (purchase_no, line_no)
    WHERE (is_deleted = 0);
```

### 1-4. payments（支払）[x]
receipts テーブルと対称。ヘッダ・明細フラット格納。
支払区分は既存の `payment_method_classifications` テーブル流用。

```sql
CREATE TABLE dbo.payments (
    payment_id             int           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    payment_no             nvarchar(20)  NOT NULL,
    payment_date           date          NOT NULL,
    supplier_id            int           NOT NULL,
    supplier_code          nvarchar(20)  NOT NULL,
    supplier_name          nvarchar(100) NOT NULL,
    supplier_postal_code   nvarchar(8)   NULL,
    supplier_address1      nvarchar(100) NULL,
    supplier_address2      nvarchar(100) NULL,
    line_no                int           NOT NULL,
    payment_method_id      int           NOT NULL,
    amount                 decimal(10,0) NOT NULL,
    bill_due_date          date          NULL,
    slip_remarks           nvarchar(200) NULL,
    line_remarks           nvarchar(200) NULL,
    ap_closing_at          date          NULL,   -- 買掛金集計ロック（締め日付）
    is_deleted             bit           NOT NULL DEFAULT 0,
    row_version            rowversion    NOT NULL
);

CREATE UNIQUE NONCLUSTERED INDEX UQ_payments_line
    ON dbo.payments (payment_no, line_no)
    WHERE (is_deleted = 0);
```

---

## STEP 2: 更新用 SP 作成 [x]

### 2-1. 仕入先マスタ SP [x]
- `usp_suppliers_upsert`
- `usp_suppliers_delete`

### 2-2. 発注 SP [x]
- `usp_purchase_orders_summaries_select`
- `usp_purchase_orders_select`（has_purchases を動的計算）
- `usp_purchase_orders_upsert`（論理削除+再INSERT。仕入済みなら変更不可）
- `usp_purchase_orders_delete`（仕入済みなら削除不可）

### 2-3. 仕入 SP [x]
- `usp_purchases_summaries_select`
- `usp_purchases_select`
- `usp_purchases_upsert`（仕入先住所は suppliers から自動取得。ap_closing_at IS NOT NULL で変更不可）
- `usp_purchases_delete`（ap_closing_at IS NOT NULL で削除不可）

### 2-4. 支払 SP [x]
- `usp_payments_summaries_select`
- `usp_payments_select`
- `usp_payments_upsert`（仕入先住所は suppliers から自動取得。ap_closing_at IS NOT NULL で変更不可）
- `usp_payments_delete`（ap_closing_at IS NOT NULL で削除不可）

---

## STEP 3: プロジェクトに機能追加 [x]

### 3-0. 共通（Core / Infrastructure / Shared） [x]

**bmcs_app.Core:**
- [x] `Models/Supplier.cs`
- [x] `Models/PurchaseOrderSlip.cs` / `PurchaseOrderLine.cs` / `PurchaseOrderLineInput.cs`
- [x] `Models/PurchaseSlip.cs` / `PurchaseLine.cs` / `PurchaseLineInput.cs`
- [x] `Models/PaymentSlip.cs` / `PaymentLine.cs` / `PaymentLineInput.cs`
- [x] `Interfaces/ISupplierRepository.cs`
- [x] `Interfaces/IPurchaseOrderRepository.cs`
- [x] `Interfaces/IPurchaseRepository.cs`
- [x] `Interfaces/IPaymentRepository.cs`

**bmcs_app.Infrastructure:**
- [x] `Repositories/SupplierRepository.cs`
- [x] `Repositories/PurchaseOrderRepository.cs`
- [x] `Repositories/PurchaseRepository.cs`
- [x] `Repositories/PaymentRepository.cs`

### 3-1. bmcs_app.Master: 仕入先マスタ追加 [x]
既存の CustomerMaint パターンに準拠。

- [x] `ViewModels/SupplierMaintViewModel.cs`
- [x] `Views/SupplierMaintView.xaml`
- [x] ランチャーメニューに「仕入先マスタ」追加

### 3-2. bmcs_app.PurchaseOrder（発注登録）新規プロジェクト [x]
bmcs_app.Order をほぼそのまま流用。supplier_id に差し替え。

- [x] プロジェクト新規作成・sln 登録
- [x] `App.xaml.cs`
- [x] `Services/LookupService.cs`（仕入先 / 社員 / 商品 検索）
- [x] `ViewModels/PurchaseOrderMainViewModel.cs`
- [x] `ViewModels/PurchaseOrderLineViewModel.cs`
- [x] `Views/PurchaseOrderMainView.xaml` / `PurchaseOrderMainView.xaml.cs`
- [x] `Views/PurchaseOrderLineControl.xaml` / `PurchaseOrderLineControl.xaml.cs`
- [x] ランチャーメニューに「発注登録」追加

### 3-3. bmcs_app.Purchase（仕入登録）新規プロジェクト [x]
bmcs_app.Sales をほぼそのまま流用。supplier_id に差し替え、発注Noフィールド追加。

- [x] プロジェクト新規作成・sln 登録
- [x] `App.xaml.cs`
- [x] `Services/LookupService.cs`（仕入先 / 社員 / 商品 / 発注伝票 検索）
- [x] `ViewModels/PurchaseMainViewModel.cs`
- [x] `ViewModels/PurchaseLineViewModel.cs`
- [x] `Views/PurchaseMainView.xaml` / `PurchaseMainView.xaml.cs`
- [x] `Views/PurchaseLineControl.xaml` / `PurchaseLineControl.xaml.cs`
- [x] ランチャーメニューに「仕入登録」追加

### 3-4. bmcs_app.Payment（支払登録）新規プロジェクト [x]
bmcs_app.Receipt をほぼそのまま流用。supplier_id に差し替え。

- [x] プロジェクト新規作成・sln 登録
- [x] `App.xaml.cs`
- [x] `Services/LookupService.cs`（仕入先 / 支払区分 検索）
- [x] `ViewModels/PaymentMainViewModel.cs`
- [x] `ViewModels/PaymentLineViewModel.cs`
- [x] `Views/PaymentMainView.xaml` / `PaymentMainView.xaml.cs`
- [x] `Views/PaymentLineControl.xaml` / `PaymentLineControl.xaml.cs`
- [x] ランチャーメニューに「支払登録」追加

---

## 設計上の注意点

### ロックカラム
- `purchases.ap_closing_at` / `payments.ap_closing_at`: 買掛金締め処理でセット
- `sales` の `invoiced_at` / `ar_aggregated_at` に相当するが、仕入側は請求締めがないため1カラムで十分
- 将来の買掛金集計（Closing 拡張）のために確保しておく

### 発注の has_purchases フラグ
- 売上側の `orders.HasSales` に相当
- 1件でも仕入が起票された発注は `has_purchases=true` → 発注削除不可

### 採番ルール
- 既存と統一: `yyyyMMddnnn` 形式（例: 2026041300１）

### 税計算
- 仕入・発注は既存の `TaxCalculator` をそのまま流用可

### payment_method_classifications の流用
- 支払区分（現金・手形等）は入金側の `payment_method_classifications` テーブルをそのまま共用
- 別テーブル化は不要
