# bmcs_app.Payment（入金登録モジュール）

## 役割
入金伝票の新規登録・検索・更新・削除を行う単独 exe。

## 実装状況
未着手。bmcs_app.Sales の実装パターンをそのまま流用する。

## ビジネスルール
- 入金は得意先単位で登録（伝票No. + 得意先 + 入金日 + 金額）
- 請求集計（bmcs_app.Closing）で `ar_aggregated_at` がセットされると編集・削除不可（伝票ロック）
- ロック判定: `ar_aggregated_at IS NOT NULL`

## 実装時の参照先
- 画面・操作・フォーカスパターン: `bmcs_app.Sales/CLAUDE.md` を参照
- MasterSearchDialog / LookupService は `bmcs_app.Sales` の実装を再利用
- App.xaml.cs 起動フロー・DI パターン: ルート `CLAUDE.md` の「DI・依存注入」を参照
- 伝票ロック方針: ルート `CLAUDE.md` の「伝票ロック方針」を参照

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない（フォーカス移動・DataContext 購読のみ許容）
