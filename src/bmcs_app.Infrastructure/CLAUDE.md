# bmcs_app.Infrastructure

## Database
- Server: 172.16.6.11
- Database: bmcs_db
- UID: sa
- PWD: Sapassword1

## DB接続情報の管理
- 接続文字列は `bin/Debug/bmcs_config.json` で一元管理
- `AppConfig.ConnectionString`（静的プロパティ）経由で取得
- リポジトリに接続文字列を直接書かない

```json
{
  "connectionString": "Server=...;Database=bmcs_db;User Id=sa;Password=...;TrustServerCertificate=True;"
}
```

---

## 実装済みリポジトリ

| クラス | インターフェース | 備考 |
|---|---|---|
| `EmployeeRepository` | `IEmployeeRepository` | GetAllAsync |
| `CustomerRepository` | `ICustomerRepository` | GetAllAsync（TaxCalcUnitId 含む） |
| `ProductRepository` | `IProductRepository` | GetAllAsync |
| `TaxTypeRepository` | — | GetAllAsync（インターフェースなし） |
| `TaxRatePeriodRepository` | `ITaxRatePeriodRepository` | GetAllAsync |
| `SaleRepository` | `ISaleRepository` | GetSummariesAsync / GetBySlipNoAsync / UpsertAsync / DeleteAsync |
| `CompanyInfoRepository` | — | GetAsync（インターフェースなし）|

## CompanyInfoRepository

`company_info` テーブルから自社情報を取得する。インターフェースなし。

```sql
SELECT TOP 1 company_name, address, tel, fax, invoice_no
FROM company_info
ORDER BY company_info_id
```

戻り値: `CompanyInfo`（`bmcs_app.Infrastructure` 名前空間）

```csharp
public class CompanyInfo
{
    public string Name                  { get; set; }  // company_name
    public string Address               { get; set; }  // address
    public string Phone                 { get; set; }  // tel
    public string Fax                   { get; set; }  // fax
    public string InvoiceRegistrationNo { get; set; }  // invoice_no（T + 13桁）
}
```

テーブルスキーマ（`company_info`）:

| 列名 | 型 | NULL |
|---|---|---|
| company_info_id | int | NO |
| company_name | nvarchar(100) | NO |
| address | nvarchar(200) | YES |
| tel | nvarchar(20) | YES |
| fax | nvarchar(20) | YES |
| invoice_no | nvarchar(20) | YES |

---

## SaleRepository 実装メモ

### GetBySlipNoAsync
- `usp_sales_select`（@sale_no のみ）を呼び出す
- **IsLocked は SP の結果セットに含まれない** → 別途 SQL で判定：
  ```sql
  SELECT COUNT(1) FROM sales
  WHERE sale_no = @sale_no AND is_deleted = 0
    AND (invoiced_at IS NOT NULL OR ar_aggregated_at IS NOT NULL)
  ```
- SP 結果の列名を1つでも間違えると reader 全体が例外になり明細が表示されなくなる

### UpsertAsync
- `@lines` パラメータに `System.Text.Json.JsonSerializer` で JSON 配列を渡す
- JSON フィールド: `line_no`, `product_id`, `product_code`, `product_name`, `quantity`,
  `unit_price`, `tax_type_id`, `tax_rate_type`, `applied_tax_rate`, `line_tax_amount`,
  `slip_tax_amount`, `line_remarks`

---

## ルール
- SELECT は SP がない場合は直接クエリ可
- INSERT / UPDATE / DELETE は必ず SP 経由（`usp_{entity}_{operation}`）
- null 許容パラメータは `(object?)value ?? DBNull.Value` で渡す
