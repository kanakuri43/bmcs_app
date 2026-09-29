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
- **M-15（明細行数の上限）**: 自動改ページのため上限は設けない（下記2026-09-17改訂後は
  1セクション6行固定でA4を複数枚に自動改ページする。行数計算に`ReportPagination`
  （Domain、請求書・明細請求書と共用）は使わず、固定行数で単純に分割する）。

#### 2-1-1. 3段複写（ミシン目入りA4）への変更（2026-09-17実装）

参考実装（`bmcs_app.Sales/Services/SalesPrintHelper.cs`の`BuildTripleDocument`系）を移植し、
単一フローのA4 1枚印字から、ミシン目で3等分されたA4用紙に同一内容を3セクション印字する形へ
変更した。タイトルは上から「納品書（控）」「請求書」「納品書」（3セクションとも内容は同一で
タイトルのみ異なる。複写紙の代わりに手で切り分けて配布する運用）。

- **明細行数**: 1セクション6行固定（`DeliveryNoteDocumentBuilder.LinesPerSection`）。
  ミシン目の位置を合わせるため、行数が足りない場合も罫線付きの空行で埋める
  （行数を可変にすると3セクションの高さが揃わずミシン目とずれるため）。7行目以降は
  次のA4用紙（3セクションとも「（続き）」表記の続紙ヘッダーになる）に送る。
- **帳票基盤の分割**: `ReportDocumentBuilder`（描画プリミティブ：A4寸法・`Tb`/`HLine`・
  宛先ブロック・自社情報ボックス・税率別内訳行・合計行・明細1行の描画）と、改ページを伴う
  単一フロー帳票のテンプレートメソッド（`Build()`・ヘッダー/フッターの高さ見積りに基づく
  `ReportPagination`呼び出し）を持つ新設の`PagedReportDocumentBuilder`に分割した。
  請求書・明細請求書は`PagedReportDocumentBuilder`を継承し、`DeliveryNoteDocumentBuilder`は
  3段複写という改ページの考え方自体が異なるため`ReportDocumentBuilder`を直接継承し、
  自前の`Build()`で3セクションを組み立てる（不要な単一フロー用抽象メンバーを実装させられる
  歪みを避けるための分割）。
- **フォント・行高**: 3セクション分の縦幅（A4を3等分）に収めるため、明細テーブルの
  行高・フォントサイズを通常の単一フロー帳票より縮小している
  （`CondensedLineHeight`/`CondensedTableHeaderHeight`/`CondensedFontSize`）。
  ヘッダー・フッターの構成要素（税率別内訳・合計行等）はそのまま再利用しつつ、
  宛先ブロックのみ縦幅節約のため郵便番号・住所を1行にまとめている
  （通常の`BuildCustomerBlock`は住所を複数行に分けるため、ここだけ専用の組み立てにした）。
- **再発行表示**: 「（再発行）」は3セクションのタイトルそれぞれに付く
  （`納品書（控）（再発行）`等）。

### 2-2. 請求書・明細請求書（TODO.md 10-5、2026-09-16実装）

締め得意先向け「請求書」（`InvoiceDocumentBuilder`／`InvoiceService`）・都度得意先向け
「明細請求書」（`DetailInvoiceDocumentBuilder`／`DetailInvoiceService.GetPrintDataAsync`）を
実装した。適格請求書の記載事項（発行者の名称・登録番号／取引年月日／取引内容と軽減税率の付記／
税率ごとに区分した対価の額と適用税率／税率ごとの消費税額／交付を受ける者の名称）をすべて満たす。

- **帳票基盤の共通化**: 10-3のコメントどおり「2枚目の帳票を書いた時点で共通部分の不足が判明したら
  基底へ引き上げる」を実行した。`ReportDocumentBuilder`（基底）に`BuildCustomerBlock`
  （宛先ブロック）・`BuildCompanyInfoBox`（発行者情報ボックス。代表者印字の有無を引数で切替）・
  `BuildBillingBankAccountsBox`（振込先口座ボックス。2026-09-29、下記「振込先」参照）・
  `BuildBreakdownRow`／`BuildLabelValue`／`BuildTotalRow`を`protected`として引き上げ、
  `DeliveryNoteDocumentBuilder`もこれらを使うよう移行した（表示内容は変えていない）。
- **明細の粒度**: 請求書・明細請求書とも売上明細行ごと（品目別）に印字する。どちらも複数の
  売上伝票にまたがるため、納品書には無い「伝票No.」列を明細テーブルに持つ。
- **税率別内訳の「適用税率」表示（設計判断）**: `billings`／`detail_invoices`は税種別区分ごとの
  確定金額（固定5カラム）のみを持ち、税率(%)そのものは保持しない。一方、明細を構成する
  `sales`行は税単位によらず必ず`tax_rate`をスナップショットとして持つため、
  `ConsumptionTaxCalculator.ResolveConfirmedBuckets`（Domain、単体テスト済み）が
  「金額はヘッダーの確定値をそのまま使い、税率(%)ラベルだけを該当する税種別区分を持つ明細行から
  拝借する」方式で内訳を組み立てる。これにより、伝票単位が伝票ごとに端数処理する構造（C-4b暫定）
  と、請求全体で1回だけ丸め直す表示との二重丸めによる金額不一致を避けている。対価額・税額が
  ともに0の区分は表示しない。明細行に該当区分が無い場合（解除済み・取消済みで明細0件、下記参照）
  は税率0でフォールバックする。
- **請求書サマリー**: `billings`ヘッダーの本体データ（前回請求額・ご入金額・今回売上額・
  消費税額・今回ご請求額）を明細テーブルの上に表示する。フッターの税率別内訳は本請求期間の
  売上・消費税（`SalesAmount`／`TaxAmount`）に対するものであり、前回残高・入金を含む
  今回ご請求額とは別物のため表示を分離している。
- **締め解除済み・取消済みを指定した場合**: `sales.billing_number`（締め解除で`NULL`に戻る）・
  `detail_invoice_sales_lines`（取消で物理削除される）という既存の非破壊ヘッダー方式により、
  明細が0件でヘッダーの確定金額のみ表示される（12章の明細請求書取消と同じ挙動。請求書側も
  同様に扱う）。印刷自体は禁止しない（過去の参照用）。ただし明細請求書は取消済み
  （`InvoiceStatus = Cancelled`）を印刷ボタンで選べないようにしている（F8の`CanCancel`と対称）。
- **代表者印字**: 得意先マスタ`print_representative_flag`が真のとき、発行者情報ボックスに
  「代表者　○○○○」＋押印用の空欄枠を追加する（自社情報マスタは代表者名のみ保持し印影画像は
  持たないため）。宛名（明細請求書の`addressee_name`）を書き換えても、この値は得意先マスタの
  設定に従う（`docs/product-spec.md`共通業務ルール3）。
- **振込先（2026-09-29改訂）**: 全社共通で`bank_account.is_print_on_invoice`が真の口座を
  `display_order`順にフッターへ表示する方式は廃止した。**発行元の得意先（請求書は請求集約先。
  `billing.customer_code`）の得意先マスタ`bank_account_code1`／`bank_account_code2`に紐づく
  口座を、発行者情報ボックスの直下・独立した枠**（同じ220px幅の右カラム内）に印字する
  （`BuildBillingBankAccountsBox`）。「お振込先」見出し＋1口座あたり2行（1行目＝銀行名＋支店、
  2行目＝種別＋口座番号＋口座名義）。**紐づけが0件・1件でも常に「見出し1行＋4行」の固定行数で
  組み立て**、得意先によって帳票のレイアウトがずれないようにする。得意先ごとに使い分けたいという
  業務要件により、全社共通フラグ方式から得意先単位の紐づけ方式へ完全移行した
  （`bank_accounts.is_print_on_invoice`列は`scripts/021_add_customer_bank_accounts.sql`で削除）。
  納品書には印字しない（従来どおり）。

  **改ページへの影響**: 発行者情報ボックスの直下に枠を追加した分、
  `PagedReportDocumentBuilder.FullHeaderHeight`（`InvoiceDocumentBuilder.cs`／
  `DetailInvoiceDocumentBuilder.cs`）に`ReportDocumentBuilder.BillingBankAccountsBoxHeightEstimate`
  （100.0、安全側の見積り）を加算した。`PagedReportDocumentBuilder.Build`の計算式
  （`ContentHeight - FullHeaderHeight - TableHeaderHeight - FooterHeight`）より、
  **ヘッダー高さの過大見積りは1ページの明細行数が減るだけで安全だが、フッター高さの過小見積りは
  最終ページで本文とフッターが重なる**ため、高さを見積る際はヘッダー側に余裕を持たせる。
  旧`BuildBankAccountsBlock`をフッターから撤去した分（口座件数によって可変だった）は
  `FooterHeight`を変更せず据え置いた（安全側）。1ページ目に入る明細行数が請求書・明細請求書とも
  数行減る（見積りの詳細は実装時のコミットを参照）。
- **印刷履歴**: 記録しない（`billings`／`detail_invoices`にDDL変更なし）。納品書と異なり
  「（再発行）」表示も行わない。
- **印刷導線**:
  - 明細請求書発行画面（`DetailInvoiceIssueWindow`）の「印刷 (F11)」に配線した。発行済み
    （`InvoiceStatus = Issued`）を読み込んでいる場合のみ有効。発行直後にも「印刷しますか？」の
    確認ダイアログを挟む（納品書と同じパターン）。
  - 請求書は専用の発行画面を新設せず、請求締め処理画面（`BillingClosingWindow`）の結果一覧
    から選択行を印刷する導線にした（2026-09-16ユーザー確認）。**これに伴い、2026-09-11確定の
    「確定後は結果一覧を含めて画面を起動直後の状態へ即座にリセットする」仕様を変更した**
    （確定した瞬間に印刷対象の請求番号が一覧から消えてしまうため）。確定後も結果一覧は
    そのまま残り、選択行に対する「選択行を印刷 (F11)」で印刷できる。締め日区分・請求日を
    変更すれば次のバッチとして一覧が自動的に置き換わる（`docs/design_document.md` 9章に
    改訂履歴を記録）。

### 2-2-1. 親子請求（請求集約）の明細表示（2026-09-29実装。`docs/design_document.md` 28章 Phase D）

請求集約先（他の得意先の分もまとめて請求される得意先）の請求書には、明細のどこからどこまでが
どの請求集約元（支店等）の分かを明記する（用語・業務ルールは`docs/database-schema.md` 1-1節・
`docs/design_document.md` 28章参照）。

- **列は増やさない。** `InvoiceDocumentBuilder`の列定義（伝票No./商品コード/商品名/数量/単価/
  金額/税率/摘要）に「得意先」列は追加していない。代わりに、`InvoiceService.GetByNumberAsync`が
  得意先コード順→伝票日付順に整列した明細を`InvoiceReportRowBuilder`（Domain純粋関数、単体テスト
  済み）へ渡し、得意先ごとの境界に「見出し行」（商品名列に`【CUS004 株式会社山田商事　大阪支店】`）
  と「小計行」（商品名列に`株式会社山田商事　大阪支店 小計`、金額列に小計金額）を挟んだ表示順を
  組み立てる。**明細に含まれる得意先コードが1種類だけ（＝集約していない請求書）の場合は、
  `InvoiceReportRowBuilder`が見出し行・小計行を挟まず明細をそのまま返すため、既存の単独得意先の
  帳票はバイト単位で不変**（回帰防止のための最重要制約。単独得意先ではCustomerCodeが全行同じ値の
  ため、`InvoiceService`側の並び順変更自体も無害）。
- **税率別内訳（フッター）は無改修で正しい。** `ConsumptionTaxCalculator.ResolveConfirmedBuckets`は
  ヘッダーの確定金額（請求集約先の`billings`1件分。請求集約元の分もすでに合算済み）を使い、
  税率(%)ラベルだけを明細行から拝借する方式のため、明細が複数得意先混在になっても二重丸めは
  発生しない。
- **見出し行の改ページ対応**: 見出し行がページ末尾に孤立し、次ページ先頭の明細と離れる事故を
  防ぐため、`ReportPagination.AvoidTrailingHeaderOrphans`（Domain純粋関数、単体テスト済み）を
  新設した。既存の`Split`は無改修（シグネチャ変更なし）で、`PagedReportDocumentBuilder.Build()`が
  `Split`の結果に対して後段でこの調整を適用する。見出し行の直後には必ず1件以上の明細行と小計行が
  続く構造（`InvoiceReportRowBuilder`）のため、この調整は1回の走査で十分（連鎖的な調整は発生しない）。
- **続紙ヘッダー・フッター**: 集約時は`FullHeaderHeight`（`protected override double`）を
  +18.0して「請求集約元: N社」の1行の高さを確保する（`FullHeaderHeight`が変わると
  `PagedReportDocumentBuilder`が1ページ目の収容行数を再計算するため、可変にし忘れると最終ページで
  フッターが本文と重なる）。フッターの請求集約元別内訳ブロックは**任意扱いのまま今回は実装しない**
  （本文の見出し行・小計行だけで完了条件「請求集約元ごとの内訳が明記される」を満たすため。
  `FooterHeight`は無改修）。「請求集約元: N社」のNは配下の請求集約元（自分自身＝請求集約先を除く）
  の件数。請求集約先自身の売上が今回の請求期間に無いケース（配下の分だけで請求データが作られた
  場合）もあり得るため、見出し行の総数からではなく「請求集約先自身のコードが明細に含まれているか」
  で判定する（`InvoiceDocumentBuilder.AggregatedChildCount`）。

### 2-3. 得意先元帳（Phase 10-6）

未着手。着手時に本節へ追記する。得意先元帳は「前頁繰越／次頁繰越」を毎ページのフッターに
出す必要があり、納品書・請求書・明細請求書の「最終ページのみフッター」という形は使えない点に
注意（`ReportDocumentBuilder`を拡張する際に検討する）。
