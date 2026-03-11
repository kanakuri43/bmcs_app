# bmcs_app.Order（受注登録モジュール）

## 役割
- Prism IModule として受注登録機能を提供
- OrderModule.cs でビュー・ViewModel を DI コンテナに登録

## ビジネスルール
- 受注は CRUD 対応（売上登録済みの受注は変更・削除不可）
- 売上登録済みフラグ `has_sales` は `usp_orders_select` が返す
- 変更・削除可否は `usp_orders_update` / `usp_orders_delete` でもチェックされる

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない
