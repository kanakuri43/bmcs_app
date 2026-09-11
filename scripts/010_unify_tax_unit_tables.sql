-- =============================================================================
-- 010_unify_tax_unit_tables.sql
-- 税単位で3分割／2分割されていた伝票テーブルを統合する。
--   sales_tax_unit_invoice / sales_tax_unit_slip / sales_tax_unit_line → sales
--   receipt_tax_unit_invoice / receipt_tax_unit_slip                   → receipt
--   billing_tax_unit_invoice / billing_tax_unit_slip                   → billing
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.8〜2.12, 2.14
--
-- 背景:
--   税単位（請求単位／伝票単位／内税明細単位）ごとにテーブルを物理分割する構成は、
--   共通構造の重複（EF Core の基底クラス＋設定拡張メソッドで吸収せざるを得ない）、
--   税単位ごとに3本／2本のクエリを書き分ける必要、「どのテーブルに書くか」自体が
--   DBで保証されない、といったコストに見合わなかった。税単位を tax_unit 列で表す
--   単一テーブルに統合し、税単位固有の列は NULL 許容＋CHECK 制約で整合性を担保する。
--
-- 【重要】本スクリプトはデータを移行しない。DROP して作り直す。
--   現時点の開発DBには scripts/seed_dev_data.sql が投入した使い捨てデータしか
--   存在しないため。適用後は必ず scripts/seed_dev_data.sql を再実行すること。
--   本番稼働後にこの手順を流用してはならない。
--
-- 整合性の設計（本スクリプトで導入する新しい不変条件）:
--   1. customer に UNIQUE (customer_code, tax_unit) を追加し、sales / receipt /
--      billing は単独列の customer FK ではなく複合 FK
--      (customer_code, tax_unit) → customer (customer_code, tax_unit) を張る。
--      これにより「伝票の税単位は得意先マスタの税区分と必ず一致する」を DB が強制する。
--      分割構成では「どのテーブルに書くか」自体がアプリ判断であり DB は一切保証して
--      いなかった。この副作用として、伝票が1件でも存在する得意先の tax_unit を
--      UPDATE すると FK 違反（Msg 547）で拒否される。CK_customer_tax_unit_closing_day
--      により closing_day も連動するため、締め得意先⇔都度得意先の切替も同様に拒否
--      される。「発行済み伝票の税単位は後から変えられない」という業務ルールの
--      DB側表現であり、アプリ側では既に得意先マスタ画面で編集不可にしているため
--      通常経路では到達しない。
--   2. billing に UNIQUE (billing_number, tax_unit) を追加し、sales / receipt の
--      請求データ参照も複合 FK にする。分割構成では自明だった「税単位をまたいで
--      請求データを参照できない」を統合後も維持するため。
--      billing_number が NULL の行は SQL Server の MATCH SIMPLE により FK 判定が
--      スキップされるので、未請求（sales）・前受金（receipt）はそのまま表現できる。
--
-- 注意: 001〜009 は改変せず、本ファイルで追従する（docs/database-schema.md 3章の
-- 運用ルール）。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- 1. 削除対象テーブルを参照している外部キーを外す
--    sales_tax_unit_line を参照しているのはこの2本だけ（他の FK は削除対象
--    テーブル同士の参照なので、DROP TABLE の順序だけで解消できる）。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.FK_detail_invoice_sales_line_sales_line', N'F') IS NOT NULL
BEGIN
    ALTER TABLE dbo.detail_invoice_sales_line DROP CONSTRAINT FK_detail_invoice_sales_line_sales_line;
END
GO

IF OBJECT_ID(N'dbo.FK_detail_receipt_sales_line', N'F') IS NOT NULL
BEGIN
    ALTER TABLE dbo.detail_receipt DROP CONSTRAINT FK_detail_receipt_sales_line;
END
GO

-- -----------------------------------------------------------------------------
-- 2. 参照先を失う行を削除する
--    売上明細行を参照している行は、新 sales テーブル（空）に対して FK 違反に
--    なるため物理削除する。detail_receipt は CK_detail_receipt_target により
--    target_type = 1 のとき target_sales_slip_number を NULL にできないので、
--    列を空にするのではなく行ごと削除する。
--    削除対象は開発用シードデータのみ（本ファイル冒頭の注意書きを参照）。
--    旧テーブルの存在で条件を切ることで、再実行時に再投入済みデータを消さない。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.sales_tax_unit_line', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.detail_invoice_sales_line;
    DELETE FROM dbo.detail_receipt WHERE target_type = 1;
END
GO

-- -----------------------------------------------------------------------------
-- 3. 旧テーブルの削除（sales_* / receipt_* を先に、billing_* を最後に。
--    sales_* / receipt_* → billing_* を参照しているため）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.sales_tax_unit_invoice', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.sales_tax_unit_invoice;
END
GO

IF OBJECT_ID(N'dbo.sales_tax_unit_slip', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.sales_tax_unit_slip;
END
GO

IF OBJECT_ID(N'dbo.sales_tax_unit_line', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.sales_tax_unit_line;
END
GO

IF OBJECT_ID(N'dbo.receipt_tax_unit_invoice', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.receipt_tax_unit_invoice;
END
GO

IF OBJECT_ID(N'dbo.receipt_tax_unit_slip', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.receipt_tax_unit_slip;
END
GO

IF OBJECT_ID(N'dbo.billing_tax_unit_invoice', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.billing_tax_unit_invoice;
END
GO

IF OBJECT_ID(N'dbo.billing_tax_unit_slip', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.billing_tax_unit_slip;
END
GO

-- -----------------------------------------------------------------------------
-- 4. customer に UNIQUE (customer_code, tax_unit) を追加する
--    customer_code は既に PK で一意なので、この制約自体が既存データを弾くことは
--    ない。目的は sales / receipt / billing から複合 FK を張るための参照先
--    （SQL Server は FK の参照先列と完全一致する一意インデックスを要求する）。
--    既存の CHECK 制約（CK_customer_tax_unit / CK_customer_closing_day /
--    CK_customer_tax_unit_closing_day / CK_customer_rounding_type）、PK_customer、
--    FK_customer_employee はいずれも影響を受けない。UNIQUE 制約の追加は
--    非クラスター化一意インデックスを1本足すだけで、他制約の再検証も
--    テーブルの作り直しも発生しない。
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_customer_code_tax_unit')
BEGIN
    ALTER TABLE dbo.customer
        ADD CONSTRAINT UQ_customer_code_tax_unit UNIQUE (customer_code, tax_unit);
END
GO

-- -----------------------------------------------------------------------------
-- 5. billing（請求データ・統合版）
--    旧 billing_tax_unit_invoice / billing_tax_unit_slip は構造が完全に共通
--    だったため、tax_unit を足すだけで統合できる。明細請求書（detail_invoice）は
--    繰越残高の概念がないため請求データではなく、統合対象外（据え置き）。
--    sales / receipt から参照されるので先に作成する。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.billing', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.billing
    (
        billing_number                  varchar(20)     NOT NULL,
        customer_code                   varchar(10)     NOT NULL,
        -- 1=請求単位 / 2=伝票単位。内税明細単位(3)は請求データを持たない
        -- （明細請求書 detail_invoice が担う）。
        tax_unit                        tinyint         NOT NULL,
        customer_name                   nvarchar(60)    NOT NULL,
        billing_date                    date            NOT NULL,
        closing_year_month              char(6)         NOT NULL,
        previous_balance                decimal(15, 2)  NOT NULL,
        receipt_amount                  decimal(15, 2)  NOT NULL,
        sales_amount                    decimal(15, 2)  NOT NULL,
        tax_amount                      decimal(15, 2)  NOT NULL,
        current_billing_amount          decimal(15, 2)  NOT NULL,
        standard_rate_taxable_amount    decimal(15, 2)  NOT NULL,
        standard_rate_tax_amount        decimal(15, 2)  NOT NULL,
        reduced_rate_taxable_amount     decimal(15, 2)  NOT NULL,
        reduced_rate_tax_amount         decimal(15, 2)  NOT NULL,
        tax_exempt_amount               decimal(15, 2)  NOT NULL,
        billing_status                  tinyint         NOT NULL,
        confirmed_at                    datetime2(3)    NOT NULL,
        confirmed_by                    varchar(10)     NOT NULL,
        released_at                     datetime2(3)    NULL,
        released_by                     varchar(10)     NULL,

        is_deleted                      bit             NOT NULL CONSTRAINT DF_billing_is_deleted DEFAULT (0),
        created_by                      varchar(10)     NOT NULL,
        created_at                      datetime2(3)    NOT NULL,
        updated_by                      varchar(10)     NOT NULL,
        updated_at                      datetime2(3)    NOT NULL,
        row_version                     rowversion      NOT NULL,

        CONSTRAINT PK_billing PRIMARY KEY (billing_number),

        -- sales / receipt から複合 FK で参照させるための一意制約。
        -- billing_number 単独で既に一意なので論理的には冗長だが、SQL Server は
        -- FK の参照先列と完全一致する一意インデックスを要求するため必要。
        CONSTRAINT UQ_billing_number_tax_unit UNIQUE (billing_number, tax_unit),

        CONSTRAINT FK_billing_customer
            FOREIGN KEY (customer_code, tax_unit) REFERENCES dbo.customer (customer_code, tax_unit),

        CONSTRAINT CK_billing_tax_unit CHECK (tax_unit IN (1, 2)),
        CONSTRAINT CK_billing_status CHECK (billing_status IN (1, 2))
    );
END
GO

-- -----------------------------------------------------------------------------
-- 6. sales（売上・統合版）
--    旧3テーブルの差分は「税額カラム」と「billing_number の有無」だけだった
--    （docs/database-schema.md 2.9 の「テーブルごとに異なるカラム」）。
--    これを NULL 許容カラム＋tax_unit との対応を強制する CHECK で表現する。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.sales', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sales
    (
        sales_slip_number           varchar(20)     NOT NULL,
        line_number                 smallint        NOT NULL,
        slip_date                   date            NOT NULL,
        customer_code                varchar(10)     NOT NULL,
        -- 1=請求単位 / 2=伝票単位 / 3=内税明細単位。得意先マスタの税区分の
        -- スナップショットではなく、複合 FK でマスタと常に一致することを保証する。
        tax_unit                    tinyint         NOT NULL,
        customer_name                nvarchar(60)    NOT NULL,
        slip_type                   tinyint         NOT NULL,
        product_code                varchar(20)     NOT NULL,
        product_name                nvarchar(60)    NOT NULL,
        specification               nvarchar(60)    NULL,
        unit_name                   nvarchar(10)    NULL,
        quantity                    decimal(13, 3)  NOT NULL,
        -- 単価は tax_unit = 3 のとき内税単価、1 / 2 のとき外税単価のスナップショット。
        unit_price                  decimal(15, 4)  NOT NULL,
        amount                      decimal(15, 2)  NOT NULL,
        cost_price                  decimal(15, 4)  NOT NULL,
        tax_category                tinyint         NOT NULL,
        tax_rate                    decimal(5, 2)   NOT NULL,
        -- 伝票単位の税額。tax_unit = 2 のときのみ値を持つ（同一伝票の全行に同値。
        -- SUM してはいけない）。
        slip_tax_amount             decimal(15, 2)  NULL,
        -- 行ごとの内税額。tax_unit = 3 のときのみ値を持つ。
        tax_amount                  decimal(15, 2)  NULL,
        delivery_note_issued_at     datetime2(3)    NULL,
        delivery_note_issue_count   smallint        NOT NULL,
        billing_status               tinyint        NOT NULL,
        settlement_status           tinyint         NOT NULL,
        settled_amount               decimal(15, 2)  NOT NULL,
        order_slip_number           varchar(20)     NULL,
        order_line_number           smallint        NULL,
        -- 締め請求データへの参照。tax_unit = 1 / 2 のときのみ使用し、NULL = 未請求。
        -- tax_unit = 3 は detail_invoice_sales_line 経由で明細請求書と紐付ける。
        billing_number               varchar(20)     NULL,

        is_deleted                   bit             NOT NULL CONSTRAINT DF_sales_is_deleted DEFAULT (0),
        created_by                   varchar(10)     NOT NULL,
        created_at                   datetime2(3)    NOT NULL,
        updated_by                   varchar(10)     NOT NULL,
        updated_at                   datetime2(3)    NOT NULL,
        row_version                  rowversion      NOT NULL,

        CONSTRAINT PK_sales PRIMARY KEY (sales_slip_number, line_number),

        CONSTRAINT FK_sales_customer
            FOREIGN KEY (customer_code, tax_unit) REFERENCES dbo.customer (customer_code, tax_unit),
        CONSTRAINT FK_sales_product
            FOREIGN KEY (product_code) REFERENCES dbo.product (product_code),
        CONSTRAINT FK_sales_order_slip
            FOREIGN KEY (order_slip_number, order_line_number) REFERENCES dbo.order_slip (order_slip_number, line_number),
        -- billing_number が NULL の行（未請求）は MATCH SIMPLE により検査対象外。
        CONSTRAINT FK_sales_billing
            FOREIGN KEY (billing_number, tax_unit) REFERENCES dbo.billing (billing_number, tax_unit),

        CONSTRAINT CK_sales_tax_unit CHECK (tax_unit IN (1, 2, 3)),
        CONSTRAINT CK_sales_slip_type CHECK (slip_type IN (1, 2, 3)),
        CONSTRAINT CK_sales_tax_category CHECK (tax_category IN (1, 2, 3)),
        CONSTRAINT CK_sales_billing_status CHECK (billing_status IN (1, 2)),
        CONSTRAINT CK_sales_settlement_status CHECK (settlement_status IN (1, 2, 3)),

        -- 税単位と税額カラムの対応を1対1に固定する。分割構成では
        -- 「そのテーブルにその列が無い」ことで表現されていた制約に相当する。
        --   1=請求単位       … 請求締め時に一括計算するため伝票時点では税額が無い
        --   2=伝票単位       … 伝票登録時に伝票単位で税額を確定する
        --   3=内税明細単位   … 明細行ごとに内税額を確定する（amount は税込金額）
        CONSTRAINT CK_sales_tax_amount_by_tax_unit
            CHECK (
                   (tax_unit = 1 AND slip_tax_amount IS NULL     AND tax_amount IS NULL)
                OR (tax_unit = 2 AND slip_tax_amount IS NOT NULL AND tax_amount IS NULL)
                OR (tax_unit = 3 AND slip_tax_amount IS NULL     AND tax_amount IS NOT NULL)
            ),

        -- 内税明細単位（3）は締め請求データを持たない。
        CONSTRAINT CK_sales_billing_number_by_tax_unit
            CHECK (billing_number IS NULL OR tax_unit IN (1, 2))
    );
END
GO

-- -----------------------------------------------------------------------------
-- 7. receipt（締め入金・統合版）
--    旧 receipt_tax_unit_invoice / receipt_tax_unit_slip は構造が完全に共通。
--    内税明細単位（3）の入金は detail_receipt が担うため、tax_unit は 1 / 2 のみ。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.receipt', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.receipt
    (
        receipt_slip_number     varchar(20)     NOT NULL,
        line_number             smallint        NOT NULL,
        receipt_date            date            NOT NULL,
        customer_code           varchar(10)     NOT NULL,
        tax_unit                tinyint         NOT NULL,
        customer_name           nvarchar(60)    NOT NULL,
        receipt_method          tinyint         NOT NULL,
        bank_account_code       varchar(10)     NULL,
        -- 伝票単位の値。SUM してはいけない。
        receipt_amount          decimal(15, 2)  NOT NULL,
        -- 充当先の請求データ。NULL = 前受・過入金（充当先未定）。
        billing_number          varchar(20)     NULL,
        allocated_amount        decimal(15, 2)  NOT NULL,
        fee_adjustment_amount   decimal(15, 2)  NOT NULL,
        allocation_status       tinyint         NOT NULL,

        is_deleted              bit             NOT NULL CONSTRAINT DF_receipt_is_deleted DEFAULT (0),
        created_by              varchar(10)     NOT NULL,
        created_at              datetime2(3)    NOT NULL,
        updated_by              varchar(10)     NOT NULL,
        updated_at              datetime2(3)    NOT NULL,
        row_version             rowversion      NOT NULL,

        CONSTRAINT PK_receipt PRIMARY KEY (receipt_slip_number, line_number),

        CONSTRAINT FK_receipt_customer
            FOREIGN KEY (customer_code, tax_unit) REFERENCES dbo.customer (customer_code, tax_unit),
        CONSTRAINT FK_receipt_bank_account
            FOREIGN KEY (bank_account_code) REFERENCES dbo.bank_account (bank_account_code),
        -- billing_number が NULL の行（前受・過入金）は MATCH SIMPLE により検査対象外。
        CONSTRAINT FK_receipt_billing
            FOREIGN KEY (billing_number, tax_unit) REFERENCES dbo.billing (billing_number, tax_unit),

        CONSTRAINT CK_receipt_tax_unit CHECK (tax_unit IN (1, 2)),
        CONSTRAINT CK_receipt_method CHECK (receipt_method IN (1, 2, 3, 4)),
        CONSTRAINT CK_receipt_allocation_status CHECK (allocation_status IN (1, 2, 3))
    );
END
GO

-- -----------------------------------------------------------------------------
-- 8. 手順1で外した外部キーを新 sales に張り直す
--    参照先テーブル名が sales_tax_unit_line → sales に変わったため、制約名の
--    末尾 _sales_line も _sales に改める。
--    明細請求書・明細入金が参照できるのは実際には tax_unit = 3 の行だけだが、
--    複合 FK にすると detail_invoice_sales_line / detail_receipt 側にも tax_unit を
--    持たせる必要があり非正規化が増えるため、ここは単純な複合キー参照に留める。
--    「tax_unit = 3 の売上行のみを対象とする」はアプリ側の抽出条件で担保する。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.FK_detail_invoice_sales_line_sales', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.detail_invoice_sales_line WITH CHECK
        ADD CONSTRAINT FK_detail_invoice_sales_line_sales
            FOREIGN KEY (sales_slip_number, sales_line_number)
            REFERENCES dbo.sales (sales_slip_number, line_number);
END
GO

IF OBJECT_ID(N'dbo.FK_detail_receipt_sales', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.detail_receipt WITH CHECK
        ADD CONSTRAINT FK_detail_receipt_sales
            FOREIGN KEY (target_sales_slip_number, target_sales_line_number)
            REFERENCES dbo.sales (sales_slip_number, line_number);
END
GO

-- -----------------------------------------------------------------------------
-- 9. 非クラスター化インデックス
--    本スキーマはこれまで PK 以外のインデックスを1本も持っていない
--    （UQ_detail_invoice_sales_line_sales_line のみ）。統合により1テーブルあたりの
--    行数が2〜3倍になるため、主要な業務クエリが確実にテーブルスキャンになる
--    箇所だけに絞って追加する。
--
--    フィルター付きインデックス（WHERE 句付き）は使わない。作成時・DML 時ともに
--    QUOTED_IDENTIFIER ON を要求するが、本プロジェクトの適用手段である sqlcmd は
--    既定で QUOTED_IDENTIFIER OFF のため、seed_dev_data.sql の INSERT が失敗する
--    （-I オプションが必須になる）。NULL は通常のインデックスでもキー値として
--    格納・シークできるので、未発行抽出は通常インデックスで足りる。
-- -----------------------------------------------------------------------------

-- 売上ジャーナル・得意先元帳・締め請求の対象売上抽出は、いずれも
-- 「得意先 × 伝票日付の範囲」で引く。統合後の sales で最も件数が多いクエリ。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_sales_customer_code_slip_date' AND object_id = OBJECT_ID(N'dbo.sales'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_sales_customer_code_slip_date
        ON dbo.sales (customer_code, slip_date);
END
GO

-- 請求書の明細部分の組み立て・再発行、および締め解除時の売上行の一括差し戻しは
-- billing_number で売上行を引く。FK_sales_billing の子側インデックスも兼ねる。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_sales_billing_number' AND object_id = OBJECT_ID(N'dbo.sales'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_sales_billing_number
        ON dbo.sales (billing_number);
END
GO

-- 納品書の一括発行は delivery_note_issued_at IS NULL（未発行）を全件から抽出し、
-- 伝票日付順に処理する。先頭列を delivery_note_issued_at にすることで
-- IS NULL がシーク述語になり、第2列 slip_date でソート済みに読める。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_sales_delivery_note_issued_at' AND object_id = OBJECT_ID(N'dbo.sales'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_sales_delivery_note_issued_at
        ON dbo.sales (delivery_note_issued_at, slip_date);
END
GO

-- 入金一覧・得意先元帳の入金行は「得意先 × 入金日の範囲」で引く。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_receipt_customer_code_receipt_date' AND object_id = OBJECT_ID(N'dbo.receipt'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_receipt_customer_code_receipt_date
        ON dbo.receipt (customer_code, receipt_date);
END
GO

-- 請求データに対する充当実績の集計（入金残の算出）と、締め解除時の充当解除は
-- billing_number で入金行を引く。FK_receipt_billing の子側インデックスも兼ねる。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_receipt_billing_number' AND object_id = OBJECT_ID(N'dbo.receipt'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_receipt_billing_number
        ON dbo.receipt (billing_number);
END
GO

-- 締め処理の重複チェック（この得意先のこの締め月の請求データが既にあるか）、
-- 前月請求残高の取得、得意先別の請求履歴。
-- 締め解除 → 再締めで同一 (得意先, 締め年月) に新しい billing_number を採番する
-- 運用のため、非フィルタの UNIQUE にはできない（解除済みの古い行と衝突する）。
-- 確定済み行だけを対象にしたフィルタ付き UNIQUE は 013_add_billing_confirmed_unique_index.sql
-- で別途追加した（Phase 6-1、docs/database-schema.md 2.12節）。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_billing_customer_code_closing_year_month' AND object_id = OBJECT_ID(N'dbo.billing'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_billing_customer_code_closing_year_month
        ON dbo.billing (customer_code, closing_year_month);
END
GO

-- 月次締め画面・請求一覧は得意先を絞らず締め年月だけで全件引くため、
-- 上のインデックス（先頭列が customer_code）では代用できない。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_billing_closing_year_month' AND object_id = OBJECT_ID(N'dbo.billing'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_billing_closing_year_month
        ON dbo.billing (closing_year_month);
END
GO

-- -----------------------------------------------------------------------------
-- 10. 補足
--     slip_number_sequence の sequence_key（'sales_slip' / 'receipt_slip' /
--     'billing' 等）は元々税単位を含まない汎用キーのため、変更不要。
--     detail_invoice / detail_invoice_sales_line / detail_receipt / order_slip /
--     monthly_closing / 各マスタは構造変更なし。
-- -----------------------------------------------------------------------------
