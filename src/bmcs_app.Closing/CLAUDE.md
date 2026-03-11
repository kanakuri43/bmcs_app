# bmcs_app.Closing（請求集計・締め処理モジュール）

## 役割
- Prism IModule として請求集計・締め処理機能を提供
- ClosingModule.cs でビュー・ViewModel を DI コンテナに登録

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない
