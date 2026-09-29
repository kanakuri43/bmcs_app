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
tests/
 └─ bmcs_app.Domain.Tests/     … Domain の単体テスト（Phase 5-1 で構築。15章参照）
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

`Show<TWindow, TViewModel>(Action<TViewModel>? configure = null)` は `ShowDialog` と同じ位置づけの `configure` コールバックを持つ（TODO.md 8-3で追加）。呼び出し元の文脈（プレビュー対象の伝票No.等）を ViewModel へ渡すために使うが、**`configure` は値を設定するだけに留め、非同期の初期化処理は行わない**。`Show` は `window.Show()` の前に `configure` を呼ぶが、View の `Loaded`（→ ViewModel の `LoadCommand`）はその後に非同期で発火するため、`configure` 側で非同期処理をすると実行順が保証されない。

**帳票プレビュー（TODO.md 10-3）はこの「複数画面の並行利用」の例外として `ShowDialog`（モーダル）を使う**（2026-09-15ユーザー確認）。プレビューを開いている間は他画面を操作できなくなるが、①プレビューは印刷前の確認作業であり長時間開けたままにする文書ではない、②印刷成功→発行記録（`DeliveryNoteService.MarkIssuedAsync`等）の順序をコールバックなしで保証できる、という理由から許容する。

### モーダルダイアログ（選択結果を返す画面、Phase 3 で追加）

共通検索モーダルのように「呼び出し元へ選択結果を返して閉じる」画面は、上記 `Show` とは別に `WindowService.ShowDialog<TWindow, TViewModel, TResult>(Action<TViewModel>? configure = null)` を使う。

- `TViewModel` は `src/bmcs_app/ViewModels/DialogViewModelBase.cs` の `DialogViewModelBase<TResult>` を継承する。`CloseWith(TResult? result)` を呼ぶと `Result` を設定し `CloseRequested` イベントを発火する。ViewModel は View を参照しない方針（11章）のため、実際に `Window.Close()` を呼ぶのは `ShowDialog` 側で `CloseRequested` を配線して行う。
- `Show` と同様にウィンドウ1つにつきスコープを1つ作り、`window.ShowDialog()` の完了後（`finally`）にスコープを破棄する。
- `configure` は、呼び出し元の文脈（対象得意先コード等）を ViewModel の生成後・表示前に設定するためのコールバック。
- キャンセル操作は `DialogViewModelBase<TResult>` が提供する `CancelCommand`（`CloseWith(default)`）を使う。呼び出し元は戻り値が `null` かどうかでキャンセルを判定する。
- 命名・配置は10章の規約どおり `Views/Common/{名前}Dialog.xaml` / `ViewModels/Common/{名前}DialogViewModel.cs`（`CustomerSearchDialog`/`ProductSearchDialog` が実装例）。

## 5. 各層の責務

| 層 | 置くもの | 置かないもの |
|---|---|---|
| **Domain** | エンティティ（EF Core の POCO を兼ねる）、enum、消費税・端数処理の計算、状態判定のロジック | DB アクセス、UI、DI、ファイル I/O。**副作用を持つコードを置かない** |
| **Infrastructure** | `DbContext`、Fluent API のマッピング設定、接続文字列の解決、ストアドプロシージャ呼び出し、端末ローカル設定ファイルの読み書き | 業務ルール（どの売上が請求対象か等）、WPF 依存のコード |
| **Application** | ユースケース（伝票登録、請求締め、入金消込…）、**トランザクション境界**、複数テーブルにまたがる整合更新、業務操作の権限チェック、**帳票のデータ取得**（WPF 型を含まないプレーンな DTO を返す） | 画面の状態、`Window` や `Visibility` などの UI 概念 |
| **Presentation** | View（XAML）、ViewModel（画面状態・入力書式・コマンド）、DI 構成、ウィンドウ管理、**帳票のレンダリング・印刷・PDF出力・プレビュー**（`src/bmcs_app/Reports/`） | 業務ルール、SQL、`DbContext` の直接操作 |

補足:

- **Domain のエンティティを EF Core の POCO として兼用する。** 別途 DTO を作って詰め替えることはしない（差し替えを見据えた抽象化を行わない方針に沿う）。
- **業務操作の権限チェックは Application 層に置く。** メニューの出し分け（Presentation）だけに頼ると、画面を直接開かれた場合に守られないため。`TODO.md` の C-8 暫定設定「各画面に直接書く」は「権限マトリクスのテーブルを作らずレベル比較を直接書く」という意味であり、比較を書く場所は Application 層のユースケース内とする。
- **ViewModel は業務ルールを判断しない。** 「この売上は訂正できるか」の判定は Application 層に問い合わせ、ViewModel はその結果でボタンの有効・無効を切り替えるだけにする。
- **帳票出力（TODO.md 10-3・2026-09-15訂正）**: 当初は「帳票出力の実装」を Infrastructure 層に置く想定だったが、M-10確定方式（`docs/report-spec.md`）が WPF の `FixedDocument`/`FixedPage`/`PrintQueue` に依存するため、`Infrastructure`（`net10.0`、WPF なし）には置けないことが実装着手時に判明した。`Infrastructure` を `net10.0-windows`＋`UseWPF` にすると、これを参照する `Application` 層とテスト2プロジェクト（`Domain.Tests`／`Application.Tests`）まで推移的に WPF 依存になってしまうため採らない。**帳票のデータ取得（DB読込・税額の組み立て等）は各業務領域フォルダ（10章の規約）の Application サービスが担い、WPF 型を含まないプレーンな DTO（例: `DeliveryNoteData`）を返す。レンダリング（`FixedDocument` の組み立て）・印刷・PDF出力・プレビューは Presentation 層の `src/bmcs_app/Reports/` にまとめて置く**（`Views/`・`ViewModels/`とは別の技術的フォルダ。`Services/`・`Behaviors/`・`Converters/`と同じ位置づけ）。

## 6. トランザクション境界

**Application 層のユースケースメソッド1つ＝1トランザクション。** `SaveChangesAsync()` を呼ぶのは Application 層のみとする。

- **原則: 1ユースケース＝1回の `SaveChangesAsync()`。** EF Core は単一の `SaveChanges` を暗黙のトランザクションで実行するため、明示的なトランザクションは不要。入金登録と売上明細の消込ステータス更新（`docs/database-schema.md` が同一トランザクション内更新を要求）も、同じ `SaveChangesAsync()` にまとめれば満たせる。
- **明示的なトランザクション（`BeginTransactionAsync`）を使うのは次の場合に限る。**
  1. 1回の `SaveChanges` に収まらない処理（請求締めなど、大量データを分割保存する場合）
  2. ストアドプロシージャの実行と EF Core の更新を1つの整合単位にまとめる場合
  3. **伝票番号の採番と伝票登録を1つの整合単位にまとめる場合**（下記）
  4. **キャッシュ列を「書いた内容をDBから読み直して」再計算する場合**（入金消込。TODO.md 7-1）。
     `SettlementService.RecalculateForBillingGroupAsync`（`src/bmcs_app.Application/Receipt/`）は、
     入金明細（`receipts`／`detail_receipts`）から得意先の売上明細行の消込キャッシュ列
     （`sales.settlement_status`／`settled_amount`）を再計算する。再計算方式（差分ではなく
     毎回全件から導出する。docs/design_document.md 16章）は、呼び出し元が入金行を保存した
     **後**でなければ再計算の入力（DB上の確定値）が揃わないため、1ユースケース内で
     `SaveChangesAsync` を2回呼ぶ構成になる（①入金行の保存 → ②
     `RecalculateForBillingGroupAsync` 内部での消込キャッシュ列の保存）。1つの整合単位である
     ことは明示トランザクションで担保する。`SettlementService` 自身は
     `BeginTransactionAsync`／`CommitAsync` を呼ばず、呼び出し元が開始した明示トランザクション
     に参加する（`OrderStatusService.ApplySalesQuantityDeltasAsync` と同じ構成）。明示
     トランザクションが開始されていない場合は `InvalidOperationException` を投げる
     （`SlipNumberService.NextAsync` と同じアサーション）。
  5. **導出データを「書いた内容をDBから読み直して」再構築する場合**（入金の訂正。TODO.md 7-5）。
     `ReceiptEntryService.UpdateAsync`は、明細行（支払手段の内訳）を更新した直後に
     `receipt_allocations`（請求への充当。導出データ）を全部論理削除して`SaveChangesAsync`を
     1回呼び、その**後**に`GetOutstandingBillingsAsync`（サーバー側クエリ）で請求残高を
     再取得して新しい充当を組み立てる。上記4番目のケースと同じ理由（サーバー側クエリは
     ChangeTracker上の未コミット変更を見ないため、確定させてから読み直す必要がある）で、
     1ユースケース内で`SaveChangesAsync`を2回呼ぶ（詳細は`docs/design_document.md` 19章）。
- **ViewModel から複数のユースケースを呼んで1つの整合単位にしてはいけない。** 画面から2回呼べば2トランザクションになる。1つの整合単位が必要なら、Application 層にそれを1メソッドとして用意する。
- **伝票番号の採番は、伝票登録と同一トランザクション内で行う（TODO.md 4-1）。** 別トランザクションで先に採番すると、登録が失敗したときに欠番が出る。伝票番号の欠番は業務上の説明が難しいため避ける。採番テーブルの行ロックがトランザクション終了まで残るが、同時利用者は数十人規模であり実用上の問題にならない。
  - 呼び出し順は「メモリ上でエンティティグラフを組み立てる → `BeginTransactionAsync` → `SlipNumberService.NextAsync` → 番号を明細行に代入 → `SaveChangesAsync`（1回） → `CommitAsync`」。**画面を開いた時点や入力開始時に採番してはならない。** ロックがコミットまで残るため、UI操作をまたいでトランザクションを開いたままにしない。
  - 同一トランザクション内で複数の採番系列（`SlipNumberKind`）を取得する場合は、**列挙値の宣言順で取得する。** 取得順序が呼び出し元ごとに異なると ABBA デッドロックの可能性がある。
  - `SlipNumberService.NextAsync` は明示トランザクションが開始されていない場合 `InvalidOperationException` を投げる（`src/bmcs_app.Application/Common/SlipNumberService.cs` 経由で `src/bmcs_app.Infrastructure/Numbering/SlipNumberSequenceCommand.cs` が検証する）。採番だけが暗黙のトランザクションで先にコミットされてしまう事故を、実行時に確実に検出するためのアサーションであり、抽象化ではない。
  - **アンビエントトランザクション（`System.Transactions.TransactionScope`）は使わない。** `Database.CurrentTransaction` は `TransactionScope` 配下では `null` のままになり、上記のガードが常に発火してしまう。
  - **`EnableRetryOnFailure`（EF Core の自動リトライ）は使わない。** ユーザー起動トランザクション（`BeginTransactionAsync`）は再試行戦略と併用できないため、将来これを有効化すると採番が全面的に失敗する。
- **`DbUpdateConcurrencyException` は Application 層で捕捉し、業務的な意味を持つ結果（「他のユーザーが更新しました」）に変換して返す。** ViewModel に EF Core の例外型を漏らさない。

## 7. EF Core（LINQ）とストアドプロシージャの使い分け

**原則は LINQ。ストアドプロシージャは例外的に使う。**

| | 使うもの | 理由 |
|---|---|---|
| 単票の登録・更新・参照、一覧検索、得意先元帳のマージ | **LINQ** | EF Core を O/R マッパーとして使う方針。元帳は `docs/database-schema.md` でアプリ側 LINQ マージと決定済み |
| 請求締め・月次締めの一括集計、大量行の一括更新 | **ストアドプロシージャを検討** | LINQ だと全件をメモリに展開する、または N+1 クエリになるため |

- **判断の目安は「1回の処理で 1,000 行を超える更新・集計かどうか」。** それ未満は LINQ で書く。目安を超えても、まず LINQ で書いて実測し、実際に遅い場合にストアド化する。**推測でストアドを選ばない。**
- **ストアドに限らず、生SQL全般の呼び出しは Infrastructure 層に置く。** Application 層に生SQLが散らばらないようにする。Application からは通常のメソッドとして呼ぶ。
  - 例: 伝票番号の採番（`src/bmcs_app.Infrastructure/Numbering/SlipNumberSequenceCommand.cs`、TODO.md 4-1）。`UPDATE ... OUTPUT` による「+1して読む」のアトミック実行で、ストアドプロシージャではないが同じ理由で Infrastructure に置く。
  - `dbContext.Database.SqlQuery<T>()` は、LINQ で合成していない（`Where`/`OrderBy`/`Take` 等を後付けしていない）スカラークエリに限り、生SQLをそのまま1コマンドとして発行する（EF Core が派生テーブルに包まない）。`UPDATE ... OUTPUT` のように「派生テーブルの中では書けない」文を実行する場合は、必ず `ToListAsync()` で受け取ること。`FirstOrDefaultAsync`/`SingleAsync`/`Take(1)` 等を使うと LINQ 合成と判定され、EF が派生テーブルに包んでしまい SQL Server が実行時エラーを返す（コンパイルは通るため気付きにくい）。実装例は `SlipNumberSequenceCommand.IncrementAsync` を参照。
- ストアド名には `usp_` を付ける（`docs/database-schema.md`）。追加・変更したストアドは DDL と同様に `scripts/` へ連番 SQL として残す（採番のような生SQLで、テーブル構造の変更を伴わないものは対象外）。
- **SQL ビューは使わない**（`docs/database-schema.md` の元帳方針と同じ理由）。

## 8. 非同期処理の方針

