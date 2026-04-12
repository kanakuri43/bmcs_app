# タスク管理

## 実装済みモジュール

| モジュール | 状態 |
|---|---|
| bmcs_app（ランチャー） | 完了 |
| bmcs_app.Master（マスタ保守） | 完了 |
| bmcs_app.Sales（売上登録） | 完了 |
| bmcs_app.Order（受注登録） | 完了 |
| bmcs_app.Receipt（入金登録） | 完了 |
| bmcs_app.Closing（請求集計・売掛金集計） | 完了 |
| bmcs_app.Search（伝票横断検索） | 完了 |
| bmcs_app.Shared/Helpers/FocusHelper.cs | 完了（Sales/Order/Receipt 共用） |
| bmcs_app.Core/Services/TaxCalculator.cs | 完了（Sales/Order 共用） |

---

## 残タスク

### [ ] Closing: 得意先指定機能
- 請求集計・売掛金集計で特定得意先のみを対象にする機能
- 現在は「全得意先」のみ動作し「指定」RadioButton は `IsEnabled=False`
- `usp_invoice_closing` / `usp_ar_closing` は `@customer_id` パラメータ対応済み（SP側は完成）
- **残作業**: View の RadioButton 有効化 + 得意先コード欄の入力 → `@customer_id` を ViewModel から SP に渡す
- **残作業**: 請求残高一覧表・売掛金残高一覧表 （レポートおよびCSV出力）

### [ ] Search: 伝票検索

- **残作業**: 発注・仕入 伝票検索  発注・仕入伝票登録実装後に対応

