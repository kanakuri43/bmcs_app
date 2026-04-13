# bmcs_app.Purchase（仕入登録モジュール）

## 役割
仕入伝票の新規登録・検索・更新・削除を行う単独 exe。

## 実装状況
実装完了。bmcs_app.Sales のパターンを仕入側に差し替えて実装。

## ビジネスルール
- 仕入は CRUD 対応
- ロックカラム: `ap_closing_at`（買掛金集計済みなら編集・削除不可）
- Sales の `invoiced_at` / `ar_aggregated_at`（2カラム）に対し、仕入側は `ap_closing_at`（1カラム）のみ
- 発注No. を任意で紐付け可能（`purchase_order_id` / `purchase_order_no`）

## 売上側との対応

| 売上側 | 仕入側 |
|---|---|
| 売上 (sales) | 仕入 (purchases) |
| 得意先 (customers) | 仕入先 (suppliers) |
| 受注No. (EditOrderNo) | 発注No. (EditPurchaseOrderNo) |
| invoiced_at / ar_aggregated_at | ap_closing_at |
| InvoicedAtText / ArAggregatedAtText | ApClosingAtText |
| PrintCommand (F11) | なし |
| SalesMainViewModel | PurchaseMainViewModel |
| SaleLineViewModel | PurchaseLineViewModel |

## 主要コンポーネント

- `App.xaml.cs` — 起動フロー・DI（同期ロード）
- `Services/LookupService.cs` — 仕入先 / 社員 / 商品 / 発注伝票 検索（ローカル実装）
  - `SetPurchaseOrderData` / `OpenPurchaseOrderSearch` を追加（売上側の `SetSlipData` / `OpenOrderSearch` 相当）
- `ViewModels/PurchaseMainViewModel.cs` — `ApClosingAtText`（"yyyy/MM/dd" or "未集計"）
- `ViewModels/PurchaseLineViewModel.cs` — `MoveToQuantityRequested` イベント
- `Views/PurchaseMainView.xaml` — Title="仕入伝票"、F11 なし、集計状況は支払集計日付のみ
- `Views/PurchaseMainView.xaml.cs` — `FocusField` イベント購読
- `Views/PurchaseLineControl.xaml` / `.xaml.cs`

## DB 操作

| 操作 | SP名 |
|---|---|
| 伝票取得 | `usp_purchases_select` |
| 保存（新規/更新）| `usp_purchases_upsert` |
| 削除 | `usp_purchases_delete` |
| 一覧取得 | `usp_purchases_summaries_select` |

`@lines` JSON フィールド: `line_no`, `product_id`, `product_code`, `product_name`, `quantity`,
`unit_price`, `cost_price`, `tax_type_id`, `tax_rate_type`, `applied_tax_rate`, `line_tax_amount`,
`slip_tax_amount`, `line_remarks`

仕入先住所（`supplier_postal_code`, `supplier_address1`, `supplier_address2`）は
SP が `suppliers` テーブルから自動取得（Sales の customers 自動取得と同様）。

## ロック判定
`ap_closing_at IS NOT NULL` → 編集・削除不可。SP でもチェック済み。

## フォーカスフロー
```
[伝票No.] Enter → 伝票取得（SearchCommand）
[発注No.] Enter → 発注適用（LookupPurchaseOrderByNoCommand）→ [仕入先コード]（FocusHelper）
[仕入先コード] Enter → 補完後 [担当者コード]（FocusHelper.MoveNextOnEnter）
[担当者コード] Enter → 補完後 [摘要]（FocusHelper.MoveNextOnEnter）
[摘要] Enter → 行追加（必要時）→ 1行目[商品コード]（FocusField: LineProductCode）
[商品コード] Enter → 商品補完 → [数量]（MoveToQuantityRequested）
[行摘要] Enter → 行追加 → 新規行[商品コード]（FocusField: LineProductCodeLast）
```

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- 印刷機能なし（売上側と異なる）
- コードビハインドにロジックを書かない（フォーカス移動・DataContext 購読のみ許容）