- **DB アクセスは必ず非同期。** `ToListAsync()` / `FirstOrDefaultAsync()` / `SaveChangesAsync()` を使う。同期版は使わない。
- **`.Result` / `.Wait()` / `.GetAwaiter().GetResult()` は禁止。** UI スレッドでのデッドロックを招く。
- **`async void` は禁止**（例外を捕捉できずプロセスが落ちる）。ViewModel のコマンドハンドラは `async Task` にする。WPF のイベントハンドラのみ例外として許容する。
- **`ConfigureAwait(false)` は付けない。** 上記のとおり同期待ちを禁止するためデッドロックは起きず、付けても効果はわずかな性能差にとどまる。全 `await` に付ける手間と読みにくさに見合わない。**付ける／付けないを混在させないことが重要**なので、統一して付けない。
- **`DbContext` はスレッドセーフではない。** 同一の `DbContext` に対する複数の操作を `Task.WhenAll` で並行実行してはいけない。並行させたい場合はスコープを分ける。
- **`CancellationToken` を実際に呼び出し元から渡して積極的にキャンセルさせるのは、当面は長時間処理（請求締め・月次締め・帳票の一括発行）にのみ限定する。** 単票の登録・参照でキャンセル導線をUIに設けることはしない（実装量に対して得るものが小さい）。ただし Application 層の非同期メソッドは `CancellationToken cancellationToken = default` を引数に持たせることを既定のシグネチャとする（`SalesService`／`OrderService`等、TODO.md 4-1以降の全サービス）。値を渡さず既定値で呼ぶだけなら追加コストがなく、将来キャンセル導線を追加する際にシグネチャ変更が要らないため。
- 1秒を超える可能性のある処理は進捗表示を出す。共通の仕組みは Phase 0-3 で用意する。

## 9. 楽観的排他制御（rowversion）の適用単位

**rowversion は全テーブルの各行に持たせる。** 売上・入金・受注は明細行1テーブル構成（`docs/database-schema.md`）なので、**物理的な適用単位は明細行**になる。

ただし業務上の編集単位は「伝票（同一伝票番号の明細行の集合）」であり、行単位の rowversion だけでは次の2つを検出できない。

1. 自分が変更しなかった明細行を、他のユーザーが変更した
2. 他のユーザーが同じ伝票に明細行を追加・削除した

そのため伝票の訂正では、以下を Application 層の共通処理として実装する。

- **読み込んだ明細行すべてを更新対象に含める。** 値が変わっていない行も rowversion の照合を受けるようにし、上記1を検出する。
- **保存時に伝票の明細行の集合を再取得し、読み込み時点と一致することを確認する。** 一致しなければ他のユーザーが追加・削除したと判断して更新を中止し、上記2を検出する。
- この2つは伝票種別ごとに書かず、**共通処理として1箇所に実装する**（Phase 5 で実装。
  `src/bmcs_app.Application/Common/SlipConcurrencyGuard.cs`。`EnsureLineSetUnchanged` が上記2、
  `TouchAll`（監査列を更新するだけで EF Core が該当行を Modified とマークする）が上記1を担う。
  競合は `SlipConcurrencyException` に変換する。TODO.md 5-6 で `SalesService.UpdateAsync`／
  `CancelSlipAsync` から利用）。

その他:

- **マスタは1レコードが編集単位なので、rowversion がそのまま機能する。** 追加の考慮は不要。
- **請求データ・月次締めは rowversion だけに頼らない。** 「確定済みのものを再確定しない」「解除済みのものを再解除しない」といった状態遷移の前提条件を、更新前に必ず確認する。rowversion は同時更新を検出するだけで、業務的に不正な遷移は防げない。
- 競合を検出したときは、画面に「他のユーザーが更新しました。再読み込みしてください」と表示し、**自動マージや後勝ちでの上書きは行わない**（金額が静かに壊れることを避ける）。
- **派生（キャッシュ）列の再計算では `TouchAll` を使わない（TODO.md 7-1）。** `TouchAll` は
  ユーザーが伝票を編集するときに「自分が変更しなかった行を他人が変更した」を検出するための
  意図的な rowversion 照合強制であり、対象は編集中の1伝票に限られる。一方、消込キャッシュ列
  の再計算（`SettlementService.RecalculateForBillingGroupAsync`）は得意先の全売上・全入金行に
  及ぶ派生更新であり、ここで全行を Modified にすると、再計算のたびに無関係な伝票を編集中の
  別ユーザーが不要に弾かれる。**値が実際に変わった行だけを Modified にする。** 真に競合すべき
  ケース（同一 `billings` への2つの入金の同時登録）は、両方が実際に `settled_amount` を書き換え
  るため、変更行だけを対象にしても rowversion で正しく検出される。

## 10. 命名・フォルダ規約

機能フォルダ名は全層で統一する: `Order`（受注）/ `Sales`（売上）/ `Billing`（請求）/ `Receipt`（入金）/ `Ledger`（元帳）/ `Closing`（月次締め）/ `Master`（マスタ）/ `Search`（データ検索）/ `Common`（共通）

| 層 | 規約 |
|---|---|
| Presentation | `Views/{機能}/{画面名}Window.xaml`、`ViewModels/{機能}/{画面名}ViewModel.cs`。モーダルは `{名前}Dialog` |
| Application | `{機能}/{ユースケース名}Service.cs` |
| Domain | `Entities/`、`Enums/`、`Calculations/`（消費税計算など）、`Numbering/`（伝票番号の書式化、TODO.md 4-1） |
| Infrastructure | `BmcsDbContext.cs`、`Configurations/{エンティティ名}Configuration.cs`、`StoredProcedures/`、`LocalSettings/`、`Numbering/`（採番の生SQL、TODO.md 4-1） |

