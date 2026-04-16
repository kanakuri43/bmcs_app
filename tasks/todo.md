# タスク管理

## 実装済みモジュール

| モジュール | 状態 |
|---|---|
| bmcs_app（ランチャー） | 完了 |
| bmcs_app.Master（マスタ保守） | 完了（仕入先マスタ含む） |
| bmcs_app.Sales（売上登録） | 完了 |
| bmcs_app.Order（受注登録） | 完了 |
| bmcs_app.Receipt（入金登録） | 完了 |
| bmcs_app.Closing（請求集計・売掛金集計） | 完了 |
| bmcs_app.Search（伝票横断検索） | 完了 |
| bmcs_app.PurchaseOrder（発注登録） | 完了 |
| bmcs_app.Purchase（仕入登録） | 完了 |
| bmcs_app.Payment（支払登録） | 完了 |
| bmcs_app.Shared/Helpers/FocusHelper.cs | 完了（Sales/Order/Receipt/Purchase/PurchaseOrder/Payment 共用） |
| bmcs_app.Core/Services/TaxCalculator.cs | 完了（Sales/Order/Purchase/PurchaseOrder 共用） |

---

## 残タスク（既存）

### [ ] Closing: 得意先指定機能
- 請求集計・売掛金集計で特定得意先のみを対象にする機能
- 現在は「全得意先」のみ動作し「指定」RadioButton は `IsEnabled=False`
- `usp_invoice_closing` / `usp_ar_closing` は `@customer_id` パラメータ対応済み（SP側は完成）
- **残作業**: View の RadioButton 有効化 + 得意先コード欄の入力 → `@customer_id` を ViewModel から SP に渡す
- **残作業**: 請求残高一覧表・売掛金残高一覧表 （レポートおよびCSV出力）

---

## 新規機能: 発注・仕入・支払

受注→売上→入金 の対称処理として、発注→仕入→支払 を追加する。

### 対応関係

| 売上側 | 仕入側 | 備考 |
|---|---|---|
| 受注 (orders / bmcs_app.Order) | 発注 (purchase_orders / bmcs_app.PurchaseOrder) | |
| 売上 (sales / bmcs_app.Sales) | 仕入 (purchases / bmcs_app.Purchase) | |
| 入金 (receipts / bmcs_app.Receipt) | 支払 (payments / bmcs_app.Payment) | |
| 得意先 (customers) | 仕入先 (suppliers) ※新規マスタ | |

---

## 設計上の注意点

### ロックカラム
- `purchases.ap_closing_at` / `payments.ap_closing_at`: 買掛金締め処理でセット
- `sales` の `invoiced_at` / `ar_aggregated_at` に相当するが、仕入側は請求締めがないため1カラムで十分
- 将来の買掛金集計（Closing 拡張）のために確保しておく

### 発注の has_purchases フラグ
- 売上側の `orders.HasSales` に相当
- 1件でも仕入が起票された発注は `has_purchases=true` → 発注削除不可

### 採番ルール
- 既存と統一: `yyyyMMddnnn` 形式（例: 2026041300１）

### 税計算
- 仕入・発注は既存の `TaxCalculator` をそのまま流用可

### payment_method_classifications の流用
- 支払区分（現金・手形等）は入金側の `payment_method_classifications` テーブルをそのまま共用
- 別テーブル化は不要
