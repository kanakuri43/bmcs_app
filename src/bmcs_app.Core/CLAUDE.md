# bmcs_app.Core（共通モデル・インターフェース）

## 役割
- 全プロジェクト共通のドメインモデルを定義
- リポジトリ・サービスのインターフェースを定義

## ルール
- 外部ライブラリへの依存を持たない
- DB・UI に依存しない純粋な C# クラスのみ配置
- `Models/` にドメインモデル、`Interfaces/` にインターフェース

---

## Models

| クラス | 用途 |
|---|---|
| `Employee` | 社員マスタ |
| `Customer` | 得意先マスタ（`TaxCalcUnitId`: 1=明細単位 / 2=伝票単位、`PostalCode?` / `Address1?` / `Address2?` を持つ） |
| `Product` | 商品マスタ（`TaxTypeId`, `TaxRateType`, `CostPrice`） |
| `TaxTypeClassification` | 税種別（`TaxTypeId`: 1=外税 / 2=内税） |
| `TaxCalcUnitClassification` | 税計算単位区分（システム区分: 1=明細単位 / 2=伝票単位、固定） |
| `TaxFractionClassification` | 税端数区分（切捨・切上・四捨五入） |
| `TaxRatePeriod` | 税率期間マスタ（`StartDate`, `EndDate?`, `PrimaryTaxRate`, `SecondaryTaxRate`, `TertiaryTaxRate?`） |
| `SlipSummary` | 伝票一覧用サマリ（`SlipNo`, `SlipDate`, `CustomerName`） |
| `SaleSlip` | 売上伝票読み込みモデル（ヘッダ + `List<SaleLine>` + `IsLocked`。`CustomerPostalCode?` / `CustomerAddress1?` / `CustomerAddress2?` を持つ） |
| `SaleLine` | 売上明細読み込みモデル（`usp_sales_select` 結果1行） |
| `SaleLineInput` | 売上明細書き込み用レコード（`usp_sales_upsert` の @lines JSON に使用） |
| `ReceiptSlip` | 入金伝票読み込みモデル（ヘッダ + `List<ReceiptLine>` + `IsLocked`。`CustomerPostalCode?` / `CustomerAddress1?` / `CustomerAddress2?` を持つ） |
| `InvoiceHeader` | 請求ヘッダーモデル（`CustomerPostalCode?` / `CustomerAddress1?` / `CustomerAddress2?` を持つ。集計時点の住所を保持） |

## Interfaces

| インターフェース | 実装 |
|---|---|
| `IEmployeeRepository` | `EmployeeRepository` |
| `ICustomerRepository` | `CustomerRepository` |
| `IProductRepository` | `ProductRepository` |
| `ITaxRatePeriodRepository` | `TaxRatePeriodRepository` |
| `ISaleRepository` | `SaleRepository` |
| `ILookupService` | `LookupService`（bmcs_app.Sales） |

### IProductRepository メソッド
```csharp
Task<IEnumerable<Product>> GetAllAsync();
Task UpsertAsync(int? productId, string code, string name, int taxTypeId, byte taxRateType, decimal costPrice);
Task DeleteAsync(int productId);
```

### ISaleRepository メソッド
```csharp
Task<IEnumerable<SlipSummary>> GetSummariesAsync();
Task<SaleSlip?> GetBySlipNoAsync(string saleNo);
Task UpsertAsync(string saleNo, DateOnly saleDate, int customerId,
    int? orderId, string? orderNo, int employeeId,
    string? slipRemarks, IEnumerable<SaleLineInput> lines);
Task DeleteAsync(string saleNo);
```

### ILookupService メソッド
```csharp
void Initialize(IEnumerable<Customer>, IEnumerable<Employee>, IEnumerable<Product>);
Customer?  OpenCustomerSearch(string initialKeyword = "");
Employee?  OpenEmployeeSearch(string initialKeyword = "");
Product?   OpenProductSearch(string initialKeyword = "");
string?    OpenSlipSearch(IEnumerable<SlipSummary> slips, string initialKeyword = "");
Customer?  FindCustomerByCode(string code);
Employee?  FindEmployeeByCode(string code);
Product?   FindProductByCode(string code);
```
