# よく使うコマンド（Windows / PowerShell or Git Bash）

## ビルド・実行
- `dotnet build` — 全プロジェクトビルド（ルートで実行）。Nullable警告はエラーになるので注意。
- `dotnet run --project src/bmcs_app` — WPFアプリ起動（想定。要確認）

## テスト
- `dotnet test tests/bmcs_app.Domain.Tests` — DB不要の単体テスト（消費税計算・単価決定・税額分岐・伝票区分正規化・編集ロック判定・消込配分ロジック等）。
- `dotnet test tests/bmcs_app.Application.Tests` — 開発用ライブDBへの結合テスト（`DevDatabaseFixture`経由、実接続が必要）。採番・各画面の登録/訂正/取消・消込・フェーズレビュー。
- 件数はフェーズ追加のたびに増えるため固定値をここに書かない。`dotnet test`実行結果の合計件数を都度確認する。
- 特定テストのみ: `dotnet test <プロジェクトパス> --filter "FullyQualifiedName~<クラス名>"`

## DB（開発用ライブDB `bmcs_db`、サーバ `172.16.3.171`）
sqlcmdは `go-sqlcmd`（v1.9.0系）。認証情報は `src/bmcs_app/appsettings.Development.json` 参照（sa / 要パスワード）。

- スキーマ変更の適用（新しい連番ファイルを追加した後）:
  `sqlcmd -S 172.16.3.171 -U sa -P '<password>' -d bmcs_db -C -i scripts/00N_xxx.sql`
- 開発用シードデータの投入（何度でも再実行可、既存データをDELETEしてから再投入）:
  `sqlcmd -S 172.16.3.171 -U sa -P '<password>' -d bmcs_db -C -i scripts/seed_dev_data.sql`
- スキーマ確認（乖離チェック）:
  `sqlcmd ... -Q "SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.<table>')"` 等
- テスト後の残留データ確認（`__`接頭辞のテスト用customer_codeが残っていないか）:
  `sqlcmd -S 172.16.3.171 -d bmcs_db -U sa -P '<password>' -C -Q "SELECT COUNT(*) FROM dbo.customers WHERE customer_code LIKE '\_\_TST%' ESCAPE '\'"`

**注意**: `scripts/001_*.sql` 等の適用済みDDLファイルは絶対に改変しない。スキーマ変更は必ず新しい連番ファイルを追加する（`docs/database-schema.md` 3章）。

## Git
- 通常のgitコマンド。**コミットメッセージは日本語で書く**（CLAUDE.md、2026-09-10追加。Conventional Commits形式ではない自由記述）。

## その他ユーティリティ（Windows）
- ファイル検索: `Glob`/`Grep`ツール推奨（`find`/`grep`より確実）
- シェルはPowerShell 5.1想定。Bashツールも利用可能（Git Bash、POSIX構文）。
