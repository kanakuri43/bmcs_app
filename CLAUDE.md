# bmcs_app ソリューション

## 概要
卸売B2B向け販売管理システム（中小企業向け）

## 技術スタック
- SQL Server 2022+
- C# / WPF / MahApps.Metro
- Prism.Wpf（MVVM: BindableBase / DelegateCommand）
- Entity Framework Core / rowversion楽観的排他
- Microsoft.Data.SqlClient（SP呼び出し）
- JSON over TVP（ジャーナル系ストアドプロシージャ）

## プロジェクト構成

| プロジェクト | 種別 | 役割 |
|---|---|---|
| bmcs_app | WinExe | ランチャーメニュー |
| bmcs_app.Core | ライブラリ | 共通モデル・インターフェース |
| bmcs_app.Infrastructure | ライブラリ | DB接続・リポジトリ |
| bmcs_app.Master | WinExe | マスタ保守（社員・得意先・商品等） |
| bmcs_app.Order | WinExe | 受注登録 |
| bmcs_app.Sales | WinExe | 売上登録 |
| bmcs_app.Payment | WinExe | 入金登録 |
| bmcs_app.Closing | WinExe | 請求集計・締め処理 |

## アーキテクチャ方針

### SDI（Single Document Interface）
- 各プロジェクトが独立した exe
- ランチャー（bmcs_app）が `Process.Start(exeName)` で各 exe を起動
- Prism の RegionManager / IModule は使用しない

### ビルド出力先
- `src/Directory.Build.props` により全プロジェクトの出力先を統一
- 出力先: `bin/Debug/`（ソリューションルートの `bin/Debug/`）
- **ビルドは必ず `dotnet build bmcs_app.sln -c Debug` で行う**
  - 個別プロジェクトビルドだと `bin/`（Debug なし）に出力されランチャーが見つけられない

### DI・依存注入
- Prism の DI コンテナは使用しない
- 各 exe の `App.xaml.cs` `OnStartup` でリポジトリをインスタンス化し、ViewModel コンストラクタに渡す
  ```csharp
  protected override void OnStartup(StartupEventArgs e)
  {
      var repo = new EmployeeRepository();
      var vm   = new EmployeeMaintViewModel(repo);
      var win  = new EmployeeMaintView { DataContext = vm };
      win.Show();
  }
  ```

## マスタメンテ画面パターン（bmcs_app.Master を基準）

### レイアウト構成
```
[ToolBar: 新規(F3)]
┌──────────────────────┬────────────────────────────┐
│ [キーワード検索 TextBox] │ 編集フォーム                 │
│ DataGrid（一覧）       │                            │
│                      │  [削除(F8)]  [保存(F10) ▶]  │
│ [ページセレクタ]       │                            │
└──────────────────────┴────────────────────────────┘
[StatusBar: 件数・状態メッセージ]
```

- ToolBar には **新規(F3) のみ**
- 保存(F10)・削除(F8) は **編集フォーム右下**にボタン配置
- 保存ボタンは `MahApps.Styles.Button.Square.Accent` スタイルで強調

### キーボードショートカット
- F3: 新規
- F8: 削除
- F10: 保存
- Enter（検索ボックス内）: 絞り込み実行

### 検索・ページネーション
- 一覧上部にキーワード検索（Enter で適用）
- 1ページ100件、リスト下部にページセレクタ
- データフロー: `_allItems` → `_filteredItems`（検索）→ `Items`（ページスライス）
- 検索適用時はページ1にリセット
- ページボタンラベル: `|◀` `◀` `▶` `▶|`（Unicode 記号は使わない・豆腐になる）

### DataGrid 設定
```xml
<DataGrid ItemsSource="{Binding Items}"
          SelectedItem="{Binding SelectedItem, Mode=TwoWay}"
          AutoGenerateColumns="False"
          IsReadOnly="True"
          SelectionMode="Single"
          CanUserAddRows="False"
          CanUserDeleteRows="False"
          GridLinesVisibility="Horizontal"
          HeadersVisibility="Column" />
```

