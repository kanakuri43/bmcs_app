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

## 伝票入力画面パターン（bmcs_app.Sales を基準）

### レイアウト構成
```
[ToolBar: 新規(F3) | 伝票No.検索(Space/Enter) | 前後ナビ | 登録件数]
┌────────────────────────────────────────────────────────────┐
│ ヘッダー                                                    │
│  売上日付 [DatePicker]  伝票No. [TextBox]  受注No. [TextBox] │
│  得意先   [コード TextBox] [名称 TextBox(readonly)]          │
│  担当者   [コード TextBox] [名称 TextBox(readonly)]          │
│  摘要     [TextBox]                                         │
├────────────────────────────────────────────────────────────┤
│ 明細 DataGrid（編集可）                                      │
│  行 | 商品コード | 商品名 | 数量 | 単価 | 金額 | 税種 | 税率 | 税額 | 行摘要 │
│  [行追加(F2)] [行削除]                                       │
├────────────────────────────────────────────────────────────┤
│ 《税抜金額》[値]  《消費税計》[値]   (外税)[値]  合計[値]  (内税)[値] │
│                                      [削除(F8)] [保存(F10)] │
└────────────────────────────────────────────────────────────┘
[StatusBar]
```

### キーボードショートカット
- F2: 行追加
- F3: 新規
- F8: 伝票削除
- F10: 保存
- Space（コード欄）: マスタ検索ダイアログを開く
- Enter（コード欄）: コードで直接補完 → 次フィールドへ自動フォーカス

### マスタ参照フィールドパターン（コード + 名称）
ComboBox は使わず、コード入力欄 + 名称表示欄の2欄構成にする。

```xml
<TextBox x:Name="CustomerCodeBox"
         Text="{Binding EditCustomerCode, UpdateSourceTrigger=PropertyChanged}"
         mah:TextBoxHelper.Watermark="コード"
         mah:TextBoxHelper.ClearTextButton="True">
    <TextBox.InputBindings>
        <KeyBinding Key="Space"  Command="{Binding OpenCustomerLookupCommand}" />
        <KeyBinding Key="Return" Command="{Binding LookupCustomerByCodeCommand}" />
    </TextBox.InputBindings>
</TextBox>
<TextBox Text="{Binding EditCustomerName, Mode=OneWay}"
         IsReadOnly="True"
         Foreground="{DynamicResource MahApps.Brushes.Gray3}" />
```

### MasterSearchDialog（共通マスタ検索ダイアログ）
- `Sales/Views/MasterSearchDialog.xaml` — コード / 名称の2列リスト
- `ILookupService`（Core/Interfaces）経由でダイアログを開く
- `LookupService`（Sales/Services）が実装を担う
- 操作: キーワード入力でリアルタイム絞り込み / `↓` でリスト移動 / Enter or ダブルクリックで確定 / Esc でキャンセル
- 受注・入金でも同じ `MasterSearchDialog` と `LookupService` を再利用する

```csharp
// LookupService のエンティティ登録（App.xaml.cs）
var lookupService = new LookupService();
lookupService.Initialize(customers, employees, products);
var vm = new SalesMainViewModel(lookupService, saleRepo);
```

### フォーカス移動パターン（コード確定後）
ViewModel が `event Action<string>? FocusField` を発火 → View のコードビハインドがフォーカスを移動。
フォーカス移動は純 UI 挙動なのでコードビハインドに書いてよい。

**2通りの方法を使い分ける:**

1. **`h:FocusHelper.MoveNextOnEnter="True"`**（TextBox 間の単純な移動）
   - `AddHandler(KeyDownEvent, handler, handledEventsToo: true)` で登録（KeyBinding 後も発火）
   - readonly 名称欄は `IsTabStop="False"`、DataGrid readonly 列は `DataGridCell.IsTabStop=False` でスキップ

2. **`FocusField` イベント**（DataGrid セルへの移動など特殊なケース）

```csharp
// View.xaml.cs
private void OnDataContextChanged(...)
{
    if (e.NewValue is SalesMainViewModel vm)
        vm.FocusField += OnFocusField;
}

private void OnFocusField(string target)
{
    if (target != SalesMainViewModel.FocusTargets.LineProductCode) return;
    Dispatcher.BeginInvoke(() =>
    {
        var row = LinesGrid.Items[0];
        LinesGrid.SelectedItem = row;
        LinesGrid.ScrollIntoView(row);
        LinesGrid.CurrentCell = new DataGridCellInfo(row, LinesGrid.Columns[1]); // 商品コード列
        LinesGrid.BeginEdit();
    }, DispatcherPriority.Input);
}
```

フォーカスフロー（売上登録）:
```
[伝票No.] Enter   → DBから伝票取得（SearchCommand）
[受注No.] Enter   → [得意先コード]（FocusHelper.MoveNextOnEnter）
[得意先コード] 確定 → [担当者コード]（FocusHelper.MoveNextOnEnter）
[担当者コード] 確定 → [摘要]（FocusHelper.MoveNextOnEnter）
[摘要] Enter      → 行追加（必要時）→ DataGrid 商品コードセル（FocusField イベント）
```

### Space キーとダイアログの注意点
Space キーで開くコマンドを **async DelegateCommand** にすると race condition が発生し、TextBox の Space 入力がコマンドより先に処理されてダイアログが開かない。

**解決策**: 起動時にデータをキャッシュし、コマンドを同期メソッドにする。

```csharp
// NG: async DelegateCommand は Space キー時に race condition
OpenSlipLookupCommand = new DelegateCommand(async () => await OnOpenSlipLookupAsync());

// OK: 起動時キャッシュ済みデータを使い同期で開く
OpenSlipLookupCommand = new DelegateCommand(OnOpenSlipLookup); // 同期

private void OnOpenSlipLookup()
{
    var selected = _lookup.OpenSlipSearch(_slipSummaries, EditSaleNo); // _slipSummaries はキャッシュ済み
    if (selected is not null) { EditSaleNo = selected; _ = OnSearchAsync(); }
}
```

### DataGrid 税種別列（RelativeSource 問題）
`DataGridComboBoxColumn` は Visual Tree 外のため `RelativeSource` が効かない。
**必ず `DataGridTemplateColumn` + `CellEditingTemplate` を使う。**

```xml
<DataGridTemplateColumn Header="税種別" Width="80">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate><TextBlock Text="{Binding TaxType.TaxTypeName}" /></DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
    <DataGridTemplateColumn.CellEditingTemplate>
        <DataTemplate>
            <ComboBox ItemsSource="{Binding DataContext.TaxTypes,
                                   RelativeSource={RelativeSource AncestorType=Window}}"
                      SelectedItem="{Binding TaxType}"
                      DisplayMemberPath="TaxTypeName" />
        </DataTemplate>
    </DataGridTemplateColumn.CellEditingTemplate>
</DataGridTemplateColumn>
```

### ViewModel 構造（伝票画面）
```csharp
public class SalesMainViewModel : BindableBase
{
    private readonly ILookupService  _lookup;
    private readonly ISaleRepository _saleRepo;

    // ヘッダー: コード + 名称 + 内部ID を対で持つ
    public string EditCustomerCode { get; set; }
    public string EditCustomerName { get; set; }  // readonly 表示用
    private int? _editCustomerId;                 // 保存時に使用

    // 明細: SaleLineViewModel の ObservableCollection
    public ObservableCollection<SaleLineViewModel> Lines { get; } = new();

    // 集計: Lines を集計した計算プロパティ
    public decimal TaxExcludedTotal => Lines.Sum(l => l.LineAmount);
    // ...

    // フォーカス移動イベント（View のコードビハインドがハンドル）
    public event Action<string>? FocusField;

    // フォーカスターゲット定数
    public static class FocusTargets { ... }
}
```

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

## 伝票ロック方針

### 対象テーブル
`sales` / `receipts` の両テーブルに以下のカラムを持つ：

| カラム | 型 | 意味 |
|---|---|---|
| `invoiced_at` | `datetime NULL` | 請求集計に取り込まれた日時 |
| `ar_aggregated_at` | `datetime NULL` | 売掛金集計に取り込まれた日時 |

### ロック判定
```sql
-- どちらか一方でも集計済みなら編集・削除不可
(invoiced_at IS NOT NULL OR ar_aggregated_at IS NOT NULL)
```

- SP（usp_sales_upsert / usp_sales_delete / usp_receipts_upsert / usp_receipts_delete）でチェック済み
- ロック解除は集計処理の再実行で当該カラムをNULLに戻す

### 集計処理でのセット
集計日時はシステム日付ではなく、締め処理が対象とする日付をパラメータで渡す
（当日より前の日付で締め処理を行うケースがあるため）。

```sql
-- @process_date: 締め処理の対象日付（任意・GETDATE()は使わない）
UPDATE sales SET invoiced_at = @process_date
WHERE customer_id = @customer_id AND sale_date <= @closing_date AND invoiced_at IS NULL;
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
