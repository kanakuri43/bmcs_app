# 販売管理システム Design Document

## 1. 概要

日本の卸売業（B2B）向け販売管理システム。  
中小零細企業の社内LAN運用を想定。

### 技術スタック

| 区分 | 技術 |
|------|------|
| DB | SQL Server 2022 |
| アプリ | C# / WPF |
| UIフレームワーク | Prism.Wpf / MahApps.Metro |
| DB アクセス | Microsoft.Data.SqlClient（SP呼び出し）/ EF Core（参照） |
| 楽観的排他制御 | `rowversion` |
| 設定ファイル | `bin/Debug/bmcs_config.json`（接続文字列） |
| プリンタ設定 | `bin/Debug/bmcs_printer_settings.json` |

---

## 2. 主要機能

### 売掛側

| 機能 | 説明 |
|------|------|
| 受注管理 | CRUD（売上登録済みは変更・削除不可） |
| 売上管理 | CRUD（請求集計済みは変更・削除不可）・納品書印刷 |
| 入金管理 | CRUD（請求集計済みは変更・削除不可） |
| 請求集計 | 得意先の締日単位。締後は売上・入金の登録・変更・削除不可 |
| 売掛金集計 | 月末1回。締解除可能 |
| 伝票横断検索 | 受注・売上・入金・発注・仕入・支払の横断キーワード検索 |
| 請求書印刷 | A4縦・全得意先一括または任意1社 |

### 買掛側

| 機能 | 説明 |
|------|------|
| 発注管理 | CRUD（仕入登録済みは変更・削除不可） |
| 仕入管理 | CRUD（AP集計済みは変更・削除不可） |
| 支払管理 | CRUD（AP集計済みは変更・削除不可） |
| 仕入横断検索 | 発注・仕入・支払の横断キーワード検索 |

### マスタ保守

| 機能 |
|------|
| 得意先マスタ |
| 仕入先マスタ |
| 商品マスタ（原価管理含む） |
| 社員マスタ |
| 消費税率マスタ |
| 入金方法区分 |
| 課税種別区分 |
| 端数処理区分 |
| 計算単位区分 |
| 自社情報（会社名・住所・インボイス番号・振込先口座 1〜3） |

---

## 3. テーブル構成

### 3.1 マスタ系

```
company_info               自社情報（会社名・住所・TEL・FAX・適格請求書番号・振込先口座1〜3）
employees                  社員マスタ
customers                  得意先マスタ（締日・消費税端数処理・消費税計算単位・住所）
suppliers                  仕入先マスタ（締日・消費税端数処理・消費税計算単位・住所）
products                   商品マスタ（課税種別・税率種別・原価）
tax_rate_periods           消費税率マスタ（適用期間で管理。通常・軽減・予備を1行で保持）
```

#### 区分マスタ群

| テーブル | 内容 | 値 |
|----------|------|----|
| `payment_method_classifications` | 入金・支払方法 | 現金・振込・手形・小切手・その他 |
| `tax_type_classifications` | 課税種別 | 外税(01)・内税(02)・非課税(03) |
| `tax_fraction_classifications` | 税端数処理 | 切捨(01)・切上(02)・四捨五入(03) |
| `tax_calc_unit_classifications` | 消費税計算単位 | 明細(01)・伝票(02) |

### 3.2 ジャーナル系

#### 売掛側

```
orders                     受注（伝票＋明細行）
sales                      売上（伝票＋明細行）
receipts                   入金（伝票＋明細行）
```

#### 買掛側

```
purchase_orders            発注（伝票＋明細行）
purchases                  仕入（伝票＋明細行）
payments                   支払（伝票＋明細行）
```

### 3.3 集計履歴系

```
invoice_headers            請求ヘッダ履歴（得意先×請求日でユニーク）
accounts_receivable_histories  売掛金集計履歴（得意先×月末日でユニーク）
```

### 3.4 スキーマ変更履歴（scripts/）

| スクリプト | 内容 |
|---|---|
| `alter_cost_price.sql` | 商品マスタ・売上・仕入・発注明細テーブルに原価カラムを追加 |
| `alter_customers_address.sql` | 得意先マスタに郵便番号・住所1・住所2を追加 |
| `alter_invoice_headers_address.sql` | `invoice_headers` に得意先住所カラムを追加 |
| `alter_journal_address.sql` | 売上・入金ジャーナルに得意先住所カラムを追加 |

---

## 4. ジャーナルの設計方針

### 4.1 共通規則

