# bmcs_app.Sales（売上登録モジュール）

## 役割
売上伝票の新規登録・検索・更新・削除を行う単独 exe。

## 実装済み機能（完成）
- 伝票 CRUD（新規 / 検索 / 保存 / 削除）
- 伝票No. 自動採番（yyyyMMddnnn 形式）
- 前後ナビゲーション（`|◀` / `▶|`）
- 全コード欄: Space→ダイアログ検索 / Enter→コード補完
- 摘要 Enter → 明細 DataGrid 商品コードセルへフォーカス移動
- 消費税計算（明細単位 / 伝票単位）
- リアルタイム合計更新（Lines の CollectionChanged + PropertyChanged）
- 伝票ロック（invoiced_at / ar_aggregated_at が NULL でない場合、保存・削除不可）
- **印刷（F11）: インボイス制度準拠 A4 納品書。自社情報は `company_info` テーブルから取得**

---

## キーボードショートカット

| キー | 効果 |
|---|---|
| F2 | 明細行追加 |
| F3 | 新規伝票 |
| F8 | 伝票削除（確認ダイアログあり） |
| F10 | 保存 |
| F11 | 印刷 |
| Space（コード欄） | マスタ検索ダイアログを開く |
| Enter（コード欄） | コードで直接補完 → 次フィールドへ移動 |
| Enter（伝票No.欄） | 伝票を検索して読み込む |
| Enter（摘要欄） | 明細ゼロなら行追加 → 商品コードセルへ移動 |

---

## フォーカスフロー

```
[伝票No.] Enter → DBから伝票取得（SearchCommand）
[受注No.] Enter → [得意先コード]（FocusHelper.MoveNextOnEnter）
[得意先コード] Enter → 補完後 [担当者コード]（FocusHelper.MoveNextOnEnter）
[担当者コード] Enter → 補完後 [摘要]（FocusHelper.MoveNextOnEnter）
[摘要] Enter → 行追加（必要時）→ DataGrid 商品コードセル（FocusField イベント）
[商品コード] Enter → 商品補完 → [数量]セル（FocusHelper.MoveNextOnEnter）
```

- `h:FocusHelper.MoveNextOnEnter="True"`: `FocusScope` 内の次の `IsTabStop` 要素へ移動
- DataGrid readonly 列はすべて `DataGridCell.IsTabStop="False"` → Tab スキップ
- readonly 名称欄は `IsTabStop="False"` → Tab スキップ

---

## フォーカス移動イベント（ViewModel → View）

ViewModelが `event Action<string>? FocusField` を発火し、View のコードビハインドが処理する。
フォーカス移動は純 UI 挙動なのでコードビハインドに書いてよい（MVVM 例外）。

```csharp
// SalesMainView.xaml.cs
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

`FocusTargets.LineProductCode` = `"LineProductCode"`（定数）

---

## マスタ検索ダイアログ（MasterSearchDialog）

- `Sales/Views/MasterSearchDialog.xaml` — コード / 名称 の2列リスト
- `ILookupService` 経由で開く（`Sales/Services/LookupService.cs` が実装）
- **Owner = Application.Current.MainWindow を必ず設定**（設定しないとメイン画面の裏に隠れる）
- ダブルクリック確定: `DataGridRow` 上のクリックのみ受け付ける（ヘッダ・空白行は無視）

```csharp
// LookupService — すべてのダイアログで共通
var dlg = new MasterSearchDialog(title, items, initialKeyword)
    { Owner = System.Windows.Application.Current.MainWindow };
return dlg.ShowDialog() == true ? (T)dlg.SelectedSearchItem!.Source : null;
```

### Space キーでダイアログを開く際の注意

**非同期コマンドでは Space キー入力と race condition になる（TextBoxの Space 入力が優先されてコマンドが空振りする）。**
`OpenSlipLookupCommand` のように開く前にDBアクセスが必要な場合は、起動時キャッシュ（`_slipSummaries`）を使い、コマンドを同期にする。

```csharp
// NG: async DelegateCommand は Space キー時に race condition
OpenSlipLookupCommand = new DelegateCommand(async () => await OnOpenSlipLookupAsync());

// OK: 起動時にキャッシュ済みの _slipSummaries を使う
OpenSlipLookupCommand = new DelegateCommand(OnOpenSlipLookup); // 同期

private void OnOpenSlipLookup()
{
    var selected = _lookup.OpenSlipSearch(_slipSummaries, EditSaleNo);
    if (selected is not null) { EditSaleNo = selected; _ = OnSearchAsync(); }
}
```

---

## 税計算ロジック

### 税計算単位
得意先の `TaxCalcUnitId` で明細単位 / 伝票単位を判定（固定ID）：
- `1` = 明細単位: 各行で `Math.Floor(LineAmount × Rate)` を計算
- `2` = 伝票単位: 明細の LineTaxAmount は 0、フッターで税率ごとにグループ集計

```csharp
private static bool ResolveIsLineTaxCalc(int taxCalcUnitId) => taxCalcUnitId == 1;
```

### 税率の解決
`TaxRateType`（byte）と `SaleDate` で `TaxRatePeriod` マスタから適用税率を取得：
- `1` = 標準税率（PrimaryTaxRate: 通常10%）
- `2` = 軽減税率（SecondaryTaxRate: 通常8%）
- `3` = 特殊税率（TertiaryTaxRate）

### 伝票単位の外税・内税計算
明細の LineTaxAmount ではなく、税率ごとに合計してフッターで計算：

```csharp
// ExternalTaxTotal（外税）
return externalLines.GroupBy(l => l.AppliedTaxRate)
    .Sum(g => Math.Floor(g.Sum(l => l.LineAmount) * g.Key));

// InternalTaxTotal（内税）
return internalLines.GroupBy(l => l.AppliedTaxRate)
    .Sum(g => Math.Floor(g.Sum(l => l.LineAmount) * g.Key / (1 + g.Key)));
```

### リアルタイム集計更新
`Lines.CollectionChanged` で各行VMの `PropertyChanged` を購読し、
`LineAmount` / `LineTaxAmount` / `TaxType` 変更時に `RaiseTotalsChanged()` を呼ぶ。

---

## 税種別列（DataGridTemplateColumn）

`DataGridComboBoxColumn` は列が Visual Tree 外のため `RelativeSource` が効かない。
**必ず `DataGridTemplateColumn` + `CellEditingTemplate` の `ComboBox` を使う。**

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

---

## 伝票ロック

`SaleRepository.GetBySlipNoAsync` でSP読み込み後、別途 SQL で判定：

```csharp
SELECT COUNT(1) FROM sales
WHERE sale_no = @sale_no AND is_deleted = 0
  AND (invoiced_at IS NOT NULL OR ar_aggregated_at IS NOT NULL)
```

- **usp_sales_select の結果セットに invoiced_at / ar_aggregated_at は含まれない**
- SP結果の列名を間違えると reader 全体が例外となり明細も表示されなくなる

---

## 伝票番号自動採番

保存時に `EditSaleNo` が空の場合のみ生成（入力済みの場合はそのまま使用）：

```csharp
private string GenerateSlipNo(DateOnly date)
{
    var prefix = date.ToString("yyyyMMdd");
    var count  = _slipNos.Count(n => n.StartsWith(prefix));
    return $"{prefix}{count + 1:000}";
}
```

---

## App.xaml.cs 起動フロー

```
1. CustomerRepository / EmployeeRepository / ProductRepository を同期ロード
2. TaxRatePeriodRepository を同期ロード
3. LookupService.Initialize(customers, employees, products)
4. CompanyInfoRepository.GetAsync() を同期ロード
5. SalesMainViewModel(lookupService, saleRepo) を生成
6. vm.SetTaxRatePeriods(taxRatePeriods)
7. vm.SetCompanyInfo(companyInfo)
8. TaxTypeRepository を非同期ロード → vm.TaxTypes に追加（InitTaxTypesAsync）
9. SalesMainView を Show
```

税種別（TaxTypes）は非同期で追加されるが、DataGrid の ComboBox は遅延表示でも問題ない。
伝票読み込み時に `TaxTypes.FirstOrDefault(t => t.TaxTypeId == l.TaxTypeId)` で照合する。

---

## 印刷（インボイス制度準拠 A4 納品書）

### 実装クラス
- `Sales/Services/SalesPrintHelper.cs` — FixedDocument 構築・印刷実行
- `Sales/Services/SalePrintData.cs` — 印刷用データモデル（`SalePrintData` / `SalePrintLine` / `TaxRateBreakdown`）

### 印刷レイアウト（A4 縦）
```
タイトル「納　品　書」
────────────────────────────────
得意先名 御中           自社名
伝票No. / 担当者        住所 / TEL / FAX
摘要                   登録番号: T...
────────────────────────────────
行 | 商品コード | 商品名 | 数量 | 単価 | 金額 | 税種 | 税率 | 摘要
────────────────────────────────
※10%対象   税抜金額: xxx   消費税: xxx
※8%対象（軽減税率）...
税込合計: xxx
```

### インボイス制度 必須記載事項の対応
| 要件 | 対応箇所 |
|---|---|
| 発行事業者の名称・登録番号 | 右上ボックス（`company_info.invoice_no`） |
| 取引年月日 | タイトル下左側 |
| 取引内容 | 明細テーブル |
| 税率別 税抜金額・適用税率 | フッター税率別集計 |
| 税率別 消費税額 | フッター税率別集計 |
| 受取事業者の名称 | 左上「得意先名 御中」 |

### 自社情報
`company_info` テーブル（`SELECT TOP 1 ORDER BY company_info_id`）から取得。
`App.xaml.cs` 起動時に `CompanyInfoRepository.GetAsync()` でロードし、`vm.SetCompanyInfo()` で注入。

### 複数ページ対応
- 1 ページあたり約 25 行（A4 高さから自動計算）
- 続紙はコンパクトヘッダー（「納品書（続き）」＋伝票No.＋ページ番号）
- 税率別集計・合計は最終ページのみ表示

### `BuildTaxBreakdowns()` ロジック
`_isLineTaxCalc`（明細単位 / 伝票単位）に応じて税額を計算し、`TaxRateBreakdown` リストを返す。
- 外税（TaxTypeId=1）: 税率ごとにグループ → `LineAmount` 合計 + 税額計算
- 内税（TaxTypeId=2）: 同様に内税計算

---

## DB 操作

| 操作 | SP名 | 備考 |
|---|---|---|
| 伝票取得 | `usp_sales_select` | @sale_no のみ |
| 保存（新規/更新）| `usp_sales_upsert` | @lines に JSON 配列 |
| 削除 | `usp_sales_delete` | 論理削除（is_deleted=1） |
| 一覧取得 | 直接 SQL（SELECT GROUP BY sale_no） | SP なし |

`@lines` JSON フィールド一覧:
`line_no`, `product_id`, `product_code`, `product_name`, `quantity`, `unit_price`,
`tax_type_id`, `tax_rate_type`, `applied_tax_rate`, `line_tax_amount`, `slip_tax_amount`, `line_remarks`

---

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない（フォーカス移動・DataContext 購読のみ許容）
