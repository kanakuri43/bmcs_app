# bmcs_app 設計資料（画面一覧・不明点）

> `CLAUDE.md` に置くべき「常に守るべき薄いルール」に対して、こちらは調査・分析系の内容（画面一覧、未解決の不明点）をまとめた設計参考資料。更新頻度は低いが、都度読み込む必要はないため `docs/` に分離している。データベースに関する情報は `docs/database-schema.md` に記載する（本資料には書かない）。
>
> 画面番号（`SCR-xxx`等）は正式な画面IDとして確定していないため、本資料では記載せず、画面は名称で参照する。

## 目次

- [1. やりたいことの要点（画面ごと）](#1-やりたいことの要点画面ごと)
- [2. 不明点・要確認事項（実装前に確定が必要）](#2-不明点要確認事項実装前に確定が必要)
- [3. 共通検索モーダルの実装確定事項](#3-共通検索モーダルの実装確定事項)
- [4. 受注入力の明細行グリッド](#4-受注入力の明細行グリッド)
- [5. 受注入力画面](#5-受注入力画面)
- [6. 売上入力画面](#6-売上入力画面)
- [7. 受注の状態遷移](#7-受注の状態遷移)
- [8. 売上入力画面の拡張](#8-売上入力画面の拡張)
- [9. 請求締め処理](#9-請求締め処理)
- [10. 締め解除処理](#10-締め解除処理)
- [11. 明細請求書発行](#11-明細請求書発行)
- [12. 明細請求書の取消](#12-明細請求書の取消)
- [13. 請求フェーズの構造上の制約](#13-請求フェーズの構造上の制約)
- [14. メインメニュー画面](#14-メインメニュー画面)
- [15. ジャーナル系画面の伝票No入力欄の挙動](#15-ジャーナル系画面の伝票no入力欄の挙動)
- [16. 入金消込サービスの実装](#16-入金消込サービスの実装)
- [17. 入金入力画面の実装](#17-入金入力画面の実装)
- [18. 明細入金画面の実装](#18-明細入金画面の実装)
- [19. 入金の取消・訂正の実装](#19-入金の取消訂正の実装)
- [20. 消込キャッシュの整合性](#20-消込キャッシュの整合性)
- [21. 得意先元帳・現在残高](#21-得意先元帳現在残高)
- [22. 帳票基盤・納品書の実装](#22-帳票基盤納品書の実装)
- [23. 請求書・明細請求書の実装](#23-請求書明細請求書の実装)
- [24. 受注の訂正](#24-受注の訂正)
- [25. ジャーナル系の日付制限](#25-ジャーナル系の日付制限)
- [26. 受注入力の過去伝票複写](#26-受注入力の過去伝票複写)
- [27. 入金方法マスタ](#27-入金方法マスタ)
- [28. 親子請求（請求集約）の設計](#28-親子請求請求集約の設計)
- [29. 月次締め処理](#29-月次締め処理)
- [30. コピー機売上CSV取込](#30-コピー機売上csv取込)

---

## 1. やりたいことの要点（画面ごと）

| 画面 | 目的 |
|---|---|
| メインメニュー | 起動時パラメータで渡された社員コードから権限を判定し、その権限に応じてメニューを出し分け、各機能へ遷移 |
| 受注入力 | 注文を仮伝票として先行登録。入荷・在庫引当ができるまでは売上（売掛金）を発生させない（引当数量の扱いはproduct-spec.md共通業務ルール6を参照）。過去伝票の複写入力にも対応（26章） |
| 売上入力 | 都度売上・受注からの売上確定・返品/値引を扱い、納品書を発行。過去伝票の複写入力にも対応 |
| 入金入力（締め） | 締め得意先の入金を登録し、古い請求から自動で消込。手数料差額の入力は別画面とし（手入力のみ。自動計算・自動補正提案は行わない）、その画面は保留中で未実装 |
| 明細入金 | 都度得意先向け。売上伝票 or 明細請求書を指定してピンポイント消込 |
| 請求締め処理 | 締め対象得意先の期間内売上・入金を集計して請求データを確定し、請求書を一括発行 |
| 締め解除処理 | 管理者権限のみ。確定済み請求データを解除済にし、売上の請求状態を未請求へ戻す。**請求締め処理とは別画面（別メニュー項目）とする**（画面内アクションではなく画面分離で権限差を表現する） |
| 明細請求書発行 | 都度得意先の「未発行かつ未入金」の売上をまとめて選び、宛名を都度書き換えて1枚の請求書を発行 |
| 得意先元帳 | 得意先ごとの売上・入金明細と残高推移を時系列表示。現時点のリアルタイム残高も常時表示。伝票プレビューは`readOnly`モードの売上/入金画面を再利用（プレビュー専用の別画面は用意しない。21-7章参照） |
| 月次締め処理 | 全得意先の月次売掛残高、担当者別売上・粗利を集計・確定（確定後はロックされ編集不可） |
| 月次締め解除処理 | 管理者権限のみ。確定済み月次締めを解除済にする。**月次締め処理とは別画面（別メニュー項目）とする**（締め解除処理と同じ理由） |
| データ横断検索 | 受注/売上/入金を横断的に検索し、納品書未発行の売上をまとめて一括発行 |
| 共通検索モーダル | 得意先・商品の検索モーダル。商品検索は「マスタから」「過去の取引履歴から」の2軸、最大6件を伝票へ一括転記 |
| マスタ管理 | 得意先・商品・銀行・**入金方法（27章）**・コピー機（30章）・社員・**自社情報（適格請求書発行事業者の登録番号等）**の各種マスタ管理、プリンタ設定（保存先はproduct-spec.md参照）、メニューマスタ管理（14章） |

---

## 2. 不明点・要確認事項（実装前に確定が必要）

**未解決事項の入口はこの章**。種類ごとの所在は次のとおり。

| 種類 | 所在 |
|---|---|
| DB設計以外の不明点・要確認事項 | この章（下記） |
| DB設計に影響する暫定運用中の項目（採番規則・単価決定・原価の取得元・残高・支払条件／与信限度額） | `docs/database-schema.md` 4章 |
| 採用理由の記録が無い設計判断 | `docs/decisions.md` 末尾の「要確認（理由の記録が無い項目）」 |

以下はDB設計以外の不明点。

- **在庫・発注との連携インターフェース**: スコープ外だが「連携する部分は考慮」という方針のみ確定。実際にどのような形で連携するか（DB直接連携／API／ファイル連携等）、受注入力画面の「発注データへ回す」ボタンが具体的に何をすべきかは未定義。
- **受注入力画面の店頭在庫数表示**: 在庫管理がスコープ外である一方、受注入力画面は「現時点の店頭在庫」の表示を前提としている。この情報をどこから取得するか（在庫システム連携が前提か、当面は非表示/固定値でよいか）は未確定。
- **インボイスの端数処理と税区分の整合（要: 顧問税理士確認）**: 適格請求書の「税率ごとに区分した消費税額」は1枚の請求書につき税率ごとに1回の端数処理を行うのが原則だが、得意先マスタの税区分3種のうちこれを自然に満たすのは「請求単位」のみ。「伝票単位」「内税明細単位」は伝票単位・明細単位で端数処理するため原則と衝突する。確認すべき点は次の3つ。
  1. どの書類を適格請求書として扱うか（月次請求書か、納品書・明細請求書か。複数書類で記載事項を満たす形とするか）。
  2. 「伝票単位」の得意先の月次請求書で、伝票ごとに確定した税額の合計を印字してよいか、請求書上で税率ごとに再計算するか。
  3. 「内税明細単位」の得意先（＝都度得意先）の明細請求書で、明細ごとに算出した税額の合計を印字してよいか。
  現行の他システム・既存帳票でどう運用しているかも確認材料とする。業務ルール側の記述は [`docs/product-spec.md`](product-spec.md) の共通業務ルール9を参照。
  確認が取れるまでの暫定方針は、得意先マスタの税区分どおりに端数処理を行うこと（`ConsumptionTaxCalculator`。実装済み。9-4章・23章参照）。

- **得意先マスタの支払条件・与信限度額の要否**: 締め得意先には支払条件（例: 20日締め翌月末払い）が必要になることが多いが、設計資料に記載がない。推測を避けるため、現状は項目を作っていない。必要であれば `ALTER TABLE` で追加できる。与信限度額の管理を行うかも併せて確認したい。

これらは実装着手前にユーザーへの確認が必要。

---

## 3. 共通検索モーダルの実装確定事項

- **商品検索モーダル「過去の取引履歴から」の対象データ**: 売上のみ（`sales`）。`orders`（受注）は対象外。
- **履歴から転記する単価**: 履歴行の単価（過去の実売価格）。「マスタから」軸は商品マスタの単価。
- **履歴軸の得意先スコープ**: 呼び出し元画面で選択済みの得意先のみ。得意先が未選択の場合は履歴タブを無効化し、「先に得意先を選択してください」と案内する。
- **履歴軸はカナ検索非対応**: 売上テーブルに商品カナ列がないため。「マスタから」軸のみコード・名称・カナ・規格で検索できる。
- **既知の制約（性能）**: 得意先・商品検索とも、マスタ全件をロードしたうえでのメモリ内絞り込み（既存の得意先・商品マスタ画面と同じ全件ロード方針に揃えたもの）。マスタが数万件規模になった場合は性能問題が出てから、両方まとめてサーバ側絞り込みに変更する。

---

## 4. 受注入力の明細行グリッド

- **単価の初期値転記**: 商品を選ぶと、得意先の税区分（`tax_unit`）に応じて商品マスタの外税単価／内税単価のどちらかが単価欄に入る。以後は手入力で上書きできる（`IUnitPriceCalculator`／`StandardUnitPriceCalculator`、`src/bmcs_app.Domain/Calculations/`。単価決定ロジックのみ、差し替え可能なインターフェースにしている）。商品検索モーダルのマスタ軸も同じ判定で単価列を表示し、単価列ヘッダ（「単価(税抜)」／「単価(税込)」）で転記元を明示する。
- **明細行 ViewModel は受注・売上で共用する**: `src/bmcs_app/ViewModels/Common/SlipLineViewModel.cs` を受注入力・売上入力の両方から使う。専用画面ごとにコピーは作らない。
- **レイアウト・キー操作**: 受注入力と売上入力で共通の明細行パターンを使う。
  - 列幅（左から）: 行番号36 / 商品コード110 / 商品名可変 / 数量72 / 単価88 / 原価80 / 金額96 / 税率56 / 行摘要130 / 削除28。
  - 商品コード欄で `Space` → 商品検索モーダルを開く、`Enter` → 入力済みコードで直接引き当てる。
  - 原価・金額・税率は表示のみ（編集不可）。金額は `数量×単価` を得意先の端数区分で1円に丸めた値（`ConsumptionTaxCalculator.CalculateLineAmount`）。
  - 商品検索モーダルの一括転記（最大6件）は、1件目を呼び出した行へ、残りは後続の空行を埋める／なければ直後に挿入する。転記後は常に末尾に空行を1行維持し、確定後は数量欄へフォーカスを移す。

---

## 5. 受注入力画面

ツールバー・ヘッダー・明細・フッター集計・StatusBarで構成する。この構成の項目のうち、`orders` エンティティに列がない、または機能自体が未実装のものは、**枠（コントロール）だけ用意し `IsEnabled="False"` で使用不可にしている**（非表示にはしない）。

| 項目 | 状態 | 理由 |
|---|---|---|
| 受注日付・得意先（コード+名称、Space/Enter対応）・明細行・フッター集計・保存(F10)・新規(F3) | **実装済み** | `orders` に対応する列があり、既存の `ConsumptionTaxCalculator`／`IUnitPriceCalculator`／`SlipNumberService` を再利用できる |
| 受注No. | 新規入力時は採番前のため空欄（ウォーターマーク「自動採番（SPACEで検索）」）で、**採番は保存時にトランザクション内で1回だけ**行う。既存受注は、受注No.を入力して`Enter`で読み込むか、`Space`で伝票検索モーダル（8-0章）から選ぶ（読み込み・修正・複写は24章・26章） | 採番は保存時にトランザクション内で1回だけ行う方針（`docs/architecture.md` 6章）であり、画面を開いた時点や入力中に採番しない |
| 得意先名の編集 | **実装済み**。得意先コード検索後、名称を直接書き換えられる（宛名の都度書き換え方式。`sub_customer_id`行を参照） | 学校のクラス・先生等の宛名の柔軟性を、子得意先マスタではなく名称欄の都度上書きで実現する |
| 得意先名の編集（諸口得意先） | **未実装** | `Customer` に「諸口」相当のフラグがない |
| 担当者（コード＋名称） | **実装済み** | その受注自体の担当者を `orders.employee_code` に保存する（任意項目。伝票単位の値として全行に複写）。得意先を選ぶと得意先マスタの営業担当（`Customer.SalesEmployeeCode`）が初期値として入り、修正できる。コード欄は `Space` で社員マスタの検索モーダル、`Return` で社員コードの直接照会（無効化された社員は入力できない）。訂正でも変更できる。過去伝票の複写では複写元の担当者を引き継ぐ。なお画面右上の「営業担当」は得意先マスタの営業担当の表示で、伝票の担当者とは別概念 |
| 摘要（伝票摘要） | **実装済み** | ジャーナル系テーブル（`sales`／`receipts`／`detail_receipts`／`orders`）に `slip_remarks` 列があり（`scripts/011_add_slip_and_line_remarks.sql`）、同一伝票の全明細行に複写して保存する（`docs/database-schema.md` 1章） |
| 行摘要（明細行摘要） | **実装済み** | `line_remarks` 列に保存する。`src/bmcs_app/Views/Common/SlipLineControl.xaml` の行摘要 TextBox は売上入力とも共用する |
| 受注状態バッジ | **実装済み**。新規入力中は「未売上」、既存受注を読み込んだ場合は読込内容の受注状態（未売上／一部売上／売上完了／中止）を表示する | 状態遷移ロジックは`OrderStatusService`（7章）、既存受注の読み込みは8-1章・24章 |
| 前の受注／次の受注（ナビゲーション） | **実装済み** | 受注No順（論理削除除外）に移動する。画面を開いた直後・新規状態は「最新伝票の次」の位置で、「次」は使用不可・「前」で最新伝票を表示する。先頭伝票の表示中は「前」が使用不可。最新伝票で「次」を押すと新規状態に戻る |
| 中止（F8） | **実装済み**。読み込んだ既存受注を`OrderStatusService.CancelSlipAsync`で伝票単位に中止する（7章）。物理削除はしない | 伝票は物理削除しない方針（取消は`is_deleted`または状態で表す）のため、F8は物理削除ではなく中止として動作する。既存受注を読み込んでいて中止済みでない場合のみ有効 |
| `sub_customer_id`（宛名） | **入力欄なし（確定）** | 画面項目として扱わない。学校のクラス・先生等の宛名柔軟性は`sub_customer_id`ではなく、得意先名称欄（`CustomerName`）の都度書き換え方式で実現する。`sub_customer_id`列自体は将来の別要件に備えて残すが、入力欄は作らず参照・更新する処理もない |

実装ファイル: `src/bmcs_app.Application/Order/OrderService.cs`（新規登録ユースケース。採番と登録を同一トランザクションで行う）、`src/bmcs_app/ViewModels/Order/OrderEntryViewModel.cs`、`src/bmcs_app/Views/Order/OrderEntryWindow.xaml`。

過去伝票の複写入力は26章を参照。

`src/bmcs_app/Behaviors/EnterKeyNavigationBehavior.cs` は、Enterキーで次項目へフォーカス移動する処理を `PreviewKeyDown`（トンネリング）でコンテナに実装しているため、`TextBox.InputBindings` に一致する `Enter` の `KeyBinding` がある場合はフォーカス移動を行わない（子の `KeyBinding`〔商品コード欄の「Enterでコード確定」等〕が先にイベントを消費されて機能しなくなるのを避けるため）。同ビヘイビアを使う全画面に共通の挙動。

---

## 6. 売上入力画面

受注入力画面（5章）と同じ構成で実装している。スキーマ上存在しない項目は枠のみ用意し無効化している。都度売上の直接入力に加え、受注からの売上確定・返品値引・過去伝票の複写・訂正・取消を扱う（8章）。

| 項目 | 状態 | 理由 |
|---|---|---|
| 売上日付・得意先（コード+名称、Space/Enter対応）・明細行・フッター集計・保存(F10)・新規(F3) | **実装済み** | `sales` に対応する列があり、既存の `ConsumptionTaxCalculator`／`IUnitPriceCalculator`／`SlipNumberService`／`SlipLineViewModel` を再利用できる |
| 得意先名称の上書き | **実装済み**（TwoWay、保存は`this.CustomerName`） | 宛名の都度書き換え方式。受注入力画面と同じ仕組み |
| 受注No. | **実装済み**。`Space`で受注検索モーダル、`Return`で受注No.直接読込（8-1章） | `sales.order_slip_number`／`order_line_number`列で受注行に紐付ける |
| 請求状態・消込状態の表示 | **実装済み**。新規入力中は固定表示「未請求」「未消込」、既存伝票を読み込んだ場合は実際の状態を表示 | 新規登録では`BillingStatus=Unbilled`／`SettlementStatus=Unsettled`が常に正しい。請求状態・消込状態の枠は、伝票の状態カラム（`BillingStatus`／`SettlementStatus`）に対応させている |
| 編集ロック（保存/取消の無効化） | **実装済み** | ロック判定は請求締め・月次締め・入金済み等に基づき、既存伝票の読み込み時に行う（8-4章）。新規伝票は定義上ロック対象外 |
| 印刷（F11） | **実装済み**。既存伝票を読み込んでいる場合のみ有効（納品書）。新規保存の直後は発行確認ダイアログから印刷する | 帳票エンジンはWPF FixedDocument方式（`docs/report-spec.md`） |
| 前の売上／次の売上（ナビゲーション） | **実装済み** | 売上No順に移動する。挙動は受注入力画面と同一 |
| 取消（F8） | **実装済み**。読み込んだ既存売上を`SalesService.CancelSlipAsync`で取消す。編集ロック中は無効 | 伝票は物理削除しない方針のため、物理削除は行わない。取消は状態遷移として実装している（8-4章） |
| 担当者 | **実装済み** | その売上自体の担当者を `sales.employee_code` に保存する（任意項目。受注入力画面と同じ入力方法・初期値）。受注を参照して売上化するときは受注の担当者を引き継ぐ。1つの売上に複数の受注を読み込んだ場合は、最初に読み込んだ受注の担当者を採用し、2件目以降で担当者が異なれば値を変えずにステータスバーへ警告を出す。受注側が未設定なら得意先の営業担当（初期値）のままにする。納品書には印字しない |
| 摘要・行摘要 | **実装済み** | `sales.slip_remarks`／`line_remarks`に保存する |

### 保存時の税額確定ロジック

**税額カラムの分岐処理は`SalesTaxAmountAssigner`（`src/bmcs_app.Domain/Calculations/SalesTaxAmountAssigner.cs`）に一本化し、`SalesService.CreateAsync`から呼ぶ。** ViewModelには置かない。得意先の税区分（`tax_unit`）に応じて次のように確定する（DBのCHECK制約`CK_sales_tax_amount_by_tax_unit`と対応）。

- 請求単位（1）: `slip_tax_amount`／`tax_amount`ともにNULL（請求締め時に一括計算するため）
- 伝票単位（2）: 伝票全体で1回だけ計算した`slip_tax_amount`を全行に複写（`tax_amount`はNULL）
- 内税明細単位（3）: 明細行ごとに`tax_amount`を確定（`slip_tax_amount`はNULL）

同じ分岐は受注からの売上確定・返品値引・複写入力・訂正（8章）でも必要になるため、`sales`への書き込み口である`SalesService`を必ず経由させることで、どの経路でも正しい税額が確定するようにしている。`BillingNumber`は`SalesTaxAmountAssigner`では扱わず、新規登録専用の`SalesService.CreateAsync`が無条件にNULLを設定する（既存の請求紐付けを消してしまう経路が訂正にはないようにするため）。

実装ファイル: `src/bmcs_app.Application/Sales/SalesService.cs`、`src/bmcs_app.Domain/Calculations/SalesTaxAmountAssigner.cs`、`src/bmcs_app/ViewModels/Sales/SalesEntryViewModel.cs`、`src/bmcs_app/Views/Sales/SalesEntryWindow.xaml`。メインメニューへの導線はメニュー構成マスタ駆動（`screen_key="sales_entry"`）。

---

## 7. 受注の状態遷移

`orders.order_status`（未売上／一部売上／売上完了／中止）と`sales_confirmed_quantity`を更新する処理は、サービス層（`OrderStatusService`）が担う。売上登録・取消からの呼び出しは8-1章、受注入力画面の既存受注の読み込みと中止(F8)は5章・8章・24章を参照。

### 決定事項

1. **中止（`OrderStatus.Cancelled`）の解除は実装しない。** `docs/product-spec.md`の遷移定義どおり、中止は終端状態とする（誤って中止した場合は新規に受注を入力し直す）。
2. **中止の操作単位は伝票単位のみ。** 明細行単位の中止APIは作らない（業務上の失注・キャンセルは伝票丸ごとが通常のため）。

### 実装構成

- **`src/bmcs_app.Domain/Calculations/OrderStatusCalculator.cs`**: `Determine(orderQuantity, salesConfirmedQuantity)` で状態を判定する純粋関数。副作用なし。`Cancelled`はここでは導出しない（決定1）。
- **`src/bmcs_app.Application/Order/OrderStatusService.cs`**: 新規登録専用の`OrderService`とは責務を分ける。
  - `ApplySalesQuantityDeltasAsync`: 正のデルタ＝売上化、負のデルタ＝売上取消（逆遷移）を1メソッドで扱う。**トランザクションを開始せず`SaveChangesAsync`も呼ばない**（呼び出し元の売上登録・取消ユースケースが、伝票登録と同一の`SaveChangesAsync`1回に含めて保存する。`docs/architecture.md`6章）。呼び出し時に明示トランザクションが開始されていなければ`InvalidOperationException`（`SlipNumberSequenceCommand`と同じ理由）。受注数量超過・マイナス残・中止済み行への売上化・存在しない行・デルタの重複指定は`OrderOperationException`。
  - `CancelSlipAsync`: 伝票単位の中止。単独のユースケースとして`SaveChangesAsync`を1回呼ぶ。売上完了済みの明細行を含む受注・既に中止済みの受注は`OrderOperationException`。`SalesConfirmedQuantity`は変更しない（分納済みの実績を残す）。

`ApplySalesQuantityDeltasAsync`は`SalesService`が売上登録・取消と同一トランザクション・同一`SaveChangesAsync`から呼ぶ（8-1章）。

---

## 8. 売上入力画面の拡張

売上入力画面は「受注からの売上確定」「返品・値引」「過去伝票の複写」「既存伝票の訂正・取消」を扱う。
受注入力画面（4-3）も「既存受注の読み込み」と「中止（F8）」を扱う（7章）。

### 8-0. 共通の前提: 伝票検索モーダル

受注No.／売上No.の検索には、`CustomerSearchDialog`と同じ作りの共通モーダルを1つ使う
（`src/bmcs_app/Views/Common/SlipSearchDialog.xaml`／`ViewModels/Common/SlipSearchDialogViewModel.cs`）。
受注用・売上用の画面を別々に作らず、`SlipSearchTarget`（`Order`／`Sales`）で対象を切り替える。
全件ロード後にメモリで絞り込む方式（`CustomerSearchDialogViewModel.ApplyFilter`と同じ）。
選択結果は**伝票番号の文字列のみ**を返し、実体の読み込みは呼び出し元が自分のクエリサービス
（`SalesQueryService`／`OrderQueryService`。いずれも伝票単位にサマリ化した検索結果を返す）で行う。

データ横断検索は本モーダルとは別に、横断検索専用の画面として作る。

### 8-1. 受注からの売上確定

- `SalesService.CreateAsync`は、明細行の`OrderSlipNumber`／`OrderLineNumber`から
  受注デルタをサービス側で導出して`OrderStatusService.ApplySalesQuantityDeltasAsync`を呼ぶ
  （同一トランザクション・同一`SaveChangesAsync`。7章）。
  デルタは`(受注伝票番号, 受注行番号)`ごとに**合算してから**渡す（同じ受注行を複数の売上行が
  参照するケースで、`ApplySalesQuantityDeltasAsync`の重複キー拒否に引っかからないため）。
- 受注の消化に算入するのは`SlipType.Sales`の行のみ。**返品・値引行を受注に紐付けることは
  禁止**とし（`SalesService`が`SalesOperationException`で拒否）、受注由来の売上を返品したい
  場合は元の売上行を直接訂正する（8-4）運用とする。
- 売上入力画面の受注No.欄は、`Space`で伝票検索モーダル（`Target=Order`）、`Return`で
  直接読込。読込時は受注の**残数量**（`受注数量 - 売上化済数量`）を明細行へ転記し、税率は
  **売上日付**で再解決する（単価・原価は受注のスナップショットを引き継ぐ）。中止・売上完了済みの
  受注は検索結果から除外する（`OrderQueryService.SearchAsync`の既定挙動）。
- **受注入力画面の配線**: 受注No.欄から既存受注を読み込める（訂正は24章）。
  削除(F8)は`OrderStatusService.CancelSlipAsync`に配線され、受注状態バッジも読込内容に追従する。

#### 中止済み受注への負のデルタ

`OrderStatusService.ApplySalesQuantityDeltasAsync`は、`OrderStatus.Cancelled`の明細行への
**正のデルタ（売上化）は拒否するが、負のデルタ（売上取消）は許可する**。
「一部売上化 → 受注を中止 → その売上を後から訂正・取消する」という順序が起こり得るため、
負のデルタまで一律拒否すると売上側の取消が永久にできなくなるからである。
状態は`Cancelled`のまま維持し、`未売上`／`一部売上`へは戻さない
（中止は終端状態。`docs/product-spec.md`）。

### 8-2. 返品・値引

マイナス数量・マイナス金額＋伝票区分カラム（`slip_type`）で表す。

- **`sales.slip_type`は明細行ごとに選択する**。同一伝票内に売上行と値引行を混在できる。
- **値引行も商品コードは必須**とする（値引専用の擬似商品コードは作らない）。税率・
  税種別区分がその商品から決まるのはインボイス制度上も正しいため。
- `src/bmcs_app.Domain/Calculations/SalesSlipTypeRules.cs`に正規化ロジックを1箇所へ集約する。
  - `NormalizeQuantity`: ユーザーは常に正の数量を入力し、符号は区分（売上／返品／値引）から
    機械的に決まる。
  - `NormalizeCostPrice`: **値引の原価は常に0**（現品の移動を伴わないため）。返品は商品原価を
    そのまま使う（返品時も原価をマイナス計上することで、元の売上の粗利影響をちょうど打ち消す）。
    `SlipLineViewModel.CostPrice`自体は破壊的に書き換えない（値引→売上と往復させても原価を
    失わないようにするため）。正規化は粗利計算・保存時の2箇所の境界でのみ適用する。
- `SalesService.CreateAsync`／`UpdateAsync`は保存前に次の2つを検証する。
  - `Math.Sign(Quantity) == Math.Sign(Amount)`（数量と金額の符号が一致しない行は
    `SalesOperationException`。正規化はViewModel側の責務であり、Application層は
    整合性を検証するだけで黙って書き換えない）。
  - 返品・値引行に受注紐付けがないこと（8-1参照）。
- `ConsumptionTaxCalculator`はマイナス金額を網羅している
  （`TaxRounding.RoundToYen`が絶対値で丸めて符号を戻す）。
- `ProductHistoryQueryService.SearchAsync`は`!IsDeleted && SlipType == SlipType.Sales`の
  行だけを対象とする。返品・値引行や論理削除された行（8-4）が、商品検索モーダルの
  「過去の取引履歴から」タブに単価候補として現れないようにするため。
- 明細行UI（`SlipLineControl.xaml`）は区分列（幅68、`EnumDisplayConverter`で表示）を持つ。
  `orders`には伝票区分の概念がないため、受注入力画面では列を非表示にする
  （`SlipLineViewModel.IsSlipTypeVisible`をホストが画面単位で設定）。

### 8-3. 過去伝票の複写入力

- `SalesEntryViewModel.CopyFromPastSlipCommand`（ツールバー）→ 伝票検索モーダル（`Target=Sales`）
  →`SalesQueryService.GetSlipAsync`→ 新規登録として明細行へ展開。
- **複写するもの**: 得意先、明細行（商品・数量・単価・原価・区分・行摘要）、伝票摘要。
- **複写しないもの**: 伝票番号（新規採番）、伝票日付（当日）、受注リンク、請求状態・消込状態・
  消込済金額・請求番号・納品書発行状態（すべて新規登録の初期値）。
- 税率は新しい売上日付で再解決する（単価は複写元の値を維持し、同じ取引条件の再現を優先する）。

### 8-4. 既存伝票の訂正・取消

元伝票の直接修正とする（赤伝方式は採用しない。`docs/product-spec.md` 共通業務ルール5）。

- `SalesService.UpdateAsync`: 行の追加・更新・削除（論理削除）を1回でまとめて扱う。
  1. 明細行の集合を再取得し、読込時点の行番号集合と比較（`SlipConcurrencyGuard.EnsureLineSetUnchanged`）。
     不一致なら他ユーザーの行追加・削除とみなし`SlipConcurrencyException`。
  2. 訂正前の状態で編集ロック（下記4条件）を判定。該当すれば`SalesOperationException`。
  3. 受注デルタを（訂正前数量→訂正後数量の差分として）収集する。
  4. 既存行は**ホワイトリスト方式**で上書き可能な列だけコピーする（伝票日付・得意先名・区分・
     商品・数量・単価・金額・原価・税種別・税率・受注リンク・摘要）。**得意先コード・税区分・
     請求/消込関連の状態カラム・監査列は対象外**（`BillingNumber`は`CreateAsync`だけが
     無条件にNULLを設定する方針を維持し、`UpdateAsync`は一切触らない）。
  5. 読込時にあったが今回の一覧にない行は`IsDeleted=true`（物理削除しない）。
     新規追加行は現在の最大行番号+1を採番する（主キーが(伝票番号,行番号)のため、
     既存行の番号は詰め直さない）。
  6. **訂正後（新状態）でも編集ロックを再判定する**（伝票日付を確定済みの月次締め年月へ
     動かす訂正を防ぐため）。該当すれば保存前に`SalesOperationException`で中止する。
  7. `SlipConcurrencyGuard.TouchAll`で読込済み全行（削除された行を含む）を更新対象に含め、
     値を変えていない行もrowversion照合を受けさせる（docs/architecture.md 9章）。
  8. `SalesTaxAmountAssigner.Assign`には**`IsDeleted=false`の行だけ**を渡す（取消済み行を
     含めると伝票単位の合計税額・内税明細単位の行別税額が狂うため）。全行削除の場合は
     `Assign`を呼ばない（`CancelSlipAsync`と同じ状態になるため）。
- `SalesService.CancelSlipAsync`: 伝票取消。全明細行を`IsDeleted=true`にし、受注デルタを
  負方向に戻す。ロック判定・排他制御は`UpdateAsync`と同じ。伝票の取消は`is_deleted`で表す
  （`docs/database-schema.md` 2.8節。取消も直接修正としつつ、物理削除しないという
  方針と整合させるため`is_deleted`方式とする）。
- **編集ロック判定**（`SalesEditLockEvaluator`。Domain純粋関数＋`SalesEditLockService`が
  `monthly_closings`／`detail_invoice_sales_lines`を照会）は`docs/product-spec.md` 共通業務ルール5の
  4条件のいずれかに該当する場合にロックとする。①`billing_number`が確定済み請求を指す（請求締め）
  ②明細請求書発行済み（`detail_invoice_sales_lines`に連携している。13章参照）
  ③対象年月の`monthly_closings`が確定済み（月次締め）④`settlement_status`=消込完了（入金済み）。
  理由文言を返すのみで、ViewModelは業務判断をせずそのまま表示する（docs/architecture.md 5章）。
- **伝票単位の楽観的排他制御の共通処理**（`SlipConcurrencyGuard`。docs/architecture.md 9章）を
  `src/bmcs_app.Application/Common/`に置き、`EnsureLineSetUnchanged`／`TouchAll`の2メソッドを
  提供する。伝票種別ごとに書かない。
- 売上入力画面: 売上No.欄を入力可能とし（`Space`で検索モーダル、`Return`で直接読込）、
  請求状態・消込状態の表示は実際の値にバインドし、編集ロック中は保存・削除を無効化して
  理由をステータスバーに表示する。削除(F8)は`CancelSlipAsync`に配線される。
  **前／次ナビゲーションは伝票検索モーダルで代替できるため実装しない**（一覧を
  キャッシュする方式は採らない）。担当者は `sales.employee_code` に保存する
  （得意先の営業担当が初期値。修正可。受注からの売上化では受注の担当者を引き継ぐ）。

---

## 9. 請求締め処理

締め得意先（`tax_unit`＝請求単位／伝票単位、`closing_day ≠ 0`）の期間内売上・入金を集計し、
`billings`へ請求データを確定する。確定時に`sales.billing_number`を書き込むことで、編集ロック条件①
（請求締め済み）が発火する。

### 9-1. 締め日の決定

`ClosingDateResolver.Resolve(year, month, closingDay)`（`src/bmcs_app.Domain/Calculations/`）が
`(対象年月, 締め日区分)`から実際の締め日（`DateOnly`）を求める。`closingDay = 99`は当月末日、
`closingDay`がその月の日数を超える場合（31日締めの2月等）も当月末日に丸める。`closingDay = 0`
（都度得意先）は締め対象外のため呼び出し不可（`ArgumentOutOfRangeException`）。

### 9-2. 集計期間 ― 売上と入金で下限の扱いが非対称

| | 下限 | 上限 | 二重集計を防ぐ手段 |
|---|---|---|---|
| 売上（`sales`） | なし | 締め日 | `billing_number IS NULL`（集計済みマーカー） |
| 入金（`receipts`） | 前回確定`billings`の締め日 + 1日 | 締め日 | 期間で区切る |

この非対称はデータモデルから必然的に導かれる。`receipt_allocations.billing_number`は「充当先の請求データ」
であり、入金入力が**過去の**請求へ古い順に充当したときに設定される値のため、締め処理
の「集計済み」マーカーとして上書きすることができない。したがって入金は期間で区切るしかない。

売上に下限を設けないのは締め漏れを防ぐため。前回締め日より前の日付で後から登録された売上も、
未請求である限り次回の締めで必ず拾われる。

**残存リスク（既知・許容）**: 前回締め日より前の日付で**後から登録された入金**は、どの締めの
期間にも入らず永久に拾われない。対処は締め解除（10章）→再締め。

前回確定`billings`＝同一`customer_code`／`billing_status`＝確定／`is_deleted`＝偽のうち
`closing_year_month`が最大のもの。存在しなければ前回残高0・入金の下限なし。

集計対象は締め対象の得意先（請求集約先または単独得意先）1件ではなく、その請求集約グループ
（請求集約先＋全請求集約元）の売上・入金全体（28章）。前回確定`billings`の照会は、`billings`が
請求集約先にしか作られないため対象得意先自身のコードで行う。

### 9-3. 金額の組み立て

```
current_billing_amount = previous_balance - receipt_amount + sales_amount + tax_amount
```

- `receipt_amount`は期間内の`receipts`の`amount`（支払手段の内訳の行単位の値）の単純合計
  （`docs/database-schema.md` 2.8節）。
- `sales_amount`／`tax_amount`／税率別内訳5カラムは9-4の税額計算から得る。

### 9-4. 税額計算 ― 既存の`ConsumptionTaxCalculator`を税単位で使い分ける

新しい計算ロジックは追加せず、既存のメソッドをそのまま使う。

| `tax_unit` | 使うメソッド |
|---|---|
| 請求単位 | `CalculateExternalTaxBuckets`→`ToSummary`（請求全体で(税種別,税率)ごとに1回だけ丸める） |
| 伝票単位 | `CalculateExternalTaxPerSlip`（伝票ごとに確定した税額を積み上げる） |
| 内税明細単位 | 対象外（`closing_day = 0`のCHECK制約により自然に除外。サービス側でも明示的に弾く） |

伝票単位は、再計算した伝票税額の合計が保存済み`slip_tax_amount`の合計と一致することを
検証する（不一致は`BillingClosingException`）。端数区分（`rounding_type`）は登録後変更不可
なので本来一致するはずであり、不一致はデータ異常を意味する。

返品・値引行（`slip_type`＝2／3）はマイナス金額のままそのまま含める（`ConsumptionTaxCalculator`
はマイナス対応済み）。

### 9-5. 二重締め防止 ― アプリ側とDB側の二段構え

**アプリ側**: 確定前に同一`customer_code`×`closing_year_month`の確定済み`billings`が無いこと
を確認する。あればその得意先をスキップする（理由付きで結果に含める）。あわせて、より新しい
`closing_year_month`の確定済み`billings`が既にある場合も拒否する（締め順序の逆転防止）。

**DB側**: フィルタ付き一意インデックス`UQ_billings_customer_closing_ym_confirmed`
（`ON billing (customer_code, closing_year_month) WHERE billing_status = 1 AND is_deleted = 0`。
`scripts/013_add_billing_confirmed_unique_index.sql`）。解除済み（`billing_status = 2`）は対象外
なので、締め解除→再締めで新番号を採番する運用（10章）を壊さない。

### 9-6. 締め対象にしない得意先

締め確定の挙動（9-7の一覧が確定済み請求データのみを表示するのとは別の話）:

- 対象売上・対象入金が無く、かつ前回残高も0 → `billings`を作らない（空の請求書を出さない）。
- 対象が無くても前回残高≠0 → 繰越請求として`billings`を作る。

### 9-6-1. 実装

- `src/bmcs_app.Application/Billing/BillingClosingService.cs`が本体。得意先ごとの集計を
  1つのprivateメソッドに集約し、`PreviewAsync`（保存しない読み取り専用の事前確認）・
  `ConfirmAsync`（同条件で再集計してから確定）の両方から呼ぶ（「金額を出す経路を
  1本にする」方針）。`ConfirmAsync`はプレビュー結果を引数に取らない
  （プレビューと確定の間に他ユーザーが伝票を登録しても古い集計値で確定しないため）。
  `PreviewAsync`は請求締め処理画面からは呼ばれず、結合テストの検証でのみ使われる。
- トランザクション境界は`docs/architecture.md` 6章のとおり。伝票番号（`SlipNumberKind.Billing`）
  の採番が複数得意先分必要になるため明示トランザクションで包み、`SaveChangesAsync`は最後に
  1回だけ呼ぶ（6章が「請求締め」を明示トランザクションの例として挙げている）。
- 画面（`Views/Billing/BillingClosingWindow.xaml`／`ViewModels/Billing/BillingClosingViewModel.cs`）
  は「締め日を指定して一括」処理する専用画面。締め日区分（得意先マスタに実在する`closing_day`
  から選択）・請求日を指定し、「締め確定」で確定する。対象年月は別入力にしない（請求日から
  導出できる冗長な入力になるため）。

### 9-7. 一覧＝確定済み請求データの照会（請求書の再印刷）

**一覧は「これから締めたらどうなるか」の集計プレビューではなく、`billings`に実在する確定済み
請求データを請求日（`billing_date`）だけで抽出したもの。** 締め日区分は抽出条件に使わない
（`billings`に締め日区分を保持する列が無いため）。抽出条件（`InvoiceService.GetByBillingDateAsync`）:
`billing_date`一致・`billing_status = 1`（確定）・`is_deleted = 0`。解除済み(`billing_status = 2`)は
表示しない（同一得意先に解除済みの請求番号と再締めした請求番号が並び得るため、印刷対象の判別が
つかなくなることを避ける）。未確定の請求日を選んだときは1件も表示しない。

確定前の集計プレビューは持たない。画面を開き直すと`BillingClosingService.PreviewAsync`は
「既にこの締め年月で確定済みです」というスキップ行（請求番号なし）を返すため、過去に締めた
請求書を再照会・再印刷する手段を優先して、一覧は確定済み請求データの照会に限る。
締め確定（F10）は実行前に対象件数を提示する確認ダイアログを挟む（`BillingReleaseViewModel`
と同型。プレビューが無い分、何も見えない状態での即DB書き込みを避ける）。

一覧は拡張選択（`SelectionMode="Extended"`、Ctrl/Shiftクリック。チェックボックス列は持たない）
で複数行を選べる。選択した行はまとめて1つの`FixedDocument`に連結して
1回のプレビューダイアログで印刷でき、これが請求書の再発行手段になる
（`PagedReportDocumentBuilder.BuildInto`、`BillingClosingViewModel.PrintCommand`）。
F11／ボタン／行のダブルクリック・Enter（`RowActivationBehavior`）のいずれからも「選択中の
全行を印刷」で一貫させるため、`PrintCommand`はパラメータを取らない
（選択の同期は`MultiSelectionBehavior`が担う。`ListBox.SelectedItems`は依存関係
プロパティでないため直接バインドできないため）。

---

## 10. 締め解除処理

確定済み`billings`を解除済（`billing_status = 2`）にし、紐付く`sales`行の`billing_number`を
`NULL`、`billing_status`（`BillingLinkStatus`）を未請求へ戻す。管理者権限のみの操作のため、
請求締め処理（9章）とは別画面（別ウィンドウ）として提供する。

**解除の指定単位は請求番号1件ではなく、請求日（`billing_date`）。** 請求締め処理（9章）が
「締め日を指定して一括」確定するのと粒度を揃え、指定した請求日に確定済みの`billings`を
すべてまとめて解除する。`billing_date`は締める側が締め切り日を
そのまま書いた値（9章）であり、締め1回を一意に指すキーとして使える。

### 10-1. 解除できる対象の制約 ― 締め順序の逆転防止と対になる制約

**解除できるのは、同一得意先の確定済み`billings`のうち`closing_year_month`が最も新しいものに
限る。** それより古いものを解除すると、より新しい確定済み`billings`が引き継いだ`previous_balance`
の参照元が失われ、9-2の前回残高チェーンが破綻する。判定は9-5と対称で、
「同一`customer_code`／`billing_status`＝確定／`is_deleted`＝偽のうち`closing_year_month`が
最大のもの」を求め、それが解除対象自身でなければ拒否する（`BillingReleaseException`）。

解除後は、その1つ前の確定済み`billings`が再び「最新の確定済み」になるため、連鎖的に古い方から
順に解除していくことができる。

**指定した請求日の対象が複数件（複数得意先）ある場合、1件でもこの制約に違反すれば
解除処理全体を中止し、何も更新しない（All-or-nothing）。**
一部だけ解除してスキップするという扱いはしない。

その他の拒否条件:

- 指定した請求日に確定済みの`billings`が1件も存在しない。

### 10-2. 実装

- `src/bmcs_app.Application/Billing/BillingReleaseService.cs`が本体。`PreviewAsync`
  （画面表示用の読み取り専用取得。指定した請求日の確定済み`billings`一覧と、各件の
  `BlockReason`＝解除できない理由を返す）と`ReleaseByBillingDateAsync`（解除の確定）を持つ。
  `ReleaseByBillingDateAsync`は9章の`ConfirmAsync`と同じく明示トランザクションで包み、
  対象全件の`BlockReason`を先に判定してから（1件でも非nullなら`SaveChangesAsync`前に
  例外を投げてロールバックする）、`billings`本体と紐付く`sales`行をまとめて同一トランザクション
  内で更新する。締め順序の逆転判定は対象得意先分をまとめて1クエリで行う（得意先ごとに
  都度問い合わせるN+1を避ける）。
- **充当済みの入金（`receipt_allocations`）は解除時に付け替えない**（16章「既知の限界」）。
  解除で外れた売上行の消込キャッシュ列は`SettlementService.RecalculateForBillingGroupAsync`で
  未消込へ戻す。
- 画面（`Views/Billing/BillingReleaseWindow.xaml`／`ViewModels/Billing/BillingReleaseViewModel.cs`）
  は9章の`BillingClosingWindow`と同型で、請求日入力欄を持ち、条件変更時に自動でプレビュー
  （一覧表示）を再取得する。一覧（得意先・税区分・締め年月・前回残高・入金額・売上額・
  消費税・今回請求額・確定日時・備考）は読み取り専用で、対象の中に`BlockReason`を持つ行が
  1件でもあれば「解除実行」（F8）を無効化する。取消系の操作のため、実行前に得意先マスタの
  無効化と同様の確認ダイアログ（Yes/No）を挟む。解除成功後は画面を起動直後の状態へ戻す
  （`docs/product-spec.md` UI/UX節「登録後のリセット」）。
- 権限判定（管理者専用）は、メニュー単位（`scripts/014_seed_menu_structure.sql`で本画面を権限レベル9に設定。14-4）で行う。画面内アクション単位の権限チェックは持たない。

---

## 11. 明細請求書発行

都度得意先（`tax_unit = 3` 内税明細単位）の未請求かつ消込完了でない売上明細行を数件選び、
`detail_invoices`／`detail_invoice_sales_lines`へ確定する。採番系列は`SlipNumberKind.DetailInvoice`。

### 11-1. 対象条件 ― 連携テーブルの存在が唯一の正

共通業務ルール2（`docs/product-spec.md`）のとおり、対象条件は**未請求かつ消込完了でない**
売上明細行（`tax_unit = 3`）。「未請求」の判定は`sales.billing_status`（キャッシュ）ではなく
**`detail_invoice_sales_lines`に連携行が存在しないこと**で行う。締め請求（`billing_number`）とは
異なり、明細請求は連携テーブルの有無が唯一の正であり、`billing_status`はその写しに過ぎないため
（万一両者が食い違っても、連携行が無い行は候補に出て再請求でき、自己修復になる）。

この抽出条件は`DetailInvoiceService.BuildCandidateQuery`（private）に1本化し、画面表示用の
`GetCandidatesAsync`と、発行時にトランザクション内で再確認する`IssueAsync`の両方から使う
（9章「金額を出す経路を1本にする」と同じ方針）。

### 11-2. 税額計算・二重請求防止

税額計算には`ConsumptionTaxCalculator.CalculateInternalTaxPerLine`
（XMLコメントに「明細請求書用」と明記）を使う。返品行（`slip_type`=2）を含めても
マイナス金額のまま計算に含まれ、符号対称な端数処理により元の売上の税額をちょうど打ち消す。

発行時は、再計算した明細行ごとの税額合計が保存済み`sales.tax_amount`の合計と一致することを
検証する（9-4が伝票単位で行っている検証と対称。端数区分は登録後不変のため、本来一致するはず
のデータ異常を検出する）。

二重請求防止は`detail_invoice_sales_lines`のDB側UNIQUE制約（`UQ_detail_invoice_sales_lines_sales_line`）
とアプリ側の事前チェック（11-1の対象条件クエリを発行直前にトランザクション内で再実行）の二段構え。
`detail_invoice_sales_lines`はrowversionを持たない（行の追加・削除のみで更新が無いテーブルのため）
ので、この再確認とDB側のUNIQUE制約が排他制御の担保になる（`docs/architecture.md` 9章）。

**`tax_unit = 3`の締め請求データ（`sales.billing_number`）は常にNULLのまま。** `CK_sales_billing_number_by_tax_unit`
により内税明細単位は締め請求データを持てないため、発行時に更新するのは`sales.billing_status`
（`BillingLinkStatus.Billed`）のみで、紐付けは連携テーブルのみで行う。

### 11-3. 実装

- `src/bmcs_app.Application/Billing/DetailInvoiceService.cs`が本体。`GetCandidatesAsync`
  （画面表示用、保存しない）・`GetByNumberAsync`（既存分の読み取り専用取得。明細は連携テーブル
  経由で売上ジャーナルから組み立てる）・`IssueAsync`（発行の確定）を持つ。`IssueAsync`は
  伝票番号の採番（`SlipNumberKind.DetailInvoice`）を伴うため明示トランザクションで包む
  （`docs/architecture.md` 6章）。
- 画面（`Views/Billing/DetailInvoiceIssueWindow.xaml`／`ViewModels/Billing/DetailInvoiceIssueViewModel.cs`）
  は左＝取込候補／右＝請求書明細の2ペイン構成。一覧は`ListView`＋`GridView`（本プロジェクトの
  既存画面が一貫して使う一覧パターン。`DataGrid`は使わない）で、候補行↔明細行の移動は
  `RowActivationBehavior`によるEnter／ダブルクリックと、同じコマンドを再利用する「選択した行を
  追加 ▶」／「◀ 除外」ボタンの両方から行える。
- 一覧画面は持たず、明細請求書No.を直接入力してEnterで既存分を読み込む方式（得意先／商品マスタ・
  締め解除処理と同じコード直接入力方式）。明細請求書No.欄でSpaceを押すと検索モーダルを開ける
  （15-3章）。既存分を読み込んだ場合は読み取り専用表示にする。`detail_invoices`は「発行済／取消」
  の2状態で訂正の概念が無いため（訂正は12章の取消→再発行で行う）。
- 宛名（`addressee_name`）は得意先コード確定時に得意先名を初期値として転記し、手入力で
  上書き可能にする（他のジャーナル系画面と同じ「名称欄を直接書き換える」
  方式だが、`detail_invoices`は`customer_name`（得意先マスタのスナップショット）と
  `addressee_name`（印字用宛名）を別カラムで持つため、本画面では宛名専用の入力欄として分離している）。
- 発行成功後は完了メッセージを出して画面を起動直後の状態に戻す（`docs/product-spec.md` UI/UX節
  「登録後のリセット」）。
- 削除 (F8) は明細請求書の取消（12章）、印刷 (F11) は明細請求書の印刷（23章）で、いずれも
  ツールバーとキーバインドから使える。
- 前後の請求書ナビゲーション・登録件数表示・上書き保存（Upsert）は持たない。前項および「一覧を持たずコード直接入力」という既存画面の統一パターンを
  優先するため。

---

## 12. 明細請求書の取消

11章（発行）と対になる、明細請求書の取消。別画面にはせず、11章の画面（`DetailInvoiceIssueWindow`）
の「削除 (F8)」に配線している。別画面分離を要求しているのは締め解除処理・月次締め解除処理
（管理者権限のみの操作）に限られ、明細請求書の取消は対象に含まれないため。

### 12-1. 連携行は物理削除する

`docs/database-schema.md` 2.14節のとおり、取消では`detail_invoice_sales_lines`の該当行を
**物理削除**する（このテーブルが`row_version`を持たないのは行の追加・削除しか発生しない前提
のため）。ヘッダー（`detail_invoices`）は物理削除せず`invoice_status`を取消済（`2`）にし
`cancelled_at`／`cancelled_by`を立てるだけに留める（締め解除と同じ非破壊方式）。

連携行を削除すると、`DetailInvoiceService.BuildCandidateQuery`（11-1節）の
「どの明細請求書にも連携していない」という条件が自動的に真に戻るため、対象の売上明細行は
何もしなくても次回の候補に再び現れる。`UQ_detail_invoice_sales_lines_sales_line`も解放される
ため、同じ行を新しい明細請求書へ再発行できる（取消後に同じ売上を再度請求できる）。
副作用として、取消済みの明細請求書を`GetByNumberAsync`で読み込むと明細行は0件になる
（連携行自体が残っていないため）。ヘッダーの確定金額（`SalesAmount`／`TaxAmount`／`TotalAmount`
等）は取消後もスナップショットとして残るため、金額の追跡はヘッダー側で行う。

### 12-2. 取消の拒否条件（二重取消の防止に加えて2つ）

締め解除（10章）と異なり、明細請求書には繰越残高の概念が無いため**締め順序の制約は無い**
（どの明細請求書も他の明細請求書の集計に依存しない）。一方で明細請求書は入金と直接結びつくため、
以下の場合は取消を拒否する。

1. **連携先の売上明細行に消込済み（一部消込・消込完了のいずれか）の行が含まれる場合。**
   取消して未請求に戻すと、入金済みなのに未請求という業務上あり得ない状態になるため。
2. **この明細請求書を指定した明細入金（`detail_receipt.target_type=2`）が存在する場合。**
   取消すると入金の充当先が宙に浮くため（`FK_detail_receipts_detail_invoices`）。

排他制御はヘッダーの`RowVersion`（`DetailInvoice : AuditableEntity`）に委ね、`SaveChangesAsync`の
`DbUpdateConcurrencyException`を`DetailInvoiceException`へ変換する。複数明細行の伝票向けの
`SlipConcurrencyGuard`（`docs/architecture.md` 9章）は、連携行が単純な追加・削除しかしない
本ユースケースには使わない。

### 12-3. 実装

- `DetailInvoiceService.CancelAsync`（11章の`IssueAsync`と同じクラス）。
  ヘッダー取得→取消済みチェック→明細入金ガード→連携行・売上行取得→消込済みチェック→
  ヘッダー更新・連携行削除・売上行の`BillingStatus`復帰→保存、の順で明示トランザクション内で行う。
- 画面（`DetailInvoiceIssueViewModel`）: 既存分読込時に`LoadedInvoiceStatus`を保持し、
  取消(F8)は`IsExistingLoaded && LoadedInvoiceStatus == Issued`のときだけ有効（取消済み・新規時は
  無効）。実行前に`MessageBox`でYes/No確認（締め解除と同じ取消系操作の既定パターン）。
  成功後は画面を起動直後の状態に戻す（`docs/product-spec.md` UI/UX節「登録後のリセット」）。

---

## 13. 請求フェーズの構造上の制約

9〜12章（締め・解除・発行・取消）の機能間の組み合わせと、請求と売上訂正・取消（8章）の
相互作用について、現在の構造と制約を整理する。

### 13-1. 締め請求と明細請求は構造的に分離している

締め得意先（`tax_unit`＝請求単位／伝票単位。9・10章が対象）と都度得意先（`tax_unit`＝内税明細
単位。11・12章が対象）は`customer.tax_unit`（登録後不変）で完全に分離され、`sales.tax_unit`も
これをスナップショットする。`BillingClosingService.BuildCandidatesAsync`は
`TaxUnit.Invoice || TaxUnit.Slip`の得意先のみを対象にし、`DetailInvoiceService.BuildCandidateQuery`
は`TaxUnit.Line`のみを対象にするため、**同じ売上明細行が締め請求と明細請求の両方に載る経路は
構造的に存在しない**。`CK_customers_tax_unit_closing_day`（内税明細単位⇔`closing_day=0`）により、
都度得意先が締め処理の対象得意先抽出条件（`closing_day`一致）に紛れ込むこともない。

締め順序（9-5）と解除順序（10-1）は対称な判定で、前回残高チェーンが破綻しない。二重締めは
アプリ側チェック＋フィルタ付き一意インデックス、二重請求はアプリ側再確認＋UNIQUE制約の
二段構えで、いずれもDB側の最終防衛線がある。`receipt_amount`の伝票単位重複計上も
`GroupBy(ReceiptSlipNumber).First()`で回避している。

### 13-2. 売上の編集ロックは明細請求書発行済みを含む4条件

売上の編集ロック（`SalesEditLockEvaluator`）の判定順は
①請求締め（`billing_number`）→②明細請求書発行済み→③月次締め→④消込完了
（`docs/product-spec.md`共通業務ルール5・`docs/database-schema.md` 1章）。
②は、いずれかの明細行が`detail_invoice_sales_lines`に連携していることを指す。

②が必要なのは、`tax_unit=3`の`billing_number`が`CK_sales_billing_number_by_tax_unit`により
常にNULLのため①が都度得意先には決して発火せず、明細請求書を発行済みでも売上入力画面から自由に
訂正・取消できてしまうため。その場合は次の不整合が生じる。

- `detail_invoices`ヘッダーの確定金額（スナップショット）が実データと乖離する。
- 訂正で行を論理削除しても`detail_invoice_sales_lines`の連携行は残り、`IsDeleted=true`の売上を
  指し続ける（`DetailInvoiceService.GetByNumberAsync`は`!IsDeleted`で絞っていないため、その行を
  明細として表示し続ける）。

DBアクセス（`detail_invoice_sales_lines`の存在確認）は`SalesEditLockService.EvaluateAsync`が
行い、判定ロジック自体は純粋関数（`SalesEditLockEvaluator.Evaluate`）のまま維持する。
`SalesService.UpdateAsync`／`CancelSlipAsync`は`SalesEditLockService`経由のため、訂正後の再判定にも
自動的に効く。

### 13-3. EF例外は業務例外へ変換し、ViewModelは例外を握りつぶさない

`docs/architecture.md`は「`DbUpdateConcurrencyException`はApplication層で捕捉し、ViewModelに
EF Coreの例外型を漏らさない」と規定している。`BillingClosingService.ConfirmAsync`は
`DbUpdateException`（二重締めのユニーク制約違反）・`DbUpdateConcurrencyException`（rowversion競合）
を`BillingClosingException`へ、`BillingReleaseService.ReleaseByBillingDateAsync`は
`DbUpdateConcurrencyException`を`BillingReleaseException`へ、`DetailInvoiceService`は
`IssueAsync`／`CancelAsync`とも`DetailInvoiceException`へ変換する。3つの例外クラスは
`(string message, Exception? inner = null)`のシグネチャで揃えている。

請求系3画面のViewModel（`BillingClosingViewModel`・`BillingReleaseViewModel`・
`DetailInvoiceIssueViewModel`）は業務例外に加えて`catch (Exception)`のフォールバックを持つ
（`SalesEntryViewModel`／`OrderEntryViewModel`と同じ形）。fire-and-forgetで呼ぶ
`BillingClosingViewModel`の`_ = RefreshAsync()`（9-7節参照）と
`DetailInvoiceIssueViewModel`の`_ = ApplyCustomerAsync(...)`は、例外が呼び出し元で観測されず
`TaskScheduler.UnobservedTaskException`（ログのみ・UI通知なし）に落ち一覧が黙って古いままに
なるため、各メソッド内部でtry/catchして処理する。

### 13-4. 既知の制約（対応しない）

入金の下限に関するリスク（前回締め日より前の日付で後から登録された入金が拾われない）は
9-2を参照。

**`sales.billing_status`とリンクの一致にDB側の防波堤は持たない。**
`billing_status`（請求済／未請求）と実際の紐付け（`tax_unit`1/2は`billing_number`、
`tax_unit=3`は`detail_invoice_sales_lines`の存在）の一致は、アプリのトランザクションだけが
担保しており、DB側のCHECK制約は無い。二重請求側（`UQ_detail_invoice_sales_lines_sales_line`）・
二重締め側（`UQ_billings_customer_closing_ym_confirmed`）はDB側の最終防衛線を持つのと非対称だが、
`tax_unit=3`側は連携テーブルの存在確認が必要でCHECK制約として書けないため、`tax_unit`1/2側だけ
追加しても非対称が残る。このためDB制約は追加しない。

---

## 14. メインメニュー画面

メインメニューは**リスト・アコーディオン型**で、ウィンドウは縦長（幅300、高さは作業領域に合わせる。`MainMenuWindow.xaml`）。
表示内容はメニュー構成マスタ（`menus`）で決まる。

### 14-1. 前提: 起動時の社員コード

`ICurrentEmployeeContext`の実装は`StartupArgsCurrentEmployeeContext`（ショートカット引数の1つ目を
社員コードとして使う。引数なしは開発用に`EMP001`へフォールバック）。詳細は`docs/architecture.md` 14章。
権限レベルの異なる社員コードで起動すると表示メニューが変わる。

### 14-2. メニュー構成マスタの編集範囲

「メニューマスタ管理」画面（管理者専用、`screen_key="menu_master"`、`MenuMasterWindow`／`MenuMasterViewModel`、
更新は`MenuService.UpdateAsync`）から編集できるのは、既存項目の**表示名・表示順・必要権限レベル（子のみ）・
初期展開（親のみ）**だけ。項目の追加・削除、`screen_key`・親子関係の変更は画面から行わず、
`scripts/`のSQLへ直接記述する運用（`screen_key`は`OpenMenuItemCommand`の`switch`に対応するコードが要るため）。
更新は`row_version`による楽観的排他で、変更はメインメニューの次回起動時に反映される。
`014`を再適用すると画面での編集内容は`014`の値に戻る。
実データは`scripts/014_seed_menu_structure.sql`が投入する（開発用テストデータの
`scripts/seed_dev_data.sql`とは分離。menuは実運用でも使う本物の構成データのため）。

### 14-3. 階層・権限フィルタの実装

- `MenuTreeBuilder`（Domain/Calculations、純粋関数）が`menus`の全行と社員の権限レベルから
  「カテゴリ（親）→表示可能な機能（子）」の階層を組み立てる。子の
  `required_permission_level ≦ 社員の権限レベル`のものだけを残し、表示できる子が1件も無い
  カテゴリはメニュー自体を表示しない（単体テスト`MenuTreeBuilderTests`）。
- `MenuService.GetMenuTreeAsync`（Application/Master）が`menus`全行（論理削除除く）を取得する
  だけで、フィルタは行わない（フィルタはDomain層の責務）。
- `MainMenuViewModel.LoadAsync`が`ICurrentEmployeeContext.EmployeeCode`から
  `EmployeeService.GetByCodeAsync`で社員名・権限レベルを取得し、上記フィルタを適用して
  `Categories`（画面表示用の`MenuCategoryDisplayItem`/`MenuItemDisplayItem`）を組み立てる。
  社員コードが見つからない場合は権限レベル0として扱い、警告メッセージを表示する
  （メニューが1件も表示されない状態になる）。
- 画面遷移は`screen_key`文字列を`switch`する`OpenMenuItemCommand`に一本化している
  （個別の`OpenXxxCommand`は持たない）。未知の`screen_key`は警告メッセージを表示し、
  例外にしない（`menus`データの入力誤りに対する防御）。
- カテゴリの色分け（アコーディオン見出しの左バー・ホバー色）は表示順に応じた固定パレットを
  循環させる。ViewModelはWPFのMedia型（Brush）を持たず整数インデックス（`ColorIndex`）のみを
  持ち、View側の`MenuAccentColorConverter`がインデックス→Brushへ変換する
  （ViewModelをWPF依存にしない、既存の`EnumDisplayConverter`と同じ方針）。

### 14-4. 実装済み画面のみを対象にする

メニューには実装済みの画面だけを登録し、画面を実装するたびに`scripts/014_seed_menu_structure.sql`へ
追記する。現在の構成は、受注・売上（受注入力・売上入力）／請求（請求締め処理・締め解除処理・
明細請求書発行）／入金（入金入力・明細入金）／元帳（得意先元帳）／月次（月次締め処理・月次締め解除
処理）／マスタ管理（得意先・商品・社員・自社情報・銀行・入金方法・プリンタ設定・メニューマスタ管理）の各画面。
データ横断検索は専用画面が未実装のためメニューに無い。

権限レベルは、締め解除処理・月次締め解除処理（権限差を別画面として表現する対象）と
社員マスタ・自社情報（機微な情報のため）・メニューマスタ管理を9（管理者専用）、それ以外を1（一般）とする。
数値の重み付けは暫定の解釈であり、`scripts/014_seed_menu_structure.sql`の値を書き換えるだけで
調整できる（コード変更は不要）。

---

## 15. ジャーナル系画面の伝票No入力欄の挙動

伝票No入力欄の操作仕様（空欄`Enter`＝新規登録モードで次の入力欄へ、伝票No入力`Enter`＝読込、
存在しない伝票Noはエラー表示のみ、`Space`で検索ダイアログ）は`docs/product-spec.md` UI/UX節
「ジャーナル系画面の伝票No入力欄の挙動」を参照。対象は売上入力・受注入力・明細請求書発行・
入金入力の4画面（入金入力の画面レイアウトも他3画面に合わせ「伝票No（左）→日付（右）」）。
本章は実装方式を記す。

### 15-2. フォーカス移動は ViewModel 起点の1機構に統一

`EnterKeyNavigationBehavior`（Enterをタブ相当として扱う添付ビヘイビア）は伝票No欄の
ためには変更しない。「Enterに対する`KeyBinding`を持つTextBoxでは譲る」というガードに
CanExecute判定を足す案は、`[RelayCommand]`が生成する`AsyncRelayCommand`が既定で実行中
`CanExecute()`がfalseを返すため、非同期Lookupの実行中にEnterを二度押すとフォーカスが勝手に
飛ぶ誤爆が起き、しかもこのビヘイビアは多数の画面に適用済みで得意先コード欄・商品コード欄など無関係な
欄にまで波及するため採らない。

代わりに`ViewModelBase`の`FocusRequested`イベント（`RequestFocus(string focusKey)`）と、
`Behaviors/FocusBehavior.cs`の`FocusKey`添付プロパティを使う。伝票No欄で空欄`Enter`／読込成功の
両方から同じ`RequestFocus(key)`を呼び、対応する`FocusKey`を持つ入力欄（売上入力→`"SlipDate"`、
受注入力→`"OrderDate"`、明細請求書発行→`"IssueDate"`）へフォーカスを移す。XAMLの宣言順が
「日付→伝票No」のため、`MoveFocus(FocusNavigationDirection.Next)`では日付欄に戻れない
（タブ順の都合で別の欄に飛ぶ）。明示的なキー指定にしたのはこのため。

売上入力の受注No.欄（受注からの売上確定）も同じパターンで、`FocusKey="CustomerCode"`
（得意先コード欄）へ移動する。

**例外**: 明細請求書発行で既存分を読み込んだ場合（`IsExistingLoaded = true`）は請求日付・得意先・
宛名がすべて`IsReadOnly`になるため、フォーカスを請求書No欄に留める（`RequestFocus`を呼ばない）。

### 15-3. 明細請求書の検索モーダル

明細請求書発行画面の`Space`キー伝票検索は、既存の汎用検索モーダル
（`Views/Common/SlipSearchDialog.xaml`）を使う。

- `SlipSearchTarget.DetailInvoice`を対象とする。
- `DetailInvoiceService`（採番・発行・取消のコマンドサービス）に検索を足すと`SlipNumberService`等の
  依存一式が付いてくるため、`SalesQueryService`/`OrderQueryService`と対称な
  `DetailInvoiceQueryService.SearchAsync`（読み取り専用）が検索を担う。
- 都度得意先は学校のクラス・先生単位など宛名（`AddresseeName`）で識別することが多いため、
  **宛名でも検索できる**。`SlipSearchItem`の得意先名欄に得意先名と宛名が異なる場合
  `"得意先名（宛名）"`の形で併記し、既存のキーワード一致ロジックを変更せずに宛名検索を成立させている。
- 取消済み（`DetailInvoiceStatus.Cancelled`）も検索結果に含める（`GetByNumberAsync`と同じ扱い）。
- `EnumDisplayConverter`が`DetailInvoiceStatus`を日本語（発行済／取消済）で表示する。
- 明細請求書発行画面のルート`Grid`に`EnterKeyNavigationBehavior.IsEnabled="True"`を設定している
  （他の2画面と揃え、請求日付欄でEnter確定できるようにする副次効果もある）。

---

## 16. 入金消込サービスの実装

`SettlementService`が`sales.settlement_status`/`settled_amount`（消込キャッシュ列）を
入金データから導出して書き戻す。呼び出し元は売上訂正・取消、締め解除、入金入力（17章）・
明細入金（18章）・入金の取消訂正（19章）の各ユースケース。

### 16-1. 決定事項

1. **消込対象額は売上明細行の`amount`をそのまま使う。** 請求単位・伝票単位（外税）は税抜
   金額、内税明細単位は税込金額（`amount`が既に税込）であり、消費税分は明細行レベルの
   消込には載せない。例: 明細6,000円＋4,000円＝10,000円の請求（税1,000円）に全額11,000円
   入金すると両行とも消込完了になり、残1,000円（税額分）は行に載らない。
2. **振込手数料差額（`fee_adjustment_amount`）は消込済金額に含める。** 売上明細行への
   充当額は`allocated_amount + fee_adjustment_amount`。入金側自身の充当状態
   （`AllocationStatus`）の判定には含めない（実際に受け取った現金の消化状況を表すため）。
3. **締め入金（`receipts`）は`billings`単位で充当するが、キャッシュ列は`sales`明細行にある。**
   `billing_number`へ充当した額を、その`billing_number`を持つ売上明細行へ伝票日付→伝票
   番号→行番号の古い順に配分する。

### 16-2. アーキテクチャ: デルタ方式ではなく「得意先スコープの再計算方式」

キャッシュ列は登録・取消・訂正のいずれでも実態と一致しなければならない。`OrderStatusService`の
ようなデルタ方式（差分だけを適用する）ではドリフトを許す（訂正で充当先自体が変わるケースを
追跡しきれない）。代わりに`SettlementService`は**入金データ（`receipts`／`receipt_allocations`／
`detail_receipts`）から得意先単位で毎回全件再計算して書き戻す**（請求集約グループへの展開は28章）。

スコープを「伝票」ではなく「得意先」にした理由:

- `customer_code`は`sales`/`receipts`/`detail_receipts`いずれも伝票単位の値で、訂正でも
  変わらない。訂正で充当先（`billing_number`等）が変わった場合、変更前・変更後の両方の
  グループを自動的に再計算できる（差分追跡が不要になる）。
- 都度得意先の「直接指定（`target_type=1`）」と「明細請求書経由（`target_type=2`）」の
  合算が、得意先の全売上・全明細入金が同時に視界に入ることで単純なローカル計算になる。
- 消込整合性レビュー（20章）が同じ関数を「実態」の定義として再利用できる。

公開メソッドは`SettlementService.RecalculateForBillingGroupAsync(string customerCode)`の
1本のみ。得意先の`TaxUnit`で締め得意先向け（`RecalculateClosingAsync`）・都度得意先向け
（`RecalculateDetailAsync`）に内部分岐する。

`RecalculateForBillingGroupAsync`は入金入力・明細入金・入金の訂正・取消のほか、
`BillingReleaseService.ReleaseByBillingDateAsync`（締め解除）と`SalesService.UpdateAsync`／`CancelSlipAsync`
（売上の訂正・取消）の`SaveChangesAsync`後・`CommitAsync`前にも呼ばれる。締め解除では
`sales.billing_number`が外れた行が充当先を失い未消込へ戻る。売上訂正・取消では、一部消込の行を
金額を減らす方向へ訂正できる（編集ロック条件4は`FullySettled`のみが対象）ため、消込済金額が
新しい金額を超えて取り残らないよう、実際の入金データに基づき新しい金額へ丸め直される。

### 16-3. 配分アルゴリズム（返品・値引の符号を正しく扱う）

対象額（`amount`）はマイナス（返品・値引行）を含みうる。素朴な「マイナス行を先に全額
充当し、その分を残額に戻す」方式は、入金が全く無い（`pool = 0`）場合でも返品行だけが
消込完了になる事故を起こす（`SalesEditLockEvaluator`の編集ロック条件4・
`DetailInvoiceService`の明細請求書候補除外に波及する実害がある）。

`SettlementAllocator.Allocate`（`src/bmcs_app.Domain/Calculations/`）は次の3分岐で、
この事故を起こさずに全額充当・過入金・不足の各ケースを扱う。

1. `pool == 0` → 全行0。
2. 対象額の合計（`netTarget`）が`pool`と同符号かつ`|pool| >= |netTarget|` → 各行を
   対象額のとおりに配分する（返品・値引行も含めて全額消込完了になる。超過分は行に
   載せない）。
3. それ以外（不足） → `pool`と同符号の行だけに、呼び出し元が整列した順（伝票日付→
   伝票番号→行番号の古い順）で`min(残額, 残対象額)`を配分する。符号が異なる行は0の
   まま据え置く（返品行を消し込むには全額充当が必要という業務上の前提と一致する安全側
   の挙動）。

### 16-4. 直接指定と明細請求書経由の合算順序

都度得意先（内税明細単位）の売上明細行は、明細入金の「直接指定」と「明細請求書経由」の
両方から同時に充当されうる（`docs/database-schema.md` 2.11節）。`RecalculateDetailAsync`
は**直接指定分を先に確定し、残額（`amount - 直接充当額`）を明細請求書経由の配分に回す**
（名指しした明示的な指示を、システムが行う配分（導出）より優先する）。

### 16-5. トランザクション境界と監査列

再計算方式は「入金行を保存→DBを読み直して再計算」という構造上、1ユースケース内で
`SaveChangesAsync`を2回呼ぶ（①呼び出し元の入金行保存 → ②`SettlementService`内の
消込キャッシュ列の保存）。これは`docs/architecture.md` 6章の明示トランザクションの許容ケースの
1つ。`SettlementService`自身は`BeginTransactionAsync`/`CommitAsync`を呼ばず、呼び出し元が開始した
明示トランザクションに参加する（`OrderStatusService`と同じ構成）。明示トランザクションが開始
されていない場合は`InvalidOperationException`を投げる（`SlipNumberService.NextAsync`と同じ
アサーション）。

キャッシュ再計算は**値が実際に変わった行だけ**をModifiedにする（`SlipConcurrencyGuard.
TouchAll`とは逆方針。`TouchAll`は編集中の1伝票に対する意図的な照合強制だが、再計算は
得意先の全行に及ぶ派生更新であり、全行を対象にすると無関係な伝票を編集中の別ユーザーを
不要に弾いてしまう。`docs/architecture.md` 9章参照）。

### 16-6. 名前空間の衝突に関する注意

`docs/architecture.md` 10章の機能フォルダ規約に従い`SettlementService`は
`src/bmcs_app.Application/Receipt/`（名前空間`bmcs_app.Application.Receipt`）に置く。
このため、**`bmcs_app.Application.*`名前空間配下（`Application`本体・`Application.Tests`
の両方）で`using bmcs_app.Domain.Entities;`により`Receipt`型を裸で参照しているコードは、
名前空間`bmcs_app.Application.Receipt`と衝突してコンパイルエラーになる**（C#は enclosing
namespace のメンバーを using 導入の型より優先して解決するため）。
`using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;`のようなエイリアスで参照する。
**`bmcs_app.Application.*`配下にファイルを追加する際、エンティティ`Receipt`を裸で参照しないこと**
（`SettlementService.cs`・`SettlementServiceTests.cs`・`BillingClosingServiceTests.cs`は既にこの形）。

### 16-8. 既知の限界（対応しない）

- **解除済み`billings`を指したままの`receipts`行は`allocated_amount`が入力データとして
  残る。** `SettlementService`は入力データ（`allocated_amount`／`fee_adjustment_amount`）
  を書き換えず、締め解除も充当を付け替えない。解除で外れた売上行の消込キャッシュ列だけが
  再計算で未消込へ戻る。後にその入金を訂正すると、再構築時の充当先は確定済み`billings`のみが
  対象になるため、別の確定済み`billings`があればそちらへ、無ければ前受行へ回る
  （19章19-5節）。
- **返品・値引行（マイナスの`amount`）が明細入金から直接指定される場合の符号の組み合わせ
  は実務上の発生例が未確認。** `SettlementAllocator`・`SettlementStatusCalculator`は
  符号対称に実装しているが、実データでの検証は行っていない（発生した場合に単体テストを
  追加して確認する）。

---

## 17. 入金入力画面の実装

締め得意先（請求単位／伝票単位）専用の新規登録画面。都度得意先の入金は明細入金画面
が担う。

### 17-2. 決定

締め得意先の入金は「過去の請求額に対して、入金伝票の合計金額を入金する」ものであり、**どの
請求に充当されたかは利用者にとって重要ではなく、知りたいのは残高である**。一方で1回の入金が
複数の支払手段（現金・振込・手形等）に分かれることがあり、**入金方法は行ごとに選べる必要が
ある**。この業務実態に基づき、次の方針とする。

1. **`receipts`の明細行は支払手段の内訳（入金方法＋金額＋行摘要）とする。**
   入金先口座は行単位（入金方法マスタの
   `requires_bank_account`が立つ行のみ）、手形期日も行単位（`requires_bill_due_date`が立つ行のみ）
   で持つ。
2. **請求への充当（`billing_number`／`allocated_amount`／`fee_adjustment_amount`）は
   `receipt_allocations`テーブルへ分離し、画面には表示しない内部データとする。** 支払手段の
   内訳行の件数と、充当先の請求の件数は一致しない（例: 入金2行の合計を3件の請求へ古い順に
   充当）ため、同じ行に両方の意味を持たせることはできない。保存時に`receipts`の明細行合計額を
   確定済み`billings`の未消込残額へ古い順に自動配分し、`receipt_allocations`へ書き込む（配分
   ロジックは16章の`SettlementAllocator`）。
   詳細なテーブル定義は`docs/database-schema.md` 2.10節・2.10-1節を参照。
3. **画面には請求残高（得意先確定時点の未収額合計）を表示する。** 充当先の一覧は表示しない
   （1の理由により、利用者にとって重要ではないため）。
4. **機能は新規登録、既存伝票番号による読込、読込後の訂正・取消。** 訂正・取消の詳細は19章。
5. **振込手数料差額（`fee_adjustment_amount`）の入力はこの画面のスコープ外。** 手数料差額の
   入力画面は保留中で未確定（手入力のみ。自動計算・自動補正提案は行わない）。入力先は
   `receipt_allocations.fee_adjustment_amount`になる予定であり、本画面が新規登録する充当行は常に`0`。

## 18. 明細入金画面の実装

都度得意先（内税明細単位、`tax_unit=3`）専用の新規登録画面。締め得意先（請求単位／伝票単位）の
入金は入金入力画面が担う。消込サービス（`SettlementService`、16章）・明細請求書発行・
`detail_receipts`テーブル・採番系列と組み合わせ、`detail_receipts`へ書き込む唯一の入口になる
（`target_type=2`のデータはseed以外に生成経路が無い）。


### 18-1. 設計判断

| # | 論点 | 決定 |
|---|---|---|
| 1 | 充当先の粒度 | 売上伝票タブ＝**売上明細行**単位（`target_type=1`）、明細請求書タブ＝**明細請求書まるごと1行**（`target_type=2`）。行単位ではなく請求書単位とするのは、`docs/database-schema.md` 2.11節のスキーマと、完了条件「売上伝票**または**明細請求書を指定した」に合わせるため |
| 2 | 明細行の金額 | **読み取り専用**（常に対象の全額または残額を充当）。手入力での減額はしない |
| 3 | 手形期日 | **画面に持たない**。`detail_receipts`に`bill_due_date`列が無いため、入金方法の選択肢から手形（`requires_bill_due_date=1`）を除外する（18-2参照） |
| 4 | 前受金 | **無し**。充当先がNULLの行は作らない（入金入力画面の前受・過入金行とは対照的） |
| 5 | 手数料差額 | 常に`0`（手数料差額の入力画面は保留中で未確定） |
| 6 | 訂正・取消 | 新規登録＋既存伝票番号による読込に加え、読込後の訂正・取消が可能（19章） |

**決定1・2の帰結（既知の制約）**: 金額が読み取り専用かつ請求書がまるごと1行のため、
「明細請求書の一部の売上だけ入金があった」ケースは、請求書タブではなく**売上伝票タブから
該当売上明細行を個別に取り込んで対応する**。`SettlementService.RecalculateDetailAsync`が
「直接指定を先に確定してから残額を明細請求書経由の配分に回す」設計のため、この併用は
既存ロジックで正しく処理される。

### 18-2. 候補条件・拒否条件

骨格は、候補条件2本＋トランザクション内再確認＋`receipt_amount = Σallocated`＋同一tx内
`SettlementService.RecalculateForBillingGroupAsync`呼び出しである（`DetailReceiptEntryService`）。
その上で次の条件を課す。

1. **候補条件は「未消込または一部消込」とする。** 未消込だけだと、一部消込済みの売上明細行が
   売上タブにも請求書タブ（連携行が全行未消込）にも出せなくなり、残額を永久に入金できない
   行き止まりが生まれるため。売上伝票タブの候補金額は**残額**（`sales.amount − sales.settled_amount`）
2. **`Σallocated_amount`（=`receipt_amount`）が0になる保存は拒否する。** `AllocationStatusCalculator`は
   合計0を「未充当」と判定するため、合計0の伝票は永久に充当完了にならない
3. **`Σallocated_amount`は0より大きい値のみ許可する**（入金入力画面と同じ規則）。行単位のマイナス
   （返品・値引の売上明細行を直接指定するケース）は許可し、正の行との差引を認める。返品行
   単独・返品合計が正の行を上回るケースは保存時に拒否される（＝その返品は将来の売上と相殺
   するまで「未消込」のまま残る。返金処理ではないという業務判断）
4. **金額ゼロを候補から除外する。** 売上明細行の残額が0、明細請求書の`total_amount = 0`は
   候補にしない。`SettlementStatusCalculator`はamount=0の行を常に「未消込」と判定するため
   （消込完了にならない）、直接指定すると`DetailInvoiceService.CancelAsync`の消込済みチェックを
   すり抜けたまま明細入金だけが残る不整合を作れてしまうため
5. **明細請求書タブの「この請求書を指す明細入金が存在しない」条件に`!IsDeleted`を付ける。**
   付けないと、取消済みの明細入金がこの請求書を永久にブロックする
6. **保存時に`detail_invoice.total_amount == Σ(連携先売上明細行のamount)`を照合し、不一致なら
   拒否する。** `DetailInvoiceService.IssueAsync`が税額で同じ防御をしている前例に合わせる。
   理論上は発行時の計算式から常に一致するはずだが、データ補正等でズレた場合に自動で丸めず
   拒否するほうが安全
7. **入金方法が振込以外の行は`bank_account_code`を必ず`null`にする**
8. **入金方法の選択肢から手形（`requires_bill_due_date=1`）を除外する。** `detail_receipts`に
   `bill_due_date`列が無く、入金入力画面は手形選択時に期日入力を必須にしているため、この画面で
   手形を選べると期日情報が保存できずに欠落する。除外は入金方法マスタのフラグで表現し
   （27章）、専用フラグは持たない。手形を含まない選択肢（現金／振込／相殺等）のみ表示する
9. **同時実行制御は既存のrowversion楽観的排他に委ねる（追加のロックは実装しない）。** `Sales`・
   `DetailReceipt`はいずれも`AuditableEntity`を継承し`RowVersion`を持つ。2人が同じ売上明細行に
   同時に全額入金しようとした場合、`SettlementService.RecalculateForBillingGroupAsync`が対象の
   `Sales`行をtracking付きで読み込み・更新するため、後にコミットする側はrowversion不一致で
   `DbUpdateConcurrencyException`→`SlipConcurrencyException`で弾かれる。入金入力画面・消込サービスと
   同一の機構であり、`detail_receipts`自体に一意制約は付けない（付けると決定1の部分入金・残額
   充当が壊れるため）

### 18-3. 実装構成

- `DetailReceiptEntryService`（Application/Receipt）: `GetSalesLineCandidatesAsync`／
  `GetDetailInvoiceCandidatesAsync`（画面表示用の候補取得）、`SaveNewAsync`（新規登録。金額は
  利用者の入力に頼らずトランザクション内で候補条件を再実行してサーバー側で確定する）、
  `GetByNumberAsync`（読込）、訂正・取消（19章）
- `DetailReceiptQueryService`（Application/Receipt）: 伝票検索モーダル用の検索
  （`ReceiptQueryService`と対称）。`SlipSearchTarget.DetailReceipt`
- `DetailReceiptEntryViewModel`／`DetailReceiptLineViewModel`（Presentation/Receipt）:
  入金入力画面の明細行パターンと、明細請求書発行画面の候補→明細取込パターンを合成
- `DetailReceiptEntryWindow.xaml`（Presentation/Receipt）: 左＝入金登録の明細行、右＝上段タブ
  （売上伝票／明細請求書）＋下段の選択伝票明細。売上伝票タブは行単位で取込み、明細請求書タブは請求書単位（上段リストの行から
  まるごと取込み、下段は参考表示・読み取り専用）
- メインメニューに「入金 > 明細入金」（`scripts/014_seed_menu_structure.sql`、
  `screen_key = detail_receipt_entry`、`menu_code = MNU_DETAIL_RECEIPT`。`varchar(20)`制約のため
  `MNU_DETAIL_RECEIPT_ENTRY`から短縮）

### 18-4. 未解決事項

- **手数料差額の入力画面（保留中）を実装する場合**: 「金額は常に対象の全額」という前提を
  再検討する必要がある（`allocated = 全額 − fee`とする）

## 19. 入金の取消・訂正の実装

`ReceiptEntryService`／`DetailReceiptEntryService`は`UpdateAsync`（訂正）・`CancelSlipAsync`（取消）・
`EvaluateEditLockAsync`（編集ロック判定）を持ち、入金入力・明細入金の両画面の「取消 (F8)」・保存(F10)
に配線される。消込の巻き戻し自体は16章の`SettlementService.RecalculateForBillingGroupAsync`
（得意先単位の全件再計算）を呼ぶだけで実現する（売上入力の`SalesService.UpdateAsync`／
`CancelSlipAsync`と同じ設計）。

### 19-1. `receipts`の編集ロックに請求締めスナップショット条件を加える

`BillingClosingService.BuildCandidateAsync`（9章）は締め処理時に`receipt.Amount`の合計（前回確定
`billing.billing_date`〜今回`closing_date`の期間で集計）を`billing.CurrentBillingAmount`
（`= PreviousBalance − ReceiptAmount + Tax...`）へ**スナップショットとして焼き込み、以後誰も
再計算しない**。この期間の`receipts`を無条件に取消・訂正できると、確定済み請求の残高が入金1件分
だけ二重に増減する不整合が生まれ、`ReceiptEntryService.GetOutstandingBillingsAsync`
（`receipt_allocations`から都度計算する現在値）と`billing.CurrentBillingAmount`（締め時点の
スナップショット）が永久に食い違う。このため`receipts`の編集ロックには月次締めに加えて
「請求締めスナップショット」条件を課す。`detail_receipts`はこの問題を持たない
（`detail_invoices`の金額は`sales`から都度導出され、`detail_receipts`からスナップショットを
焼き込まれることがないため）ので、追加条件は不要。

### 19-2. 編集ロック方針

| 伝票 | ロック条件 |
|---|---|
| `receipts`（締め入金） | (a) 月次締め: `customer_code`＋`receipt_date`の年月に対応する確定済み`monthly_closings`が存在する（判定は`MonthlyClosedService.IsClosedAsync`に集約。請求集約先の確定も含む）。(b) 請求締めスナップショット: `receipt_date <= MAX(その得意先の確定済みbilling.billing_date)` |
| `detail_receipts`（明細入金） | 月次締めのみ |

判定結果の型は既存の`SalesEditLock`（`bmcs_app.Domain.Calculations`の
readonly record struct）を再利用する。専用の編集ロック判定クラス（`SalesEditLockService`
相当）は置かず、`ReceiptEntryService.EvaluateEditLockAsync`／`DetailReceiptEntryService.
EvaluateEditLockAsync`として各サービスの public メソッドに実装する。理由は判定条件が
「日付とmonthly_closing／billingの突き合わせ」の数行で、`SalesEditLockEvaluator`のように
複数エンティティのプロパティを見る分岐ロジックが無く、抽出しても再利用先が無いため
（Minimal Impact）。

請求締めスナップショットに抵触して訂正・取消できない場合は、締め解除してから操作する
（`BillingReleaseService.ReleaseByBillingDateAsync`は最新の確定`billings`のみ解除可能という既存制約と整合する）。

### 19-3. 明細入金（detail_receipt）の訂正は充当先の追加を許さない

訂正で変更できるのは入金日付・伝票摘要（伝票単位）、各行の入金方法・入金先口座・行摘要、および
**行の削除**のみ。新しい充当先（売上明細行・明細請求書）の追加はできない（追加したい場合は
別伝票で登録する）。`DetailReceiptLineCorrection`（訂正用の行入力型）は`TargetType`等の充当先
フィールドを一切持たないため、シグネチャ上も追加不可能になっている。

これに伴い、**既存行の`AllocatedAmount`／`FeeAdjustmentAmount`は訂正時に再計算せず、読込時の
値をそのまま保持する。** 候補判定（`BuildSalesLineCandidateQuery`／`BuildDetailInvoiceCandidateQuery`）
は自伝票自身の充当を除外する仕組みを持たないサーバー側クエリであり、訂正のたびに再実行すると
以下の問題が起きるため、再実行しない。

- **誤って拒否される**: 明細請求書指定の行は、保存した時点で自分自身が「この請求書を指す
  未削除の`detail_receipts`」として存在するため、`BuildDetailInvoiceCandidateQuery`を再実行すると
  常に自分自身の存在で弾かれる（日付だけを変える訂正すら失敗する）。
- **他の伝票の影響で金額が変わる**: `sales.settlement_status`は他の`detail_receipts`の登録・取消に
  よって変動するキャッシュ列であり、訂正の意図とは無関係にこの伝票の充当額が動いてしまう。

`receipts`（締め入金）は候補概念が無く充当は完全に内部自動計算のため、訂正は金額・入金方法・
行の追加削除すべて自由（`receipt_allocations`は訂正のたびに全面再構築する。19-4節）。

### 19-4. `UpdateAsync`の実装（行単位の差分方式）

売上入力の`SalesService.UpdateAsync`と同じ「行単位の差分」方式を採る（全行を論理削除して作り直す方式は
採らない。`SlipConcurrencyGuard.EnsureLineSetUnchanged`で検証した行がそのまま更新対象になっている
という保証を保つため）。

**`ReceiptEntryService.UpdateAsync`の処理順**（`receipts`本体は行単位の差分、`receipt_allocations`は
全面再構築）:

1. `ValidateLines`（新規登録と共通）
2. 対象`receipts`行を取得 → `SlipConcurrencyGuard.EnsureLineSetUnchanged` → 編集ロック判定（訂正前）
3. 行diff（更新・論理削除・追加）を適用
4. 編集ロック判定（訂正後。新しい`receipt_date`で再判定）
5. 対象伝票の`receipt_allocations`を全部論理削除 → **ここで`SaveChangesAsync`を1回呼ぶ**
6. `GetOutstandingBillingsAsync`で請求残高を再取得 → 新しい合計額で`BuildAllocationLines`を実行 →
   新しい`receipt_allocations`行を追加
7. `SaveChangesAsync` → `SettlementService.RecalculateForBillingGroupAsync` → コミット

手順5で`SaveChangesAsync`を挟むのが唯一の非自明な点である。`GetOutstandingBillingsAsync`は
サーバー側の`GroupBy`／`ToDictionaryAsync`クエリであり、ChangeTracker上の未コミットな論理削除を
見ない。挟まずに手順6を実行すると、自分自身の旧充当が「まだ生きている」ものとしてカウントされ、
請求の未消込残額が実際より少なく計算され、全額充当済みだった請求へ減額訂正すると全額が前受行
（`billing_number = NULL`）に落ちてしまう。`docs/architecture.md` 6章の「1ユースケース内で複数回
`SaveChangesAsync`」の許容ケースに該当する。

**`DetailReceiptEntryService.UpdateAsync`は単一フェーズ**（候補クエリを一切呼ばないため、
`SaveChangesAsync`を挟む必要がない）:

1. 対象`detail_receipts`行を取得 → `SlipConcurrencyGuard.EnsureLineSetUnchanged` → 編集ロック判定（訂正前）
2. 入力に無い既存行番号を含んでいたら（＝追加しようとした）例外
3. 既存行を`DepositMethodCode`／`BankAccountCode`／`LineRemarks`で上書き、入力に無い既存行は論理削除
   （全行削除になる場合は例外。「取消をご利用ください」と案内する）
4. **`receiptAmount = Σ AllocatedAmount`（生き残った行のみ）を生き残る全行へ書き直す**
   （伝票単位の全行同値不変条件を保つ。一部の行だけ論理削除すると崩れるため）
5. 編集ロック判定（訂正後）→ `TouchAll` → `SaveChangesAsync` →
   `SettlementService.RecalculateForBillingGroupAsync` → コミット

### 19-5. 締め解除済み`billings`への再充当は付け替えない

`receipts`の訂正で`receipt_allocations`を全面再構築する際、`GetOutstandingBillingsAsync`は
`BillingStatus.Confirmed`の`billings`のみを対象にする。したがって、訂正前に確定済み`billings`へ
充当されていた`receipts`が、その後`billings`が締め解除された状態で訂正されると、
再構築後の`receipt_allocations`はその`billings`を対象外にする（別の確定済み`billings`があれば
そちらへ、無ければ前受行へ回る）。`BillingReleaseService.ReleaseByBillingDateAsync`が「解除では既存の
`receipt_allocations`を付け替えない」と明言している既存方針（16章「既知の限界」）の自然な帰結であり、
新たな防止策は設けない。

### 19-6. ViewModelの構成

`ReceiptEntryViewModel`／`DetailReceiptEntryViewModel`は、`SalesEntryViewModel`のパターンに
合わせた構成とする。

- `IsExistingLoaded`（既存伝票を読み込んだか。得意先コードの読取専用化にのみ使う）と
  `IsEditLocked`（`EvaluateEditLockAsync`の結果）を分離し、`IsEditable`（プレビューモードでなく、
  かつ`!IsExistingLoaded || !IsEditLocked`。ロックされていなければ既存伝票も編集できる）と
  `IsHeaderLocked`（`= !IsEditable`。ヘッダー欄の入金日付・摘要の`IsReadOnly`に使う）を合成プロパティとして持つ。
- `LookupAsync`（伝票No.欄でのEnter読込）は、得意先を`customerService.GetByCodeAsync`で再取得
  して`_customer`へセットする（`CanSave`の前提）。
- 保存(F10)は`_loadedXxxNumber`（読込中の伝票番号）の有無で新規登録／訂正を分岐し、取消(F8)を
  `CancelSlipAsync`に配線する。`DetailReceiptEntryViewModel`は`CanAddNewTarget =>
  _loadedDetailReceiptNumber is null`により、F2（候補からの取込）を訂正モードでは常に不可にする
  （19-3節）。
- 得意先コード欄のみ`IsExistingLoaded`を使い、訂正モードでも得意先の付け替えは不可とする。

## 20. 消込キャッシュの整合性

消込キャッシュ列（`sales.settlement_status`／`sales.settled_amount`／`receipt.allocation_status`／
`detail_receipt.allocation_status`の4つ）は、入金明細の実集計と常に一致していなければならない。
この一致を成り立たせる構造上の根拠（20-1・20-2）、不変条件（20-4）、回帰検知の仕組み（20-3）、
未解決事項（20-5）を本章にまとめる。

### 20-1. キャッシュ列の書き手

上記4カラムへの代入は`SettlementService`（`ApplySettlement`／`ApplyReceiptAllocationStatus`／
`ApplyDetailReceiptAllocationStatus`）の1箇所に限られる。例外は新規追加行への一時的な
プレースホルダ2箇所のみである。

- `SalesService.UpdateAsync`の新規行追加ループ（`SettlementStatus.Unsettled`／`SettledAmount = 0m`）
- `ReceiptEntryService`／`DetailReceiptEntryService`の新規保存（`AllocationStatus.Unallocated`）

いずれも値を書いた**同一トランザクション内**で必ず`SettlementService.
RecalculateForBillingGroupAsync`が呼ばれる（各メソッドの末尾）ため、プレースホルダのまま
コミットされることはない。書き手が実質1箇所という構造が、不整合を作りにくくしている。

### 20-2. 再計算を呼ばない書き込み経路が値を狂わせない根拠

`RecalculateForBillingGroupAsync`を呼ばない書き込み経路のうち、消込キャッシュに影響しうるものは
次の3つである。いずれも呼ばなくても値は狂わない。

- **`BillingClosingService.ConfirmAsync`**（締め処理）: 対象の`sales`行へ新しい`billing_number`を
  割り当てる（`billing_number = NULL` → 新規`billing_number`）が、`RecalculateClosingAsync`は
  `billing_number`ごとにグループ化し、`billing_number IS NULL`の行は`pool`を常に`0`として扱う
  （グルーピングキーが`null`かどうかで分岐しており、`receipt_allocations`の内容を見ない）。
  新しく確定した`billing_number`は同一トランザクション内で今まさに作られたものであり、それを指す
  `receipt_allocations`が事前に存在することはあり得ない（`billings`へのFK制約上、存在しない
  `billing_number`を指す`receipt_allocations`は作れない）ため、締め前後で対象行の`pool`は`0`の
  まま変化しない。
- **`DetailInvoiceService.IssueAsync`**（明細請求書発行）: 対象の`sales`行を`detail_invoice_
  sales_line`で明細請求書に連携させる。`RecalculateDetailAsync`は連携済みの行を「直接指定分 +
  請求書経由の残額配分」で計算するが、発行直後の請求書には`detail_receipts`が存在し得ない
  （FK制約上、存在しない請求書番号を指す`detail_receipts`は作れない）ため`pool = 0`となり、
  請求書経由の配分は常に`0`。連携前（直接指定のみで計算）と連携後（直接指定 + 0）の結果は
  同じになる。
- **`DetailInvoiceService.CancelAsync`**（明細請求書取消）: 連携解除の前提として「連携先の
  売上明細行がすべて未消込（`SettlementStatus.Unsettled`）」を既存ガードで強制している
  （取消時のチェック、`docs/design_document.md` 12章）。連携解除の前後で対象行が
  「請求書経由（pool=0のため寄与0）」から「非連携（直接指定のみ）」に移るだけであり、
  ガードにより連携解除前の値が既に`0`であることが保証されているため、値は変化しない。

新しい書き込み経路を追加するときは、この3経路と同様に「呼ばなくても値が狂わない」根拠を
確認するか、`RecalculateForBillingGroupAsync`を呼ぶこと。

### 20-3. 再計算差分ゼロの回帰テスト

`SettlementService.RecalculateForBillingGroupAsync`は「値が実際に変わった行だけ」を更新する設計
（`docs/architecture.md` 9章）であるため、キャッシュ列が実態と一致していれば、どの得意先に対して
呼んでも更新件数は0件になる。`tests/bmcs_app.Application.Tests/Receipt/SettlementPhaseReviewTests.cs`
は開発用ライブDBの全得意先に対して再計算を呼び、更新件数0件を確認する恒久的な回帰テストである
（得意先ごとにトランザクションを開始・ロールバックし、データは変更しない）。新しい書き込み経路が
再計算の呼び出しを漏らした場合、このテストの実行時にいずれかの得意先に差分が出て検出できる。

### 20-4. 整合性の不変条件

20-3はプロダクションコードの計算ロジック（`SettlementStatusCalculator`／
`AllocationStatusCalculator`）自体に誤りがあった場合には検出できない。計算ロジックを経由しない
生SQLで次の不変条件を確認でき、いずれも該当0件でなければならない。

```sql
-- sales: settled_amount が amount を超えていないか
SELECT COUNT(*) FROM dbo.sales WHERE is_deleted = 0 AND ABS(settled_amount) > ABS(amount);
-- sales: settlement_status とカラム値の不整合
SELECT COUNT(*) FROM dbo.sales WHERE is_deleted = 0 AND settlement_status <> (
  CASE WHEN settled_amount = 0 OR amount = 0 THEN 1
       WHEN SIGN(settled_amount) = SIGN(amount) AND ABS(settled_amount) >= ABS(amount) THEN 3
       ELSE 2 END);
-- receipt / detail_receipt: allocation_status と伝票ごとの充当額合計から導いた状態の不一致（JOINで集計）
-- detail_receipt: receipt_amount が同一伝票の全行で同値であること
```

### 20-5. 申し送り

- 振込手数料差額の入力画面は未実装で、`fee_adjustment_amount`が非ゼロになる経路は現状のUIから
  到達不能である（入金入力・明細入金のいずれも常に`0`を書き込む）。したがって20-3・20-4が
  実データで確認できる範囲は「手数料差額なし」のパターンに限られる。手数料差額の入力を実装して
  非ゼロの実データが生まれた時点で、20-3・20-4を再確認すること。

---

## 21. 得意先元帳・現在残高

### 21-1. スコープ

サービス層（元帳データのマージ）＋ViewModel＋View（画面）までを範囲とする。リアルタイム残高の常時表示は21-6、伝票プレビューは21-7を参照。

スコープ外は印刷・プレビュー（ボタンは枠のみ用意し`IsEnabled="False"`。得意先元帳の帳票は
未実装。`docs/report-spec.md` 2-3節）。

### 21-2. 中心的な設計課題と決定事項

`tax_unit=1`（請求単位）の売上明細行は伝票時点で消費税額を持てない（CHECK制約
`CK_sales_tax_amount_by_tax_unit`）。一方入金額は税込である。そのため「売上`amount`の累計 −
入金の累計」では締め得意先の残高が税額分だけマイナスにずれる。この問題と、都度得意先の
消込証跡の表示要件を解決するため、以下を決定している。

- **残高は税込。消費税を独立した明細行として時系列に挿入する。** `tax_unit=1`は請求締め済み区間は
  `billing.tax_amount`（確定値）を`billing_date`の行、未締め区間は
  `ConsumptionTaxCalculator.CalculateExternalTaxBuckets`で仮計算した額を期末日付の行として挿入する。
  `tax_unit=2`は伝票ごとの`slip_tax_amount`をその伝票の直下に1回だけ挿入する。`tax_unit=3`は
  `amount`が既に税込なので消費税行を作らない。
- **入金の残高影響は常に入金日付の独立行で発生させる。** 都度得意先の売上行の右側には
  「入金日付・入金No」を消込の**証跡**として表示するが金額は載せない（`ReceiptAmount`をnullの
  ままにする）。売上と入金が月をまたいでも各月末の売掛残高が日付どおり正確になり、月次締めの
  暦月末残高の定義と矛盾しない。
- 1つの売上明細行に複数の入金が証跡として紐づく場合、2件目以降は売上側の列を空欄にした行として
  直下に続ける。
- 繰越は**全期間積み上げ**で算出する。`monthly_closings`／`billing.previous_balance`を起点に
  使わない（残高キャッシュ列を持たず都度集計する方針と整合させ、`billings`のスナップショットとの
  二重計上を避けるため）。
- 解除済みデータ（`billing.billing_status=2`）は残高計算の対象外（product-spec.md「解除済＝集計
  対象外」）。

### 21-3. アーキテクチャ

マージ・時系列化・残高推移・仮計算税・消込証跡はすべて Domain の純粋関数に置き、Application層は
クエリして渡すだけにする（`SalesEditLockEvaluator`と同じ方針）。残高推移が手計算と一致することを
DB不要の単体テストで直接検証でき、かつ月次締めが同じ関数を再利用できる。

- `src/bmcs_app.Domain/Calculations/CustomerLedgerBuilder.cs`: マージ本体。`Build(CustomerLedgerInput)`
  が`CustomerLedgerResult`（先頭が繰越行のエントリ一覧＋各種合計）を返す。仮計算消費税は
  `ProvisionalTaxAsOf(customer, salesLines, asOf)`という**日付の関数**として定義し、期間境界
  （`PeriodFrom`の前日／`PeriodTo`）で差分を取ることで、消費税の独立行挿入と全期間積み上げを
  両立させる（固定の「期末に1行」にすると、期間Fromより前の未請求売上の税が繰越に入らず、
  全期間積み上げと矛盾する）。
- `src/bmcs_app.Domain/Calculations/LedgerReceiptPairing.cs`: 都度得意先の消込証跡を作るための
  紐づけ。**表示専用であり残高計算には使わない。** 残高は`DetailReceipt.AllocatedAmount`
  を明細入金行ごとに独立行として計上する別ロジックが担うため、本クラスは金額の按分を一切行わない
  （`SettlementService`の消込配分アルゴリズムとは別物）。証跡に万一漏れがあっても残高は狂わない。
  直接指定（`target_type=1`）はエンティティが対象を直接持つため単純な参照、明細請求書指定
  （`target_type=2`）は連携する**すべて**の売上明細行に証跡として結びつける（どの明細行にいくら
  充当されたかの厳密な按分は行わない）。
- `src/bmcs_app.Application/Ledger/CustomerLedgerQueryService.cs`: `GetAsync(customerCode, from, to)`が
  得意先の全期間・未削除の売上・入金・（締め得意先のみ）確定済み請求を読み、`CustomerLedgerBuilder`に
  渡す。**`receipt_allocations`は一切参照しない**（21-4参照）。請求集約グループの分岐（請求集約先は
  グループ全体の`Sales`／`Receipts`を渡す、請求集約元は取引履歴のみモードでSales行のみを返す）は
  28-9節を参照。

### 21-4. 申し送り事項

既知の制約・責務分担。

- **元帳は`receipt_allocations`を参照しない。** `BillingReleaseService`は締め解除時に
  `sales.billing_number`をNULLに戻すが`receipt_allocations`は触らないため、解除済み`billings`を
  指す充当行が残留しうる。元帳は`receipt_allocations`を参照しない設計のため影響しない。
- **元帳が正、`billings`は締め時点のスナップショットである。** 元帳の残高と
  `billing.current_billing_amount`の一致は「遡及入力が無い」前提でのみ成立する。元帳は常に
  実データからの都度集計なので、この不一致を検出して例外を投げてはならない（遡及入力は正常な
  業務オペレーションで起こりうる）。なお請求締め済み・月次締め済みの期間への新規登録・日付変更は
  25章「ジャーナル系の日付制限」（請求締め分）・29章（月次締め分）で禁止している。
- **振込手数料差額が非ゼロになると、元帳の残高に残渣が残りうる（未解決）。** 手数料差額の入力画面が
  未実装で、現状は常に`0`を書き込む。`billing.receipt_amount`も`Σ receipt.Amount`（手数料を含まない）
  なので両者は一致し元帳固有の問題ではないが、実装時に「手数料差額を残高からどう落とすか」の
  業務判断が必要になる。
- **税計算の不整合は元帳側で検査しない。** 1伝票の明細行が請求済・未請求に分かれると
  `tax_unit=2`の`slip_tax_amount`（伝票単位の値）が実態とずれうるが、締め時に
  `BillingClosingService.CalculateTaxSummary`が検出して例外を投げる。元帳は照会画面であり、
  業務ルール違反の検出責務は請求締めが負う。
- **将来案（未実装）: 性能の逃げ道。** `GetAsync`に`historyFrom`のような引数を足し、それ以前を
  `monthly_closings`の確定残高で置き換える。残高キャッシュ列を持たない方針の下で、性能問題が
  出た場合に採る拡張である。

### 21-6. リアルタイム残高の常時表示

「常時表示」は、ウィンドウを開いたまま他画面での更新を自動検知して書き換えるリアクティブUI
（プッシュ通知・ポーリング等）ではない。`docs/architecture.md`にウィンドウ間でデータ変更を
通知し合う仕組み（イベント配信・SignalR・ポーリング等）は設計されておらず、各ウィンドウは独立した
DIスコープ・DbContextを持つだけである。求めるのは**「キャッシュ列に頼らず、表示するたび
（画面を開く・再検索する等）に必ず最新の実データから残高を計算する」という正確性の保証**
（残高キャッシュ列を持たず都度集計する方針の帰結）である。

得意先元帳画面（`CustomerLedgerViewModel`）は**検索期間とは独立した**「現在残高」
（本日時点の残高）を表示する。得意先確定時（`ApplyCustomerAsync`）と表示(F5)実行時
（`SearchCommand`）の両方で`CustomerLedgerQueryService.GetBalanceAsOfAsync`
（`GetAsync(code, asOf, asOf).ClosingBalance`と同値）を呼んで都度再計算する。期間From/Toを
過去の月に変更しても「現在残高」自体は連動しない（「前月繰越／今回」集計とは別の独立表示）。

### 21-7. 伝票プレビュー

**方針**: 専用のプレビュー画面は作らず、既存の売上入力（`SalesEntryWindow`）・入金入力
（`ReceiptEntryWindow`）・明細入金（`DetailReceiptEntryWindow`）を読み取り専用（プレビュー）
モードで開く。元帳の行を`Enter`／ダブルクリックで活性化する
（`RowActivationBehavior`、`CustomerLedgerViewModel.OpenSlipPreviewCommand`）と対応する画面が
非モーダル・毎回新規ウィンドウで開く（既存の`WindowService.Show`と同じ方式）。

**行の種別→開く画面の対応**（`OpenSlipPreview`）:

| 行の種別 | 開く画面 | 補足 |
|---|---|---|
| `Sales`かつ`SalesSlipNumber`あり | 売上入力 | |
| `Sales`かつ`SalesSlipNumber`なし（消込証跡の継続行。都度得意先のみ） | 明細入金 | 実体は`ReceiptSlipNumber`に入った`detail_receipts`番号 |
| `Receipt` | 得意先の`TaxUnit`が`Line`なら明細入金、それ以外は入金入力 | `ReceiptSlipNumber`は`receipts`/`detail_receipts`どちらの番号かを区別する情報を持たないため、税区分で分岐する（登録後不変） |
| `ConsumptionTax`かつ`SalesSlipNumber`あり（伝票単位） | 売上入力 | |
| `ConsumptionTax`（請求単位の確定額・未締め仮計算）／`OpeningBalance` | 開かない | 辿れる伝票が無い。ステータスバーに理由を表示するのみ |

**`WindowService.Show`の`configure`パラメータ**（`ShowDialog`と同じ位置づけ）は、
プレビュー対象の伝票No.（`PreviewSlipNumber`）をセットするだけの薄いコールバックとし、
実際の読込（DBアクセス）は行わない。理由: `Show`は`window.Show()`の**前**に`configure`を呼ぶが、
ウィンドウの初期化（各ViewModelの`LoadCommand`）はViewの`Loaded`イベントで**その後**に非同期発火する。
`configure`側で読込まで行うと実行順が保証されない。読込は各`LoadAsync`の末尾で
`PreviewSlipNumber`を見て行う。

**読み取り専用化は3画面それぞれに個別実装する**（共通基底クラスは作らない。過剰な抽象化を避ける
既定方針どおり）。入金入力・明細入金は既存の`IsExistingLoaded`/`IsEditLocked`/`IsEditable`/
`IsHeaderLocked`の枠組みに`!IsPreviewMode`を混ぜ、売上入力は`IsEditable`/`IsHeaderLocked`を
持つ。**編集ロック中（`IsEditLocked`）の既存UXはプレビューとは別の機能であり、変えない**
（編集ロック中でも入力欄自体は触れて保存だけ不可、という挙動）。

WPFの`KeyBinding`はコントロールが`IsEnabled=false`でも生き続けるため（`Space`/`Return`）、
`IsReadOnly`/`IsEnabled`のXAMLバインドだけでは`New`・検索モーダル・伝票読込・複写・保存・取消の
各コマンドを塞ぎきれない。**コマンドの`CanExecute`（`private bool CanEdit => !IsPreviewMode`を
合成）で塞ぐのが唯一の手段**であり、特に取消系コマンド（`DeleteSlipAsync`）は
`CanExecute`合成漏れがあると未消込・未締めの伝票をプレビュー中に本当に取消してしまうため、
`CanExecute`とコマンド本体先頭の`if (IsPreviewMode) return;`の二重で防御する。

明細行グリッドは`IsEnabled="{Binding IsEditable}"`で`ItemsControl`ごと無効化する（行単位の
読み取り専用フラグでは、区分コンボ・×削除ボタン・商品検索の`InputBindings`をまとめて
無効化できないため）。**トレードオフとして明細行全体が灰色になり、プレビューの主目的である
「内容を読む」体験としては見やすさを犠牲にしている**が、「編集できない」ことを
機械的に保証することを優先する。`ScrollViewer`自体を無効化するとスクロールできなくなるため、
入金入力・明細入金の`IsEnabled`バインドは内側の`ItemsControl`に付ける（編集ロック中の既存伝票でも
スクロールできる）。

**Escで閉じられる**（保存・取消以外に退出手段が無いため。3画面それぞれの
`.xaml.cs`の`PreviewKeyDown`で`IsPreviewMode`のときだけ`Close()`する。通常の編集セッションは
未保存の入力をEscで誤って破棄しないよう対象外）。

**既知の制約**:
- プレビューを開いた後に元伝票が削除・取消されていた場合、空欄・無効化・タイトル「プレビュー」の
  ウィンドウが残る。プレビューは新しいDIスコープ・DbContextで開くため常に最新DBを見に行く設計
  であり、元帳表示時点のスナップショットと食い違うことは正常な業務オペレーションとして許容する。
- `WindowService.Show`は`Owner`を設定しない（`ShowDialog`と異なる）ため、同じ行を複数回活性化すると
  同じ伝票のプレビューが複数枚開く。非モーダル・毎回新規ウィンドウという方針の範囲内として許容する。
- プレビュー中の伝票No.欄等は`IsReadOnly`のみで`KeyBinding`自体は残るため、`Enter`を押しても
  次項目へフォーカス移動しない（`EnterKeyNavigationBehavior`は独自の`KeyBinding`を持つ要素を
  スキップするため）。`Tab`は機能する。
- `CustomerLedgerBuilder`が生成する`LedgerEntryKind`は`OpeningBalance`/`Sales`/`ConsumptionTax`/
  `Receipt`の4種のみで受注（`orders`）の行は無いため、`OrderEntryWindow`はプレビューモードを
  持たない。

---

## 22. 帳票基盤・納品書の実装

### 22-1. スコープ

帳票基盤（全帳票が同じ仕組みで出力できること）と、その最初の実帳票である納品書を対象とする。
帳票エンジンの選定方針は`docs/decisions.md`、詳細な実装構成・レイアウト決定は
`docs/report-spec.md` 2章、層配置は`docs/architecture.md` 5章補足・4章補足を参照。

### 22-2. 実装上の要点

帳票基盤・納品書の実装上の要点（保存直後の印刷導線＝`New()`前の確認ダイアログ、
`MarkIssuedAsync`の追跡エンティティ`ReloadAsync`、`DocumentViewer`の`ControlTemplate`と
自前ボタン、A4寸法と`PrintTicket`、`Foreground = Brushes.Black`の明示、Application層の
フォルダ配置）は`docs/report-spec.md` 2-0・2-1節と`docs/decisions.md`に判断理由とともに記載している。

### 22-3. 実装構成

`docs/report-spec.md` 2章（`ReportPagination`・`DeliveryNoteService`・`ReportDocumentBuilder`・
`ReportPrintService`・`ReportPreviewDialog`等）を参照。税単位別の税額計算・フッター表示・適格請求書として
扱わない判断も`docs/report-spec.md` 2-1節に記載している。

### 22-5. 申し送り

得意先元帳の帳票（毎ページ繰越フッター、`ReportKind`・`PrinterSettingsConfig`の追加）は
未実装で、要件は`docs/report-spec.md` 2-3節、判断は`docs/decisions.md`に記載している。

---

## 23. 請求書・明細請求書の実装

### 23-1. スコープ

締め得意先向け「請求書」・都度得意先向け「明細請求書」の印刷を実装している。`ReportKind.Invoice`／
`ReportKind.DetailInvoice`は帳票基盤（`PrinterSettings`の4項目に対応）に用意されている。

適格請求書の記載事項（発行者の名称・登録番号／取引年月日／取引内容と軽減税率の付記／
税率ごとに区分した対価の額と適用税率／税率ごとの消費税額／交付を受ける者の名称）は
すべて満たす。レイアウト詳細・帳票基盤の共通化内容は`docs/report-spec.md` 2-2節を参照
（重複させないためここには書かない）。本節以降は業務ロジック上の設計判断を記録する。

### 23-2. 設計判断: 税率別内訳の「適用税率」表示

`billings`／`detail_invoices`は税種別区分ごとの確定金額（標準税率・軽減税率・非課税の
固定5カラム）のみを保持し、税率(%)そのものは持たない（`docs/database-schema.md` 2.12・
2.13節）。適格請求書は税率ごとの対価の額に加えて「適用税率」自体も法定記載事項であるため、
このままでは印字できない。

一方、明細を構成する`sales`行は税単位によらず必ず`tax_rate`をスナップショットとして持つ
（2.9節）。そこで`ConsumptionTaxCalculator.ResolveConfirmedBuckets`（Domain、純粋関数）が、
**金額はヘッダーの確定値をそのまま使い、税率(%)ラベルだけを該当する税種別区分を持つ
明細行から拝借する**方式で内訳（`TaxRateBucket`）を組み立てる。

この方式の理由: 伝票単位（`tax_unit=2`）は伝票ごとに端数処理する構造
（`CalculateExternalTaxPerSlip`）であり、請求期間全体の明細行を単純に再グループ化して
税率ごとに1回だけ丸め直すと、保存済みのヘッダー確定値と金額が食い違う（二重丸め）
おそれがある。金額を常に確定値に固定し、税率ラベルの解決だけを明細行から行うことで、
どの税単位でも金額の不一致を起こさずに適用税率を表示できる（`docs/decisions.md`
「税・端数」にも同旨）。

対価額・税額がともに0の区分は表示しない（`CalculateExternalTaxBuckets`と同じ「0円の
区分は載せない」扱い）。明細行に該当区分が無い場合（下記23-3の締め解除済み・取消済み）は
税率0でフォールバックする。

### 23-3. 締め解除済み・取消済みを指定した場合の挙動

締め解除（`BillingReleaseService`）は`sales.billing_number`を`NULL`に戻し、明細請求書の
取消（`DetailInvoiceService.CancelAsync`）は`detail_invoice_sales_lines`を物理削除する
（12-1節）。どちらも既存の非破壊ヘッダー方式のため、解除済み・取消済みの番号を指定して
印刷すると**明細0件・ヘッダーの確定金額のみ**が返る。これは12章で明細請求書の取消について
文書化・許容されている挙動と同じであり、請求書側も同様に扱う。印刷自体は禁止しない
（過去の参照用の印刷を禁止する理由がないため）。

明細請求書のみ、取消済み（`InvoiceStatus = Cancelled`）は画面の印刷ボタンを無効化する
（F8の`CanCancel`が発行済みのみ許可するのと対称。取消済みは明細0件になり印刷の実用性が
低いため）。締め得意先の請求書は`InvoiceService.GetByNumberAsync`自体は確定・解除済みの
どちらでも取得できるが、請求締め処理画面の一覧（9-7節）は確定済みしか
表示しないため、その画面からは解除済みを選んで印刷することはできない。

### 23-4. 請求書の印刷導線: 請求締め処理画面

請求書の印刷導線は、専用の発行画面を新設せず、請求締め処理画面（`BillingClosingWindow`）の
結果一覧から選択行を印刷する方式とする。締め確定後も画面を起動直後の状態へリセットせず、
入力条件・結果一覧をそのまま残す（確定した瞬間に印刷対象の請求番号が一覧から消えると
印刷導線と両立しないため）。

`BillingClosingViewModel.ConfirmAsync`は確定後に`Results.Clear()`／
`ResetConditionsToDefault()`／`RefreshPreviewAsync()`を呼ばない。次のバッチ（別の締め日区分・
請求日）に進みたい場合は入力条件を変更すればよく、既存の自動再取得
（`OnSelectedClosingDayChanged`／`OnClosingDateChanged`）がその時点で一覧を置き換える。
一覧の抽出条件・選択方式・印刷コマンド（`PrintCommand`、F11・行のダブルクリック／Enter）の
現行仕様は9-7節を参照。

### 23-5. 実装構成

- **Domain**: `ConsumptionTaxCalculator.ResolveConfirmedBuckets`（純粋関数）。
- **Application** (`Billing/`): `InvoiceData`／`InvoiceLine`、`InvoiceService`
  （`GetByNumberAsync`）、`DetailInvoiceData`／`DetailInvoiceLine`、
  `DetailInvoiceService.GetPrintDataAsync`。`DetailInvoiceSalesLineItem`は印刷に必要な
  `Specification`／`UnitName`を持つ。
- **Presentation** (`Reports/`): `InvoiceDocumentBuilder`／`DetailInvoiceDocumentBuilder`。
  `ReportDocumentBuilder`（基底）が`BuildCustomerBlock`／`BuildCompanyInfoBox`
  （代表者印字対応）／`BuildBillingBankAccountsBox`（23-7節参照）／`BuildBreakdownRow`／
  `BuildLabelValue`／`BuildTotalRow`を提供し、`DeliveryNoteDocumentBuilder`も同じ
  ものを使う。
- **配線**: `DetailInvoiceIssueViewModel.PrintCommand`（発行済み読込時のみ有効）＋発行直後の
  確認ダイアログ。`BillingClosingViewModel.PrintCommand`（一覧で選択行がある場合のみ有効）。

### 23-7. 申し送り

- 得意先元帳の帳票要件（毎ページ繰越フッター）と専用のプリンタ設定
  （`ReportKind`・`PrinterSettingsConfig`）は`docs/report-spec.md` 2-3節を参照。
  `ReportKind`に得意先元帳用の値は現在存在しない。
- 納品書を適格請求書として扱うかは税理士確認待ち（`docs/design_document.md` 2章の
  【要確認】は閉じていない）。
- 請求書・明細請求書の振込先は得意先ごとに使い分ける。`bank_accounts`は自社の振込先口座
  マスタであり、得意先マスタ`bank_account_code1`／`bank_account_code2`（最大2件）から
  紐づけて請求書・明細請求書へ印字する。全社共通で印字対象を選ぶ`is_print_on_invoice`
  フラグは存在しない。帳票側は発行者情報ボックスの直下に固定行数（見出し1行＋2行。1口座1行・
  口座名義は印字しない）の枠を置き、紐づけ0〜1件でも高さがずれないようにする。改ページ用の
  高さ見積り（`FullHeaderHeight`）の詳細は`docs/report-spec.md` 2-2節を参照。

## 24. 受注の訂正

### 24-1. 受注の修正可否

受注入力画面で既存受注を呼び出して修正できるのは、**未売上（`order_status`＝未売上）の伝票のみ**。
一部売上・売上完了・中止済みは読込・表示のみで修正できない（`docs/database-schema.md` 1章・
`docs/product-spec.md`共通業務ルール5と同じ）。`orders`は編集ロック4条件のいずれにも
該当しない（請求・消込の対象外）が、この修正可否だけを別途持つ。

### 24-2. 編集可否の判定: `OrderEditLockEvaluator`

`SalesEditLockEvaluator`と同形のDomain純粋関数だが、Application層のラッパー
（`SalesEditLockService`相当）は作らない。売上がラッパーを持つのは`monthly_closings`・
`detail_invoice_sales_lines`のDB照会をDomainに持ち込まないためで、受注の判定条件は
明細行自身の`order_status`のみで済み外部照会が不要なため、転送しかしないクラスを
置く理由がない。

判定順は中止済み→売上完了→一部売上で、状態ごとに異なる文言を返す
（`OrderStatusService.CancelSlipAsync`の文言分けに合わせている）。`OrderService.UpdateAsync`と
`OrderEntryViewModel`の両方がこの関数を直接呼ぶ（ViewModelがDomainの純粋関数を直接呼ぶのは
既存パターン。`ConsumptionTaxCalculator`と同様、判定式の正が1箇所にあれば
`docs/architecture.md` 5章「ViewModelは業務ルールを判断しない」の趣旨を満たす）。

### 24-3. `OrderService.UpdateAsync` — 売上側との差異

`SalesService.UpdateAsync`と同じ構成（追跡クエリで現行行を再取得→`SlipConcurrencyGuard`で
排他検出→編集ロック判定→既存行はホワイトリストで上書き・一覧に無い行は論理削除・新規行は
`Max(line_number)+1`で採番→保存）だが、次の点が異なる。

- **明示トランザクションを開始しない。** 採番せず、他サービス（受注デルタ適用・税額確定・
  消込再計算に相当するもの）も呼ばず、`SaveChangesAsync`は1回のみのため、
  `docs/architecture.md` 6章「1ユースケース＝1回のSaveChangesAsync。明示的なトランザクションは
  不要」の原則どおり暗黙トランザクションで足りる（前例: `OrderStatusService.CancelSlipAsync`）。
- **訂正後の再判定をしない。** 売上が訂正後に編集ロックを再判定するのは伝票日付が月次締め
  年月を跨ぐ条件があるため。受注のロック条件は`order_status`のみに依存し、`UpdateAsync`は
  `order_status`を書き換えず（訂正できるのは未売上限定なので常に`NotSold`のまま）、
  新規行も`NotSold`固定なので、訂正後の状態は定義上ロック対象になり得ない。
- **税額確定（`SalesTaxAmountAssigner`相当）・消込再計算（`SettlementService`相当）を行わない。**
  `orders`は税額列を持たず、消込の概念もないため。
- **全行削除を拒否する。** 売上の`UpdateAsync`は全行削除を許容し（結果は`CancelSlipAsync`と
  同じ状態になる）、受注では成立しない。受注の中止は`is_deleted`ではなく
  `order_status = Cancelled`で表すため（`OrderStatusService.CancelSlipAsync`）、訂正で全行を
  `IsDeleted = true`にすると`OrderQueryService`のどの照会（`!IsDeleted`で絞る）からも見えなく
  なり、`CancelSlipAsync`も0件ヒットで「受注が見つかりません」を投げるため中止すらできない
  伝票になる。`OrderOperationException("訂正で全行を削除することはできません。
  中止（F8）をご利用ください。")`で拒否する。
- **`OrderQuantity >= SalesConfirmedQuantity`の検証を書かない。** 修正可能なのは未売上限定
  （`SalesConfirmedQuantity`は常に0）のため論理的に到達不能な条件になる。

**ホワイトリスト15列**: `OrderDate, CustomerName, ProductCode, ProductName, Specification,
UnitName, OrderQuantity, UnitPrice, Amount, CostPrice, TaxCategory, TaxRate, SlipRemarks,
LineRemarks, InternalRemarks`。除外: `CustomerCode`（得意先は変更不可）、`SubCustomerId`（UIが値を持たず常に
null。将来値を持つようになったときに静かに消えるのを防ぐ）、`AllocatedQuantity`（在庫連携スコープ外）、
`OrderStatus`／`SalesConfirmedQuantity`（所有者は`OrderStatusService`のみ）、監査列、
`RowVersion`。既存行のコピー時に`IsDeleted = false`を明示設定する（下記24-5）。

排他例外は既存の`SlipConcurrencyGuard`が投げる`SlipConcurrencyException`をそのまま使う。
`OrderConcurrencyException`は`OrderStatusService.CancelSlipAsync`専用で、両者は
DbUpdateConcurrencyExceptionの変換先として並存する（`OrderEntryViewModel`は保存で前者、中止で後者を捕捉する）。

### 24-4. 画面側: 得意先コードのReadOnly化（売上側とは対応が異なる）

`OrderService.UpdateAsync`は`CustomerCode`の変更を無視する。受注の画面では
**訂正モード中は得意先コード欄を`IsReadOnly`にし、得意先検索・コード照会の
2コマンドも`CanExecute`で無効化する**（欄をReadOnlyにするだけではSpace/Returnキーバインド
経由で得意先が差し替わってしまうため両方必要）。得意先**名**欄は編集可のまま
（宛名の都度書き換え。ホワイトリスト対象）。

**既知の差異**: 売上入力画面は訂正モードでも得意先コード欄が編集可能で、変更しても黙って
無視される潜在的な不整合が残っており、この対応は受注のみで売上側は揃えていない。

修正不可（一部売上・売上完了・中止済み）の受注を読み込んだ場合は、ヘッダー入力欄
（受注日付・得意先名・摘要）と明細`ItemsControl`・行追加ボタンを`IsEnabled="{Binding
IsEditable}"`（`IsEditable => !IsEditLocked`）で読取専用にする。受注No.欄自体は常に
編集可のままにし（別の受注を検索・読込できるようにするため）、保存ボタンは
`CanSave`（`_loadedOrderSlipNumber is null ? !IsSaved : !IsEditLocked && !IsReloadRequired`）
で無効化する。

### 24-5. 保存失敗後のChangeTracker汚染対策（受注・売上共通）

`BmcsDbContext`はウィンドウ単位スコープ（`App.xaml.cs`）で画面を開いている間生き続けるため、
`UpdateAsync`がミューテーション後に例外を投げると、ChangeTrackerが汚れたまま残る。同じ画面
から再保存すると、EFのアイデンティティ解決で以前の`IsDeleted = true`のインスタンスが
返り黙って論理削除される、あるいは前回`Add`した新規行が残っていて同一キーの
`InvalidOperationException`になる。

対策は次の2点（`docs/decisions.md`にも記載）。①`ApplyLineValues`（受注・`SalesService`の両方）で
既存行コピー時に`IsDeleted = false`を明示設定する。②ViewModel側（`OrderEntryViewModel`・
`SalesEntryViewModel`の両方）で、訂正モード中の保存が例外で失敗したら`IsReloadRequired`を
立てて再読込まで保存を封じる。`docs/architecture.md` 9章「自動マージや
後勝ちでの上書きは行わない」をメッセージ表示だけでなく操作面でも守る形。

### 24-8. 受注入力画面の検索では売上化できない受注も表示する

`OrderQueryService.SearchAsync`は既定（`excludeUnavailableForSales = true`）で「全行が中止または
売上完了」の受注を検索結果から除外する。これは売上入力画面の受注No.検索（これ以上
売上化できない受注を候補に出す意味がない）のための挙動である。受注入力画面のSPACE検索
（`OrderEntryViewModel.OpenOrderSlipSearch`）も同じ`SlipSearchDialogViewModel`を共有するが、
**受注入力画面では閲覧目的で売上完了・中止済みの受注も探せるようにする**
（直接番号入力ではもともと読込できる）。

`SlipSearchDialogViewModel.IncludeUnavailableOrders`（既定`false`）が
`OrderQueryService.SearchAsync`の`excludeUnavailableForSales`引数へ`!IncludeUnavailableOrders`として
渡される。`OrderEntryViewModel`の検索（`OpenOrderSlipSearch`）と過去伝票複写の検索のみ`true`を設定し、
`SalesEntryViewModel.OpenOrderSlipSearch`（受注からの売上確定用）は既定の`false`のまま
（売上化できない受注を候補に出さない）。

## 25. ジャーナル系の日付制限

### 25-1. 目的

請求集計（請求締め）された日を含め、集計前の日付の新規売上・入金は登録できない
（例: 9/30締めなら10/01以降のみ）。編集ロック（既存行の編集・訂正の禁止）ではこの新規登録を
防げないため、新規登録・日付変更の入口を本章で禁止する。

### 25-2. 対象範囲

- **対象画面は売上入力・入金入力の2画面のみ。** 明細入金（都度得意先専用）・受注入力は対象外。
- **都度得意先（`tax_unit=3`）は制限しない。** `billings`を1件も持たないため、税単位で分岐せずとも
  自動的に無制限になる（後述）。
- **月次締め（`monthly_closings`）は本章の対象外。** 月次締めは得意先×暦月の任意集合であり許可日が
  連続にならないため、`DatePicker.DisplayDateStart`（単一の最小日付）という設計と相性が悪い。
  保存時のApplication層検証として別途実装している（29章）。

### 25-3. 判定ルール: `BillingClosedDateEvaluator`

`src/bmcs_app.Domain/Calculations/BillingClosedDateEvaluator.cs`（Domain純粋関数、
`SalesEditLockEvaluator`と同形）。

```
最小日付 = (対象得意先の確定済みbillingのうち最大のbilling_date) + 1日
確定済みbillingが無ければ制限なし（null）
```

`billing_status = Confirmed`のみを対象にする（`Released`＝締め解除済みは対象外。締め解除すれば
その期間に再度登録できる、という既存の締め解除運用とそのまま整合する）。

DBアクセスは`src/bmcs_app.Application/Billing/BillingClosedDateService.cs`が担う。クエリは
`ReceiptEntryService.EvaluateEditLockAsync`が使うものと同一（対象得意先の確定済み`billings`のうち
最新の`billing_date`）だが、責務（既存行の編集ロック／新規登録・日付変更の入口）が異なるため
判定結果は共有せず、別クラスにしている（それぞれの文言・利用箇所が独立に変わってよいようにする
ため。境界を問い合わせるクエリ自体の重複は許容する）。

### 25-4. 強制ポイント

| 対象 | 検証する日付 |
|---|---|
| `SalesService.CreateAsync` | 新規登録の伝票日付 |
| `SalesService.UpdateAsync` | 訂正後の伝票日付（過去へ動かす経路も塞ぐ） |
| `ReceiptEntryService.SaveNewAsync` | 新規登録の入金日付 |
| `ReceiptEntryService.UpdateAsync` | 訂正後の入金日付 |

いずれも既存の編集ロック判定（`lockResultBefore`／`lockResultAfter`）の直後に差し込み、
違反時は各サービスの既存例外型（`SalesOperationException`／`ReceiptEntryException`）をそのまま使う。
取消（`CancelSlipAsync`）は日付を変えないため対象外（既存の編集ロックが担当）。

### 25-5. 画面側: `DisplayDateStart`は利便性のみ、最終的な検証はApplication層

`SalesEntryViewModel.MinimumSlipDate`／`ReceiptEntryViewModel.MinimumReceiptDate`を
`DatePicker.DisplayDateStart`にバインドし、カレンダーから締め済み日以前を選べなくする。ただし
`DisplayDateStart`はカレンダーのマウス選択のみを制限し、`DatePickerInputBehavior`（全画面デザイン
統一）が8桁ベタ打ち等の入力で`SelectedDate`を直接代入する経路を迂回できるため、**画面側の
制御は利便性のみであり、最終的な検証は25-4のApplication層が担う**（`BlackoutDates`は範囲外日付の
`SelectedDate`代入で例外を投げるため採用しない。訂正モードの読込・複写がクラッシュしうる）。

得意先確定時（`ApplyCustomerAsync`）に最小日付を取得し、現在の伝票日付がそれより前なら**自動で
最小日付へ補正し`StatusMessage`で通知する**。既存伝票の読込（訂正モード）
では最小日付は表示用に設定するのみで、読込んだ日付は書き換えない。保存時にも同じ最小日付との
比較を行い、二重防御とする。

---

## 26. 受注入力の過去伝票複写

売上入力画面の複写（8-3章）と同様の機能を受注入力画面にも持つ。動作は売上と同様とし、`SalesEntryViewModel.CopyFromPastSlipCommand`をそのまま`OrderEntryViewModel.CopyFromPastSlipCommand`へ移植している。

- ツールバーに「複写」ボタンを置く（`OrderEntryWindow.xaml`）→ 伝票検索モーダル（`Target=Order`）
  → `OrderQueryService.GetSlipAsync`で明細取得 → 新規登録として明細行へ展開。
- **複写するもの**: 得意先、明細行（商品・数量・単価・原価・税区分・行摘要）、伝票摘要、社内摘要。
- **複写しないもの**: 受注No.（新規採番）、受注日付（当日）、受注状態・引当数量・売上確定数量
  （すべて新規登録の初期値。`BuildEntity`が常にゼロ初期化するため複写ロジック側では何もしない）。
- 税率は複写後の受注日付（当日）で再解決する（単価は複写元の値を維持）。
- 受注には伝票区分（`SlipType`）の概念がないため、売上側複写にあるこの項目のコピーは対象外。
- 複写元の検索は`IncludeUnavailableOrders = true`とし、売上完了・中止済みの受注も選べる。
  24-8章の「受注入力画面の検索では売上化できない受注も表示する」方針と、売上側の複写が
  複写元の状態を絞り込んでいないことの両方に整合させるため。
- コマンドに`CanExecute`ガードは付けない。売上側は`IsPreviewMode`（読取専用プレビュー）中の
  複写を禁止しているが、受注入力画面にはプレビューモードという概念自体が存在しないため、
  `NewCommand`等と同じ無条件コマンドとしている。訂正モード中に押すと`New()`により新規状態へ
  リセットされる（売上側で編集中に複写した場合と同じ挙動）。

---

## 27. 入金方法マスタ

入金方法（現金・振込・手形・相殺等）は`deposit_methods`マスタで管理し、`ReceiptMethod` enum（tinyint固定値）は使わない。利用者はコード修正・再ビルドなしに入金方法を追加・改称できる。`product-spec.md`共通業務ルール4「振込手数料差額は手入力のみ、自動計算・自動補正提案は行わない」の方針は入金方法マスタの有無に関わらず変わらない。

### 27-1. 設計判断

- **付随項目の必須制約はマスタのフラグで表す（`requires_bank_account`／`requires_bill_due_date`）。**
  「振込＝口座必須・期日NULL／手形＝期日必須・口座NULL／それ以外＝両方NULL」という対応関係のうち、
  「口座と期日が同時に埋まらない」という単一テーブルで表現できる部分だけをCHECK制約
  （`CK_receipts_bank_account_bill_due_date_exclusive`）とし、「入金方法によってどちらが
  必須か」という他テーブル参照が要る部分はアプリ層（`ReceiptEntryService.ValidateLinesAsync`／
  `DetailReceiptEntryService.ValidateLineFieldsAsync`）のみで担保する。
  トリガーは作らない。このリポジトリにトリガー・ユーザー定義SPは1つも無く、`rowversion`楽観的
  排他制御・`SlipConcurrencyGuard.TouchAll`との相性が悪いため（docs/architecture.md 9章）。
- **`detail_receipts`の手形除外**: `requires_bill_due_date=1`の入金方法を選択肢から除外するだけで足り、専用フラグは持たない（18章参照）。
- **列名は`deposit_method_code`（varchar(10)、FK）。** FK列の命名規約（`bank_account_code`等と同じ
  `<マスタ名>_code`）に揃える。
- **入金方法名のスナップショットは持たない。** `CustomerLedgerEntry.DepositMethodName`は
  `CustomerLedgerQueryService`が都度`deposit_methods`（`IsDeleted`で絞らない。過去の入金が
  無効化済みの入金方法を参照していても名称解決できるようにするため）を読み、
  `CustomerLedgerBuilder`がコード→名称の辞書で解決する。帳票に入金方法名を印字する要件が無く、
  `customer_name`スナップショットのような「発行当時の名称を固定する」要件も現時点で無いため。

### 27-3. 影響範囲

- 入金方法マスタ画面は`BankAccount`マスタ一式と同じパターン（コード直接入力＋
  Space検索モーダル＋Enter読込）で、メインメニュー「マスタ管理」配下に登録する
  （`scripts/014_seed_menu_structure.sql`の`MNU_DEPOSIT_METHOD`）。
- 入金入力画面・明細入金画面のComboBoxはマスタ全件をロードする（`BankAccounts`
  と同じ`ObservableCollection`供給パターン）。行ViewModelは`SelectedValue`ではなく
  `SelectedItem`でマスタ行のエンティティ自体を保持し、`RequiresBankAccount`／`RequiresBillDueDate`
  を直接読んで付随欄の表示制御を行う。

---

## 28. 親子請求（請求集約）の設計

### 28-1. 背景・スコープ

各地に支店を持つ会社で、各支店の売上を本社に一括請求したいという業務要件（本支店間以外の取引先でも同じ運用を採る可能性あり）。得意先マスタの `billing_customer_code`（請求得意先コード）が得意先コードと一致すれば従来どおり単独で請求し、異なればその得意先の売上は指し先の得意先の請求書にまとめて計上する。

用語は**「請求集約先」（他の得意先の分もまとめて請求される得意先）／「請求集約元」（請求が他の得意先に集約される得意先）**とし、「親得意先」「子得意先」は使わない。既存の `sub_customer_id`（学校のクラス・先生等の宛名を都度書き換える仕組み。C-9で「子得意先マスタは作らない」と確定済み）とは別概念であり、C-9の決定を覆すものではない（`docs/database-schema.md` 1章・`docs/product-spec.md` 共通業務ルール3参照）。

機能全体はDB・得意先マスタ・請求締め・締め解除・消込・入金入力・得意先元帳・請求書帳票の8領域にまたがる。

### 28-2. 確定した業務ルール

| # | 決定 |
|---|---|
| 1 | 対象は締め得意先のみ（`tax_unit`=1 請求単位／2 伝票単位）。都度得意先（`tax_unit`=3）は請求集約元になれない |
| 2 | 請求集約先と請求集約元は `closing_day`・`tax_unit`・`rounding_type`がすべて一致していなければならない |
| 3 | 請求データ（`billings`）は請求集約先にだけ作る。請求集約元の売上は請求集約先の`billing`に取り込み、`sales.billing_number`に請求集約先の請求番号を書く。請求集約元には`billings`を一切作らない |
| 4 | 入金は請求集約先にだけ入る。入金入力画面で請求集約元を指定したら業務例外で拒否する |
| 5 | 売掛残高の管理は請求集約先に集約する（請求集約元では残高を管理しない） |
| 6 | 得意先元帳: 請求集約先は配下の請求集約元の売上も含めて表示する（残高を正しくするため必須）。請求集約元は取引履歴（売上）のみを表示し、残高・繰越は表示せず「請求・残高は請求集約先◯◯に集約」と案内する |
| 7 | 請求得意先コードは、確定済み`billings`に取り込まれた売上が1件でもある得意先は変更不可。それ以外は変更できる（`closing_day`/`tax_unit`/`rounding_type`と異なり、新規登録時限定ではない） |
| 8 | 請求集約元の請求集約元は不可（階層は2段まで） |
| 9 | 月次締めは請求集約元ごとに個別集計する（`docs/database-schema.md` 2.16節、29章） |

DB制約の具体的な実現方式（計算列を使った複合自己参照FK）は `docs/database-schema.md` 1-1節・2.1節を参照。

### 28-3. 8領域への影響方針（一覧）

**中心的な設計判断**: スコープキーを「得意先」から「請求集約グループ」（請求集約先＋全請求集約元）に広げる。請求・入金・消込・残高に関わる処理だけがグループ単位になり、**売上・受注・納品書・商品単価履歴など伝票そのものを扱う処理は`customer_code`単位のまま変えない**。

| 領域 | 方針 |
|---|---|
| DB | `customers.billing_customer_code` ＋ 計算列2本 ＋ 複合自己参照FK ＋ CHECK制約で「集約先の存在」「孫の禁止」「締日/税区分/端数区分の一致」「都度得意先は集約元不可」をすべてDB側で強制 |
| 得意先マスタ | 請求得意先コード欄、リンク検証、業務ルール7の変更可否判定、集約元を持つ得意先の無効化禁止 |
| 日付制限 | `BillingClosedDateService`は請求集約先を解決してから`billings`を引く。請求集約元には`billings`が無く、自身の`billings`しか見ないと確定済み請求期間への遡及登録を防げず、次回締めに取りこぼしが混入する実害があるため |
| 消込 | `SettlementService.RecalculateForBillingGroupAsync`の対象クエリ（`sales`/`receipts`/`receipt_allocations`）をグループの得意先コード集合に広げる。配分ロジック自体（`billing_number`でのグルーピング）は無改修で請求集約元の売上を自然に合算する |
| 入金入力 | `ReceiptEntryService.GetClosingCustomerAsync`で請求集約元を業務例外で拒否 |
| 請求締め | `BillingClosingService`の締め対象ループを請求集約先のみに絞り、売上・入金の抽出をグループ集合に広げる |
| 締め解除 | `BillingReleaseService`の再計算呼び出しは消込のグループスコープに合わせる（改修不要、28-7節） |
| 得意先元帳 | `CustomerLedgerBuilder`に「取引履歴のみ」モードを持つ。請求集約先では未締め区間の仮計算税もグループ売上＋請求集約先の端数区分で計算し、締め処理の結果と一致し続けるようにする |
| 請求書帳票 | `InvoiceDocumentBuilder`で明細を「見出し行・明細行・小計行」に平坦化。**集約していない請求書は明細のみとし、既存の単独得意先の帳票をバイト単位で不変に保つ** |

**修正不要と確認済み（根拠）**:
- `SalesEditLockEvaluator`: 売上行自身の`billing_number`を見る設計のため、請求集約元の売上に請求集約先の請求番号が書かれれば無改修でロックが効く
- `DeliveryNoteService`（納品書）: 実際に納品した請求集約元宛に出すのが正しい業務であり、`customer_code`スコープのままでよい
- 受注（`OrderService`等）: `billings`・`receipts`と無関係
- 明細請求書・明細入金（`tax_unit`=3専用）: CHECK制約により都度得意先は請求集約元になれないため無関係
- 商品単価履歴（`ProductHistoryQueryService`）: 請求集約元ごとの購買実績を見るのが正しいため`customer_code`スコープのまま
- 月次締め: 28-2節#9のとおり請求集約元ごとに個別集計する

**既知の制約（`receipts`）**: `CustomerService.HasBillingChangeLockAsync`は請求済み売上・確定済み`billings`・有効な請求集約元の3点しか見ず、`receipts`の有無を見ない。そのため過去に単独で入金（前受金・締め解除後の入金残）を受けた得意先を後から請求集約元へ変更できる。この入金は、消込（`SettlementService`）・請求締め（`BillingClosingService`）・得意先元帳（`CustomerLedgerQueryService`）がいずれもグループ全体の`receipts`を対象にすることで、請求額・消込・残高から漏れない（28-6・28-7・28-9節）。`HasBillingChangeLockAsync`に`receipts`チェックを足す案は見送っている（新規に請求集約元へ変更する入り口対策として将来検討の余地はある）。

### 28-5. DB・得意先マスタ

DBスキーマ（`scripts/020_add_billing_customer_code.sql`）・エンティティ・EF構成の詳細は `docs/database-schema.md` 1-1節・2.1節を参照。

**得意先マスタ画面**: 請求得意先コード欄は既存のコード欄と同じ入力作法（`Space`で検索モーダル、`Return`でコード照会）。入力可否は`closing_day`等（新規登録時のみ）と異なり、業務ルール7に基づき「新規登録時は常に入力可・更新時は`CustomerService`の判定結果に従う」という二段構えで制御する。締めたことのない得意先を後から請求集約元に変更すると未請求売上がすべて次回の請求集約先の請求に合算されるため（締め処理の売上抽出に期間下限が無い）、画面側で変更前に確認ダイアログを出す。

**バリデーション（`BillingAggregationValidator`、Domain純粋関数）**: 得意先2件と真偽値から拒否理由を返す。`SalesEditLockEvaluator`と同じ形。検証項目は28-2節#1・2・8（締め得意先限定・3項目一致・孫の禁止）。DB制約と二重に持たせる理由は、ユーザーへの分かりやすいエラーメッセージをアプリ層で返すため（DB制約は最後の防波堤）。

### 28-6. 日付制限・消込のグループスコープ拡張

**消込（`SettlementService`）**: 対象クエリを請求集約グループへ広げている（`RecalculateForBillingGroupAsync`）。グループへ広げないと請求集約先への入金がその得意先自身の売上行だけと突き合わされ、**請求集約先の売上が実際には未入金でも消込完了になり、請求集約元の売上は永久に未消込のまま残る**という金額の実害が出る（`SettlementAllocator`の「全額充当」分岐がグループ全体の入金額と単独得意先の売上額を比較して誤って成立するため）。グループ解決は`RecalculateClosingAsync`内（`TaxUnit`分岐より後）で`customers.Where(c => c.BillingCustomerCode == customer.BillingCustomerCode)`の1クエリで行い、都度得意先（CHECK制約により常に単独グループ）はこのクエリ自体を通らない。`sales`/`receipts`/`receipt_allocations`の3クエリは、グループが1件（単独得意先）なら従来どおり等値比較、2件以上なら`Contains`に分岐し、単独得意先（当面ほぼ全件）の実行計画を変えない。配分本体（`billing_number`でのグルーピング、`SettlementAllocator.Allocate`）は無改修。グルーピングキー（`billing_number`／`receipt_slip_number`）と整列キー（`sales`の主キー）がいずれも全社で一意（得意先単位でない）であることが、対象を得意先集合へ広げても配分ロジックが無改修で成立する根拠。

**日付制限（`BillingClosedDateService`）**: `GetLatestConfirmedBillingDateAsync`は`billings`を引く前に対象得意先の`BillingCustomerCode`（請求集約先）を解決する。`ReceiptEntryService.EvaluateEditLockAsync`も、コンストラクタ注入済みの`billingClosedDateService`を呼ぶ（同一クエリを手書きで重複させない）。

**入金入力（`ReceiptEntryService`／`ReceiptEntryViewModel`）**: 業務ルール4（入金は請求集約先にだけ入る）を`GetClosingCustomerAsync`（Application）と`ApplyCustomerAsync`（Presentation）の両方に実装している。既存の都度得意先拒否と同じ「ViewModelの事前チェック＋サービスのthrow」の二重化に倣う（ViewModelにエラー表示の共通ヘルパーが無く、事前チェックを欠くと検索モーダル経由の入力がfire-and-forgetで例外を握り潰すため）。

### 28-7. 請求締め・締め解除のグループスコープ拡張

**請求締め（`BillingClosingService`）**: `BuildCandidatesAsync`の`customers`クエリは`c.BillingCustomerCode == c.CustomerCode`で絞り、請求集約先（または単独得意先）だけを締め対象の候補にする。請求集約元も候補にすると「1得意先＝1候補＝1`billing`」としてそのまま処理され、**請求集約元が自分自身の`billing`を単独で確定してしまう**（業務ルール3違反）ため。`BuildCandidateAsync`では、対象が請求集約先であることが確定した時点で`SettlementService`と同じパターンでグループの得意先コード集合を解決し、`salesQuery`と`receiptQuery`の両方をそのグループへ広げる（単独得意先ならインデックスを保つため等値比較のまま、2件以上なら`Contains`に分岐）。`latestConfirmed`／`previousBalance`（`billings`照会）は対象得意先自身のコードのままでよい（`billings`は請求集約先にしか作られないため、対象がすでに請求集約先であればこれ以上広げる必要も広げてはいけない理由もない）。`CalculateTaxSummary`（`ConsumptionTaxCalculator`呼び出し）は無改修（`TaxUnit.Slip`の`GroupBy(SalesSlipNumber)`は伝票番号が全社一意で1伝票=1得意先のため、グループ内の複数得意先の行が混ざっても伝票ごとの税額再計算は汚染されない）。

`receiptQuery`もグループへ広げる理由は、28-3節「既知の制約（`receipts`）」のとおり。広げておけば、請求集約元へ変更された得意先に単独時代の入金が残っていても、請求額は`SettlementService`（グループ全体を見る）と食い違わずに算定される。

**締め解除（`BillingReleaseService`）は改修不要**。対象抽出は`billing_date`のみで絞るため、上記により自動的に請求集約先のみが対象になる。売上の紐付け解除クエリは`billing_number`の所属だけで判定し`customer_code`で一切絞っていないため、請求集約元の売上行も無改修で正しく拾われて未請求へ戻る。消込再計算は`SettlementService.RecalculateForBillingGroupAsync`を対象`billing`のCustomerCode（＝請求集約先）ごとに呼ぶだけで、グループ解決がそのまま請求集約元の消込キャッシュも巻き戻す。

### 28-8. 請求書帳票の請求集約元ごとの内訳表示

請求集約時の明細表示（見出し行・明細行・小計行、ヘッダーの「請求集約元: N社」、改ページ時の見出し行孤立防止）の仕様と実装は`docs/report-spec.md` 2-2-1節を参照。

### 28-9. 得意先元帳のグループスコープ拡張・取引履歴のみモード

**Domain（`CustomerLedgerBuilder`）**: `Build`の先頭で`!input.Customer.IsBillingRoot`（請求集約元）なら`BuildTransactionHistoryOnly`へ分岐する。既存の`AddSalesEntries`のみを呼び、消費税行・入金行・前月繰越行・残高累積を一切行わず、`SalesTotal`のみ実値で他（`OpeningBalance`／`TaxTotal`／`ReceiptTotal`／`ClosingBalance`）を0にした`CustomerLedgerResult`（`IsTransactionHistoryOnly = true`）を返す。**請求集約先（従来どおりの経路）のビルダー側の計算ロジックは単独得意先と共通で、グループ専用の分岐を持たない**。このクラスは`Customer.CustomerCode`をどこでも参照せず`TaxUnit`／`RoundingType`しか見ないため、呼び出し元（`CustomerLedgerQueryService`）がグループ全体（自身＋全請求集約元）の`Sales`／`Receipts`を渡すだけで、`ProvisionalTaxAsOf`が28-3節の要件（グループ売上＋請求集約先の端数区分で仮計算し締め処理の結果と一致し続ける）を自動的に満たす。`SortKey`の構成要素（売上PK・`billing_number`）が全社一意であることは28-6／28-7と同じ根拠。時系列表としての読みやすさを優先し、請求書帳票（28-8）とは対照的に`SortKey`へ得意先コードは加えない（同日行を得意先ごとにまとめると残高が日付順に読めなくなるため）。

`CustomerLedgerEntry`は`CustomerCode`／`CustomerName`（売上・入金行のスナップショット列から設定）を持ち、グループ内のどの得意先の伝票かを行単位で識別できる。`CustomerLedgerResult.IsBalanced`は`IsTransactionHistoryOnly`のとき常にtrueへ短絡する（取引履歴のみモードは検算式の対象外）。

**Application（`CustomerLedgerQueryService`）**: `customer.IsBillingRoot`かつ`TaxUnit != Line`（都度得意先は請求集約に一切参加できないためグループ解決自体を省略。`SettlementService`が`TaxUnit.Line`を`RecalculateDetailAsync`へ分岐して同クエリを避けるのと同じ理由）のとき、`Sales`と`Receipts`の両方をグループへ広げる。グループ解決・`Contains`/等値比較の分岐は`SettlementService.RecalculateClosingAsync`・`BillingClosingService`と同じイディオム。

**`Receipts`もグループ展開する**: 業務ルール#4「入金は請求集約先にだけ入る」は入力時のガードに過ぎず、28-3節「既知の制約（`receipts`）」のとおり請求集約元へ変更された得意先に単独時代の入金が残りうる。この入金は消込・請求締めではグループ全体の分として扱われるため、元帳だけ単独スコープのままだと請求集約先の残高が`billing.current_billing_amount`と一致しなくなり、かつ請求集約元の元帳は取引履歴のみで入金を出さないため、その入金がどの元帳にも現れなくなる。

`Billings`の照会は対象得意先自身のコードのままとする。根拠は`HasBillingChangeLockAsync`が請求集約元自身のコードに確定済み`billings`が存在する状態を防いでいること（請求済み売上があればリンク自体が拒否され、単独得意先は常に請求集約先なので確定済み`billings`があればリンクが拒否される）。

`GetBalanceAsOfAsync`は請求集約元に対して`null`を返す（`result.IsTransactionHistoryOnly`を見て判定）。残高0円と区別するため（戻り値は`decimal?`で、唯一の呼び出し元`CustomerLedgerViewModel`が`?? 0m`で受けている）。

**Presentation（元帳画面）**: `CustomerLedgerViewModel`は`IsTransactionHistoryOnly`・その反転の`IsBalanceVisible`・案内文`AggregationNotice`を持つ。請求集約元では`GetBalanceAsOfAsync`の呼び出しをスキップし、「「{得意先名}」は請求集約元です。請求・消費税・残高は請求集約先「{コード}」に集約されています。この画面には売上（税抜）のみを表示します。」を表示する（入金入力画面の既存の拒否メッセージと同じくコードのみを出す作法に合わせる）。`CustomerLedgerLineViewModel`は`CustomerLabel`（得意先欄）・`BalanceDisplay`（取引履歴のみモードでは`null`を返し残高欄を空欄にする。`Entry.Balance`が非nullableのため専用コンバーターを作らない既存慣行に従いViewModel側でnullableにしている）を持つ。XAMLは「得意先」列を常時表示し（`GridViewColumn`に`Visibility`がなく条件付き非表示は割に合わないため）、残高列のバインドは`BalanceDisplay`、現在残高・前月繰越・消費税計・入金額計・残高の各表示は`IsBalanceVisible`でまとめて非表示にする。

## 29. 月次締め処理

### 29-1. 月次締め処理（集計・確定）

全得意先の暦月末売掛残高を`monthly_closings`に確定保存する画面・処理。画面の骨組みは請求締め処理（9章）と同じ。違いは次の2点。

- 締め日のコンボボックスは持たない（全得意先が対象）。
- 請求日の代わりに「集計年月」（暦月）を選ぶ。ComboBoxで当月から過去24か月までを選べ、既定値は前月。年月は日付ではないため、「日付欄はDatePickerに統一」の対象外。

一覧は選んだ年月の確定済み`monthly_closings`（解除済みを除く）。年月を変えると自動で再取得する。締め確定（F10）は実行前に確認ダイアログを挟む。

売掛金残高一覧表の印刷ボタン（F11）を合計行の右に置く。請求締め処理と違って行選択は無く、選んだ年月の一覧を全件印刷する（一覧が0件のときは無効）。レイアウトは`report-spec.md` 2-4。

#### 金額の決め方

計算は`MonthlyClosingCalculator`（Domain純粋関数）、入力は元帳（`CustomerLedgerQueryService.GetInputAsync`）と共通。

| 項目 | 値 |
|---|---|
| 当月残高 | 元帳の月末残高（`CustomerLedgerResult.ClosingBalance`）そのまま。完了条件「集計値が元帳の残高と一致する」はこれで満たす |
| 前月残高 | 前月の確定行の当月残高。確定行が無ければ元帳の月初残高 |
| 売上額・入金額 | 元帳の`SalesTotal`・`ReceiptTotal` |
| 消費税額 | `当月残高 − 前月残高 − 売上額 + 入金額`で逆算する |

`tax_unit=1`（請求単位）では、前月に仮計算した税が請求締め後に確定値へ置き換わり、前月行の当月残高と「元帳を今計算し直した月初残高」が食い違うことがある（`database-schema.md` 2.16節の「確定した行の税額は都度再計算しても異なる値になり得る」）。「前月残高＝前月行の当月残高」の連続性を優先し、そのずれを消費税額で吸収する。確定した時点では元帳の月末残高と完全に一致する。

- **請求集約先**: 元帳の値（グループ合算）をそのまま入れる。
- **請求集約元**: 自社の売上額と税率別の対価額だけを入れ、前月残高・入金額・消費税額・当月残高・税率別の税額は0にする。この行は残高の計算式が成り立たず、行を合計すると売上が請求集約先の行と二重に数えられる。集計に使うときは請求集約元の行を除くこと。
- **`tax_unit=3`（内税・都度得意先）**: 売上額は税抜額（`Amount − TaxAmount`）、消費税額は内税額の合計。売上＋消費税が税込額になる。
- **税率別内訳5列**: 対価額は当月売上を税種別区分ごとに合計する。税額は`tax_unit=2`が当月の伝票を伝票ごとに再計算した合計（保存済みの`slip_tax_amount`と一致しなければ`MonthlyClosingException`）、`tax_unit=1`が「当月の請求日を持つ確定済み請求の税額＋仮計算税の当月増分」。上記の逆算で吸収したずれは内訳に配分しないため、**内訳の税額合計が消費税額と一致しないことがある**。

#### 対象外・スキップ・上書き

- 「前月残高・売上・入金・当月残高がすべて0」の得意先は締めない。
- 当月が確定済み、より後の年月が確定済み、前月が解除済み、のいずれかの得意先は飛ばす（理由を画面に表示）。
- 当月の行が解除済みの場合、主キー（`closing_date`, `customer_code`）が同じため新しい行を作れない。既存行を今回の集計値で上書きして確定に戻し、解除日時・解除者をクリアする（ログに件数を残す）。

#### 実装

- Domain: `MonthlyClosingCalculator`。`CustomerLedgerBuilder`の`ProvisionalTaxBucketsAsOf`は税率別の仮計算税を返し、`ProvisionalTaxAsOf`はその合計を返す。
- Application（`Closing/`）: `MonthlyClosingService.ConfirmAsync(year, month)`、`MonthlyClosingQueryService.GetByMonthAsync`。`CustomerLedgerQueryService.GetInputAsync`は`GetAsync`から切り出して公開している。`ConfirmAsync`は独自にトランザクションを開き、追跡中の`MonthlyClosing`を最初と最後に破棄する（画面のスコープは複数回の確定をまたぐため、古い状態値を持ち越さない）。
- 売掛金残高一覧表: `MonthlyClosingQueryService.GetReceivablesBalanceReportAsync`（請求集約元の判定を含む）、`ReceivablesBalanceDocumentBuilder`（`Reports/`）、`ReportKind.ReceivablesBalance`。
- Presentation: `MonthlyClosingWindow`／`MonthlyClosingViewModel`。メニューは`MNU_MONTHLY`（月次）→`MNU_MONTHLY_CLOSE`（月次締め処理、権限レベル1、`monthly_closing`）（`scripts/014_seed_menu_structure.sql`）。

### 29-2. 確定後のロック

`monthly_closings`に確定行があると、既存行の編集ロック（`SalesEditLockService`・`ReceiptEntryService.EvaluateEditLockAsync`・`DetailReceiptEntryService.EvaluateEditLockAsync`）が効く。これに加えて次の2点でロックを補う。

**判定ルール（`MonthlyClosedService.IsClosedAsync`に集約）**: 得意先Cの日付Dは、`monthly_closings`に「`closing_date`＝Dの月末日、`closing_status`＝確定、`customer_code`がCまたはCの請求集約先（`customers.billing_customer_code`）」の行があれば月次締め済み。解除済みは含めない。3か所の編集ロックの月次判定はこのクラスを使う。

1. **新規登録・日付変更の禁止（保存時にApplication層で検証）**
   - 対象: `SalesService.CreateAsync`（`SalesOperationException`）、`ReceiptEntryService.SaveNewAsync`（`ReceiptEntryException`）、`DetailReceiptEntryService.SaveNewAsync`（`DetailReceiptEntryException`）。25章の請求締めの日付検証の隣で`MonthlyClosedService.CheckEntryAsync`を呼ぶ。
   - 訂正による日付変更は、訂正後の状態で編集ロックを再評価する既存処理（`SalesService.UpdateAsync`・`ReceiptEntryService.UpdateAsync`・`DetailReceiptEntryService.UpdateAsync`）が同じ判定を使うので、追加の検証は不要。
   - 画面側の`DisplayDateStart`による制限は付けない（締め済みの月が飛び飛びになるため。25-2・25-5と同じ理由）。締め済みの月を選んで保存するとエラーメッセージで知らせる。
2. **請求集約先の行でもロックする**: 請求集約先の月次行はグループ全体（請求集約元の売上・入金）を含むため、請求集約元に自分の月次行が無くても、請求集約先が確定済みなら請求集約元の売上・入金も登録・訂正・取消できない。判定対象は`database-schema.md` 2.16節の「`customer_code`＋年月」に「請求集約先の行」を足したもの。

### 29-3. 月次締め解除

指定した年月の確定済み`monthly_closings`を全得意先まとめて解除済にする画面・処理。月次締め処理（29-1）とは別ウィンドウ。構成は締め解除処理（請求。10章）と同じ。

- **権限はメニュー単位の判定のみ。** 「月次締め解除処理」（`MNU_MONTHLY_RELEASE`、`monthly_release`）は権限レベル9（管理者）で登録している。一般社員（レベル1）にはメニューが表示されず画面を開けない。画面内・サービス内には権限判定を持たない（請求の締め解除処理と同じ方式）。
- **入力は集計年月のみ。** 画面表示時・年月変更時に自動で対象一覧（確定済みの行）を取得し、解除実行（F8）のみ明示操作にする。実行前に確認ダイアログを挟む。
- **All-or-nothing。** 対象のうち1件でも解除できないものがあれば、何も更新せず全体を中止する。一覧の備考欄に理由を出し、1件でもあれば解除実行を無効にする。
- **解除できない条件**: その得意先に、より後の年月の確定済み行がある。解除すると、後の月の前月残高（前月確定行の当月残高。29-1）とのつながりが切れるため。新しい月から順に解除する運用になる（請求の「締め順序の逆転」と同型）。
- **非破壊。** `closing_status`を解除済にし、`released_at`／`released_by`・`updated_by`／`updated_at`を設定する。物理削除しない。他のテーブルは更新しない。編集ロックは導出方式なので、解除すると29-2の判定から外れ、その月の伝票を再び登録・訂正・取消できる。
- **再確定**は月次締め処理（29-1）が解除済みの行を上書きして確定に戻す（この画面に再確定の機能は置かない）。

実装: `MonthlyClosingReleaseService`（`PreviewAsync`／`ReleaseAsync`。`MonthlyClosingException`を再利用）、`MonthlyClosingReleaseWindow`／`MonthlyClosingReleaseViewModel`。権限レベルによるメニューの出し分けは既存の`MenuTreeBuilderTests`で担保している。

### 補足

- 得意先1件だけを解除する機能は持たない（年月単位の一括のみ）。必要になった場合は別途設計する。
- 都度得意先（明細入金・売上）も月次締めの対象なので、判定は税単位で分岐しない。
- 受注入力は月次締めの対象外（受注は請求・消込・残高の対象外）。

---

## 30. コピー機売上CSV取込

### 30-1. スコープ

コピー機の売上データCSV（1行＝1台分の請求。基本料等の加算行が同一機番の別行で出ることがある）を取り込み、同一機番・同一締日の行を合算して1機番・1締日につき売上1伝票（明細1行）を作成する。取込後に納品書を印字できる。受注は介さない。CSVの定義とサンプルはメーカー出力そのままで、列は**列名（ヘッダー）で引く**（列の並び替え・追加に強くするため）。取込に使う列は次の9つのみ。

| CSV列 | 用途 |
|---|---|
| 機番 | コピー機マスタで得意先を特定する。売上の社内摘要 |
| 機種名（漢字） | 売上の社外摘要 |
| ユーザ請求CV（モノ／フル／フルＰ） | 売上の社外摘要 |
| ユーザー請求金額（機器合計）-税別 | 売上の単価 |
| 締日（yyyyMMdd） | 売上の伝票日付 |

### 30-2. 業務ルール

- **同一機番・同一締日は合算して1伝票**（明細1行）。数量は1固定、単価は「ユーザー請求金額（機器合計）-税別」（合算時は合計）、金額＝単価。納品書は1機番1枚。
- **合算ルール**: 同一機番・同一締日の成功行を1件にまとめる。金額（税別）は合計、機種名・社外摘要・社内摘要・行番号は最初の行のもの（機種名が異なっても最初の行を採用しエラーにしない）。出力順は最初の出現順。行エラーの行は合算に含めずエラーのまま表示する。プレビューには合算した行数を表示する。
- **得意先**: 機番→`copier_machines`→得意先コード。得意先名・税区分・端数区分は得意先マスタから取る。機番がマスタに無い行は取込不可（エラー）。
- **社外摘要**（`slip_remarks`、200字）: `{機種名} モノ{CV} フル{CV} フルP{CV}`（CV＝ユーザ請求CVの3列。空欄は0）。上限超過はエラー。**社内摘要**（`internal_remarks`）: `機番`。
- **伝票日付**: CSVの「締日」。請求締め済み・月次締め済みの期間に当たる行は取込不可（`SalesService.CreateAsync` の既存検証。画面でも事前に判定して表示する）。
- **商品**: 売上明細は商品コード必須のため、取込専用の汎用商品1件（コード `COPYCHG`・名称「コピー機利用料」・税種別区分「通常税率」・課税・単位なし。登録手順は `docs/operations.md` 7章）を商品マスタに登録しておく運用とする（暫定。コードは定数。無い・無効なら画面を開いた時点でエラー表示し取込不可）。原価は商品マスタの標準原価を転記する（`SalesSlipTypeRules.NormalizeCostPrice`）。**暫定: 機種ごとの税種別・商品の使い分けはしない。**
- **税区分**: 外税の得意先（請求単位・伝票単位）のみ対象。単価はCSVの税別金額をそのまま使う。内税明細単位の得意先は税別金額を単価にできないためエラー行とする（暫定）。税額は `SalesService.CreateAsync` の `SalesTaxAmountAssigner` が確定するので取込側は計算しない。金額は `ConsumptionTaxCalculator.CalculateLineAmount`。
- **0円・負数**: 税別金額が0以下の行は取込対象外（警告表示。返品・値引は扱わない）。
- **二重取込防止**: 同一機番・同一締日は1回のみ（`copier_import_histories`、2.6-2節）。取込済みの行はプレビューで「取込済」と表示し取込対象から外す。
- **担当者**: 取込操作者の社員コード（`ICurrentEmployeeContext`）を伝票担当者に設定する。

### 30-3. 画面と操作

メニュー「受注・売上」配下に「コピー機売上CSV取込」（`screen_key=copier_csv_import`、権限1）を追加する。

1. 「ファイル選択」（`OpenFileDialog`）でCSVを選ぶ。Shift-JISで読む（`CodePagesEncodingProvider` を起動時に登録）。ヘッダー必須の列名が欠けていればエラー。
2. 読込結果をプレビューのDataGridに表示する（行番号・機番・得意先コード/名・締日・機種名・金額・合算行数・状態）。状態は「取込可」「取込済」「エラー（理由）」「対象外（0円）」（色分け）。取込可の行のみ取込対象。ファイル全体のエラー・パーサーの行エラーも同じ一覧に理由つきで表示し、件数サマリ（取込可の件数・合計金額、取込済・エラー・対象外の件数）を出す。画面表示時に汎用商品を確認し、無い・無効なら警告を表示して「取込実行」を無効にする。
3. 「取込実行」で取込可の行（合算後の1機番・1締日）を1件ずつ作成する。**1件＝1トランザクション（`CreateAsync` の既存の単位）で全件のAll-or-nothingにはしない。** 行ごとに成功（伝票番号）・失敗（理由）を結果欄へ表示し、失敗行があっても他の行は登録済みになる（再取込時は取込済みとして除外される）。実行前に確認ダイアログを出し、実行中は再実行できない。
4. 完了後「納品書を印字しますか？」を確認し、Yesなら取込で作成した伝票をプレビューなしで連続印刷する（`DeliveryNoteService.GetAsync`→`DeliveryNoteDocumentBuilder`→`ReportPrintService.Print`→成功時 `MarkIssuedAsync`）。印字は失敗した伝票を除き、作成した全伝票が対象。プリンタは納品書の既存のプリンタ設定に従い（未設定・送信失敗時は `ReportPrintService.Print` が印刷ダイアログを出す）、1枚失敗しても続行して最後に成功・失敗件数を表示する。

### 30-4. コピー機マスタ画面

メニュー「マスタ管理」配下に「コピー機マスタ」（`screen_key=copier_machine_master`、権限1）を追加する。他のマスタと同じ「コード（機番）直接入力＋Enter読込、得意先コード欄でSpace検索、無効化（論理削除）」。得意先は無効でないものに限る。得意先が無効化されたとき紐づく機番が残っていてもよい（取込時に得意先の有効性を検証してエラー行にする）。

### 30-5. 実装構成

- Domain: `Import/CopierCsvParser`（純粋関数。デコード済み全文→ヘッダーの列名辞書、金額・日付の解析、摘要の組み立て。必須列欠落はファイル全体のエラー、それ以外は行単位のエラー〔機番空・金額/締日不正・列数不足・摘要200字超過〕。空行は無視、各値はTrim。成功行は（機番・締日）で合算し、合算行数を `MergedLineCount` に持つ）。Application: `Sales/CopierCsvFileReader`（Shift-JIS読込）。
- Application: `CopierMachineService`（CRUD）、`CopierSalesImportService`（`PreviewAsync(rows)`＝機番解決・検証・取込済判定、`ImportAsync(previewRows)`＝1行ずつ `SalesService.CreateAsync` と履歴INSERT。履歴INSERTは `SalesService.CreateAsync` の `beforeSave` コールバックで売上と同一のSaveChanges・トランザクションに載せ、二重取込を防ぐ。汎用商品の有無は `CheckProductAsync` で画面開始時に確認できる。履歴の主キー違反は「取込済」の失敗行として扱う）。
- Presentation: `CopierMachineMasterWindow/ViewModel`、`CopierSalesImportWindow/ViewModel`。取込画面は他画面と同じくウィンドウ単位のDIスコープ（`WindowService.Show`）で動く。`ImportAsync` が失敗行のあとに `ChangeTracker.Clear()` するため、DbContext を他画面と共有してはならない。
- DB: `scripts/024_create_copier_machines.sql`（2テーブル）、`scripts/014_seed_menu_structure.sql` へメニュー2項目を追記、`MainMenuViewModel.OpenMenuItem` に2キーを追加、`App.xaml.cs` にDI登録。
- テスト: Domain単体（パーサー）、Application結合（外側トランザクション＋Rollback方式。機番解決・税額・摘要・二重取込拒否・締め済み拒否）。実機確認で追加したデータは最後に削除する。

### 30-6. 申し送り

- 商品は暫定で固定の1件。機種・摘要区分ごとに商品を分けたい要望が出たら `copier_machines` に商品コードを足す。
- CSVの「締日」を伝票日付とした根拠はサンプル（`20260920`）から。業務上別の日付を使うなら確認する。
