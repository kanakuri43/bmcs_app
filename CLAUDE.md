# bmcs_app ソリューション

## 概要
卸売B2B向け販売管理システム（中小企業向け）

## 技術スタック
- SQL Server 2022+
- C# / WPF / Prism / MahApps.Metro
- Entity Framework Core / rowversion楽観的排他
- JSON over TVP（ストアドプロシージャ）

## プロジェクト構成
| プロジェクト | 役割 |
|---|---|
| bmcs_app | WPFシェル・起動 |
| bmcs_app.Core | 共通モデル・インターフェース |
| bmcs_app.Infrastructure | DB・EF Core・SP |
| bmcs_app.Sales | 売上登録モジュール |
| bmcs_app.Payment | 入金登録モジュール |
| bmcs_app.Closing | 請求集計・締め処理モジュール |

## 共通ルール
- インボイス制度対応（税率・登録番号管理）
- 非同期は async/await 統一
- DB操作はすべてStoredProcedure経由
- DBスキーマはライブDBに直接クエリして確認する
- null許容
- スキーマはコードに転記せず必ず直接クエリする
- コードビハインドにロジックを書かない
- Prism の RegionManager でナビゲーション管理

## 命名規則
- C#: Microsoft推奨に合わせる
- DB: snake case
- ストアドには"usp_"プレフィックス

