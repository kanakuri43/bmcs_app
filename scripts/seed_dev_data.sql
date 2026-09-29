-- =============================================================================
-- seed_dev_data.sql
-- 開発用テストデータの投入スクリプト。
--
-- 【重要】これは開発用データであり、本番環境には絶対に流さない。
--
-- 対象: bmcs_db（172.16.3.171）
--
-- scripts/ 配下の連番ファイル（DDL）とは異なり、このファイルは何度でも
-- 再実行してよい。冒頭で本スクリプトが作成したテストデータのみを
-- FKの逆順で削除してから、再投入する（他の開発者が別途入れたデータは触らない）。
--
-- 網羅する内容:
--   - 得意先の税単位3種（tax_unit=1/2/3）と締め区分（締め・都度）の対応
--   - 商品の税種別区分3種
--   - docs/product-spec.md「伝票の状態遷移」の全18状態
--   - sales/receipts/detail_receiptsの消込・充当キャッシュ列は、SettlementService
--     （TODO.md 7-1）の再計算ルールで再現可能な値になっている（2026-09-14修正）
--
-- 010_unify_tax_unit_tables.sql での統合に追従し、sales / receipts / billings
-- （旧・税単位別8テーブル）は tax_unit 列を持つ単一テーブルへの投入に変更した。
--
-- 適用: sqlcmd -S 172.16.3.171 -U sa -d bmcs_db -C -I -i scripts\seed_dev_data.sql
-- =============================================================================

USE bmcs_db;
GO

-- scripts/013_add_billing_confirmed_unique_index.sql が billings にフィルタ付き一意索引を
-- 追加したため、sqlcmd既定の QUOTED_IDENTIFIER OFF のままだと本スクリプトの billings への
-- DML自体が失敗する。フィルタ付き索引を持つテーブルへのDMLは QUOTED_IDENTIFIER ON が必須
-- （sqlcmdは既定でOFF。呼び出し側は -I オプションでも ON にできるが、本スクリプト単体で
-- 再実行できるようここでも明示する。TODO.md 7-1の実装検証で発見）。
SET QUOTED_IDENTIFIER ON;
GO

-- -----------------------------------------------------------------------------
-- 1. 既存テストデータの削除（FKの逆順）
-- -----------------------------------------------------------------------------
DELETE FROM dbo.detail_invoice_sales_lines WHERE detail_invoice_number IN (N'DIV001', N'DIV002');
DELETE FROM dbo.detail_receipts WHERE detail_receipt_number IN (N'DRC001', N'DRC002');
DELETE FROM dbo.detail_invoices WHERE detail_invoice_number IN (N'DIV001', N'DIV002');
DELETE FROM dbo.receipt_allocations WHERE receipt_slip_number IN (N'RCP_INV001', N'RCP_INV002', N'RCP_SLP001');
DELETE FROM dbo.receipts WHERE receipt_slip_number IN (N'RCP_INV001', N'RCP_INV002', N'RCP_SLP001');
DELETE FROM dbo.sales WHERE sales_slip_number IN (N'SALINV001', N'SALINV002', N'SALSLP001', N'SALSLP002',
                                                   N'SALLIN001', N'SALLIN002', N'SALLIN003', N'SALLIN004');
DELETE FROM dbo.billings WHERE billing_number IN (N'BIL_INV001', N'BIL_INV002', N'BIL_SLP001');
DELETE FROM dbo.orders WHERE order_slip_number IN (N'ORD001', N'ORD002', N'ORD003', N'ORD004');
DELETE FROM dbo.monthly_closings WHERE closing_date IN ('2026-01-31', '2026-02-28');
DELETE FROM dbo.customers WHERE customer_code IN (N'CUS001', N'CUS002', N'CUS003', N'CUS004');
DELETE FROM dbo.products WHERE product_code IN (N'PRD001', N'PRD002', N'PRD003');
DELETE FROM dbo.bank_accounts WHERE bank_account_code IN (N'BNK001');
DELETE FROM dbo.company_infos WHERE company_info_id = 1;
DELETE FROM dbo.employees WHERE employee_code IN (N'EMP001', N'EMP002');
GO

-- -----------------------------------------------------------------------------
-- 2. マスタ
-- -----------------------------------------------------------------------------

