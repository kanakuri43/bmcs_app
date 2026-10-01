-- =============================================================================
-- 023_add_slip_employee_code.sql
-- 受注（orders）・売上（sales）に伝票の担当者（employee_code）を追加する。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.9節・2.15節
--
-- 背景: 得意先の自社担当営業（customers.sales_employee_code）とは別に、その売上
-- 自体の担当者を伝票へ登録する。slip_remarks と同じ「伝票単位の値（全明細行へ
-- 複写）」で、任意項目（NULL 可）。既存の伝票は NULL のまま。
-- 対象は受注・売上のみで、入金（receipts）・明細入金（detail_receipts）は対象外。
-- =============================================================================

USE bmcs_db;
GO

IF COL_LENGTH('dbo.orders', 'employee_code') IS NULL
BEGIN
    ALTER TABLE dbo.orders ADD employee_code varchar(10) NULL;
END
GO

IF COL_LENGTH('dbo.sales', 'employee_code') IS NULL
BEGIN
    ALTER TABLE dbo.sales ADD employee_code varchar(10) NULL;
END
GO

IF OBJECT_ID(N'dbo.FK_orders_employees', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.orders WITH CHECK
        ADD CONSTRAINT FK_orders_employees
            FOREIGN KEY (employee_code) REFERENCES dbo.employees (employee_code);
END
GO

IF OBJECT_ID(N'dbo.FK_sales_employees', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.sales WITH CHECK
        ADD CONSTRAINT FK_sales_employees
            FOREIGN KEY (employee_code) REFERENCES dbo.employees (employee_code);
END
GO
