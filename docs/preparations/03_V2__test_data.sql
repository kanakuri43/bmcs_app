-- =============================================================================
-- 販売管理システム テストデータ V1
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 自社情報
-- -----------------------------------------------------------------------------
INSERT INTO company_info (company_name, address, tel, fax, invoice_no) VALUES
    (N'株式会社テスト商事', N'東京都千代田区丸の内1-1-1', N'03-1234-5678', N'03-1234-5679', N'T1234567890123');

-- -----------------------------------------------------------------------------
-- 社員マスタ
-- -----------------------------------------------------------------------------
INSERT INTO employees (employee_code, employee_name) VALUES
    ('E001', N'山田 太郎'),
    ('E002', N'鈴木 花子'),
    ('E003', N'佐藤 次郎');

-- -----------------------------------------------------------------------------
-- 得意先マスタ
-- tax_fraction_id: 1=切捨 2=切上 3=四捨五入
-- tax_calc_unit_id: 1=明細 2=伝票
-- closing_day: 10, 20, 31=末日
-- -----------------------------------------------------------------------------
INSERT INTO customers (customer_code, customer_name, closing_day, tax_fraction_id, tax_calc_unit_id, employee_id) VALUES
    ('C001', N'東京食品株式会社',     31, 1, 2, 1),   -- 末締 切捨 伝票単位 担当:山田
    ('C002', N'大阪雑貨有限会社',     20, 1, 1, 2),   -- 20日締 切捨 明細単位 担当:鈴木
    ('C003', N'名古屋部品工業株式会社', 10, 3, 2, 1);  -- 10日締 四捨五入 伝票単位 担当:山田

-- -----------------------------------------------------------------------------
-- 商品マスタ
-- tax_type_id: 1=外税 2=内税 3=非課税
-- tax_category_id: 1=通常 2=軽減 3=予備
-- -----------------------------------------------------------------------------
INSERT INTO products (product_code, product_name, tax_type_id, tax_category_id) VALUES
    ('P001', N'コーヒー豆 1kg',       1, 2),   -- 外税 軽減税率
    ('P002', N'緑茶 500g',            1, 2),   -- 外税 軽減税率
    ('P003', N'事務用品セット',         1, 1),   -- 外税 通常税率
    ('P004', N'段ボール箱 10枚入り',    1, 1),   -- 外税 通常税率
    ('P005', N'コンサルティング料',     1, 1),   -- 外税 通常税率
    ('P006', N'土地賃借料',            3, 1);   -- 非課税

-- -----------------------------------------------------------------------------
-- 受注データ
-- 受注No: ORD-YYYYMMDD-XXX
-- -----------------------------------------------------------------------------

-- 受注1: C001 東京食品 （受注後売上登録あり）
-- C001: tax_calc_unit_id=2(伝票), tax_fraction_id=1(切捨)
-- 伝票単位計算: 軽減(50000*0.08=4000) + 通常(15000*0.10=1500) = 5500
INSERT INTO orders (order_no, order_date, customer_id, customer_code, customer_name, line_no, product_id, product_code, product_name, quantity, unit_price, tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id, tax_amount, slip_tax_amount, slip_remarks, line_remarks) VALUES
    ('ORD-20260101-001', '2026-01-05', 1, 'C001', N'東京食品株式会社', 1, 1, 'P001', N'コーヒー豆 1kg',  10, 2000, 1, 2, 2, 1, NULL, 5500, N'1月分まとめ発注', NULL),
    ('ORD-20260101-001', '2026-01-05', 1, 'C001', N'東京食品株式会社', 2, 2, 'P002', N'緑茶 500g',      20, 1500, 1, 2, 2, 1, NULL, 5500, N'1月分まとめ発注', NULL),
    ('ORD-20260101-001', '2026-01-05', 1, 'C001', N'東京食品株式会社', 3, 3, 'P003', N'事務用品セット',  5,  3000, 1, 1, 2, 1, NULL, 5500, N'1月分まとめ発注', N'急ぎ');

-- 受注2: C002 大阪雑貨 （一部売上登録あり）
-- C002: tax_calc_unit_id=1(明細), tax_fraction_id=1(切捨)
-- P003: 9000*0.10=900 / P004: 5000*0.10=500
INSERT INTO orders (order_no, order_date, customer_id, customer_code, customer_name, line_no, product_id, product_code, product_name, quantity, unit_price, tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id, tax_amount, slip_tax_amount, slip_remarks, line_remarks) VALUES
    ('ORD-20260110-001', '2026-01-10', 2, 'C002', N'大阪雑貨有限会社', 1, 3, 'P003', N'事務用品セット',    3, 3000, 1, 1, 1, 1, 900, NULL, NULL, NULL),
    ('ORD-20260110-001', '2026-01-10', 2, 'C002', N'大阪雑貨有限会社', 2, 4, 'P004', N'段ボール箱 10枚入り', 10,  500, 1, 1, 1, 1, 500, NULL, NULL, NULL);

-- 受注3: C003 名古屋部品 （未売上）
-- C003: tax_calc_unit_id=2(伝票), tax_fraction_id=3(四捨五入)
-- 伝票単位計算: 50000*0.10=5000
INSERT INTO orders (order_no, order_date, customer_id, customer_code, customer_name, line_no, product_id, product_code, product_name, quantity, unit_price, tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id, tax_amount, slip_tax_amount, slip_remarks, line_remarks) VALUES
    ('ORD-20260115-001', '2026-01-15', 3, 'C003', N'名古屋部品工業株式会社', 1, 5, 'P005', N'コンサルティング料', 1, 50000, 1, 1, 2, 3, NULL, 5000, N'1月分', NULL);

