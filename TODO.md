# bmcs_app 開発タスク

販売管理システムを新規プロジェクトとして1から構築するためのタスク一覧。設計資料は `CLAUDE.md` / `docs/product-spec.md` / `docs/design_document.md` / `docs/database-schema.md`、非自明な決定とその理由は `docs/decisions.md`、設計上の残課題は下記「保留項目の扱い」と X-3 を参照する。

- 各タスクの **推奨モデル** は、そのタスクを実行する際に選択すべきモデル（`/model` で切り替え）。**ただし Phase 2-2 完了以降は、推奨モデルに関わらずすべて Sonnet 5 で実行する。**
- **前提** 列に課題ID（C-x／M-x 等）が入っているタスクは、下記「保留項目の扱い」の**暫定設定に従って進める**（回答待ちにしない）。ただし **「要決定」に挙げた課題だけは、回答が出るまでそのタスクに着手できない**。
- **完了状態はすべて md のチェックボックス（`[ ]` / `[x]`）で管理する。** 絵文字・打ち消し線は使わない。

---

## モデル選択の基準

| | Opus | Sonnet |
|---|---|---|
| 使う場面 | 設計判断を伴う／複数テーブル・複数画面に影響が波及する／金額計算やトランザクション整合性が絡む | 方針が確定しており、既存パターンの適用・定型実装で済む |
| 具体例 | ソリューション骨格、DBスキーマ設計、消費税・端数処理、入金消込、請求締め、状態遷移の整合更新、元帳のマージ、採番の同時実行制御 | マスタCRUD画面、XAML・スタイル・ビヘイビア、一覧／検索画面、EFエンティティの機械的作成、DDLの適用、テストデータ生成 |
| 判断に迷ったら | **間違えたときの影響が広いなら Opus。** 特に「金額が狂う」「データ整合が壊れる」種類のタスクは必ず Opus | 画面単位で閉じており、間違えても再実装が容易なら Sonnet |

補足: Phase 2-2 からはすべて Sonnet 5 で実装。金額計算・消込・締めに関わるタスク完了後は、**業務正確性を確認するための手計算テストが重要**（フェーズ末のレビュータスク X-1 で単体テストを実装）。

---

## 保留項目の扱い

設計上の残課題は、**決まらないと作れないもの（下記「要決定」）を除き、暫定設定を採用してそのまま実装する。**

将来の差し替えを見据えた抽象化・インターフェース化は**行わない**。方針が変わった時点で該当箇所を直接修正する。過剰設計を避け、実装量を最小に保つため。

ただし、**採用した暫定設定は必ず `docs/` に「暫定」と明記して記録する**（実装だけが知っている状態にしない）。これは抽象化の代わりに払う唯一のコストであり、後から何を直せばよいかを追跡可能にするため。

### 要決定（暫定では進められない）

| 決定 | 課題 | 必要な時期 | 内容 |
|---|---|---|---|
| [x] | M-10 帳票エンジン | **Phase 10 着手前** | **2026-09-10 確定。** WPFの `FixedDocument`/`FixedPage` をC#コードで直接組み立てる方式（外部帳票ライブラリ不使用）。印刷は `PrintQueue`、PDF保存は都度「Microsoft Print to PDF」へ出力（ダイアログで保存先選択）。詳細は `docs/report-spec.md` |
| [x] | C-5 正式なライブDBの接続先 | **2026-09-03 解消** | `172.16.3.171` の `bmcs_db`（SQL Server 2022 Express）。接続確認済み。詳細は `docs/architecture.md` 2.5章 |

### 暫定設定で進めるもの

「決定」列の `[x]` はユーザーに業務確認済み、`[ ]` は対応方針としては採用済みだが業務確認がまだのもの（回答が出たら該当箇所を直接修正する）。

| 決定 | 課題 | 採用する暫定設定 |
|---|---|---|
| [x] | C-2 明細入金テーブルの二重定義 | **2026-09-03 確定（暫定ではない）。** 明細入金は `detail_payment` の1テーブル。`PaymentTaxUnitLine` は作らない。税単位を表す `Line` と業務概念を表す `Detail` を使い分ける |
| [x] | M-5 受注テーブルの構成 | **2026-09-03 決定済み。** 売上・入金と同じ**非正規化（明細行1テーブル構成）**に統一する。受注はジャーナル性を持たない仮伝票だが、テーブル構成の一貫性を優先する。`sub_customer_id` も受注明細行に持たせる |
| [ ] | M-2 採番規則 | 年度リセットなしの通し連番。採番テーブル方式。**表記は接頭辞なし・8桁ゼロ埋め（`00000001`〜）、伝票種別ごとに独立した連番、欠番なし（2026-09-08 決定、4-1で実装）。桁数は varchar(20) の範囲で余裕を持たせる** |
| [x] | M-3 単価決定ロジック | **2026-09-10 確定（例外的にスタブ・ドライバ実装とする）。** 将来的に掛け率マスタ等の実装を予定しているため、単価決定ロジックを差し替え可能なインターフェース（ドライバ）として設計し、現時点はシンプルな実装（商品マスタの外税単価／内税単価を初期値として転記、手入力で上書き可。単価計算マスタは作らない）を差し込む。**本方針の「将来の差し替えを見据えた抽象化はしない」という既定ルールの例外として、この項目に限り明示的に採用**（2026-09-10ユーザー確認）。掛け率マスタ実装時はドライバの差し替えのみで対応する想定。**2026-09-10、`UnitPriceSelector`（Phase 4-2実装）を`IUnitPriceCalculator`／`StandardUnitPriceCalculator`（DI登録、`ApplicationServiceCollectionExtensions`）へ refactor 済み** |
| [x] | M-4 原価の取得元 | **2026-09-10 確定（暫定ではない）。** 仕入機能がスコープ外のため、単純に商品マスタの標準原価（`standard_cost_price`）を転記する。最終仕入原価・移動平均等への対応は、将来仕入機能を実装する際に再検討する |
| [x] | C-6 訂正・取消方式 | **2026-09-10 確定（暫定ではない）。2026-09-14 Phase 6-5レビューで4条件に改訂。** 元伝票を直接修正する（赤伝方式は採用しない）。ジャーナル系テーブル（`sales`／`receipt`／`detail_receipt`／`order_slip`）が修正不可になるのは次の4条件のいずれかに該当する場合のみ、それ以外は上書き可能。①請求締め（紐づく`billing`が確定済み）②明細請求書発行済み（都度得意先の対象行が`detail_invoice_sales_line`に連携済み。当初「ロック条件に含めない」としていたが、発行済み明細請求書ヘッダーが実データと乖離する不具合が見つかったため追加）③月次締め（該当年月の`monthly_closing`が確定済み）④入金済み（`sales.settlement_status`=消込完了、または`receipt`／`detail_receipt.allocation_status`=充当完了）。`order_slip`はいずれの条件にも該当しないため常に直接修正可能 |
| [ ] | C-4b インボイス端数処理 | 得意先マスタの税区分どおり（請求単位／伝票単位／明細単位）に計算する |
| [x] | C-7 担当者の定義 | **2026-09-10 確定（Phase 1-1で採用した解釈が正しいと確認済み）。** 得意先マスタに「得意先担当者名（得意先側で自社を担当している窓口、文字列）」と「営業担当社員コード（自社で得意先を担当している社員、社員マスタFK）」を別項目で持つ。月次締めの担当者別集計は後者を使う |
| [x] | C-8 権限マトリクス | **2026-09-10 確定（暫定ではない）。** 権限マトリクスのテーブルは作らず、メニュー単位の権限レベル判定のみとする。**締め解除等、画面内で必要権限に差が出る操作は、同一画面のアクションにせず別画面（別メニュー項目）として分離する。** これにより画面内アクション単位の権限判定コードは不要になる（メニュー単位の権限レベル判定だけで足りる）。締め解除画面は6-2・9-3で実装し、6-1・9-1とは別ウィンドウにする |
| [x] | C-9 宛名 | **2026-09-10 確定（暫定ではない）。** 都度書き換え方式に一本化する（子得意先マスタは作らない）。ジャーナル系の画面（受注・売上・入金・明細請求書等）は、得意先コードで検索した後、名称欄を手入力で上書き修正できるようにする。学校－学年－クラスのような階層を持つ得意先も、マスタ上は常に一つの得意先として扱う。`sub_customer_id`カラムは実質使い道がなくなったが、将来の別要件に備えて**列は残す**（2026-09-10確認済み。削除しない） |
| [x] | M-8 税区分の網羅性 | **2026-09-10 確定（暫定ではない）。** インボイス制度上の理由により、請求単位・伝票単位（締め得意先）は常に外税、内税明細単位（都度得意先）は常に内税に限定する（既存実装どおり）。残り3パターン（内税×請求単位／内税×伝票単位／外税×明細単位）は業務上発生しないため扱わない |
| [x] | M-9 返品・値引 | **2026-09-10 確定（暫定ではない）。** マイナス数量・マイナス金額の伝票とし、`sales.slip_type`（1=売上／2=返品／3=値引）で識別する。**伝票区分は明細行ごとに選択**（同一伝票内に売上行と値引行が混在できる）。値引の原価は常に0、返品は商品原価をそのまま使う。返品・値引行を受注に紐付けることはできない。詳細は`docs/product-spec.md`共通業務ルール10・`docs/design_document.md` 8章 |
| [x] | M-11 リアルタイム残高 | **2026-09-10 確定。** 都度集計する（残高キャッシュ列は持たない）。性能問題が出た場合に残高キャッシュ列を追加する |
| [x] | M-12 監査列 | **2026-09-10 確定（暫定ではない）。** 全テーブルに 作成者・作成日時・更新者・更新日時 を持つ。操作ログの別テーブルは作らない |
| [x] | M-14 手数料差額のしきい値 | **2026-09-10 確定（暫定ではない）。** 手数料差額は手入力とし、自動計算・自動補正提案は行わない（`receipt`／`detail_receipt`の`fee_adjustment_amount`は手入力項目とする）。しきい値・設定ファイルは不要 |
| [x] | M-15 明細行数の上限 | **2026-09-15解消（暫定ではない）。** 帳票エンジンが自動改ページ（`ReportPagination`）に対応したため、明細行数の上限は設けない。`docs/report-spec.md` 2-1節 |
| [x] | M-17 削除方針 | **2026-09-10 確定（暫定のまま確定）。** マスタは論理削除（削除フラグ）、伝票は物理削除しない |
| [x] | M-18 認証方式のリスク | **2026-09-10 確定（暫定のまま確定）。** 社内クローズド環境での簡易識別として**容認する**。Windows認証の併用は行わない |
| [x] | M-6 残件（担当者別集計の保存） | **2026-09-03 決定、2026-09-09 範囲を担当者別集計のみに限定。** 担当者別売上・粗利は保存せず都度集計する（月次締め確定後は該当年月の伝票が編集不可になるため、都度集計でも確定時と同じ値が再現される。性能問題が出た場合に集計結果テーブルを追加する）。**得意先別の月次売掛残高は`monthly_closing`に保存する方式へ変更**（`tax_unit=1`の未確定区間の税額を都度計算では確定できないため。`docs/database-schema.md` 1章・2.16節） |

---

## Phase 0: 開発基盤

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 0-1 | ソリューション・プロジェクト骨格の作成（4層構造＝4プロジェクト、機能はフォルダ分割、層間の依存方向の固定） | **Opus** | — | ビルド成功、参照方向が意図どおり、WPFアプリが起動し2プロセス同時起動も確認済み。詳細は `docs/architecture.md` を参照 |
| [x] | 0-2 | `docs/architecture.md` への追記（各層の責務、トランザクション境界、EF Core/ストアドの使い分け、非同期方針、rowversionの適用単位、命名規約、MVVM方針）。P-6 の推奨(a)〜(e)すべてを反映 | **Opus** | P-6 | 5〜11章として追記済み。実装時の判断が資料だけで付く粒度になっている |
| [x] | 0-3 | 共通基盤の実装（DIコンテナ、`CommunityToolkit.Mvvm`、DbContextのウィンドウ単位スコープ登録、ウィンドウ管理サービス、例外ハンドリング、ロギング、進捗表示） | **Opus** | — | 実機DBへの非同期アクセス・スコープ分離（2ウィンドウで別インスタンス）・例外ハンドラ・ログ出力をすべて実証済み |
| [x] | 0-4 | 開発用DBの接続確認（接続文字列の外部化、`appsettings.Development.json` は git 管理外） | Sonnet | C-5 | `172.16.3.171` / `bmcs_db` への接続確認済み。DDL適用は 1-5 で実施 |
| [x] | 0-5 | MahApps.Metro テーマ適用と共通スタイル（日付 `YYYY/MM/DD`、金額・数量のカンマ区切り右揃え、blur時整形、フォーカス中の背景色） | Sonnet | — | サンプル画面で表示・入力書式が仕様どおり |
| [x] | 0-6 | 共通キーボード操作ビヘイビアの実装（Enterでの次項目フォーカス移動、上下矢印での行選択、Enterで確定・転記） | Sonnet | — | マウスを使わず入力を完結できる。誤送信しない |
| [x] | 0-7 | 起動時パラメータ（社員コード）の受け取りと権限判定の基盤 | Sonnet | C-8 | **2026-09-14実装（2-7の前提として先行実装。ユーザー確認済み）。** `ICurrentEmployeeContext`の実装を`PlaceholderCurrentEmployeeContext`（固定値）から`StartupArgsCurrentEmployeeContext`へ差し替え、`App.xaml.cs`の`OnStartup`が受け取る`e.Args`の1つ目を社員コードとして使う（引数なしは開発用に`EMP001`へフォールバック）。権限レベルの取得自体は2-7（メインメニュー画面）が`EmployeeService`経由で行う。詳細は`docs/architecture.md` 14章 |

---

## Phase 1: DBスキーマ構築

**このフェーズは全て Opus 推奨。** 後続の全機能がここに依存し、間違えたときの手戻りが最大になるため。

**このフェーズで決めきる範囲と、後続フェーズで追加してよい範囲を区別する。**

| | 内容 | 理由 |
|---|---|---|
| Phase 1 で**決めきる**（構造） | どのテーブルが存在するか（特に税単位別の分割）、主キー・外部キー、連携テーブルの粒度、状態カラム、監査列、rowversion | 後から変えるとデータ移行を伴う大手術になる。EF Coreのマイグレーションを使わない方針のため、構造変更のコストがさらに高い |
| 後続フェーズで**追加してよい**（属性） | 個々の属性カラム（備考欄、桁数の調整、単価計算マスタの詳細項目、帳票用の付加項目など） | `ALTER TABLE` 1本で済む。実装して初めて必要性が分かる項目を机上で作り込まない |

M-2（採番規則）・M-3（単価決定）・C-6（訂正・取消方式）は**属性の追加で吸収できる範囲**なので、暫定設定のまま Phase 1 を進めてよい。一方 **C-2（明細入金テーブルの二重定義）は構造の問題**なので、1-2 の中で確定させる。**M-5（受注テーブルの構成）は 2026-09-03 に決定済み**（売上・入金と同じ非正規化に統一）。

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 1-1 | マスタ系テーブルの定義（得意先・商品・社員・自社情報・銀行口座・メニュー構成の6テーブル。**単価計算マスタは M-3 暫定により作らない**） | **Opus** | C-7 | `docs/database-schema.md` 2章に定義済み。他文書が前提とする全カラムの存在を確認、`CK_customer_tax_unit_closing_day` で C-1 の相互制約を表現 |
| [x] | 1-2 | 売上・入金・請求データの税単位別テーブル定義（`SalesTaxUnit*` / `PaymentTaxUnit*` / `BillingTaxUnit*`）＋明細請求書・明細入金・売上明細との連携テーブル | **Opus** | C-2 | `docs/database-schema.md` 2.7〜2.13 に10テーブルを定義。C-2 を `detail_payment` で解消、C-3 も併せて修正。税率別内訳カラムと二重請求防止の一意制約を含む |
| [x] | 1-3 | 受注テーブルの定義（売上・入金と同じ非正規化＝明細行1テーブル構成で作成。M-5決定済み） | Sonnet | — | `docs/database-schema.md` 2.14 に `order_slip` を定義。状態遷移（未売上／一部売上／売上完了／中止）の4状態をカラムで保持できることを確認済み |
| [x] | 1-4 | 状態カラム・監査列・月次締めテーブルの定義（状態カラム・監査列は 1-1〜1-3 で各テーブルに定義済み。本タスクでは `monthly_closing` と `slip_number_sequence` を追加し、網羅性を検証） | **Opus** | — | `docs/database-schema.md` 2.17 に状態→カラムの対応表を作成し、**状態遷移の全18状態が保持できることを機械的に照合済み** |
| [x] | 1-5 | DDLを `scripts/` に連番SQLとして作成し、SQLCMDで開発用DBへ適用 | Sonnet | 1-1〜1-4 | `bmcs_db` に19テーブル・FK30件・CHECK制約34件を適用済み。C-1の相互制約が実データで拒否されることを確認。再実行安全性・他DB非影響も確認済み |
| [x] | 1-6 | EF Core エンティティ・マッピングの作成（PascalCase ↔ snake_case、rowversion、enum ↔ tinyint） | Sonnet | 1-5 | 全19テーブルへの書き込み・読み取りを実DBで確認済み。ナビゲーションプロパティなしで実装（`docs/architecture.md` 10章参照） |
| [x] | 1-7 | `docs/database-schema.md` の章構成の整理（P-2）。テーブル定義章は 1-1〜1-4 で追記していくため、ここでは命名規則・運用を分離して3章構成に整える | Sonnet | 1-1〜1-4 | 方針／テーブル定義／命名規則・運用／未確定のDB設計判断の4章構成に整理済み。命名規則とDDL運用ルールを1章から分離し、章内の重複記述も解消 |
| [x] | 1-8 | 開発用テストデータの投入スクリプト作成（締め得意先・都度得意先の両方、税区分3種を網羅） | Sonnet | 1-5 | `scripts/seed_dev_data.sql` を作成・適用済み。`product-spec.md` の全18状態と3税単位を実データで網羅。再実行安全性・他DB非影響を確認済み |

**1-2 の再構成（2026-09-08決定）**: 1-2 で作成した税単位別テーブル（`SalesTaxUnit*`＝3テーブル／`ReceiptTaxUnit*`＝2テーブル／`BillingTaxUnit*`＝2テーブル、計8テーブル）を、`sales`/`receipt`/`billing` の3テーブルに統合した。税単位は各テーブルの `tax_unit` カラムで表し、得意先マスタとの整合・請求データとの整合を複合FKで強制する。DDLは `scripts/010_unify_tax_unit_tables.sql`、EFエンティティ・設定・DbContext・`ProductHistoryQueryService`（唯一の税単位分岐箇所）も追従済み。経緯・設計は `docs/database-schema.md` 1章末尾・2.8〜2.14節を参照。Phase 4以降（受注入力〜）が未着手で開発DBに本番データがない時点での変更のため、データ移行は行わずDROP＆再作成、`scripts/seed_dev_data.sql` も統合後のテーブルに合わせて書き直した。

---

## Phase 2: マスタ管理

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 2-1 | 得意先マスタ画面（税区分×締日の整合バリデーション、登録後は締め区分・税区分を編集不可にする） | **Opus** | C-1, 0-5, 0-6 | 破綻する組み合わせが登録できない。既存得意先で締め区分・税区分が編集できない。**端数区分も同様に編集不可（5-1で追加、2026-09-08決定）** |
| [x] | 2-2 | 商品マスタ画面（税種別区分＝課税10%／軽減8%／非課税を持つ） | Sonnet | 0-5, 0-6 | 商品ごとの税種別が設定でき、売上明細へ転記できる |
| [x] | 2-3 | 社員マスタ画面（権限レベル） | Sonnet | C-8, 0-5, 0-6 | **2026-09-14実装。** 得意先マスタ・商品マスタ（2-1/2-2）と同じ「一覧を持たず、コード直接入力＋Enter読込、コード欄でSpace検索モーダル」のパターンで実装。`EmployeeService`（Application/Master）＋`EmployeeMasterViewModel`（`Views/Master/EmployeeMasterWindow`）＋専用の社員検索モーダル（`EmployeeMasterSearchDialog`、`ProductMasterSearchDialog`と同じ「マスタ全件ロード後にメモリで絞り込む」方式）。権限レベルは`menu.required_permission_level`と比較する数値のため固定選択肢を設けず、文字列入力（`PermissionLevelText`）を保存時に0〜255の数値として検証する構成にした（メニュー構成マスタ側の必要権限値が2-7でまだ未定のため）。実機（UI Automation）でseedデータEMP001のコード直接読込、新規登録の保存とDB反映、無効化（論理削除）のDB反映、Space検索モーダルからのEMP002選択・転記を確認済み。 |
| [x] | 2-4 | 自社情報マスタ画面（適格請求書発行事業者の登録番号・自社名・住所・代表者名・振込口座） | Sonnet | 0-5, 0-6 | **2026-09-14実装。** `company_info`は1レコード運用（`CompanyInfoId`固定値1）のため、得意先・商品・社員マスタ（2-1〜2-3）とは異なりコード検索・新規登録・無効化を持たない。プリンタ設定（2-6）と同じ「画面表示時に読み込み→編集→保存」の構成で、`CompanyInfoService`（Application/Master、GetAsync/SaveAsync＝upsert）＋`CompanyInfoSettingsViewModel`（`Views/Master/CompanyInfoSettingsWindow`）として実装。旧WPFプロトタイプ（`bmcs_app.Views.CompanyInfoSettingsWindow`）を画面イメージの参考にしたが、振込口座欄は本プロジェクトでは`bank_account`マスタ（Phase 2-5、未実装）で別管理するため持たず、代わりに現行スキーマ（`docs/database-schema.md` 2.5）に合わせて郵便番号・住所1/2・代表者名を追加した。登録番号は「T+13桁」形式をViewModelで検証（旧プロトタイプと同じ正規表現）。実機（UI Automation）でseedデータの8項目全読込、FAX番号の更新・DB反映・元の値への復元、および不正な登録番号形式での保存が警告メッセージで拒否されることを確認済み。 |
| [x] | 2-5 | 銀行マスタ画面（単価計算マスタは暫定方針では使わないため作らない） | Sonnet | 0-5, 0-6 | **2026-09-14実装。** 得意先・商品・社員マスタ（2-1〜2-3）と同じ「一覧を持たず、コード直接入力＋Enter読込、コード欄でSpace検索モーダル」のパターンで実装。`BankAccountService`（Application/Master）＋`BankAccountMasterViewModel`（`Views/Master/BankAccountMasterWindow`）＋専用の銀行口座検索モーダル（`BankAccountMasterSearchDialog`）。口座種別（普通／当座）は`TaxCategory`と同じ`ComboBox`＋`EnumDisplayConverter`（`BankAccountType`の表示文字列を追加）。表示順は社員マスタの権限レベルと同じ文字列入力＋保存時数値検証。実機（UI Automation）でseedデータBNK001のコード直接読込、新規登録の保存とDB反映、無効化（論理削除）のDB反映、Space検索モーダルからのBNK001選択・転記を確認済み。 |
| [x] | 2-6 | プリンタ環境設定（端末ローカルのファイルへ保存。DB管理しない） | Sonnet | 0-5 | 端末ごとに設定が保持され、再起動後も復元される。`bmcs_config.json`（exeと同フォルダ）にプリンタ設定のみ保存。接続文字列は既存の appsettings.json 方式を維持（ユーザー確認済み） |
| [x] | 2-7 | メニュー構成マスタ＋メインメニュー画面（親子階層、権限による出し分け、ログインUIなし）。現状 `MainMenuViewModel` にある暫定導線（得意先/商品マスタ・プリンタ設定・得意先/商品検索・受注/売上入力への直接遷移ボタン）をメニュー構成マスタ駆動の表示に置き換える | Sonnet | 0-5, 0-6, 0-7 | **2026-09-14実装。** デザインモックのC案（リスト・アコーディオン型）を採用し、ウィンドウは縦長（440×820）に変更（ユーザー指示）。決定確定によりモック画面（`MainMenuMockWindow`）は削除。`MenuTreeBuilder`（Domain、純粋関数、単体テスト4件）が`menu`テーブルと社員の権限レベルから表示可能な階層を組み立て、`MainMenuViewModel`が`screen_key`文字列で画面遷移を一本化（個別の`OpenXxxCommand`は全廃）。**メニュー構成マスタの編集用CRUD画面は作らない**（完了条件に含まれないため。項目追加は`scripts/014_seed_menu_structure.sql`を直接書き換える運用）。実機（UI Automation）でEMP001（権限レベル1）・EMP002（権限レベル9）それぞれ起動時に表示メニューが変わること、存在しない社員コードでは権限レベル0・警告表示・メニュー非表示になること、全メニュー項目（10画面）が正しく開くことを確認済み。Phase 0-3時点の動作確認用コード（`DatabaseHealthService`、書式・キーボード操作のサンプル表示）も本タスクで削除した（既に各実画面で確認済みのため）。詳細は`docs/design_document.md` 14章 |
| [x] | 2-8 | 入金方法マスタ画面（`ReceiptMethod` enumを廃止しマスタ駆動へ移行） | Sonnet | 0-5, 0-6 | **2026-09-18実装。** ユーザー要望「入金区分をコード直書きでなくマスタで管理したい」により、ハードコードのenumを`deposit_method`マスタへ置き換えた（D-7・C-11。詳細は`docs/design_document.md` 27章）。銀行マスタ（2-5）と同じ「一覧を持たず、コード直接入力＋Enter読込、コード欄でSpace検索モーダル」のパターンで実装。`DepositMethodService`（Application/Master）＋`DepositMethodMasterViewModel`（`Views/Master/DepositMethodMasterWindow`）＋専用の検索モーダル。マスタ項目はコード・名称・付随フラグ（入金先口座必須／手形期日必須）・表示順のみ。入金入力・明細入金画面のComboBoxもマスタ駆動に置き換え、`ReceiptEntryService`/`DetailReceiptEntryService`の検証ロジックをマスタのフラグ（`requires_bank_account`/`requires_bill_due_date`）参照に改めた。マイグレーション`scripts/018_create_deposit_method_master.sql`。**当初は振込手数料の参考値（`transfer_fee`）も追加したが、ユーザー確認の結果「イメージと異なる」との指摘を受け撤回した（2026-09-18）。** 全体テスト（Domain 281件／Application 184/185件、残る1件は本移行と無関係の既存事象）green。実機確認は`dotnet build`成功のみ（GUI操作の自動確認は未実施）。 |

