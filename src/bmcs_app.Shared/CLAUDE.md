# bmcs_app.Shared（共通 WPF コンポーネント）

## 役割
全 exe プロジェクトで共通利用する WPF コンポーネントを集約するライブラリ。
UI に依存するため `bmcs_app.Core` には置けないものを管理する。

## 参照方針
- 新規プロジェクト（仕入・支払等）は `bmcs_app.Shared` を ProjectReference に追加するだけで使用可能
- `bmcs_app.Core` / `bmcs_app.Infrastructure` への依存は持たない（UI コンポーネントに限定）

---

## Views/MasterSearchDialog

### 概要
コード欄 Space キー押下時に表示するマスタ検索ダイアログ。
得意先・担当者・商品・入金区分など全エンティティで共用。

### 使い方

```csharp
using bmcs_app.Shared.Views;

// SearchItem(Code, Name, Source) のリストを渡す
var items = _customers.Select(c =>
    new MasterSearchDialog.SearchItem(c.CustomerCode, c.CustomerName, c));
var dlg = new MasterSearchDialog("得意先検索", items, initialKeyword)
    { Owner = Application.Current.MainWindow };   // Owner 必須（裏に隠れる防止）

return dlg.ShowDialog() == true
    ? (Customer)dlg.SelectedSearchItem!.Source
    : null;
```

### SearchItem record
```csharp
public record SearchItem(string Code, string Name, object Source);
```
`Source` に元のドメインオブジェクトを格納し、確定後に型キャストして取得する。

### 操作
| 操作 | 効果 |
|---|---|
| キーワード入力 | リアルタイム絞り込み（Code・Name 部分一致） |
| ↓ キー | 検索ボックスからリストへフォーカス移動 |
| Enter（検索ボックス） | 先頭行を確定 |
| Enter（リスト行）| 選択行を確定 |
| ダブルクリック | 選択行を確定（DataGridRow 上のみ有効） |
| Esc | キャンセル |

### 注意: Owner を必ず設定すること
```csharp
// NG: Owner 未設定だとメイン画面の裏に隠れる
var dlg = new MasterSearchDialog(title, items);

// OK
var dlg = new MasterSearchDialog(title, items) { Owner = Application.Current.MainWindow };
```