- **論理削除**: 全テーブルで `is_deleted = 1` による論理削除。物理削除なし。
- **楽観的排他制御**: `row_version`（ROWVERSION型）による更新衝突検知。
- **外部キー**: テーブル間のFK必須。
- **ジャーナル保持**: 登録時点のコード・名称・税率をジャーナル側に複写（マスタ変更の影響を受けない）。

### 4.2 伝票構造

受注・売上・入金・発注・仕入・支払は「1伝票 = 複数明細行」の構造。

- `(伝票番号, 行番号)` でユニーク制約。
- 更新は「既存行を全行論理削除 → 新規行を INSERT」の差し替え方式。
- 伝票備考（`slip_remarks`）と行備考（`line_remarks`）を両方保持。

### 4.3 受注 → 売上の連携

- 売上登録時に受注を参照することも、参照せずに独立登録することも可能。
- 売上が参照する受注 `order_id` / `order_no` を売上側に保持。
- **売上登録済みの受注は変更・削除不可**（`usp_orders_update` / `usp_orders_delete` でチェック）。
- `usp_orders_select` では `has_sales` フラグを返す。

### 4.4 発注 → 仕入の連携

- 仕入登録時に発注を参照することも、参照せずに独立登録することも可能。
- 仕入が参照する発注 `purchase_order_id` / `purchase_order_no` を仕入側に保持。
- **仕入登録済みの発注は変更・削除不可**（`usp_purchase_orders_upsert` / `usp_purchase_orders_delete` でチェック）。
- `usp_purchase_orders_select` では `has_purchases` フラグを返す。

### 4.5 消費税の計算

| 設定 | カラム | 説明 |
|------|--------|------|
| **課税種別** | `tax_type_id` | 商品単位で外税・内税・非課税を指定 |
| **税率種別** | `tax_rate_type` | 明細行単位で使用する税率を指定（1=通常, 2=軽減, 3=予備） |
| **適用税率** | `applied_tax_rate` | 登録時点の税率をジャーナル保持（例: 0.1000） |
| **計算単位** | `tax_calc_unit_id` | 得意先・仕入先マスタで「明細」or「伝票」を指定 |
| **端数処理** | `tax_fraction_id` | 得意先・仕入先マスタで切捨・切上・四捨五入を指定 |
| **明細単位計算時** | `line_tax_amount` | 行ごとの消費税額を格納 |
| **伝票単位計算時** | `slip_tax_amount` | 伝票全行同値で消費税額を格納 |

#### 税率種別と `tax_rate_periods` の対応

| `tax_rate_type` | 意味 | `tax_rate_periods` カラム | 現行値 |
|-----------------|------|---------------------------|--------|
| 1 | 通常税率 | `primary_tax_rate` | 10% |
| 2 | 軽減税率 | `secondary_tax_rate` | 8% |
| 3 | 予備 | `tertiary_tax_rate` | NULL（未定義） |

インボイス制度（適格請求書等保存方式）対応のため、計算単位は**明細または伝票**。請求単位は不可。

#### TaxCalculator（共通サービス）

`bmcs_app.Core/Services/TaxCalculator.cs` に税計算ロジックを集約。  
全メソッド static、副作用なし。受注・売上・仕入・発注で共有。

| メソッド | 説明 |
|---|---|
| `GetAppliedTaxRate` | 日付と税率タイプから適用税率を決定 |
| `CalcLineTaxAmount` | 行税額計算（外税: 金額×税率, 内税: 金額×税率÷(1+税率)。いずれも切捨） |
| `CalcExternalTaxTotal` | 外税合計（明細単位: 行税額合計、伝票単位: 税率グループごとに計算） |
| `CalcInternalTaxTotal` | 内税合計（同上） |

---

## 5. 締め処理の設計

### 5.1 請求集計（売掛側）

- 得意先の `closing_day`（1〜27、99=月末）単位で実行。
- 任意の日付での請求集計も可（仕様上の柔軟性）。
- 集計後、`sales.invoiced_at` / `receipts.invoiced_at` に締日（`date`型）を書き込む。
- **締後のルール**: 集計済み得意先はその締日以前の売上・入金の登録・変更・削除が不可。
- `invoice_headers` に以下を記録：前回請求額・入金額・売上額（標準/軽減）・消費税額（標準/軽減）・今回請求額。

### 5.2 売掛金集計

- 月末1回のみ実行。
- `accounts_receivable_histories.closing_date` は必ず月末日（`EOMONTH` チェック制約）。
- 繰越残高・当月売上・当月消費税・当月入金・月末残高を保持。

### 5.3 伝票ロック判定（売掛側）

```sql
-- どちらか一方でも集計済みなら編集・削除不可
(invoiced_at IS NOT NULL OR ar_aggregated_at IS NOT NULL)
```

- SP（usp_sales_upsert / usp_sales_delete / usp_receipts_upsert / usp_receipts_delete）でチェック。
- ロック解除は集計処理の再実行で当該カラムをNULLに戻す。

#### 締日付カラムの意味

| カラム | 意味 |
|---|---|
| `invoiced_at` | どの請求締めに取り込まれたかを示す締め日付（例: 2026-03-31） |
| `ar_aggregated_at` | どの売掛金集計に取り込まれたかを示す締め日付 |

**重要**: 「処理実行日時」ではなく「どの締め期間に属するか」を示す締め日付。`GETDATE()` は使わない。

### 5.4 締解除

- 請求集計・売掛金集計とも締解除可能（`is_deleted` フラグ管理）。
- 取消時（`usp_invoice_closing_cancel` / `usp_ar_closing_cancel`）は sales・receipts 両方の締日付カラムを NULL に戻す。

---

## 6. ストアドプロシージャ一覧

### 命名規則: `usp_{エンティティ}_{操作}`

#### 売掛側ジャーナル系

| プロシージャ | 処理概要 |
|-------------|----------|
| `usp_orders_upsert` | 受注登録・更新（売上登録済みは更新不可） |
| `usp_orders_delete` | 受注論理削除（売上登録済みは不可） |
| `usp_orders_select` | 受注照会（伝票番号・得意先・期間で絞込。`has_sales` フラグを返す） |
| `usp_sales_upsert` | 売上登録・更新（請求集計済みは更新不可） |
| `usp_sales_delete` | 売上論理削除（請求集計済みは不可） |
| `usp_sales_select` | 売上照会 |
| `usp_receipts_upsert` | 入金登録・更新（請求集計済みは更新不可） |
| `usp_receipts_delete` | 入金論理削除（請求集計済みは不可） |
| `usp_receipts_select` | 入金照会 |

#### 買掛側ジャーナル系

| プロシージャ | 処理概要 |
|-------------|----------|
| `usp_purchase_orders_upsert` | 発注登録・更新（仕入登録済みは更新不可） |
| `usp_purchase_orders_delete` | 発注論理削除（仕入登録済みは不可） |
| `usp_purchase_orders_select` | 発注照会（`has_purchases` フラグを返す） |
| `usp_purchase_orders_summaries_select` | 発注サマリー一覧取得（ダイアログ・ナビ用） |
| `usp_purchases_upsert` | 仕入登録・更新 |
| `usp_purchases_delete` | 仕入論理削除 |
| `usp_purchases_select` | 仕入照会 |
| `usp_purchases_summaries_select` | 仕入サマリー一覧取得 |
| `usp_payments_upsert` | 支払登録・更新 |
| `usp_payments_delete` | 支払論理削除 |
| `usp_payments_select` | 支払照会 |
| `usp_payments_summaries_select` | 支払サマリー一覧取得 |

#### 締め処理系

| プロシージャ | 処理概要 |
|-------------|----------|
| `usp_invoice_closing` | 請求集計（`sales.invoiced_at` / `receipts.invoiced_at` セット、`invoice_headers` 作成） |
| `usp_invoice_closing_cancel` | 請求集計取消（`invoiced_at` を NULL に戻す、`invoice_headers` 論理削除） |
| `usp_ar_closing` | 売掛金集計（`ar_aggregated_at` セット、`accounts_receivable_histories` 作成） |
| `usp_ar_closing_cancel` | 売掛金集計取消 |

#### マスタ系

| プロシージャ | 処理概要 |
|-------------|----------|
| `usp_employees_upsert` | 社員登録・更新（`@employee_id=NULL` で新規） |
| `usp_employees_delete` | 社員論理削除（売上・得意先担当者参照チェック） |
| `usp_products_upsert` | 商品登録・更新（`@product_id=NULL` で新規） |
| `usp_products_delete` | 商品論理削除（受注・売上・発注・仕入参照チェック） |
| `usp_customers_upsert` | 得意先登録・更新（`@customer_id=NULL` で新規） |
| `usp_customers_delete` | 得意先論理削除（受注・売上・入金参照チェック） |
| `usp_suppliers_upsert` | 仕入先登録・更新（`@supplier_id=NULL` で新規） |
| `usp_suppliers_delete` | 仕入先論理削除（発注・仕入・支払参照チェック） |
| `usp_tax_rate_periods_upsert` | 消費税率期間登録・更新（`@tax_rate_period_id=NULL` で新規） |
| `usp_tax_rate_periods_delete` | 消費税率期間論理削除 |
| `usp_payment_method_classifications_upsert` | 入金・支払方法区分登録・更新 |
| `usp_payment_method_classifications_delete` | 入金・支払方法区分論理削除（入金・支払参照チェック） |
| `usp_tax_type_classifications_upsert` | 課税種別区分登録・更新 |
| `usp_tax_type_classifications_delete` | 課税種別区分論理削除（商品参照チェック） |
| `usp_tax_fraction_classifications_upsert` | 端数処理区分登録・更新 |
| `usp_tax_fraction_classifications_delete` | 端数処理区分論理削除（得意先・仕入先参照チェック） |
| `usp_tax_calc_unit_classifications_upsert` | 計算単位区分登録・更新 |
| `usp_tax_calc_unit_classifications_delete` | 計算単位区分論理削除（得意先・仕入先参照チェック） |
| `usp_company_info_upsert` | 自社情報登録・更新（単一行。deleteなし。銀行口座1〜3含む） |
| `usp_invoice_headers_select` | 請求ヘッダ照会（請求日のみで特定。`@closing_day` パラメータなし） |

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

#### `@lines` JSONスキーマ（発注・仕入共通）

```json
[
  {
    "line_no"          : 1,
    "product_id"       : 1,
    "product_code"     : "P001",
    "product_name"     : "コーヒー豆 1kg",
    "quantity"         : 10.00,
    "unit_price"       : 2000.00,
    "cost_price"       : 1800.00,
    "tax_type_id"      : 1,
    "tax_rate_type"    : 2,
    "applied_tax_rate" : 0.0800,
    "line_tax_amount"  : 144.00,
    "line_remarks"     : null
  }
]
```

#### `@lines` JSONスキーマ（支払）

```json
[
  {
    "line_no"             : 1,
    "payment_method_id"   : 1,
    "payment_method_name" : "振込",
    "amount"              : 50000.00,
    "bill_due_date"       : null,
    "line_remarks"        : null
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
| 得意先・仕入先の存在・削除チェック | 都度 |
| 請求集計済み期間チェック | 売上・入金の登録・更新時 |
| 担当社員の存在・削除チェック | 売上・仕入・発注のみ |
| 参照受注・発注の存在チェック | 売上（`order_id` 指定時）・仕入（`purchase_order_id` 指定時）のみ |
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

## 8. 共通コンポーネント（bmcs_app.Shared）

| コンポーネント | 場所 | 説明 |
|---|---|---|
| `MasterSearchDialog` | `Views/MasterSearchDialog.xaml` | コード・名称の2列マスタ検索ダイアログ（全プロジェクト共用）。オプションで日付列も表示 |
| `SlipSearchDialog` | `Views/SlipSearchDialog.xaml` | 伝票番号・得意先名等での伝票検索ダイアログ（売上・受注・入金等で共用） |
| `FocusHelper` | `Helpers/FocusHelper.cs` | `MoveNextOnEnter` 添付プロパティ。TextBox 間のフォーカス移動を XAML から宣言的に設定 |

### MasterSearchDialog の操作

- キーワード入力でリアルタイム絞り込み
- `↓` キーでリスト移動
- Enter またはダブルクリックで確定
- Esc でキャンセル

### ILookupService（`bmcs_app.Core/Interfaces`）

各プロジェクトの `LookupService`（`Services/` 配下）が実装。ダイアログ起動・コード直接補完を抽象化。

```csharp
// App.xaml.cs でキャッシュデータを渡して初期化
var lookupService = new LookupService();
lookupService.Initialize(customers, employees, products);
var vm = new SalesMainViewModel(lookupService, saleRepo);
```

### Space キー + async DelegateCommand の注意点

Space キーで開くコマンドを **async DelegateCommand** にすると race condition が発生する（TextBox の Space 入力がコマンドより先に処理され、ダイアログが開かない）。

**解決策**: 起動時にデータをキャッシュし、コマンドを同期メソッドにする。

---

## 9. 印刷

| 帳票 | サイズ | タイミング |
|------|--------|-----------|
| 納品書 | A4縦 | 売上登録時（後から再発行も可） |
| 請求書 | A4縦 | 請求集計後（全得意先一括 or 任意1社） |

請求書印字項目: 前回請求額・入金額・売上額（標準税率/軽減税率）・消費税額（標準税率/軽減税率）・今回請求額・振込先口座。

### プリンタ設定

- 設定ファイル: `bin/Debug/bmcs_printer_settings.json`
- 納品書・請求書ごとに使用プリンタを指定
- ダイアログなしで直接印刷
- ランチャーのプリンタ設定画面から変更可能
