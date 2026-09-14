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
| 入金入力（締め） | 締め得意先の入金を登録し、古い請求から自動で消込。手数料差額の自動補正提案あり |
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