- C# のクラス名・プロパティ名は PascalCase、DB のテーブル名・カラム名は `snake_case`（`docs/database-schema.md`）。変換は Infrastructure 層のマッピング設定で行う。
- 画面の正式名称は `docs/design_document.md` の画面一覧を正とする。

### エンティティ・マッピングの実装方針（Phase 1-6 で確定）

- **PascalCase ↔ snake_case の変換は `EFCore.NamingConventions`（`UseSnakeCaseNamingConvention()`）に任せる。** 列ごとに `HasColumnName` を書かずに済む。ただし**数字を含む列名**（`address1`／`address2` 等）は自動変換が意図通りにならないため、該当プロパティのみ `HasColumnName` を明示する。
- **テーブル名には `ToTable()` を必ず明示する。** DB のテーブル名は複数形の `snake_case`（`docs/database-schema.md` 2.0節）だが、`ToTable()` を省略すると DbSet プロパティ名からの命名変換に暗黙に追従してしまう。DDL を正としテーブル名の変更が DbSet 名の変更で意図せず連動しないようにするため、常に明示する。
- **ナビゲーションプロパティは持たせない。** 得意先元帳はアプリ側 LINQ で複数テーブルをマージする方針であり、`Include()` によるナビゲーション経由の結合を使わないため。リポジトリを作らない方針と同様、使わない抽象化を先回りして作らない。
- **ただし FK 関係は `HasOne<TPrincipal>().WithMany().HasForeignKey(...)` で登録する（ナビゲーションプロパティなしで）。** これを省略すると、複数エンティティを同一 `SaveChangesAsync()` で保存したときに EF Core が依存関係を解決できず、INSERT 文の順序が（観測した限りでは）テーブル名のアルファベット順になり、FK 制約違反を起こす。DB 側にすでに存在する FK 制約（Phase 1-5）と対になる形で、全 FK 関係を登録する。
- **監査列は共通基底クラスで重複を排除する。** `TrackedEntity`（`CreatedBy`/`CreatedAt`/`UpdatedBy`/`UpdatedAt`）と、これを継承し `IsDeleted`/`RowVersion` を追加する `AuditableEntity` の2段構成。監査列の Fluent API 設定は共通拡張メソッド（`Configurations/AuditableEntityConfigurationExtensions.cs`）に集約する。**旧 `TaxUnitConfigurationExtensions.cs`（`BillingTaxUnitBase` 等の税単位別共通基底クラス向け拡張）は、`sales`/`receipts`/`billings` の統合（2026-09-08、`docs/database-schema.md` 2.8節）に伴い削除した。** 統合後は `Sales`/`Receipt`/`Billing` それぞれに派生クラスがなくなり、共有すべき基底クラスが存在しないため。
- **`decimal` は `HasPrecision(p, s)` を必ず明示する。** 省略すると既定精度（18,2）になり、`decimal(15,4)` の単価カラム等で桁落ちする。
- **`varchar`/`char` 列は `.IsUnicode(false)` を明示する。** 省略すると EF Core が `nvarchar` パラメータを送り、SQL Server 側で暗黙変換が発生してインデックスを使えなくなる（コード系カラムは PK/FK で全 JOIN に絡むため実害が大きい）。
- **`date` 型は C# `DateOnly` にマッピングする。** EF Core 8+ のネイティブ対応。時刻成分を持たせないことでバグを防ぐ。

## 11. MVVM の実装方針

- **`CommunityToolkit.Mvvm` を使う。** `ObservableObject` や `RelayCommand` を自作しない。
- **ViewModel は View を参照しない。** 別ウィンドウを開く操作は、Presentation 層内のウィンドウ管理サービス経由で行う（Phase 0-3 で実装）。ViewModel が `new SalesWindow()` を書かない。
- **入力の書式（日付・金額のカンマ区切り等）と操作性（Enter でのフォーカス移動等）は共通のスタイル・ビヘイビアで実現する**（Phase 0-5、0-6）。画面ごとに個別実装しない。
- **入力値の形式チェックは ViewModel、業務ルールの検証は Application 層。** 「数値が入っているか」は ViewModel、「この得意先にこの税区分は登録できるか」は Application で判定する。
- **伝票明細行の ViewModel は画面ごとに複製しない。** `ViewModels/Common/SlipLineViewModel.cs`（TODO.md 4-2）を受注入力・売上入力で共用する。行1つに対する UserControl（`Views/Common/SlipLineControl.xaml`）も同様に共用する。

## 12. 共通UIスタイル（Phase 0-5、暫定設定）

MahApps.Metro を導入し、共通スタイルは `src/bmcs_app/Styles/`、書式整形は
`src/bmcs_app/Behaviors/FormattedTextBoxBehavior.cs`（金額・数量）・`DatePickerInputBehavior.cs`（日付）
に集約する（画面ごとに実装しない。11章参照）。
配色・書式の具体値は `docs/product-spec.md` に指定がないため、以下は**暫定**（実機確認後に変更可）。

- **テーマ**: `Light.Blue`（MahApps 標準）。`App.xaml` で `Controls.xaml` / `Fonts.xaml` /
  `Themes/Light.Blue.xaml` を静的にマージする。実行時のテーマ切替は行わないため `ThemeManager` は使わない。
- **フォーカス中の背景色**: `Styles/Colors.xaml` の `FocusedInputBackgroundBrush`（`#FFFFF3C4`）。
  `TextBox` の暗黙スタイルに適用され、全画面で共通に効く。
- **ウィンドウ切替アニメーション**: 全ウィンドウで無効化する（`WindowTransitionsEnabled=False`）。
  **各ウィンドウの XAML ルート要素に個別指定する。** 当初は `CommonControlStyles.xaml` の
  `MetroWindow` 暗黙スタイル（`BasedOn` 有無いずれも）で一括設定を試みたが、実機確認したところ
  `WindowTransitionsEnabled` の実効値が常に既定の `True` のままになり、効かないことが判明した
  （原因は MahApps.Metro 側の内部実装によるものと推測されるが特定できていない）。
  ローカル値（XAML 属性での直接指定）は WPF のプロパティ値優先順位で最上位のため、
  スタイル解決の仕組みに依存せず確実に効く。画面を追加する際は、各ウィンドウのルート
  `mah:MetroWindow` 要素に `WindowTransitionsEnabled="False"` を必ず付ける。
- **日付入力（2026-09-15改訂）**: 標準 `DatePicker`（`MahApps.Styles.DatePicker` を暗黙適用。全画面デザイン
  統一）を使う。**当初は「カレンダーピッカーは導入しない」「日付を保持する ViewModel プロパティは
  `DateOnly` ではなく `string` で持つ」としていたが、これを覆した。** 旧方針は「必要とする画面が具体化
  した時点で追加を検討する」という条件付きの記述であり、今回の「全画面デザイン統一」（TextBox・
  ComboBox・DatePicker の高さを揃える）がその契機になった。`string` 保持を選んでいた理由（`DateOnly` に
  既定の `TypeConverter` がなく `TextBox.Text` との双方向バインディングが失敗する）は、`DatePicker.SelectedDate`
  （`DateTime?`）への直接バインドには当てはまらないため、**日付を保持する ViewModel プロパティは
  `DateTime?` で持つ**（`DateOnly?` ではなく `DateTime?` なのは `DatePicker.SelectedDate` の型に合わせるため。
  時刻成分は必ず `00:00:00` に揃える）。
  - 8桁ベタ打ち（`20260101` 等）の入力速度を維持するため、`beh:DatePickerInputBehavior.IsEnabled` を
    暗黙 `DatePicker` スタイルに一律設定する（`src/bmcs_app/Behaviors/DatePickerInputBehavior.cs`）。
    内部 `PART_TextBox` の入力を Enter確定／フォーカス離脱時に `yyyy/MM/dd` へ正規化し、
    解釈できない入力は直前の `SelectedDate` の表示へ巻き戻す（値の妥当性検証はしない。11章）。
    `FormattedTextBoxBehavior` と異なりオプトインにしない（13章の「暗黙スタイルで全画面自動適用しない」
    方針の例外。DatePicker は日付専用コントロールで誤爆しないため）。
  - `FrameworkElement.Language` を `App.xaml.cs` の `OnStartup` で `ja-JP` に固定する。`DatePicker` は
    `Language` 由来のカルチャで表示書式（`ShortDatePattern`）を決めるため、設定しないと `9/15/2026`
    表記になり「日付は `yyyy/MM/dd` で統一」が崩れる。
  - 読み取り専用は `mah:ControlsHelper.IsReadOnly`（`DatePicker` に `IsReadOnly` プロパティは無い）。
    `IsEnabled` は使わない（グレーアウトしタブ順から外れるため、読み取り専用でもフォーカス・コピー可能
    という現状の挙動が変わる）。
  - 旧 `DateTextBoxStyle`／`FormattedTextBoxBehavior.FormattedTextBoxKind.Date` は削除済み。
- **金額・数量書式（2026-09-16確定）**: `AmountTextBoxStyle` / `QuantityTextBoxStyle`。表示はカンマ区切り・
  右揃え、フォーカス中はカンマなしの生数値、blur時に `N{DecimalPlaces}` で再整形する。
  **画面上の数量・金額等の数値は小数点以下を表示しない（`DecimalPlaces`既定値0、`N0`書式による
  四捨五入＝`MidpointRounding.AwayFromZero`）。** 単価等の内部精度（`decimal(15,4)`等）はそのまま
  保持し、画面表示のみ丸める。旧「既定値0は暫定」の記述は撤回し、上記を正式仕様とする。
  帳票（納品書・請求書・明細請求書）も数量は同様に0桁（`N0`）に統一する。ただし**単価は帳票側のみ
  現状の`N4`（小数4桁）を維持する**（画面の単価欄はマスタ入力用の丸め表示、帳票の単価は明細金額の
  根拠を示す表示という役割の違いによる。2026-09-16、ユーザー判断で単価は据え置きと確定）。
  対象: `DeliveryNoteDocumentBuilder`／`InvoiceDocumentBuilder`／`DetailInvoiceDocumentBuilder`の
  `Quantity.ToString("N0")`（変更済み）、`UnitPrice.ToString("N4")`（変更なし）。
- 数値として解釈できない入力はそのまま残す（このビヘイビアは書式のみを担当し、値の妥当性検証は
  ViewModel の責務とする。11章）。
- **入力欄の高さ統一（2026-09-15、全画面デザイン統一）**: `TextBox`/`ComboBox`/`DatePicker` の暗黙スタイルに
  共通の `MinHeight`（`Styles/Metrics.xaml` の `InputControlHeight`、実測で確定）と
  `VerticalAlignment="Center"`・`VerticalContentAlignment="Center"` を設定する。MahApps 既定では
  `ComboBox` の自然高が `TextBox` より大きく、`RowDefinition` 未指定の `Grid`（子が `Stretch`）に混在させると
  行高が `ComboBox` に引っ張られて画面ごとに高さがバラつく。`VerticalAlignment=Center` で各コントロールを
  行高から独立させ、`MinHeight` を3種の自然高の最大値以上に取ることで高さを一致させる。`Button` にも
  `MinHeight` のみ適用する（`ComboBox`/`DatePicker`/`Button` が横一列に並ぶ画面向け。`VerticalAlignment=Center`
  は付けない）。

## 13. 共通キーボード操作ビヘイビア（Phase 0-6）

11章で予告した「Enter でのフォーカス移動」「検索モーダル・明細一覧の行選択・確定」は、
以下の2つの添付ビヘイビア（`src/bmcs_app/Behaviors/`）で実現する。12章と異なり、
配色等の暫定値ではなく実装方針そのものなので確定事項として記載する。

- **`EnterKeyNavigationBehavior`**: `IsEnabled` を画面ルート要素に付けると、`TextBox` にフォーカスが
  ある状態での `Enter` を次項目へのフォーカス移動として扱う（`Tab` と同じ挙動）。`TextBox` 以外
  （`Button` 等）では反応しないため、既定の `Enter` 挙動と衝突しない。
- **`RowActivationBehavior`**: `Command` を `ListBox`/`ListView`/`DataGrid` 等の `Selector` 系コントロールに
  付けると、行選択中に `Enter` を押したときに選択中の行を引数としてコマンドを実行する。
  上下矢印キーによる行選択自体は `Selector` の既定動作であり、追加実装はしていない。
  「転記」の具体的な処理（選択行のデータをどこにどう反映するか）は画面固有のコマンド実装に委ねる。
- **両ビヘイビアともオプトイン。** `FormattedTextBoxBehavior` と同様、暗黙スタイルによる全画面自動適用は
  行わない（ダイアログ等への意図しない波及を避けるため）。使う画面のルート要素・対象コントロールに
  明示的に設定する。
  - **例外: `DatePickerInputBehavior`（2026-09-15追加）は暗黙 `DatePicker` スタイルで全画面一律適用する。**
    `DatePicker` は日付専用コントロールであり、キー操作の意味を変える上記2ビヘイビアと異なり書式整形
    （`FormattedTextBoxBehavior` と同種）のため、12章と同じ扱いにする。誤爆の心配がなく、XAML側に
    書き忘れる余地をなくす利点を優先した。

## 14. 現在操作中の社員コードの解決（ICurrentEmployeeContext、Phase 0-7 で確定）

全テーブルの監査列（`created_by`/`updated_by`、M-12）は社員コードを必須で持つ。
Phase 2-1（得意先マスタ画面）で監査列への書き込みが最初に発生した時点では起動時パラメータの受け取りが
未実装だったため、`src/bmcs_app.Application/Common/ICurrentEmployeeContext.cs`（`EmployeeCode` プロパティのみ）
という最小限の抽象を先に導入し、暫定実装 `PlaceholderCurrentEmployeeContext` が固定の `EMP001` を返していた。

**2026-09-14、Phase 0-7 で `StartupArgsCurrentEmployeeContext` に差し替えた。** ショートカット引数の
1つ目を社員コードとして扱う（`App.xaml.cs` の `OnStartup` が `e.Args` を受け取り、
`ApplicationServiceCollectionExtensions.AddApplication(services, configuration, startupArgs)` へ渡す）。
引数が渡されない場合（IDEからの起動等）は開発用に `EMP001` を既定値とする。インターフェースの形は
変えていないため、`ApplicationServiceCollectionExtensions` 側の登録を1箇所差し替えるだけで完了した。

社員コードの実在確認・権限レベルの取得は、ここでは行わない。実際に使う側（メインメニュー画面、
Phase 2-7）が `EmployeeService.GetByCodeAsync` を通じて解決する。存在しない社員コードが渡された場合は
権限レベル0（メニュー項目が1つも表示されない）として扱う。

## 15. 単体テストの構成（Phase 5-1 で構築）

TODO.md X-1（テスト方針の決定と単体テスト基盤の構築）のうち、基盤構築部分は
Phase 5-1（消費税計算）着手時に前倒しで構築した。

- **対象は Domain の純粋ロジックのみ。** 2章で定めたとおり「DB に依存しない単体テストの対象は
  Domain に集める（消費税計算・端数処理・状態判定）」。消込・締めなど DB アクセスを伴う処理は、
  開発用DBに対する結合テストで別途担保する（16章）。
- **構成**: `tests/{テスト対象アセンブリ名}.Tests/`（例: `tests/bmcs_app.Domain.Tests/`）。
  テスト対象への `ProjectReference` のみを持つ。
- **フレームワークは xUnit v2。** `dotnet new xunit` の既定構成（`Microsoft.NET.Test.Sdk` /
  `xunit` / `xunit.runner.visualstudio` / `coverlet.collector`）をそのまま使う。
- **`Directory.Build.props` のカーブアウトは作らない。** ルートの設定（`Nullable`/`ImplicitUsings`/
  `WarningsAsErrors=nullable`）がそのまま継承される。テストプロジェクトの csproj には
  `TargetFramework`（Domain と同じ `net10.0`、`-windows` は付けない）とテスト固有の設定のみ書く。
- **実行**: `dotnet test tests/bmcs_app.Domain.Tests/bmcs_app.Domain.Tests.csproj`
  （`bmcs_app.sln` 単位で実行すると WPF の `net10.0-windows` プロジェクトを不要に巻き込むため、
  プロジェクト単位で指定する）。
- **テストメソッド名は日本語。** 完了条件が業務正確性（金額が手計算と一致するか）であり、
  非エンジニアでも行列の網羅性を確認できるようにするため。

## 16. 結合テストの構成（Phase 4-1 で構築）

15章「消込・締めなど DB アクセスを伴う処理は、開発用DBに対する結合テストで別途担保する」を
はじめて実体化したもの。伝票番号の採番（TODO.md 4-1「同時登録でも採番が重複しない」）の
実証に使い、以降の消込（7-1）・締め（6-1・9-1）の結合テストもここに集約する想定。

- **構成**: `tests/bmcs_app.Application.Tests/`。単一アセンブリの単体テストではなく、
  Application・Infrastructure・実機 SQL Server を縦串で検証するため、15章の
  `tests/{アセンブリ名}.Tests/` とは性質が異なる（**開発用DBへの接続が前提で、繋がらなければ
  落ちる**）が、命名は既存規約を踏襲する。
- **DI配線ごと検証する。** `ApplicationServiceCollectionExtensions.AddApplication` を通して
  サービスを解決する（`DevDatabaseFixture`、`tests/bmcs_app.Application.Tests/DevDatabaseFixture.cs`）。
  モックは使わない。
- **接続文字列**: `src/bmcs_app` と同じパターン。`appsettings.Development.json.sample`
  （コミットする）を複製して `appsettings.Development.json`（`.gitignore` の既存パターンが
  深さを問わず一致するため自動的に除外される）を作り、`Password` を記入する。
- **テスト分離**: 実キー（`orders` 等）の `current_value` は変更しない。
  - 並列採番の重複・欠番なしを検証するテストのみ、`"__test_slip_number"` という
    業務キーと衝突しない使い捨てキーを使い、コミットする（変化することが検証対象のため）。
    フィクスチャが `InitializeAsync`/`DisposeAsync` で冪等に作成・削除する。
  - それ以外のテストは実キーを使うが、`BeginTransactionAsync` → 検証 → `RollbackAsync` で
    DBを無変化に保つ。
  - `DbContext` はスレッドセーフでない（8章）ため、並列実行するテストは
    タスクごとに独立したスコープ（＝独立した `DbContext`）を使う。
- **実行**: `dotnet test tests/bmcs_app.Application.Tests/bmcs_app.Application.Tests.csproj`
  （`bmcs_app.sln` 単位で実行しても動くが、15章と同様プロジェクト単位を基本とする）。
