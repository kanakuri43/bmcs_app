# ソリューション全体構成

bmcs_app/
├── .git/
├── .gitignore
├── CLAUDE.md                          // ソリューション全体
├── bmcs_app.sln
│
├── docs/
│   ├── design_document.md
│   ├── overall_structure.md           // ← このファイル
│   └── references/
│       └── database_definition.sql    // DB スキーマ参照用（ライブDBとの差分に注意）
│
├── scripts/                           // DBスキーマ変更SQL
│   ├── alter_cost_price.sql
│   ├── alter_customers_address.sql
│   ├── alter_invoice_headers_address.sql
│   └── alter_journal_address.sql
│
├── bin/
│   └── Debug/                         // 全プロジェクト共通ビルド出力先（Directory.Build.props）
│       ├── bmcs_app.exe               // ランチャー
│       ├── bmcs_app.Master.exe
│       ├── bmcs_app.Order.exe
│       ├── bmcs_app.Sales.exe
│       ├── bmcs_app.Receipt.exe
│       ├── bmcs_app.Closing.exe
│       ├── bmcs_app.Search.exe
│       ├── bmcs_app.PurchaseOrder.exe
│       ├── bmcs_app.Purchase.exe
│       ├── bmcs_app.Payment.exe
│       ├── bmcs_app.PurchaseSearch.exe
│       ├── bmcs_config.json           // DB接続文字列
│       └── bmcs_printer_settings.json // プリンタ設定
│
└── src/
    │
    ├── Directory.Build.props          // 全プロジェクトの出力先を bin/Debug/ に統一
    │
    ├── bmcs_app/                      // ランチャー（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Views/
    │   │   ├── MainWindow.xaml        // メインメニュー（売掛・買掛・マスタ・設定）
    │   │   └── CompanyInfoSettingsWindow.xaml
    │   └── ViewModels/
    │       ├── MainWindowViewModel.cs
    │       └── CompanyInfoSettingsViewModel.cs
    │
    ├── bmcs_app.Core/                 // 共通モデル・インターフェース（ライブラリ）
    │   ├── Models/
    │   │   ├── Customer.cs / Supplier.cs / Employee.cs / Product.cs
    │   │   ├── TaxRatePeriod.cs / PaymentMethod.cs
    │   │   ├── TaxTypeClassification.cs / TaxFractionClassification.cs / TaxCalcUnitClassification.cs
    │   │   ├── OrderSlip.cs / OrderLine.cs / OrderLineInput.cs
    │   │   ├── SaleSlip.cs / SaleLine.cs / SaleLineInput.cs
    │   │   ├── ReceiptSlip.cs / ReceiptLine.cs / ReceiptLineInput.cs
    │   │   ├── PurchaseOrderSlip.cs / PurchaseOrderLine.cs / PurchaseOrderLineInput.cs
    │   │   ├── PurchaseSlip.cs / PurchaseLine.cs / PurchaseLineInput.cs
    │   │   ├── PaymentSlip.cs / PaymentLine.cs / PaymentLineInput.cs
    │   │   ├── InvoiceHeader.cs / InvoiceHistorySummary.cs / ArHistorySummary.cs
    │   │   ├── InvoiceSlipDetail.cs / InvoiceReceiptDetail.cs / InvoiceTaxGroup.cs
    │   │   ├── SlipSummary.cs / SearchResultItem.cs
    │   ├── Interfaces/
    │   │   ├── ICustomerRepository.cs / ISupplierRepository.cs / IEmployeeRepository.cs
    │   │   ├── IProductRepository.cs / IPaymentMethodRepository.cs / ITaxRatePeriodRepository.cs
    │   │   ├── IOrderRepository.cs / ISaleRepository.cs / IReceiptRepository.cs
    │   │   ├── IPurchaseOrderRepository.cs / IPurchaseRepository.cs / IPaymentRepository.cs
    │   │   ├── IClosingRepository.cs / ISearchRepository.cs / IPurchaseSearchRepository.cs
    │   │   └── ILookupService.cs
    │   └── Services/
    │       └── TaxCalculator.cs       // 税計算共通ロジック（static、副作用なし）
    │
    ├── bmcs_app.Infrastructure/       // DB接続・リポジトリ（ライブラリ）
    │   ├── CLAUDE.md                  // DB接続情報はここに記載
    │   ├── AppConfig.cs               // 接続文字列（bmcs_config.json を読み込み）
    │   ├── Repositories/
    │   │   ├── CustomerRepository.cs / SupplierRepository.cs / EmployeeRepository.cs
    │   │   ├── ProductRepository.cs / PaymentMethodRepository.cs / TaxRatePeriodRepository.cs
    │   │   ├── TaxTypeRepository.cs
    │   │   ├── OrderRepository.cs / SaleRepository.cs / ReceiptRepository.cs
    │   │   ├── PurchaseOrderRepository.cs / PurchaseRepository.cs / PaymentRepository.cs
    │   │   ├── ClosingRepository.cs / CompanyInfoRepository.cs
    │   │   ├── SearchRepository.cs / PurchaseSearchRepository.cs
    │   └── StoredProcedures/          // SP定義SQLファイル（参照・適用用）
    │       ├── usp_invoice_closing.sql / usp_invoice_closing_cancel.sql
    │       ├── usp_ar_closing.sql / usp_ar_closing_cancel.sql
    │       ├── usp_suppliers_upsert.sql / usp_suppliers_delete.sql
    │       ├── usp_purchase_orders_*.sql
    │       ├── usp_purchases_*.sql
    │       └── usp_payments_*.sql
    │
    ├── bmcs_app.Shared/               // 共通WPFコンポーネント（WPFライブラリ）
    │   ├── Views/
    │   │   ├── MasterSearchDialog.xaml    // コード・名称マスタ検索ダイアログ
    │   │   └── SlipSearchDialog.xaml      // 伝票検索ダイアログ
    │   └── Helpers/
    │       └── FocusHelper.cs             // MoveNextOnEnter 添付プロパティ
    │
    ├── bmcs_app.Master/               // マスタ保守（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Views/
    │   │   ├── EmployeeMaintView.xaml
    │   │   ├── CustomerMaintView.xaml
    │   │   ├── SupplierMaintView.xaml
    │   │   ├── ProductMaintView.xaml
    │   │   └── ...（区分マスタ各画面）
    │   └── ViewModels/
    │       └── ...
    │
    ├── bmcs_app.Order/                // 受注登録（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Services/LookupService.cs
    │   ├── Views/
    │   │   ├── OrderMainView.xaml
    │   │   └── UserControls/OrderLineControl.xaml
    │   └── ViewModels/
    │       ├── OrderMainViewModel.cs
    │       └── OrderLineViewModel.cs
    │
    ├── bmcs_app.Sales/                // 売上登録（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Services/LookupService.cs
    │   ├── Views/
    │   │   ├── SalesMainView.xaml
    │   │   └── UserControls/SaleLineControl.xaml
    │   └── ViewModels/
    │       ├── SalesMainViewModel.cs
    │       └── SaleLineViewModel.cs
    │
    ├── bmcs_app.Receipt/              // 入金登録（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Services/LookupService.cs
    │   ├── Views/
    │   │   ├── ReceiptMainView.xaml
    │   │   └── UserControls/ReceiptLineControl.xaml
    │   └── ViewModels/
    │       ├── ReceiptMainViewModel.cs
    │       └── ReceiptLineViewModel.cs
    │
    ├── bmcs_app.Closing/              // 請求集計・売掛金集計（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Services/
    │   ├── Views/
    │   └── ViewModels/
    │
    ├── bmcs_app.Search/               // 伝票横断検索（売掛＋買掛）（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Views/SearchMainView.xaml
    │   └── ViewModels/SearchMainViewModel.cs
    │
    ├── bmcs_app.PurchaseOrder/        // 発注登録（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Services/LookupService.cs
    │   ├── Views/
    │   │   ├── PurchaseOrderMainView.xaml
    │   │   └── UserControls/PurchaseOrderLineControl.xaml
    │   └── ViewModels/
    │       ├── PurchaseOrderMainViewModel.cs
    │       └── PurchaseOrderLineViewModel.cs
    │
    ├── bmcs_app.Purchase/             // 仕入登録（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Services/LookupService.cs
    │   ├── Views/
    │   │   ├── PurchaseMainView.xaml
    │   │   └── UserControls/PurchaseLineControl.xaml
    │   └── ViewModels/
    │       ├── PurchaseMainViewModel.cs
    │       └── PurchaseLineViewModel.cs
    │
    ├── bmcs_app.Payment/              // 支払登録（WinExe）
    │   ├── App.xaml / App.xaml.cs
    │   ├── Services/LookupService.cs
    │   ├── Views/
    │   │   ├── PaymentMainView.xaml
    │   │   └── UserControls/PaymentLineControl.xaml
    │   └── ViewModels/
    │       ├── PaymentMainViewModel.cs
    │       └── PaymentLineViewModel.cs
    │
    └── bmcs_app.PurchaseSearch/       // 仕入横断検索（発注・仕入・支払）（WinExe）
        ├── App.xaml / App.xaml.cs
        ├── Views/PurchaseSearchMainView.xaml
        └── ViewModels/PurchaseSearchMainViewModel.cs
