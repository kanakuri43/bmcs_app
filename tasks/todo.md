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

## インボイス制度対応 — 修正タスク（2026-04-21 調査）

### 調査根拠
国税庁「インボイスの記載事項」より：
**端数処理ルール: 「一のインボイスにつき、税率の異なるごとに1回の端数処理」**
→ 明細行ごとのFloorを合算することは明示的に**禁止**

**追加方針（2026-04-21）:**
- 内税（税込価格ベース）は廃止。外税（税抜価格ベース）のみサポート。
- 税計算単位「明細単位」は廃止。「伝票単位」または「請求単位」のみサポート。

---

### 🔴 HIGH（法的コンプライアンス直結）

#### [ ] #1 明細単位の端数処理が「インボイスにつき1回」ルール違反 + 内税廃止【最重要】

##### 端数処理修正
- **問題ファイル**: `src/bmcs_app.Sales/ViewModels/SalesMainViewModel.cs:803-804`
- **問題**: `_isLineTaxCalc=true`（明細単位）時に `g.Sum(l => l.LineTaxAmount)` = 明細行ごとのFloor合算
- **違反**: 「インボイスにつき税率ごとに1回の端数処理」= 行ごとFloor合算は禁止
- **具体例**: 105円×10%×2行 → 行ごとFloor=10+10=20円 / 正しい割戻し=Floor(210×0.1)=**21円**

##### TaxCalculator（`src/bmcs_app.Core/Services/TaxCalculator.cs`）
- `CalcInternalTaxTotal()` メソッド全体を**削除**
- `CalcLineTaxAmount()` 内の `taxTypeId == 2` 分岐（内税計算）を**削除**、外税計算のみ残す
- `CalcExternalTaxTotal()` の `_isLineTaxCalc` 分岐を削除し、常に割戻し計算に統一
  ```csharp
  // 修正後: isLineTaxCalc 分岐なし、外税割戻し計算のみ
  return lines.Where(l => l.AppliedTaxRate > 0)
      .GroupBy(l => l.AppliedTaxRate)
      .Sum(g => Math.Floor(g.Sum(l => l.LineAmount) * g.Key));
  ```
- `_isLineTaxCalc=true` は**明細行の参考表示**（`LineTaxAmount` 表示列）にのみ残す

##### ViewModel / View（Sales / Order / Purchase / PurchaseOrder 各プロジェクト）
- `InternalTaxTotal` プロパティを削除（4プロジェクト × 各 MainViewModel）
- 画面フッターの「(内税)[値]」表示欄を削除（各 MainView.xaml）
- 明細行 `TaxType` ComboBox を削除（税種別選択不要になるため）
  - `SaleLineViewModel.TaxType` / `OrderLineViewModel.TaxType` 等のプロパティ削除
  - 各 MainView.xaml の DataGrid「税種別」列を削除

#### [ ] #2 内税廃止 — ストアドプロシージャ修正

##### usp_invoice_closing.sql（`src/bmcs_app.Infrastructure/StoredProcedures/usp_invoice_closing.sql`）
- 内税分岐 `CASE WHEN tax_type_id = 2 THEN ... ELSE ... END` を外税計算に統一
  ```sql
  -- 修正前（内税/外税分岐）
  CASE WHEN tax_type_id = 2
      THEN FLOOR(group_base * applied_tax_rate / (1 + applied_tax_rate))
      ELSE FLOOR(group_base * applied_tax_rate)
  END

  -- 修正後（外税のみ）
  FLOOR(group_base * applied_tax_rate)
  ```
- 標準税率（tax_rate_type=1）・軽減税率（tax_rate_type=2）の両 CASE 文を修正

##### usp_ar_closing.sql（`src/bmcs_app.Infrastructure/StoredProcedures/usp_ar_closing.sql`）
- usp_invoice_closing と同じ内税分岐を削除・外税計算に統一

#### [ ] #3 内税廃止 — マスタ・DB スキーマ

##### 商品マスタ（products テーブル / ProductMaint）
- `products.tax_type_id` の用途変更: 外税(1)のみ許容。内税(2)は登録不可にする
  - `ProductMaintViewModel.TaxTypes` の ComboBox から内税選択肢を除外（または非表示）
  - `usp_products_upsert` に `tax_type_id = 2` を拒否するバリデーション追加（または削除して固定値1に変更）
- 既存データ: `UPDATE products SET tax_type_id = 1 WHERE tax_type_id = 2;`（マイグレーション）

##### TaxTypeRepository / App.xaml.cs
- `TaxTypeRepository` の `GetAllAsync()` から内税（tax_type_id=2）を除外するか、ComboBox 用途自体を廃止
  - 外税のみになれば TaxType 選択 UI が不要 → `TaxTypeRepository` ロードを各 App.xaml.cs から削除検討
- `tax_type_classifications` テーブルの `tax_type_id=2` レコードを `is_deleted=1` に更新（物理削除は FK 制約があるため論理削除）

#### [ ] #4 月次請求書の税額と個別納品書（適格請求書）の税額が不整合
- **問題ファイル**: `src/bmcs_app.Infrastructure/StoredProcedures/usp_invoice_closing.sql:92-123`
- **問題**: SPは常に「伝票単位」(per-slip FLOOR)で計算。明細単位顧客では納品書の税額と異なる
- **注**: #1修正（割戻し計算に統一）後は「各伝票がFloor計算→積上げ」で一致するか確認
- **修正**: #1修正後に手動検算で確認。不一致が残る場合は SP を顧客レベル集計に変更

