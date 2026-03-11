# bmcs_app.Payment（入金登録モジュール）

## 役割
- Prism IModule として入金登録機能を提供
- PaymentModule.cs でビュー・ViewModel を DI コンテナに登録

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない
