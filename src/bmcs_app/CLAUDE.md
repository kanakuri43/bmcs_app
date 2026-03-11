# bmcs_app（WPF シェル）

## 役割
- アプリケーションのエントリーポイント
- Prism の UnityContainerExtension で各モジュールを登録
- MahApps.Metro テーマを適用

## ナビゲーション
- RegionManager でモジュール間ナビゲーションを管理
- コードビハインドにロジックを書かない（MVVM 徹底）

## 参照プロジェクト
- bmcs_app.Core
- bmcs_app.Infrastructure
- bmcs_app.Sales / bmcs_app.Payment / bmcs_app.Closing
