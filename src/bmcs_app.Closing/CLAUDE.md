# bmcs_app.Closing（請求集計・締め処理モジュール）

## 役割
月次の請求集計・締め処理を行う単独 exe。

## 実装状況
未着手。

## ビジネスルール
- 締め処理対象: 指定得意先 × 指定締め日以前の未集計売上
- 集計実行時に `sales.invoiced_at` / `receipts.ar_aggregated_at` をセット → 伝票ロック
- 集計日時は `GETDATE()` ではなく `@process_date`（任意の過去日付を指定可能）:
  ```sql
  UPDATE sales SET invoiced_at = @process_date
  WHERE customer_id = @customer_id AND sale_date <= @closing_date AND invoiced_at IS NULL;
  ```
- 集計取り消し: 当該カラムを NULL に戻す → ロック解除

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない
