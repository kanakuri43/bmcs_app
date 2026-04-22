# タスク管理


## インボイス制度対応 — 修正タスク（2026-04-21 調査）

### 調査根拠
国税庁「インボイスの記載事項」より：
**端数処理ルール: 「一のインボイスにつき、税率の異なるごとに1回の端数処理」**
→ 明細行ごとのFloorを合算することは明示的に**禁止**

**追加方針（2026-04-21）:**
- 内税（税込価格ベース）は廃止。外税（税抜価格ベース）のみサポート。
- 税計算単位「明細単位」は廃止。「伝票単位」または「請求単位」をサポート。
  - 伝票単位 (id=2): 伝票ごと・税率ごとに FLOOR → 各伝票の税額を月次請求書に積上げ
  - 請求単位 (id=3): 請求期間内の全売上を顧客×税率でまとめて FLOOR（月次請求書単位で1回計算）

---

### 🔴 HIGH（法的コンプライアンス直結）

#### [x] #1 明細単位の端数処理が「インボイスにつき1回」ルール違反 + 内税廃止【完了】
- `TaxCalculator`: `CalcInternalTaxTotal` / `CalcLineTaxAmount` 内税分岐 / `IsLineTaxCalc` 削除
- `TaxLineInput` を `(AppliedTaxRate, LineAmount)` のみに簡素化
- `CalcExternalTaxTotal` の `isLineTaxCalc` 分岐削除 → 常に割戻し計算に統一
- Sales / Order / Purchase / PurchaseOrder 各 MainViewModel: `_isLineTaxCalc` / `TaxTypes` / `InternalTaxTotal` / `PropagateLineTaxCalcToLines` 削除
- 各 MainView.xaml フッター「(内税)」行削除
- 各 LineControl.xaml「税種別」列・「税額」列削除
- 各 App.xaml.cs: `InitTaxTypesAsync` 削除
- `SalePrintData.SalePrintLine.TaxTypeName` 削除 / `SalesPrintHelper` の税種列削除
- `BuildTaxBreakdowns()` 検証済み（内税・`_isLineTaxCalc` 参照なし）

#### [x] #2 内税廃止 — ストアドプロシージャ修正【完了】
- `usp_invoice_closing`: `slip_groups` から `tax_type_id` 除去、`sales_agg` を `FLOOR(group_base * applied_tax_rate)` に統一
- `usp_ar_closing`: 同上

#### [x] #3 内税廃止 — マスタ・DB スキーマ【完了】
- `products` の内税商品1件（筆文字サインペン）を `tax_type_id=1`（外税）へ移行済み
- `tax_type_classifications` の内税（id=2）を `is_deleted=1` で論理削除済み
- `TaxTypeRepository.GetAllAsync()` が `WHERE is_deleted=0` フィルタ済みのため、ProductMaint ComboBox に内税が表示されなくなった（コード変更不要）
- 残存: 外税（id=1）/ 非課税（id=3） のみ有効

#### [x] #7 税計算単位「明細単位」廃止 / 「請求単位」追加【完了】
- 得意先2件（渋谷文具・盛文堂）を `tax_calc_unit_id=2`（伝票単位）に移行済み
- `tax_calc_unit_classifications`: 明細（id=1）論理削除 / 請求（id=3）追加
- `usp_invoice_closing` / `usp_ar_closing`: `tax_calc_unit_id=2`（伝票単位）は伝票ごとFLOOR、`id=3`（請求単位）は全伝票合算してFLOOR
- CustomerMaint ComboBox は `is_deleted=0` フィルタ済みのため自動的に「伝票」「請求」のみ表示

#### [x] #4 月次請求書の税額と個別納品書（適格請求書）の税額が不整合【確認済・問題なし】
- 全3顧客・全請求期間で `invoice_headers.tax_amount` と伝票単位FLOOR積上げ計算が完全一致（diff=0）
- 納品書: `FLOOR(Σline_amounts × rate)` per slip per rate ← BuildTaxBreakdowns()
- 月次請求書SP: `FLOOR(group_base × rate)` per slip per rate を顧客単位に積上げ
- NTA「一のインボイスにつき税率ごとに1回端数処理」ルールに準拠

#### [x] #5 仕入先の登録番号（T番号）が管理されていない【完了（仕入伝票印刷は未着手）】
- `suppliers` テーブルに `invoice_no nvarchar(20) NULL` 追加済み
- `usp_suppliers_upsert` に `@invoice_no` パラメータ追加済み
- `Supplier.InvoiceNo` / `ISupplierRepository.UpsertAsync` / `SupplierRepository` / `SupplierMaintViewModel` / `SupplierMaintView` に登録番号欄追加済み
- **残作業**: 仕入伝票（bmcs_app.Purchase）の印刷に仕入先登録番号を表示

#### [x] #6 usp_invoice_closing が顧客住所を invoice_headers に保存していない【ライブDB修正済み】

---

### 🟡 MEDIUM（品質・一貫性）

#### [x] #8 TaxFractionId（切捨/切上/四捨五入）が計算に反映されていない【完了】
- `TaxCalculator.CalcExternalTaxTotal` に `taxFractionId` 引数追加（デフォルト1=切捨）
- `ApplyRounding` ヘルパー追加（1=Floor / 2=Ceiling / 3=四捨五入）
- Sales / Order / Purchase / PurchaseOrder 各 MainViewModel: `_taxFractionId` フィールド追加、得意先/仕入先選択時・伝票ロード時にセット
- `BuildTaxBreakdowns()` も `TaxCalculator.CalcExternalTaxTotal` に統一（一貫性確保）
- `usp_invoice_closing` / `usp_ar_closing`: `FLOOR` を `CASE c.tax_fraction_id WHEN 2 THEN CEILING WHEN 3 THEN ROUND ELSE FLOOR` に変更、GROUP BY に `c.tax_fraction_id` 追加

#### [x] #9 納品書の軽減税率対象品目に ※ 識別マーク未対応【完了】
- `SalePrintLine.IsReducedRate` 追加（`TaxRateType == 2` で判定）
- `SalesMainViewModel.CreatePrintData()`: `IsReducedRate = l.TaxRateType == 2` をセット
- `SalesPrintHelper.BuildLinesTable()`: 商品名列に `"※ " + ProductName` 条件付与
- `SalesPrintHelper.BuildFooter()`: 軽減税率行がある場合のみ `"※ は軽減税率（8%）対象商品です"` 注記を表示

---

### 🔵 LOW（整備）

#### [x] #10 会社登録番号（invoice_no）のフォーマット検証なし【完了】
- `CompanyInfoSettingsViewModel.OnSaveAsync()`: `^T\d{13}$` でバリデーション追加（空欄は許可）
- `SupplierMaintViewModel.OnSaveAsync()`: 同様のバリデーション追加（仕入先登録番号）

#### [x] #11 月次請求書フッターに適用税率の数値が明記されているか確認【確認済・問題なし】
- `InvoiceClosingViewModel.BuildPrintData()` でラベル生成: `"10%対象"` / `"8%対象（軽減税率）"` 
- `InvoicePrintHelper.BuildFooter()` で `"※ {bd.Label}"` として表示
- NTA「税率ごとの税抜金額・消費税額」要件に準拠済み。コード変更不要

---

### 修正優先順位
```
🔴 #5（仕入先T番号）← 次タスク
   #4（#1/#2修正後に手動検算で確認）
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
