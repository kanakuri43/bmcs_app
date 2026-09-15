# 帳票仕様（report-spec）

> `REVIEW.md` M-10（帳票の出力方式）の決定事項を記録する。各帳票（納品書・請求書・明細請求書・得意先元帳等）のレイアウト詳細は、実装フェーズで随時この文書に追記する。

---

## 1. 帳票エンジンの決定（2026-09-10 確定）

### 決定

**WPFの `FixedDocument`/`FixedPage` を C# コードで直接組み立てる方式を採用する。** RDLC・Crystal Reports・FastReport・QuestPDF等の外部帳票デザイナー・ライブラリは使わない。

- 出力経路は2つ。どちらも同じ `FixedDocument` を使う。
  - **A4レーザープリンタへの直接印刷**（基本の出力経路）: `PrintQueue`/`PrintDialog` で印刷する。プリンタの選択は既存の `PrinterSettingsConfig`（帳票種別ごとの設定済みプリンタ）を踏襲する。ダイアログは出さない。
  - **PDF保存・メール送付**: 都度ユーザーが「PDF保存」ボタンを押し、Windows標準搭載の**「Microsoft Print to PDF」仮想プリンタ**に印刷する形で実現する。保存先はユーザーがダイアログで選ぶ（1件ずつでよい）。追加ライブラリ・ライセンスは不要。
- 参考実装: `bmcs_app.Sales/Services/SalesPrintHelper.cs`（別リポジトリの先行実装、納品書印刷）と同じパターンを、他帳票（請求書・明細請求書・元帳）にも展開する。

### 経緯・不採用にした案

- 当初、請求書・明細請求書のPDF保存・メール送付について「複数件をダイアログなしで自動的にPDFファイル化して送信する運用（一括発行画面での利用）が必要」という前提でQuestPDF（C#製の帳票ライブラリ、印刷とPDF生成を1つのレイアウト定義から出せる）への一本化を検討した。
- しかし QuestPDF の無償 Community License は「年間売上高$1M未満の企業」限定であり、石山商店がこれに該当するか確認が取れず、該当しない場合は有償ライセンスが必要になる。
- 改めて確認した結果、**PDF保存・メール送付は「都度ユーザーがボタンを押して保存先を選ぶ」運用で進める**ことになった。この運用であれば「Microsoft Print to PDF」で十分であり、QuestPDFは不要と判断し、既存のFixedDocument方式をそのまま踏襲する。
- CLAUDE.md記載の「データ横断検索・納品書一括発行画面」は**複数件の印刷**を指すもので、PDFファイルの自動生成を指すものではないという理解で進めている。この理解が異なる場合（＝一括でPDFファイルを自動生成・送信する必要が出た場合）は、改めてQuestPDF等の導入を再検討する。

---

## 2. 各帳票のレイアウト要件

### 2-0. 帳票基盤（TODO.md 10-3、2026-09-15実装）

M-10で決定した方式を、以下の構成で実装した。詳細は`docs/architecture.md` 5章補足を参照。

- **データ取得（Application、業務領域フォルダ）**: 各帳票のデータ取得サービス（例:
  `DeliveryNoteService`、`src/bmcs_app.Application/Sales/`）が AsNoTracking で読み込み、
  WPF 型を含まないプレーンな DTO（例: `DeliveryNoteData`）を返す。値は decimal/DateOnly のまま
  持ち、書式（カンマ区切り・日付表記等）は付けない。