-- 社員（一般／管理者）
INSERT INTO dbo.employees (employee_code, employee_name, employee_name_kana, permission_level, created_by, created_at, updated_by, updated_at)
VALUES
    (N'EMP001', N'営業一郎', N'ｴｲｷﾞｮｳｲﾁﾛｳ', 1, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'EMP002', N'管理者太郎', N'ｶﾝﾘｼｬﾀﾛｳ', 9, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- 得意先: 税単位3種（Invoice/Slip/Line）を1つずつ網羅
-- CK_customers_tax_unit_closing_day の相互制約（tax_unit=3 ⇔ closing_day=0）を満たす。
-- CUS004 は親子請求（請求集約）の確認用サンプル。CUS001 を請求集約先（billing_customer_code）
-- に指定し、closing_day/tax_unit/rounding_type を CUS001 と一致させている
-- （docs/database-schema.md 1-1節・scripts/020_add_billing_customer_code.sql）。
INSERT INTO dbo.customers
    (customer_code, customer_name, customer_name_kana, postal_code, address1, contact_person_name,
     sales_employee_code, closing_day, tax_unit, rounding_type, print_representative_flag,
     billing_customer_code,
     created_by, created_at, updated_by, updated_at)
VALUES
    (N'CUS001', N'株式会社山田商事', N'ﾔﾏﾀﾞｼｮｳｼﾞ', N'100-0001', N'東京都千代田区1-1-1', N'山田太郎',
     N'EMP001', 20, 1, 1, 0,
     N'CUS001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'CUS002', N'鈴木工業株式会社', N'ｽｽﾞｷｺｳｷﾞｮｳ', N'150-0001', N'東京都渋谷区2-2-2', N'鈴木花子',
     N'EMP001', 99, 2, 2, 0,
     N'CUS002',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'CUS003', N'石山市立石山小学校', N'ｲｼﾔﾏｼﾘﾂｲｼﾔﾏｼｮｳｶﾞｯｺｳ', N'400-0001', N'山梨県甲府市3-3-3', N'佐藤先生',
     N'EMP002', 0, 3, 3, 1,
     N'CUS003',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'CUS004', N'株式会社山田商事　大阪支店', N'ﾔﾏﾀﾞｼｮｳｼﾞ ｵｵｻｶｼﾃﾝ', N'530-0001', N'大阪府大阪市北区4-4-4', N'山田次郎',
     N'EMP001', 20, 1, 1, 0,
     N'CUS001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- 商品: 税種別区分3種を1つずつ網羅
INSERT INTO dbo.products
    (product_code, product_name, product_name_kana, specification, unit_name,
     standard_unit_price_excl_tax, standard_unit_price_incl_tax, standard_cost_price, tax_category,
     created_by, created_at, updated_by, updated_at)
VALUES
    (N'PRD001', N'事務用品セット', N'ｼﾞﾑﾖｳﾋﾝｾｯﾄ', N'A4', N'セット', 1000.0000, 1100.0000, 700.0000, 1,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'PRD002', N'給食用食材', N'ｷｭｳｼｮｸﾖｳｼｮｸｻﾞｲ', N'1kg', N'袋', 500.0000, 540.0000, 350.0000, 2,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'PRD003', N'商品券', N'ｼｮｳﾋﾝｹﾝ', NULL, N'枚', 1000.0000, 1000.0000, 1000.0000, 3,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- 銀行口座
INSERT INTO dbo.bank_accounts
    (bank_account_code, bank_name, branch_name, account_type, account_number, account_holder_name,
     display_order, created_by, created_at, updated_by, updated_at)
VALUES
    (N'BNK001', N'石山銀行', N'本店', 1, N'1234567', N'ｲｼﾔﾏｼｮｳﾃﾝ', 1,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- 自社情報（1レコード運用）
INSERT INTO dbo.company_infos
    (company_info_id, company_name, invoice_registration_number, postal_code, address1,
     phone_number, representative_name, created_by, created_at, updated_by, updated_at)
VALUES
    (1, N'株式会社石山商店', N'T1234567890123', N'400-0000', N'山梨県甲府市本町1-1',
     N'055-000-0000', N'石山一郎', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 3. 受注: order_status の4状態を1つずつ網羅
-- -----------------------------------------------------------------------------
INSERT INTO dbo.orders
    (order_slip_number, line_number, order_date, customer_code, customer_name,
     product_code, product_name, order_quantity, unit_price, amount, cost_price,
     tax_category, tax_rate, allocated_quantity, order_status, sales_confirmed_quantity,
     created_by, created_at, updated_by, updated_at)
VALUES
    -- 未売上
    (N'ORD001', 1, '2026-08-01', N'CUS001', N'株式会社山田商事',
     N'PRD001', N'事務用品セット', 10.000, 1000.0000, 10000.00, 700.0000,
     1, 10.00, 0.000, 1, 0.000,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- 一部売上
    (N'ORD002', 1, '2026-08-02', N'CUS001', N'株式会社山田商事',
     N'PRD001', N'事務用品セット', 10.000, 1000.0000, 10000.00, 700.0000,
     1, 10.00, 0.000, 2, 5.000,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- 売上完了
    (N'ORD003', 1, '2026-08-03', N'CUS001', N'株式会社山田商事',
     N'PRD001', N'事務用品セット', 10.000, 1000.0000, 10000.00, 700.0000,
     1, 10.00, 0.000, 3, 10.000,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- 中止
    (N'ORD004', 1, '2026-08-04', N'CUS001', N'株式会社山田商事',
     N'PRD001', N'事務用品セット', 10.000, 1000.0000, 10000.00, 700.0000,
     1, 10.00, 0.000, 4, 0.000,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 4. 請求データ（統合版。先に作る。売上・入金から参照されるため）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.billings
    (billing_number, customer_code, tax_unit, customer_name, billing_date, closing_year_month,
     previous_balance, receipt_amount, sales_amount, tax_amount, current_billing_amount,
     standard_rate_taxable_amount, standard_rate_tax_amount, reduced_rate_taxable_amount, reduced_rate_tax_amount, tax_exempt_amount,
     billing_status, confirmed_at, confirmed_by,
     created_by, created_at, updated_by, updated_at)
VALUES
    -- 確定（CUS001・請求単位）
    (N'BIL_INV001', N'CUS001', 1, N'株式会社山田商事', '2026-07-20', N'202607',
     0.00, 0.00, 10000.00, 1000.00, 11000.00,
     10000.00, 1000.00, 0.00, 0.00, 0.00,
     1, '2026-07-20T10:00:00', N'EMP001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- 解除済（CUS001・請求単位）
    (N'BIL_INV002', N'CUS001', 1, N'株式会社山田商事', '2026-06-20', N'202606',
     0.00, 0.00, 5000.00, 500.00, 5500.00,
     5000.00, 500.00, 0.00, 0.00, 0.00,
     2, '2026-06-20T10:00:00', N'EMP001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- 確定（CUS002・伝票単位）
    (N'BIL_SLP001', N'CUS002', 2, N'鈴木工業株式会社', '2026-07-31', N'202607',
     0.00, 0.00, 8000.00, 800.00, 8800.00,
     8000.00, 800.00, 0.00, 0.00, 0.00,
     1, '2026-07-31T10:00:00', N'EMP001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

UPDATE dbo.billings
    SET released_at = '2026-06-25T09:00:00', released_by = N'EMP002'
    WHERE billing_number = N'BIL_INV002';
GO

-- -----------------------------------------------------------------------------
-- 5. 売上（統合版。税単位3種 × 売上の3軸9状態＋返品を網羅）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.sales
    (sales_slip_number, line_number, slip_date, customer_code, tax_unit, customer_name, slip_type,
     product_code, product_name, quantity, unit_price, amount, cost_price,
     tax_category, tax_rate, slip_tax_amount, tax_amount, delivery_note_issued_at, delivery_note_issue_count,
     billing_status, settlement_status, settled_amount, order_slip_number, order_line_number, billing_number,
     created_by, created_at, updated_by, updated_at)
VALUES
    -- CUS001・請求単位: 未発行・未請求・未消込
    (N'SALINV001', 1, '2026-08-10', N'CUS001', 1, N'株式会社山田商事', 1,
     N'PRD001', N'事務用品セット', 5.000, 1000.0000, 5000.00, 700.0000,
     1, 10.00, NULL, NULL, NULL, 0,
     1, 1, 0.00, N'ORD002', 1, NULL,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS001・請求単位: 発行済・請求済・一部消込
    (N'SALINV002', 1, '2026-07-15', N'CUS001', 1, N'株式会社山田商事', 1,
     N'PRD001', N'事務用品セット', 10.000, 1000.0000, 10000.00, 700.0000,
     1, 10.00, NULL, NULL, '2026-07-15T14:00:00', 1,
     2, 2, 4000.00, N'ORD003', 1, N'BIL_INV001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS002・伝票単位: 未発行・未請求・未消込
    (N'SALSLP001', 1, '2026-08-11', N'CUS002', 2, N'鈴木工業株式会社', 1,
     N'PRD001', N'事務用品セット', 3.000, 1000.0000, 3000.00, 700.0000,
     1, 10.00, 300.00, NULL, NULL, 0,
     1, 1, 0.00, NULL, NULL, NULL,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS002・伝票単位: 発行済・請求済・消込完了
    -- settled_amount は8000.00（2026-09-14修正: TODO.md 7-1決定1により消込の対象額は
    -- amount（税抜）そのもの。消費税分（800円）は明細行レベルの消込に載せない。
    -- 旧値8800.00（税込）は再計算方式の消込ルールと不整合だった）。
    (N'SALSLP002', 1, '2026-07-10', N'CUS002', 2, N'鈴木工業株式会社', 1,
     N'PRD001', N'事務用品セット', 8.000, 1000.0000, 8000.00, 700.0000,
     1, 10.00, 800.00, NULL, '2026-07-10T11:00:00', 1,
     2, 3, 8000.00, NULL, NULL, N'BIL_SLP001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS003・内税明細単位（都度得意先）: 未請求（明細請求の対象候補）
    -- unit_price は内税単価（550）を転記する。amount=11000 は 20×550 で既に整合している
    -- （2026-09-08 修正: 旧値500は外税単価の取り違えで、TODO.md 4-2 の検証に不整合だった）。
    (N'SALLIN001', 1, '2026-08-12', N'CUS003', 3, N'石山市立石山小学校', 1,
     N'PRD002', N'給食用食材', 20.000, 550.0000, 11000.00, 350.0000,
     2, 8.00, NULL, 815.00, '2026-08-12T09:00:00', 1,
     1, 1, 0.00, NULL, NULL, NULL,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS003・内税明細単位: 請求済（DIV001 と連携）・消込完了
    -- tax_amount は612.00（2026-09-14修正: CUS003のrounding_type=3=切上。
    -- 8250×8÷108=611.111…を切上すると612.00になる。旧値611.00はSALLIN004と同種の
    -- 見落としだった。TODO.md 7-1の実装検証で発見）。
    (N'SALLIN002', 1, '2026-07-20', N'CUS003', 3, N'石山市立石山小学校', 1,
     N'PRD002', N'給食用食材', 15.000, 550.0000, 8250.00, 350.0000,
     2, 8.00, NULL, 612.00, '2026-07-20T09:00:00', 1,
     2, 3, 8250.00, NULL, NULL, NULL,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS003・内税明細単位【境界値】: 未請求だが消込完了 → 明細請求の対象外になるべき行
    (N'SALLIN003', 1, '2026-07-25', N'CUS003', 3, N'石山市立石山小学校', 1,
     N'PRD002', N'給食用食材', 5.000, 550.0000, 2750.00, 350.0000,
     2, 8.00, NULL, 204.00, '2026-07-25T09:00:00', 1,
     1, 3, 2750.00, NULL, NULL, NULL,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS003・内税明細単位: 返品（マイナス数量。M-9暫定）
    -- tax_amount は -82.00（2026-09-14修正: CUS003のrounding_type=3=切上。符号対称な切上のため
    -- -1100×8÷108=-81.4815…はMath.Floor(-81.4815)=-82.00になる。旧値-81.00はTaxRoundingの
    -- 符号対称規則を反映していない不整合値だった。TODO.md 6-3の実装検証で発見）。
    (N'SALLIN004', 1, '2026-08-13', N'CUS003', 3, N'石山市立石山小学校', 2,
     N'PRD002', N'給食用食材', -2.000, 550.0000, -1100.00, 350.0000,
     2, 8.00, NULL, -82.00, NULL, 0,
     1, 1, 0.00, NULL, NULL, NULL,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 6. 締め入金（明細行＝支払手段の内訳。充当は receipt_allocation が別に持つ。
--    docs/design_document.md 17章、2026-09-15改訂。充当状態3種を網羅）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.receipts
    (receipt_slip_number, line_number, receipt_date, customer_code, tax_unit, customer_name, deposit_method_code,
     bank_account_code, bill_due_date, amount, allocation_status, created_by, created_at, updated_by, updated_at)
VALUES
    -- CUS001・請求単位: 充当完了（振込1行）
    -- amountは4000.00（2026-09-14修正: TODO.md 7-1決定3によりbillingへの充当額はその請求に
    -- 紐づくsales行へ配分される。全額11000.00を充当するとSALINV002（amount=10000）が消込
    -- 完了になり、既存のSALINV002の消込状態（一部消込・settled_amount=4000.00）という境界値が
    -- 再現できなくなる。一部入金に変更し、SALINV002の既存値と整合させた）。
    (N'RCP_INV001', 1, '2026-07-25', N'CUS001', 1, N'株式会社山田商事', N'TRANSFER',
     N'BNK001', NULL, 4000.00,
     3, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS001・請求単位: 未充当（前受・過入金。充当先が無いため receipt_allocation の行を作らない）
    (N'RCP_INV002', 1, '2026-08-05', N'CUS001', 1, N'株式会社山田商事', N'CASH',
     NULL, NULL, 3000.00,
     1, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS002・伝票単位: 一部充当（振込8,000＋現金2,000の2行で合計10,000。複数の支払手段が
    -- 混在するケースを網羅する。2026-09-15改訂）
    (N'RCP_SLP001', 1, '2026-08-01', N'CUS002', 2, N'鈴木工業株式会社', N'TRANSFER',
     N'BNK001', NULL, 8000.00,
     2, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'RCP_SLP001', 2, '2026-08-01', N'CUS002', 2, N'鈴木工業株式会社', N'CASH',
     NULL, NULL, 2000.00,
     2, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

INSERT INTO dbo.receipt_allocations
    (receipt_slip_number, line_number, customer_code, tax_unit, billing_number, allocated_amount,
     fee_adjustment_amount, created_by, created_at, updated_by, updated_at)
VALUES
    (N'RCP_INV001', 1, N'CUS001', 1, N'BIL_INV001', 4000.00, 0.00,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS002・伝票単位: 一部充当。allocated_amount=8800.00（2026-09-14修正: 旧値は両方
    -- 4000.00で、TODO.md 7-1決定1（対象額=amount=税抜8000.00）だと充当完了になり
    -- 一部充当の境界値が消える。請求額8800.00（税込）を超える過入金にすることで、
    -- 一部充当（8800.00 < 10000.00）の境界値を維持しつつ、過入金ケースも網羅する）。
    (N'RCP_SLP001', 1, N'CUS002', 2, N'BIL_SLP001', 8800.00, 0.00,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 7. 明細請求書（発行済／取消の2状態）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.detail_invoices
    (detail_invoice_number, customer_code, customer_name, addressee_name, issue_date,
     sales_amount, tax_amount, total_amount,
     standard_rate_taxable_amount, standard_rate_tax_amount, reduced_rate_taxable_amount, reduced_rate_tax_amount, tax_exempt_amount,
     invoice_status, issued_at, issued_by,
     created_by, created_at, updated_by, updated_at)
VALUES
    -- 発行済
    -- sales_amount/tax_amount/total_amount/reduced_rate_*は2026-09-14修正。
    -- DetailInvoiceService.IssueAsyncはConsumptionTaxCalculator.CalculateInternalTaxPerLineの
    -- TaxableAmount（=Amount-Tax。税抜）をsales_amountに入れるため、SALLIN002（amount=8250、
    -- tax_amount=612。上記修正と連動）から sales_amount=8250-612=7638・tax_amount=612・
    -- total_amount=7638+612=8250 が正しい。旧値（8250/611/8861）はamount（税込）をそのまま
    -- sales_amountに入れており、税額を二重に加算した誤り（TODO.md 7-1の実装検証で発見）。
    (N'DIV001', N'CUS003', N'石山市立石山小学校', N'石山小学校5年1組 佐藤先生', '2026-07-20',
     7638.00, 612.00, 8250.00,
     0.00, 0.00, 7638.00, 612.00, 0.00,
     1, '2026-07-20T10:00:00', N'EMP001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- 取消済
    (N'DIV002', N'CUS003', N'石山市立石山小学校', N'石山小学校6年2組 田中先生', '2026-07-05',
     3000.00, 240.00, 3240.00,
     0.00, 0.00, 3000.00, 240.00, 0.00,
     2, '2026-07-05T10:00:00', N'EMP001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

UPDATE dbo.detail_invoices
    SET cancelled_at = '2026-07-06T09:00:00', cancelled_by = N'EMP002'
    WHERE detail_invoice_number = N'DIV002';
GO

-- -----------------------------------------------------------------------------
-- 8. 連携テーブル（DIV001 と請求済の売上明細行を紐付け）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.detail_invoice_sales_lines
    (detail_invoice_number, sales_slip_number, sales_line_number, created_by, created_at, updated_by, updated_at)
VALUES
    (N'DIV001', N'SALLIN002', 1, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 9. 明細入金（充当先2種類: 売上明細行を直接指定／明細請求書を指定）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.detail_receipts
    (detail_receipt_number, line_number, receipt_date, customer_code, customer_name, deposit_method_code,
     bank_account_code, receipt_amount, target_type,
     target_sales_slip_number, target_sales_line_number, target_detail_invoice_number,
     allocated_amount, fee_adjustment_amount, allocation_status,
     created_by, created_at, updated_by, updated_at)
VALUES
    -- target_type=SalesLine（SALLIN003を直接指定。消込完了済の実績）
    (N'DRC001', 1, '2026-07-25', N'CUS003', N'石山市立石山小学校', N'CASH',
     NULL, 2750.00, 1,
     N'SALLIN003', 1, NULL,
     2750.00, 0.00, 3,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- target_type=DetailInvoice（DIV001を指定）
    -- receipt_amount/allocated_amountは8250.00（2026-09-14修正: DIV001のtotal_amount修正
    -- （8861.00→8250.00）に追従。DIV001を全額入金した実績のため常にDIV001.total_amountと
    -- 一致させる）。
    (N'DRC002', 1, '2026-07-22', N'CUS003', N'石山市立石山小学校', N'TRANSFER',
     N'BNK001', 8250.00, 2,
     NULL, NULL, N'DIV001',
     8250.00, 0.00, 3,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 10. 月次締め（得意先×月末日で1レコード。billing の締め期間とは別に暦月で集計する。
--     確定／解除済の2状態を網羅。直近月はレコードを作らず「未締め」を再現）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.monthly_closings
    (closing_date, customer_code, tax_unit, customer_name,
     previous_balance, sales_amount, receipt_amount, tax_amount, closing_balance,
     standard_rate_taxable_amount, standard_rate_tax_amount, reduced_rate_taxable_amount, reduced_rate_tax_amount, tax_exempt_amount,
     closing_status, confirmed_at, confirmed_by,
     created_by, created_at, updated_by, updated_at)
VALUES
    -- 202601分（確定）
    ('2026-01-31', N'CUS001', 1, N'株式会社山田商事',
     0.00, 10000.00, 0.00, 1000.00, 11000.00,
     10000.00, 1000.00, 0.00, 0.00, 0.00,
     1, '2026-02-01T09:00:00', N'EMP002', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    ('2026-01-31', N'CUS002', 2, N'鈴木工業株式会社',
     0.00, 8000.00, 0.00, 800.00, 8800.00,
     8000.00, 800.00, 0.00, 0.00, 0.00,
     1, '2026-02-01T09:00:00', N'EMP002', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    ('2026-01-31', N'CUS003', 3, N'石山市立石山小学校',
     0.00, 8250.00, 0.00, 650.00, 8900.00,
     0.00, 0.00, 8250.00, 650.00, 0.00,
     1, '2026-02-01T09:00:00', N'EMP002', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- 202602分（確定 → 解除済に更新）
    ('2026-02-28', N'CUS001', 1, N'株式会社山田商事',
     11000.00, 5000.00, 11000.00, 500.00, 5500.00,
     5000.00, 500.00, 0.00, 0.00, 0.00,
     1, '2026-03-01T09:00:00', N'EMP002', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    ('2026-02-28', N'CUS002', 2, N'鈴木工業株式会社',
     8800.00, 4000.00, 8800.00, 400.00, 4400.00,
     4000.00, 400.00, 0.00, 0.00, 0.00,
     1, '2026-03-01T09:00:00', N'EMP002', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    ('2026-02-28', N'CUS003', 3, N'石山市立石山小学校',
     8900.00, 10185.00, 8900.00, 815.00, 11000.00,
     0.00, 0.00, 10185.00, 815.00, 0.00,
     1, '2026-03-01T09:00:00', N'EMP002', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

UPDATE dbo.monthly_closings
    SET closing_status = 2, released_at = '2026-03-02T09:00:00', released_by = N'EMP002'
    WHERE closing_date = '2026-02-28';
GO
