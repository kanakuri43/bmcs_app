# bmcs_app アーキテクチャ

> 実装方針の正となる文書。`REVIEW.md` の P-6（4層構造＋MVVM の中身が未定義）への対応。
> 1〜4章は Phase 0-1（骨格作成）で、5章以降は Phase 0-2 で確定させた。

---

## 1. プロジェクト構成

**4層＝4プロジェクト。機能（受注・売上・請求…）はプロジェクトではなくフォルダで分ける。**

```
bmcs_app.sln
Directory.Build.props          … 全プロジェクト共通のビルド設定
src/
 ├─ bmcs_app/                  … プレゼンテーション層（WPF, exe）
 │   └─ Views/Menu/            … 機能別フォルダ。ViewModels/ も同じ構成にする
 ├─ bmcs_app.Application/      … 業務処理層
 ├─ bmcs_app.Infrastructure/   … データアクセス層
 └─ bmcs_app.Domain/           … ドメイン層
```

機能別にプロジェクトを分けない理由: 全11画面という規模に対して参照管理とビルド時間の負担が見合わないため。なお、機能ごとに exe を分ける必要は無いと判断している（同時起動は後述のとおり単一 exe で実現できる）。

## 2. 層間の依存方向

**古典的レイヤード（一方向）。** インターフェースによる依存関係の逆転は行わない。

```
Presentation ──► Application ──► Infrastructure ──► Domain
      │                                               ▲
      └───────────────────────────────────────────────┘
```

| プロジェクト | 参照するもの | 役割 |
|---|---|---|
| `bmcs_app.Domain` | なし | エンティティ、enum、消費税計算などの純粋ロジック |
| `bmcs_app.Infrastructure` | Domain | EF Core の `DbContext`・マッピング、端末ローカル設定の読み書き |
| `bmcs_app.Application` | Domain, Infrastructure | 伝票登録・請求締め・入金消込などのユースケース。トランザクション境界を持つ |
| `bmcs_app`（WPF） | Domain, Application | View / ViewModel、DI 構成 |

- **リポジトリのインターフェースは作らない。** Application が `DbContext` を直接使う。差し替えを見据えた抽象化を行わない方針（`TODO.md` 保留項目の扱い）に沿う。
- **DB に依存しない単体テストの対象は Domain に集める**（消費税計算・端数処理・状態判定）。消込・締めは開発用DBに対する結合テストで担保する。

### 規約: Presentation は Infrastructure を参照しない

ViewModel が `DbContext` を直接触らず、DB アクセスは必ず Application 層を経由させる。

**この規約はコンパイラで強制されていない。** .NET SDK の `ProjectReference` は推移的に伝播するため、`bmcs_app` の csproj が Infrastructure を参照していなくても、Application 経由で Infrastructure の型が見えてしまう（Phase 0-1 で実測して確認した）。

強制する手段は検討したが採用しなかった。

- `DisableTransitiveProjectReferences=true` … コンパイル参照は遮断できるが、`bmcs_app.Infrastructure.dll` が出力ディレクトリにコピーされなくなり実行時に読み込めない
- 上記に加えて `ExcludeAssets="compile"` を付けた明示参照 … dll はコピーされるがコンパイル参照も復活してしまい、遮断できない
- `ReferenceOutputAssembly="false"` ＋ 手動コピー … 実現はできるが MSBuild の細工が過剰

したがって**コードレビューで担保する**。将来ここを機械的に守らせたい場合は、MSBuild ではなくアーキテクチャテスト（参照関係を検査するテスト）を追加する方が素直である。

## 2.5. 開発用データベース環境

| 項目 | 値 |
|---|---|
| サーバ | `172.16.3.171` |
| エディション | SQL Server 2022 Express（Windows Server 2022 上） |
| データベース | `bmcs_db` |
| 認証 | SQL 認証 |

運用ルール:

- **DDL は必ず `bmcs_db` を明示して適用し、サーバ全体に及ぶ操作は行わない。** 同一サーバ上に他システムのデータベースが15個存在するため（`bmcs_db_demo`、`PBS_*`、`PSM_*`、`SalesManagementDB` 等）。
- **接続文字列は `src/bmcs_app/appsettings.Development.json` に置き、git 管理外とする**（パスワードを含むため `.gitignore` 済み）。リポジトリには `appsettings.Development.json.sample` のみを置く。
- **`CLAUDE.md` の Tech stack は「SQL Server (latest)」だが、実機は 2022 Express である。** Express の制約（1DB あたり 10GB、SQL Server Agent なし）を前提に設計する。
- 現在は sysadmin 権限のアカウントで接続している。**DDL 誤りが他データベースへ波及しないよう、`bmcs_db` のみに権限を絞った専用ログインの作成を推奨する**（未対応）。

## 3. ターゲットフレームワーク

| プロジェクト | TFM |
|---|---|
| `bmcs_app`（WPF） | `net10.0-windows` |
| Domain / Application / Infrastructure | `net10.0` |

- .NET 10 を採用する。LTS であり、.NET 9 は 2026年5月にサポート終了済みのため。
- **下位3層に `-windows` を付けない**のは、WPF の型が下位層に混入することをコンパイラで防ぐため。こちらは推移参照の影響を受けないので確実に効く。
- 共通設定（`Nullable=enable`、`ImplicitUsings=enable`、`LangVersion=latest`、null 安全性の警告をエラー扱い）は `Directory.Build.props` に集約する。個別の csproj には TFM とプロジェクト固有の設定のみを書く。

## 4. 複数画面の同時起動

売上入力と受注入力を同時に開くなど、**複数画面の並行利用を前提とする。** 次の2通りをどちらも許可する。

1. **1プロセス・複数ウィンドウ** … メニューから各画面を別ウィンドウとして開く。`App.xaml` の `ShutdownMode="OnLastWindowClose"` により、メインメニューを閉じても他の画面が開いている間はアプリを終了しない
2. **多重起動** … 同じ exe を複数起動する。多重起動の抑止は行わない

機能ごとに exe を分ける必要はない。exe を分けても得られるのは「別プロセスで動く」ことだけで、それは同じ exe の多重起動と等価であるため。

**`DbContext` はウィンドウごとにスコープを分ける（Phase 0-3 で実装済み）**。単一の `DbContext` を全ウィンドウで共有すると、受注画面の未保存の変更が売上画面の保存時に一緒に書き込まれるため。

実装は `src/bmcs_app/Services/WindowService.cs` が担う。`IServiceScopeFactory` でウィンドウ1つにつきスコープを1つ作り、`Window.Closed` でスコープを破棄する。**画面を追加する際は必ず `WindowService.Show<TWindow, TViewModel>()` を経由させ、`new` で直接ウィンドウを生成しない。** ウィンドウと ViewModel は `Scoped` で DI 登録する。

## 5. 各層の責務

| 層 | 置くもの | 置かないもの |
|---|---|---|
| **Domain** | エンティティ（EF Core の POCO を兼ねる）、enum、消費税・端数処理の計算、状態判定のロジック | DB アクセス、UI、DI、ファイル I/O。**副作用を持つコードを置かない** |
| **Infrastructure** | `DbContext`、Fluent API のマッピング設定、接続文字列の解決、ストアドプロシージャ呼び出し、端末ローカル設定ファイルの読み書き、帳票出力の実装 | 業務ルール（どの売上が請求対象か等） |
| **Application** | ユースケース（伝票登録、請求締め、入金消込…）、**トランザクション境界**、複数テーブルにまたがる整合更新、業務操作の権限チェック | 画面の状態、`Window` や `Visibility` などの UI 概念 |
| **Presentation** | View（XAML）、ViewModel（画面状態・入力書式・コマンド）、DI 構成、ウィンドウ管理 | 業務ルール、SQL、`DbContext` の直接操作 |

補足:

- **Domain のエンティティを EF Core の POCO として兼用する。** 別途 DTO を作って詰め替えることはしない（差し替えを見据えた抽象化を行わない方針に沿う）。
- **業務操作の権限チェックは Application 層に置く。** メニューの出し分け（Presentation）だけに頼ると、画面を直接開かれた場合に守られないため。`TODO.md` の C-8 暫定設定「各画面に直接書く」は「権限マトリクスのテーブルを作らずレベル比較を直接書く」という意味であり、比較を書く場所は Application 層のユースケース内とする。
- **ViewModel は業務ルールを判断しない。** 「この売上は訂正できるか」の判定は Application 層に問い合わせ、ViewModel はその結果でボタンの有効・無効を切り替えるだけにする。

## 6. トランザクション境界

**Application 層のユースケースメソッド1つ＝1トランザクション。** `SaveChangesAsync()` を呼ぶのは Application 層のみとする。

- **原則: 1ユースケース＝1回の `SaveChangesAsync()`。** EF Core は単一の `SaveChanges` を暗黙のトランザクションで実行するため、明示的なトランザクションは不要。入金登録と売上明細の消込ステータス更新（`docs/database-schema.md` が同一トランザクション内更新を要求）も、同じ `SaveChangesAsync()` にまとめれば満たせる。
- **明示的なトランザクション（`BeginTransactionAsync`）を使うのは次の場合に限る。**
  1. 1回の `SaveChanges` に収まらない処理（請求締めなど、大量データを分割保存する場合）
  2. ストアドプロシージャの実行と EF Core の更新を1つの整合単位にまとめる場合
- **ViewModel から複数のユースケースを呼んで1つの整合単位にしてはいけない。** 画面から2回呼べば2トランザクションになる。1つの整合単位が必要なら、Application 層にそれを1メソッドとして用意する。
- **伝票番号の採番は、伝票登録と同一トランザクション内で行う。** 別トランザクションで先に採番すると、登録が失敗したときに欠番が出る。伝票番号の欠番は業務上の説明が難しいため避ける。採番テーブルの行ロックがトランザクション終了まで残るが、同時利用者は数十人規模であり実用上の問題にならない。
- **`DbUpdateConcurrencyException` は Application 層で捕捉し、業務的な意味を持つ結果（「他のユーザーが更新しました」）に変換して返す。** ViewModel に EF Core の例外型を漏らさない。

## 7. EF Core（LINQ）とストアドプロシージャの使い分け

**原則は LINQ。ストアドプロシージャは例外的に使う。**

| | 使うもの | 理由 |
|---|---|---|
| 単票の登録・更新・参照、一覧検索、得意先元帳のマージ | **LINQ** | EF Core を O/R マッパーとして使う方針。元帳は `docs/database-schema.md` でアプリ側 LINQ マージと決定済み |
| 請求締め・月次締めの一括集計、大量行の一括更新 | **ストアドプロシージャを検討** | LINQ だと全件をメモリに展開する、または N+1 クエリになるため |

- **判断の目安は「1回の処理で 1,000 行を超える更新・集計かどうか」。** それ未満は LINQ で書く。目安を超えても、まず LINQ で書いて実測し、実際に遅い場合にストアド化する。**推測でストアドを選ばない。**
- **ストアドの呼び出しは Infrastructure 層に置く。** 生 SQL が Application 層に散らばらないようにする。Application からは通常のメソッドとして呼ぶ。
- ストアド名には `usp_` を付ける（`docs/database-schema.md`）。追加・変更したストアドは DDL と同様に `scripts/` へ連番 SQL として残す。
- **SQL ビューは使わない**（`docs/database-schema.md` の元帳方針と同じ理由）。

## 8. 非同期処理の方針

- **DB アクセスは必ず非同期。** `ToListAsync()` / `FirstOrDefaultAsync()` / `SaveChangesAsync()` を使う。同期版は使わない。
- **`.Result` / `.Wait()` / `.GetAwaiter().GetResult()` は禁止。** UI スレッドでのデッドロックを招く。
- **`async void` は禁止**（例外を捕捉できずプロセスが落ちる）。ViewModel のコマンドハンドラは `async Task` にする。WPF のイベントハンドラのみ例外として許容する。
- **`ConfigureAwait(false)` は付けない。** 上記のとおり同期待ちを禁止するためデッドロックは起きず、付けても効果はわずかな性能差にとどまる。全 `await` に付ける手間と読みにくさに見合わない。**付ける／付けないを混在させないことが重要**なので、統一して付けない。
- **`DbContext` はスレッドセーフではない。** 同一の `DbContext` に対する複数の操作を `Task.WhenAll` で並行実行してはいけない。並行させたい場合はスコープを分ける。
- **`CancellationToken` は当面、長時間処理（請求締め・月次締め・帳票の一括発行）にのみ通す。** 単票の登録・参照には導入しない（実装量に対して得るものが小さい）。
- 1秒を超える可能性のある処理は進捗表示を出す。共通の仕組みは Phase 0-3 で用意する。

## 9. 楽観的排他制御（rowversion）の適用単位

**rowversion は全テーブルの各行に持たせる。** 売上・入金・受注は明細行1テーブル構成（`docs/database-schema.md`）なので、**物理的な適用単位は明細行**になる。

ただし業務上の編集単位は「伝票（同一伝票番号の明細行の集合）」であり、行単位の rowversion だけでは次の2つを検出できない。

1. 自分が変更しなかった明細行を、他のユーザーが変更した
2. 他のユーザーが同じ伝票に明細行を追加・削除した

そのため伝票の訂正では、以下を Application 層の共通処理として実装する。

- **読み込んだ明細行すべてを更新対象に含める。** 値が変わっていない行も rowversion の照合を受けるようにし、上記1を検出する。
- **保存時に伝票の明細行の集合を再取得し、読み込み時点と一致することを確認する。** 一致しなければ他のユーザーが追加・削除したと判断して更新を中止し、上記2を検出する。
- この2つは伝票種別ごとに書かず、**共通処理として1箇所に実装する**（Phase 5 で実装）。

その他:

- **マスタは1レコードが編集単位なので、rowversion がそのまま機能する。** 追加の考慮は不要。
- **請求データ・月次締めは rowversion だけに頼らない。** 「確定済みのものを再確定しない」「解除済みのものを再解除しない」といった状態遷移の前提条件を、更新前に必ず確認する。rowversion は同時更新を検出するだけで、業務的に不正な遷移は防げない。
- 競合を検出したときは、画面に「他のユーザーが更新しました。再読み込みしてください」と表示し、**自動マージや後勝ちでの上書きは行わない**（金額が静かに壊れることを避ける）。

## 10. 命名・フォルダ規約

機能フォルダ名は全層で統一する: `Order`（受注）/ `Sales`（売上）/ `Billing`（請求）/ `Receipt`（入金）/ `Ledger`（元帳）/ `Closing`（月次締め）/ `Master`（マスタ）/ `Search`（データ検索）/ `Common`（共通）

| 層 | 規約 |
|---|---|
| Presentation | `Views/{機能}/{画面名}Window.xaml`、`ViewModels/{機能}/{画面名}ViewModel.cs`。モーダルは `{名前}Dialog` |
| Application | `{機能}/{ユースケース名}Service.cs` |
| Domain | `Entities/`、`Enums/`、`Calculations/`（消費税計算など） |
| Infrastructure | `BmcsDbContext.cs`、`Configurations/{エンティティ名}Configuration.cs`、`StoredProcedures/`、`LocalSettings/` |

- C# のクラス名・プロパティ名は PascalCase、DB のテーブル名・カラム名は `snake_case`（`docs/database-schema.md`）。変換は Infrastructure 層のマッピング設定で行う。
- 画面の正式名称は `docs/design_document.md` の画面一覧を正とする。

### エンティティ・マッピングの実装方針（Phase 1-6 で確定）

- **PascalCase ↔ snake_case の変換は `EFCore.NamingConventions`（`UseSnakeCaseNamingConvention()`）に任せる。** 列ごとに `HasColumnName` を書かずに済む。ただし**数字を含む列名**（`address1`／`address2` 等）は自動変換が意図通りにならないため、該当プロパティのみ `HasColumnName` を明示する。
- **テーブル名は DDL の単数形に対して `ToTable()` を必ず明示する。** DbSet プロパティは複数形（`Customers` 等）で宣言するため、命名変換に任せると `customers` のように誤って複数形になる。
- **ナビゲーションプロパティは持たせない。** 得意先元帳はアプリ側 LINQ で複数テーブルをマージする方針であり、`Include()` によるナビゲーション経由の結合を使わないため。リポジトリを作らない方針と同様、使わない抽象化を先回りして作らない。
- **ただし FK 関係は `HasOne<TPrincipal>().WithMany().HasForeignKey(...)` で登録する（ナビゲーションプロパティなしで）。** これを省略すると、複数エンティティを同一 `SaveChangesAsync()` で保存したときに EF Core が依存関係を解決できず、INSERT 文の順序が（観測した限りでは）テーブル名のアルファベット順になり、FK 制約違反を起こす。DB 側にすでに存在する FK 制約（Phase 1-5）と対になる形で、全 FK 関係を登録する。
- **監査列は共通基底クラスで重複を排除する。** `TrackedEntity`（`CreatedBy`/`CreatedAt`/`UpdatedBy`/`UpdatedAt`）と、これを継承し `IsDeleted`/`RowVersion` を追加する `AuditableEntity` の2段構成。共通基底クラス（`BillingTaxUnitBase` 等）を持つテーブル群は、Fluent API 側も共通拡張メソッド（`Configurations/TaxUnitConfigurationExtensions.cs`）に集約する。
- **`decimal` は `HasPrecision(p, s)` を必ず明示する。** 省略すると既定精度（18,2）になり、`decimal(15,4)` の単価カラム等で桁落ちする。
- **`varchar`/`char` 列は `.IsUnicode(false)` を明示する。** 省略すると EF Core が `nvarchar` パラメータを送り、SQL Server 側で暗黙変換が発生してインデックスを使えなくなる（コード系カラムは PK/FK で全 JOIN に絡むため実害が大きい）。
- **`date` 型は C# `DateOnly` にマッピングする。** EF Core 8+ のネイティブ対応。時刻成分を持たせないことでバグを防ぐ。

## 11. MVVM の実装方針

- **`CommunityToolkit.Mvvm` を使う。** `ObservableObject` や `RelayCommand` を自作しない。
- **ViewModel は View を参照しない。** 別ウィンドウを開く操作は、Presentation 層内のウィンドウ管理サービス経由で行う（Phase 0-3 で実装）。ViewModel が `new SalesWindow()` を書かない。
- **入力の書式（日付・金額のカンマ区切り等）と操作性（Enter でのフォーカス移動等）は共通のスタイル・ビヘイビアで実現する**（Phase 0-5、0-6）。画面ごとに個別実装しない。
- **入力値の形式チェックは ViewModel、業務ルールの検証は Application 層。** 「数値が入っているか」は ViewModel、「この得意先にこの税区分は登録できるか」は Application で判定する。
