# 販売管理システム Design Document

## 1. 概要

日本の卸売業（B2B）向け販売管理システム。  
中小零細企業の社内LAN運用を想定。

### 技術スタック

| 区分 | 技術 |
|------|------|
| DB | SQL Server 2022 |
| アプリ | C# / WPF |
| UIフレームワーク | Prism / MahApps.Metro |
| ORM | Entity Framework |
| 楽観的排他制御 | `rowversion` |

---

## 2. 主要機能

| 機能 | 説明 |
|------|------|
| 受注管理 | CRUD（売上登録済みは変更・削除不可） |
| 売上管理 | CRUD（請求集計済みは変更・削除不可）・納品書印刷 |
| 入金管理 | CRUD（請求集計済みは変更・削除不可） |
| 請求集計 | 得意先の締日単位。締後は売上・入金の登録不可 |
| 売掛金集計 | 月末1回。締解除可能 |
| マスタ保守 | 得意先・商品・社員・消費税率・各種区分 |
| 売上分析 | 商品・得意先・担当者別 |
| 請求書印刷 | A4縦・全得意先一括または任意1社 |

---

## 3. テーブル構成

### 3.1 マスタ系

```
company_info               自社情報（会社名・住所・TEL・適格請求書番号）
employees                  社員マスタ
customers                  得意先マスタ（締日・消費税端数処理・消費税計算単位）
products                   商品マスタ（課税種別・税率種別）
tax_rate_periods           消費税率マスタ（適用期間で管理。通常・軽減・予備を1行で保持）
```

#### 区分マスタ群

| テーブル | 内容 | 値 |
|----------|------|----|
| `payment_method_classifications` | 入金方法 | 現金・振込・手形・小切手・その他 |
| `tax_type_classifications` | 課税種別 | 外税(01)・内税(02)・非課税(03) |
| `tax_fraction_classifications` | 税端数処理 | 切捨(01)・切上(02)・四捨五入(03) |
| `tax_calc_unit_classifications` | 消費税計算単位 | 明細(01)・伝票(02) |

### 3.2 ジャーナル系

```
orders                     受注（伝票＋明細行）
sales                      売上（伝票＋明細行）
receipts                   入金（伝票＋明細行）
```

### 3.3 集計履歴系

```
invoice_headers            請求ヘッダ履歴（得意先×締日でユニーク）
accounts_receivable_histories  売掛金集計履歴（得意先×月末日でユニーク）
```

---

## 4. ジャーナルの設計方針

### 4.1 共通規則

- **論理削除**: 全テーブルで `is_deleted = 1` による論理削除。物理削除なし。
- **楽観的排他制御**: `row_version`（ROWVERSION型）による更新衝突検知。
- **外部キー**: テーブル間のFK必須。
- **ジャーナル保持**: 登録時点のコード・名称・税率をジャーナル側に複写（マスタ変更の影響を受けない）。

### 4.2 伝票構造

受注・売上・入金は「1伝票 = 複数明細行」の構造。

- `(伝票番号, 行番号)` でユニーク制約。
- 更新は「既存行を全行論理削除 → 新規行を INSERT」の差し替え方式。
- 伝票備考（`slip_remarks`）と行備考（`line_remarks`）を両方保持。

### 4.3 受注 → 売上の連携

- 売上登録時に受注を参照することも、参照せずに独立登録することも可能。
- 売上が参照する受注 `order_id` / `order_no` を売上側に保持。
- **売上登録済みの受注は変更・削除不可**（`usp_orders_update` / `usp_orders_delete` でチェック）。
- `usp_orders_select` では `has_sales` フラグを返す。

### 4.4 消費税の計算

