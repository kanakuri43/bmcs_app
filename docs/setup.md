# bmcs_app 開発環境の構築

ビルド・起動・テストまで到達するための手順。配布は `docs/release.md`、運用は `docs/operations.md` を参照。

---

## 1. 前提ソフト

| ソフト | 内容 |
|---|---|
| OS | Windows 11 以降 |
| .NET 10 SDK | `net10.0` / `net10.0-windows`（WPF）をビルドする |
| SQL Server | 接続先の DB サーバ（開発用は `docs/architecture.md` 2.5節）。新規構築は SQL Server 2022 Express 以上 |
| `sqlcmd` | `scripts/` の適用に使う（go-sqlcmd で動作確認済み。`-f 65001` は使えない） |
| Git | |

---

## 2. データベースの作成

1. 空の DB `bmcs_db` を作成する（作成用スクリプトは無い。`scripts/001` は `USE bmcs_db;` から始まる）。
2. `scripts/` の連番SQLを **番号順に** 適用する。001 は単数形のテーブル名で作成し、019 で複数形に改名するため、順序を飛ばさない。

```
sqlcmd -S <サーバ> -U <ユーザー> -P <パスワード> -d bmcs_db -C -I -i scripts\001_create_master_tables.sql
（002, 003, … 022 を同様に順に適用）
```

- `-C`: サーバ証明書を信頼する。`-I`: `QUOTED_IDENTIFIER ON`（フィルタ付き一意インデックスの作成・更新に必要）。**常に `-I` を付ける。**
- 各スクリプトは `IF OBJECT_ID(...) IS NULL` 等で再実行しても壊れない作りになっている。
- `014_seed_menu_structure.sql` はメニュー構成と権限レベルを投入する。**開発・本番とも適用する。** 再適用すると、メニューマスタ管理画面での編集内容は本スクリプトの値に戻る。
- 適用済みスクリプトは改変しない。スキーマ変更は新しい連番ファイルで行う（`docs/database-schema.md` 3.2節）。

### 開発用データ（開発DB専用）

| スクリプト | 内容 | 注意 |
|---|---|---|
| `seed_dev_data.sql` | 開発・確認用のテストデータを投入（自身のデータのみ削除→再投入） | **本番へ流さない** |
| `reset_test_data.sql` | 得意先・商品・コピー機マスタ・伝票等を全削除し、採番を0に戻す（`company_infos`・`menus`・`tax_rates`・`deposit_methods` は残す） | **本番へ流さない** |

どちらも `-I` を付けて実行する。

---

## 3. 接続文字列

接続文字列のキーは `ConnectionStrings:BmcsDb`。`src/bmcs_app/appsettings.Development.json`（`.gitignore` 済み）に置く。

1. `src/bmcs_app/appsettings.Development.json.sample` を同名の `.json` にコピーする。
2. サーバ・ユーザー・パスワードを書き換える。

```
Server=<サーバ>;Database=bmcs_db;User Id=<ユーザー>;Password=<パスワード>;TrustServerCertificate=True;
```

- 未設定だと起動時に `接続文字列 'BmcsDb' が設定されていません` で失敗する。
- `appsettings.Development.json` は存在するときだけ出力フォルダへコピーされる。実行環境名に関わらず読み込まれる。
- テストも同じ設定を使う。`tests/bmcs_app.Application.Tests` の `.sample` を同様にコピーする。

---

## 4. ビルド・起動・テスト

| 操作 | コマンド |
|---|---|
| ビルド | `dotnet build` |
| 起動 | `dotnet run --project src/bmcs_app [社員コード]` |
| 単体テスト（DB不要） | `dotnet test tests/bmcs_app.Domain.Tests` |
| 結合テスト（DB必要） | `dotnet test tests/bmcs_app.Application.Tests` |
| テストの絞り込み | `--filter "FullyQualifiedName~<クラス名>"` |

- テストは **プロジェクトごとに** 実行する（ソリューション全体だと WPF プロジェクトが含まれる）。
- 結合テストはライブDBに接続する。使い捨てデータは `__` 接頭辞のキーで作り、テスト内で物理削除する。

### 起動引数

引数は社員コード1つだけ（`args[0]`）。省略・空白の場合は `0`。社員マスタに存在しなければメインメニューが権限を解決できない。社員コードは監査列（作成者・更新者）にも使われる。他の引数は無い。

### 同時起動

複数の端末・複数のインスタンスを同時に起動できる（単一起動制限は無い）。排他は rowversion による楽観的排他制御（`docs/architecture.md` 9章）。

---

## 5. 共有開発DBである注意

開発用DBは共有のライブDBで、`seed_dev_data.sql`・`reset_test_data.sql`・結合テストが同じDBを使う。

- DDL は必ず `bmcs_db` を明示して適用し、サーバ全体に及ぶ操作は行わない（同一サーバに他システムのDBがある）。
- 他の人のデータを `reset_test_data.sql` で消さない。実行前に周囲へ周知する。
- 結合テストは、DBの状態（シードの有無）によって失敗することがある。
