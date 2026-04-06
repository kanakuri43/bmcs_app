# bmcs_app.Search（伝票検索モジュール）

## 役割
売上・入金伝票をまたいで横断検索する単独 exe。
検索結果をダブルクリックすると該当登録画面を起動して伝票を表示する。

## 実装済み機能（完成）
- 売上・入金・両方の種別絞り込み（RadioButton）
- 日付範囲フィルタ（デフォルト: 当月1日〜今日）
- キーワード検索（得意先名 / 商品名 / 摘要）
- 得意先コード絞り込み
- 集計状態フィルタ（全件 / 未処理のみ / 処理済のみ）
- 結果ダブルクリック → 売上登録 / 入金登録画面を `--slip-no=` 引数付きで起動

---

## 画面レイアウト

```
┌────────────────────────────────────────────────────────────┐
│ 種別: (◎)両方 ( )売上 ( )入金   日付: [from]～[to]  集計状態: [ComboBox] │
│ キーワード: [TextBox]  得意先コード: [TextBox]  [検索(F5)]              │
├────────────────────────────────────────────────────────────┤
│ DataGrid: 種別 / 日付 / 伝票No. / 得意先名 / 金額（税抜）/ 状態 / 摘要 │
│ （行ダブルクリック → 対象画面起動）                                    │
└────────────────────────────────────────────────────────────┘
[StatusBar: 件数]
```

- Height="720" Width="1100"
- F5 / Enter（各テキストボックス内）: 検索実行

---

## キーボードショートカット

| キー | 効果 |
|---|---|
| F5 | 検索実行 |
| Enter（TextBox 内） | 検索実行 |
| ダブルクリック（行） | 対象登録画面を起動 |

---

## 集計状態の表示値

### 売上
| 状態 | 条件 |
|---|---|
| 請求・売掛済 | invoiced_at IS NOT NULL AND ar_aggregated_at IS NOT NULL |
| 請求済 | invoiced_at IS NOT NULL |
| 売掛済 | ar_aggregated_at IS NOT NULL |
| 未処理 | 両方 NULL |

### 入金
| 状態 | 条件 |
|---|---|
| 請求・集計済 | invoiced_at IS NOT NULL AND ar_aggregated_at IS NOT NULL |
| 請求済 | invoiced_at IS NOT NULL |
| 集計済 | ar_aggregated_at IS NOT NULL |
| 未処理 | 両方 NULL |

集計状態フィルタ「未処理のみ」= 両方 NULL、「処理済のみ」= どちらか一方でも NOT NULL。

---

## ダブルクリック起動の仕組み

**Search 側（OpenSlipCommand）**
- `SlipType == "売上"` → `bmcs_app.Sales.exe --slip-no=<伝票No>`
- `SlipType == "入金"` → `bmcs_app.Receipt.exe --slip-no=<伝票No>`
- `AppDomain.CurrentDomain.BaseDirectory` 基準でパス解決
- exe が見つからない場合は MessageBox でエラー表示

**View のダブルクリック検出（SearchMainView.xaml.cs）**
```csharp
private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
{
    // VisualTree を上に辿って DataGridRow を確認（ヘッダ・空白行クリックを除外）
    var dep = e.OriginalSource as DependencyObject;
    while (dep is not null and not DataGridRow)
        dep = VisualTreeHelper.GetParent(dep);
    if (dep is not DataGridRow) return;
    if (DataContext is SearchMainViewModel vm && vm.OpenSlipCommand.CanExecute())
        vm.OpenSlipCommand.Execute();
}
```
`ItemContainerStyle + EventSetter` は MahApps.Metro スタイルと競合するため使用しない。
DataGrid の `MouseDoubleClick` イベントを直接処理する。

**Sales / Receipt 側（起動時引数受け取り）**
```csharp
// App.xaml.cs
var initialSlipNo = e.Args
    .Select(a => a.StartsWith("--slip-no=") ? a["--slip-no=".Length..] : null)
    .FirstOrDefault(v => v is not null);
if (initialSlipNo is not null)
    _ = vm.LoadInitialSlipAsync(initialSlipNo);
```
`LoadInitialSlipAsync` は ViewModel の public メソッド。
EditSaleNo（または EditReceiptNo）にセットして OnSearchAsync を呼ぶ。

---

## SearchRepository 実装メモ

- `BuildSalesQuery` / `BuildReceiptsQuery` で動的 WHERE を構築し UNION ALL で結合
- キーワードは C# 側で `$"%{keyword}%"` にして `@keyword` パラメータで渡す
- パラメータ（@date_from / @date_to / @keyword / @customer_code）は UNION 全体で1セット
- ORDER BY: `slip_date DESC, slip_no`

---

## App.xaml.cs 起動フロー

```
1. SearchRepository を生成
2. SearchMainViewModel(searchRepo) を生成
3. SearchMainView を Show
```

---

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（直接 SQL）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない（ダブルクリック検出のみ許容）