| 設定 | カラム | 説明 |
|------|--------|------|
| **課税種別** | `tax_type_id` | 商品単位で外税・内税・非課税を指定 |
| **税率種別** | `tax_rate_type` | 明細行単位で使用する税率を指定（1=通常, 2=軽減, 3=予備） |
| **適用税率** | `applied_tax_rate` | 登録時点の税率をジャーナル保持（例: 0.1000） |
| **計算単位** | `tax_calc_unit_id` | 得意先マスタで「明細」or「伝票」を指定 |
| **端数処理** | `tax_fraction_id` | 得意先マスタで切捨・切上・四捨五入を指定 |
| **明細単位計算時** | `line_tax_amount` | 行ごとの消費税額を格納 |
| **伝票単位計算時** | `slip_tax_amount` | 伝票全行同値で消費税額を格納 |

#### 税率種別と `tax_rate_periods` の対応

| `tax_rate_type` | 意味 | `tax_rate_periods` カラム | 現行値 |
|-----------------|------|---------------------------|--------|
| 1 | 通常税率 | `primary_tax_rate` | 10% |
| 2 | 軽減税率 | `secondary_tax_rate` | 8% |
| 3 | 予備 | `tertiary_tax_rate` | NULL（未定義） |

インボイス制度（適格請求書等保存方式）対応のため、計算単位は**明細または伝票**。請求単位は不可。

---

## 5. 締め処理の設計

### 5.1 請求集計

- 得意先の `closing_day`（1〜27、99=月末）単位で実行。
- 任意の日付での請求集計も可（仕様上の柔軟性）。
- 集計後、`sales.invoiced_at` / `receipts.invoiced_at` に締日（`date`型）を書き込む。
- **締後のルール**: 集計済み得意先はその締日以前の売上・入金の登録・変更・削除が不可。
- `invoice_headers` に以下を記録：前回請求額・入金額・売上額（標準/軽減）・消費税額（標準/軽減）・今回請求額。

### 5.2 売掛金集計

- 月末1回のみ実行。
- `accounts_receivable_histories.closing_date` は必ず月末日（`EOMONTH` チェック制約）。
- 繰越残高・当月売上・当月消費税・当月入金・月末残高を保持。

### 5.3 締解除

- 請求集計・売掛金集計とも締解除可能（`is_deleted` フラグ管理）。

---

## 6. ストアドプロシージャ一覧

### 命名規則: `usp_{エンティティ}_{操作}`

#### ジャーナル系

| プロシージャ | 処理概要 |
|-------------|----------|
| `usp_orders_upsert` | 受注登録・更新（伝票番号が存在すれば更新、なければ登録。売上登録済みは更新不可） |
| `usp_orders_delete` | 受注論理削除（売上登録済みは不可） |
| `usp_orders_select` | 受注照会（伝票番号・得意先・期間で絞込） |
| `usp_sales_upsert` | 売上登録・更新（伝票番号が存在すれば更新、なければ登録。請求集計済みは更新不可） |
| `usp_sales_delete` | 売上論理削除（請求集計済みは不可） |
| `usp_sales_select` | 売上照会 |
| `usp_receipts_upsert` | 入金登録・更新（伝票番号が存在すれば更新、なければ登録。請求集計済みは更新不可） |
| `usp_receipts_delete` | 入金論理削除（請求集計済みは不可） |
| `usp_receipts_select` | 入金照会 |

#### マスタ系

| プロシージャ | 処理概要 |
|-------------|----------|
| `usp_employees_upsert` | 社員登録・更新（`@employee_id=NULL` で新規） |
| `usp_employees_delete` | 社員論理削除（売上・得意先担当者参照チェック） |
| `usp_products_upsert` | 商品登録・更新（`@product_id=NULL` で新規） |
| `usp_products_delete` | 商品論理削除（受注・売上参照チェック） |
| `usp_customers_upsert` | 得意先登録・更新（`@customer_id=NULL` で新規） |
| `usp_customers_delete` | 得意先論理削除（受注・売上・入金参照チェック） |
| `usp_tax_rate_periods_upsert` | 消費税率期間登録・更新（`@tax_rate_period_id=NULL` で新規） |
| `usp_tax_rate_periods_delete` | 消費税率期間論理削除 |
| `usp_payment_method_classifications_upsert` | 入金方法区分登録・更新 |
| `usp_payment_method_classifications_delete` | 入金方法区分論理削除（入金参照チェック） |
| `usp_tax_type_classifications_upsert` | 課税種別区分登録・更新 |
| `usp_tax_type_classifications_delete` | 課税種別区分論理削除（商品参照チェック） |
| `usp_tax_fraction_classifications_upsert` | 端数処理区分登録・更新 |
| `usp_tax_fraction_classifications_delete` | 端数処理区分論理削除（得意先参照チェック） |
| `usp_tax_calc_unit_classifications_upsert` | 計算単位区分登録・更新 |
| `usp_tax_calc_unit_classifications_delete` | 計算単位区分論理削除（得意先参照チェック） |
| `usp_company_info_upsert` | 自社情報登録・更新（単一行。deleteなし） |

### 共通インターフェース仕様

- 明細行は `NVARCHAR(MAX)` の **JSON配列**（`@lines`）で受け渡し。
- `OPENJSON` + `WITH` 句でパース後、一時テーブル `#lines` に展開。
- バリデーション → `BEGIN TRANSACTION` → INSERT/UPDATE → `COMMIT` の順。
- `SET XACT_ABORT ON` によりエラー時は自動ロールバック。

#### マスタupsertの共通仕様

- `@{entity}_id = NULL` → INSERT（IDENTITY自動採番）
- `@{entity}_id IS NOT NULL` → UPDATE（存在確認後）
- コード列の重複は `is_deleted = 0` のレコード間でのみチェック（他IDとの衝突防止）

#### `@lines` JSONスキーマ（受注・売上共通）

```json
[
  {
    "line_no"          : 1,
    "product_id"       : 1,
    "product_code"     : "P001",
    "product_name"     : "コーヒー豆 1kg",
    "quantity"         : 10.00,
    "unit_price"       : 2000.00,
    "tax_type_id"      : 1,
    "tax_rate_type"    : 2,
    "applied_tax_rate" : 0.0800,
    "line_tax_amount"  : null,
    "slip_tax_amount"  : 1760.00,
    "line_remarks"     : null
  }
]
```

---

## 7. バリデーション方針（共通）

### ジャーナル系 upsert

| チェック | タイミング |
|----------|-----------|
| JSON形式チェック | 最初に実施 |
| 請求集計済み／売上登録済みチェック（更新時のみ） | JSONパース後、他チェック前 |
| 得意先の存在・削除チェック | 都度 |
| 請求集計済み期間チェック | 売上・入金の登録・更新時 |
| 担当社員の存在・削除チェック | 売上のみ |
| 参照受注の存在チェック | 売上・`order_id` 指定時のみ |
| 商品・入金方法の存在チェック | 明細展開後 |
| 明細行の件数チェック（0件不可） | 明細展開後 |

### マスタ系 upsert

| チェック | タイミング |
|----------|-----------|
| 区分マスタの存在・削除チェック（FK先） | 最初に実施 |
| コード重複チェック（`is_deleted=0` かつ他ID） | 区分チェック後 |
| ID存在・削除チェック（UPDATE時のみ） | INSERT/UPDATE分岐後 |

### マスタ系 delete

| チェック | タイミング |
|----------|-----------|
| 対象レコードの存在・削除チェック | 最初に実施 |
| 参照先ジャーナル・マスタの存在チェック | 存在確認後 |

---

## 8. 印刷

| 帳票 | サイズ | タイミング |
|------|--------|-----------|
| 納品書 | A4縦 | 売上登録時（後から再発行も可） |
| 請求書 | A4縦 | 請求集計後（全得意先一括 or 任意1社） |

請求書印字項目: 前回請求額・入金額・売上額（標準税率/軽減税率）・消費税額（標準税率/軽減税率）・今回請求額。
