# extract-db-schema

ライブDB（bmcs_db）のスキーマを抽出して `docs/references/database_definition.sql` に保存する。

## 手順

以下の PowerShell スクリプトを一時ファイル `tasks/extract_schema.ps1` に書き出し、実行後に削除する。

### 接続情報
`src/bmcs_app.Infrastructure/CLAUDE.md` を参照。

### 出力先
`docs/references/database_definition.sql`（UTF-8 BOM付き）

### 出力内容（順番）
1. ヘッダーコメント（DB名・サーバー・抽出日時）
2. `-- ===== TABLES =====` セクション
   - `sys.tables` から全テーブルを `name` 順で取得
   - 各テーブルの `CREATE TABLE` 文を生成
     - カラム定義：型・精度・NULL制約・IDENTITY・DEFAULT
     - PRIMARY KEY 制約（`sys.key_constraints`）
     - FOREIGN KEY 制約（`sys.foreign_key_columns`）
   - 非PK インデックス（`CREATE [UNIQUE] INDEX`）
3. `-- ===== STORED PROCEDURES & FUNCTIONS =====` セクション
   - `sys.objects` から `type IN ('P','FN','IF','TF')` を `type, name` 順で取得
   - `OBJECT_DEFINITION()` で定義を取得

### PowerShell 実装のポイント
- `System.Data.SqlClient.SqlConnection` を使用
- `Exec-Query` 関数では **`return ,$dt`**（カンマ付き）で DataTable のアンラップを防ぐ
- `foreach ($row in $dt.Rows)` でカラム値は `[string]$row["col_name"]` と明示キャスト
- `[System.DBNull]` チェックを忘れずに（default_def, id_seed 等）
- 出力は `System.Text.StringBuilder` に蓄積し、`File.WriteAllText` で一括書き込み

### 実行コマンド
```
powershell.exe -ExecutionPolicy Bypass -File "tasks/extract_schema.ps1"
```

実行後、`tasks/extract_schema.ps1` を削除する。
