# bmcs_app 概要

石山商店の**販売管理システム**（受注 → 売上 → 請求 → 入金 → 元帳 → 月次締め）。新規構築中。

- 取引先は2種類、業務フローが異なる:
  1. 一般企業（締め取引）: 締め日で1ヶ月分まとめて請求・入金消込
  2. 官公庁・学校・都度取引先（明細・都度請求）: 売上1件〜数件ごとに明細請求書を都度発行、明細単位で消込
- 在庫・発注・仕入・買掛・支払は今回のスコープ外（将来連携を見据えてカラムは削らない）。

## 技術スタック

- C# / WPF / 4層構造+MVVM / MahApps.Metro / SQL Server 2022 Express（実機）/ EF Core（O/Rマッパーのみ、マイグレーション機能は使わない）/ rowversion楽観的排他 / Windows 11+
- `net10.0-windows`（WPF exe）、`net10.0`（他3層）。Nullable参照型有効、警告はエラー扱い（`Directory.Build.props`）。

## プロジェクト構成（4層=4プロジェクト、機能はフォルダで分割）

```
bmcs_app.sln
Directory.Build.props
src/
 ├─ bmcs_app/                  … Presentation (WPF, exe)。ViewModels/Views を Order/Sales/Receipt/Billing/Closing/Ledger/Menu/Master/Common で分割
 ├─ bmcs_app.Application/      … 業務処理層（ユースケース、トランザクション境界）。Order/Sales/Receipt/Billing/Closing/Ledger/Master/Common
 ├─ bmcs_app.Infrastructure/   … データアクセス層（EF Core DbContext, Configurations/）
 └─ bmcs_app.Domain/           … ドメイン層（エンティティ, enum, Calculations/, Numbering/）
tests/
 ├─ bmcs_app.Domain.Tests/     … DB不要の単体テスト（xUnit v2）。消費税計算・単価決定・税額分岐・編集ロック判定等
 └─ bmcs_app.Application.Tests/… 開発用ライブDBへの結合テスト（DevDatabaseFixtureで実接続）。採番・各画面の登録/訂正/取消・消込・フェーズレビュー
scripts/                       … DDL（001, 002, ... 連番。適用済みは改変しない）、seed_dev_data.sql
docs/                          … 設計文書（下記）
```

**進捗（2026-11-07時点）**: Phase 0〜12（親子請求）・月次締め・帳票・売上粗利分析・文書整備（DOC-1〜3）まで完了し、運用文書（`docs/setup.md`・`operations.md`・`release.md`）も整備済み。9月末以降に加わった主な機能: タイル型メインメニュー（カテゴリ色分けなし）、売上・粗利分析（担当者別・得意先別・商品別・仕入先別をタブ切替の1画面）、得意先×商品の単価・定価掛率マスタ、商品のJANコード・定価、コピー機CSV取込・コピー機マスタ、納品書一括発行、消費税率マスタ管理、仕入先マスタ・仕入先別原価（商品×仕入先）、起動時スプラッシュ。残りの未完は`TODO.md`の`[ ]`行（X-3 設計残課題、X-4 設計資料の同期、X-8 締め入金の消込廃止の検討〔未決定〕、X-9 ベータ区切りでのスクリプトのベースライン化〔時期未定〕）。詳細は`TODO.md`（各Phaseの完了行に実装の要点が記録されている）。`scripts/`の連番は043（`043_add_slip_supplier_code.sql`）まで適用済み。
- 月次締め（Phase 9、詳細は`docs/design_document.md` 29章）: `Application/Closing/`に`MonthlyClosingService`（確定。全得意先を暦月単位、独自トランザクション）・`MonthlyClosingReleaseService`（解除。年月単位一括・非破壊・All-or-nothing）・`MonthlyClosingQueryService`（一覧）・`MonthlyClosedService`（月次締め済み判定の集約。編集ロックと新規登録の禁止の両方が使う）。計算は`Domain/Calculations/MonthlyClosingCalculator`（当月残高＝元帳の月末残高、前月残高＝前月確定行の当月残高（無ければ元帳の月初残高）、消費税額＝逆算で差を吸収。請求集約元の行は自社売上と税率別対価額のみ）。画面は`Views|ViewModels/Closing/`（`MonthlyClosingWindow`、`MonthlyClosingReleaseWindow`）。元帳の入力組み立ては`CustomerLedgerQueryService.GetInputAsync`に切り出し済み。
- 月次締め済みの判定＝`monthly_closings`に「月末日一致・確定・`customer_code`が売上の得意先またはその請求集約先」の行がある（請求集約先の行でも請求集約元の伝票をロック）。締め済みの月への売上・入金（締め入金・明細入金）の新規登録も保存時に拒否（9-2）。
- 権限はメニュー単位のみ（C-8）。「月次締め解除処理」はレベル9のメニュー（`scripts/014_seed_menu_structure.sql`、テーブル名は`menus`）。
- 得意先元帳（Phase 8）: 得意先・期間のいずれかを変更すると自動的に再表示する（`CustomerLedgerViewModel`。2026-09-17、専用の「表示」ボタンは撤去。F5キーは手動再表示用に残す）。
- 帳票基盤（Phase 10）は`src/bmcs_app/Reports/`配下。`ReportDocumentBuilder`（A4寸法・`Tb`/`HLine`・宛先ブロック・明細1行描画等の描画プリミティブ、抽象メンバー無し）と、改ページを伴う単一フロー帳票（請求書`InvoiceDocumentBuilder`・明細請求書`DetailInvoiceDocumentBuilder`）用のテンプレートメソッドを持つ`PagedReportDocumentBuilder`（`ReportDocumentBuilder`を継承、`Build()`と単一フロー用抽象メンバーを持つ）に分割されている（2026-09-17）。納品書`DeliveryNoteDocumentBuilder`は改ページの考え方が異なる（ミシン目入りA4に「納品書（控）」「請求書」「納品書」を3段複写で印字。1セクション6行固定）ため`ReportDocumentBuilder`を直接継承し、自前で`Build()`を実装する。
- マスタ画面のコード入力欄は「SPACEで検索モーダル／Enterでコード照会（名称ラベル表示）」パターンで統一（`windowService.ShowDialog<TDialog, TDialogVM, TEntity>()`＋`*MasterSearchDialog`、`RunBusyAsync`）。得意先マスタの請求得意先・振込先口座1/2に加え、営業担当社員コードも`EmployeeMasterSearchDialog`で選択可（`CustomerMasterViewModel`が`EmployeeService`を注入。仕入先も`SupplierMasterSearchDialog`で同パターン）。

主なユースケースサービス（`src/bmcs_app.Application/`）:
- `Order/`: `OrderService`・`OrderStatusService`・`OrderQueryService`
- `Sales/`: `SalesService`（新規/訂正/取消）・`SalesQueryService`・`SalesEditLockService`
- `Billing/`: `BillingClosingService`（請求締め）・`BillingReleaseService`（締め解除）・`DetailInvoiceService`（明細請求書発行/取消）・`DetailInvoiceQueryService`
- `Receipt/`: `SettlementService`（消込キャッシュ列の得意先単位全件再計算、`RecalculateForCustomerAsync`が唯一の書き手）・`ReceiptEntryService`（締め入金、新規/訂正/取消/編集ロック）・`ReceiptQueryService`・`DetailReceiptEntryService`（明細入金、新規/訂正/取消/編集ロック）・`DetailReceiptQueryService`

- `Master/`: 各マスタのサービス（`CustomerService`・`ProductService`・`SupplierService`・`ProductSupplierCostService`・`CustomerProductPriceService`・`TaxRateService`・`CopierMachineService`・`MenuService`等）と`UnitPriceService`
- `Analysis/`: `SalesSummaryQueryService`（得意先別・商品別・仕入先別）・`EmployeeSalesQueryService`（担当者別）。集計は保存せず`sales`から都度集計
- `Sales/`の`CopierSalesImportService`（コピー機CSV取込）・`DeliveryNoteService`（納品書）、`Billing/`の`InvoiceService`、`Common/`の`QueryRunnerService`・`PrinterSettingsService`・`ProductHistoryQueryService`等

編集ロック（訂正・取消の可否判定）は`sales`/`receipt`/`detail_receipt`で条件が異なる（C-6、`docs/database-schema.md` 1章参照）。`receipt`は月次締めに加え「請求締めスナップショット」（`BillingClosingService`が締め時に`receipt`合計を`billing.CurrentBillingAmount`へ焼き込み再計算しないため）が独自のロック条件。`sales`は4条件（請求締め・明細請求書発行済み・月次締め・入金済み）、`detail_receipt`は月次締めのみ。

受注入力・売上入力は`SlipLineViewModel`/`SlipLineControl`（Common）を共用。単価決定は`IUnitPriceCalculator`インターフェース（実装`StandardUnitPriceCalculator`、`Domain/Calculations/`。既定方針への例外的な抽象化、M-3）。決定順は ①得意先／商品／単価マスタ → ②得意先／商品／定価掛率マスタ（定価×掛率） → ③商品マスタ（税単位に応じた外税／内税単価）。マスタ行と税率の取得は`UnitPriceService`（Application/Master）が得意先・伝票日付ごとに1回で行う。

仕入先・原価（商品検索で決まる。詳細は`docs/database-schema.md` 2.2節・2.6-5/6節、`docs/decisions.md`）: 商品検索「マスタから」軸は商品マスタに`product_supplier_costs`（商品×仕入先）を LEFT JOIN した一覧。**有効な仕入先別原価がある商品は仕入先ごとの行のみ（標準原価の行は出ない）、無い商品は仕入先なし・標準原価の1行**（例: 標準原価100・仕入先X原価80 → X/80の1行のみ）。標準原価で売りたい商品は、仕入先別原価の行を空欄にして無効化する。商品コード直入力＋Enterは主仕入先とその原価（無ければ標準原価）。「過去の取引履歴から」軸は前回売上行の仕入先を使い、原価は現在の原価マスタから解決。明細の仕入先欄は表示のみ（変更は商品の選び直し）。実装: `ProductSearchDialogViewModel`（`GetSelectableCostsAsync`をメモリ結合）、`ProductSupplierCostService`、`SlipLineViewModel.ApplySupplier`、`SlipLineSupplierHelper`。\n\n依存方向: `Presentation → Application → Infrastructure → Domain`（古典的レイヤード、インターフェースでの逆転なし）。リポジトリ抽象化なし（Applicationが`DbContext`を直接使う）。

## ドキュメント地図（実装前に必ず確認）

正は `CLAUDE.md` の Docs map。実装前に該当文書を確認する。画面は名称で参照し、画面番号（SCR-xxx等）は使わない。docs には現在の仕様だけを書き、日付・決定ID・Phase番号・TODO項番・経緯は書かない（「なぜ」は`docs/decisions.md`）。

- `CLAUDE.md` — 作業ルール（指示が矛盾したら必ずユーザーに確認する）、Git運用、Purpose、Tech stack、業務フロー概要
- `README.md` — 概要と文書の入口
- `docs/setup.md` — 開発環境構築、DB作成・スクリプト適用、接続文字列、ビルド・起動・テスト
- `docs/operations.md` — ログ、障害・競合の対処、データ補正の原則、締め解除の業務手順、期首・年度運用
- `docs/release.md` — publish、配布、端末の前提、本番の接続、`bmcs_config.json`
- `docs/architecture.md` — 層構成、依存方向、トランザクション・排他、EF Core・命名規約、MVVM・共通UI、テスト構成、開発用DB環境
- `docs/product-spec.md` — 用語、UI/UX方針、共通業務ルール（認証含む）、請求集約、伝票の状態遷移
- `docs/design_document.md` — 画面ごとの要点（章番号順）、未解決の不明点（DB以外）
- `docs/database-schema.md` — テーブル設計方針・定義・スキーマ変更とバックアップの運用・暫定運用中の項目（DB関連の詳細はここに書く）
- `docs/report-spec.md` — 帳票エンジン（印刷・PDF経路）、各帳票のレイアウト要件
- `docs/decisions.md` — 非自明な意思決定とその理由（不採用案の理由、罠、性能・制約）
- `TODO.md` — 開発タスク一覧、モデル選択基準、保留項目の扱い方針
- `docs/user-manual-jp/`（操作マニュアルHTML）・`docs/release-note/`・`docs/leaflet-jp/` — 利用者向け資料。画面を変えたらマニュアルの該当ページと画像も合わせる

## Git運用

- ブランチは `main`（本番相当）と `develop`（統合）があり、機能は `feature/…`（`fix/…`・`docs/…`）で作業して `develop` へマージ、区切りで `develop` を `main` へマージしている。`main` への直接コミットは文書の軽微な修正だけ。
- コミットは1目的1コミット。DDLは`scripts/`の新しい連番SQLと、それを使うコードを同じコミットに含める。適用済みの連番SQLは改変しない。`reset_test_data.sql`・`seed_dev_data.sql`は破壊的なので本番へ流さない。
- 改行・文字コードは`.gitattributes`・`.editorconfig`に従う（LF・UTF-8）。

## 開発用DB環境

- サーバ `172.16.3.171`（SQL Server 2022 Express）、DB名 `bmcs_db`。同一サーバに他システムDBが同居するため **DDLは必ず`bmcs_db`を明示**。
- 接続文字列は `src/bmcs_app/appsettings.Development.json`（gitignore対象、`.sample`のみコミット）。
- DBスキーマ変更はEF Coreマイグレーションを使わず、**AIがSQLCMDでライブDBに直接DDLを適用**する運用（`scripts/00N_*.sql`を連番で追加、適用済みファイルは改変しない）。
