# タスク管理

## 在庫管理実装ロードマップ

### 在庫数の考え方
```
現在庫 = 最新棚卸数量
        + SUM(purchase_lines.quantity  WHERE purchase_date > last_count_date AND is_deleted=0)
        - SUM(sale_lines.quantity      WHERE sale_date     > last_count_date AND is_deleted=0)
```
棚卸が一度もない商品は現在庫 = NULL（表示上は「未棚卸」）

---

## Phase 1: DB テーブル設計

### [x] inventory_counts（棚卸、1テーブル）
既存の sales / purchases / orders と同じフラット設計（ヘッダー情報を各行に持つ）。
棚卸は相手方がないため列がさらにシンプル。

```sql
CREATE TABLE inventory_counts (
    inventory_count_id  int            IDENTITY(1,1) PRIMARY KEY,
    count_date          date           NOT NULL,       -- 棚卸日（グルーピングキー）
    product_id          int            NOT NULL REFERENCES products(product_id),
    quantity            decimal(10,2)  NOT NULL,
    note                nvarchar(200)  NULL,
    created_at          datetime       NOT NULL DEFAULT GETDATE(),
    row_version         rowversion
);
-- 同一日×同一商品の重複を防ぐ
CREATE UNIQUE INDEX uix_inventory_counts_date_product
    ON inventory_counts (count_date, product_id);
```

---

## Phase 2: ストアドプロシージャ

### [x] usp_inventory_count_upsert
- 引数: `@count_date DATE`, `@lines NVARCHAR(MAX)`（JSON: `[{product_id, quantity, note}, ...]`）
- 対象日の全行を DELETE → JSON から INSERT（日単位の全置換）
- 1テーブルなのでヘッダー操作不要

### [x] usp_inventory_count_delete
- 引数: `@count_date DATE`（日単位削除）または `@inventory_count_id INT`（1行削除）

### [x] usp_inventory_current_get（現在庫照会）
- 引数: `@product_id INT NULL`（NULL = 全商品）
- 返却列: `product_id`, `product_code`, `product_name`,
  `last_count_date`, `last_count_qty`, `purchase_qty`, `sale_qty`, `current_stock`
- ロジック（JOINが1段、sales/purchases がフラットなのでシンプル）:
  1. 商品ごとの最新棚卸: `MAX(count_date)` per `product_id`
  2. その `count_date` より後の `purchases.quantity`（is_deleted=0）を集計
  3. その `count_date` より後の `sales.quantity`（is_deleted=0）を集計
  4. `current_stock = last_count_qty + purchase_qty - sale_qty`
  5. 棚卸なし商品: `last_count_date=NULL, current_stock=NULL`

---

## Phase 3: bmcs_app.Inventory プロジェクト

### [x] プロジェクト新規作成
- 種別: WinExe（既存プロジェクトと同パターン）
- 参照: bmcs_app.Core / bmcs_app.Infrastructure / bmcs_app.Shared
- 出力先: `bin/Debug/`（Directory.Build.props 継承）

### [x] 在庫照会画面（読み取り専用）
**ファイル:**
- `Views/InventoryInquiryView.xaml`
- `ViewModels/InventoryInquiryViewModel.cs`

### [x] 棚卸入力画面
**ファイル:**
- `Views/InventoryCountView.xaml`
- `ViewModels/InventoryCountViewModel.cs`
- `ViewModels/InventoryCountLineViewModel.cs`（明細行用）

**Infrastructure:**
- `IInventoryCountRepository` (Core/Interfaces)
- `InventoryCountRepository` (Infrastructure/Repositories)
- `IInventoryCurrentRepository` / `InventoryCurrentRepository` (照会専用)
- `Services/LookupService.cs`（商品マスタキャッシュ）

### [x] App.xaml.cs
- `InventoryCountRepository` / `InventoryCurrentRepository` をインスタンス化
- `LookupService.Initialize(products)` で商品マスタをキャッシュ
- 在庫照会画面起動、「棚卸入力」ボタンで棚卸入力ウィンドウをオンデマンド起動

---

## Phase 4: ランチャー統合

### [x] bmcs_app（ランチャー）にボタン追加
- 「在庫管理」ボタン → `bmcs_app.Inventory.exe` を `Process.Start` で起動

---

## 実装済みモジュール（参考）

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

---

## 残タスク（既存）

### [ ] Closing: 得意先指定機能
- 請求集計・売掛金集計で特定得意先のみを対象にする機能
- 現在は「全得意先」のみ動作し「指定」RadioButton は `IsEnabled=False`
- `usp_invoice_closing` / `usp_ar_closing` は `@customer_id` パラメータ対応済み（SP側は完成）
- **残作業**: View の RadioButton 有効化 + 得意先コード欄の入力 → `@customer_id` を ViewModel から SP に渡す
- **残作業**: 請求残高一覧表・売掛金残高一覧表（レポートおよびCSV出力）