---

## Phase 3: 共通検索モーダル

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 3-1 | 得意先検索モーダル（担当者名・住所も検索対象） | Sonnet | 2-1, 0-6 | キーボードのみで検索・選択・転記が完結する。コード・名称・カナ・住所・担当者名での絞り込み、Enterでの一覧遷移・確定、Escapeでのキャンセルを実機で確認済み |
| [x] | 3-2 | 商品検索モーダル（「マスタから」「過去の取引履歴から」の2軸、最大6件の一括転記） | Sonnet | 2-2, 0-6 | 2軸の切り替えと複数選択・一括転記が動作する。得意先の税区分ごとに異なる売上テーブル（請求単位/伝票単位/内税明細単位）からの履歴取得、商品ごとの最新行への重複排除、重複選択の拒否、Ctrl+Enterでの一括転記を実機で確認済み |

---

## Phase 4: 受注入力

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 4-1 | 伝票番号の採番実装（採番テーブル方式） | Sonnet | — | 同時登録でも採番が重複しない。**Opus指定の理由は同時実行制御**。`SlipNumberService`（Application）→`SlipNumberSequenceCommand`（Infrastructure、`UPDATE ... OUTPUT`の生SQL）→`SlipNumberFormatter`（Domain）で実装。結合テスト`tests/bmcs_app.Application.Tests`で実機DBに対し32並列採番の重複・欠番なし、ロールバック時に欠番が出ないこと、トランザクション外呼び出しが例外になることを確認済み |
| [x] | 4-2 | 単価の初期値転記（商品マスタの単価→明細、手入力で上書き可） | Sonnet | 2-2 | 商品を選ぶと単価が入り、手で変更できる。単価列の選択は `IUnitPriceCalculator`／`StandardUnitPriceCalculator`（Domain、単体テストで確認。2026-09-10、M-3決定により`UnitPriceSelector`静的クラスからインターフェース化）に集約。得意先の税区分に応じて外税／内税単価を切り替える不整合（商品検索モーダルが常に外税単価を使っていた）も本タスクで修正し、商品検索モーダルの単価列ヘッダ表示と転記を一致させた。転記先の明細行グリッドは受注・売上で共用する `SlipLineViewModel`／`SlipLineControl`（Common）として実装し、実機確認用に採番・保存を持たない受注入力画面シェル（`OrderEntryWindow`）を追加（4-3が拡張する）。実機で CUS003（内税明細単位）に対し PRD002 が 540（外税500ではない）で転記されること、手入力での単価・数量上書きに応じて端数区分どおりに金額が再計算されること、一括転記・行削除・フォーカス移動を確認済み。あわせて `scripts/seed_dev_data.sql` の CUS003 売上行の単価不整合（500と記録されていたが内税単価は550）を修正 |
| [x] | 4-3 | 受注入力画面（仮伝票として登録、売上を発生させない。引当数量カラムは保持のみ） | Sonnet | 3-1, 3-2, 4-1, 4-2, 0-5, 0-6 | 受注が登録でき、売上・売掛が一切発生しない。旧WPFプロトタイプ（`bmcs_app.Order`）のツールバー・ヘッダー・明細・フッター集計・StatusBarのレイアウトを再現（ユーザー指示、2026-09-08）。`order_slip` に列がない項目（担当者）と、機能が未実装の項目（受注No.の手入力/検索、前後移動、削除＝物理削除はM-17と矛盾するため実装せず4-4に委ねる）は枠のみ用意し無効化。**追記（2026-09-09）**: 摘要・行摘要は当初 `order_slip` に列がなく枠のみだったが、ジャーナル系テーブル（`sales`／`receipt`／`detail_receipt`／`order_slip`）へ `slip_remarks`／`line_remarks` を追加する方針決定（`docs/database-schema.md` 1章、`scripts/011_add_slip_and_line_remarks.sql`）にあわせて本画面でも有効化・保存対応した。新規 `OrderService.CreateAsync`（Application層）が採番（`SlipNumberService`）と登録を同一トランザクションで行う。フッター集計（税抜金額・粗利・消費税計・合計）は `ConsumptionTaxCalculator` を得意先の税区分で分岐して再利用。詳細は `docs/design_document.md` 5章。実機でCUS001（外税）・CUS003（内税）両方の集計一致、保存後の受注No.表示・二重保存防止・DBへの実登録（`sqlcmd`で確認、検証用データは削除済み）、新規リセットを確認済み。**副次的に発見・修正**: `EnterKeyNavigationBehavior`（0-6）が子TextBoxのEnter用KeyBindingより先にイベントを消費し、商品コード欄・得意先コード欄のEnter直接引当が機能しない不具合を修正（売上入力等の他画面にも影響する修正） |
| [x] | 4-4 | 受注の状態遷移の実装（未売上／一部売上／売上完了／中止、売上化済数量の更新） | Sonnet | 1-4 | 分納・中止・売上取消による逆遷移が正しく反映される。**2026-09-10実装。** `OrderStatusCalculator`（Domain、純粋関数）＋`OrderStatusService`（Application、`ApplySalesQuantityDeltasAsync`／`CancelSlipAsync`）として実装。**実装範囲はサービス層＋テストのみ（ユーザー確認済み）**。受注入力画面への配線（既存受注の読み込み、削除(F8)の有効化）は、受注No.から既存伝票を読み込む機能自体が未実装のため5-3／10-1に先送り。中止は伝票単位のみ・終端状態（解除は実装しない）。単体テスト`OrderStatusCalculatorTests`（7件）・結合テスト`OrderStatusServiceTests`（15件、開発用ライブDB、`BeginTransactionAsync`→`RollbackAsync`でseedデータ非破壊）で分納・逆遷移・中止・各異常系を実証済み。全体テスト（Domain 181件／Application 23件）green。詳細は`docs/design_document.md` 7章 |
| [ ] | 4-5 | 店頭在庫数表示の暫定対応（非表示または固定値。スコープ外のため） | Sonnet | design_document の不明点 | 在庫連携が未定でも画面が成立している |
| [x] | 4-6 | 既存受注の直接修正（明細追加・変更・削除して上書き保存） | Sonnet | 4-3, 4-4, 5-3 | 未売上の受注のみ修正・保存できる。一部売上・売上完了・中止済みは読込・表示のみで保存不可。**2026-09-16実装。** 受注入力で「既存受注を読み込んでも保存できない」というユーザー報告を機に、`OrderService`に更新系ユースケースがないこと（4-3・5-3のスコープ外だった）を発見し実装。`docs/database-schema.md`・`docs/product-spec.md`の当初のC-6決定「受注は状態にかかわらず常に直接修正可能」はユーザー確認により「未売上のみ修正可」に改訂。`OrderEditLockEvaluator`（Domain純粋関数）＋`OrderService.UpdateAsync`（`SalesService.UpdateAsync`と同構成だが明示トランザクションなし・全行削除拒否）。付随して、保存失敗後の再保存で行が静かに論理削除される潜在バグ（受注・売上共通）も修正。詳細は`docs/design_document.md` 24章 |
| [x] | 4-7 | 過去受注の複写入力（売上入力5-5の受注版） | Sonnet | 4-3 | 複写元を検索して明細を引き継げる。**2026-09-18実装。** ツールバーの「複写」ボタン→伝票検索モーダル（`Target=Order`、`IncludeUnavailableOrders=true`）→`OrderQueryService.GetSlipAsync`で明細行を新規登録として展開。受注No.・受注日付・状態は初期化し、税率は新しい受注日付で再解決する。詳細は`docs/design_document.md` 26章 |

---

## Phase 5: 売上入力

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 5-1 | 消費税計算の実装（税区分3種×端数区分3種、税率別集計）。複数画面から使うため共通ロジックとして1箇所に置く | **Opus** | — | 全組み合わせの単体テストが通る。`src/bmcs_app.Domain/Calculations/ConsumptionTaxCalculator.cs` に実装、`tests/bmcs_app.Domain.Tests/`（145件、全green）で検証済み。テスト基盤（X-1）も本タスクで前倒し構築 |
| [x] | 5-2 | 売上入力画面（都度売上の直接入力） | Sonnet | 5-1, 4-1, 4-2, 0-5, 0-6 | 税区分別に正しい税額で登録される。**2026-09-10実装。** 保存時の税額分岐（`slip_tax_amount`／`tax_amount`／`billing_number`のCHECK制約対応）は`SalesTaxAmountAssigner`（Domain）に一本化し`SalesService.CreateAsync`から呼ぶ。GUIでのsmoke testの代わりに`tests/bmcs_app.Application.Tests/Sales/SalesServiceTests.cs`（開発用ライブDB結合テスト）でCUS001（請求単位）・CUS002（伝票単位）・CUS003（内税明細単位）の3得意先すべてを検証し、税額・請求状態・消込状態・摘要複写が正しいことを確認済み（検証用データは削除済み）。単体テスト`SalesTaxAmountAssignerTests`（14件）・全体テスト（Domain 174件／Application 8件）すべてgreen。詳細は`docs/design_document.md` 6章 |
| [x] | 5-3 | 受注からの売上確定（受注の残数量を売上化し、受注側の状態を更新） | Sonnet | 4-4 | 分納しても受注・売上の数量整合が崩れない。**2026-09-10実装。** `SalesService.CreateAsync`が明細行の`OrderSlipNumber`／`OrderLineNumber`から受注デルタを導出し`OrderStatusService.ApplySalesQuantityDeltasAsync`を同一トランザクションで呼ぶ。売上入力画面の受注No.欄（`Space`で伝票検索モーダル、`Return`で直接読込）から残数量を明細行へ転記。あわせて4-4の繰り越し分（受注入力画面での既存受注の読み込み・F8中止の配線）も実施。4-4実装時の不具合（中止済み受注への負のデルタが一律拒否され、分納後に中止した受注に紐づく売上を後から取消できなかった）を修正（正のデルタのみ拒否、負のデルタは許可し状態はCancelledを維持）。結合テストは5-7参照。詳細は`docs/design_document.md` 8章 |
| [x] | 5-4 | 返品・値引の入力対応 | Sonnet | M-9 | 税計算・粗利計算でマイナス値が正しく扱われる。**2026-09-10実装。** `sales.slip_type`を明細行ごとに選択可能にし（`SlipLineControl`に区分列を追加）、`SalesSlipTypeRules`（Domain）で数量符号・原価（値引は常に0）を正規化。`ConsumptionTaxCalculator`は5-1で既にマイナス金額に対応済みのため変更なし。返品・値引行の受注紐付けは禁止（`SalesOperationException`）。単体テスト`SalesSlipTypeRulesTests`（6件）で検証 |
| [x] | 5-5 | 過去伝票の複写入力 | Sonnet | 5-2 | 複写元を検索して明細を引き継げる。**2026-09-10実装。** ツールバーの「複写」ボタン→伝票検索モーダル（新設の`SlipSearchDialog`、`SalesQueryService.SearchAsync`）→`GetSlipAsync`で明細行を新規登録として展開。伝票番号・日付・状態は初期化し、税率は新しい売上日付で再解決する |
| [x] | 5-6 | 売上の訂正・取消（月次締め前は元伝票の直接修正、取消は状態を戻す） | Sonnet | — | 請求済み・入金済みの売上に対する操作が適切に制限される。**2026-09-10実装。** `SalesService.UpdateAsync`／`CancelSlipAsync`を追加。編集ロック判定（C-6の3条件）は`SalesEditLockEvaluator`（Domain純粋関数）＋`SalesEditLockService`（`monthly_closing`照会）。伝票単位の楽観的排他制御の共通処理（`docs/architecture.md` 9章が予告）を`SlipConcurrencyGuard`として新設し利用。訂正後の状態が新たに編集ロック対象になる場合（伝票日付を確定済み月次締め年月へ移動する等）も保存前に再判定して拒否する。売上No.欄から既存伝票を検索・読込できるようにし、削除(F8)を`CancelSlipAsync`に配線 |
| [x] | 5-7 | フェーズレビュー（税計算・粗利・状態整合） | Sonnet | 5-1〜5-6 | 金額が狂う経路が残っていないことを確認できている。**2026-09-10実施。** 新規登録・受注確定・複写・訂正・取消のすべてが`SalesService`を経由し税額確定は`SalesTaxAmountAssigner`のみが行うことを確認。`ProductHistoryQueryService`の`IsDeleted`／`SlipType`フィルタ漏れを本フェーズ中に発見・修正。結合テスト`tests/bmcs_app.Application.Tests/Sales/SalesServiceCorrectionTests.cs`（14件）で受注確定・返品・訂正・取消・編集ロック3条件・排他制御・4-4修正の全経路を検証。単体テスト`SalesSlipTypeRulesTests`（6件）・`SalesEditLockEvaluatorTests`（8件）を追加。全体テスト（Domain 195件／Application 37件）すべてgreen。詳細は`docs/design_document.md` 8章 |

---

## Phase 6: 請求（締め請求・明細請求書）

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 6-1 | 請求締め処理（対象売上・入金の集計、請求データの確定、二重集計の防止、税率別内訳の確定値保存） | Sonnet | 5-1 | **2026-09-11実装。** `BillingClosingService`（Application/Billing）が「締め日を指定して一括」処理する（2026-09-11ユーザー確認）。集計期間は売上（下限なし・`billing_number IS NULL`で二重集計防止）と入金（前回確定`billing`の締め日+1日を下限）で非対称（`docs/design_document.md` 9章）。税額計算は既存`ConsumptionTaxCalculator`を税単位で使い分け（請求単位＝`CalculateExternalTaxBuckets`、伝票単位＝`CalculateExternalTaxPerSlip`で保存済み`slip_tax_amount`との一致を検証）。二重締め防止はアプリ側の事前チェックとDB側のフィルタ付き一意インデックス`UQ_billing_customer_closing_ym_confirmed`（`scripts/013_add_billing_confirmed_unique_index.sql`）の二段構え。画面（`Views/Billing/BillingClosingWindow`）から一連の操作を実機確認済み（実データのCUS001に対し前回確定`billing`の残高11,000円が正しく引き継がれ、同一締め年月への二重締めが「既にこの締め年月で確定済みです」で正しくスキップされることをUI Automation経由で確認）。結合テスト`BillingClosingServiceTests`（7件、専用テスト得意先で検証）・単体テスト`ClosingDateResolverTests`（9件）を追加。全体テスト（Domain 204件／Application 44件）すべてgreen |
| [x] | 6-2 | 締め解除処理（請求データを解除済にし、売上の請求状態を未請求へ戻す）。**6-1とは別画面（別ウィンドウ）として実装する（C-8決定）** | Sonnet | 6-1, C-8 | **2026-09-14実装。2026-09-15改訂: ユーザー指示により、解除の指定単位を請求番号1件から請求日（`billing_date`）単位の一括解除へ変更（締め側`BillingClosingService.ConfirmAsync`と粒度を揃える）。** `BillingReleaseService.ReleaseByBillingDateAsync`（Application/Billing）が指定した請求日の確定済み`billing`をすべて解除済にし、紐付く`sales`行を未請求へ戻す。解除できるのは同一得意先の確定済み`billing`のうち締め年月が最も新しいものに限定（9-5の締め順序逆転防止と対になる制約。より新しい確定済みがあれば拒否）という制約は維持しつつ、**対象の一部でもこの制約に違反する場合は全体を中止するAll-or-nothing方式**を採用（2026-09-15ユーザー確認）。画面（`Views/Billing/BillingReleaseWindow`）は請求締め処理とは別ウィンドウのまま、入力を請求日に変え、対象を一覧表示してから一括解除する方式に置き換えた（請求番号直接入力方式は廃止）。権限判定は基盤（0-7）が未着手のため本画面では行わない。結合テスト`BillingReleaseServiceTests`（6件）で解除後の状態遷移・二重解除の拒否・締め順序逆転の拒否・対象0件の拒否・All-or-nothingに加え、**完了条件「解除→再締めで金額が一致する」を解除後の再締めで直接検証**。詳細は`docs/design_document.md` 10章 |
| [x] | 6-3 | 明細請求書発行（対象条件＝未請求かつ消込完了でない売上明細行、宛名の都度入力） | Sonnet | 1-2 | **2026-09-14実装。** `DetailInvoiceService`（Application/Billing）が対象条件（`detail_invoice_sales_line`に連携行が無い、かつ消込完了でない`tax_unit=3`の売上明細行）を`BuildCandidateQuery`に1本化し、画面表示（`GetCandidatesAsync`）と発行時の再確認（`IssueAsync`、トランザクション内で再実行）の両方から使う。税額計算は5-1の`ConsumptionTaxCalculator.CalculateInternalTaxPerLine`をそのまま使用。二重請求防止はDB側UNIQUE制約とアプリ側再確認の二段構え。画面（`Views/Billing/DetailInvoiceIssueWindow`）はデモ`bmcs_app.LineInvoice`の2ペイン構成（左＝取込候補／右＝請求書明細）を踏襲しつつ、一覧は`ListView`＋`GridView`、明細請求書No.の直接入力＋Enter読込（既存分は読み取り専用表示）という既存画面の統一パターンに合わせた（デモとの差異は`docs/design_document.md` 11章）。宛名は得意先名を初期値に手入力で上書き可能（C-9）。実装検証で`scripts/seed_dev_data.sql`のCUS003返品行（SALLIN004）の`tax_amount`不整合（-81.00→-82.00）を発見・修正。結合テスト`DetailInvoiceServiceTests`（8件、専用テスト得意先で検証）。全体テスト（Domain 204件／Application 57件）すべてgreen。実機（UI Automation）でCUS003指定時に候補がSALLIN001・SALLIN004の2行のみ（請求済・消込完了は除外）になることを確認済み。詳細は`docs/design_document.md` 11章 |
| [x] | 6-4 | 明細請求書の取消（連携テーブルの紐付け解除と売上側の状態復帰） | Sonnet | 6-3 | **2026-09-14実装。** `DetailInvoiceService.CancelAsync`（6-3と同じクラス）が連携行（`detail_invoice_sales_line`）を物理削除し売上明細行の`billing_status`を未請求へ戻す。ヘッダー（`detail_invoice`）は物理削除せず`invoice_status`を取消済にするだけに留める（締め解除と同じ非破壊方式）。連携行の削除により対象条件（11-1節「連携行の存在が唯一の正」）が自動的に真に戻り、完了条件「取消後に同じ売上を再度請求できる」を満たす。C-8が別画面分離を要求するのは締め解除・月次締め解除のみのため、別画面ではなく6-3画面（`DetailInvoiceIssueWindow`）の「削除 (F8)」（枠のみだった箇所）に配線した（2026-09-14ユーザー確認）。取消の拒否条件は二重取消・存在しない番号に加え、①連携先の売上明細行に消込済み（一部・完了とも）の行が含まれる、②この明細請求書を指定した明細入金（`detail_receipt.target_type=2`）が存在する、の2つ（Phase 7-4未実装だがテーブル・FK・seedデータは既存のため先取りで塞いだ）。結合テスト`DetailInvoiceServiceTests`に5件追加（既存8件と合わせて13件）。全体テスト（Domain 204件／Application 62件）すべてgreen。実機でseedデータ`DIV001`（発行済・消込完了済の`SALLIN002`と連携・明細入金`DRC002`あり）の取消が両条件で拒否されること、取消済み`DIV002`では「削除 (F8)」が無効であることを確認済み。詳細は`docs/design_document.md` 12章 |
| [x] | 6-5 | フェーズレビュー（二重計上・状態整合） | Sonnet | 6-1〜6-4 | **2026-09-14実施。** 締め得意先と都度得意先は`tax_unit`（登録後不変）で構造的に分離され、締め請求と明細請求が同じ売上を二重に拾う経路が存在しないことを確認。レビューで2件の不具合を発見・修正した。①明細請求書発行済みの売上明細行が編集ロックされておらず（`tax_unit=3`は`billing_number`が常にNULLのため条件①が発火しない）、発行後も売上入力画面から自由に訂正・取消できてしまっていた問題。編集ロックに4つ目の条件「明細請求書発行済み」を追加（C-6改訂、`SalesEditLockEvaluator`／`SalesEditLockService`）。②`BillingClosingService.ConfirmAsync`／`BillingReleaseService.ReleaseAsync`がEF例外を業務例外へ変換していなかった問題（`DetailInvoiceService`は既に変換済みで非対称だった）。両サービスに変換処理を追加し、請求系3画面のViewModelにも`catch (Exception)`フォールバックを追加した。単体テスト`SalesEditLockEvaluatorTests`に2件追加、結合テスト`BillingPhaseReviewTests`（6件、新規）で締め↔解除↔売上訂正、発行↔取消↔売上訂正、請求締め済み・明細請求書発行済みの編集ロック、確定済み`billing`の一意性を検証。全体テスト（Domain 205件／Application 68件）すべてgreen。詳細は`docs/design_document.md` 13章 |

---

