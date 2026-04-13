# bmcs_app.PurchaseOrder（発注登録モジュール）

## 役割
発注伝票の新規登録・検索・更新・削除を行う単独 exe。

## 実装状況
実装完了。bmcs_app.Order のパターンを仕入側に差し替えて実装。

## ビジネスルール
- 発注は CRUD 対応（仕入登録済みの発注は変更・削除不可）
- 仕入登録済みフラグ `has_purchases` は `usp_purchase_orders_select` が動的計算して返す
- 変更・削除可否は `usp_purchase_orders_upsert` / `usp_purchase_orders_delete` でもチェックされる

## 売上側との対応

| 売上側 | 発注側 |
|---|---|
| 受注 (orders) | 発注 (purchase_orders) |
| 得意先 (customers) | 仕入先 (suppliers) |
| has_sales | has_purchases |
| OrderMainViewModel | PurchaseOrderMainViewModel |
| OrderLineViewModel | PurchaseOrderLineViewModel |
| ILookupService / LookupService (Sales) | ローカル LookupService |

## 主要コンポーネント

- `App.xaml.cs` — 起動フロー・DI（同期ロード、TaxRatePeriods / TaxTypes 初期化）
- `Services/LookupService.cs` — 仕入先 / 社員 / 商品 / 伝票 検索（ローカル実装・ILookupService 不使用）
- `ViewModels/PurchaseOrderMainViewModel.cs` — `HasPurchasesText`（"仕入登録済み" / "未登録"）、`IsLocked` フラグ
- `ViewModels/PurchaseOrderLineViewModel.cs` — `MoveToQuantityRequested` イベント
- `Views/PurchaseOrderMainView.xaml` — Title="発注伝票"
- `Views/PurchaseOrderMainView.xaml.cs` — `FocusField` イベント購読、`FocusProductCode()` デリゲート
- `Views/PurchaseOrderLineControl.xaml` / `.xaml.cs`

## DB 操作

| 操作 | SP名 |
|---|---|
| 伝票取得 | `usp_purchase_orders_select` |
| 保存（新規/更新）| `usp_purchase_orders_upsert` |
| 削除 | `usp_purchase_orders_delete` |
| 一覧取得 | `usp_purchase_orders_summaries_select` |

## フォーカスフロー
```
[伝票No.] Enter → 伝票取得（SearchCommand）
[仕入先コード] Enter → 補完後 [担当者コード]（FocusHelper.MoveNextOnEnter）
[担当者コード] Enter → 補完後 [摘要]（FocusHelper.MoveNextOnEnter）
[摘要] Enter → 行追加（必要時）→ 1行目[商品コード]（FocusField: LineProductCode）
[商品コード] Enter → 商品補完 → [数量]（MoveToQuantityRequested）
[数量] Enter → [単価]（FocusHelper.MoveNextOnEnter）
[行摘要] Enter → 行追加 → 新規行[商品コード]（FocusField: LineProductCodeLast）
```

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- コードビハインドにロジックを書かない（フォーカス移動・DataContext 購読のみ許容）