- **レンダリング・印刷・PDF・プレビュー（Presentation、`src/bmcs_app/Reports/`）**:
  - `ReportDocumentBuilder`（abstract）: A4寸法（210mm×297mm を正確に変換した
    793.7008×1122.5197 DIU）・行高・フォント・改ページ・明細テーブル描画の共通処理を持つ
    テンプレートメソッド基底クラス。改ページの分割計算自体は
    `src/bmcs_app.Domain/Calculations/ReportPagination.cs`（純粋関数、単体テスト済み）に
    委ねる。派生クラス（例: `DeliveryNoteDocumentBuilder`）はヘッダー・フッター・列定義・
    行データのみを与える。**派生クラスが1個の間はこの形を維持し、2枚目の帳票（10-5）を
    書いた時点で共通部分の不足が判明したら基底へ引き上げる**（将来の差し替えを見据えた
    抽象化は先回りして行わない方針）。
  - `ReportPrintService`（Singleton、`PrinterSettingsService`と同じ理由でDbContextに
    依存しない）: `Print(FixedDocument, ReportKind, jobName)` が設定済みプリンタへ
    ダイアログなしで直接送信し、失敗時のみ`PrintDialog`にフォールバックする。
    `PrintToPdf(FixedDocument, jobName)` が「Microsoft Print to PDF」へ送信する
    （保存先はこの仮想プリンタが表示するOS標準ダイアログに任せる）。両メソッドとも
    `PrintTicket.PageMediaSize`/`PageOrientation`をA4縦に固定する（プリンタの既定用紙が
    Letterの端末で縮小・欠けが起きるのを防ぐ）。エラー表示は行わず`ReportPrintResult`で
    結果を返し、呼び出し元がStatusMessageに表示する。
  - `ReportPreviewDialog`／`ReportPreviewDialogViewModel`
    （`Views/Common/`・`ViewModels/Common/`）: 全帳票共通のプレビュー画面。
    特定帳票の知識を持たず、`Func<FixedDocument>`（表示・印刷・PDF保存の都度呼び直す。
    `FixedPage`は1つのビジュアルツリーにしか属せないため`FixedDocument`を使い回さない）を
    受け取る。`DocumentViewer`は既定で組込みツールバー（標準の印刷ダイアログを開く）を
    持つため、「ダイアログを出さない」方針と矛盾しないよう`ControlTemplate`を
    `PART_ContentHost`だけの`ScrollViewer`に絞り、印刷・PDF保存・閉じるは自前ボタンに
    一本化した。**プレビューは`ShowDialog`（モーダル）で開く**
    （`docs/architecture.md` 4章補足）。
  - `ReportKind` enum（`DeliveryNote`/`Invoice`/`DetailInvoice`）は既存
    `PrinterSettings`（TODO.md 2-6）の3項目に一致させる。**得意先元帳（10-6）用のプリンタ
    設定は存在しない。10-6着手時に`PrinterSettingsConfig`・設定画面・enumへ追加する
    （先取りしない）。**

### 2-1. 納品書（TODO.md 10-4、2026-09-15実装）

- **適格請求書としては扱わない**（2026-09-15ユーザー確認）。「本書は適格請求書です」等の
  文言は印字しない。根拠: `docs/product-spec.md`共通業務ルール9は請求書・明細請求書のみを
  適格請求書の記載事項対象として挙げており、納品書を含めていない。どの書類を適格請求書と
  するかは税理士確認待ちの【要確認】事項（`docs/design_document.md` 2章）であり、**本決定は
  それを閉じるものではない**。確認の結果が変わった場合は`DeliveryNoteDocumentBuilder`の
  該当箇所（注記の追加）だけを直接修正する。
  - 登録番号・自社名・税率別内訳・軽減税率「※」注記は税単位に関わらず印字する
    （発行者情報・税率区分の記載自体は適格請求書か否かに関わらず有用なため）。
  - 自社情報（`CompanyInfo`）が未登録の場合は`DeliveryNoteException`で印刷を中止する
    （登録番号が発行者情報として欠落した紙を出さないため）。
- **税単位別のフッター**:
  - 請求単位（`TaxUnit.Invoice`）: 伝票時点で税額を一切持たない（CHECK制約で
    `slip_tax_amount`/`tax_amount`両方NULL強制）ため、**税率別内訳・消費税額は一切印字せず、
    税抜合計のみ**＋「※消費税は月次請求書にてご確認ください」を表示する。月次請求書側で
    税率ごとに1回丸め直すため、納品書側で仮計算した税額を印字すると請求書と食い違う事故に
    なるため。
  - 伝票単位（`TaxUnit.Slip`）: 税率別内訳（`ConsumptionTaxCalculator.CalculateExternalTaxBuckets`）
    ＋税抜合計／消費税合計／税込合計。消費税合計は**保存済み`SlipTaxAmount`**を使う
    （SUMしない。全行同値）。再計算した内訳合計が保存値と一致しない場合はログに警告する
    （丸め規約の退行検知）。
  - 内税明細単位（`TaxUnit.Line`）: `Amount`は税込のため、単価・金額の列ヘッダを
    「単価(税込)」「金額(税込)」にする。税率別内訳・消費税合計は**保存済み`TaxAmount`を
    行ごとに合算**して作る（再計算し直さない。端数区分変更後の伝票でDBと食い違うため）。
- **明細行の表示**: `SlipType`が行単位のため、返品・値引行の商品名に`[返品]`/`[値引]`を
  前置する（列を増やさず残余幅の商品名列に収める）。軽減税率対象商品には`※`を前置し、
  「※は軽減税率（8%）対象商品です」を注記する。担当者は`sales`に列が無いため印字しない
  （`docs/design_document.md` 6章と同じ理由）。
- **再発行**: `DeliveryNoteIssueCount >= 1`のときタイトル横に「（再発行）」を表示する。
  `DeliveryNoteIssuedAt`は**最終発行日時**（上書き。初回日時の履歴は保持しない）。
  発行は**印刷成功時のみ**カウントする。PDF保存はカウントしない
  （「Microsoft Print to PDF」はOS側の保存ダイアログをアプリから検知できず、
  キャンセルされたPDF出力を発行済みとして記録してしまう事故を避けるため）。
- **発行記録（`DeliveryNoteService.MarkIssuedAsync`）**: 請求済・入金済であっても再発行できる
  （`docs/product-spec.md`軸1「再発行は可能」）ため、`SalesEditLockEvaluator`の編集ロック
  （C-6の4条件）は適用しない。発行日時・発行回数は伝票内容ではなく帳簿外の記録列のため
  rowversionの楽観的排他制御も掛けず、集合更新（`ExecuteUpdateAsync`）で行う。
  `UpdatedBy`/`UpdatedAt`も更新しない（専用の発行日時列があるため、伝票内容を最後に
  編集した者の記録を印刷操作で上書きしない）。TODO.md 10-2の一括発行も本メソッドを
  そのまま再利用する。
  - **既知の注意点**: `ExecuteUpdateAsync`はChangeTrackerを経由しないため、同じ
    `BmcsDbContext`スコープで`SalesQueryService.GetSlipAsync`（追跡あり）等により当該伝票が
    既に読み込まれている場合、追跡中エンティティの`RowVersion`がDBの新しい値と食い違ったまま
    になる。SQL Serverの`rowversion`は列の値に関わらずどのUPDATEでも進むため、放置すると
    次の保存が偽の「他のユーザーが更新しました」エラーになる。そのため`MarkIssuedAsync`は
    更新後に同一スコープの追跡エンティティを`ReloadAsync`で最新化する
    （結合テスト`DeliveryNoteServiceTests.追跡中の売上行に対して発行記録しても後続の保存が競合エラーにならない`
    で検証済み）。
- **保存後の発行導線**: 売上入力画面は保存成功後、画面リセット（`New()`。
  `docs/product-spec.md`「登録後のリセット」）の前に「納品書を発行しますか？」の確認
  ダイアログを出し、Yesならプレビュー→印刷→発行記録まで行う（2026-09-15ユーザー確認）。
  印刷(F11)は既存伝票を読み込んでいる場合（訂正モード・プレビューモード）のみ有効で、
  再発行に使う。
- **M-15（明細行数の上限）**: 自動改ページ（`ReportPagination`）のため上限は設けない。

### 2-2. 請求書・明細請求書・得意先元帳（Phase 10-5・10-6）

未着手。着手時に本節へ追記する。得意先元帳は「前頁繰越／次頁繰越」を毎ページのフッターに
出す必要があり、納品書の「最終ページのみフッター」という形は使えない点に注意
（`ReportDocumentBuilder`を拡張する際に検討する）。
