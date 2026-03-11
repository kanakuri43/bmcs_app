# ソリューション全体構成

bmcs_app/
├── .git/
├── .gitignore
├── CLAUDE.md                          // ソリューション全体
├── bmcs_app.sln
│
├── src/
│   │
│   ├── bmcs_app/                      // WPF シェル・起動
│   │   ├── CLAUDE.md
│   │   ├── bmcs_app.csproj
│   │   ├── App.xaml
│   │   ├── Views/
│   │   │   └── MainWindow.xaml
│   │   └── ViewModels/
│   │       └── MainWindowViewModel.cs
│   │
│   ├── bmcs_app.Core/                 // 共通モデル・インターフェース
│   │   ├── CLAUDE.md
│   │   ├── bmcs_app.Core.csproj
│   │   ├── Models/
│   │   └── Interfaces/
│   │
│   ├── bmcs_app.Infrastructure/       // DB・EF Core・SP
│   │   ├── CLAUDE.md                  // DB接続情報もここに記載
│   │   ├── bmcs_app.Infrastructure.csproj
│   │   ├── Data/
│   │   │   └── AppDbContext.cs
│   │   ├── Repositories/
│   │   └── StoredProcedures/
│   │
│   ├── bmcs_app.Order/                // 受注登録
│   │   ├── CLAUDE.md
│   │   ├── bmcs_app.Order.csproj
│   │   ├── OrderModule.cs             // Prism IModule
│   │   ├── Views/
│   │   └── ViewModels/
│   │
│   ├── bmcs_app.Sales/                // 売上登録
│   │   ├── CLAUDE.md
│   │   ├── bmcs_app.Sales.csproj
│   │   ├── SalesModule.cs             // Prism IModule
│   │   ├── Views/
│   │   └── ViewModels/
│   │
│   ├── bmcs_app.Payment/              // 入金登録
│   │   ├── CLAUDE.md
│   │   ├── bmcs_app.Payment.csproj
│   │   ├── PaymentModule.cs
│   │   ├── Views/
│   │   └── ViewModels/
│   │
│   └── bmcs_app.Closing/              // 請求集計・締め処理
│       ├── CLAUDE.md
│       ├── bmcs_app.Closing.csproj
│       ├── ClosingModule.cs
│       ├── Views/
│       └── ViewModels/
│