-- -----------------------------------------------------------------------------
-- 売上データ
-- 売上No: SAL-YYYYMMDD-XXX
-- C001: 伝票単位計算 / C002: 明細単位計算
-- -----------------------------------------------------------------------------

-- 売上1: ORD-20260101-001 を参照（C001 伝票単位）
-- 伝票単位: tax_amount=NULL, slip_tax_amount=5500(全行同値), tax_fraction_id=1(切捨)
INSERT INTO sales (sale_no, sale_date, customer_id, customer_code, customer_name, order_id, order_no, employee_id, employee_code, employee_name, line_no, product_id, product_code, product_name, quantity, unit_price, tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id, tax_amount, slip_tax_amount, invoiced_date, slip_remarks, line_remarks) VALUES
    ('SAL-20260110-001', '2026-01-10', 1, 'C001', N'東京食品株式会社', 1, 'ORD-20260101-001', 1, 'E001', N'山田 太郎', 1, 1, 'P001', N'コーヒー豆 1kg',  10, 2000, 1, 2, 2, 1, NULL, 5500, NULL, N'1月分納品', NULL),
    ('SAL-20260110-001', '2026-01-10', 1, 'C001', N'東京食品株式会社', 1, 'ORD-20260101-001', 1, 'E001', N'山田 太郎', 2, 2, 'P002', N'緑茶 500g',      20, 1500, 1, 2, 2, 1, NULL, 5500, NULL, N'1月分納品', NULL),
    ('SAL-20260110-001', '2026-01-10', 1, 'C001', N'東京食品株式会社', 1, 'ORD-20260101-001', 1, 'E001', N'山田 太郎', 3, 3, 'P003', N'事務用品セット',  5,  3000, 1, 1, 2, 1, NULL, 5500, NULL, N'1月分納品', NULL);

-- 売上2: 受注参照なし（C001 伝票単位）
-- 伝票単位: 軽減(10000*0.08=800) + 通常(30000*0.10=3000) = 3800
INSERT INTO sales (sale_no, sale_date, customer_id, customer_code, customer_name, order_id, order_no, employee_id, employee_code, employee_name, line_no, product_id, product_code, product_name, quantity, unit_price, tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id, tax_amount, slip_tax_amount, invoiced_date, slip_remarks, line_remarks) VALUES
    ('SAL-20260120-001', '2026-01-20', 1, 'C001', N'東京食品株式会社', NULL, NULL, 1, 'E001', N'山田 太郎', 1, 1, 'P001', N'コーヒー豆 1kg',    5, 2000, 1, 2, 2, 1, NULL, 3800, NULL, NULL, NULL),
    ('SAL-20260120-001', '2026-01-20', 1, 'C001', N'東京食品株式会社', NULL, NULL, 1, 'E001', N'山田 太郎', 2, 5, 'P005', N'コンサルティング料', 1, 30000, 1, 1, 2, 1, NULL, 3800, NULL, NULL, NULL);

-- 売上3: ORD-20260110-001 を参照（C002 明細単位 1行目のみ部分出荷）
-- 明細単位: tax_amount=900(P003:3000*3*0.10), slip_tax_amount=NULL, tax_fraction_id=1(切捨)
INSERT INTO sales (sale_no, sale_date, customer_id, customer_code, customer_name, order_id, order_no, employee_id, employee_code, employee_name, line_no, product_id, product_code, product_name, quantity, unit_price, tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id, tax_amount, slip_tax_amount, invoiced_date, slip_remarks, line_remarks) VALUES
    ('SAL-20260115-001', '2026-01-15', 2, 'C002', N'大阪雑貨有限会社', 4, 'ORD-20260110-001', 2, 'E002', N'鈴木 花子', 1, 3, 'P003', N'事務用品セット', 3, 3000, 1, 1, 1, 1, 900, NULL, NULL, NULL, N'先行納品');

-- -----------------------------------------------------------------------------
-- 入金データ
-- 入金No: REC-YYYYMMDD-XXX
-- -----------------------------------------------------------------------------

-- 入金1: C001 1月末締分の入金（2月入金）
INSERT INTO receipts (receipt_no, receipt_date, customer_id, customer_code, customer_name, line_no, payment_method_id, amount, invoiced_date, slip_remarks, line_remarks) VALUES
    ('REC-20260205-001', '2026-02-05', 1, 'C001', N'東京食品株式会社', 1, 2, 80000, NULL, N'1月末締分', NULL);

-- 入金2: C002 1月20日締分の入金
INSERT INTO receipts (receipt_no, receipt_date, customer_id, customer_code, customer_name, line_no, payment_method_id, amount, invoiced_date, slip_remarks, line_remarks) VALUES
    ('REC-20260201-001', '2026-02-01', 2, 'C002', N'大阪雑貨有限会社', 1, 1,  9900, NULL, NULL, NULL);

-- 入金3: C001 手形と現金の複数行入金
INSERT INTO receipts (receipt_no, receipt_date, customer_id, customer_code, customer_name, line_no, payment_method_id, amount, invoiced_date, slip_remarks, line_remarks) VALUES
    ('REC-20260210-001', '2026-02-10', 1, 'C001', N'東京食品株式会社', 1, 3, 50000, NULL, N'手形分', NULL),
    ('REC-20260210-001', '2026-02-10', 1, 'C001', N'東京食品株式会社', 2, 1, 10000, NULL, N'現金分', NULL);