#### [ ] #5 仕入先の登録番号（T番号）が管理されていない
- **問題ファイル**: `src/bmcs_app.Core/Models/Supplier.cs`（`invoice_no` フィールドなし）
- **法律要件**: 仕入税額控除には仕入先の適格請求書発行事業者登録番号の確認・保存が必要
- **リスク**: 登録番号のない仕入先からの仕入は消費税控除不可
- **修正**:
  - `ALTER TABLE suppliers ADD invoice_no nvarchar(20) NULL;`
  - `Supplier.cs` / `SupplierRepository` / `SupplierMaintView` に登録番号欄を追加
  - 仕入伝票印刷時に仕入先登録番号を表示

#### [x] #6 usp_invoice_closing が顧客住所を invoice_headers に保存していない【ライブDB修正済み】
- **状態**: ライブDB上の SP は `affected` CTE で `customers` を JOIN し住所3列を INSERT 済み（修正済み）
- **残作業**: ソースファイルがライブDBと乖離している → ファイルをライブDB定義に合わせて更新すること

#### [ ] #7 税計算単位「明細単位」廃止 — 伝票単位 / 請求単位 のみサポート

##### ライブDB現状（確認済み 2026-04-21）
```
tax_calc_unit_id=1  code=01  name=明細  ← 廃止対象（得意先2件が設定中）
tax_calc_unit_id=2  code=02  name=伝票  ← 存続
「請求単位」は未登録 → 新規追加が必要
```

##### DBマイグレーション
- `tax_calc_unit_classifications` に「請求単位」を追加
  ```sql
  INSERT INTO tax_calc_unit_classifications (tax_calc_unit_code, tax_calc_unit_name, is_deleted)
  VALUES ('03', '請求', 0);
  ```
- 明細単位の得意先2件を伝票単位に移行（または担当者が確認後に請求単位へ）
  ```sql
  UPDATE customers SET tax_calc_unit_id = 2 WHERE tax_calc_unit_id = 1;
  ```
- 明細単位レコードを論理削除
  ```sql
  UPDATE tax_calc_unit_classifications SET is_deleted = 1 WHERE tax_calc_unit_id = 1;
  ```

##### TaxCalculator（`src/bmcs_app.Core/Services/TaxCalculator.cs`）
- `IsLineTaxCalc()` メソッドを削除（明細単位なし → 呼び出し箇所すべて `false` 相当になる）
- `_isLineTaxCalc` フラグを使っているすべての箇所（#1 修正と連動）を除去

##### 請求単位の税計算方針（要確認・実装待ち）
- 「請求単位」= 月次請求書単位で税額を1回計算（`usp_invoice_closing` レベルで集計）
- 「伝票単位」= 伝票ごとに税額を計算し積み上げ（現行の伝票単位と同等）
- **実装方針は別途確認が必要**（`usp_invoice_closing` への影響あり）

##### CustomerMaint（`src/bmcs_app.Master/`）
- `CustomerMaintViewModel` の TaxCalcUnit ComboBox から「明細」選択肢を非表示
  - `TaxCalcUnitRepository.GetAllAsync()` の結果で `is_deleted=0` のみ表示すれば自動反映

---

### 🟡 MEDIUM（品質・一貫性）

#### [ ] #7 TaxFractionId（切捨/切上/四捨五入）が計算に反映されていない
- **問題ファイル**: `src/bmcs_app.Core/Services/TaxCalculator.cs`
- **問題**: `customers.tax_fraction_id` がDBに保存されているが計算は常に `Math.Floor`（切捨）
- **修正**: `TaxCalculator` に `taxFractionId` 引数追加・分岐実装。SP も同様
- **注**: 内税廃止により外税（`CalcExternalTaxTotal`）のみ対象

#### [ ] #8 納品書の軽減税率対象品目に ※ 識別マーク未対応
- **問題ファイル**: `src/bmcs_app.Sales/Services/SalesPrintHelper.cs:304-306`
- **法律要件**: 適格請求書記載事項③「取引内容（軽減税率の対象品目である旨）」
- **現状**: 税率列に "8%" を表示するが、NTA標準は品名に ※ マーク
- **修正**: `SalePrintLine` に `IsReducedRate` 追加。商品名に "※ " プレフィックス条件付与。フッター注記追加

---

### 🔵 LOW（整備）

#### [ ] #9 会社登録番号（invoice_no）のフォーマット検証なし
- `company_info.invoice_no` 入力時に `^T\d{13}$` 検証追加

#### [ ] #10 月次請求書フッターに適用税率の数値が明記されているか確認
- `※10%対象` / `※8%対象（軽減税率）` で税率は表示済みと思われるが要確認
- 適格請求書記載事項④「適用税率」の明示要件

---

### 修正優先順位
```
🔴 #1（TaxCalculator/ViewModel/View — 内税廃止 + 端数処理）
   #2（SP: usp_invoice_closing / usp_ar_closing）      ← #1 と並行可
   #7（税計算単位「明細」廃止 — DB/CustomerMaint）      ← #1 と並行可（IsLineTaxCalc 削除で連動）
   #3（商品マスタ / TaxTypeRepository / DBマイグレーション）
   #5（仕入先T番号）
   #4（#1修正後に手動検算で確認）
   #6 ライブDBソースファイル同期
🟡 #8 → #9
🔵 #10 → #11
```

---

## 残タスク（既存）

### [ ] Closing: 得意先指定機能
- 請求集計・売掛金集計で特定得意先のみを対象にする機能
- 現在は「全得意先」のみ動作し「指定」RadioButton は `IsEnabled=False`
- `usp_invoice_closing` / `usp_ar_closing` は `@customer_id` パラメータ対応済み（SP側は完成）
- **残作業**: View の RadioButton 有効化 + 得意先コード欄の入力 → `@customer_id` を ViewModel から SP に渡す
- **残作業**: 請求残高一覧表・売掛金残高一覧表（レポートおよびCSV出力）
