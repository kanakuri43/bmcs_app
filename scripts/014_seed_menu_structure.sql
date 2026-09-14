-- =============================================================================
-- 014_seed_menu_structure.sql
-- メニュー構成マスタ（menu）の実データ投入（TODO.md 2-7）。
--
-- scripts/seed_dev_data.sql の開発用テストデータとは異なり、これはメインメニュー画面が
-- 実際に使う構成データそのものであり、開発・本番のどちらでも適用する。
-- 既存の menu 行を全件削除してから再投入する（再実行安全）。画面からの編集機能は持たないため、
-- 新しい画面を追加する・権限レベルを見直す場合は本スクリプトを直接書き換えて再適用する。
--
-- 実装済みの画面のみを対象とする。未実装フェーズ（入金・元帳・月次締め・データ検索等）の
-- 項目は、該当フェーズの実装時に本スクリプトへ追記する。
--
-- 権限レベルは "menu.required_permission_level ≦ employee.permission_level" で比較する
-- （数値が大きいほど高権限。docs/database-schema.md 2.4/2.7）。現時点のseedデータでは
-- 一般社員=1（EMP001）、管理者=9（EMP002）の2段階のみのため、本スクリプトも
-- 「一般=1」「管理者専用=9」の2値で割り振る。社員マスタ・締め解除処理（C-8）等の
-- 機密性が高い操作を管理者専用（9）とした（暫定の重み付けであり、必要に応じて本スクリプトの
-- 数値を書き換えるだけで調整できる。コード変更は不要）。
--
-- 適用: sqlcmd -S 172.16.3.171 -U sa -d bmcs_db -C -i scripts\014_seed_menu_structure.sql
-- =============================================================================

USE bmcs_db;
GO

-- 自己参照FKのため、子（リーフ）を先に削除する。
DELETE FROM dbo.menu WHERE parent_menu_code IS NOT NULL;
DELETE FROM dbo.menu WHERE parent_menu_code IS NULL;
GO

INSERT INTO dbo.menu
    (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
     created_by, created_at, updated_by, updated_at)
VALUES
    -- 受注・売上
    (N'MNU_ORDER_SALES', NULL, N'受注・売上', 1, NULL, NULL, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_ORDER_ENTRY', N'MNU_ORDER_SALES', N'受注入力', 1, 1, N'order_entry', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_SALES_ENTRY', N'MNU_ORDER_SALES', N'売上入力', 2, 1, N'sales_entry', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),

    -- 請求
    (N'MNU_BILLING', NULL, N'請求', 2, NULL, NULL, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_BILLING_CLOSE', N'MNU_BILLING', N'請求締め処理', 1, 1, N'billing_closing', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_BILLING_RELEASE', N'MNU_BILLING', N'締め解除処理', 2, 9, N'billing_release', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_DETAIL_INVOICE', N'MNU_BILLING', N'明細請求書発行', 3, 1, N'detail_invoice_issue', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),

    -- マスタ管理
    (N'MNU_MASTER', NULL, N'マスタ管理', 3, NULL, NULL, N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_CUSTOMER_MASTER', N'MNU_MASTER', N'得意先マスタ', 1, 1, N'customer_master', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_PRODUCT_MASTER', N'MNU_MASTER', N'商品マスタ', 2, 1, N'product_master', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_EMPLOYEE_MASTER', N'MNU_MASTER', N'社員マスタ', 3, 9, N'employee_master', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_COMPANY_INFO', N'MNU_MASTER', N'自社情報', 4, 9, N'company_info_settings', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_BANK_MASTER', N'MNU_MASTER', N'銀行マスタ', 5, 1, N'bank_account_master', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()),
    (N'MNU_PRINTER_SETTING', N'MNU_MASTER', N'プリンタ設定', 6, 1, N'printer_settings', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO
