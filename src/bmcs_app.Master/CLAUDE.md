# bmcs_app.Master（マスタ保守モジュール）

## 役割
社員・得意先・商品・消費税率マスタの保守を行う単独 exe。
`--master=` 引数でどのマスタを開くか切り替える。

## 起動引数

| 引数 | 画面 |
|---|---|
| （なし） | 社員マスタ |
| `--master=employee` | 社員マスタ |
| `--master=customer` | 得意先マスタ |
| `--master=product` | 商品マスタ |
| `--master=taxrate` | 消費税率マスタ |

## 実装済みマスタ

| マスタ | ViewModel | View | SP |
|---|---|---|---|
| 社員 | `EmployeeMaintViewModel` | `EmployeeMaintView` | `usp_employees_upsert` / `usp_employees_delete` |
| 得意先 | `CustomerMaintViewModel` | `CustomerMaintView` | `usp_customers_upsert` / `usp_customers_delete` |
| 商品 | `ProductMaintViewModel` | `ProductMaintView` | `usp_products_upsert` / `usp_products_delete` |
| 消費税率 | `TaxRatePeriodMaintViewModel` | `TaxRatePeriodMaintView` | — |

### 得意先マスタ固有フィールド

| フィールド | 型 | 制約 |
|---|---|---|
| 得意先コード | nvarchar(20) | 必須 |
| 得意先名 | nvarchar(100) | 必須 |
| 締日 | tinyint | 1〜27 または 99（月末） |
| 税端数区分 | ComboBox | 必須 |
| 税計算単位区分 | ComboBox | 必須 |
| 担当者 | ComboBox（NULL 可） | 任意 |
| 郵便番号 | nvarchar(8) | 任意 |
| 住所1 | nvarchar(100) | 任意 |
| 住所2 | nvarchar(100) | 任意 |

郵便番号・住所は納品書・請求書の宛先欄に印字される。

---

## 画面共通パターン

詳細はルート `CLAUDE.md` の「マスタメンテ画面パターン」を参照。

- F3: 新規 / F8: 削除 / F10: 保存
- 左列: キーワード検索 + DataGrid + ページセレクタ（100件/ページ）
- 右列: 編集フォーム（Border で囲む）
- GridSplitter（Width=5）で左右分割

---

## 商品マスタ固有の設計

### フォームフィールド
| フィールド | 型 | 制約 |
|---|---|---|
| 商品コード | nvarchar(20) | 必須 |
| 商品名 | nvarchar(100) | 必須 |
| 税種別 | ComboBox（TaxTypeClassification） | 必須 |
| 税率区分 | ComboBox（1=標準/2=軽減/3=特殊） | 必須 |
| 原価 | decimal(18,2) | 任意（デフォルト 0） |

### TaxRateTypeOption（ViewModel 内ネスト record）
```csharp
public record TaxRateTypeOption(byte Value, string Label);
// { 1, "標準税率" }, { 2, "軽減税率" }, { 3, "特殊税率" }
```

### TaxTypes の非同期ロード（重要）
`TaxTypeRepository.GetAllAsync()` は ViewModel の `LoadAsync()` 内で `await` する。

**`OnStartup`（UI スレッド）で `GetAllAsync().GetAwaiter().GetResult()` は禁止。**
WPF の SynchronizationContext 上でブロック待機するとデッドロックし、ウィンドウが表示されない。

```csharp
// OK: ViewModel コンストラクタで fire-and-forget
_ = LoadAsync();

private async Task LoadAsync()
{
    var types = await _taxTypeRepo.GetAllAsync();   // UI スレッドをブロックしない
    foreach (var t in types) TaxTypes.Add(t);
    await LoadProductsAsync();
}

// NG: App.xaml.cs OnStartup（UI スレッド）でブロック待機
var taxTypes = new TaxTypeRepository().GetAllAsync().GetAwaiter().GetResult();  // デッドロック
```

### App.xaml.cs 起動フロー（商品マスタ）
```csharp
private static Window CreateProductWindow()
{
    var repo    = new ProductRepository();
    var taxRepo = new TaxTypeRepository();
    var vm      = new ProductMaintViewModel(repo, taxRepo);
    return new ProductMaintView { DataContext = vm };
}
```

---

## ルール
- コードビハインドは `InitializeComponent()` のみ（MVVM 徹底）
- 保存・削除後は `LoadProductsAsync()` でリストをリフレッシュ（TaxTypes の再ロード不要）
- 論理削除（`is_deleted = 1`）を SP が担う
