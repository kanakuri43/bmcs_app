# bmcs_app.Order（受注登録モジュール）

## 役割
受注伝票の新規登録・検索・更新・削除を行う単独 exe。

## 実装状況
未着手。bmcs_app.Sales の実装パターンをそのまま流用する。

## ビジネスルール
- 受注は CRUD 対応（売上登録済みの受注は変更・削除不可）
- 売上登録済みフラグ `has_sales` は `usp_orders_select` が返す
- 変更・削除可否は `usp_orders_update` / `usp_orders_delete` でもチェックされる

## 実装時の参照先
- 画面・操作・フォーカスパターン: `bmcs_app.Sales/CLAUDE.md` を参照
- MasterSearchDialog / LookupService は `bmcs_app.Sales` の実装を再利用
- App.xaml.cs 起動フロー・DI パターン: ルート `CLAUDE.md` の「DI・依存注入」を参照

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない（フォーカス移動・DataContext 購読のみ許容）
