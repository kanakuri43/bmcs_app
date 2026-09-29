-- =============================================================================
-- 020_add_billing_customer_code.sql
-- 得意先マスタに「請求得意先コード」（billing_customer_code）を追加し、
-- 親子請求（請求集約）を可能にする。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 1-1節・2.1節、docs/design_document.md 28章
--
-- 背景: 各地に支店を持つ会社の各支店（請求集約元）の売上を本社（請求集約先）に
-- 一括請求したいという業務要件（2026-09-29確定）。得意先マスタに
-- billing_customer_code を持たせ、自分自身を指せば従来どおり単独で請求、
-- 他の得意先を指せばその得意先（請求集約先）に売上が集約される。
--
-- 【設計のポイント】「billing_customer_code = customer_code（自分自身を指している
-- ＝請求集約先または単独）」という判定を、CHECK制約ではなく計算列＋複合FKで
-- 表現している。CHECK制約は同一テーブルの他行を参照できないため、
--   1. 請求得意先コードは実在する得意先を指す
--   2. 指し先は必ず「請求集約先自身」（＝請求集約元をさらに別の得意先の
--      請求集約元にすること＝2段以上の階層は作れない）
--   3. 請求集約先と請求集約元は closing_day・tax_unit・rounding_type が一致する
-- という複数行にまたがる整合性はFKでしか表現できない。billing_parent_root_flag
-- は常に1の定数の計算列で、参照側・被参照側の双方に挟むことで「参照先候補は
-- is_billing_root = 1（請求集約先自身）の行に限る」ことをFKに強制させるための
-- 技巧である。
--
-- rounding_type も一致対象に含める理由: tax_unit=2（伝票単位）は売上入力時に
-- 得意先自身の rounding_type で slip_tax_amount を確定するため、請求集約先と
-- 請求集約元で端数区分がずれると締め処理（BillingClosingService.CalculateTaxSummary
-- の再計算値との突合）が例外を投げ、請求集約グループ全体の請求が発行できなく
-- なる。closing_day・tax_unit と同じ扱いでDB制約に含める。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- 1. billing_customer_code を追加する（NULL許容 → 全行backfill → NOT NULL化の3段階）
--    未指定の得意先は自分自身を指す（＝従来どおり単独で請求）状態にする。
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.customers', 'billing_customer_code') IS NULL
BEGIN
    ALTER TABLE dbo.customers ADD billing_customer_code varchar(10) NULL;
END
GO

UPDATE dbo.customers
    SET billing_customer_code = customer_code
    WHERE billing_customer_code IS NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.customers') AND name = N'billing_customer_code' AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.customers ALTER COLUMN billing_customer_code varchar(10) NOT NULL;
END
GO

-- -----------------------------------------------------------------------------
-- 2. 永続化計算列（PERSISTED）を追加する
--    is_billing_root: billing_customer_code = customer_code なら1（請求集約先／単独）、
--                      異なれば0（請求集約元）。EFエンティティにはマップしない。
--    billing_parent_root_flag: 常に1の定数列。EFエンティティにはマップしない。
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.customers', 'is_billing_root') IS NULL
BEGIN
    ALTER TABLE dbo.customers
        ADD is_billing_root AS (CASE WHEN billing_customer_code = customer_code THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END) PERSISTED;
END
GO

IF COL_LENGTH('dbo.customers', 'billing_parent_root_flag') IS NULL
BEGIN
    ALTER TABLE dbo.customers
        ADD billing_parent_root_flag AS (CONVERT(bit, 1)) PERSISTED;
END
GO

-- -----------------------------------------------------------------------------
-- 3. 複合UNIQUE制約（下記FKの参照先）
--    customer_code は既にPKで一意なので、この制約自体が既存データを弾くことは
--    ない（UQ_customers_code_tax_unit と同じ「スーパーキー」の手法）。
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_customers_billing_root_key')
BEGIN
    ALTER TABLE dbo.customers
        ADD CONSTRAINT UQ_customers_billing_root_key
            UNIQUE (customer_code, is_billing_root, closing_day, tax_unit, rounding_type);
END
GO

-- -----------------------------------------------------------------------------
-- 4. 自己参照の複合FK
--    (billing_customer_code, billing_parent_root_flag, closing_day, tax_unit, rounding_type)
--    → customers (customer_code, is_billing_root, closing_day, tax_unit, rounding_type)
--    billing_parent_root_flag が常に1のため、参照先候補は is_billing_root = 1
--    （請求集約先自身）の行に限定される。自己参照FKのため ON UPDATE/DELETE
--    CASCADE は指定できない（SQL Serverの制限）が、closing_day/tax_unit/
--    rounding_type は登録後変更不可、customers は論理削除（物理DELETEしない）
--    という既存方針と両立するため実害はない。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.FK_customers_billing_customer', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.customers WITH CHECK
        ADD CONSTRAINT FK_customers_billing_customer
            FOREIGN KEY (billing_customer_code, billing_parent_root_flag, closing_day, tax_unit, rounding_type)
            REFERENCES dbo.customers (customer_code, is_billing_root, closing_day, tax_unit, rounding_type);
END
GO

-- -----------------------------------------------------------------------------
-- 5. CHECK制約: 都度得意先（tax_unit=3）は請求集約元になれない
--    複合FKだけでは「都度得意先どうしの請求集約」を防げないため、この CHECK が
--    唯一の防壁になる。
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_customers_billing_customer_tax_unit')
BEGIN
    ALTER TABLE dbo.customers WITH CHECK
        ADD CONSTRAINT CK_customers_billing_customer_tax_unit
            CHECK (billing_customer_code = customer_code OR tax_unit IN (1, 2));
END
GO

-- -----------------------------------------------------------------------------
-- 6. インデックス（請求集約グループの解決クエリ: billing_customer_code で
--    引いて customer_code を取る）
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_customers_billing_customer_code' AND object_id = OBJECT_ID(N'dbo.customers'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_customers_billing_customer_code
        ON dbo.customers (billing_customer_code)
        INCLUDE (customer_code);
END
GO
