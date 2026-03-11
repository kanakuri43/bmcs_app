# bmcs_app.Sales（売上登録モジュール）

## 役割
- Prism IModule として売上登録機能を提供
- SalesModule.cs でビュー・ViewModel を DI コンテナに登録

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない
