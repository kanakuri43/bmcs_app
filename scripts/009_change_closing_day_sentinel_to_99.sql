-- customer.closing_day の「末日締め」専用値を 31 → 99 に変更する。
-- 変更理由: 31 は実日付（毎月31日締め）と衝突するため、実日付と衝突しない 99 を専用センチネルとする。
-- 1〜31 はすべて実日付として扱い、99 のみが「末日締め」を表す。

ALTER TABLE dbo.customer DROP CONSTRAINT CK_customer_tax_unit_closing_day;
GO

ALTER TABLE dbo.customer DROP CONSTRAINT CK_customer_closing_day;
GO

ALTER TABLE dbo.customer ADD CONSTRAINT CK_customer_closing_day
    CHECK (closing_day BETWEEN 0 AND 31 OR closing_day = 99);
GO

ALTER TABLE dbo.customer ADD CONSTRAINT CK_customer_tax_unit_closing_day
    CHECK ((tax_unit = 3 AND closing_day = 0)
        OR (tax_unit IN (1, 2) AND (closing_day BETWEEN 1 AND 31 OR closing_day = 99)));
GO

-- 既存データ移行: 旧センチネル値(31)で登録されていた「末日締め」を新センチネル(99)へ更新する。
UPDATE dbo.customer SET closing_day = 99 WHERE closing_day = 31;
GO
