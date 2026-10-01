# bmcs_app

石山商店の販売管理システム（受注 → 売上 → 請求 → 入金 → 元帳 → 月次締め）。C# / WPF / SQL Server / EF Core の4層構造 + MVVM。

## 最初に読むもの

| 目的 | 文書 |
|---|---|
| 開発環境を作り、ビルド・起動・テストを行う | [`docs/setup.md`](docs/setup.md) |
| 運用・障害対応・データ補正 | [`docs/operations.md`](docs/operations.md) |
| 配布・リリース | [`docs/release.md`](docs/release.md) |
| 作業ルール・文書の地図 | [`CLAUDE.md`](CLAUDE.md) |
| 業務仕様・用語・業務フロー | [`docs/product-spec.md`](docs/product-spec.md) |
| 構成・層の責務・規約 | [`docs/architecture.md`](docs/architecture.md) |
| 画面ごとの設計 | [`docs/design_document.md`](docs/design_document.md) |
| DB 設計・スキーマ変更の運用 | [`docs/database-schema.md`](docs/database-schema.md) |
| 帳票 | [`docs/report-spec.md`](docs/report-spec.md) |
| 判断の理由 | [`docs/decisions.md`](docs/decisions.md) |

## リポジトリ構成

```
bmcs_app.sln
src/      bmcs_app（WPF・起動プロジェクト）／bmcs_app.Application／bmcs_app.Infrastructure／bmcs_app.Domain
tests/    bmcs_app.Domain.Tests（DB不要）／bmcs_app.Application.Tests（開発用DBへの結合テスト）
scripts/  DDL・初期データの連番SQL（001〜）。開発用の seed／reset スクリプトを含む
docs/     設計・運用ドキュメント
TODO.md   開発タスク一覧
```