## Phase 7: 入金・消込

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 7-1 | 消込サービスの実装（消込ステータスと消込済金額のキャッシュ列を同一トランザクションで更新） | Sonnet | 1-4 | **2026-09-14実装。** 完了条件が「登録・取消・訂正のいずれでも一致」であるため、4-4の`OrderStatusService`のようなデルタ方式ではなく、入金データ（`receipt`／`detail_receipt`）から得意先単位で毎回全件再計算する方式を採用（`SettlementService.RecalculateForCustomerAsync`、`src/bmcs_app.Application/Receipt/`）。締め入金は`billing_number`へ充当した額をその請求に紐づく売上明細行へ伝票日付→伝票番号→行番号の古い順に配分し、明細入金は直接指定を先に確定してから残額を明細請求書経由の配分に回す。配分ロジック（`SettlementAllocator`）は「マイナス行を先に全額充当」方式が入金ゼロでも返品行を消込完了にする事故を起こすため、「全額充当なら全行そのまま／不足なら同符号の行へ古い順」の2分岐に変更した（`SettlementStatusCalculator`／`AllocationStatusCalculator`と合わせてDomain純粋関数、単体テスト計約34件）。レビューで発見した既存の取り残しバグ2件（締め解除後・売上訂正で金額を減らした後の消込キャッシュ不整合）も本タスクの範囲に含めて`BillingReleaseService`／`SalesService`に配線し修正（2026-09-14ユーザー確認）。`docs/architecture.md`6章（明示トランザクション許容ケース4番目）・9章（TouchAllを使わない方針）に設計判断を追記。`scripts/015_add_detail_receipt_indexes.sql`で`detail_receipt`に索引3本を追加（フィルタ付き索引は使わない方針を維持）。実装検証で`scripts/seed_dev_data.sql`の消込関連の金額不整合6箇所（`RCP_INV001`／`RCP_SLP001`／`SALSLP002`の税込税抜の取り違え、`SALLIN002`の切上丸め誤り、`DIV001`／`DRC002`の税額二重加算）を発見・修正。あわせて`scripts/013`がフィルタ付き一意索引を追加した後`seed_dev_data.sql`が`SET QUOTED_IDENTIFIER ON`を明示していなかったため適用不能になっていた問題も修正。結合テスト`SettlementServiceTests`（16件）で完了条件（登録→訂正→取消を通した一致・冪等性）を直接検証。全体テスト（Domain 239件／Application 88件）すべてgreen。実装範囲はサービス層＋テストのみで、UI（7-2/7-4/7-5）は未配線（4-4と同じ前例）。詳細は`docs/design_document.md` 16章 |
| [x] | 7-2 | 入金入力画面（締め得意先／請求単位で古い順に自動消込） | Sonnet | 7-1, 6-1, 0-5, 0-6 | **2026-09-15実装、同日改訂。** 締め得意先専用の新規登録画面。初版は確定済みDBスキーマを優先し明細行＝充当行（`billing_number`／`allocated_amount`）として実装したが、業務実態の確認により「充当先は利用者にとって重要でなく、入金方法を行ごとに選べる必要がある」と判明したため全面改訂した。明細行は支払手段の内訳（入金方法＋入金先口座（振込のみ）＋手形期日（手形のみ）＋金額＋行摘要）に変更し、請求への充当は新設した`receipt_allocation`テーブルへ分離して画面には表示しない内部データとした（`scripts/016_split_receipt_allocation.sql`）。配分ロジックは新規のDomainクラスを作らず、7-1で実装済みの`SettlementAllocator`を無変更で流用（`src/bmcs_app.Application/Receipt/ReceiptEntryService.cs`）。確定済み`billing`の未消込残額を古い順（`billing_date`→`billing_number`）に整列した対象額として渡し、全額充当できない残額は`billing_number = NULL`の前受・過入金行にまとめる。画面には得意先の請求残高を表示する。詳細は`docs/design_document.md` 17章。実装範囲は新規登録と既存伝票の読み取り専用読込のみ（訂正・取消は7-5。取消(F8)は枠のみ用意し無効化）。振込手数料差額の入力は7-3の範囲外として本画面では常に0（M-14。入力先は`receipt_allocation.fee_adjustment_amount`）。メインメニューに新カテゴリ「入金」を追加（`scripts/014_seed_menu_structure.sql`）。結合テスト`ReceiptEntryServiceTests`（13件）・`SettlementServiceTests`（16件、`receipt`/`receipt_allocation`分離に合わせ改訂）で完了条件を直接検証。全体テスト（Domain 239件／Application 101件）すべてgreen。実機確認は`dotnet run`でのアプリ起動・メインメニュー表示（エラーなし）まで。GUI操作を自動で駆動する手段が実行環境に無いため、画面上のクリック操作は未確認（FlaUIはPhase 11未着手）。 |
| [ ] | 7-3 | 振込手数料差額の入力（手入力。**2026-09-10決定により自動提案は行わない**）**（2026-09-15、ユーザー指示により一旦保留。7-4以降を先に進める）** | Sonnet | M-14 | 入金額と請求額の差額を手数料調整額として`receipt_allocation.fee_adjustment_amount`へ手入力で登録できる |
| [x] | 7-4 | 明細入金画面（都度得意先／売上伝票または明細請求書を指定したピンポイント消込） | Sonnet | 7-1, 6-3, 0-5, 0-6 | **2026-09-15実装。** 都度得意先（内税明細単位）専用の新規登録画面。画面レイアウト・操作方法は旧デモ`bmcs_app.LineReceipt`を踏襲（左＝入金登録の明細行、右＝上段タブ〈売上伝票／明細請求書〉＋下段の選択伝票明細）。**充当粒度はスキーマに合わせて改訂**（デモは請求書タブでも行単位だったが、`docs/database-schema.md` 2.11節に合わせ、売上伝票タブ＝売上明細行単位（`target_type=1`）、明細請求書タブ＝請求書まるごと1行（`target_type=2`）とした。2026-09-15ユーザー確認）。金額は常に対象の全額（または残額）で固定・読み取り専用、前受金は無し。手形は`detail_receipt`に期日列が無いため選択肢から除外。`DetailReceiptEntryService`（Application/Receipt）が候補抽出（未消込・一部消込の両方を含める。一部消込を除外すると残額を永久に入金できない行き止まりになるため）と保存時の再検証（金額ゼロ・二重充当・請求書金額と連携売上合計の不一致等）を担う。配分・消込判定は既存の`SettlementService`（7-1）をそのまま流用。同時実行制御は追加ロックを設けず、既存のrowversion楽観的排他（`SettlementService.RecalculateForCustomerAsync`）に委ねた。実装前にPlanエージェントによる設計検証を実施し、候補条件の抜け穴・金額ゼロ/負の扱い・二重充当防止漏れ等の修正必須事項を反映済み（詳細は`docs/design_document.md` 18章）。メインメニューに「入金 > 明細入金」を追加（`scripts/014_seed_menu_structure.sql`、`menu_code = MNU_DETAIL_RECEIPT`）。結合テスト`DetailReceiptEntryServiceTests`（20件）で完了条件を直接検証。全体テスト（Domain 239件／Application 121件）すべてgreen。実機確認は`dotnet run`でのアプリ起動まで（Phase 7-2と同じ扱い）。**指示の経緯**: 作業開始時の指示は「Phase 7-3」だったが指示内容がPhase 7-4の完了条件と一致し、7-3は保留中だったため、ユーザーに確認のうえPhase 7-4として実装した |
| [x] | 7-5 | 入金の取消・訂正（消込の巻き戻し） | Sonnet | 7-1 | **2026-09-15実装。** `ReceiptEntryService`／`DetailReceiptEntryService`に`UpdateAsync`（訂正）・`CancelSlipAsync`（取消）・`EvaluateEditLockAsync`（編集ロック判定）を追加し、7-2/7-4画面のUI（取消(F8)・訂正モード）を配線した。消込の巻き戻し自体は7-1の`SettlementService.RecalculateForCustomerAsync`をそのまま呼ぶだけで実現（5-6の`SalesService`と同じ設計）。**実装着手時に設計上の矛盾を発見しユーザー確認のうえ修正**: 当初方針「C-6の4条件は`sales`側のみに適用し`receipt`／`detail_receipt`自体には月次締めのみ適用する」だったが、`BillingClosingService`が締め処理時に`receipt.Amount`の合計を`billing.CurrentBillingAmount`へスナップショットとして焼き込み以後再計算しないことが判明し、この期間の`receipt`を無条件に取消・訂正できると確定済み請求の残高が狂うため、`receipt`のみ「請求締めスナップショット」（`receipt_date`が確定済み`billing`の集計期間に含まれるか）を編集ロック条件に追加した（`detail_receipt`はこの問題を持たないため月次締めのみ）。明細入金の訂正は充当先の追加を許さず（候補クエリが自伝票自身の充当を除外できないため）、金額も再計算せず読込時の値を保持する。実装方式は5-6と同じ「行単位の差分」（全行削除して作り直す方式は不採用）。`ReceiptEntryService.UpdateAsync`は`receipt_allocation`の全面再構築のために1ユースケース内で`SaveChangesAsync`を2回呼ぶ（`docs/architecture.md`6章に許容ケース5番目として追記）。結合テスト`ReceiptEntryServiceTests`に10件・`DetailReceiptEntryServiceTests`に9件追加（完了条件・編集ロック・訂正時の充当再構築・排他制御・返品行の巻き戻しを検証）。全体テスト（Domain 239件／Application 140件）すべてgreen。実機確認は`dotnet run`でのアプリ起動まで（Phase 7-2/7-4と同じ扱い）。詳細は`docs/design_document.md` 19章 |
| [x] | 7-6 | フェーズレビュー（消込整合性） | Sonnet | 7-1〜7-5 | **2026-09-15実施（7-3保留のまま実施。前提は7-1〜7-5のみでユーザー確認済み）。** 4カラム（`sales.settlement_status`／`settled_amount`／`receipt`・`detail_receipt.allocation_status`）の書き込み経路を`grep`で監査し、書き手が`SettlementService`の1箇所（例外は新規行への一時プレースホルダのみで、同一トランザクション内で必ず`RecalculateForCustomerAsync`により上書きされる）に限られることを確認。`RecalculateForCustomerAsync`を呼ばない`BillingClosingService.ConfirmAsync`／`DetailInvoiceService.IssueAsync`／`CancelAsync`についても、FK制約上「新規`billing`／明細請求書への充当が事前に存在し得ない」ことから再計算不要と判定できることを確認（不具合ではない）。開発用ライブDBの全得意先に対し実際に`RecalculateForCustomerAsync`を呼び差分0件であることを直接検証する結合テスト`SettlementPhaseReviewTests`を新規追加し、独立した生SQLでの構造整合性チェック（5項目）も0件を確認。**本レビューで修正した不具合はなし**。7-3実装時（`fee_adjustment_amount`が非ゼロになる）は本レビューの再実施が必要（申し送り済み）。全体テスト（Domain 239件／Application 141件）すべてgreen。詳細は`docs/design_document.md` 20章 |

---

## Phase 8: 得意先元帳

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 8-1 | 元帳データのマージ実装（該当税単位の売上テーブルと入金テーブルをアプリ側LINQで結合、時系列化、残高推移の算出） | Sonnet | 7-1 | **2026-09-15実装。** ユーザー指示により画面（ViewModel/View）まで含めて実装した。残高は税込で扱い、`tax_unit=1`（伝票時点で税額を持てない）の穴を埋めるため消費税を独立した明細行として時系列に挿入する（請求締め済み区間は`billing.tax_amount`確定値、未締め区間は`ConsumptionTaxCalculator`による仮計算）。入金の残高影響は常に入金日付の独立行でのみ発生させ、都度得意先の売上行には消込の証跡（入金日付・入金No、金額なし）のみ同居させる（売上と入金が月をまたいでも月末残高が日付どおり正確になり、9-1の暦月末残高と整合する設計。2026-09-15ユーザー確認）。マージ・残高推移・仮計算税はDomainの純粋関数（`CustomerLedgerBuilder`／`LedgerReceiptPairing`、`src/bmcs_app.Domain/Calculations/`）に集約し、Application層は`CustomerLedgerQueryService`（`src/bmcs_app.Application/Ledger/`）がクエリして渡すだけにした。画面は`Views/Ledger/CustomerLedgerWindow`（デモ`bmcs_app.CustomerLedger`のレイアウトを踏襲しつつ入金額・残高列を追加）。メインメニューに新カテゴリ「元帳」を追加（`scripts/014_seed_menu_structure.sql`、`screen_key=customer_ledger`）。単体テスト`CustomerLedgerBuilderTests`（10件）・`LedgerReceiptPairingTests`（4件）でseedデータ相当のCUS001/CUS002/CUS003の残高推移が手計算と一致することを直接検証（完了条件）。結合テスト`CustomerLedgerQueryServiceTests`（8件、開発用ライブDB）は実際のseedデータ（CUS001→9,500／CUS002→2,100／CUS003→9,900）を読んで同じ数値を確認する回帰検知テストを含む。全体テスト（Domain 253件／Application 149件）すべてgreen。実機確認は`dotnet run`でのアプリ起動・メニュー表示まで（Phase 7-2/7-4/7-5と同じ扱い）。詳細は`docs/design_document.md` 21章 |
| [x] | 8-2 | リアルタイム残高の常時表示 | Sonnet | 8-1, M-11 | **2026-09-15実装。** 「常時表示」とは、ウィンドウを開いたまま他画面の更新を自動検知して書き換える仕組み（プッシュ通知等）ではなく、**残高キャッシュ列を持たず（M-11）都度計算するため、検索期間とは独立に必ず最新値が出る**という意味であることをユーザーに確認済み。得意先元帳画面（`CustomerLedgerViewModel`）に「現在残高」（本日時点の残高。検索期間のFrom/Toを変えても連動しない）を追加し、得意先確定時と表示(F5)実行時の両方で`CustomerLedgerQueryService.GetBalanceAsOfAsync`（8-1で用意済みの入口）を呼んで再計算する。結合テスト`現在残高は伝票登録直後に反映される`（`CustomerLedgerQueryServiceTests`に追加）で、同一トランザクション内での売上登録直後に`GetBalanceAsOfAsync`が新しい値を返すことを直接検証。全体テスト（Domain 253件／Application 150件）すべてgreen。詳細は`docs/design_document.md` 21章 |
| [x] | 8-3 | 伝票プレビュー（`readOnly` モードの売上／入金画面を再利用。専用画面は作らない） | Sonnet | 5-2, 7-2 | **2026-09-15実装。** 得意先元帳の行を`Enter`／ダブルクリックで活性化（`RowActivationBehavior`、`CustomerLedgerViewModel.OpenSlipPreviewCommand`）すると、行の種別（売上／消込証跡の継続行／入金／伝票単位消費税）に応じて売上入力・入金入力・明細入金のいずれかを非モーダル・毎回新規ウィンドウで読み取り専用（プレビュー）表示する。専用のプレビュー画面は作らず既存3画面を再利用。`WindowService.Show`に`configure`パラメータ（`ShowDialog`と同じ位置づけ）を追加し、プレビュー対象の伝票No.（`PreviewSlipNumber`）を渡す。各画面のreadOnly化は個別実装（共通基底クラスは作らない）。`New`／検索モーダル／伝票読込／複写／保存／取消の各コマンドはWPFの`KeyBinding`が`IsEnabled=false`でも生き続けるため`CanExecute`（`!IsPreviewMode`）で塞ぎ、取消系はコマンド本体にも`if (IsPreviewMode) return;`の二重防御を入れた。明細行グリッドは`ItemsControl`単位で`IsEnabled`を無効化。Escで閉じられる（保存・取消以外に退出手段が無いため）。本タスクはPresentation層のみの変更のため新規テストは追加せず、既存テスト（Domain 253件／Application 150件）が全green・実機（`dotnet run`）でのアプリ起動を確認済み（GUI自動操作の手段が実行環境に無いため画面上のクリック操作はPhase 7-2/7-4/7-5/8-1/8-2と同様に未確認）。詳細は`docs/design_document.md` 21-7章 |

---

## Phase 9: 月次締め

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 9-1 | 月次締め処理（**得意先ごと**の暦月末売掛残高を`monthly_closing`に確定保存。`tax_unit=1`の未確定区間は税額を仮計算。担当者別売上・粗利は都度集計）。**【Phase 12申し送り】親子請求（請求集約）は月次締めの対象外。請求集約せず`customer_code`（請求集約元）ごとに個別集計する方針で実装してよい（`docs/database-schema.md` 2.16節）** | Sonnet | 8-1, C-7 | 集計値が元帳の残高と一致する **2026-09-30実装。** 画面は請求締め処理と同じ構成で、締め日コンボボックスなし・請求日の代わりに集計年月を選ぶ。当月残高は元帳の月末残高、前月残高は前月確定行の当月残高（連続性優先）、消費税額は逆算。請求集約元の行は自社売上のみ。担当者別集計は9-4へ分離。詳細は`docs/design_document.md`29章 |
| [x] | 9-2 | 確定後のロック（`customer_code`＋該当年月の伝票日付を持つ売上・入金、または確定済み`billing`に集計済みの売上を編集不可にする。導出方式） | Sonnet | 1-4 | 確定後に該当得意先・該当年月の伝票が編集できない **2026-09-30実装。** 既存行の編集ロックは9-1で実データが入り有効化。9-2で、①締め済みの月への新規登録・日付変更の禁止（保存時にApplication層で検証。`MonthlyClosedService`）、②請求集約先の月次行でも請求集約元の伝票をロック、の2点を追加。詳細は`docs/design_document.md`29-2 |
| [x] | 9-3 | 締め解除（管理者権限のみ。`billing`と同じ非破壊方式）。**9-1／9-2とは別画面（別ウィンドウ）として実装する（C-8決定）** | Sonnet | C-8, 9-2 | 権限のない社員コードでは解除できない **2026-09-30実装。** 集計年月単位で全得意先の確定済み行をまとめて解除（非破壊、All-or-nothing）。より後の年月が確定済みの得意先があれば解除不可。権限はメニュー単位（`MNU_MONTHLY_RELEASE`、レベル9。C-8）。詳細は`docs/design_document.md`29-3 |
| [ ] | 9-4 | 担当者別売上・粗利の集計（保存せず都度集計。`customers.sales_employee_code`をキーに、暦月単位で売上・粗利を表示する。9-1のユーザー確認で今回の対象外とした） | Sonnet | 9-1, C-7 | 担当者別の売上・粗利が月次締めの売上額と整合する |

---

## Phase 10: データ検索・帳票

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [ ] | 10-1 | データ横断検索画面（受注・売上・入金の横断検索） | Sonnet | 各伝票フェーズ, 0-5, 0-6 | 条件を組み合わせて横断検索できる |
| [ ] | 10-2 | 納品書未発行の売上の一括発行（納品書発行状態の更新を含む） | Sonnet | 1-4, 10-3 | 一括発行後に発行状態・発行日時が更新される。**前提10-3が完了済みのため着手可。`DeliveryNoteService.MarkIssuedAsync`（10-4実装）をそのまま再利用できる** |
| [x] | 10-3 | 帳票エンジンの選定と基盤実装 | Sonnet | **M-10（要決定）** | 全帳票が同じ仕組みで出力できる。**2026-09-15、10-4（納品書）と同時に実装（ユーザー確認済み。10-3単体では完了条件を実証できないため）。** `docs/report-spec.md` 2-0節に基盤構成を記載。設計レビューで層配置（帳票データ取得はApplication業務領域フォルダ、レンダリング・印刷はPresentation `Reports/`。`docs/architecture.md` 5章補足）・フォルダ規約違反（`Application/Reports/`→`Application/Sales/`）・DocumentViewer組込印刷ボタンの矛盾等を発見・修正。派生クラスが`DeliveryNoteDocumentBuilder`1つの間はこの構成を維持し、10-5着手時に不足が判明したら基底クラスへ引き上げる方針 |
| [x] | 10-4 | 納品書の実装 | Sonnet | 10-3 | 現場が使えるレイアウトで出力される。**2026-09-15実装（10-3と同時）。** `DeliveryNoteService`（Application/Sales）＋`DeliveryNoteDocumentBuilder`（Presentation/Reports）＋`ReportPreviewDialog`（Views/Common）で実装。税単位3種のフッター表示・返品値引行の表示・再発行表示・適格請求書として扱わない判断（税理士確認待ちのまま）は`docs/report-spec.md` 2-1節参照。**設計レビューで発見・修正した重大な不具合**: `ExecuteUpdateAsync`による発行記録更新がChangeTrackerを経由しないため、同一DbContextスコープで追跡中の売上行（`SalesQueryService.GetSlipAsync`経由）のRowVersionが陳腐化し、後続の訂正保存が偽の競合エラーになる問題を`MarkIssuedAsync`内の`ReloadAsync`で解消（結合テストで回帰検証）。保存直後に印刷できない問題（保存後は常に`New()`で画面初期化されるため）は、保存成功後・`New()`前に発行確認ダイアログを挟む導線で解消（2026-09-15ユーザー確認）。結合テスト`DeliveryNoteServiceTests`（7件）・単体テスト`ReportPaginationTests`（9件）を追加。全体テスト（Domain 262件／Application 158件）すべてgreen。実機確認は`dotnet run`でのアプリ起動まで（新規DI登録を含めて起動時例外なしを確認）。GUI自動操作の手段が実行環境に無いため、画面上での印刷・プレビュー表示・改ページの目視確認は次回実施。詳細は`docs/design_document.md` 22章 |
| [x] | 10-5 | 請求書・明細請求書の実装（適格請求書の記載事項、税率別内訳、軽減税率対象の付記、代表者印字フラグ、都度入力の宛名） | Sonnet | 10-3 | **2026-09-16実装。** `InvoiceDocumentBuilder`（締め得意先向け請求書）・`DetailInvoiceDocumentBuilder`（都度得意先向け明細請求書）で実装。`billing`／`detail_invoice`は税種別区分ごとの確定金額のみ保持し税率(%)は持たないため、`ConsumptionTaxCalculator.ResolveConfirmedBuckets`（新規）が「金額はヘッダーの確定値・税率ラベルは明細行から拝借」する方式で内訳を組み立てる設計判断を採用（二重丸めを回避）。代表者印字（`print_representative_flag`）は「代表者　○○○○」＋押印用の空欄枠、振込先は`bank_account.is_print_on_invoice`の口座を表示。印刷履歴は記録しない（DDL変更なし）。10-3のコメントどおり`ReportDocumentBuilder`へ共通処理（発行者情報ボックス・振込先ブロック・内訳行・合計行）を引き上げ、`DeliveryNoteDocumentBuilder`も移行（表示不変）。明細請求書は既存の発行画面（`DetailInvoiceIssueWindow`）の「印刷 (F11)」に配線。**請求書は専用画面を新設せず、請求締め処理画面（`BillingClosingWindow`）の結果一覧から選択行を印刷する導線にした（ユーザー確認）。これにより2026-09-11確定の「確定後は結果一覧を含めて即座にリセットする」仕様と矛盾したため、確認のうえリセット仕様を変更**（確定後も一覧を残し、条件変更で次のバッチへ進む。`docs/design_document.md` 9-7章・23章）。結合テスト`InvoiceServiceTests`（4件）・`DetailInvoicePrintDataTests`（3件、既存seedデータの回帰検知）、単体テスト`ConsumptionTaxCalculatorConfirmedBucketsTests`（4件）。全体テスト（Domain 266件／Application 165件）すべてgreen。実機確認は`dotnet run`でのアプリ起動まで（GUI自動操作の手段が実行環境に無いため画面上の印刷・代表者印字の目視確認は未実施）。詳細は`docs/report-spec.md` 2-2節・`docs/design_document.md` 23章 |
| [ ] | 10-6 | 得意先元帳の帳票出力 | Sonnet | 10-3, 8-1 | 画面表示と帳票の数値が一致する |
| [x] | 10-7 | 請求書の再発行（10-5の再改訂。過去に締めた請求書をいつでも再照会・再印刷できるようにする） | Sonnet | 10-5 | **2026-09-29実装。** 10-5では請求締め処理画面（`BillingClosingWindow`）の一覧が`BillingClosingService.PreviewAsync`による集計プレビューだったため、画面を開き直すと確定済みの請求番号が見えなくなり、過去に締めた請求書の再印刷が実質できなかった。**一覧を「これから締めたらどうなるか」のプレビューから、`billings`に実在する確定済み請求データを請求日だけで抽出したものへ変更**（`InvoiceService.GetByBillingDateAsync`新規。確定済み・未削除のみ、解除済みは対象外）。未確定の請求日は0件表示になり、確定前の集計プレビュー機能はこの画面から廃止した（ユーザー確認済み。10-5・9-7章の既存決定と矛盾したため確認して変更）。これに伴い締め確定(F10)実行前に確認ダイアログを追加。一覧は拡張選択（`SelectionMode="Extended"`、新規`MultiSelectionBehavior`）で複数選択可能にし、選択した全行を1つの`FixedDocument`にまとめて印刷できるようにした（`PagedReportDocumentBuilder.BuildInto`新規、`PrintCommand`はパラメータなしに変更）。結合テスト`InvoiceServiceTests`に3件追加（得意先コード順・0件・解除済み除外）。詳細・改訂履歴は`docs/design_document.md` 9-7章、`docs/report-spec.md` 2-2節参照 |

---

## Phase 11: UI結合テスト自動化（FlaUI）

各機能の画面操作を通した結合テストを FlaUI で自動化する。**着手は急がない。** 対象画面がある程度揃ってから（目安: Phase 7〜9まで完了した頃）着手すればよく、それまでは既存の単体テスト・DB結合テスト（X-1、`docs/architecture.md` 15〜16章）で金額計算・状態整合を担保する。