### ViewModel 基本構造
```csharp
public class XxxMaintViewModel : BindableBase
{
    private readonly IXxxRepository _repo;
    private List<Xxx> _allItems      = new();
    private List<Xxx> _filteredItems = new();
    public ObservableCollection<Xxx> Items { get; } = new();

    // 検索
    public string SearchKeyword { get; set; }
    public DelegateCommand SearchCommand { get; }  // → CurrentPage=1; ApplyFilter()

    // ページネーション
    private const int PageSize = 100;
    public int CurrentPage { get; }
    public int TotalPages  => _filteredItems.Count == 0 ? 1 : (int)Math.Ceiling(_filteredItems.Count / (double)PageSize);
    public string PageLabel  => $"{CurrentPage} / {TotalPages} ページ";
    public string RangeLabel => /* "1〜100 件 / 全 251 件" or "0 件" */;
    public DelegateCommand FirstPageCommand { get; }
    public DelegateCommand PrevPageCommand  { get; }
    public DelegateCommand NextPageCommand  { get; }
    public DelegateCommand LastPageCommand  { get; }
    // First/Prev: CanExecute = CurrentPage > 1
    // Next/Last:  CanExecute = CurrentPage < TotalPages
    // すべて .ObservesProperty(() => CurrentPage)

    // 編集
    private int? _editingId;
    public Xxx? SelectedItem { get; set; }  // セット時に LoadToForm() でフォームへ転写
    public string EditCode { get; set; }
    // ...

    public DelegateCommand NewCommand    { get; }
    public DelegateCommand SaveCommand   { get; }
    public DelegateCommand DeleteCommand { get; }  // CanExecute: SelectedItem is not null
    // DeleteCommand = new DelegateCommand(...).ObservesProperty(() => SelectedItem)

    public string StatusMessage { get; set; }
    // ページ切替後: StatusMessage = RangeLabel
    // 操作後:      StatusMessage = "登録しました" / "更新しました" / "削除しました" 等
}
```

### ViewModel ライフサイクル
1. コンストラクタ末尾で `_ = LoadAsync()` を呼ぶ（fire-and-forget）
2. `LoadAsync()`: リポジトリからデータ取得 → `_allItems` セット → `CurrentPage=1; ApplyFilter()`
3. 保存・削除後に `LoadAsync()` を再呼び出し（リストリフレッシュ）
4. `ApplyFilter()` → `RaisePropertyChanged(nameof(TotalPages))` → `ApplyPage()`
5. `ApplyPage()` → `Employees.Clear()` + `foreach Add` → `RaisePropertyChanged(PageLabel/RangeLabel)` → `StatusMessage = RangeLabel`

### View（MetroWindow）基本構造
```xml
<mah:MetroWindow ...
    WindowStartupLocation="CenterScreen"
    WindowTransitionsEnabled="False">
    <Window.InputBindings>
        <KeyBinding Key="F3"  Command="{Binding NewCommand}" />
        <KeyBinding Key="F8"  Command="{Binding DeleteCommand}" />
        <KeyBinding Key="F10" Command="{Binding SaveCommand}" />
    </Window.InputBindings>
    <!-- ToolBar / Grid（左:一覧+ページ / GridSplitter / 右:編集フォーム） / StatusBar -->
</mah:MetroWindow>
```
- `prism:ViewModelLocator.AutoWireViewModel` は使わない（App.xaml.cs で DataContext を明示セット）
- コードビハインドは `InitializeComponent()` のみ
- 全ウィンドウに `WindowTransitionsEnabled="False"`
- 左右分割は `Grid` + `GridSplitter`（Width=5, Background=MahApps.Brushes.Gray8）
- 編集フォームは `Border`（BorderThickness=1, BorderBrush=MahApps.Brushes.Gray8）で囲む

## Infrastructure パターン

### リポジトリ
- インターフェースを `bmcs_app.Core/Interfaces/` に定義
- 実装を `bmcs_app.Infrastructure/Repositories/` に配置
- 接続文字列はリポジトリの定数（`ConnectionString`）に記載
- SELECT は SP がない場合は直接クエリ可
- INSERT/UPDATE/DELETE は必ず SP 経由（`usp_{entity}_{operation}`）

```csharp
public async Task UpsertAsync(int? id, ...)
{
    await using var conn = new SqlConnection(ConnectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "usp_xxx_upsert";
    cmd.CommandType = CommandType.StoredProcedure;
    cmd.Parameters.AddWithValue("@xxx_id", (object?)id ?? DBNull.Value);
    // ...
    await cmd.ExecuteNonQueryAsync();
}
```

## 共通ルール
- インボイス制度対応（税率・登録番号管理）
- 非同期は async/await 統一
- DB操作（CRUD）はすべてStoredProcedure経由
- **DBスキーマはライブDBに直接クエリして確認する（コードに転記しない）**
- null許容（`Nullable enable`）
- コードビハインドにロジックを書かない（MVVM 徹底）
- 画面操作全般キーボード操作を基本とする

## DB接続情報
- 接続文字列は `bin/Debug/bmcs_config.json` で一元管理（ハードコード禁止）
- `AppConfig.ConnectionString`（`bmcs_app.Infrastructure/AppConfig.cs`）経由で参照
- 詳細は `src/bmcs_app.Infrastructure/CLAUDE.md` 参照

## 命名規則
- C#: Microsoft推奨
- DB: snake_case
- ストアドには `usp_` プレフィックス

## ショートカット
- `shortcuts/販売管理システム.lnk` → ランチャー（bmcs_app.exe）
- `shortcuts/マスタ保守.lnk` → 社員マスタ直接起動（bmcs_app.Master.exe）
- 対象: `bin/Debug/` 配下の各 exe
