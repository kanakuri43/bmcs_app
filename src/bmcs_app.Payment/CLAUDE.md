# bmcs_app.Payment（支払登録モジュール）

## 役割
支払伝票の新規登録・検索・更新・削除を行う単独 exe。

## 実装状況
実装完了。bmcs_app.Receipt のパターンを仕入側に差し替えて実装。

## ビジネスルール
- 支払は CRUD 対応
- ロックカラム: `ap_closing_at`（買掛金集計済みなら編集・削除不可）
  - Sales の `invoiced_at` / `ar_aggregated_at` に相当するが、仕入側は請求締めがないため `ap_closing_at`（1カラム）のみ
  - 将来の買掛金集計（Closing 拡張）のために確保しているカラム
- 支払区分は入金側と同じ `payment_method_classifications` テーブルを流用（別テーブル化不要）
- 手形区分選択時のみ `bill_due_date`（手形期日）DatePicker を表示

## 採番ルール
- 支払No.: `yyyyMMddnnn` 形式（例: `20260413001`）
- 既存の受注・売上・発注・仕入と統一

## 入金側との対応

| 入金側 | 支払側 |
|---|---|
| 入金 (receipts) | 支払 (payments) |
| 得意先 (customers) | 仕入先 (suppliers) |
| invoiced_at / ar_aggregated_at | ap_closing_at |
| 入金区分 | 支払区分（同テーブル流用） |
| ReceiptMainViewModel | PaymentMainViewModel |
| ReceiptLineViewModel | PaymentLineViewModel |

## 主要コンポーネント

- `App.xaml.cs` — 起動フロー・DI
- `Services/LookupService.cs` — 仕入先 / 支払区分 / 伝票 検索（ローカル実装）
- `ViewModels/PaymentMainViewModel.cs` — `PaymentMethods` コレクション（ComboBox 用）
- `ViewModels/PaymentLineViewModel.cs` — `IsBillDueDateVisible`（PaymentMethodName == "手形"）
  - `BillDueDate` は `DateTime?`（WPF DatePicker 用。保存時に `DateOnly?` に変換）
- `Views/PaymentMainView.xaml` — Title="支払伝票"、BooleanToVisibilityConverter を Window.Resources に定義
- `Views/PaymentMainView.xaml.cs` — `FocusField` イベント購読、`FocusPaymentMethod()` デリゲート
- `Views/PaymentLineControl.xaml` / `.xaml.cs`

## DB 操作

| 操作 | SP名 |
|---|---|
| 伝票取得 | `usp_payments_select` |
| 保存（新規/更新）| `usp_payments_upsert` |
| 削除 | `usp_payments_delete` |
| 一覧取得 | `usp_payments_summaries_select` |

`@lines` JSON フィールド: `line_no`, `payment_method_id`, `amount`, `line_remarks`, `bill_due_date`

## 列ヘッダー（PaymentLineControl と一致）

| 列 | 幅 |
|---|---|
| 行番号 | 36 |
| 支払区分 | 130 |
| 手形期日 | 120 |
| 金額 | 120 |
| 行摘要 | * |
| 削除 | 28 |

## フォーカスフロー
```
[伝票No.] Enter → 伝票取得（SearchCommand）
[仕入先コード] Enter → 補完後 [摘要]（FocusHelper.MoveNextOnEnter）
[摘要] Enter → 行追加（必要時）→ 最終行[支払区分]（FocusField: LinePaymentMethod）
[支払区分] → [手形期日 or 金額]（FocusHelper.MoveNextOnEnter）
[金額] → [行摘要]（FocusHelper.MoveNextOnEnter）
[行摘要] Enter → 行追加 → 新規行[支払区分]（FocusField: LinePaymentMethod）
```

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- コードビハインドにロジックを書かない（フォーカス移動・DataContext 購読のみ許容）
