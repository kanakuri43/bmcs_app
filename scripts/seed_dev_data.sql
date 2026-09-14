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
--
-- 010_unify_tax_unit_tables.sql での統合に追従し、sales / receipt / billing
-- （旧・税単位別8テーブル）は tax_unit 列を持つ単一テーブルへの投入に変更した。
--
-- 適用: sqlcmd -S 172.16.3.171 -U sa -d bmcs_db -C -i scripts\seed_dev_data.sql
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- 1. 既存テストデータの削除（FKの逆順）
-- -----------------------------------------------------------------------------
DELETE FROM dbo.detail_invoice_sales_line WHERE detail_invoice_number IN (N'DIV001', N'DIV002');
DELETE FROM dbo.detail_receipt WHERE detail_receipt_number IN (N'DRC001', N'DRC002');
DELETE FROM dbo.detail_invoice WHERE detail_invoice_number IN (N'DIV001', N'DIV002');
DELETE FROM dbo.receipt WHERE receipt_slip_number IN (N'RCP_INV001', N'RCP_INV002', N'RCP_SLP001');
DELETE FROM dbo.sales WHERE sales_slip_number IN (N'SALINV001', N'SALINV002', N'SALSLP001', N'SALSLP002',
                                                   N'SALLIN001', N'SALLIN002', N'SALLIN003', N'SALLIN004');
DELETE FROM dbo.billing WHERE billing_number IN (N'BIL_INV001', N'BIL_INV002', N'BIL_SLP001');
DELETE FROM dbo.order_slip WHERE order_slip_number IN (N'ORD001', N'ORD002', N'ORD003', N'ORD004');
DELETE FROM dbo.monthly_closing WHERE closing_date IN ('2026-01-31', '2026-02-28');
DELETE FROM dbo.menu WHERE menu_code IN (N'MNU_SALES', N'MNU_ADMIN', N'MNU_PARENT');
DELETE FROM dbo.customer WHERE customer_code IN (N'CUS001', N'CUS002', N'CUS003');
DELETE FROM dbo.product WHERE product_code IN (N'PRD001', N'PRD002', N'PRD003');
DELETE FROM dbo.bank_account WHERE bank_account_code IN (N'BNK001');
DELETE FROM dbo.company_info WHERE company_info_id = 1;
DELETE FROM dbo.employee WHERE employee_code IN (N'EMP001', N'EMP002');
GO

-- -----------------------------------------------------------------------------
-- 2. マスタ
-- -----------------------------------------------------------------------------

-- 社員（一般／管理者）
INSERT INTO dbo.employee (employee_code, employee_name, employee_name_kana, permission_level, created_by, created_at, updated_by, updated_at)
VALUES
    (N'EMP001', N'営業一郎', N'ｴｲｷﾞｮｳｲﾁﾛｳ', 1, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'EMP002', N'管理者太郎', N'ｶﾝﾘｼｬﾀﾛｳ', 9, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- 得意先: 税単位3種（Invoice/Slip/Line）を1つずつ網羅
-- CK_customer_tax_unit_closing_day の相互制約（tax_unit=3 ⇔ closing_day=0）を満たす。
INSERT INTO dbo.customer
    (customer_code, customer_name, customer_name_kana, postal_code, address1, contact_person_name,
     sales_employee_code, closing_day, tax_unit, rounding_type, print_representative_flag,
     created_by, created_at, updated_by, updated_at)
VALUES
    (N'CUS001', N'株式会社山田商事', N'ﾔﾏﾀﾞｼｮｳｼﾞ', N'100-0001', N'東京都千代田区1-1-1', N'山田太郎',
     N'EMP001', 20, 1, 1, 0,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'CUS002', N'鈴木工業株式会社', N'ｽｽﾞｷｺｳｷﾞｮｳ', N'150-0001', N'東京都渋谷区2-2-2', N'鈴木花子',
     N'EMP001', 99, 2, 2, 0,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'CUS003', N'石山市立石山小学校', N'ｲｼﾔﾏｼﾘﾂｲｼﾔﾏｼｮｳｶﾞｯｺｳ', N'400-0001', N'山梨県甲府市3-3-3', N'佐藤先生',
     N'EMP002', 0, 3, 3, 1,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- 商品: 税種別区分3種を1つずつ網羅
INSERT INTO dbo.product
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
INSERT INTO dbo.bank_account
    (bank_account_code, bank_name, branch_name, account_type, account_number, account_holder_name,
     is_print_on_invoice, display_order, created_by, created_at, updated_by, updated_at)
VALUES
    (N'BNK001', N'石山銀行', N'本店', 1, N'1234567', N'ｲｼﾔﾏｼｮｳﾃﾝ', 1, 1,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- 自社情報（1レコード運用）
INSERT INTO dbo.company_info
    (company_info_id, company_name, invoice_registration_number, postal_code, address1,
     phone_number, representative_name, created_by, created_at, updated_by, updated_at)
VALUES
    (1, N'株式会社石山商店', N'T1234567890123', N'400-0000', N'山梨県甲府市本町1-1',
     N'055-000-0000', N'石山一郎', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- メニュー: 親子階層＋CK_menu_leaf（親は権限NULL、子は権限あり）
INSERT INTO dbo.menu
    (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
     created_by, created_at, updated_by, updated_at)
VALUES
    (N'MNU_PARENT', NULL, N'売上管理', 1, NULL, NULL,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_SALES', N'MNU_PARENT', N'売上入力', 1, 1, N'sales_entry',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_ADMIN', N'MNU_PARENT', N'月次締め', 2, 9, N'monthly_closing',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 3. 受注: order_status の4状態を1つずつ網羅
-- -----------------------------------------------------------------------------
INSERT INTO dbo.order_slip
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
INSERT INTO dbo.billing
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

UPDATE dbo.billing
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
    (N'SALSLP002', 1, '2026-07-10', N'CUS002', 2, N'鈴木工業株式会社', 1,
     N'PRD001', N'事務用品セット', 8.000, 1000.0000, 8000.00, 700.0000,
     1, 10.00, 800.00, NULL, '2026-07-10T11:00:00', 1,
     2, 3, 8800.00, NULL, NULL, N'BIL_SLP001',
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
    (N'SALLIN002', 1, '2026-07-20', N'CUS003', 3, N'石山市立石山小学校', 1,
     N'PRD002', N'給食用食材', 15.000, 550.0000, 8250.00, 350.0000,
     2, 8.00, NULL, 611.00, '2026-07-20T09:00:00', 1,
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
-- 6. 締め入金（統合版。充当状態3種を網羅）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.receipt
    (receipt_slip_number, line_number, receipt_date, customer_code, tax_unit, customer_name, receipt_method,
     bank_account_code, receipt_amount, billing_number, allocated_amount, fee_adjustment_amount,
     allocation_status, created_by, created_at, updated_by, updated_at)
VALUES
    -- CUS001・請求単位: 充当完了
    (N'RCP_INV001', 1, '2026-07-25', N'CUS001', 1, N'株式会社山田商事', 2,
     N'BNK001', 11000.00, N'BIL_INV001', 11000.00, 0.00,
     3, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS001・請求単位: 未充当（前受・過入金）
    (N'RCP_INV002', 1, '2026-08-05', N'CUS001', 1, N'株式会社山田商事', 1,
     NULL, 3000.00, NULL, 0.00, 0.00,
     1, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- CUS002・伝票単位: 一部充当
    (N'RCP_SLP001', 1, '2026-08-01', N'CUS002', 2, N'鈴木工業株式会社', 2,
     N'BNK001', 4000.00, N'BIL_SLP001', 4000.00, 0.00,
     2, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 7. 明細請求書（発行済／取消の2状態）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.detail_invoice
    (detail_invoice_number, customer_code, customer_name, addressee_name, issue_date,
     sales_amount, tax_amount, total_amount,
     standard_rate_taxable_amount, standard_rate_tax_amount, reduced_rate_taxable_amount, reduced_rate_tax_amount, tax_exempt_amount,
     invoice_status, issued_at, issued_by,
     created_by, created_at, updated_by, updated_at)
VALUES
    -- 発行済
    (N'DIV001', N'CUS003', N'石山市立石山小学校', N'石山小学校5年1組 佐藤先生', '2026-07-20',
     8250.00, 611.00, 8861.00,
     0.00, 0.00, 8250.00, 611.00, 0.00,
     1, '2026-07-20T10:00:00', N'EMP001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- 取消済
    (N'DIV002', N'CUS003', N'石山市立石山小学校', N'石山小学校6年2組 田中先生', '2026-07-05',
     3000.00, 240.00, 3240.00,
     0.00, 0.00, 3000.00, 240.00, 0.00,
     2, '2026-07-05T10:00:00', N'EMP001',
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

UPDATE dbo.detail_invoice
    SET cancelled_at = '2026-07-06T09:00:00', cancelled_by = N'EMP002'
    WHERE detail_invoice_number = N'DIV002';
GO

-- -----------------------------------------------------------------------------
-- 8. 連携テーブル（DIV001 と請求済の売上明細行を紐付け）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.detail_invoice_sales_line
    (detail_invoice_number, sales_slip_number, sales_line_number, created_by, created_at, updated_by, updated_at)
VALUES
    (N'DIV001', N'SALLIN002', 1, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 9. 明細入金（充当先2種類: 売上明細行を直接指定／明細請求書を指定）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.detail_receipt
    (detail_receipt_number, line_number, receipt_date, customer_code, customer_name, receipt_method,
     bank_account_code, receipt_amount, target_type,
     target_sales_slip_number, target_sales_line_number, target_detail_invoice_number,
     allocated_amount, fee_adjustment_amount, allocation_status,
     created_by, created_at, updated_by, updated_at)
VALUES
    -- target_type=SalesLine（SALLIN003を直接指定。消込完了済の実績）
    (N'DRC001', 1, '2026-07-25', N'CUS003', N'石山市立石山小学校', 1,
     NULL, 2750.00, 1,
     N'SALLIN003', 1, NULL,
     2750.00, 0.00, 3,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    -- target_type=DetailInvoice（DIV001を指定）
    (N'DRC002', 1, '2026-07-22', N'CUS003', N'石山市立石山小学校', 2,
     N'BNK001', 8861.00, 2,
     NULL, NULL, N'DIV001',
     8861.00, 0.00, 3,
     N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

-- -----------------------------------------------------------------------------
-- 10. 月次締め（得意先×月末日で1レコード。billing の締め期間とは別に暦月で集計する。
--     確定／解除済の2状態を網羅。直近月はレコードを作らず「未締め」を再現）
-- -----------------------------------------------------------------------------
INSERT INTO dbo.monthly_closing
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

UPDATE dbo.monthly_closing
    SET closing_status = 2, released_at = '2026-03-02T09:00:00', released_by = N'EMP002'
    WHERE closing_date = '2026-02-28';
GO
