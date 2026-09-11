-- =============================================================================
-- 013_add_billing_confirmed_unique_index.sql
-- billing に、同一得意先・同一締め年月の確定済み請求データが2件存在できないことを
-- DB側でも強制するフィルタ付き一意インデックスを追加する（TODO.md 6-1）。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.12節、docs/product-spec.md 共通業務ルール2
--
-- 背景: アプリ側（BillingClosingService）は確定前に同一 (customer_code, closing_year_month)
-- の確定済み billing の有無を確認してから確定するが、DB側にも同じ制約を持たせ、
-- アプリのバグや同時実行による二重確定を最終防衛線として拒否する
-- （product-spec.md「二重請求はデータベース側でも拒否する」の締め請求版）。
--
-- billing_status = 1（確定）のみを対象にしたフィルタ付き一意インデックスにする。
-- 解除済み（billing_status = 2）は対象外にすることで、締め解除→再締めで同一
-- (得意先, 締め年月) に新しい billing_number を採番する運用（6-2）を壊さない
-- （既存の IX_billing_customer_code_closing_year_month が非フィルタで UNIQUE にできない
-- としていたのと同じ理由。scripts/010_unify_tax_unit_tables.sql 参照）。
-- =============================================================================

USE bmcs_db;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UQ_billing_customer_closing_ym_confirmed' AND object_id = OBJECT_ID(N'dbo.billing'))
BEGIN
    CREATE UNIQUE INDEX UQ_billing_customer_closing_ym_confirmed
        ON dbo.billing (customer_code, closing_year_month)
        WHERE billing_status = 1 AND is_deleted = 0;
END
GO
