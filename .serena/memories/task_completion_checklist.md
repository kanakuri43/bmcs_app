# タスク完了時にすべきこと

1. **ビルド確認**: `dotnet build` を実行し、0エラー（Nullable警告もエラー扱いなので注意）。
2. **DBスキーマを変更した場合**:
   - 新しい連番DDLファイルを`scripts/`に追加（既存の適用済みファイルは改変しない）。
   - `sqlcmd`で開発用ライブDB（`172.16.3.171` / `bmcs_db`）に実際に適用し、結果を確認する（カラム構成・データをSELECTで確認）。
   - 対応するEF Coreエンティティ・`IEntityTypeConfiguration`をコードに反映し、`BmcsDbContext`にDbSetを登録。
   - `scripts/seed_dev_data.sql`が新スキーマと整合しているか確認し、再実行してエラーが出ないことを確認する（このスクリプトは何度でも再実行可）。
   - `docs/database-schema.md`（テーブル定義・方針・未確定事項リスト）を更新する。関連して`docs/product-spec.md`等の記述も整合させる。
3. **テスト実行**（2026-09-10時点。テストプロジェクトは存在する）:
   - `dotnet test tests/bmcs_app.Domain.Tests` を実行し全green（金額計算・状態判定ロジックを変更した場合は特に必須）。
   - DB結合やユースケース層（Application）を変更した場合は `dotnet test tests/bmcs_app.Application.Tests` も実行（開発用ライブDBへの実接続が必要）。
   - 新しいユースケース（画面の保存処理等）を実装した場合は、対応する単体テスト／結合テストを追加する（既存の`SalesTaxAmountAssignerTests`/`SalesServiceTests`が例）。
4. **UI/フロントエンド変更の場合**: 実際にアプリを起動して動作確認する（型チェック・ビルド成功だけでは機能の正しさは保証されない）。GUIでの手動確認が難しい場合は、`tests/bmcs_app.Application.Tests`に開発用ライブDBへの結合テストを追加して代替する（Phase 5-2で採用したパターン）。
5. コミットは**ユーザーから明示的に指示された場合のみ**行う。**コミットメッセージは日本語で書く**（CLAUDE.md、2026-09-10追加）。
