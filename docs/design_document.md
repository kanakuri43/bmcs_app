# bmcs_app 設計資料（画面一覧・不明点）

> `CLAUDE.md` に置くべき「常に守るべき薄いルール」に対して、こちらは調査・分析系の内容（画面一覧、未解決の不明点）をまとめた設計参考資料。更新頻度は低いが、都度読み込む必要はないため `docs/` に分離している。データベースに関する情報は `docs/database-schema.md` に記載する（本資料には書かない）。
>
> 元資料: `石山初期設計/CLAUDE.md.draft`、`石山初期設計/resources/` 配下の画面仕様書一式。
>
> 各画面仕様書に付与されている `SCR-xxx` 等の画面番号は暫定的なものであり、正式な画面IDとして確定していないため、本資料でも記載しない。画面は名称で参照する。

> **画面レイアウトの参照元（2026-09-08 ユーザー指示）**: 業務要件・画面遷移は上記の元資料を正とするが、**画面のレイアウト（項目配置・操作性）は旧Delphi版を参照しない。** `C:\Users\User3292\source\repos\bmcs_app`（WPF プロトタイプ、`bmcs_app.Sales` 等）を参照する。Phase 4-3（受注入力画面）以降の画面実装で適用する。

---

## 1. やりたいことの要点（画面ごと）

| 画面 | 目的 |
|---|---|
| メインメニュー | 起動時パラメータで渡された社員コードから権限を判定し、その権限に応じてメニューを出し分け、各機能へ遷移 |
| 受注入力 | 注文を仮伝票として先行登録。入荷・在庫引当ができるまでは売上（売掛金）を発生させない（引当数量の扱いはproduct-spec.md共通業務ルール6を参照。D-3・2026-09-10） |
| 売上入力 | 都度売上・受注からの売上確定・返品/値引を扱い、納品書を発行。過去伝票の複写入力にも対応 |
| 入金入力（締め） | 締め得意先の入金を登録し、古い請求から自動で消込（Phase 7-2実装済み）。手数料差額の入力は別画面（Phase 7-3、手入力のみ。自動計算・自動補正提案は行わない。M-14・2026-09-10確定） |
| 明細入金 | 都度得意先向け。売上伝票 or 明細請求書を指定してピンポイント消込 |
| 請求締め | 締め対象得意先の期間内売上・入金を集計して請求データを確定し、請求書を一括発行 |
| 請求締め解除 | 管理者権限のみ。確定済み請求データを解除済にし、売上の請求状態を未請求へ戻す。**請求締めとは別画面（別メニュー項目）とする**（C-8・2026-09-10確定。画面内アクションではなく画面分離で権限差を表現する） |
| 明細請求書発行 | 都度得意先の「未発行かつ未入金」の売上をまとめて選び、宛名を都度書き換えて1枚の請求書を発行 |
| 得意先元帳 | 得意先ごとの売上・入金明細と残高推移を時系列表示。現時点のリアルタイム残高も常時表示。伝票プレビューは`readOnly`モードの売上/入金画面を再利用（プレビュー専用の別画面は用意しない） |
| 月次締め | 全得意先の月次売掛残高、担当者別売上・粗利を集計・確定（確定後はロックされ編集不可） |
| 月次締め解除 | 管理者権限のみ。確定済み月次締めを解除済にする。**月次締めとは別画面（別メニュー項目）とする**（C-8・2026-09-10確定、請求締め解除と同じ理由） |
| データ検索 | 受注/売上/入金を横断的に検索し、納品書未発行の売上をまとめて一括発行 |
| 共通検索モーダル | 得意先・商品の検索モーダル。商品検索は「マスタから」「過去の取引履歴から」の2軸、最大6件を伝票へ一括転記 |
| マスタ管理 | 得意先・商品・銀行・単価計算・社員・**自社情報（適格請求書発行事業者の登録番号等）**の各種マスタ管理、プリンタ設定（保存先はproduct-spec.md参照。D-4・2026-09-10） |

---

## 2. 不明点・要確認事項（実装前に確定が必要）

DB設計に関する未確定事項は `docs/database-schema.md` を参照。以下はDB設計以外の不明点。

- **在庫・発注との連携インターフェース**: スコープ外だが「連携する部分は考慮」という方針のみ確定。実際にどのような形で連携するか（DB直接連携／API／ファイル連携等）、受注入力画面の「発注データへ回す」ボタンが具体的に何をすべきかは未定義。
- **受注入力画面の店頭在庫数表示**: 在庫管理がスコープ外である一方、受注入力画面は「現時点の店頭在庫」の表示を前提としている。この情報をどこから取得するか（在庫システム連携が前提か、当面は非表示/固定値でよいか）は未確定。
- **インボイスの端数処理と税区分の整合（要: 顧問税理士確認）**: 適格請求書の「税率ごとに区分した消費税額」は1枚の請求書につき税率ごとに1回の端数処理を行うのが原則だが、得意先マスタの税区分3種のうちこれを自然に満たすのは「請求単位」のみ。「伝票単位」「内税明細単位」は伝票単位・明細単位で端数処理するため原則と衝突する。確認すべき点は次の3つ。
  1. どの書類を適格請求書として扱うか（月次請求書か、納品書・明細請求書か。複数書類で記載事項を満たす形とするか）。
  2. 「伝票単位」の得意先の月次請求書で、伝票ごとに確定した税額の合計を印字してよいか、請求書上で税率ごとに再計算するか。
  3. 「内税明細単位」の得意先（＝都度得意先）の明細請求書で、明細ごとに算出した税額の合計を印字してよいか。
  現行の他システム・既存帳票でどう運用しているかも確認材料とする。業務ルール側の記述は [`docs/product-spec.md`](product-spec.md) の共通業務ルール9を参照。

- **銀行マスタの用途**: マスタ管理の対象に「銀行」があるが、用途の記載がない。Phase 1-1 では**自社の入金口座マスタ**（入金入力で入金先口座を選ぶ／請求書に振込先を印字する）と解釈して `bank_account` を定義した。全銀協の金融機関コードマスタ（得意先の振込元を記録する用途）である可能性も残るため、実際の運用を確認したい。
- **得意先マスタの支払条件・与信限度額の要否**: 締め得意先には支払条件（例: 20日締め翌月末払い）が必要になることが多いが、設計資料に記載がない。Phase 1-1 では推測を避けて項目を作っていない。必要であれば `ALTER TABLE` で追加する（属性の追加は後続フェーズで可能）。与信限度額の管理を行うかも併せて確認したい。

これらは実装着手前にユーザーへの確認が必要。

---

## 3. 共通検索モーダルの実装確定事項（Phase 3、2026-09-08 確定）

- **商品検索モーダル「過去の取引履歴から」の対象データ**: 売上のみ（`sales`）。`order_slip`（受注）は対象外。
- **履歴から転記する単価**: 履歴行の単価（過去の実売価格）。「マスタから」軸は商品マスタの単価。
- **履歴軸の得意先スコープ**: 呼び出し元画面で選択済みの得意先のみ。得意先が未選択の場合は履歴タブを無効化し、「先に得意先を選択してください」と案内する。
- **履歴軸はカナ検索非対応**: 売上テーブルに商品カナ列がないため。「マスタから」軸のみコード・名称・カナ・規格で検索できる。
- **既知の制約（性能）**: 得意先・商品検索とも、マスタ全件をロードしたうえでのメモリ内絞り込み（既存の得意先・商品マスタ画面と同じ全件ロード方針に揃えたもの）。マスタが数万件規模になった場合は性能問題が出てから、両方まとめてサーバ側絞り込みに変更する。

---

## 4. 受注入力の明細行グリッド（Phase 4-2、2026-09-08確定）

- **単価の初期値転記**: 商品を選ぶと、得意先の税区分（`tax_unit`）に応じて商品マスタの外税単価／内税単価のどちらかが単価欄に入る。以後は手入力で上書きできる（`IUnitPriceCalculator`／`StandardUnitPriceCalculator`、`src/bmcs_app.Domain/Calculations/`。2026-09-10、M-3決定によりインターフェース化）。商品検索モーダルのマスタ軸も同じ判定で単価列を表示し、単価列ヘッダ（「単価(税抜)」／「単価(税込)」）で転記元を明示する。
- **明細行 ViewModel は受注・売上で共用する**: `src/bmcs_app/ViewModels/Common/SlipLineViewModel.cs` を受注入力（Phase 4）・売上入力（Phase 5-2）の両方から使う想定。専用画面ごとにコピーは作らない。
- **レイアウト・キー操作**: 旧WPFプロトタイプ（`bmcs_app.Sales`）の明細行パターンを踏襲する。
  - 列幅（左から）: 行番号36 / 商品コード110 / 商品名可変 / 数量72 / 単価88 / 原価80 / 金額96 / 税率56 / 行摘要130 / 削除28。
  - 商品コード欄で `Space` → 商品検索モーダルを開く、`Enter` → 入力済みコードで直接引き当てる。
  - 原価・金額・税率は表示のみ（編集不可）。金額は `数量×単価` を得意先の端数区分で1円に丸めた値（`ConsumptionTaxCalculator.CalculateLineAmount`）。
  - 商品検索モーダルの一括転記（最大6件）は、1件目を呼び出した行へ、残りは後続の空行を埋める／なければ直後に挿入する。転記後は常に末尾に空行を1行維持し、確定後は数量欄へフォーカスを移す。

---

## 5. 受注入力画面（Phase 4-3、2026-09-08確定）

旧WPFプロトタイプ（`bmcs_app.Order`／`OrderMainView.xaml`）のツールバー・ヘッダー・明細・フッター集計・StatusBarの構成をそのまま再現した。ただし旧プロトタイプが前提とする項目のうち、本プロジェクトの `order_slip` エンティティに列がない、または機能自体が未実装のものは、**枠（コントロール）だけ用意し `IsEnabled="False"` で使用不可にした**（非表示にはしない）。

| 項目 | 状態 | 理由 |
|---|---|---|
| 受注日付・得意先（コード+名称、Space/Enter対応）・明細行・フッター集計・保存(F10)・新規(F3)・行追加(F2) | **実装済み** | `order_slip` に対応する列があり、既存の `ConsumptionTaxCalculator`／`IUnitPriceCalculator`／`SlipNumberService` を再利用できる |
| 受注No. | 実装済みだが**読み取り専用表示**。手入力・Space/Enterでの検索は行わない | 採番は保存時にトランザクション内で1回だけ行う方針（`docs/architecture.md` 6章）であり、画面を開いた時点や入力中の手入力・検索を許さない |
| 得意先名の編集（諸口得意先） | **未実装** | `Customer` に「諸口」相当のフラグがない |
| 担当者（コード＋名称） | **枠のみ・使用不可** | `order_slip` に担当者列がない（社員マスタ画面自体は2-3で実装済み）。将来列を追加する場合は属性追加（`ALTER TABLE`）で対応する |
| 摘要（伝票摘要） | **実装済み（2026-09-09）** | ジャーナル系テーブル（`sales`／`receipt`／`detail_receipt`／`order_slip`）に `slip_remarks` 列を追加し（`scripts/011_add_slip_and_line_remarks.sql`）、`order_slip` に対しては本画面から保存できる。同一伝票の全明細行に複写して保存する（`docs/database-schema.md` 1章） |
| 行摘要（明細行摘要） | **実装済み（2026-09-09）** | 上記と同時に `line_remarks` 列を追加。`src/bmcs_app/Views/Common/SlipLineControl.xaml` の行摘要 TextBox の `IsEnabled="False"` を解除した。`SlipLineControl` は売上入力（Phase 5-2）とも共用するため、両方に効く。`sales`／`receipt`／`detail_receipt` はエンティティ・EF設定まで追加済みだが、対応する入力画面自体が未実装（Phase 5-2／7章）のため画面側の配線はまだない |
| 受注状態バッジ | 「受注状態：未売上」を実装（`OrderStatus.NotSold` 固定表示） | 本画面は新規登録のみを扱うため常にこの値で正しい。状態遷移ロジック自体は 4-4 で `OrderStatusService`（サービス層）として実装済み（7章）だが、既存受注を読み込む機能がまだ無いため、本画面のバッジ表示への配線は既存受注の読み込み手段（5-3／10-1）が揃うまで先送りする |
| 前の受注／次の受注（ナビゲーション） | **枠のみ・使用不可**（`CanExecute` を常に `false` にして無効化） | 既存受注の一覧・検索機能が未実装 |
| 削除（F8） | **枠のみ・使用不可** | 旧プロトタイプは物理削除だが、TODO.md の暫定設定 M-17「伝票は物理削除しない」・C-6「取消は状態を戻す」と矛盾するため、そのままは持ち込めない。中止（`OrderStatusService.CancelSlipAsync`）自体は 4-4 で実装済み（7章）。本画面から呼べるようにするには、まず受注No.から既存受注を読み込む機能（受注状態バッジと同じ理由で未実装）が必要なため、F8 配線はそれが揃うまで先送りする |
| `sub_customer_id`（宛名） | **今回のスコープ外（確定）** | 旧プロトタイプにこの概念自体が存在しないため「再現」の対象外とした。**C-9（2026-09-10確定）により、学校のクラス・先生等の宛名柔軟性は`sub_customer_id`ではなく都度書き換え方式で実現することが確定した。** 得意先名称欄（`CustomerName`、行122-137）は2026-09-10、手入力で上書き可能に変更済み（得意先コード検索後、名称を直接書き換えられる）。`sub_customer_id`列自体は将来の別要件に備えて残すが、入力欄は作らない |

実装ファイル: `src/bmcs_app.Application/Order/OrderService.cs`（新規登録ユースケース。採番と登録を同一トランザクションで行う）、`src/bmcs_app/ViewModels/Order/OrderEntryViewModel.cs`、`src/bmcs_app/Views/Order/OrderEntryWindow.xaml`。

### 副次的に発見・修正したバグ

`src/bmcs_app/Behaviors/EnterKeyNavigationBehavior.cs`（Phase 0-6）が、Enterキーで次項目へフォーカス移動する処理を`PreviewKeyDown`（トンネリング）でコンテナに実装していたため、子の `TextBox` 自身が持つ `Enter` 用 `KeyBinding`（商品コード欄の「Enterでコード確定」等）より先にイベントを消費してしまい、機能していなかった。`TextBox.InputBindings` に一致する `Enter` の `KeyBinding` がある場合はフォーカス移動を行わないよう修正した。受注入力画面の実装時に判明したが、売上入力（Phase 5-2）等、同ビヘイビアを使う全画面に影響する修正。

---

## 6. 売上入力画面（Phase 5-2確定、2026-09-10）

受注入力画面（5章）と同じ構成で実装した。**都度売上の直接入力のみ**を扱う（受注からの売上確定＝5-3、返品・値引＝5-4、過去伝票の複写＝5-5、訂正・取消＝5-6は対象外）。旧プロトタイプ（`bmcs_app.Sales`の`SalesMainView.xaml`）のレイアウトを再現しつつ、対象フェーズが別のもの・スキーマ上まだ存在しないものは枠のみ用意し無効化している。

| 項目 | 状態 | 理由 |
|---|---|---|
| 売上日付・得意先（コード+名称、Space/Enter対応）・明細行・フッター集計・保存(F10)・新規(F3)・行追加(F2) | **実装済み** | `sales` に対応する列があり、既存の `ConsumptionTaxCalculator`／`IUnitPriceCalculator`／`SlipNumberService`／`SlipLineViewModel` を再利用できる |
| 得意先名称の上書き | **実装済み**（TwoWay、保存は`this.CustomerName`） | C-9（2026-09-10確定）。受注入力画面に同日入った修正と同じ仕組み |
| 受注No. | **枠のみ・使用不可** | 受注からの売上確定はPhase 5-3の範囲。`sales.order_slip_number`／`order_line_number`列は既存 |
| 請求状態・消込状態の表示 | **実装済み**（固定表示「未請求」「未消込」） | 本画面は新規登録のみを扱うため、`BillingStatus=Unbilled`／`SettlementStatus=Unsettled`が常に正しい。旧プロトタイプの「請求:{InvoicedAtText}／売掛:{ArAggregatedAtText}」に相当する枠を、本プロジェクトの状態カラムに読み替えた |
| 同ボックスのロック（保存/削除の無効化） | **今回は実装しない** | ロック判定はC-6の3条件（請求締め・月次締め・入金済み）に基づき、既存伝票の読み込みと一体でPhase 5-6が実装する。新規伝票は定義上ロック対象外 |
| 印刷（F11） | **枠のみ・使用不可** | 帳票エンジンはM-10（2026-09-10確定：WPF FixedDocument方式）でPhase 10が実装する |
| 前の売上／次の売上（ナビゲーション） | **枠のみ・使用不可** | 既存売上の一覧・検索機能が未実装（受注入力画面と同一理由） |
| 削除（F8） | **枠のみ・使用不可** | M-17「伝票は物理削除しない」・C-6「訂正は元伝票の直接修正」と旧プロトタイプの物理削除が矛盾するため。取消を状態遷移として実装するのはPhase 5-6の範囲 |
| 担当者 | **枠のみ・使用不可** | `sales`に担当者列がない（社員マスタ画面自体は2-3で実装済み。受注入力画面と同一理由） |
| 摘要・行摘要 | **実装済み** | `sales.slip_remarks`／`line_remarks`は既にDDL・エンティティ・EF設定済み。受注入力画面の完了メモにあった「`sales`は画面側の配線がまだ」はこのタスクで解消した |

### 保存時の税額確定ロジック

**税額カラムの分岐処理は`SalesTaxAmountAssigner`（`src/bmcs_app.Domain/Calculations/SalesTaxAmountAssigner.cs`）に一本化し、`SalesService.CreateAsync`から呼ぶ。** ViewModelには置かない。得意先の税区分（`tax_unit`）に応じて次のように確定する（DBのCHECK制約`CK_sales_tax_amount_by_tax_unit`と対応）。

- 請求単位（1）: `slip_tax_amount`／`tax_amount`ともにNULL（請求締め時に一括計算するため）
- 伝票単位（2）: 伝票全体で1回だけ計算した`slip_tax_amount`を全行に複写（`tax_amount`はNULL）
- 内税明細単位（3）: 明細行ごとに`tax_amount`を確定（`slip_tax_amount`はNULL）

同じ分岐は受注からの売上確定（5-3）・返品値引（5-4）・複写入力（5-5）・訂正（5-6）でも再登場するため、`sales`への書き込み口である`SalesService`を必ず経由させることで、後続フェーズも自動的に正しくなるようにしている。`BillingNumber`は`SalesTaxAmountAssigner`では扱わず、新規登録専用の`SalesService.CreateAsync`が無条件にNULLを設定する（既存の請求紐付けを消してしまう経路が5-6にはないようにするため）。

### 検証方法

GUIでのsmoke testの代わりに、`tests/bmcs_app.Application.Tests/Sales/SalesServiceTests.cs`で開発用ライブDBに対する結合テストを実装した。CUS001（請求単位・Floor）／CUS002（伝票単位・RoundHalfUp）／CUS003（内税明細単位・Ceiling）の3得意先すべてで`SalesService.CreateAsync`を実行し、永続化された行の税額カラム・請求状態・消込状態・納品書発行状態・伝票摘要／行摘要の複写を検証した後、作成した行を物理削除する（採番自体は元に戻さない。テスト検証用の後始末であり業務操作ではないため`DevDatabaseFixture`と同じ扱い）。`tests/bmcs_app.Domain.Tests/Calculations/SalesTaxAmountAssignerTests.cs`でDB不要の単体テスト（税区分3種×端数区分3種、非課税行、異常系）も別途用意した。

`DevDatabaseFixture`（`tests/bmcs_app.Application.Tests/`）にロギング登録（`services.AddLogging()`）が欠けており、`ILogger<T>`を要求するサービス（`SalesService`／`CustomerService`等）がDIで解決できない既存の環境不備を本タスクで修正した。

実装ファイル: `src/bmcs_app.Application/Sales/SalesService.cs`、`src/bmcs_app.Domain/Calculations/SalesTaxAmountAssigner.cs`、`src/bmcs_app/ViewModels/Sales/SalesEntryViewModel.cs`、`src/bmcs_app/Views/Sales/SalesEntryWindow.xaml`。メインメニューへの導線は Phase 2-7 でメニュー構成マスタ駆動（`screen_key="sales_entry"`）に置き換わった。

---

## 7. 受注の状態遷移（Phase 4-4、2026-09-10確定）

`order_slip.order_status`（未売上／一部売上／売上完了／中止）と`sales_confirmed_quantity`を更新する処理を、**サービス層のみ**として実装した。**受注入力画面への配線（既存受注の読み込み、削除(F8)ボタンの有効化）は本タスクの範囲外。** 受注No.から既存伝票を読み込む機能自体が未実装のため（5章）、UI配線はその読込手段が5-3／10-1で揃った時点で行う。**2026-09-10、Phase 5-3でこの配線を実施済み（8章参照）。**

### 決定事項（ユーザー確認済み）

1. **実装範囲はサービス層＋結合テストのみ。** UI配線は行わない。
2. **中止（`OrderStatus.Cancelled`）の解除は実装しない。** `docs/product-spec.md`の遷移定義どおり、中止は終端状態とする。
3. **中止の操作単位は伝票単位のみ。** 明細行単位の中止APIは作らない（業務上の失注・キャンセルは伝票丸ごとが通常のため）。

### 実装構成

- **`src/bmcs_app.Domain/Calculations/OrderStatusCalculator.cs`**: `Determine(orderQuantity, salesConfirmedQuantity)` で状態を判定する純粋関数。副作用なし。`Cancelled`はここでは導出しない（決定2）。
- **`src/bmcs_app.Application/Order/OrderStatusService.cs`**: 新規登録専用の`OrderService`とは責務を分ける。
  - `ApplySalesQuantityDeltasAsync`: 正のデルタ＝売上化、負のデルタ＝売上取消（逆遷移）を1メソッドで扱う。**トランザクションを開始せず`SaveChangesAsync`も呼ばない**（呼び出し元の売上登録・取消ユースケースが、伝票登録と同一の`SaveChangesAsync`1回に含めて保存する想定。`docs/architecture.md`6章）。呼び出し時に明示トランザクションが開始されていなければ`InvalidOperationException`（`SlipNumberSequenceCommand`と同じ理由）。受注数量超過・マイナス残・中止済み行への売上化・存在しない行・デルタの重複指定は`OrderOperationException`。
  - `CancelSlipAsync`: 伝票単位の中止。単独のユースケースとして`SaveChangesAsync`を1回呼ぶ。売上完了済みの明細行を含む受注・既に中止済みの受注は`OrderOperationException`。`SalesConfirmedQuantity`は変更しない（分納済みの実績を残す）。

Phase 5-3（受注からの売上確定）は`ApplySalesQuantityDeltasAsync`を、売上登録と同一トランザクション・同一`SaveChangesAsync`から呼ぶ想定。

### 検証方法

GUI配線が無いため、GUIでのsmoke testは行わない。

- 単体テスト: `tests/bmcs_app.Domain.Tests/Calculations/OrderStatusCalculatorTests.cs`（境界値：0／一部／ちょうど／超過／受注数量0）。
- 結合テスト: `tests/bmcs_app.Application.Tests/Order/OrderStatusServiceTests.cs`。開発用ライブDBに対し、`BeginTransactionAsync`→検証→`RollbackAsync`で完結させ、seedデータ（`order_slip`のORD001〜ORD004）には触れない。分納（未売上→一部売上→売上完了）、逆遷移（売上完了→一部売上→未売上）、中止（未売上／一部売上／複数行の一括中止）、および両方の異常系（受注数量超過、マイナス残、中止済み行への売上化、存在しない行、デルタ重複、トランザクション外呼び出し、売上完了済み受注の中止、中止済み受注の再中止、存在しない受注番号）を検証済み。

---

## 8. 売上入力画面の拡張（Phase 5-3・5-4・5-5・5-6・5-7、2026-09-10確定）

Phase 5-1（消費税計算）・5-2（都度売上の直接入力）に続き、売上入力画面へ「受注からの売上確定」
「返品・値引」「過去伝票の複写」「既存伝票の訂正・取消」を追加した。受注入力画面（4-3）にも
「既存受注の読み込み（表示専用）」と「中止（F8）」の配線を追加した（7章の繰り越し分）。

### 8-0. 共通の前提: 伝票検索モーダル

受注No.／売上No.の検索に、`CustomerSearchDialog`と同じ作りの共通モーダルを1つ新設した
（`src/bmcs_app/Views/Common/SlipSearchDialog.xaml`／`ViewModels/Common/SlipSearchDialogViewModel.cs`）。
受注用・売上用の画面を別々に作らず、`SlipSearchTarget`（`Order`／`Sales`）で対象を切り替える。
全件ロード後にメモリで絞り込む方式（`CustomerSearchDialogViewModel.ApplyFilter`と同じ）。
選択結果は**伝票番号の文字列のみ**を返し、実体の読み込みは呼び出し元が自分のクエリサービス
（`SalesQueryService`／`OrderQueryService`。いずれも伝票単位にサマリ化した検索結果を返す）で行う。

TODO.md 10-1（データ横断検索）は本モーダルとは別に、後で横断検索専用の画面として作る
（2026-09-10ユーザー確認済み）。

### 8-1. 受注からの売上確定（Phase 5-3）

- `SalesService.CreateAsync`を拡張し、明細行の`OrderSlipNumber`／`OrderLineNumber`から
  受注デルタをサービス側で導出して`OrderStatusService.ApplySalesQuantityDeltasAsync`を呼ぶ
  （同一トランザクション・同一`SaveChangesAsync`。7章で予告していた呼び出し方）。
  デルタは`(受注伝票番号, 受注行番号)`ごとに**合算してから**渡す（同じ受注行を複数の売上行が
  参照するケースで、`ApplySalesQuantityDeltasAsync`の重複キー拒否に引っかからないため）。
- 受注の消化に算入するのは`SlipType.Sales`の行のみ。**返品・値引行を受注に紐付けることは
  禁止**とし（`SalesService`が`SalesOperationException`で拒否）、受注由来の売上を返品したい
  場合は元の売上行を直接訂正する（5-6）運用にした。
- 売上入力画面の受注No.欄を有効化し、`Space`で伝票検索モーダル（`Target=Order`）、`Return`で
  直接読込。読込時は受注の**残数量**（`受注数量 - 売上化済数量`）を明細行へ転記し、税率は
  **売上日付**で再解決する（単価・原価は受注のスナップショットを引き継ぐ）。中止・売上完了済みの
  受注は検索結果から除外する（`OrderQueryService.SearchAsync`の既定挙動）。
- **受注入力画面の配線（7章の繰り越し分）**: 受注No.欄から既存受注を読み込めるようにした
  （表示専用。内容の訂正保存には対応しない＝`OrderService`に更新系ユースケースがないため）。
  削除(F8)を`OrderStatusService.CancelSlipAsync`に配線し、受注状態バッジも読込内容に追従する。

#### 4-4への追加修正: 中止済み受注への負のデルタを許可

4-4実装時点の`ApplySalesQuantityDeltasAsync`は、`OrderStatus.Cancelled`の明細行への
デルタを符号を問わず一律拒否していた。しかし「一部売上化 → 受注を中止 → その売上を
後から訂正・取消する」という順序が起こり得るため、この場合に売上側の取消（負のデルタ）が
永久にできなくなる不具合があった（発見: レビューエージェントの指摘）。
**中止済み行への正のデルタ（売上化）は引き続き拒否するが、負のデルタ（売上取消）は許可する。**
ただし状態は`Cancelled`のまま維持し、`未売上`／`一部売上`へは戻さない
（中止は終端状態。`docs/product-spec.md`）。

### 8-2. 返品・値引（Phase 5-4）

M-9暫定設定（マイナス数量・マイナス金額＋伝票区分カラム）どおり実装した。旧プロトタイプに
前例がないため新規設計。

- **`sales.slip_type`は明細行ごとに選択する**（2026-09-10ユーザー確認済み）。同一伝票内に
  売上行と値引行を混在できる。
- **値引行も商品コードは必須のまま**とする（値引専用の擬似商品コードは作らない）。税率・
  税種別区分がその商品から決まるのはインボイス制度上も正しいため。
- `src/bmcs_app.Domain/Calculations/SalesSlipTypeRules.cs`に正規化ロジックを1箇所へ集約した。
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
- `ConsumptionTaxCalculator`には手を入れていない（5-1で既にマイナス金額を網羅済み。
  `TaxRounding.RoundToYen`が絶対値で丸めて符号を戻す）。
- `ProductHistoryQueryService.SearchAsync`に`!IsDeleted && SlipType == SlipType.Sales`の
  条件を追加した。返品・値引行や論理削除された行（5-6）が、商品検索モーダルの
  「過去の取引履歴から」タブに単価候補として現れないようにするため。
- 明細行UI（`SlipLineControl.xaml`）に区分列（幅68、`EnumDisplayConverter`で表示）を追加。
  `order_slip`には伝票区分の概念がないため、受注入力画面では列を非表示にする
  （`SlipLineViewModel.IsSlipTypeVisible`をホストが画面単位で設定）。

### 8-3. 過去伝票の複写入力（Phase 5-5）

- `SalesEntryViewModel.CopyFromPastSlipCommand`（ツールバーに新設。旧プロトタイプに対応する
  機能がないため新規UI）→ 伝票検索モーダル（`Target=Sales`）→`SalesQueryService.GetSlipAsync`
  → 新規登録として明細行へ展開。
- **複写するもの**: 得意先、明細行（商品・数量・単価・原価・区分・行摘要）、伝票摘要。
- **複写しないもの**: 伝票番号（新規採番）、伝票日付（当日）、受注リンク、請求状態・消込状態・
  消込済金額・請求番号・納品書発行状態（すべて新規登録の初期値）。
- 税率は新しい売上日付で再解決する（単価は複写元の値を維持し、同じ取引条件の再現を優先する）。

### 8-4. 既存伝票の訂正・取消（Phase 5-6）

C-6（元伝票の直接修正、赤伝方式は不採用）に沿って実装した。

- `SalesService.UpdateAsync`: 行の追加・更新・削除（論理削除）を1回でまとめて扱う。
  1. 明細行の集合を再取得し、読込時点の行番号集合と比較（`SlipConcurrencyGuard.EnsureLineSetUnchanged`）。
     不一致なら他ユーザーの行追加・削除とみなし`SlipConcurrencyException`。
  2. 訂正前の状態で編集ロック（C-6の3条件）を判定。該当すれば`SalesOperationException`。
  3. 受注デルタを（旧数量→新数量の差分として）収集する。
  4. 既存行は**ホワイトリスト方式**で上書き可能な列だけコピーする（伝票日付・得意先名・区分・
     商品・数量・単価・金額・原価・税種別・税率・受注リンク・摘要）。**得意先コード・税区分・
     請求/消込関連の状態カラム・監査列は対象外**（`BillingNumber`は`CreateAsync`だけが
     無条件にNULLを設定する方針を維持し、`UpdateAsync`は一切触らない）。
  5. 読込時にあったが今回の一覧にない行は`IsDeleted=true`（M-17。物理削除しない）。
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
  （`docs/database-schema.md` 2.8節。C-6は「取消も直接修正」としているが、物理削除しない
  という既存方針と整合させるため`is_deleted`方式を採用）。
- **編集ロック判定** (`SalesEditLockEvaluator`。Domain純粋関数＋`SalesEditLockService`が
  `monthly_closing`／`detail_invoice_sales_line`を照会): ①`billing_number`が確定済み請求を指す
  ①'明細請求書発行済み（`detail_invoice_sales_line`に連携している。Phase 6-5で追加。13章参照）
  ②対象年月の`monthly_closing`が確定済み ③`settlement_status`=消込完了。理由文言を返すのみで、
  ViewModelは業務判断をせずそのまま表示する（docs/architecture.md 5章）。
- **伝票単位の楽観的排他制御の共通処理**（`SlipConcurrencyGuard`。docs/architecture.md 9章が
  Phase 5での実装を予告していたもの）を`src/bmcs_app.Application/Common/`に新設し、
  `EnsureLineSetUnchanged`／`TouchAll`の2メソッドを提供する。伝票種別ごとに書かない。
- 売上入力画面: 売上No.欄を入力可能にし（`Space`で検索モーダル、`Return`で直接読込）、
  請求状態・消込状態の表示を実際の値にバインドし、編集ロック中は保存・削除を無効化して
  理由をステータスバーに表示する。削除(F8)を`CancelSlipAsync`に配線。
  **前／次ナビゲーションは伝票検索モーダルで代替できるため実装しない**（旧プロトタイプの
  一覧キャッシュ方式は踏襲しない）。印刷(F11)・担当者は引き続き対象外（Phase 10・2-3）。

### 8-5. フェーズレビュー（Phase 5-7）

完了条件「金額が狂う経路が残っていないことを確認できている」について、以下を確認した。

- 新規登録（5-2）・受注確定（5-3）・複写（5-5）・訂正（5-6）のすべてが`SalesService`を経由し、
  税額確定は`SalesTaxAmountAssigner`のみが行う（ViewModelや他のサービスが`slip_tax_amount`
  ／`tax_amount`へ直接書き込む経路はない）。
- 伝票単位の値（`slip_tax_amount`等）を誤って`SUM`している箇所はない。`SalesQueryService`の
  検索結果集計は明細行ごとに異なる`amount`列の合算であり、`docs/database-schema.md` 2.8節が
  警告する「伝票単位の値の重複計上」には当たらない。
- 返品・値引の粗利計算は`SalesSlipTypeRulesTests`（単体）と実際の`GrossProfit`計算式で
  符号を確認済み（値引は原価0で粗利がそのまま減る、返品は原価もマイナス計上され元の売上の
  粗利影響を打ち消す）。
- 状態カラム（`billing_status`／`settlement_status`／`order_status`／`sales_confirmed_quantity`）
  の整合は結合テスト（`SalesServiceCorrectionTests`）で全経路を確認済み。
- `IsDeleted`のフィルタ漏れ（`ProductHistoryQueryService`）を本タスク中に発見・修正した。

### 検証方法

- 単体テスト: `SalesSlipTypeRulesTests`（区分ごとの数量符号・原価正規化）、
  `SalesEditLockEvaluatorTests`（C-6の3条件×単独/複合/該当なし）。
- 結合テスト: `tests/bmcs_app.Application.Tests/Sales/SalesServiceCorrectionTests.cs`。
  `SalesService.CreateAsync`／`UpdateAsync`／`CancelSlipAsync`はいずれも内部で独自に
  トランザクションを開始・コミットするため、`SalesServiceTests`と同じ「使い捨てデータを
  コミットしてfinallyで物理削除する」方式を踏襲した（外側をトランザクションで包み
  `RollbackAsync`する方式は、ネストした`BeginTransactionAsync`が例外になるため使えない）。
  受注からの一部／超過売上確定、返品行の登録、数量と金額の符号矛盾の拒否、返品値引行の
  受注紐付け拒否、訂正による数量増加と受注側同期、訂正による行削除と受注側逆遷移、
  編集ロック3条件（個別に分離した使い捨てデータで検証）、訂正後の状態が新たにロック対象に
  なるケース、他ユーザーによる行追加時の排他エラー、取消による全行論理削除と受注側復元、
  中止済み受注に紐づく売上の取消（8-1の4-4修正の検証）を確認済み。全37件green
  （既存23件＋新規14件）。単体テストはDomain 195件（既存181件＋新規14件）すべてgreen。
- 実機確認: `dotnet run --project src/bmcs_app`でアプリが正常に起動しメインメニューが
  表示されることを確認済み。**GUI操作による実機確認（Space/Enterキー操作、モーダル表示等）は
  本環境にWPF向けのUI自動操作ツールがなく実施していない。** ビルド成功と自動テストのみで
  検証している。

---

## 9. 請求締め処理（Phase 6-1、2026-09-11確定）

締め得意先（`tax_unit`＝請求単位／伝票単位、`closing_day ≠ 0`）の期間内売上・入金を集計し、
`billing`へ請求データを確定する。Phase 5までで`sales.billing_number`を書き込む経路が
存在せず常にNULLだったため、C-6の編集ロック条件①（請求締め済み）が実データで一度も発火
していなかった欠落を埋める。

### 9-1. 締め日の決定

`ClosingDateResolver.Resolve(year, month, closingDay)`（`src/bmcs_app.Domain/Calculations/`）が
`(対象年月, 締め日区分)`から実際の締め日（`DateOnly`）を求める。`closingDay = 99`は当月末日、
`closingDay`がその月の日数を超える場合（31日締めの2月等）も当月末日に丸める。`closingDay = 0`
（都度得意先）は締め対象外のため呼び出し不可（`ArgumentOutOfRangeException`）。

### 9-2. 集計期間 ― 売上と入金で下限の扱いが非対称

| | 下限 | 上限 | 二重集計を防ぐ手段 |
|---|---|---|---|
| 売上（`sales`） | なし | 締め日 | `billing_number IS NULL`（集計済みマーカー） |
| 入金（`receipt`） | 前回確定`billing`の締め日 + 1日 | 締め日 | 期間で区切る |

この非対称はデータモデルから必然的に導かれる。`receipt.billing_number`は「充当先の請求データ」
であり、Phase 7（入金入力）が**過去の**請求へ古い順に充当したときに設定される値のため、締め処理
の「集計済み」マーカーとして上書きすることができない。したがって入金は期間で区切るしかない。

売上に下限を設けないのは締め漏れを防ぐため。前回締め日より前の日付で後から登録された売上も、
未請求である限り次回の締めで必ず拾われる。

**残存リスク（既知・許容）**: 前回締め日より前の日付で**後から登録された入金**は、どの締めの
期間にも入らず永久に拾われない。対処は締め解除（6-2）→再締め。

前回確定`billing`＝同一`customer_code`／`billing_status`＝確定／`is_deleted`＝偽のうち
`closing_year_month`が最大のもの。存在しなければ前回残高0・入金の下限なし。

### 9-3. 金額の組み立て

```
current_billing_amount = previous_balance - receipt_amount + sales_amount + tax_amount
```

- `receipt_amount`は`receipt.receipt_amount`（伝票単位の値）を`receipt_slip_number`でまとめて
  伝票ごとに1件へ畳んだ後の合計（`docs/database-schema.md` 2.8節の「SUMしてはいけない」規則）。
- `sales_amount`／`tax_amount`／税率別内訳5カラムは9-4の税額計算から得る。

### 9-4. 税額計算 ― 既存の`ConsumptionTaxCalculator`を税単位で使い分ける

新しい計算ロジックは追加せず、5-1で用意済みのメソッドをそのまま使う。

| `tax_unit` | 使うメソッド |
|---|---|
| 請求単位 | `CalculateExternalTaxBuckets`→`ToSummary`（請求全体で(税種別,税率)ごとに1回だけ丸める） |
| 伝票単位 | `CalculateExternalTaxPerSlip`（伝票ごとに確定した税額を積み上げる。暫定C-4b） |
| 内税明細単位 | 対象外（`closing_day = 0`のCHECK制約により自然に除外。サービス側でも明示的に弾く） |

伝票単位は、再計算した伝票税額の合計が保存済み`slip_tax_amount`の合計と一致することを
検証する（不一致は`BillingClosingException`）。端数区分（`rounding_type`）は登録後変更不可
なので本来一致するはずであり、不一致はデータ異常を意味する。

返品・値引行（`slip_type`＝2／3）はマイナス金額のままそのまま含める（`ConsumptionTaxCalculator`
は5-1でマイナス対応済み）。

### 9-5. 二重締め防止 ― アプリ側とDB側の二段構え

**アプリ側**: 確定前に同一`customer_code`×`closing_year_month`の確定済み`billing`が無いこと
を確認する。あればその得意先をスキップする（理由付きで結果に含める）。あわせて、より新しい
`closing_year_month`の確定済み`billing`が既にある場合も拒否する（締め順序の逆転防止）。

**DB側**: フィルタ付き一意インデックス`UQ_billing_customer_closing_ym_confirmed`
（`ON billing (customer_code, closing_year_month) WHERE billing_status = 1 AND is_deleted = 0`。
`scripts/013_add_billing_confirmed_unique_index.sql`）。解除済み（`billing_status = 2`）は対象外
なので、締め解除→再締めで新番号を採番する運用（6-2）を壊さない。

### 9-6. 締め対象にしない得意先

- 対象売上・対象入金が無く、かつ前回残高も0 → `billing`を作らない（空の請求書を出さない）。
- 対象が無くても前回残高≠0 → 繰越請求として`billing`を作る。

### 9-7. 実装

- `src/bmcs_app.Application/Billing/BillingClosingService.cs`が本体。得意先ごとの集計を
  1つのprivateメソッドに集約し、`PreviewAsync`（保存しない読み取り専用の事前確認）・
  `ConfirmAsync`（同条件で再集計してから確定）の両方から呼ぶ（5-7と同じ「金額を出す経路を
  1本にする」方針）。`ConfirmAsync`はプレビュー結果を引数に取らない
  （プレビューと確定の間に他ユーザーが伝票を登録しても古い集計値で確定しないため）。
- トランザクション境界は`docs/architecture.md` 6章のとおり。伝票番号（`SlipNumberKind.Billing`）
  の採番が複数得意先分必要になるため明示トランザクションで包み、`SaveChangesAsync`は最後に
  1回だけ呼ぶ（6章が「請求締め」を明示トランザクションの例として挙げている想定どおり）。
- 画面（`Views/Billing/BillingClosingWindow.xaml`／`ViewModels/Billing/BillingClosingViewModel.cs`）
  は「締め日を指定して一括」処理する専用画面。対象年月・締め日区分（得意先マスタに実在する
  `closing_day`から選択）・請求日を指定し、「締め確定」で確定する。**プレビューは対象取得
  ボタンを持たず、画面表示時（既定条件＝当月・締め日区分の先頭）と条件変更時（対象年月・
  締め日区分）に自動で再取得する**（2026-09-11ユーザー確認）。保存を伴う確定操作のみボタン
  （F10）による明示操作にする。対象年月のテキストボックスは`UpdateSourceTrigger=LostFocus`
  にし、1文字入力するごとにDB照会が走らないようにしている。一覧はチェックボックスによる
  行選択を持たない（一括処理の方針上不要であり、既存画面にチェックボックス一覧のパターンが
  無いため新パターンを増やさない）。
- **締め確定後のリセット**（`docs/product-spec.md` UI/UX節の「登録後のリセット」を本画面に
  適用したもの。2026-09-11確定）: 「締め確定」成功後は対象年月・締め日区分・請求日の入力
  条件と結果一覧（一覧は締め結果を確認できる唯一の画面上の証跡だが、確定後は再検索する
  運用のため）を両方クリアし、画面表示直後の状態（既定条件の再取得待ち）に戻す。完了メッセージ
  （確定件数）はクリア後も画面上に残す。

### 検証方法

- 単体テスト: `ClosingDateResolverTests`（通常日・末日締め・日数超過の丸め・うるう年・
  `closing_day = 0`の例外）。
- 結合テスト: `tests/bmcs_app.Application.Tests/Billing/BillingClosingServiceTests.cs`。
  `ConfirmAsync`が内部で`BeginTransactionAsync`するため、`SalesServiceTests`と同じ
  「専用のテスト得意先で確定した後、finallyで物理削除する」方式を採る。seedの得意先
  （CUS001=20日締め／CUS002=末日締め／CUS003=都度）とは重ならない`closing_day = 15`の
  専用テスト得意先を新設し、seedデータには一切触れない。

---

## 10. 締め解除処理（Phase 6-2、2026-09-14実装）

確定済み`billing`を解除済（`billing_status = 2`）にし、紐付く`sales`行の`billing_number`を
`NULL`、`billing_status`（`BillingLinkStatus`）を未請求へ戻す。管理者権限のみの操作のため、
請求締め処理（9章）とは別画面（別ウィンドウ）として提供する（C-8・2026-09-10確定）。

### 10-1. 解除できる対象の制約 ― 締め順序の逆転防止と対になる制約

**解除できるのは、同一得意先の確定済み`billing`のうち`closing_year_month`が最も新しいものに
限る。** それより古いものを解除すると、より新しい確定済み`billing`が引き継いだ`previous_balance`
の参照元が失われ、9-2の前回残高チェーンが破綻する。判定は9-5と対称で、
「同一`customer_code`／`billing_status`＝確定／`is_deleted`＝偽のうち`closing_year_month`が
最大のもの」を求め、それが解除対象自身でなければ拒否する（`BillingReleaseException`）。

解除後は、その1つ前の確定済み`billing`が再び「最新の確定済み」になるため、連鎖的に古い方から
順に解除していくことができる。

その他の拒否条件:

- 指定した請求番号の`billing`が存在しない（`is_deleted`を除く）。
- 既に解除済み（`billing_status = 2`）。

### 10-2. 実装

- `src/bmcs_app.Application/Billing/BillingReleaseService.cs`が本体。`GetByNumberAsync`
  （画面表示用の読み取り専用取得）と`ReleaseAsync`（解除の確定）を持つ。`ReleaseAsync`は
  9章の`ConfirmAsync`と同じく明示トランザクションで包み、`billing`本体と紐付く`sales`行を
  同一トランザクション内で更新する。
- Phase 7（入金・消込）が未実装のため、`receipt.billing_number`（充当先）がこの請求番号を
  指しているケースは現時点では発生しない。Phase 7実装時は、充当済みの`billing`を解除して
  よいかどうかを別途検討する必要がある（本タスクのスコープ外）。
- 画面（`Views/Billing/BillingReleaseWindow.xaml`／`ViewModels/Billing/BillingReleaseViewModel.cs`）
  は一覧を持たず、得意先・商品マスタと同じ「請求番号を直接入力してEnterで読み込む」方式。
  読み込んだ内容（得意先・税区分・締め年月・前回残高・入金額・売上額・消費税・今回請求額・
  状態・確定/解除の日時と実施者）を読み取り専用で表示し、「解除実行」（F8）で確定する。
  取消系の操作のため、実行前に得意先マスタの無効化と同様の確認ダイアログ（Yes/No）を挟む。
  解除成功後は画面を起動直後の状態へ戻す（`docs/product-spec.md` UI/UX節「登録後のリセット」）。
- 権限判定（管理者権限のみ）は、C-8の方針どおりメニュー単位（Phase 2-7、`scripts/014_seed_menu_structure.sql`で本画面を権限レベル9に設定）で行う。画面内アクション単位の権限チェックは持たない。

### 検証方法

- 結合テスト: `tests/bmcs_app.Application.Tests/Billing/BillingReleaseServiceTests.cs`。
  解除後の状態遷移（`billing`／`sales`両方）、二重解除の拒否、締め順序が逆転するケースの拒否、
  存在しない請求番号の拒否に加え、**完了条件「解除→再締めで金額が一致する」を、解除後に
  同条件で`BillingClosingService.ConfirmAsync`を再実行し金額が一致することで直接検証**している。
- 実機確認: メインメニューの「締め解除処理」ボタンから画面を開き、UI Automation経由で
  請求番号入力→Enter読込→得意先名・税区分・金額・状態が正しく表示されることを確認済み
  （既存のseedデータ`BIL_INV001`で確認。解除操作自体は結合テストで検証済みのため、
  共有のseedデータを実機操作で変更することは避けた）。

---

## 11. 明細請求書発行（Phase 6-3、2026-09-14実装）

都度得意先（`tax_unit = 3` 内税明細単位）の未請求かつ消込完了でない売上明細行を数件選び、
`detail_invoice`／`detail_invoice_sales_line`へ確定する。`detail_invoice`テーブル・EFエンティティ・
採番系列（`SlipNumberKind.DetailInvoice`）はPhase 1で作成済みだったが、そこへ書き込む経路が
一つも無かった欠落を本タスクで埋める。

### 11-1. 対象条件 ― 連携テーブルの存在が唯一の正

共通業務ルール2（`docs/product-spec.md`）のとおり、対象条件は**未請求かつ消込完了でない**
売上明細行（`tax_unit = 3`）。「未請求」の判定は`sales.billing_status`（キャッシュ）ではなく
**`detail_invoice_sales_line`に連携行が存在しないこと**で行う。締め請求（`billing_number`）とは
異なり、明細請求は連携テーブルの有無が唯一の正であり、`billing_status`はその写しに過ぎないため
（万一両者が食い違っても、連携行が無い行は候補に出て再請求でき、自己修復になる）。

この抽出条件は`DetailInvoiceService.BuildCandidateQuery`（private）に1本化し、画面表示用の
`GetCandidatesAsync`と、発行時にトランザクション内で再確認する`IssueAsync`の両方から使う
（9章「金額を出す経路を1本にする」と同じ方針）。

### 11-2. 税額計算・二重請求防止

新しい計算ロジックは追加せず、5-1で用意済みの`ConsumptionTaxCalculator.CalculateInternalTaxPerLine`
（XMLコメントに「明細請求書用」と明記済み）をそのまま使う。返品行（`slip_type`=2）を含めても
マイナス金額のまま計算に含まれ、5-1の符号対称な端数処理により元の売上の税額をちょうど打ち消す。

発行時は、再計算した明細行ごとの税額合計が保存済み`sales.tax_amount`の合計と一致することを
検証する（9-4が伝票単位で行っている検証と対称。端数区分は登録後不変のため、本来一致するはず
のデータ異常を検出する）。

二重請求防止は`detail_invoice_sales_line`のDB側UNIQUE制約（`UQ_detail_invoice_sales_line_sales_line`）
とアプリ側の事前チェック（11-1の対象条件クエリを発行直前にトランザクション内で再実行）の二段構え。
`detail_invoice_sales_line`はrowversionを持たない（行の追加・削除のみで更新が無いテーブルのため）
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
- 一覧を持たず、明細請求書No.を直接入力してEnterで既存分を読み込む方式（得意先／商品マスタ・
  締め解除処理と同じコード直接入力方式。2026-09-14ユーザー確認）。既存分を読み込んだ場合は
  読み取り専用表示にする。`detail_invoice`は「発行済／取消」の2状態で訂正の概念が無いため
  （訂正はPhase 6-4の取消→再発行で行う想定）。
- 宛名（`addressee_name`）は得意先コード確定時に得意先名を初期値として転記し、手入力で
  上書き可能にする（C-9・2026-09-10確定。他のジャーナル系画面と同じ「名称欄を直接書き換える」
  方式だが、`detail_invoice`は`customer_name`（得意先マスタのスナップショット）と
  `addressee_name`（印字用宛名）を別カラムで持つため、本画面では宛名専用の入力欄として分離した）。
- 発行成功後は完了メッセージを出して画面を起動直後の状態に戻す（`docs/product-spec.md` UI/UX節
  「登録後のリセット」）。
- 削除（Phase 6-4の取消）・印刷（Phase 10-3／10-5の帳票基盤・請求書実装）は本タスクの範囲外の
  ため、ツールバーに枠のみ用意し常に無効化する（2026-09-14ユーザー確認。4-3の受注入力画面と
  同じ扱い）。
- デモ（`bmcs_app.LineInvoice`）にあった前後の請求書ナビゲーション・登録件数表示・請求書検索
  モーダル・上書き保存（Upsert）は採用しなかった（2026-09-14ユーザー確認）。理由は前項および
  「一覧を持たずコード直接入力」という既存画面の統一パターンを優先したため。

### 11-4. seedデータの不整合修正

`scripts/seed_dev_data.sql`のCUS003返品行（`SALLIN004`）の`tax_amount`が`-81.00`と記録されて
いたが、正しくは`-82.00`（CUS003の端数区分=切上。`-1100×8÷108=-81.4815…`を符号対称な切上で
丸めると`-82.00`になる）。本タスクの実装検証（11-2の税額一致チェック）で発見し、seed側を修正した
（4-2でのCUS003単価不整合修正と同じ扱い。スキーマ変更を伴わないため`scripts/`への連番SQLは
追加していない）。

### 検証方法

- 結合テスト: `tests/bmcs_app.Application.Tests/Billing/DetailInvoiceServiceTests.cs`（8件）。
  完了条件「対象条件が明細行単位で正しく効いている」を、未請求・請求済（連携あり）・消込完了・
  削除済・他得意先の5パターンを1テストで揃えて直接検証。加えて税率混在（標準10%／軽減8%）の
  内訳計算、返品行を含めた打ち消し、二重請求・消込完了行・0件・内税明細単位以外の得意先を
  指定したときの例外を検証。専用のテスト得意先（`__TSTDIV1`／`__TSTDIV2`）で発行後、
  finallyで物理削除する方式（`BillingClosingServiceTests`と同じ。`IssueAsync`が内部で
  `BeginTransactionAsync`するため）。
- 実機確認: メインメニューの「明細請求書発行」ボタンから画面を開き、UI Automation経由で
  得意先コード`CUS003`を入力→Enter照会→取込候補が`SALLIN001`・`SALLIN004`の2行のみ
  （`SALLIN002`＝請求済、`SALLIN003`＝消込完了は表示されない）であり、金額・消費税・
  得意先名・宛名の初期値がすべて正しく表示されることを確認済み。発行操作自体（保存を伴う）は
  共有のseedデータを実機操作で変更することを避け、結合テストで検証済みの内容に委ねた
  （10章の締め解除処理と同じ判断）。

## 12. 明細請求書の取消（Phase 6-4、2026-09-14実装）

11章（発行）と対になる、明細請求書の取消。別画面にはせず、11章の画面（`DetailInvoiceIssueWindow`）
の「削除 (F8)」（枠のみ用意済みだった）に配線した（2026-09-14ユーザー確認）。C-8が別画面分離を
要求しているのは締め解除・月次締め解除（管理者権限のみの操作）に限られ、明細請求書の取消は
対象に含まれないため。

### 12-1. 連携行は物理削除する

`docs/database-schema.md` 2.14節のとおり、取消では`detail_invoice_sales_line`の該当行を
**物理削除**する（このテーブルが`row_version`を持たないのは行の追加・削除しか発生しない前提
のため）。ヘッダー（`detail_invoice`）は物理削除せず`invoice_status`を取消済（`2`）にし
`cancelled_at`／`cancelled_by`を立てるだけに留める（締め解除と同じ非破壊方式）。

連携行を削除すると、`DetailInvoiceService.BuildCandidateQuery`（11-1節）の
「どの明細請求書にも連携していない」という条件が自動的に真に戻るため、対象の売上明細行は
何もしなくても次回の候補に再び現れる。`UQ_detail_invoice_sales_line_sales_line`も解放される
ため、同じ行を新しい明細請求書へ再発行できる（完了条件「取消後に同じ売上を再度請求できる」）。
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
   Phase 7-4（明細入金）は本タスク時点では未実装だが、テーブル・FK
   （`FK_detail_receipt_detail_invoice`）・開発DBのseedデータ（`DRC002`→`DIV001`）は既に
   存在する。取消すると入金の充当先が宙に浮くため、7-4の実装を待たずにここで塞ぐ
   （7-4着手時に本チェックの妥当性を再確認する）。

排他制御はヘッダーの`RowVersion`（`DetailInvoice : AuditableEntity`）に委ね、`SaveChangesAsync`の
`DbUpdateConcurrencyException`を`DetailInvoiceException`へ変換する。複数明細行の伝票向けの
`SlipConcurrencyGuard`（`docs/architecture.md` 9章）は、連携行が単純な追加・削除しかしない
本ユースケースには使わない。

### 12-3. 実装

- `DetailInvoiceService.CancelAsync`（11章の`IssueAsync`と同じクラス。DI登録済みのため追加登録は
  不要）。ヘッダー取得→取消済みチェック→明細入金ガード→連携行・売上行取得→消込済みチェック→
  ヘッダー更新・連携行削除・売上行の`BillingStatus`復帰→保存、の順で明示トランザクション内で行う。
- 画面（`DetailInvoiceIssueViewModel`）: 既存分読込時に`LoadedInvoiceStatus`を保持し、
  取消(F8)は`IsExistingLoaded && LoadedInvoiceStatus == Issued`のときだけ有効（取消済み・新規時は
  無効）。実行前に`MessageBox`でYes/No確認（締め解除と同じ取消系操作の既定パターン）。
  成功後は画面を起動直後の状態に戻す（`docs/product-spec.md` UI/UX節「登録後のリセット」）。

### 検証方法

- 結合テスト: `tests/bmcs_app.Application.Tests/Billing/DetailInvoiceServiceTests.cs`に追加した
  5件。完了条件「取消後に同じ売上を再度請求できる」を、発行→取消→候補への再出現→再発行
  （別番号が採番される）まで一続きで直接検証。加えて二重取消・存在しない番号・消込済み行を
  含む場合・明細入金が充当されている場合の拒否を確認。
- 実機（開発用ライブDB）: 発行済みのseedデータ`DIV001`を読込むと「削除 (F8)」が有効になり、
  実行すると連携先`SALLIN002`の消込完了と、`DIV001`を指す明細入金`DRC002`の両方を理由に
  拒否されることを確認した（12-2の2条件が実データで両方成立するケース）。取消済みの
  seedデータ`DIV002`を読込むと「削除 (F8)」が無効であることも確認した。

---

## 13. 請求フェーズのレビュー（Phase 6-5、2026-09-14実施）

完了条件「締め・解除・発行・取消の全組み合わせで整合が保たれる」について、9〜12章
（Phase 6-1〜6-4）の実装をレビューした。個別の機能は結合テストで検証済みだが、**機能間の
組み合わせ**と、**請求と売上訂正・取消（Phase 5-6・8章）の相互作用**は未検証だったため、
本レビューではこの2点を対象にした。

### 13-1. 組み合わせの棚卸し ― 締め請求と明細請求は構造的に分離している

締め得意先（`tax_unit`＝請求単位／伝票単位。6-1・6-2が対象）と都度得意先（`tax_unit`＝内税明細
単位。6-3・6-4が対象）は`customer.tax_unit`（登録後不変）で完全に分離され、`sales.tax_unit`も
これをスナップショットする。`BillingClosingService.BuildCandidatesAsync`は
`TaxUnit.Invoice || TaxUnit.Slip`の得意先のみを対象にし、`DetailInvoiceService.BuildCandidateQuery`
は`TaxUnit.Line`のみを対象にするため、**同じ売上明細行が締め請求と明細請求の両方に載る経路は
構造的に存在しない**。`CK_customer_tax_unit_closing_day`（内税明細単位⇔`closing_day=0`）により、
都度得意先が締め処理の対象得意先抽出条件（`closing_day`一致）に紛れ込むこともない。

締め順序（9-5）と解除順序（10-1）は対称な判定で、前回残高チェーンが破綻しない。二重締めは
アプリ側チェック＋フィルタ付き一意インデックス、二重請求はアプリ側再確認＋UNIQUE制約の
二段構えで、いずれもDB側の最終防衛線がある。`receipt_amount`の伝票単位重複計上も
`GroupBy(ReceiptSlipNumber).First()`で回避済み（既存テストで確認済み）。

### 13-2. 発見した問題1 ― 明細請求書発行済みの売上が編集ロックされていなかった（修正済み）

売上の編集ロック（C-6・`SalesEditLockEvaluator`）は①請求締め（`billing_number`）②月次締め
③消込完了の3条件のみで、「都度得意先の明細請求書発行自体はロック条件に含めない」としていた
（`docs/product-spec.md`共通業務ルール5、旧記述）。しかし`tax_unit=3`の`billing_number`は
`CK_sales_billing_number_by_tax_unit`により常にNULLのため、①は都度得意先には決して発火せず、
**明細請求書を発行済みでも売上入力画面から自由に訂正・取消できてしまっていた**。結果:

- `detail_invoice`ヘッダーの確定金額（スナップショット）が実データと乖離する。
- 訂正で行を論理削除しても`detail_invoice_sales_line`の連携行は残り、`IsDeleted=true`の売上を
  指し続ける（`DetailInvoiceService.GetByNumberAsync`は`!IsDeleted`で絞っていないため、その行を
  明細として表示し続ける）。

**対応（C-6改訂・2026-09-14ユーザー確認）**: 編集ロックに4つ目の条件「明細請求書発行済み
（いずれかの明細行が`detail_invoice_sales_line`に連携している）」を追加した。判定順は
①請求締め→①'明細請求書発行済→②月次締め→③消込完了（`SalesEditLockEvaluator.Evaluate`、
`docs/product-spec.md`共通業務ルール5・`docs/database-schema.md` 2.0節を合わせて改訂）。
DBアクセス（`detail_invoice_sales_line`の存在確認）は`SalesEditLockService.EvaluateAsync`に
追加し、判定ロジック自体は純粋関数のまま維持した。`SalesService.UpdateAsync`／`CancelSlipAsync`
は`SalesEditLockService`経由のため呼び出し側の変更は不要で、訂正後の再判定にも自動的に効く。

### 13-3. 発見した問題2 ― 締め・解除がEF例外を業務例外へ変換していなかった（修正済み）

`docs/architecture.md`は「`DbUpdateConcurrencyException`はApplication層で捕捉し、ViewModelに
EF Coreの例外型を漏らさない」と規定しているが、`BillingClosingService.ConfirmAsync`・
`BillingReleaseService.ReleaseAsync`は`SaveChangesAsync`を無防備に呼んでおり、同時実行時の
`DbUpdateException`（二重締めのユニーク制約違反）・`DbUpdateConcurrencyException`（rowversion
競合）が生のまま伝播していた。`DetailInvoiceService`（6-3・6-4）は`IssueAsync`／`CancelAsync`の
両方で既に変換済みで、非対称だった。

**対応**: 両サービスの`SaveChangesAsync`をtry/catchで包み、それぞれ`BillingClosingException`／
`BillingReleaseException`へ変換した（`DetailInvoiceService`と同じ形）。両例外クラスを
`DetailInvoiceException`と同じ`(string message, Exception? inner = null)`シグネチャに拡張した。

あわせて、請求系3画面のViewModel（`BillingClosingViewModel`・`BillingReleaseViewModel`・
`DetailInvoiceIssueViewModel`）は業務例外だけをcatchし`catch (Exception)`のフォールバックを
持たない不整合があった（`SalesEntryViewModel`／`OrderEntryViewModel`は持つ）ため、同じ形の
フォールバックを追加した。`BillingClosingViewModel`の`_ = PreviewAsync()`と
`DetailInvoiceIssueViewModel`の`_ = ApplyCustomerAsync(...)`（いずれもfire-and-forget）は
例外が`TaskScheduler.UnobservedTaskException`（ログのみ・UI通知なし）に落ち一覧が黙って古いまま
になる経路だったため、各メソッド内部にtry/catchを足して塞いだ（呼び出し側の構造は変更していない）。

### 13-4. 残存リスク（既知・許容、対応しない）

- **入金の下限（9-2の再掲）**: 前回締め日より前の日付で後から登録された入金は、どの締めの期間
  にも入らず永久に拾われない。対処は締め解除（6-2）→再締め。
- **`sales.billing_status`とリンクの一致にDB側の防波堤が無い（新規確認・対応しない）**:
  `billing_status`（請求済／未請求）と実際の紐付け（`tax_unit`1/2は`billing_number`、
  `tax_unit=3`は`detail_invoice_sales_line`の存在）の一致は、アプリのトランザクションだけが
  担保しており、DB側のCHECK制約は無い。二重請求側（`UQ_detail_invoice_sales_line_sales_line`）・
  二重締め側（`UQ_billing_customer_closing_ym_confirmed`）はDB側の最終防衛線を持つのと非対称だが、
  `tax_unit=3`側は連携テーブルの存在確認が必要でCHECK制約として書けないため、`tax_unit`1/2側だけ
  追加しても非対称が残る。2026-09-14ユーザー確認により、DB制約は追加せずレビュー結果としてここに
  記録するのみとした。

### 検証方法

- 単体テスト: `SalesEditLockEvaluatorTests`に条件1'（明細請求書発行済み）を追加（既存7件＋新規2件）。
- 結合テスト: 新規`tests/bmcs_app.Application.Tests/Billing/BillingPhaseReviewTests.cs`（6件）。
  締め請求と明細請求が互いの対象を拾わないこと、締め→解除→売上訂正→再締めが二重計上せず
  訂正後の金額で確定すること、発行→取消→売上訂正→再発行も同様に訂正後の金額になること、
  請求締め済み・明細請求書発行済みの売上がそれぞれ訂正・取消を拒否されること（後者は取消後に
  再び編集できることも含む）、締め→解除→再締めを繰り返しても確定済み`billing`が常に1件だけ
  であることを検証。専用のテスト得意先（`__TPHR01`〜`__TPHR07`、`closing_day = 17`）で実行後、
  finallyで物理削除する方式（既存の`BillingClosingServiceTests`等と同じ）。全体テスト
  （Domain 205件／Application 68件）すべてgreen。実行後にテスト用の行が
  `customer`／`sales`／`billing`／`detail_invoice`／`detail_invoice_sales_line`に残っていないこと
  を`sqlcmd`で確認済み。
- 実機確認: 本レビューはコードレビューと結合テストが中心のため、GUI操作による実機確認は
  行っていない（ビルド成功と自動テストのみで検証）。

---

## 14. メインメニュー画面（Phase 2-7、2026-09-14実装。0-7を先行実装）

デザインモック（旧`Views/Menu/MainMenuMockWindow.xaml`、A/B/Cの3案を比較する検討専用画面）から
**C案＝リスト・アコーディオン型を採用**（ユーザー指示）。ウィンドウは横長のモック（1240×820）から
**縦長（440×820、`MainMenuWindow.xaml`）に変更**した（ユーザー指示）。方針が確定したため
モック画面自体は削除し、実装（メニュー構成マスタ駆動）に一本化した。

### 14-1. 前提: Phase 0-7 を本タスクで先行実装

2-7の完了条件「権限レベルの異なる社員コードで起動すると表示メニューが変わる」を満たすには
起動時パラメータの受け取り（Phase 0-7、未着手）が必要だったため、ユーザー確認のうえ0-7を
本タスクに含めた。`ICurrentEmployeeContext`の実装を`PlaceholderCurrentEmployeeContext`（固定値）
から`StartupArgsCurrentEmployeeContext`（ショートカット引数の1つ目を社員コードとして使う。
引数なしは開発用に`EMP001`へフォールバック）へ差し替えた。詳細は`docs/architecture.md` 14章。

### 14-2. メニュー構成マスタは画面から編集しない

「メニュー構成マスタ＋メインメニュー画面」というタスク名だが、完了条件にメニュー項目自体の
登録・編集操作は含まれていないため、**メニューマスタの編集用CRUD画面は作らない**（曖昧さの解釈。
他マスタと違い、必要な項目は実装フェーズごとに`scripts/`のSQLへ直接追記する運用とした）。
実データは`scripts/014_seed_menu_structure.sql`が投入する（開発用テストデータの
`scripts/seed_dev_data.sql`とは分離。menuは実運用でも使う本物の構成データのため）。

### 14-3. 階層・権限フィルタの実装

- `MenuTreeBuilder`（Domain/Calculations、純粋関数）が`menu`の全行と社員の権限レベルから
  「カテゴリ（親）→表示可能な機能（子）」の階層を組み立てる。子の
  `required_permission_level ≦ 社員の権限レベル`のものだけを残し、表示できる子が1件も無い
  カテゴリはメニュー自体を表示しない（単体テスト`MenuTreeBuilderTests`4件）。
- `MenuService.GetMenuTreeAsync`（Application/Master）が`menu`全行（論理削除除く）を取得する
  だけで、フィルタは行わない（フィルタはDomain層の責務）。
- `MainMenuViewModel.LoadAsync`が`ICurrentEmployeeContext.EmployeeCode`から
  `EmployeeService.GetByCodeAsync`で社員名・権限レベルを取得し、上記フィルタを適用して
  `Categories`（画面表示用の`MenuCategoryDisplayItem`/`MenuItemDisplayItem`）を組み立てる。
  社員コードが見つからない場合は権限レベル0として扱い、警告メッセージを表示する
  （メニューが1件も表示されない状態になる。実機確認済み）。
- 画面遷移は`screen_key`文字列を`switch`する`OpenMenuItemCommand`に一本化した
  （個別の`OpenXxxCommand`は全廃）。未知の`screen_key`は警告メッセージを表示し、
  例外にしない（`menu`データの入力誤りに対する防御）。
- カテゴリの色分け（アコーディオン見出しの左バー・ホバー色）は表示順に応じた固定パレットを
  循環させる。ViewModelはWPFのMedia型（Brush）を持たず整数インデックス（`ColorIndex`）のみを
  持ち、View側の`MenuAccentColorConverter`がインデックス→Brushへ変換する
  （ViewModelをWPF依存にしない、既存の`EnumDisplayConverter`と同じ方針）。

### 14-4. 実装済み画面のみを対象にする

未実装フェーズ（入金・元帳・月次締め・データ検索等）の画面はメニューに追加していない。該当
フェーズの実装時に`scripts/014_seed_menu_structure.sql`へ追記する。締め解除処理（C-8で権限差を
別画面として表現する対象）と社員マスタ・自社情報（機微な情報のため）は権限レベル9（管理者専用）、
それ以外は1（一般）とした。数値の重み付けは暫定の解釈であり、`scripts/014_seed_menu_structure.sql`
の値を書き換えるだけで調整できる（コード変更は不要）。

### 検証方法

- 単体テスト: `MenuTreeBuilderTests`（4件、権限フィルタ・カテゴリ非表示・表示順を検証）。
- 実機確認（UI Automation）: `EMP001`（権限レベル1）起動時は一般項目のみ、`EMP002`
  （権限レベル9）起動時は締め解除処理・社員マスタ・自社情報を含む全項目が表示されることを確認。
  存在しない社員コード（`NOSUCH`）起動時は権限レベル0・警告メッセージ・メニュー非表示を確認。
  起動時引数なしでは開発用フォールバック（`EMP001`）が効くことを確認。全メニュー項目
  （10画面）をクリックしてそれぞれ対応する画面が開くことを確認済み。

---

## 15. ジャーナル系画面の伝票No入力欄の挙動を仕様化・実装（2026-09-14実装）

`docs/product-spec.md` UI/UX節に「ジャーナル系画面の伝票No入力欄の挙動」を追記し、実装済みの
3画面（売上入力・受注入力・明細請求書発行）を合わせた。入金入力は当時未実装のため対象外だったが、
Phase 7-2実装（2026-09-15、17章）でNo欄のSpace検索・Enter読込・フォーカス遷移が同じパターンで
実装され、2026-09-15に画面レイアウトも他3画面へ合わせて「伝票No（左）→日付（右）」に統一した。
対象は現在4画面。

### 15-1. 既存実装の不具合

売上入力・受注入力は伝票No欄の既定値が`"（自動採番）"`という**実テキスト**だった
（Watermarkは別途「自動採番（Space検索）」を設定済みだったが、実テキストに隠れて効いていなかった）。
このため起動直後に何も入力せず`Enter`を押すと、その文字列自体を伝票Noとして検索し
「売上No.「（自動採番）」が見つかりません。」という誤ったエラーになっていた。両画面とも
既定値を`string.Empty`に変更した。

明細請求書発行の`LookupAsync`は、番号が見つからない場合に`ClearForm()`を呼んで入力済みの
得意先・宛名・明細を全部破棄していた。仕様「該当Noが無ければエラー表示のみ（新規登録モードへは
進まない）」に反するため、`ClearForm()`呼び出しを削除しエラーメッセージ表示のみにした。

### 15-2. フォーカス移動は ViewModel 起点の1機構に統一

`EnterKeyNavigationBehavior`（Enterをタブ相当として扱う既存の添付ビヘイビア）は変更していない。
「Enterに対する`KeyBinding`を持つTextBoxでは譲る」というガードにCanExecute判定を足す案を検討したが、
`[RelayCommand]`が生成する`AsyncRelayCommand`は既定で実行中`CanExecute()`がfalseを返すため、
非同期Lookupの実行中にEnterを二度押すとフォーカスが勝手に飛ぶ誤爆が起きる。しかもこのビヘイビアは
14画面に適用済みで、得意先コード欄・商品コード欄など無関係な欄にまで波及するため却下した。

代わりに`ViewModelBase`へ`FocusRequested`イベント（`RequestFocus(string focusKey)`）を追加し、
`Behaviors/InitialFocusBehavior.cs`を`FocusBehavior.cs`にリネームして`FocusKey`添付プロパティを
持たせた。伝票No欄で空欄`Enter`／読込成功の両方から同じ`RequestFocus(key)`を呼び、対応する
`FocusKey`を持つ入力欄（売上入力→`"SlipDate"`、受注入力→`"OrderDate"`、明細請求書発行→
`"IssueDate"`）へフォーカスを移す。3画面ともXAMLの宣言順が「日付→伝票No」のため、
`MoveFocus(FocusNavigationDirection.Next)`では日付欄に戻れない（タブ順の都合で別の欄に飛ぶ）。
明示的なキー指定にしたのはこのため。

売上入力の受注No.欄（受注からの売上確定、TODO.md 5-3）も同じパターンとし、
`FocusKey="CustomerCode"`（得意先コード欄）へ移動する。

**例外**: 明細請求書発行で既存分を読み込んだ場合（`IsExistingLoaded = true`）は請求日付・得意先・
宛名がすべて`IsReadOnly`になるため、フォーカスを請求書No欄に留める（`RequestFocus`を呼ばない）。

### 15-3. 明細請求書の検索モーダルを新規追加

明細請求書発行画面には`Space`キーでの伝票検索ダイアログが存在しなかった。既存の汎用検索モーダル
（`Views/Common/SlipSearchDialog.xaml`）に相乗りする形で追加した。

- `SlipSearchTarget`に`DetailInvoice`を追加。
- `DetailInvoiceService`（採番・発行・取消のコマンドサービス）に検索を足すと`SlipNumberService`等の
  依存一式が付いてくるため、`SalesQueryService`/`OrderQueryService`と対称な
  `DetailInvoiceQueryService.SearchAsync`を新設した（読み取り専用）。
- 都度得意先は学校のクラス・先生単位など宛名（`AddresseeName`）で識別することが多い
  （C-9・2026-09-10確定）ため、**宛名でも検索できるようにした**。`SlipSearchItem`の得意先名欄に
  得意先名と宛名が異なる場合`"得意先名（宛名）"`の形で併記し、既存のキーワード一致ロジックを
  変更せずに宛名検索を成立させている。
- 取消済み（`DetailInvoiceStatus.Cancelled`）も検索結果に含める（`GetByNumberAsync`と同じ扱い）。
- `EnumDisplayConverter`に`DetailInvoiceStatus`の日本語表示（発行済／取消済）を追加した
  （既存は無変換で英語がそのまま表示されていた）。
- 明細請求書発行画面のルート`Grid`に`EnterKeyNavigationBehavior.IsEnabled="True"`を追加した
  （他の2画面と揃え、請求日付欄でEnter確定できるようにする副次効果もある）。

### 15-4. 今回は直さなかった課題

**受注入力画面は既存受注を読み込むと`IsSaved = true`で保存だけ封じるが、各入力欄は編集可能な
まま**（読み取り専用表示という説明はコメント上のみで、`IsReadOnly`バインドが実装されていない）。
今回のフォーカス移動追加により、この「編集できるのに保存できない」状態へ利用者を誘導する形になる。
表示専用化（各TextBoxへの`IsReadOnly="{Binding IsSaved}"`等の追加）は本タスクの範囲外としたため、
別タスクで対応する。

### 検証方法

- 単体テスト: `bmcs_app.Application.Tests`に`DetailInvoiceQueryServiceTests`を追加
  （宛名検索・取消済みが結果に含まれることを開発用ライブDBで確認）。既存209+70件は無影響
  （全279件成功）。
- ビルド: `dotnet build`が警告・エラーなしで通ることを確認（`FocusBehavior`リネームのXAML参照
  漏れが無いこと含む）。
- 手動確認: 3画面それぞれで、起動直後の伝票No欄フォーカス／空欄`Enter`での次項目移動／
  存在する伝票Noの読込と移動先／存在しない伝票Noでのエラー表示（データを消さないこと）／
  `Space`での検索モーダル起動、を確認する（自動UIテストが無いため今後の実機確認が必要）。

---

## 16. 入金消込サービスの実装（Phase 7-1、2026-09-14実装）

`Receipt`/`DetailReceipt`エンティティ・DDL・採番系列はPhase 1で用意済みだったが、書き込む
コードが一つも無く、`sales.settlement_status`/`settled_amount`は常に未消込／0のままだった
（4-4の`order_slip`と同じ「テーブルはあるが導出ロジックが無い」欠落）。Phase 4-4（受注状態
遷移）と同じ前例に従い、**本タスクはサービス層＋テストのみ**。画面（7-2 入金入力・7-4 明細
入金・7-5 入金の取消訂正）は後続タスクで別途実装し、UI配線・実機確認は行わない。

### 16-1. 決定事項（ユーザー確認済み・2026-09-14）

1. **消込対象額は売上明細行の`amount`をそのまま使う。** 請求単位・伝票単位（外税）は税抜
   金額、内税明細単位は税込金額（`amount`が既に税込）であり、消費税分は明細行レベルの
   消込には載せない。例: 明細6,000円＋4,000円＝10,000円の請求（税1,000円）に全額11,000円
   入金すると両行とも消込完了になり、残1,000円（税額分）は行に載らない。
2. **振込手数料差額（`fee_adjustment_amount`）は消込済金額に含める。** 売上明細行への
   充当額は`allocated_amount + fee_adjustment_amount`。入金側自身の充当状態
   （`AllocationStatus`）の判定には含めない（実際に受け取った現金の消化状況を表すため）。
3. **締め入金（`receipt`）は`billing`単位で充当するが、キャッシュ列は`sales`明細行にある。**
   `billing_number`へ充当した額を、その`billing_number`を持つ売上明細行へ伝票日付→伝票
   番号→行番号の古い順に配分する。
4. **既存の取り残しバグ2件を本タスクの範囲に含めて修正する**（16-7参照）。

### 16-2. アーキテクチャ: デルタ方式ではなく「得意先スコープの再計算方式」

完了条件は「登録・取消・訂正のいずれでもキャッシュ列が実態と一致する」であり、4-4の
`OrderStatusService`と同じデルタ方式（差分だけを適用する）ではドリフトを許す（訂正で
充当先自体が変わるケースを追跡しきれない）。代わりに`SettlementService`は**入金データ
（`receipt`／`detail_receipt`）から得意先単位で毎回全件再計算して書き戻す**。

スコープを「伝票」ではなく「得意先」にした理由:

- `customer_code`は`sales`/`receipt`/`detail_receipt`いずれも伝票単位の値で、訂正でも
  変わらない。訂正で充当先（`billing_number`等）が変わった場合、変更前・変更後の両方の
  グループを自動的に再計算できる（差分追跡が不要になる）。
- 都度得意先の「直接指定（`target_type=1`）」と「明細請求書経由（`target_type=2`）」の
  合算が、得意先の全売上・全明細入金が同時に視界に入ることで単純なローカル計算になる。
- 7-6（消込整合性レビュー）が同じ関数を「実態」の定義として再利用できる。

公開メソッドは`SettlementService.RecalculateForCustomerAsync(string customerCode)`の
1本のみ。得意先の`TaxUnit`で締め得意先向け（`RecalculateClosingAsync`）・都度得意先向け
（`RecalculateDetailAsync`）に内部分岐する。

### 16-3. 配分アルゴリズム（返品・値引の符号を正しく扱う）

対象額（`amount`）はマイナス（返品・値引行）を含みうる。素朴な「マイナス行を先に全額
充当し、その分を残額に戻す」方式は、入金が全く無い（`pool = 0`）場合でも返品行だけが
消込完了になる事故を起こす（`SalesEditLockEvaluator`の編集ロック条件4・
`DetailInvoiceService`の明細請求書候補除外に波及する実害がある）。

`SettlementAllocator.Allocate`（`src/bmcs_app.Domain/Calculations/`）は次の2分岐で、
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
消込キャッシュ列の保存）。`docs/architecture.md` 6章の明示トランザクションの許容ケース
に4番目として追記した。`SettlementService`自身は`BeginTransactionAsync`/`CommitAsync`を
呼ばず、呼び出し元が開始した明示トランザクションに参加する（`OrderStatusService`と同じ
構成）。明示トランザクションが開始されていない場合は`InvalidOperationException`を投げる
（`SlipNumberService.NextAsync`と同じアサーション）。

キャッシュ再計算は**値が実際に変わった行だけ**をModifiedにする（`SlipConcurrencyGuard.
TouchAll`とは逆方針。`TouchAll`は編集中の1伝票に対する意図的な照合強制だが、再計算は
得意先の全行に及ぶ派生更新であり、全行を対象にすると無関係な伝票を編集中の別ユーザーを
不要に弾いてしまう。`docs/architecture.md` 9章に追記）。

### 16-6. 名前空間の衝突に関する注意（実装時に発見）

`docs/architecture.md` 10章の機能フォルダ規約に従い`SettlementService`は
`src/bmcs_app.Application/Receipt/`（名前空間`bmcs_app.Application.Receipt`）に置いた。
これにより、**`bmcs_app.Application.*`名前空間配下（`Application`本体・`Application.Tests`
の両方）で`using bmcs_app.Domain.Entities;`により`Receipt`型を裸で参照しているコードが、
名前空間`bmcs_app.Application.Receipt`と衝突してコンパイルエラーになる**（C#は enclosing
namespace のメンバーを using 導入の型より優先して解決するため）。既存の
`BillingClosingServiceTests.cs`がこれに該当し、`using ReceiptEntity = bmcs_app.Domain.
Entities.Receipt;`のエイリアスへ変更して解消した。**今後`bmcs_app.Application.*`配下に
ファイルを追加する際、エンティティ`Receipt`を裸で参照しないこと**（`ReceiptEntity`等の
エイリアスを使う。`SettlementService.cs`・`SettlementServiceTests.cs`は既にこの形）。

### 16-7. 既存の取り残しバグ2件の修正

レビューで発見し、本タスクの範囲に含めて修正した（2026-09-14ユーザー確認）。

1. **締め解除後に売上行の消込キャッシュが取り残される。** `BillingReleaseService.
   ReleaseAsync`は`sales.billing_number`をNULLに戻すが、`settlement_status`/
   `settled_amount`は触れておらず、解除前が消込完了のままだと解除後も編集不可
   （C-6条件④）のまま宙に浮いていた。`ReleaseAsync`の`SaveChangesAsync`後・
   `CommitAsync`前に`SettlementService.RecalculateForCustomerAsync`を呼ぶ配線を追加。
   解除で`billing_number`が外れた行は充当先を失うため未消込へ戻る。
2. **売上訂正で金額を減らすと消込済金額が新しい金額を超えて取り残る。**
   `SalesEditLockEvaluator`の編集ロック条件4は`FullySettled`のみを対象にし
   `PartiallySettled`は編集を許すため、一部消込の行を金額を減らす方向へ訂正できる経路
   があった（訂正前は`settled_amount`が編集ロックに関与せず素通りしていた）。
   `SalesService.UpdateAsync`／`CancelSlipAsync`の`SaveChangesAsync`後・`CommitAsync`前
   に同様の配線を追加。実際の入金データ（`detail_receipt`等）に基づき新しい金額へ
   丸め直される。

### 16-8. 既知の限界（対応しない。将来の課題として記録）

- **解除済み`billing`を指したままの`receipt`行は`allocated_amount`が入力データとして
  残る。** `SettlementService`は入力データ（`allocated_amount`／`fee_adjustment_amount`）
  を書き換えない。解除された充当の付け替え（別のbillingへ回す等）は7-2（入金入力）の
  責務とし、7-6（消込整合性レビュー）で棚卸しする。
- **返品・値引行（マイナスの`amount`）が明細入金から直接指定される場合の符号の組み合わせ
  は実務上の発生例が未確認。** `SettlementAllocator`・`SettlementStatusCalculator`は
  符号対称に実装済みだが、実データでの検証は行っていない（他の「残存リスク」節と同じ
  扱い。発生した場合に単体テストを追加して確認する）。

### 16-9. seedデータの不整合修正

新しい消込ルールで自己整合するよう`scripts/seed_dev_data.sql`を修正した
（2026-09-14。状態の境界値は維持し、金額のみ調整）。

- `RCP_INV001`（CUS001締め入金）: 11,000.00→4,000.00（全額入金だと`SALINV002`が消込完了
  になり、既存の一部消込という境界値が再現できなくなるため一部入金に変更）。
- `RCP_SLP001`（CUS002締め入金）: 4,000.00→10,000.00／充当額4,000.00→8,800.00（過入金にし、
  請求額8,800.00に対する一部充当という境界値を維持）。
- `SALSLP002`（CUS002売上）: `settled_amount` 8,800.00→8,000.00（決定1により対象額は
  `amount`＝税抜8,000.00。税800.00は行に載せない）。
- `SALLIN002`（CUS003売上）: `tax_amount` 611.00→612.00（CUS003の端数区分は切上。
  8,250×8÷108=611.111…を切上すると612.00。`SALLIN004`と同種の見落とし）。
- `DIV001`（CUS003明細請求書）: `sales_amount`/`tax_amount`/`total_amount`/
  `reduced_rate_taxable_amount`/`reduced_rate_tax_amount`を7,638.00/612.00/8,250.00/
  7,638.00/612.00へ修正（`DetailInvoiceService.IssueAsync`は税抜金額を`sales_amount`に
  入れるが、旧値は税込金額`amount`をそのまま入れており税額を二重に加算していた）。
- `DRC002`（CUS003明細入金）: 8,861.00→8,250.00（DIV001の`total_amount`修正に追従）。

修正後、以下の整合性チェック（sqlcmd）が0件であることを確認した。

```sql
SELECT * FROM dbo.sales WHERE is_deleted = 0 AND ABS(settled_amount) > ABS(amount);
SELECT * FROM dbo.sales WHERE is_deleted = 0 AND (
  (settled_amount = 0 AND settlement_status <> 1) OR
  (settled_amount <> 0 AND ABS(settled_amount) >= ABS(amount)
    AND SIGN(settled_amount) = SIGN(amount) AND settlement_status <> 3));
```

### 検証方法

- 単体テスト: `tests/bmcs_app.Domain.Tests/Calculations/`に`SettlementAllocatorTests`・
  `SettlementStatusCalculatorTests`・`AllocationStatusCalculatorTests`を追加（境界値・
  過入金・返品混在・`pool=0`での返品誤消込の回帰を含む）。
- 結合テスト: `tests/bmcs_app.Application.Tests/Receipt/SettlementServiceTests.cs`
  （16件）。決定1〜3の検証例、手数料差額、返品混在の全額/一部充当、前受、過入金、
  直接指定/明細請求書経由の合算順序、**完了条件の直接検証（登録→訂正→取消を通して
  キャッシュ列が実態と一致し続けること）**、冪等性、トランザクション外呼び出し・
  存在しない得意先の例外を検証。得意先・売上・入金・請求・明細請求書のすべてを
  同一トランザクション内で作成しロールバックする方式のため、seedデータには一切触れず
  後始末の物理削除も不要（`SettlementService`が自前でトランザクションを開かないため
  可能な方式）。
- `Billing/BillingReleaseServiceTests.cs`・`Sales/SalesServiceCorrectionTests.cs`に
  16-7の回帰テストを各1件追加。
- 全体テスト: Domain 239件／Application 88件、すべてgreen。
- 実機確認は行わない（UI未実装のため。4-4と同じ扱い）。

---

## 17. 入金入力画面の実装（Phase 7-2、2026-09-15実装・同日改訂）

締め得意先（請求単位／伝票単位）専用の新規登録画面。都度得意先の入金は明細入金画面
（Phase 7-4）が担う。画面レイアウト・操作方法は旧デモ`bmcs_app.Receipt`を参考にする
よう指示された。

### 17-1. 初版の決定（2026-09-15実装・同日撤回）

初版では、確定済みDBスキーマ（`receipt`の明細行＝`billing_number`／`allocated_amount`という
「請求への充当1件」）を優先し、旧デモの明細行方式（支払手段の内訳）は採用しないと判断して
実装した。しかし実装後に業務実態をユーザーへ確認したところ、この前提が誤りだと判明したため、
17-2の方針へ全面的に改訂した。

### 17-2. 改訂後の決定（ユーザー確認済み・2026-09-15）

締め得意先の入金は「過去の請求額に対して、入金伝票の合計金額を入金する」ものであり、**どの
請求に充当されたかは利用者にとって重要ではなく、知りたいのは残高である**。一方で1回の入金が
複数の支払手段（現金・振込・手形等）に分かれることがあり、**入金方法は行ごとに選べる必要が
ある**。この確認により、初版の前提（明細行＝充当行）を撤回し、次の方針とした。

1. **`receipt`の明細行は支払手段の内訳（入金方法＋金額＋行摘要）に変更する。** 旧デモ
   `bmcs_app.Receipt`の明細行方式を採用する。入金先口座は行単位（`receipt_method=2`
   〈振込〉の行のみ）、手形期日も行単位（`receipt_method=3`〈手形〉の行のみ）で持つ。
2. **請求への充当（`billing_number`／`allocated_amount`／`fee_adjustment_amount`）は
   `receipt_allocation`テーブルへ分離し、画面には表示しない内部データとする。** 支払手段の
   内訳行の件数と、充当先の請求の件数は一致しない（例: 入金2行の合計を3件の請求へ古い順に
   充当）ため、同じ行に両方の意味を持たせることはできない。保存時に`receipt`の明細行合計額を
   確定済み`billing`の未消込残額へ古い順に自動配分し、`receipt_allocation`へ書き込む（配分
   ロジック自体はPhase 7-1の`SettlementAllocator`を無変更で流用）。
   詳細なテーブル定義は`docs/database-schema.md` 2.10節・2.10-1節、移行は
   `scripts/016_split_receipt_allocation.sql`を参照。
3. **画面には請求残高（得意先確定時点の未収額合計）を表示する。** 充当先の一覧は表示しない
   （1の理由により、利用者にとって重要ではないため）。
4. **実装範囲は新規登録と、既存伝票番号による読み取り専用の読込のみ。** 既存伝票の
   訂正・取消はPhase 7-5の範囲（本タスクでは「取消 (F8)」ボタンを枠のみ用意し無効化する。
   6-3/6-4と同じ「先に枠を作り、対応フェーズで配線する」前例）。
5. **振込手数料差額（`fee_adjustment_amount`）の入力はこの画面のスコープ外。** Phase 7-3
   （手入力のみ。M-14・2026-09-10確定）で別途扱う。本画面が新規登録する充当行は常に`0`。
   Phase 7-3の入力先は`receipt_allocation.fee_adjustment_amount`になる。

### 検証方法

- 結合テスト: `tests/bmcs_app.Application.Tests/Receipt/ReceiptEntryServiceTests.cs`
  （13件）。単一請求への全額充当、複数請求にまたがる古い順充当（完了条件の直接検証）、
  過入金による前受行の発生、確定済み請求が無い場合の全額前受、複数の支払手段が混在する
  伝票の保存、`SettlementService`との連携（保存後に`sales.settlement_status`が更新される）、
  都度得意先・存在しない得意先・振込での口座未指定・手形での期日未指定・明細0件・入金額
  合計0以下の例外、既存伝票の読込を検証。`SettlementServiceTests`（16件）も
  `receipt`/`receipt_allocation`の分離に合わせて改訂。
- 全体テスト: Domain 239件／Application 101件、すべてgreen。
- 実機確認: `dotnet run`でアプリを起動し、メインメニューが例外なく開くことを確認
  （ログにエラーなし）。GUI操作を自動で駆動する手段が実行環境に無いため、画面上の
  クリック操作までは確認できていない。FlaUIによる自動化はPhase 11の範囲（11-1未着手）。

## 18. 明細入金画面の実装（Phase 7-4、2026-09-15実装）

都度得意先（内税明細単位、`tax_unit=3`）専用の新規登録画面。締め得意先（請求単位／伝票単位）の
入金は入金入力画面（Phase 7-2）が担う。消込サービス（`SettlementService`、Phase 7-1）・明細請求書
発行（Phase 6-3/6-4）・`detail_receipt`テーブル・採番系列はすべて実装済みで、本タスクが
`detail_receipt`へ書き込む唯一の入口だった（`target_type=2`のデータはseed以外に生成経路が
無かった）。

画面レイアウト・操作方法は旧デモ`bmcs_app.LineReceipt`を可能な限り再現するようユーザーから
指示された（項目に過不足がある場合は本プロジェクトのスキーマを優先）。

> **経緯の補記**: 作業開始時の指示は「Phase 7-3」だったが、指示内容（明細請求得意先向けの
> 入金画面／LineReceipt再現／対象売上への入金・前受金無し）はPhase 7-4「明細入金画面」の
> 完了条件と一致し、Phase 7-3（振込手数料差額の入力）は2026-09-15にユーザー指示で保留
> されていたため、ユーザーに確認のうえPhase 7-4として実装した。

### 18-1. 確定した設計判断（ユーザー確認済み・2026-09-15）

| # | 論点 | 決定 |
|---|---|---|
| 1 | 充当先の粒度 | 売上伝票タブ＝**売上明細行**単位（`target_type=1`）、明細請求書タブ＝**明細請求書まるごと1行**（`target_type=2`）。デモは請求書タブでも行単位だったが、`docs/database-schema.md` 2.11節のスキーマと、TODO.md 7-4の完了条件「売上伝票**または**明細請求書を指定した」に合わせてスキーマ側を採用した |
| 2 | 明細行の金額 | **読み取り専用**（常に対象の全額または残額を充当）。手入力での減額はしない |
| 3 | 手形期日 | **画面に持たない**。`detail_receipt`に`bill_due_date`列が無く（`receipt`は`scripts/016`で追加済みだが`detail_receipt`は対象外）、DDL変更なしで完結させた |
| 4 | 前受金 | **無し**。充当先がNULLの行は作らない（Phase 7-2の前受・過入金行とは対照的） |
| 5 | 手数料差額 | 常に`0`（Phase 7-3は保留中） |
| 6 | 実装範囲 | 新規登録 ＋ 既存伝票番号による**読み取り専用の読込**のみ。訂正・取消はPhase 7-5（「取消(F8)」は枠のみ用意し無効化。Phase 7-2と同じ前例） |

**決定1・2の帰結（既知の制約）**: 金額が読み取り専用かつ請求書がまるごと1行のため、
「明細請求書の一部の売上だけ入金があった」ケースは、請求書タブではなく**売上伝票タブから
該当売上明細行を個別に取り込んで対応する**。`SettlementService.RecalculateDetailAsync`が
「直接指定を先に確定してから残額を明細請求書経由の配分に回す」設計のため、この併用は
既存ロジックで正しく処理される。

### 18-2. 実装時にPlanエージェントの設計検証で判明した修正点

骨格（候補条件2本＋トランザクション内再確認＋`receipt_amount = Σallocated`＋同一tx内
`SettlementService.RecalculateForCustomerAsync`呼び出し）は既存実装と整合したが、初期案には
以下の穴があったため設計を補正した（`DetailReceiptEntryService`に反映済み）。

1. **候補条件は「未消込」だけでなく「未消込または一部消込」にする。** 金額が常に全額（または
   残額）のみ・編集不可のままだと、一部消込済みの売上明細行が売上タブ（未消込のみ）にも
   請求書タブ（連携行が全行未消込）にも出せなくなり、残額を永久に入金できない行き止まりが
   生まれる（Phase 7-3の手数料調整・7-5の部分訂正・データ補正等で一部消込は発生しうる）。
   売上伝票タブの候補金額は**残額**（`sales.amount − sales.settled_amount`）とする
2. **`Σallocated_amount`（=`receipt_amount`）が0になる保存は拒否する。** `AllocationStatusCalculator`は
   合計0を「未充当」と判定するため、合計0の伝票は永久に充当完了にならない
3. **`Σallocated_amount`は0より大きい値のみ許可する**（Phase 7-2と同じ規則）。行単位のマイナス
   （返品・値引の売上明細行を直接指定するケース）は許可し、正の行との差引を認める。返品行
   単独・返品合計が正の行を上回るケースは保存時に拒否される（＝その返品は将来の売上と相殺
   するまで「未消込」のまま残る。返金処理ではないという業務判断）
4. **候補条件に金額ゼロ除外を追加する。** 売上明細行の残額が0、明細請求書の`total_amount = 0`は
   候補から除外する。`SettlementStatusCalculator`はamount=0の行を常に「未消込」と判定するため
   （消込完了にならない）、直接指定すると`DetailInvoiceService.CancelAsync`の消込済みチェックを
   すり抜けたまま明細入金だけが残る不整合を作れてしまうため
5. **明細請求書タブの「この請求書を指す明細入金が存在しない」条件に`!IsDeleted`を付ける。**
   Phase 6-3の前例と同じ理由。無いと、将来Phase 7-5で取消済みになった明細入金がこの請求書を
   永久にブロックする
6. **保存時に`detail_invoice.total_amount == Σ(連携先売上明細行のamount)`を照合し、不一致なら
   拒否する。** `DetailInvoiceService.IssueAsync`が税額で同じ防御をしている前例に合わせた。
   理論上は発行時の計算式から常に一致するはずだが、データ補正等でズレた場合に自動で丸めず
   拒否するほうが安全
7. **入金方法が振込以外の行は`bank_account_code`を必ず`null`にする**
8. **入金方法の選択肢から「手形」を除外する。** `detail_receipt`に`bill_due_date`列が無く（決定3）、
   Phase 7-2は手形選択時に期日入力を必須にしているため、この画面で手形を選べると期日情報が
   保存できずに欠落する。「現金／振込／相殺」の3択とした
9. **同時実行制御は既存のrowversion楽観的排他に委ねる（追加のロックは実装しない）。** `Sales`・
   `DetailReceipt`はいずれも`AuditableEntity`を継承し`RowVersion`を持つ。2人が同じ売上明細行に
   同時に全額入金しようとした場合、`SettlementService.RecalculateForCustomerAsync`が対象の
   `Sales`行をtracking付きで読み込み・更新するため、後にコミットする側はrowversion不一致で
   `DbUpdateConcurrencyException`→`SlipConcurrencyException`で弾かれる。Phase 7-1/7-2と同一の
   機構であり、`detail_receipt`自体に一意制約は追加しない（追加すると決定1の部分入金・残額
   充当が壊れるため）

### 18-3. 実装構成

- `DetailReceiptEntryService`（Application/Receipt）: `GetSalesLineCandidatesAsync`／
  `GetDetailInvoiceCandidatesAsync`（画面表示用の候補取得）、`SaveNewAsync`（新規登録。金額は
  利用者の入力に頼らずトランザクション内で候補条件を再実行してサーバー側で確定する）、
  `GetByNumberAsync`（読み取り専用読込）
- `DetailReceiptQueryService`（Application/Receipt）: 伝票検索モーダル用の検索
  （`ReceiptQueryService`と対称）。`SlipSearchTarget.DetailReceipt`を追加
- `DetailReceiptEntryViewModel`／`DetailReceiptLineViewModel`（Presentation/Receipt）:
  入金入力画面（Phase 7-2）の明細行パターンと、明細請求書発行画面（Phase 6-3）の候補→明細
  取込パターンを合成
- `DetailReceiptEntryWindow.xaml`（Presentation/Receipt）: 左＝入金登録の明細行、右＝上段タブ
  （売上伝票／明細請求書）＋下段の選択伝票明細（デモ`LineReceiptMainView`のレイアウトを
  踏襲）。売上伝票タブは行単位で取込み、明細請求書タブは請求書単位（上段リストの行から
  まるごと取込み、下段は参考表示・読み取り専用）
- メインメニューに「入金 > 明細入金」を追加（`scripts/014_seed_menu_structure.sql`、
  `screen_key = detail_receipt_entry`、`menu_code = MNU_DETAIL_RECEIPT`。`varchar(20)`制約のため
  `MNU_DETAIL_RECEIPT_ENTRY`から短縮）

DDLの変更は無し（決定3のとおり）。

### 18-4. 将来フェーズへの申し送り

- **Phase 7-3（振込手数料差額の入力）実装時**: 「金額は常に対象の全額」という本タスクの前提を
  再検討する必要がある（`allocated = 全額 − fee`に改訂）
- **Phase 7-5（訂正・取消）**: 対応済み（19章）。伝票の一部行だけを論理削除すると`receipt_amount`
  （全行同値の不変条件）が崩れる懸念があったが、生き残る全行へΣ`AllocatedAmount`を書き直すことで解消した
- **Phase 8（月次締め）**: 確定済み月の`receipt_date`に入金を登録するケースの整合はPhase 7-2と
  同様に未対応のまま

### 検証方法

- 結合テスト: `tests/bmcs_app.Application.Tests/Receipt/DetailReceiptEntryServiceTests.cs`
  （20件）。直接指定・明細請求書まるごと・両方混在の消込、一部消込済み行への残額直接指定、
  完了条件の直接検証（対象外の売上は未消込のまま残る）、返品行との差引による全額充当、
  返品単独での入金合計0以下の拒否、明細0件・充当先の重複・二重充当（請求書とその連携行の
  同時指定）・締め得意先／存在しない得意先／無効化済み得意先・振込での口座未指定・手形指定・
  消込完了済み行の直接指定・金額ゼロの候補除外・請求書金額と連携売上合計の不一致・
  振込口座の保存と現金行での口座無視・既存伝票の読込を検証。
- 全体テスト: Domain 239件／Application 121件、すべてgreen。
- DB確認: `scripts/014_seed_menu_structure.sql`を再適用し、メインメニューに「入金 > 明細入金」
  （`MNU_DETAIL_RECEIPT`）が追加されたことを`sqlcmd`で確認済み。
- 実機確認: `dotnet run`でアプリを起動し、例外なく起動することを確認（ログにエラーなし）。
  GUI操作を自動で駆動する手段が実行環境に無いため、画面上のクリック操作までは確認できて
  いない（Phase 7-2と同じ扱い）。FlaUIによる自動化はPhase 11の範囲（11-1未着手）。

## 19. 入金の取消・訂正の実装（Phase 7-5、2026-09-15実装）

入金入力（7-2）・明細入金（7-4）はどちらも新規登録＋読み取り専用読込のみで、「取消 (F8)」は
枠のみ無効化されていた。本タスクは`ReceiptEntryService`／`DetailReceiptEntryService`に
`UpdateAsync`（訂正）・`CancelSlipAsync`（取消）・`EvaluateEditLockAsync`（編集ロック判定）を
追加し、両画面のUI配線まで行う。消込の巻き戻し自体は7-1の`SettlementService.
RecalculateForCustomerAsync`（得意先単位の全件再計算）をそのまま呼ぶだけで実現できる
（5-6の`SalesService.UpdateAsync`／`CancelSlipAsync`と同じ設計）。

### 19-1. 発見した設計上の矛盾: `receipt`の編集ロックに条件④をそのまま適用できない

着手前の想定は「C-6の4条件（①請求締め ②明細請求書発行済み ③月次締め ④入金済み）は`sales`側の
ものであり、`receipt`／`detail_receipt`自体には一切適用しない（月次締めのみロックする）」だった。
しかし調査の過程で、`BillingClosingService.BuildCandidateAsync`（`docs/design_document.md` 9章）が
締め処理時に`receipt.Amount`の合計（前回確定`billing.billing_date`〜今回`closing_date`の期間で
集計）を`billing.CurrentBillingAmount`（`= PreviousBalance − ReceiptAmount + Tax...`）へ
**スナップショットとして焼き込み、以後誰も再計算しない**ことが判明した。この期間の`receipt`を
無条件に取消・訂正できると、確定済み請求の残高が入金1件分だけ二重に増減する不整合が生まれ、
`ReceiptEntryService.GetOutstandingBillingsAsync`（`receipt_allocation`から都度計算する現在値）と
`billing.CurrentBillingAmount`（締め時点のスナップショット）が永久に食い違う。

この発見をユーザーに提示し、「締め入金(receipt)の編集ロックに、月次締めに加えて『請求締め
スナップショット』条件を追加するか」を確認した結果、追加する方針で確定した（2026-09-15）。
`detail_receipt`はこの問題を持たない（`detail_invoice`の金額は`sales`から都度導出され、
`detail_receipt`からスナップショットを焼き込まれることがないため）。

### 19-2. 確定した編集ロック方針

| 伝票 | ロック条件 |
|---|---|
| `receipt`（締め入金） | (a) 月次締め: `customer_code`＋`receipt_date`の年月に対応する確定済み`monthly_closing`が存在する。(b) 請求締めスナップショット: `receipt_date <= MAX(その得意先の確定済みbilling.billing_date)` |
| `detail_receipt`（明細入金） | 月次締めのみ |

現状`monthly_closing`へ書き込むコードは存在しない（Phase 9未着手）ため条件(a)は実質的に常に
falseだが、Phase 9-1/9-2実装時に自動的に効くよう条件自体は実装した（`SalesEditLockService`と
同じ形）。判定結果の型は既存の`SalesEditLock`（`bmcs_app.Domain.Calculations`の
readonly record struct）をそのまま再利用する。専用の編集ロック判定クラス（`SalesEditLockService`
相当）は新設せず、`ReceiptEntryService.EvaluateEditLockAsync`／`DetailReceiptEntryService.
EvaluateEditLockAsync`として各サービスの public メソッドに実装した。理由は判定条件が
「日付とmonthly_closing／billingの突き合わせ」の数行で、`SalesEditLockEvaluator`のように
複数エンティティのプロパティを見る分岐ロジックが無く、抽出しても再利用先が無いため
（Minimal Impact）。

請求締めスナップショットに抵触して訂正・取消できない場合は、締め解除（6-2）してから操作する
（`BillingReleaseService.ReleaseAsync`は最新の確定`billing`のみ解除可能という既存制約と整合する）。

### 19-3. 明細入金（detail_receipt）の訂正は充当先の追加を許さない

訂正で変更できるのは入金日付・伝票摘要（伝票単位）、各行の入金方法・入金先口座・行摘要、および
**行の削除**のみ。新しい充当先（売上明細行・明細請求書）の追加はできない（追加したい場合は
別伝票で登録する）。`DetailReceiptLineCorrection`（訂正用の行入力型）は`TargetType`等の充当先
フィールドを一切持たないため、シグネチャ上も追加不可能になっている。

これに伴い、**既存行の`AllocatedAmount`／`FeeAdjustmentAmount`は訂正時に再計算せず、読込時の
値をそのまま保持する。** 候補判定（`BuildSalesLineCandidateQuery`／`BuildDetailInvoiceCandidateQuery`）
は自伝票自身の充当を除外する仕組みを持たないサーバー側クエリであり、訂正のたびに再実行すると
以下の問題が起きるため、そもそも再実行しない設計にした。

- **誤って拒否される**: 明細請求書指定の行は、保存した時点で自分自身が「この請求書を指す
  未削除の`detail_receipt`」として存在するため、`BuildDetailInvoiceCandidateQuery`を再実行すると
  常に自分自身の存在で弾かれる（日付だけを変える訂正すら失敗する）。
- **他の伝票の影響で金額が変わる**: `sales.settlement_status`は他の`detail_receipt`の登録・取消に
  よって変動するキャッシュ列であり、訂正の意図とは無関係にこの伝票の充当額が動いてしまう。

`receipt`（締め入金）は候補概念が無く充当は完全に内部自動計算のため、訂正は金額・入金方法・
行の追加削除すべて自由（`receipt_allocation`は訂正のたびに全面再構築する。19-4節）。

### 19-4. `UpdateAsync`の実装（行単位の差分方式）

5-6の`SalesService.UpdateAsync`と同じ「行単位の差分」方式を採る（全行を論理削除して作り直す方式は
不採用。`SlipConcurrencyGuard.EnsureLineSetUnchanged`で検証した行がそのまま更新対象になっている
という保証を保つため）。

**`ReceiptEntryService.UpdateAsync`の処理順**（`receipt`本体は行単位の差分、`receipt_allocation`は
全面再構築）:

1. `ValidateLines`（新規登録と共通）
2. 対象`receipt`行を取得 → `SlipConcurrencyGuard.EnsureLineSetUnchanged` → 編集ロック判定（訂正前）
3. 行diff（更新・論理削除・追加）を適用
4. 編集ロック判定（訂正後。新しい`receipt_date`で再判定）
5. 対象伝票の`receipt_allocation`を全部論理削除 → **ここで`SaveChangesAsync`を1回呼ぶ**
6. `GetOutstandingBillingsAsync`で請求残高を再取得 → 新しい合計額で`BuildAllocationLines`を実行 →
   新しい`receipt_allocation`行を追加
7. `SaveChangesAsync` → `SettlementService.RecalculateForCustomerAsync` → コミット

手順5で`SaveChangesAsync`を挟むのが唯一の非自明な点である。`GetOutstandingBillingsAsync`は
サーバー側の`GroupBy`／`ToDictionaryAsync`クエリであり、ChangeTracker上の未コミットな論理削除を
見ない。挟まずに手順6を実行すると、自分自身の旧充当が「まだ生きている」ものとしてカウントされ、
請求の未消込残額が実際より少なく計算される。全額充当済みだった請求へ減額訂正するテストケース
（`締め入金の金額を訂正すると自分自身の旧充当を除いた残高に対して再配分される`）で、この
`SaveChangesAsync`を省略すると、全額が前受行（`billing_number = NULL`）に落ちてしまうことを
確認した。`docs/architecture.md` 6章の「1ユースケース内で複数回`SaveChangesAsync`」の許容ケース
に追記した。

**`DetailReceiptEntryService.UpdateAsync`は単一フェーズ**（候補クエリを一切呼ばないため、
`SaveChangesAsync`を挟む必要がない）:

1. 対象`detail_receipt`行を取得 → `SlipConcurrencyGuard.EnsureLineSetUnchanged` → 編集ロック判定（訂正前）
2. 入力に無い既存行番号を含んでいたら（＝追加しようとした）例外
3. 既存行を`ReceiptMethod`／`BankAccountCode`／`LineRemarks`で上書き、入力に無い既存行は論理削除
   （全行削除になる場合は例外。「取消をご利用ください」と案内する）
4. **`receiptAmount = Σ AllocatedAmount`（生き残った行のみ）を生き残る全行へ書き直す**
   （18-4節が指摘した伝票単位の全行同値不変条件を、ここで初めて確定させる）
5. 編集ロック判定（訂正後）→ `TouchAll` → `SaveChangesAsync` →
   `SettlementService.RecalculateForCustomerAsync` → コミット

### 19-5. 締め解除済み`billing`への再充当は付け替えない

`receipt`の訂正で`receipt_allocation`を全面再構築する際、`GetOutstandingBillingsAsync`は
`BillingStatus.Confirmed`の`billing`のみを対象にする。したがって、訂正前に確定済み`billing`へ
充当されていた`receipt`が、その後`billing`が締め解除（6-2）された状態で訂正されると、
再構築後の`receipt_allocation`はその`billing`を対象外にする（別の確定済み`billing`があれば
そちらへ、無ければ前受行へ回る）。`BillingReleaseService.ReleaseAsync`が「解除では既存の
`receipt_allocation`を付け替えない」と明言している既存方針（16章「既知の限界」）の自然な帰結であり、
新たな防止策は設けない。

### 19-6. ViewModelの構成変更

`ReceiptEntryViewModel`／`DetailReceiptEntryViewModel`は、`SalesEntryViewModel`のパターンに
合わせて作り直した。

- `IsExistingLoaded`（既存伝票を読み込んだか。得意先コードの読取専用化にのみ使う）と
  `IsEditLocked`（`EvaluateEditLockAsync`の結果）を分離し、`IsEditable => !IsExistingLoaded ||
  !IsEditLocked`（ロックされていなければ既存伝票も編集できる）という合成プロパティを新設した。
  旧実装は`IsExistingLoaded`単独で画面全体を読み取り専用にしており、7-5の訂正機能とは相容れない
  ため置き換えた。
- `LookupAsync`（伝票No.欄でのEnter読込）は、得意先を`customerService.GetByCodeAsync`で再取得
  して`_customer`へセットするよう変更した（旧実装は`_customer = null`のままで、`CanSave`が
  永久にfalseになるバグを内包していた。訂正機能追加時に合わせて修正）。
- 保存(F10)は`_loadedXxxNumber`（読込中の伝票番号）の有無で新規登録／訂正を分岐し、取消(F8)を
  `CancelSlipAsync`に配線した。`DetailReceiptEntryViewModel`は追加で`CanAddNewTarget =>
  _loadedDetailReceiptNumber is null`を新設し、F2（候補からの取込）を訂正モードでは常に不可にした
  （19-3節）。
- XAML: 両画面とも「取消 (F8)」ボタンの`IsEnabled="False"`を外し、`F8`の`KeyBinding`を追加した
  （旧実装には無かった）。ヘッダー欄（入金日付・摘要）の`IsReadOnly`バインディングは
  `IsExistingLoaded`から新設の`IsHeaderLocked`（`= !IsEditable`）へ張り替えた。得意先コード欄のみ
  引き続き`IsExistingLoaded`を使う（訂正モードでも得意先の付け替えは不可）。

### 検証方法

- 結合テスト: `tests/bmcs_app.Application.Tests/Receipt/ReceiptEntryServiceTests.cs`に10件追加
  （完了条件の直接検証2件、請求締めスナップショットのロック検証4件、訂正時の充当再構築2件、
  排他制御1件、月次締めロック1件）。`DetailReceiptEntryServiceTests.cs`に9件追加（完了条件の
  直接検証3件、候補クエリを再実行しないことの検証1件、`receipt_amount`不変条件の検証1件、
  充当先追加・全行削除の拒否2件、排他制御1件、月次締めロック1件）。両ファイルとも
  `SaveNewAsync`／`UpdateAsync`／`CancelSlipAsync`が自前でトランザクションをコミットするため
  既存と同じ「コミット＋`finally`で物理削除」方式を踏襲し、`monthly_closing`の削除を
  `CleanupAsync`に追加した。
- 全体テスト: Domain 239件／Application 140件、すべてgreen。
- DB確認: 全テスト実行後、`sqlcmd`で`__TST%`customer_codeを持つ行が`customer`／`receipt`／
  `receipt_allocation`／`detail_receipt`／`sales`／`billing`／`monthly_closing`／`detail_invoice`
  のいずれにも残っていないことを確認済み。
- 実機確認: `dotnet run`でアプリを起動し、例外なく起動することを確認（ログにエラーなし）。
  GUI操作を自動で駆動する手段が実行環境に無いため、画面上のクリック操作までは確認できて
  いない（Phase 7-2/7-4と同じ扱い）。

## 20. フェーズレビュー（消込整合性、Phase 7-6、2026-09-15実施）

完了条件「キャッシュ列と入金明細の実集計が全パターンで一致する」を、次の4つの独立した方法で
検証した。対象キャッシュ列は`sales.settlement_status`／`sales.settled_amount`／
`receipt.allocation_status`／`detail_receipt.allocation_status`の4つ。

### 20-1. 書き込み経路の監査（コード）

`src/bmcs_app.Application/`全体を`grep`し、上記4カラムへの代入箇所を洗い出した。結果は
**書き込み元が`SettlementService`（`ApplySettlement`／`ApplyReceiptAllocationStatus`／
`ApplyDetailReceiptAllocationStatus`）の1箇所に限られる**ことを確認した。例外は2箇所のみで、
いずれも新規追加行への一時的なプレースホルダである。

- `SalesService.UpdateAsync`の新規行追加ループ（`SettlementStatus.Unsettled`／`SettledAmount = 0m`）
- `ReceiptEntryService`／`DetailReceiptEntryService`の`SaveNewAsync`（`AllocationStatus.Unallocated`）

いずれも、値を書いた**同一トランザクション内**で必ず`SettlementService.
RecalculateForCustomerAsync`が呼ばれるため（各メソッドの末尾）、プレースホルダのまま
コミットされることはない。**キャッシュ列の実質的な書き手が1箇所しかない**という構造が、
そもそも不整合を作りにくくしている。

### 20-2. 消込に影響しうる他の操作の分析（コード）

`RecalculateForCustomerAsync`を呼ばない既存の書き込み経路のうち、消込キャッシュに影響しうる
ものを個別に検討した。

- **`BillingClosingService.ConfirmAsync`**（締め処理）: 対象の`sales`行へ新しい`billing_number`を
  割り当てる（`billing_number = NULL` → 新規`billing_number`）が、`RecalculateForCustomerAsync`を
  呼んでいない。しかし`RecalculateClosingAsync`は`billing_number`ごとにグループ化し、
  `billing_number IS NULL`の行は`pool`を常に`0`として扱う（グルーピングキーが`null`かどうかで
  分岐しており、`receipt_allocation`の内容を見ない）。新しく確定した`billing_number`は同一
  トランザクション内で今まさに作られたものであり、それを指す`receipt_allocation`が事前に
  存在することはあり得ない（`billing`へのFK制約上、存在しない`billing_number`を指す
  `receipt_allocation`は作れない）ため、締め前後で対象行の`pool`は`0`のまま変化しない。
  したがって再計算を呼ばなくても値は狂わない。
- **`DetailInvoiceService.IssueAsync`**（明細請求書発行）: 対象の`sales`行を`detail_invoice_
  sales_line`で明細請求書に連携させるが、同様に`RecalculateForCustomerAsync`を呼ばない。
  `RecalculateDetailAsync`は連携済みの行を「直接指定分 + 請求書経由の残額配分」で計算するが、
  発行直後の請求書には`detail_receipt`が存在し得ない（FK制約上、存在しない請求書番号を
  指す`detail_receipt`は作れない）ため`pool = 0`となり、請求書経由の配分は常に`0`。連携前
  （直接指定のみで計算）と連携後（直接指定 + 0）の結果は同じになるため、再計算は不要。
- **`DetailInvoiceService.CancelAsync`**（明細請求書取消）: 連携解除の前提として「連携先の
  売上明細行がすべて未消込（`SettlementStatus.Unsettled`）」を既存ガードで強制している
  （取消時のチェック、`docs/design_document.md` 12章）。連携解除の前後で対象行が
  「請求書経由（pool=0のため寄与0）」から「非連携（直接指定のみ）」に移るだけであり、
  ガードにより連携解除前の値が既に`0`であることが保証されているため、値は変化しない。

以上により、`RecalculateForCustomerAsync`を呼んでいない既存の書き込み経路は、いずれも
呼ばなくても消込キャッシュ列が狂わないことをコードレベルで確認した。**新たな不具合は
見つからなかった。**

### 20-3. 既存データに対する再計算差分ゼロ検証（実データ）

`SettlementService.RecalculateForCustomerAsync`は「値が実際に変わった行だけ」を更新する設計
（`docs/architecture.md` 9章）であるため、開発用ライブDBに現存する**全得意先**に対して
呼び出し、更新件数が0件であることを直接確認した
（`tests/bmcs_app.Application.Tests/Receipt/SettlementPhaseReviewTests.cs`、得意先ごとに
トランザクションを開始・ロールバックし、seed・実データは一切変更しない）。全得意先で
更新件数0件を確認した。

### 20-4. 独立したSQLでの構造整合性チェック（実データ）

20-3はプロダクションコードの計算ロジック（`SettlementStatusCalculator`／
`AllocationStatusCalculator`）自体に誤りがあった場合には検出できないため、これらを一切
経由しない生SQLで同じ不変条件を独立に再実装し、`sqlcmd`で直接検証した。

```sql
-- sales: settled_amount が amount を超えていないか
SELECT COUNT(*) FROM dbo.sales WHERE is_deleted = 0 AND ABS(settled_amount) > ABS(amount);
-- sales: settlement_status とカラム値の不整合
SELECT COUNT(*) FROM dbo.sales WHERE is_deleted = 0 AND settlement_status <> (
  CASE WHEN settled_amount = 0 OR amount = 0 THEN 1
       WHEN SIGN(settled_amount) = SIGN(amount) AND ABS(settled_amount) >= ABS(amount) THEN 3
       ELSE 2 END);
-- receipt / detail_receipt: allocation_status とスリップ合計の不整合（本文参照。JOINで集計）
-- detail_receipt: receipt_amount の全行同値不変条件（18-4節で指摘された懸念の実データ確認）
```

5項目すべてで該当0件を確認した。

### 20-5. 結論と申し送り

- 消込キャッシュ列の不整合は、コード監査・実データ検証のいずれでも検出されなかった。
  **本レビューで修正した不具合はない**（5-7・6-5とは異なり、監査の結果「既に健全」という
  結論になったケース）。
- **7-3（振込手数料差額の入力）は本レビュー時点で保留中であり、`fee_adjustment_amount`が
  非ゼロになる経路は現状のUIから到達不能**（7-2/7-4/7-5のいずれも常に`0`を書き込む）。
  したがって本レビューの検証範囲は実質的に「手数料差額なし」のパターンに限られる。
  7-3実装時に`fee_adjustment_amount`が非ゼロの実データが生まれるため、20-3・20-4の
  検証を再実施することが望ましい。
- `SettlementPhaseReviewTests`（20-3のテスト）は今後の回帰検知として恒久的に維持する
  （新しい書き込み経路が`RecalculateForCustomerAsync`の呼び出しを漏らした場合、次にこの
  テストを実行したタイミングでいずれかの得意先に差分が出て検出できる）。

### 検証方法

- 結合テスト: `tests/bmcs_app.Application.Tests/Receipt/SettlementPhaseReviewTests.cs`（新規、1件）。
  全体テスト（Domain 239件／Application 141件）すべてgreen。
- DB確認: 20-4のSQLをすべて`sqlcmd`で実行し、5項目すべて該当0件を確認済み。

---

## 21. 得意先元帳・現在残高（Phase 8-1／8-2、2026-09-15実装）

### 21-1. スコープ

TODO.md 8-1の文面は「元帳データのマージ実装」（サービス層）のみだが、ユーザー指示が画面
レイアウトに言及したため、**サービス層＋ViewModel＋View（画面）まで**を本タスクの範囲とした。
画面レイアウト・操作方法は旧デモ（`bmcs_app.CustomerLedger`）を可能な限り再現しつつ、項目に
過不足がある場合は本プロジェクトを優先した。8-2（リアルタイム残高の常時表示）も同日中に
続けて実装した（21-6参照）。

スコープ外として残したもの: 8-3（伝票プレビュー＝`RowActivationBehavior`の配線）、
印刷・プレビュー（Phase 10。ボタンは枠のみ用意し`IsEnabled="False"`）。

### 21-2. 中心的な設計課題と決定事項

`tax_unit=1`（請求単位）の売上明細行は伝票時点で消費税額を持てない（CHECK制約
`CK_sales_tax_amount_by_tax_unit`）。一方入金額は税込である。そのため「売上`amount`の累計 −
入金の累計」では締め得意先の残高が税額分だけマイナスにずれる。この問題と、都度得意先の
消込証跡の表示要件を解決するため、以下をユーザー確認のうえ決定した（2026-09-15）。

| # | 決定 |
|---|---|
| D-1 | **残高は税込。消費税を独立した明細行として時系列に挿入する。** `tax_unit=1`は請求締め済み区間は`billing.tax_amount`（確定値）を`billing_date`の行、未締め区間は`ConsumptionTaxCalculator.CalculateExternalTaxBuckets`で仮計算した額を期末日付の行として挿入。`tax_unit=2`は伝票ごとの`slip_tax_amount`をその伝票の直下に1回だけ挿入。`tax_unit=3`は`amount`が既に税込なので消費税行を作らない |
| D-2 | **入金の残高影響は常に入金日付の独立行で発生させる。** 都度得意先の売上行の右側には「入金日付・入金No」を消込の**証跡**として表示するが金額は載せない（`ReceiptAmount`をnullのままにする）。売上と入金が月をまたいでも各月末の売掛残高が日付どおり正確になり、Phase 9-1（月次締め）の暦月末残高の定義と矛盾しない |
| D-3 | 1つの売上明細行に複数の入金が証跡として紐づく場合、2件目以降は売上側の列を空欄にした行として直下に続ける |
| D-4 | 繰越は**全期間積み上げ**で算出する。`monthly_closing`／`billing.previous_balance`を起点に使わない（M-11「都度集計する・残高キャッシュ列は持たない」と整合させ、`billing`のスナップショットとの二重計上を避けるため） |
| D-5 | 解除済みデータ（`billing.billing_status=2`）は残高計算の対象外（product-spec.md「解除済＝集計対象外」） |

### 21-3. アーキテクチャ

マージ・時系列化・残高推移・仮計算税・消込証跡はすべて Domain の純粋関数に置き、Application層は
クエリして渡すだけにした（`SalesEditLockEvaluator`と同じ方針）。完了条件「残高推移が手計算と
一致する」をDB不要の単体テストで直接検証でき、かつ将来9-1が同じ関数を再利用できるようにする狙い。

- `src/bmcs_app.Domain/Calculations/CustomerLedgerBuilder.cs`: マージ本体。`Build(CustomerLedgerInput)`
  が`CustomerLedgerResult`（先頭が繰越行のエントリ一覧＋各種合計）を返す。仮計算消費税は
  `ProvisionalTaxAsOf(customer, salesLines, asOf)`という**日付の関数**として定義し、期間境界
  （`PeriodFrom`の前日／`PeriodTo`）で差分を取ることで、D-1とD-4（全期間積み上げ）を両立させた
  （固定の「期末に1行」にすると、期間Fromより前の未請求売上の税が繰越に入らずD-4と矛盾する）。
- `src/bmcs_app.Domain/Calculations/LedgerReceiptPairing.cs`: 都度得意先の消込証跡（D-2/D-3）を
  作るための紐づけ。**表示専用であり残高計算には使わない。** 残高は`DetailReceipt.AllocatedAmount`
  を明細入金行ごとに独立行として計上する別ロジックが担うため、本クラスは金額の按分を一切行わない
  （`SettlementService`の消込配分アルゴリズムとは別物）。直接指定（`target_type=1`）はエンティティが
  対象を直接持つため単純な参照、明細請求書指定（`target_type=2`）は連携する**すべて**の売上明細行に
  証跡として結びつける（どの明細行にいくら充当されたかの厳密な按分は行わない）。
  - 設計変更の経緯: 当初案は`SettlementAllocator`を使った金額按分つきの2段構えペアリング
    （`settled_amount`との一致をテストで担保する方式）だったが、D-2の決定（入金の残高影響は
    独立行でのみ発生させる）により証跡が金額を持たなくなったため、按分ロジックが不要になった。
    残高が証跡の正確さに依存しない設計になったことで、証跡ロジックを大幅に単純化できた
    （Simplicity First。証跡に万一漏れがあっても残高は狂わない）。
- `src/bmcs_app.Application/Ledger/CustomerLedgerQueryService.cs`: `GetAsync(customerCode, from, to)`が
  得意先の全期間・未削除の売上・入金・（締め得意先のみ）確定済み請求を読み、`CustomerLedgerBuilder`に
  渡す。**`receipt_allocation`は一切参照しない**（21-4のR1参照）。

### 21-4. 申し送り事項

| # | 内容 |
|---|---|
| R1 | `BillingReleaseService`は締め解除時に`sales.billing_number`をNULLに戻すが`receipt_allocation`は触らないため、解除済み`billing`を指す充当行が残留しうる。元帳は`receipt_allocation`を参照しない設計のため影響なし（観測事実として記録） |
| R2 | 元帳の残高と`billing.current_billing_amount`の一致は「遡及入力が無い」前提でのみ成立する。締め後に過去日付の**新規**売上・入金を登録することはC-6の編集ロック（既存行の編集のみ対象）では防げない。元帳は常に実データからの都度集計なので**元帳が正、`billing`は締め時点のスナップショット**であり、この不一致を検出して例外を投げてはならない（正常な業務オペレーションで起こりうる）。9-1実装時に考慮すること |
| R3 | Phase 7-3（振込手数料差額の入力）実装時、`fee_adjustment_amount`が非ゼロになると元帳の残高に残渣が残りうる。`billing.receipt_amount`も`Σ receipt.Amount`（手数料を含まない）なので両者は一致し元帳固有の問題ではないが、「手数料差額を残高からどう落とすか」の業務判断が7-3で必要になる |
| R4 | 1伝票の明細行が請求済・未請求に分かれると`tax_unit=2`の`slip_tax_amount`（伝票単位の値）が実態とずれうるが、締め時に`BillingClosingService.CalculateTaxSummary`が検出して例外を投げるため、元帳側に独自の整合チェックは入れていない（元帳は照会画面であり業務ルール違反の検出責務は6-1側） |
| R5 | `scripts/seed_dev_data.sql`の`monthly_closing`（2026-01/02分）は売上・入金の実データ（2026-07/08分）と月が重ならないため、Phase 9-1実装時にはseedデータの作り直しが必要になる見込み |
| R6 | Phase 9-1は`CustomerLedgerQueryService.GetAsync(code, 月初, 月末)`の結果を`monthly_closing`の各列にそのまま詰めるだけで済む（`OpeningBalance`/`SalesTotal`/`TaxTotal`/`ReceiptTotal`/`ClosingBalance`が`previous_balance`/`sales_amount`/`tax_amount`/`receipt_amount`/`closing_balance`と1:1対応）。ただし税率別内訳5カラムは`CustomerLedgerResult`に含めていないため、9-1で追加が必要 |
| R7 | 性能の逃げ道（本タスクでは未実装）: `GetAsync`に`historyFrom`のような引数を足し、それ以前を`monthly_closing`の確定残高で置き換える拡張が考えられる。M-11「性能問題が出た場合に残高キャッシュ列を追加する」に沿う |

### 21-5. seedデータでの手計算検証（完了条件の直接証跡）

期間 2026/07/01〜2026/08/31 で、`scripts/seed_dev_data.sql`の3得意先すべての残高推移を手計算し、
Domain単体テスト（`CustomerLedgerBuilderTests`）と、開発用ライブDBに対する結合テスト
（`CustomerLedgerQueryServiceTests`、読み取り専用）の両方で一致を確認した。

- **CUS001**（締め・請求単位・締日20・切捨）: 繰越0 → 7/15売上10,000 → 7/20消費税1,000
  （`BIL_INV001`確定値、残高11,000＝`billing.current_billing_amount`と一致）→ 7/25入金4,000 →
  8/05入金3,000（前受）→ 8/10売上5,000 → 8/31消費税500（未締め分・仮計算）→ **残高9,500**
- **CUS002**（締め・伝票単位・締日99・四捨五入）: 繰越0 → 7/10売上8,000＋消費税800（残高8,800＝
  `billing.current_billing_amount`と一致）→ 8/01入金8,000 → 8/01入金2,000（**残高−1,200の過入金を
  正常系として許容**）→ 8/11売上3,000＋消費税300 → **残高2,100**
- **CUS003**（都度・内税明細単位・切上）: 繰越0 → 7/20売上8,250（証跡: 7/22 `DRC002`）→
  7/22入金8,250 → 7/25売上2,750（証跡: 7/25 `DRC001`）＋7/25入金2,750 → 8/12売上11,000 →
  8/13返品−1,100 → **残高9,900**。消費税行は1行も作らない

いずれも`IsBalanced`（`ClosingBalance == OpeningBalance + SalesTotal + TaxTotal − ReceiptTotal`）が
成立することを確認済み。

### 21-6. リアルタイム残高の常時表示（Phase 8-2、2026-09-15実装）

**「常時表示」の意味をユーザーに確認したうえで実装した。** 当初「常時表示」という文言から
「ウィンドウを開いたまま他画面での更新を自動検知して書き換える」（プッシュ通知・ポーリング等の
リアクティブUI）という解釈もありえたが、`docs/architecture.md`にウィンドウ間でデータ変更を
通知し合う仕組み（イベント配信・SignalR・ポーリング等）は一切設計されておらず、各ウィンドウは
独立したDIスコープ・DbContextを持つだけである。ユーザーに確認した結果、8-2の実態は
**「キャッシュ列に頼らず、表示するたび（画面を開く・再検索する等）に必ず最新の実データから
残高を計算する」という正確性の保証**（M-11「都度集計する・残高キャッシュ列は持たない」の帰結）
であり、「画面を開きっぱなしでも自動で動く」というリアクティブUIではないことを確定した
（2026-09-15）。

実装は、得意先元帳画面（`CustomerLedgerViewModel`）に**検索期間とは独立した**「現在残高」
（本日時点の残高）を追加した。得意先確定時（`ApplyCustomerAsync`）と表示(F5)実行時
（`SearchCommand`）の両方で`CustomerLedgerQueryService.GetBalanceAsOfAsync`（8-1で用意済みの
入口。`GetAsync(code, asOf, asOf).ClosingBalance`と同値）を呼んで都度再計算する。期間From/Toを
過去の月に変更しても「現在残高」自体は連動しない（デモの「前月繰越／今回」集計とは別の独立表示）。

### 検証方法

- 単体テスト: `tests/bmcs_app.Domain.Tests/Calculations/CustomerLedgerBuilderTests.cs`（10件）・
  `LedgerReceiptPairingTests.cs`（4件）。全体テスト（Domain 253件）すべてgreen。
- 結合テスト: `tests/bmcs_app.Application.Tests/Ledger/CustomerLedgerQueryServiceTests.cs`（9件、
  開発用ライブDB。うち3件はseedデータ`CUS001`/`CUS002`/`CUS003`を直接読む回帰検知テスト、
  1件は8-2の完了条件「伝票登録直後に残高が正しく変わる」を`GetBalanceAsOfAsync`で直接検証）。
  全体テスト（Application 150件）すべてgreen。
- 実機: `dotnet run`でのアプリ起動、メインメニュー「元帳 > 得意先元帳」の表示まで確認済み
  （GUI自動操作の手段が実行環境に無いため、画面上のクリック操作はPhase 7-2/7-4/7-5と同様に未確認）。