DBへの書き込みを伴う点は `tests/bmcs_app.Application.Tests`（16章）と同じ課題を抱えるため、テストデータの後始末方針（ロールバック or 専用テストデータの削除）は16章の考え方を踏襲する。

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [ ] | 11-1 | FlaUIテスト基盤の構築（`tests/bmcs_app.UiTests` プロジェクト作成、`FlaUI.Core`／`FlaUI.UIA3`導入、アプリのプロセス起動・ウィンドウ取得・終了のヘルパー、実DBを使う場合の後始末方針の決定） | Sonnet | Phase 7〜9程度まで完了 | サンプル画面（例: 得意先マスタ一覧）に対し起動→要素取得→終了までが自動実行でき、実行後にDBへ余計なデータが残らない |
| [ ] | 11-2 | AutomationId命名規則の策定と既存画面への付与 | Sonnet | 11-1 | 主要画面（マスタ・検索モーダル・受注／売上／入金・請求締め・元帳）の入力欄・ボタン・グリッドにAutomationIdが設定され、名前ではなくIDで要素取得できる |
| [ ] | 11-3 | 業務フロー別シナリオテストの実装（例: 受注入力→売上確定、売上入力の税区分3パターン、返品・値引、請求締め→入金消込、明細請求書発行→明細入金）。対象フェーズが実装済みのものから順に追加していく | Sonnet | 11-1, 11-2, 各対象フェーズ | 各シナリオがUI操作のみで実行でき、実行後のDB状態が期待値と一致する |
| [ ] | 11-4 | 実行手順の確立（ローカルでの実行コマンド、実行前提条件＝開発用DB接続・アプリの多重起動禁止、CI組み込みの要否判断） | Sonnet | 11-1 | 手順どおりに実行して毎回同じ結果になる（テスト間の副作用が残らない） |
| [ ] | 11-5 | フェーズレビュー（カバーしている業務フローの棚卸し、抜けている画面・シナリオの洗い出し） | Sonnet | 11-1〜11-4 | 主要業務フロー（受注〜入金〜元帳）がUI経由で回帰確認できる状態になっている |

---

## Phase 12: 親子請求（請求集約）

各地に支店を持つ会社の各支店（請求集約元）の売上を本社（請求集約先）に一括請求する機能。DB・得意先マスタ・請求締め・締め解除・消込・入金入力・得意先元帳・請求書帳票の8領域にまたがるため段階実装とする。用語・確定した業務ルール・8領域すべての設計方針は `docs/design_document.md` 28章・`docs/database-schema.md` 1-1節に確定済み。**12-Bは12-Cに依存しない**（請求集約元が1件も無ければグループは常に単独になるため、12-Bを先に入れても既存の単独得意先の挙動は変わらない）。

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 12-A | 設計資料の確定・DBスキーマ変更・得意先マスタ（`billing_customer_code`の追加、計算列を使った複合自己参照FK・CHECK制約、`CustomerService`のリンク検証・変更可否判定、得意先マスタ画面） | Opus | 設計確定済み | **2026-09-29実装。** `docs/database-schema.md` 1-1節・2.1節、`docs/product-spec.md`、`docs/design_document.md` 28章、`docs/report-spec.md` 2-2-1節に設計方針を確定。`scripts/020_add_billing_customer_code.sql`で`billing_customer_code`＋計算列`is_billing_root`／`billing_parent_root_flag`（PERSISTED）＋複合自己参照FK`FK_customers_billing_customer`＋CHECK`CK_customers_billing_customer_tax_unit`を追加し、開発用ライブDBへSQLCMDで適用。孫（3段階層）・締め日不一致・税区分不一致・端数区分不一致・都度得意先の請求集約元指定の5パターンをSQLで直接INSERTし拒否されることを確認済み（確認用データは後片付け済み）。`Customer`エンティティに`BillingCustomerCode`（required）・`IsBillingRoot`を追加（EFには計算列をマップしない）。`BillingAggregationValidator`（Domain純粋関数）＋`CustomerService`（リンク検証・業務ルール7の変更可否判定・`DeactivateAsync`のガード）＋`CustomerMasterViewModel`／`CustomerMasterWindow.xaml`（請求得意先コード欄、Space検索・Enter照会、`IsBillingCustomerEditable`）＋`CustomerSearchDialogViewModel`（`BillingRootOnly`フィルタ）を実装。単体テスト`BillingAggregationValidatorTests`（8件）・結合テスト`CustomerServiceTests`（9件、新規`Master/`フォルダ、外側トランザクション＋Rollback方式）を追加、全件green。既存テストへの回帰なし（Domain 289件all green。Application は開発用DBのseedデータ乖離による既存失敗125件を除き全green、TODO.md X-6時点と同数）。`dotnet run`でアプリ起動しメインウィンドウ表示・例外なしを確認。請求締め・消込・入金入力・元帳・帳票（Phase 12-B〜E）は次セッション以降 |
| [x] | 12-B | 日付制限・消込のスコープ拡張（`BillingClosedDateService`が請求集約先を解決してから`billings`を見るよう修正、`SettlementService.RecalculateForCustomerAsync`を請求集約グループスコープへ拡張・改称、入金入力で請求集約元を拒否） | Sonnet | 12-A | **2026-09-29実装。** 改称のみ（`RecalculateForBillingGroupAsync`）を挙動変更と別コミットに分離。`SettlementService`は対象クエリ（`sales`/`receipts`/`receipt_allocations`）をグループの得意先コード集合へ広げ、`TaxUnit`分岐より後でグループ解決することで都度得意先の無駄なクエリを避けた（配分本体は無改修。グルーピングキー・整列キーが全社一意のため得意先集合へ広げても成立）。**実装前は請求集約先への入金が自身の売上だけと突き合わされ、請求集約先の売上が未入金でも消込完了になり請求集約元は永久に未消込のまま残る金額バグがあった。** `BillingClosedDateService`は`billings`照会前に`BillingCustomerCode`を解決するよう修正し、`ReceiptEntryService.EvaluateEditLockAsync`の同一クエリ重複も統合。入金入力は`ReceiptEntryService.GetClosingCustomerAsync`（Application）と`ReceiptEntryViewModel.ApplyCustomerAsync`（Presentation）の両方に請求集約元の拒否を実装（既存の都度得意先拒否と同じ二重化。ViewModelに事前チェックが無いと検索モーダル経由の例外がfire-and-forgetで握り潰されるため）。結合テスト`SettlementServiceTests`に4件（混在グループの消込・古い順配分・返品行・未請求売上）、`SalesServiceBillingClosedDateTests`・`ReceiptEntryServiceTests`に1件ずつ追加、全green。全体テスト（Domain 289件／Application は開発用DBのseedデータ乖離による既知の失敗125件を除き全green、12-A時点と同数）。開発用DBに請求集約元を持つ`receipts`行が0件であることをSQLで確認済み。実機確認は`dotnet run`でのアプリ起動まで。詳細は`docs/design_document.md` 28-6章 |
| [x] | 12-C | 請求締め・締め解除（`BillingClosingService`の締め対象を請求集約先のみに絞り売上・入金抽出をグループへ拡張、`BillingReleaseService`の再計算スコープ対応） | Sonnet | 12-B | **2026-09-29実装。** `BillingClosingService.BuildCandidatesAsync`の`customers`クエリに`BillingCustomerCode == CustomerCode`を追加し、請求集約先のみが締め対象の候補になるようにした（実装前は請求集約元が自分自身の`billing`を単独で確定してしまう業務ルール3違反があった）。`BuildCandidateAsync`は対象が請求集約先であることが確定した時点でグループの得意先コード集合を解決し、`salesQuery`と`receiptQuery`の両方をグループへ広げる（単独得意先の実行計画を保つため`Contains`は2件以上のときだけ使う）。レビューで追加の実害を発見: `CustomerService.HasBillingChangeLockAsync`は`receipts`の有無を見ないため、得意先が過去に単独で入金を受けた後に他の得意先の請求集約元へ変更されるケースがあり得る。この場合`receiptQuery`を広げていなければ請求額の算定から漏れて`SettlementService`と食い違うため、`receiptQuery`も同じグループへ広げて解消した。**`BillingReleaseService`は改修不要と確認済み**（対象抽出は`billing_date`のみ、売上の紐付け解除は`billing_number`所属のみで判定するため`customer_code`で絞っておらず、消込再計算は12-Bで実装済みの`RecalculateForBillingGroupAsync`をそのまま呼ぶだけで請求集約元も正しく巻き戻る）。結合テスト`BillingClosingServiceTests`に2件（グループ合算後に1回だけ丸めることの証明＝個別丸め20円に対しグループ丸め21円になるケース、請求集約元名義の残存入金が合算されるケース）・`BillingReleaseServiceTests`に1件追加、全green。全体テスト（Domain 289件／Application は開発用DBのseedデータ乖離による既知の失敗125件を除き全green、12-B時点と同数）。開発用DBに請求集約元名義の`billings`行が0件であることをSQLで確認済み。実機確認は`dotnet run`でのアプリ起動まで（請求締め・締め解除画面は締め日・請求日の指定のみで得意先個別選択が無いため画面側の変更確認は不要）。詳細は`docs/design_document.md` 28-7章 |
| [x] | 12-D | 請求書帳票（`InvoiceDocumentBuilder`で請求集約元ごとの見出し行・小計行、`ReportPagination`の改ページ対応） | Sonnet | 12-C | **2026-09-29実装。** `InvoiceService.GetByNumberAsync`の明細並び順を得意先コード優先に変更し（単独得意先は挙動不変）、新設の`InvoiceReportRowBuilder`（Domain純粋関数）に渡して得意先ごとの見出し行・明細行・小計行の表示順を組み立てる。明細のCustomerCodeが1種類だけ（＝集約していない請求書）なら見出し行・小計行を挟まず明細のみを返すため、**既存の単独得意先の帳票はバイト単位で不変**。見出し行がページ末尾に孤立する事故を防ぐため、既存の`ReportPagination.Split`は無改修のまま新設の`AvoidTrailingHeaderOrphans`で後段調整する。`PagedReportDocumentBuilder`に`IsGroupMarkerRow`／`IsPageBreakSensitive`の2フックを追加（既定false、`DetailInvoiceDocumentBuilder`に無影響）。集約時のみフルヘッダーに「請求集約元: N社」の1行を追加（Nは請求集約先自身を除く件数、`AggregatedChildCount`）。単体テスト`InvoiceReportRowBuilderTests`（5件）・`ReportPaginationTests`に5件追加、結合テスト`InvoiceServiceTests`に1件追加（`BillingClosingService.ConfirmAsync`で実際に合算締めした請求書を読み、明細が得意先コード順に両得意先分含まれることを確認）。全体テスト（Domain 299件全green／Applicationは開発用DBのseedデータ乖離による既知の失敗125件を除き全green、12-C時点と同数）。WPFの`FixedDocument`描画の見た目自体を検証する自動テストはこの環境に無いため未実施（Phase 10-3〜10-5と同様の既知の限界）。実機確認は`dotnet run`でのアプリ起動まで。詳細は`docs/report-spec.md` 2-2-1節・`docs/design_document.md` 28-8章 |
| [x] | 12-E | 得意先元帳（`CustomerLedgerBuilder`に取引履歴のみモード追加、請求集約先はグループ売上を含めて残高計算） | Sonnet | 12-B | **2026-09-29実装。** `CustomerLedgerBuilder.Build`が`!Customer.IsBillingRoot`（請求集約元）で取引履歴のみモードへ分岐（`BuildTransactionHistoryOnly`、売上行のみ・残高/繰越/税/入金は常に0）。請求集約先側は無改修（`Customer.CustomerCode`を参照しないため、呼び出し元がグループ展開した`Sales`/`Receipts`を渡すだけで残高が合算になる）。`CustomerLedgerQueryService.GetAsync`は`IsBillingRoot`かつ`TaxUnit != Line`のとき`Sales`と`Receipts`の両方をグループへ展開（`SettlementService`/`BillingClosingService`と同じイディオム）。**設計時に想定していなかった穴を実装中に発見**: `CustomerService.HasBillingChangeLockAsync`が`receipts`の有無を見ないため過去に単独入金を受けた得意先が後から請求集約元になり得る（28-7節で判明済みの穴が元帳にも影響）ため、`Receipts`もグループ展開が必要と判明し対応した（`Billings`は対象得意先自身のコードのままで正しい）。`GetBalanceAsOfAsync`は請求集約元に`null`を返す（残高0円と区別）。`CustomerLedgerEntry`に`CustomerCode`/`CustomerName`を追加しグループ内のどの得意先の伝票か行単位で識別可能にし、`CustomerLedgerResult.IsBalanced`は取引履歴のみモードで常にtrueへ短絡。画面（`CustomerLedgerViewModel`/`CustomerLedgerWindow.xaml`）は「得意先」列を常時追加、請求集約元では残高・繰越関連の表示を`IsBalanceVisible`で隠し案内文を表示する。単体テスト`CustomerLedgerBuilderTests`に2件・結合テスト`CustomerLedgerQueryServiceTests`に2件追加。全体テスト（Domain 301件全green／Applicationは開発用DBのseedデータ乖離による既知の失敗127件を除き全green、Phase 12-D時点の125件から2件増加。増加分は新規テスト2件が既存の`products`/`bank_accounts`マスタ乖離という既知の原因でFK違反になったものであり、実在するコードへ一時的に差し替えて実行し正しく振る舞うことを確認済み・差し替えはコミットしていない）。開発用ライブDBには請求集約ペアのマスタ行が既に存在する（`1001`＝請求集約先 ← `1005`＝請求集約元、両者とも伝票は0件）ことをSQLCMDで確認済み。実機確認は`dotnet run`でのアプリ起動まで（UI Automationは未実施）。詳細は`docs/design_document.md` 28-9章・21-4章R8 |
| [x] | 12-F | ドキュメント整備（実装で判明した内容を`docs/`へ反映） | Sonnet | 各タスク完了時 | **2026-09-29実装。** 12-A〜12-Eの実装内容は各タスク完了時に`docs/design_document.md` 28章へ随時反映済み（28-5〜28-9）だったため、本タスクは横断的な整合性チェックとして次を修正した。①`docs/database-schema.md` 1-1節の「日付制限への影響（既知の注意点）」が、Phase B（2026-09-29）で既に解消済みの問題を未解決のまま記述していたため、解消済みである旨に書き換え（`docs/design_document.md` 28-6節への参照を追加）。②`docs/design_document.md` 21章（Phase 8-1/8-2、得意先元帳の原設計）の先頭に、Phase E（28-9節）による拡張の概要と申し送りへの参照を追記（原設計のD-1〜D-5決定は変更されていないことを明記）。③21-4章の申し送り事項にR8を追加（R6「Phase 9-1は`GetAsync`の結果を`monthly_closings`へ1:1で詰めるだけで済む」という前提が、Phase Eの取引履歴のみモードにより請求集約元に対して崩れたため、Phase 9-1実装時の注意点として記録）。④28章のトップ・実装順序図・8領域一覧・「修正不要と確認済み」節の進捗表記をPhase E完了に合わせて更新。`docs/product-spec.md`・`docs/report-spec.md`・`docs/architecture.md`は該当箇所を確認したが追加の乖離なし（`product-spec.md`は既に得意先元帳の挙動を正しく記述済み、`architecture.md`はこの機能に触れていない）。 |

---

## Phase 13: 請求書の振込先口座（得意先ごとの紐づけ）

得意先マスタに最大2つの銀行口座（`bank_accounts`）を紐づけ、請求書・明細請求書の自社名（発行者情報ボックス）の直下に印字する。従来の全社共通フラグ方式（`bank_accounts.is_print_on_invoice`）は完全に廃止し、得意先単位の紐づけ方式へ置き換えた。紐づけが0〜1件でも印字位置の高さを固定し、得意先によってレイアウトがずれないようにする。設計方針は `docs/report-spec.md` 2-2節・`docs/database-schema.md` 2.1節・2.6節・`docs/design_document.md` 23-7節参照。

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [x] | 13-1 | DBスキーマ・Domain・Application・帳票・得意先マスタ画面の実装 | Sonnet | 設計確定済み | **2026-09-29実装。** `scripts/021_add_customer_bank_accounts.sql`で`customers`に`bank_account_code1`／`bank_account_code2`（NULL許容varchar(10)、FK 2本、重複禁止CHECK）を追加、`bank_accounts.is_print_on_invoice`を削除し、開発用ライブDBへSQLCMDで適用済み（適用前後でスキーマを`sys.columns`等で確認）。`Customer`エンティティに2プロパティ追加、`BankAccount`から`IsPrintOnInvoice`を削除。`CustomerService.UpdateAsync`は値が変わったときのみ`ValidateBankAccountLinksAsync`（重複・実在チェック）を実行し手動コピーする方式（`BillingCustomerCode`と同じ方針。既存の紐づけをそのまま保存し直す分には、紐づけ先が後から無効化されていても拒否しない）。`BankAccountService.DeactivateAsync`に、得意先から紐づけられている口座は無効化できないガードを追加。`InvoiceService`／`DetailInvoiceService`は得意先の`BankAccountCode1/2`からスロット順に口座を解決する方式に変更（`InvoiceData`／`DetailInvoiceData`の`PrintBankAccounts`を`BillingBankAccounts`に改称）。`ReportDocumentBuilder.BuildBillingBankAccountsBox`（発行者情報ボックス直下の独立枠。紐づけ0〜1件でも「見出し1行＋4行」の固定行数で組み立てるため高さの明示指定は不要）で旧`BuildBankAccountsBlock`（全社共通フッター印字）を置き換え、`FullHeaderHeight`に`BillingBankAccountsBoxHeightEstimate`（100.0、安全側の見積り）を加算（`FooterHeight`は据え置き。過大なヘッダー見積りは行数減のみで安全、過小なフッター見積りが重なりの原因になるため）。`CustomerMasterViewModel`／`Window`に振込先口座1・2欄（`BillingCustomerCodeText`と同じSpace検索・Enter照会パターン、既存の`BankAccountMasterSearchDialog`を再利用）を追加、`BankAccountMasterViewModel`／`Window`から印字フラグのUIを削除。`docs/database-schema.md`・`docs/design_document.md`・`docs/report-spec.md`を更新。`dotnet build`成功、`dotnet test tests/bmcs_app.Domain.Tests`（301件all green）、`dotnet test tests/bmcs_app.Application.Tests`（既知のseedデータ乖離による失敗127件のみ、Phase 12-E時点と同数＝新規回帰なし。`CustomerServiceTests`9件は全green）を確認。**実機確認（ユーザー、2026-09-30）**: 得意先マスタで振込先口座2件を紐づけ、請求書を印刷プレビューし、発行者情報ボックスの直下に「お振込先」枠が正しく表示されることを確認済み（2口座・単一ページのケースのみ）。0件・1件のケースでの高さの一致、複数ページ時の改ページ境界の目視確認は未実施 |

---

## 横断タスク

| 完了 | # | タスク | 推奨モデル | 前提 | 完了条件 |
|---|---|---|---|---|---|
| [ ] | X-1 | テスト方針の決定と単体テスト基盤の構築（税計算・消込・締めを最優先で対象にする）。**単体テスト基盤（`tests/bmcs_app.Domain.Tests/`、xUnit v2）は5-1で前倒し済み。開発用DBに対する結合テストの器（`tests/bmcs_app.Application.Tests/`）は4-1で前倒し済み**（`docs/architecture.md` 16章）。残りは消込・締めの結合テストの個別実装 | **Opus** | 0-2 | 金額計算ロジックがテストで守られている |
| [ ] | X-2 | 排他制御の動作確認（rowversionによる楽観的排他、競合時のUI挙動） | **Opus** | 0-3 | 2端末同時更新で後勝ちにならない |
| [ ] | X-3 | 設計上の残課題の解消（旧 REVIEW.md の未完了項目を集約）。①用語集の新設（`docs/product-spec.md` に章を追加し、CLAUDE.md の Docs map の「用語」と一致させる。旧M-16/P-3）②画面名称の統一（CLAUDE.md のフロー図・補助機能の表記を `docs/design_document.md` の画面一覧の正式名称に合わせる。旧C-10。X-4と同件）③`docs/design_document.md` を `screen-spec.md` へリネームするかの要否判断（旧P-5。参照が多く影響範囲が大きい）④採番規則の業務確認（旧M-2。実装済みで確認のみ未実施）⑤顧問税理士への確認（旧C-4b。適格請求書の端数処理と税区分の整合。確認までの暫定は税区分どおり。`docs/decisions.md` 参照） | **Opus** | ④⑤は各回答 | 各項目が `docs/` に反映され、「暫定」の記述が消えている |
| [ ] | X-4 | 設計資料の同期（実装で確定した内容を `docs/` に反映。重複記載のSSOTへの集約＝D-1〜D-6、画面名称の統一＝C-10）。**2026-09-10、D-1〜D-6を`CLAUDE.md`／`docs/design_document.md`／`docs/product-spec.md`へ反映済み。** 残るのはC-10（画面名称の統一） | Sonnet | 各フェーズ完了時 | 資料と実装が乖離していない |
| [x] | X-6 | ジャーナル系の日付制限（請求集計済み期間への新規登録・日付変更の禁止、21-4章R2の解消） | Sonnet | 6-1, 7-2, 7-5 | **2026-09-16実装。** 対象は売上入力・入金入力の2画面のみ（明細入金・受注入力は対象外、都度得意先は制限なし。ユーザー確認済み）。得意先ごとに「確定済み`billing`のうち最新の`billing_date`＋1日」を最小日付とし、`SalesService.CreateAsync`／`UpdateAsync`・`ReceiptEntryService.SaveNewAsync`／`UpdateAsync`で強制する（`BillingClosedDateEvaluator`／`BillingClosedDateService`、新規）。画面側は`DatePicker.DisplayDateStart`で利便性を提供しつつ、得意先確定時に現在日付が最小日付より前なら自動補正・通知する。月次締め分はPhase 9-1へ再申し送り。Domain単体テスト・Application結合テスト（開発用ライブDB）を追加し、既存`ReceiptEntryServiceTests`3件はテストデータの組み立て順序を実際の業務順序に合わせて修正した（ルールは緩めていない）。全体テスト（Domain 281件／Application既存分は開発用DBのseedデータ乖離による既存失敗125件を除き全green）。詳細は`docs/design_document.md` 25章 |
| [x] | X-5 | 全画面デザイン統一。**2026-09-15完了。** ①TextBox/ComboBox/DatePicker/Buttonの高さを共通スタイル（`Styles/Metrics.xaml`・`CommonControlStyles.xaml`）で統一、②日付入力欄を`DateTextBoxStyle`（TextBox+マスク入力）から標準`DatePicker`へ全10箇所移行（8桁ベタ打ち入力は`DatePickerInputBehavior`で維持。`docs/architecture.md`12章・13章改訂）、③SPACE検索欄のWatermark表記を「SPACEで検索」に統一 | Sonnet | 0-5, 0-6 | 全画面で入力欄の高さが揃い、日付欄がDatePickerで統一され、Watermark表記が統一されている |
| [ ] | X-7 | ソースコードのコメントの整理（docs整理に伴う参照ずれ・経緯表現の除去）。①「design_document.md 21-4章 申し送り事項R2」等のR番号参照（`SalesEntryViewModel`・`ReceiptEntryViewModel`・`BillingClosedDateService`・`BillingClosedDateEvaluator` ほか約10か所）を、節の主題（元帳が正・billings は締め時点のスナップショット）で参照する形へ②`SettlementAllocator` のクラスコメントの「2分岐」を、実装（pool=0／全額充当／不足の3ケース）に合わせて修正③`BillingReleaseService` の「再消込は7-2の責務」、`SalesEditLockEvaluator.cs` の Phase 番号など、経緯を含むコメントの現在形化 | Sonnet | なし | コメントが現在の docs の章・節と一致し、Phase 番号・経緯を含まない |

---

## 進め方のルール

1. **1タスク＝1セッション**を目安にし、着手時に該当タスクの推奨モデルへ切り替える。
2. 非自明なタスクは**プランモードで方針を確認してから実装**する。特に Opus 指定のタスクは必ず。
3. **DBスキーマを変更したら、必ず `scripts/` に連番SQLを残してからSQLCMDで適用する。** コードとDBの乖離を防ぐため。
4. タスク完了時は**動作を実証してから完了扱いにする**（テスト実行・画面での確認）。金額計算は手計算との一致を確認する。
5. 実装中に設計資料の誤り・不足を見つけたら、`docs/` を直してからコードを書く。**コード側で辻褄を合わせない。**
6. このファイルは進捗に応じて更新する（完了したタスクは `完了` 列を `[x]` にする、新たに判明したタスクを追加する）。