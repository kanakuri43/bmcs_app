# bmcs_app データベース設計

> データベースに関する情報（テーブル設計方針・暫定運用中の項目・テーブル/カラム定義など）はこのファイルに記載する。`CLAUDE.md` や `docs/design_document.md` にDB関連の詳細を書かない。

---

## 1. 設計方針

DBスキーマは、各画面仕様書が実際に前提としている業務要件をもとに新規に設計する。**設計上の正はエンティティクラス定義とし、EF Core のマイグレーション機能は使用しない**（DDLの管理方法は3章「命名規則・運用」を参照）。

方針は以下：

- 月次締め処理の粗利計算に必要な原価カラム（`cost_price`）は、受注明細・売上明細の両テーブルに設ける。
- 受注明細行には `sub_customer_id`（学校のクラス・先生等の子得意先／請求・納品先指定）カラムを設けるが、**参照先の子得意先マスタは作らず、現時点では未使用（値を持つだけで参照・更新する処理はない）。** 学校・官公庁向けの宛名柔軟性は、子得意先マスタではなく**都度書き換え方式**で実現する。ジャーナル系の画面（受注・売上・入金・明細請求書等）は、得意先コードで検索した後、`customer_name`（画面上の名称欄）を手入力で上書き修正できるようにする。学校－学年－クラスのような階層を持つ得意先も、マスタ上は常に一つの得意先として扱い、学年・クラスの違いは伝票入力時の名称上書きだけで表現する。`sub_customer_id` は将来の別要件に備えて列は残すが、削除しない以外の対応方針はない（実装予定なし）。**この `sub_customer_id`（宛名の都度書き換え）は、下記「親子請求（請求集約）」の `billing_customer_code` とは別概念である。** 前者は「請求書に印字する名称」だけを都度差し替える仕組み（マスタ上は常に1得意先）、後者は「どの得意先の売上をどの得意先の請求書に計上するか」という正式なマスタ構造であり、子得意先マスタを作らない方針は後者を否定するものではない。混同を避けるため、親子請求側では「子得意先」という語を使わず「請求集約元／請求集約先」と呼ぶ。
- **親子請求（請求集約）**: 各地に支店を持つ会社の各支店（請求集約元）の売上を本社（請求集約先）に一括請求するための機構。本支店間以外の取引先でも同じ運用を採る可能性がある。得意先マスタに `billing_customer_code`（請求得意先コード）を持たせ、自分自身を指せば従来どおり単独で請求、他の得意先を指せばその得意先の売上は指し先（請求集約先）の請求書にまとめて計上される。詳細な業務ルール・スコープ拡張の設計方針は1-1節、テーブル定義は2.1節、画面仕様・全領域への影響は `docs/design_document.md` の該当章を参照。
- **売上・入金・請求残高は、消費税計算単位（請求単位／伝票単位／明細単位）で物理テーブルを分割しない。** `sales` / `receipts` / `billings` の各1テーブルとし、`tax_unit` カラム（1=請求単位／2=伝票単位／3=内税明細単位）で税単位を表す。得意先マスタの税区分設定に応じて、対象の得意先データがどの `tax_unit` の値を持つかが決まる。1得意先は常にいずれか1つの単位に属する。**税単位ごとに物理分割しない理由**: 分割すると、(1) 伝票番号の重複禁止がDB制約ではなく「単一系列から採番する」というアプリの運用ルールだけで担保され、手動SQLや実装ミスで別テーブルに同じ伝票番号を入れても検知できない、(2) 「どのテーブルに書くか」がアプリ判断で、得意先の税区分と異なるテーブルへのINSERTをDBが受け入れてしまう、(3) 入金と請求は税単位間で構造が完全に同一で、分割する理由が「売上の分割方針に揃えるため」だけになる、(4) 受注（`orders`）は税単位で分割しておらず、売上の税額カラムの都合はNULL許容カラム＋CHECK制約で解決できる。これらは、税単位ごとの税額カラムの対応（CHECK制約）と得意先マスタへの複合FK（次項）で解決する。**明細単位の入金・請求（`detail_receipts` / `detail_invoices`）は構造が本当に異なるため統合対象外**（繰越残高の概念がない、`target_type` 分岐がある等）。
- **`sales` / `receipts` / `billings` は、得意先マスタとの複合FKで税単位の整合をDBが強制する。** `customers` に `UNIQUE (customer_code, tax_unit)` を持たせ、各テーブルから `(customer_code, tax_unit)` の複合FKで参照する。「伝票の税単位は得意先マスタの税区分と必ず一致する」がDB制約になるため、誤った税単位でINSERTすることはできない。同様に `billings` にも `UNIQUE (billing_number, tax_unit)` を持たせ、`sales` / `receipt_allocations` から `(billing_number, tax_unit)` の複合FKで参照することで、税単位をまたいで請求データを参照できないことも強制する（`receipts`自体はbillingへのFKを持たない。2.10節参照）（`billing_number IS NULL` の未請求・前受金行はSQL Serverの MATCH SIMPLE によりFK検査対象外になり、そのまま表現できる）。
- **明細入金は、税区分が「明細単位」の得意先専用の入金テーブルとして実装する。** 明細入金を使う得意先は税単位が明細単位の得意先のみの予定であるため、専用テーブルを新設するか `receipts` に一本化するかという判断は不要（明細単位バケット＝明細入金テーブルそのもの）。**`receipts`（締め入金）と `detail_receipts`（明細入金）が別テーブルという非対称は意図的なもの。** 明細入金は「売上伝票または明細請求書を指定したピンポイント消込」であり、締め入金（請求単位で古い順に自動消込）とは保持すべきカラムが異なるため統合しない（税単位が同じだけで構造まで同じとは限らない、というのが `sales`/`receipts`/`billings` を1テーブルにまとめた判断との違い）。
- **明細請求書と売上の紐付けは、売上明細（行）単位の連携テーブルで管理する。** 1つの売上の各明細行が、それぞれ別の明細請求書に分散して紐づくことがあるため、売上ヘッダー単位の直接FK（1対多）では表現できない。売上ヘッダー単位ではなく、売上明細行単位での多対多の紐付けが必要。
- **消込ステータスはキャッシュ列方式で管理する。** 売上明細行に消込ステータスのカラムを持ち、入金の登録・取消・訂正時に関連する売上明細のステータスを同一トランザクション内で更新する。都度SUM計算方式（入金明細を都度集計）は、元帳表示・明細請求書候補抽出・月次締めの整合性チェックなど絞り込み表示が頻出するため採用しない。
- **得意先元帳は、アプリ側（LINQ）で複数テーブルを取得してマージする方式とする。** SQLビュー（UNION等）によるDB側結合は、マイグレーションを使わない方針と相性が悪いため採用しない。`sales`/`receipts` を `customer_code` で絞るだけで済み（内税明細単位の得意先は `sales` と `detail_receipts` を絞る）、税単位に応じて参照テーブルを振り分ける分岐は不要。
- **締め対象／都度対象の判定は、得意先マスタの締日カラム（`closing_day`、`0`なら都度・明細）のみで行う。** 専用の区分フラグは別途持たない。現状の業務要件（一般企業=締め、官公庁・学校・都度取引先=都度・明細）ではこれで十分なため。
- **締日カラム（`closing_day`）と税区分カラムは独立ではなく、`税区分 = 内税明細単位 ⇔ closing_day = 0` の相互制約を持つ。** 締め日のある得意先で内税明細単位を使うことはなく、逆に都度得意先で請求単位／伝票単位を使うこともない（業務確認済み）。**この組み合わせに限定される理由はインボイス制度上のもの**（請求単位・伝票単位＝締め得意先は常に外税、内税明細単位＝都度得意先は常に内税）。残り3パターン（内税×請求単位／内税×伝票単位／外税×明細単位）は業務上発生しないため扱わない。この2カラムを独立に入力できる状態にすると、次の破綻が起きるため制約を明示する。
  - 締め得意先（`closing_day≠0`）＋内税明細単位 … 明細単位バケットには前月繰越残高を持つ請求データ（`billings`）が存在しないため、請求締め処理が実行できない。
  - 都度得意先（`closing_day=0`）＋請求単位 … 請求締め時に消費税を一括計算する前提のため、締めを行わない都度得意先では消費税の確定タイミングが決まらない。
  - 担保方法: 得意先マスタに **CHECK制約**（上記の同値条件）を設け、加えてアプリ側の得意先マスタ登録・更新時バリデーションでも弾く。
- **得意先の締め区分（`closing_day` の 0／非0）と税区分は、登録後に変更できないものとして扱う。** 変更すると既存の売上・入金・請求データの `tax_unit` と食い違い、既存データの整合が取れなくなるため。マスタ管理画面では新規登録時のみ入力可とし、更新時は編集不可（読み取り専用）とする。したがって移行処理は実装しない。**これはDB制約でも裏付けられる。** `customers` の `UNIQUE (customer_code, tax_unit)` を `sales`/`receipts`/`billings` から複合FKで参照しているため、伝票が1件でも存在する得意先の `tax_unit` を `UPDATE` すると `ON UPDATE CASCADE` を持たないFK違反（Msg 547）で拒否される。アプリ側では既に編集不可にしているため通常経路では到達しないが、直接SQLを書いた場合の最後の防波堤になる。
- **メニュー構成マスタは親子関係（階層構造）とする。** 権限設定（最小必須権限）は子（末端の機能メニュー）側にのみ持たせ、親（分類の見出し）には権限を持たせない。詳細は2.7節。
- **売上・入金・受注のテーブルは、ヘッダーと明細を正規化（別テーブルに分割してFK参照）しない。** 明細行を単位とした1テーブル構成とし、ヘッダー相当の情報（得意先・日付等）は各明細行に持たせる（非正規化）。これは消費税計算単位（請求単位／伝票単位／明細単位）の3系統いずれにも適用する。
  - 売上・入金は、会計上のジャーナル（変更されない記録）としての意味も持つため、明細行単独で完結した記録である必要がある、というのが非正規化の主な理由。**「変更されない」とは常に修正不可という意味ではなく、確定されたら修正できないという意味。** 修正不可になる条件・元伝票の直接修正方式については本章の「ジャーナル系テーブルの編集ロック・訂正方式」を参照。
  - **受注も同じ1テーブル構成に統一する。** 受注は売上・入金と異なり状態が変化する仮伝票（未売上／一部売上／売上完了／中止、`docs/product-spec.md` の状態遷移を参照）でジャーナル性は無いが、テーブル構成の一貫性・実装の単純さを優先し、あえて分離しない。行の状態はキャッシュ列（`受注進捗状態`・`売上化済数量`）として明細行自体を更新する。
- **ジャーナル系のテーブル（`sales`／`receipts`／`detail_receipts`／`orders`）は、伝票摘要（`slip_remarks`）と行摘要（`line_remarks`）の両方のカラムを持つ。** 伝票摘要は自由記述のメモで、**同一伝票の全明細行に複写する**（`slip_date`／`customer_code` と同じ「伝票単位の値」。`SUM` 等での多重計上を避ける扱いは2.8節を参照）。行摘要は明細行ごとに独立した値を持つ。ヘッダーのみの集計テーブル（`billings`／`detail_invoices`）はジャーナルではないため対象外（2.8節）。
- **受注（`orders`）・売上（`sales`）は、上記に加えて社内摘要（`internal_remarks`）カラムを持つ。** `slip_remarks` と同じ「伝票単位の値」として同一伝票の全明細行に複写するが、**画面表示専用で、納品書には印字しない**点のみ `slip_remarks` と異なる。対象は受注・売上のみで、入金（`receipts`）・明細入金（`detail_receipts`）は対象外。
- **得意先マスタは、一般的な得意先情報として担当者名・住所を保持する。** 共通検索モーダル（伝票入力画面から共通で呼び出される得意先検索）が検索対象とする項目であり、得意先マスタの標準項目として設ける。
- **請求データ（請求残高）は明細行を持たず、集計値のみのヘッダー1テーブルとする。** 請求書発行時の明細部分は、請求データ側に保持せず、売上のジャーナルデータ（売上テーブルの明細行）を参照して都度組み立てる。
- **請求データ（請求残高）は、請求単位・伝票単位の間で構造が共通で、`billings` 1テーブルに保持する。** 両者は消費税計算のタイミング（請求時に一括計算 か 伝票登録時に計算済み）が違うだけで、集計後の請求データとしては同じ構造になるため、`tax_unit`（1=請求単位／2=伝票単位）で区別する。明細単位（都度得意先向けの明細請求書）は前月からの繰越残高という概念がなく構造が異なるため、この請求データとは別の既存概念（明細請求書）を使う。
- **インボイス対応のため、商品マスタに税種別区分（標準税率／軽減税率／非課税）を持ち、売上明細行・受注明細行には税率と税種別区分を転記して保持する。** 伝票時点の税率をスナップショットとして行内に持つことで、マスタ側の税率改定が既存伝票に影響しないようにする（非正規化方針と同じ趣旨）。税率別内訳の集計はこのカラムでグループ化して行う。税種別区分は識別子・カラム名に具体的な税率（%）をハードコードしない（税率改定で名称が実態と食い違うため）。実際の税率は税率マスタ（2.3節）で管理する。
- **商品マスタは、外税単価と内税単価を別カラムで持つ。** 得意先の税区分（`tax_unit`）は「請求単位」「伝票単位」「内税明細単位」のいずれかに固定されるため、**同一得意先への販売は常に外税か内税のどちらか一方のみ**（明細単位の得意先＝内税、それ以外の得意先＝外税で、内税と外税を混在させて売ることはない）。商品選択時は、対象得意先の `tax_unit` に応じて商品マスタの外税単価・内税単価のいずれかを売上明細行・受注明細行の `unit_price` に転記する（内税/外税の選択自体は商品側の属性ではなく、得意先の `tax_unit` から一意に決まる）。非課税品は税の内外の区別がないため、外税単価・内税単価に同じ値を設定する運用とする。
- **請求データ（請求残高）に、確定時点の税率別内訳を保持する。** 保持方法は子テーブルではなく**税種別区分ごとの固定カラム**（例: 標準税率対価額／標準税率消費税額／軽減税率対価額／軽減税率消費税額／非課税対価額。カラム名に具体的な税率（%）はハードコードしない）とする。日本の税種別区分は少数の閉じた集合であり、「請求データは集計値のみのヘッダー1テーブル」という方針を崩さずに済むため。請求書の**明細部分**は従来方針どおり売上ジャーナルから都度組み立てるが、**税額は請求データ側の確定値を印字**し、再発行時に金額が変わらないようにする。
- **自社情報マスタを設ける。** 適格請求書発行事業者の登録番号・自社名称・住所・代表者名を保持し、請求書・明細請求書・納品書の発行元情報として参照する。登録番号は法定記載事項であり、コードへのハードコードは行わない。原則1レコード運用とする。**振込口座は自社情報マスタではなく銀行口座マスタ（`bank_accounts`）で管理し、請求書に印字する口座は得意先マスタの `bank_account_code1`／`bank_account_code2` に紐づける**（口座は複数持ちうるため、自社情報マスタに1組だけ持たせると銀行マスタと重複する）。
- **伝票の状態は、軸ごとに独立したカラムとして保持する（単一のステータスカラムに集約しない）。** 各状態の業務上の意味・遷移は `docs/product-spec.md` の「伝票の状態遷移」を正とし、保持するカラムは2.18節に一覧する。状態カラムはすべてキャッシュ列で、関連伝票の登録・取消・訂正と同一トランザクション内で更新する（2.18節）。
- **月次締め（`monthly_closings`）は、得意先ごとの暦月末時点の売掛残高を保持するテーブルとする（得意先×月末日で1レコード、`billings`類似レイアウト）。** 締め得意先への請求（`billings`）は得意先ごとの締め日（`closing_day`）期間で集計するが、会計上の月次売掛金は全得意先を暦月（月初〜月末）で集計する必要があり、両者の集計期間が一致しないため。**「請求締め」と「月次締め」は別々の締め処理として併存する。** 都度得意先（`tax_unit=3`）も含め全得意先が対象。
  - 20日締めの得意先の例: `billings`は1/21〜2/20を集計するが、`monthly_closings`は2/1〜2/28を集計する。2/21〜2/28分の売上は、その得意先自身の請求締め（次回3/20締め）をまだ通っておらず、`tax_unit=1`（請求単位）の得意先は伝票時点で税額を確定しない設計（2.9節）のため、この区間の税額は`monthly_closings`確定処理が`ConsumptionTaxCalculator`を「確定させずに」呼び出して仮計算し、`monthly_closings`側のカラムにのみ保存する（`sales.slip_tax_amount`には書き込まない。CHECK制約 `CK_sales_tax_amount_by_tax_unit` に違反するため）。
  - 状態（確定／解除済）、確定日時・確定者、解除日時・解除者を保持する（`billings`と同じ非破壊方式。締め解除で物理削除しない）。
- **ジャーナル系テーブル（`sales`／`receipts`／`detail_receipts`／`orders`）の編集ロック・訂正方式。** 訂正・取消は**元伝票の直接修正**とし、赤伝（マイナス伝票）方式は採用しない。伝票側にフラグを持たず、次のいずれかに該当する伝票行のみ編集不可（それ以外は直接修正可能）。**4条件は`sales`にのみそのまま適用し、`receipts`／`detail_receipts`／`orders`はテーブルごとに適用範囲が異なる（後述）。**
  1. **請求締め**: 対象行が確定済みの `billings` に集計済み（`sales.billing_number` が確定済み `billings` を指す）
  2. **明細請求書発行済み**: 都度得意先（`tax_unit=3`）の対象行が `detail_invoice_sales_lines` に連携済み。都度得意先は `billing_number` が常にNULLで条件①が発火しないため、この条件が無いと発行済みの `detail_invoices` ヘッダー（確定金額のスナップショット）が実データと乖離する（`docs/design_document.md` 13章）
  3. **月次締め**: 対象行の `customer_code` と伝票日付の年月に一致する `monthly_closings` レコードが存在し、`closing_status`＝確定（`customer_code` は売上の得意先、またはその請求集約先。請求集約先の行はグループ全体を含むため。`docs/design_document.md` 29-2節）
  4. **入金済み**: `sales.settlement_status`＝消込完了
  - 締め・解除のたびに大量の伝票行を更新するのを避けるため、既存のキャッシュ列（`billing_number`／`closing_status`／`settlement_status`／`detail_invoice_sales_lines`の存在等）のみで導出する。**この「編集不可」はユーザーによる伝票内容の直接編集を指す。** 締め処理自身が状態カラム（`billing_number`等）を更新することはロック対象外（システム内部の状態遷移であり、ユーザー編集ではないため）。
  - **`orders`（受注）はこの4条件のいずれにも該当しない**（受注は請求・消込の対象外）が、**未売上（`order_status`＝未売上）の伝票のみ直接修正可能**。一部売上・売上完了・中止済みの伝票は読込・表示はできるが修正できない（`OrderEditLockEvaluator`、Domain純粋関数。判定は明細行の`order_status`のみで行い、外部テーブル照会は不要）。中止（伝票単位）は一部売上でも可能なのに対し修正は未売上限定という非対称は意図した仕様。
  - **`receipts`（締め入金）・`detail_receipts`（明細入金）は`sales`とは別の条件を持つ。** 条件④（入金済み＝`allocation_status`＝充当完了）を適用しないのは、入金は保存直後にほぼ必ず充当完了になるため、適用すると訂正・取消できる入金がほぼ存在しなくなるため。条件①（請求締め）は、`billing_number` を `receipt_allocations` へ分離しており `receipts` 自身は持たないため使えない。テーブルごとの条件は次のとおり。
    - **`detail_receipts`**: ③（月次締めのみ）。`detail_invoices`（明細請求書）の金額は`sales`から都度導出され`detail_receipts`からスナップショットを焼き込まれないため、締め請求のような追加ロックは不要。
    - **`receipts`**: ③（月次締め）に加え、**「請求締めスナップショット」**という独自条件を持つ: `receipt_date <= その得意先の確定済み billing のうち最新の billing_date`。`BillingClosingService`が締め処理時に `receipt.Amount` の合計（前回確定`billing.billing_date`〜今回`closing_date`の期間で集計）を `billing.current_billing_amount` へスナップショットとして焼き込み、以後誰も再計算しないため、この期間に属する`receipts`を無条件に取消・訂正できると確定済み請求の残高が二重計上・二重減算のいずれかで永久に狂う。この期間の`receipts`を訂正・取消したい場合は、対象の`billings`を締め解除（`docs/design_document.md` 10章）してから行う（解除により対象外の確定済み`billings`が別に存在すれば、それが新たな基準日になる）。
    - 実装は`ReceiptEntryService.EvaluateEditLockAsync`／`DetailReceiptEntryService.EvaluateEditLockAsync`（Application/Receipt）。専用の編集ロック判定クラス（`SalesEditLockService`相当）は作らず、判定条件が単純なため各サービスの public メソッドとする。詳細は`docs/design_document.md` 19章。
    - **この編集ロックは既存行の編集・訂正のみを対象とする。** 新規登録・日付変更で締め済み期間に
      入り込むこと自体は別機構（「ジャーナル系の日付制限」、`BillingClosedDateEvaluator`／
      `BillingClosedDateService`）が防ぐ。詳細は`docs/design_document.md` 25章。
- **ステータス値はDB側 `tinyint`、C#側は enum で扱う。** 文字列コードは使わず、画面表示名はアプリ側で解決する。
- 暫定運用中の項目は4章「暫定運用中の項目」を参照。

### 1-1. 親子請求（請求集約）の設計方針

**用語**: 他の得意先の分もまとめて請求される得意先を**請求集約先**、請求が他の得意先に集約される得意先を**請求集約元**、1つの請求集約先とそれを指すすべての請求集約元の集合を**請求集約グループ**と呼ぶ。「親得意先」「子得意先」という語は使わない（`sub_customer_id` の文脈で使われる「子得意先」と紛れるため）。

**業務ルール**:

| # | 決定 |
|---|---|
| 1 | 対象は締め得意先のみ（`tax_unit`=1 請求単位／2 伝票単位）。都度得意先（`tax_unit`=3）は請求集約元になれない |
| 2 | 請求集約先と請求集約元は `closing_day`・`tax_unit`・`rounding_type` がすべて一致していなければならない |
| 3 | **請求データ（`billings`）は請求集約先にだけ作る。** 請求集約元の売上は請求集約先の `billing` に取り込み、`sales.billing_number` に請求集約先の請求番号を書く。請求集約元には `billings` を一切作らない |
| 4 | 入金は請求集約先にだけ入る。入金入力画面で請求集約元を指定したら業務例外で拒否する |
| 5 | 売掛残高の管理は請求集約先に集約する（請求集約元では残高を管理しない） |
| 6 | 得意先元帳: 請求集約先は配下の請求集約元の売上も含めて表示する（残高を正しくするため必須）。請求集約元は取引履歴（売上）のみを表示し、残高・繰越は表示しない |
| 7 | 請求得意先コードは、確定済み `billings` に取り込まれた売上が1件でもある得意先は変更不可 |
| 8 | 請求集約元の請求集約元は不可（階層は2段まで） |
| 9 | 月次締め（`monthly_closings`）は請求集約元ごとに個別集計する（2.16節参照） |

**中心的な設計判断 ― スコープキーを「得意先」から「請求集約グループ」に広げる。** 請求・入金・消込・残高に関わる処理（`BillingClosingService`／`BillingReleaseService`／`SettlementService`／`ReceiptEntryService`／`CustomerLedgerQueryService`／`BillingClosedDateService`）は、対象得意先の請求集約先を解決してからグループ全体（請求集約先＋全請求集約元）を読む。**売上・受注・納品書・商品単価履歴など、伝票そのものを扱う処理は `customer_code` 単位のまま変えない**（納品書は実際に納品した請求集約元宛に出すのが正しく、商品単価履歴も請求集約元ごとの実績であるべきため）。

この切り分けにより、既存の「得意先スコープで全件再計算する」（`SettlementService`。得意先スコープにした理由は2.10-1節参照）・「繰越は全期間積み上げ」（`CustomerLedgerBuilder`）という設計思想はそのまま維持される。スコープが1得意先からグループに変わるだけで、配分・積み上げのアルゴリズム自体は変更しない。例えば `SettlementService` の消込配分は、対象の `sales`/`receipts`/`receipt_allocations` を取得するクエリの絞り込み条件を「対象得意先コード」から「グループの得意先コード群（`IN`）」に変えるだけでよく、`billing_number` でグルーピングして配分する既存ロジックが請求集約先・請求集約元の売上を自然に1つのグループとして扱う。

**`rounding_type`（端数区分）も請求集約先・請求集約元で一致させる理由**: `tax_unit`=2（伝票単位）の得意先は、売上入力時に自分自身の `rounding_type` で `slip_tax_amount` を確定する。請求締め時に `BillingClosingService` は再計算した伝票税額と保存済み `slip_tax_amount` の合計を突合し、不一致なら例外を投げて締め処理そのものが失敗する。請求集約先と請求集約元で端数区分がずれたまま請求集約元に売上が入ると、この不一致が締め当日に顕在化し、請求集約グループ全体の請求書が発行できなくなる。`rounding_type` は登録後変更不可（1章）のため、**端数区分が異なる既存の2得意先は請求集約先・請求集約元の関係にできない**（どちらかの得意先コードを新規に取り直す必要がある）。

**日付制限への影響**: `BillingClosedDateService`（「ジャーナル系の日付制限」、1章）は確定済み `billings` の最新 `billing_date` を基準に、それ以前の日付での新規登録・変更を禁止する。請求集約元には `billings` が存在しないため、`GetLatestConfirmedBillingDateAsync` は `billings` を引く前に対象得意先の `BillingCustomerCode`（請求集約先）を解決する。自身の `billings` しか見ないと請求集約元の売上にはこの日付制限が効かず、確定済み請求期間に請求集約元の売上を遡及登録できてしまうため。詳細は `docs/design_document.md` 28-6節。

**元帳・月次締めが請求集約元ごとに個別集計を維持する理由**: 得意先元帳・月次締め（`monthly_closings`）は、会計上の売掛金集計であると同時に、支店別の営業実績（担当者別売上・粗利、`customers.sales_employee_code`）の集計単位でもある。請求集約元の実績を請求集約先に合算すると支店別実績が失われるため、**売上・元帳の取引履歴・月次の実績集計は常に `customer_code`（請求集約元）単位を維持し、残高・請求・入金だけを請求集約先に集約する**。

**確定済み請求を持つ得意先の役割変更を禁止する理由（業務ルール7）**: 請求得意先コードを変更できるのを「確定済み `billings` に取り込まれた売上が無い」場合に限ることで、過去に発行済みの請求書と現在の `sales.billing_number` の対応関係が変わらないことを保証する。締め解除すると `billing_number` が `NULL` に戻るため、解除後は再び変更可能になる（締め解除の意味論と整合する）。

**運用上の注意**: 一度も締めたことがない得意先を後から請求集約元に変更すると、その得意先の未請求売上（`billing_number IS NULL`）はすべて次回の請求集約先の請求に合算される（`BillingClosingService` の売上抽出には期間下限が無いため）。業務ルール7の判定はこのケースを弾かないため、得意先マスタ画面側で変更前に警告を表示する。

---

## 2. テーブル定義

### 2.0. 全テーブル共通の規約

| 項目 | 決定 | 理由 |
|---|---|---|
| テーブル名 | 複数形 `snake_case` | EF Core の `DbSet` 命名（複数形）と DB のテーブル名を一致させる。`orders`（受注）・`tax_rates`（税率マスタ）は単純な複数形化ではなく業務上の呼称に合わせた名称。`sales` は単数形と複数形が同形 |
| 主キー | **業務コードをそのまま主キーにする。** サロゲートキーは使わない | 伝票側は非正規化で得意先コード等を行内に持つ方針のため、サロゲートキーを挟むと JOIN が増えるだけで利点がない |
| コード | `varchar(n)` | 日本語を含まないため |
| 名称・住所 | `nvarchar(n)` | |
| 金額 | `decimal(15, 2)` | |
| 単価・原価 | `decimal(15, 4)` | 0.5円単位などの端数単価に対応するため |
| 数量 | `decimal(13, 3)` | |
| 税率 | `decimal(5, 2)` | |
| 区分値 | `tinyint`（C# 側は enum） | 前章の方針どおり |
| 日時 | `datetime2(3)` | |

**全テーブルが持つ共通カラム**（以下の各テーブル定義では省略する）:

| カラム | 型 | 内容 |
|---|---|---|
| `is_deleted` | `bit NOT NULL DEFAULT 0` | 論理削除フラグ。マスタは論理削除、伝票は物理削除しない |
| `created_by` | `varchar(10) NOT NULL` | 作成者の社員コード |
| `created_at` | `datetime2(3) NOT NULL` | |
| `updated_by` | `varchar(10) NOT NULL` | 更新者の社員コード |
| `updated_at` | `datetime2(3) NOT NULL` | |
| `row_version` | `rowversion NOT NULL` | 楽観的排他制御。適用単位は `docs/architecture.md` 9章 |

**共通カラムの例外**:

| テーブル | 例外 | 理由 |
|---|---|---|
| `detail_invoice_sales_lines`（連携） | `row_version` を持たない | 行の追加・削除しか発生せず、更新がないため |
| `slip_number_sequences`（採番） | `row_version` を持たない | 採番は `UPDATE` の行ロックで直列化する。楽観的排他だと競合時にリトライが必要になり、採番の直列性と相性が悪い |
| `slip_number_sequences`（採番） | `is_deleted` を持たない | 伝票種別ごとに1行を永続保持するため |

---

### 2.1. `customers`（得意先マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `customer_code` | `varchar(10)` | PK | 得意先コード |
| `customer_name` | `nvarchar(60)` | × | 得意先名称 |
| `customer_name_kana` | `nvarchar(60)` | ○ | カナ。共通検索モーダルの検索対象 |
| `postal_code` | `varchar(8)` | ○ | |
| `address1` | `nvarchar(100)` | ○ | 共通検索モーダルの検索対象 |
| `address2` | `nvarchar(100)` | ○ | |
| `phone_number` | `varchar(20)` | ○ | |
| `fax_number` | `varchar(20)` | ○ | |
| `contact_person_name` | `nvarchar(40)` | ○ | **得意先側**の担当者名。共通検索モーダルの検索対象 |
| `sales_employee_code` | `varchar(10)` | ○ | **自社の**営業担当社員コード（FK → `employees`）。月次締めの担当者別売上・粗利の集計キー |
| `closing_day` | `tinyint` | × | `0`＝都度・明細／`1`〜`31`＝締め日（実日付）。**末日締めは `99` で表す**（実日付31日と区別するための専用値） |
| `tax_unit` | `tinyint` | × | `1`＝請求単位／`2`＝伝票単位／`3`＝内税明細単位 |
| `rounding_type` | `tinyint` | × | `1`＝切捨／`2`＝四捨五入／`3`＝切上 |
| `print_representative_flag` | `bit` | × | 請求書への代表者印字の要否 |
| `billing_customer_code` | `varchar(10)` | × | **請求得意先コード**。自分自身を指せば単独で請求、他の得意先を指せばその得意先を**請求集約先**として売上を集約する。詳細は1-1節・`docs/design_document.md` の親子請求（請求集約）章を参照 |
| `bank_account_code1` | `varchar(10)` | ○ | **振込先口座1**（FK → `bank_accounts`）。請求書・明細請求書の自社名の下に印字する。空欄なら印字しない |
| `bank_account_code2` | `varchar(10)` | ○ | **振込先口座2**（同上）。`bank_account_code1` と同一の口座は指定できない（`CK_customers_bank_account_distinct`） |
| `is_billing_root`（計算列） | `bit` | × | `billing_customer_code = customer_code` なら`1`（＝**請求集約先**または単独）、異なれば`0`（＝**請求集約元**）。`PERSISTED` の永続化計算列。**EFエンティティにはマップしない**（アプリからは`BillingCustomerCode == CustomerCode`で同じ判定ができるため） |
| `billing_parent_root_flag`（計算列） | `bit` | × | 常に`1`の定数の永続化計算列。下記の複合FKで「参照先は必ず請求集約先自身」を強制するために存在する（CHECK制約は他行を参照できないため、この強制はFKでしか表現できない）。**EFエンティティにはマップしない** |

**「得意先側の担当者名」と「自社の営業担当社員コード」を別項目にしている**（同じ「担当者」という語で別概念を指すため）。月次締めの担当者別集計は後者を使う。

**CHECK 制約**:

| 制約名 | 条件 | 目的 |
|---|---|---|
| `CK_customers_tax_unit_closing_day` | `(tax_unit = 3 AND closing_day = 0) OR (tax_unit IN (1,2) AND (closing_day BETWEEN 1 AND 31 OR closing_day = 99))` | **前章の相互制約（`税区分 = 内税明細単位 ⇔ closing_day = 0`）を DB 側で強制する。** 破綻する組み合わせ（締め得意先×明細単位／都度得意先×請求単位）を登録できないようにする |
| `CK_customers_tax_unit` | `tax_unit IN (1, 2, 3)` | |
| `CK_customers_rounding_type` | `rounding_type IN (1, 2, 3)` | |
| `CK_customers_closing_day` | `closing_day BETWEEN 0 AND 31 OR closing_day = 99` | |
| `CK_customers_billing_customer_tax_unit` | `billing_customer_code = customer_code OR tax_unit IN (1, 2)` | **都度得意先（`tax_unit=3`）は請求集約元になれない。** 複合FK（下記）だけでは「都度得意先どうしの請求集約」を防げないため、この CHECK が唯一の防壁になる |
| `CK_customers_bank_account_distinct` | `bank_account_code1 IS NULL OR bank_account_code2 IS NULL OR bank_account_code1 <> bank_account_code2` | 振込先口座1・2に同一口座を重複指定できないようにする |

**`UNIQUE (customer_code, tax_unit)`（`UQ_customers_code_tax_unit`）を持つ。**`sales`/`receipts`/`billings` から `(customer_code, tax_unit)` の複合FKで参照させ、伝票の税単位が得意先マスタの税区分と一致することをDBで強制するための一意インデックス。`customer_code` は既にPKで一意なのでこの制約自体が既存データを弾くことはない。

**締め区分（`closing_day` の 0／非0）と `tax_unit` は登録後に変更できない。** アプリ側で更新時は読み取り専用にする（前章の方針）。前章末尾のとおり、伝票が存在する得意先の `tax_unit` を直接 `UPDATE` すると複合FK違反（Msg 547）で拒否される。

**`rounding_type`（端数区分）も登録後に変更できない**。変更を許すと発行済み伝票の消費税額を後から再現できず、請求締めで金額が合わなくなるため、`closing_day`/`tax_unit` と同じ扱いにする。

**現時点で持たない項目**: 支払条件（サイト）、与信限度額。設計資料に記載がなく推測になるため。締め得意先の運用で必要になる可能性があり、`docs/design_document.md` の確認事項に記録している。

#### `billing_customer_code`（請求得意先コード）の制約（1-1節参照）

**`UNIQUE (customer_code, is_billing_root, closing_day, tax_unit, rounding_type)`（`UQ_customers_billing_root_key`）を持つ。** `customer_code` が既にPKで一意なのでこの制約自体が既存データを弾くことはない。

**自己参照の複合FK `FK_customers_billing_customer`**: `(billing_customer_code, billing_parent_root_flag, closing_day, tax_unit, rounding_type)` → 上記 `UQ_customers_billing_root_key`。これにより次の3つが同時にDB側で強制される。

1. **請求得意先コードは実在する得意先を指す**（FK違反で拒否）
2. **指し先は必ず請求集約先自身**（`is_billing_root = 1` の行しか参照先候補に無いため、請求集約元をさらに別の得意先の請求集約元にすること＝2段以上の階層は複合FK違反で拒否される）
3. **請求集約先と請求集約元は `closing_day`・`tax_unit`・`rounding_type` が一致する**（一致しない組み合わせは参照先の候補行が存在せずFK違反になる）

`billing_parent_root_flag` という常に`1`の定数の計算列を参照側・被参照側の両方に挟んでいるのは、CHECK制約が同一テーブルの他行を参照できない（＝「指し先が請求集約先である」ことをCHECK単体では表現できない）ため、複合FKで表現するための技巧である。自己参照FKのため `ON UPDATE/DELETE CASCADE` は使えない（SQL Serverの制限）が、`closing_day`/`tax_unit`/`rounding_type` はいずれも登録後変更不可、`customers` は論理削除（物理DELETEしない）という既存方針と両立するため実害はない。

**`billing_customer_code` は登録後いつでも変更できるわけではない。** 確定済み `billings` に取り込まれた売上（`sales.billing_number IS NOT NULL`）が1件でもある得意先は変更不可（業務ルール7）。`closing_day`/`tax_unit`/`rounding_type`のような「新規登録時のみ入力可」ではなく、**未請求の間は請求集約先・請求集約元とも自由に変更できる**点が異なる。

**複合自己参照FKはEF Coreでは表現できない**（`billing_parent_root_flag` が定数列のため）。`CustomerConfiguration` には `billing_customer_code` 列のマッピングのみを登録し、FK制約自体はDB側のみで強制する。この帰結として、**請求集約先と請求集約元を同一の `SaveChanges` で同時に新規作成することはできない**（EFがFK関係を認識できないため挿入順序を解決できない）。得意先マスタ画面は1件ずつ保存するため実運用上の制約にはならない。

---

### 2.2. `products`（商品マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `product_code` | `varchar(20)` | PK | 社内商品コード（JAN コードではない） |
| `product_name` | `nvarchar(60)` | × | |
| `product_name_kana` | `nvarchar(60)` | ○ | 共通検索モーダルの検索対象 |
| `specification` | `nvarchar(60)` | ○ | 規格 |
| `unit_name` | `nvarchar(10)` | ○ | 単位（個・本・ケース等） |
| `standard_unit_price_excl_tax` | `decimal(15,4)` | × | 外税単価（税抜）。得意先の `tax_unit` が請求単位／伝票単位の場合、伝票入力時の単価初期値としてここから転記する |
| `standard_unit_price_incl_tax` | `decimal(15,4)` | × | 内税単価（税込）。得意先の `tax_unit` が内税明細単位の場合、伝票入力時の単価初期値としてここから転記する |
| `standard_cost_price` | `decimal(15,4)` | × | 標準原価。粗利計算用に伝票明細へ転記する |
| `tax_category` | `tinyint` | × | `1`＝標準税率／`2`＝軽減税率／`3`＝非課税。**具体的な税率（%）は持たず、税率マスタ（2.3節）から引く** |

**CHECK 制約**: `CK_products_tax_category` … `tax_category IN (1, 2, 3)`（税種別区分は3区分のみ。「不課税」は区分として存在しない）

**税種別区分から具体的な税率への対応は税率マスタ（2.3節）を参照する。** 税率の解決方法は2.3節を参照。

**単価計算マスタは作らない（現時点）。** 単価は商品マスタの外税単価・内税単価（得意先の `tax_unit` に応じたどちらか一方）を初期値として転記し、手入力で上書きする方式。単価列の選択ロジックは `IUnitPriceCalculator`（`src/bmcs_app.Domain/Calculations/IUnitPriceCalculator.cs`）インターフェースと、その現時点の実装 `StandardUnitPriceCalculator`に集約する。**将来的に掛け率マスタ等を実装する予定があるため、単価決定ロジックを差し替え可能なインターフェース（ドライバ）として設計している（「将来の差し替えを見据えた抽象化は行わない」という既定方針の例外として明示的に採用）。** 掛け率マスタ実装時は `StandardUnitPriceCalculator` を差し替える（DI登録は `ApplicationServiceCollectionExtensions.AddApplication`）だけで対応する想定。

**原価は商品検索モーダルの「過去の取引履歴から」軸でも商品マスタの標準原価を転記する（暫定）。** 過去の売上行が保持する原価（`sales.cost_price`）は当時のスナップショットだが、粗利計算には現在の標準原価を使う方針とし、履歴軸から選んだ場合も再照会せず商品マスタから取り直す。過去実績原価での粗利計算が必要になった場合は `ProductSearchDialogViewModel.AddHistoryToBasket` を直接修正する。

---

### 2.3. `tax_rates`（税率マスタ）

**施行日付きの税率マスタとする。** 税種別区分（`tax_category`）から税率への対応を Domain 層の定数として持たず、税率改定にコード修正なしで追従できるようにする。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `effective_date` | `date` | PK | 適用開始日。この日付以降（次のレコードの適用開始日の前日まで）に適用される税率を表す |
| `standard_tax_rate` | `decimal(5,2)` | × | 通常税率（`tax_category = 1`） |
| `reduced_tax_rate` | `decimal(5,2)` | × | 軽減税率（`tax_category = 2`） |

**終了日は持たない範囲設定。** 適用期間は「自分の `effective_date` 〜 次に新しい `effective_date` を持つレコードの前日まで（最新レコードは無期限）」で決まるため、終了日カラムは不要（隙間・重複が生じない）。

**税率の解決方法**: 伝票登録時、対象の伝票日付以前で最も新しい `effective_date` を持つレコード（`WHERE effective_date <= @slip_date ORDER BY effective_date DESC` の先頭1件）を採用し、`tax_category` に応じて `standard_tax_rate` / `reduced_tax_rate` のいずれかを売上明細行・受注明細行の `tax_rate` に転記する。転記後は伝票側にスナップショットとして残るため、マスタ側の税率改定が既存伝票に影響しないという既存方針（1章）は変わらない。

**非課税（`tax_category = 3`）は本マスタを参照しない。** 非課税の `tax_rate` は `0` を転記する。

---

### 2.4. `employees`（社員マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `employee_code` | `varchar(10)` | PK | 社員コード。起動時パラメータで渡される値 |
| `employee_name` | `nvarchar(40)` | × | |
| `employee_name_kana` | `nvarchar(40)` | ○ | |
| `permission_level` | `tinyint` | × | 権限レベル。`menus.required_permission_level` と比較する |

---

### 2.5. `company_infos`（自社情報マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `company_info_id` | `tinyint` | PK | **固定値 `1`**。1レコード運用 |
| `company_name` | `nvarchar(60)` | × | |
| `invoice_registration_number` | `varchar(14)` | × | 適格請求書発行事業者の登録番号（`T` ＋13桁）。法定記載事項 |
| `postal_code` | `varchar(8)` | ○ | |
| `address1` / `address2` | `nvarchar(100)` | ○ | |
| `phone_number` / `fax_number` | `varchar(20)` | ○ | |
| `representative_name` | `nvarchar(40)` | ○ | 代表者名 |

**CHECK 制約**: `CK_company_infos_single_row` … `company_info_id = 1`（複数行の登録を防ぐ）

振込口座は持たない（`bank_accounts` を参照）。

---

### 2.6. `bank_accounts`（銀行口座マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `bank_account_code` | `varchar(10)` | PK | |
| `bank_name` | `nvarchar(40)` | × | 銀行名 |
| `branch_name` | `nvarchar(40)` | × | 支店名 |
| `account_type` | `tinyint` | × | `1`＝普通／`2`＝当座 |
| `account_number` | `varchar(10)` | × | |
| `account_holder_name` | `nvarchar(60)` | × | 口座名義 |
| `display_order` | `smallint` | × | 表示順（銀行マスタ画面の一覧順） |

**CHECK 制約**: `CK_bank_accounts_type` … `account_type IN (1, 2)`

**このテーブルの用途は自社の振込先口座マスタ。** 得意先マスタ（2.1節）の `bank_account_code1`／`bank_account_code2` から最大2件紐づけて、請求書・明細請求書に得意先ごとに印字する（`docs/report-spec.md` 2-2節）。全社共通でフッターに印字する口座を選ぶフラグ方式は採らない（得意先ごとに振込先を使い分けるため）。

---

### 2.6-1. `deposit_methods`（入金方法マスタ）

入金方法（現金・振込・手形・相殺等）を管理するマスタ。利用者が入金方法を自由に追加・編集できるようマスタ駆動とし、tinyint固定値のenumは使わない（enumでは入金方法の追加・改称のたびにコード修正・再ビルドが必要になるため。理由・検討経緯は`docs/design_document.md` 27章を参照）。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `deposit_method_code` | `varchar(10)` | PK | |
| `deposit_method_name` | `nvarchar(20)` | × | 画面表示名（「現金」等） |
| `requires_bank_account` | `bit` | × | この入金方法を選んだ行に入金先口座の指定を要するか |
| `requires_bill_due_date` | `bit` | × | この入金方法を選んだ行に手形期日の指定を要するか |
| `display_order` | `smallint` | × | 表示順 |

**CHECK 制約**: `CK_deposit_methods_requires` … `NOT (requires_bank_account = 1 AND requires_bill_due_date = 1)`（口座と手形期日を同時に必須にはできない）。

**「振込なら口座必須」「手形なら期日必須」という対応関係は、他テーブル（`deposit_methods`）を参照する必要がありDBのCHECK制約では表現できないため、アプリ層（`ReceiptEntryService.ValidateLinesAsync`／`DetailReceiptEntryService.ValidateLineFieldsAsync`）のみで担保する。** トリガーは作らない（このリポジトリにトリガー・ユーザー定義SPは無く、`rowversion`楽観的排他との相性も悪いため）。

初期データ: `CASH`（現金）／`TRANSFER`（振込・`requires_bank_account=1`）／`NOTE`（手形・`requires_bill_due_date=1`）／`OFFSET`（相殺）。

---

### 2.7. `menus`（メニュー構成マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `menu_code` | `varchar(20)` | PK | |
| `parent_menu_code` | `varchar(20)` | ○ | 親メニュー（FK → `menus` の自己参照）。`NULL`＝最上位 |
| `menu_name` | `nvarchar(40)` | × | |
| `display_order` | `smallint` | × | 同一階層内の表示順 |
| `required_permission_level` | `tinyint` | ○ | 最小必須権限。**子（末端の機能メニュー）のみ設定し、親は `NULL`** |
| `screen_key` | `varchar(40)` | ○ | 起動する画面の識別子。親は `NULL` |

**CHECK 制約**: `CK_menus_leaf` … `(screen_key IS NULL AND required_permission_level IS NULL) OR (screen_key IS NOT NULL AND required_permission_level IS NOT NULL)`

→ 「権限は子にのみ持たせ、親（分類の見出し）には持たせない」という方針を DB 側で強制する。親には遷移先画面も権限もなく、子には両方ある。社員マスタの `permission_level`（2.4節）を子の `required_permission_level` と比較する。

---

### 2.8. 伝票系テーブルの全体像

| 業務概念 | テーブル | 構成 |
|---|---|---|
| 売上（全税単位共通） | `sales` | 明細行1テーブル |
| 入金（締め入金。請求単位／伝票単位共通） | `receipts` | 明細行1テーブル（支払手段の内訳） |
| 締め入金の請求への充当（内部データ・画面には非表示） | `receipt_allocations` | 明細行1テーブル |
| 明細入金（明細単位） | `detail_receipts` | 明細行1テーブル |
| 請求データ（請求単位／伝票単位共通） | `billings` | ヘッダーのみ |
| 明細請求書 | `detail_invoices` | ヘッダーのみ |
| 明細請求書 ↔ 売上明細行の連携 | `detail_invoice_sales_lines` | 連携（多対多） |

税単位は各テーブルの `tax_unit` カラム（1=請求単位／2=伝票単位／3=内税明細単位）で表す。

得意先の税区分によって、その得意先のデータがどのテーブル・`tax_unit` 値に入るかが一意に決まる。

| 得意先の税区分 | 売上 | 入金 | 請求 |
|---|---|---|---|
| 請求単位 | `sales`（`tax_unit=1`） | `receipts`（`tax_unit=1`） | `billings`（`tax_unit=1`） |
| 伝票単位 | `sales`（`tax_unit=2`） | `receipts`（`tax_unit=2`） | `billings`（`tax_unit=2`） |
| 内税明細単位（＝都度得意先） | `sales`（`tax_unit=3`） | `detail_receipts` | `detail_invoices`（繰越残高の概念がないため請求データではない） |

**この対応はDBの複合FKで強制される。** `customers` の `UNIQUE (customer_code, tax_unit)` を `sales`/`receipts`/`billings` から `(customer_code, tax_unit)` の複合FKで参照するため、得意先マスタの税区分と異なる `tax_unit` でINSERTすることはできない。同様に `billings` の `UNIQUE (billing_number, tax_unit)` を `sales`/`receipt_allocations` から複合FKで参照するため、税単位をまたいで請求データを参照することもできない（`billing_number IS NULL` の行はFK検査対象外）。

#### 非正規化構成の帰結（重要な運用ルール）

売上・入金・明細入金は明細行1テーブル構成のため、**伝票単位の値（伝票日付・得意先コード・入金額・伝票単位の消費税額など）は、同一伝票の全明細行に同じ値が入る。**

> **これらのカラムを `SUM` してはいけない。** 伝票の行数だけ多重計上される。伝票単位で1行に絞ってから扱う（`GROUP BY` 伝票番号など）。

伝票摘要（`slip_remarks`）・社内摘要（`internal_remarks`）も同じ伝票単位の値で、同一伝票の全明細行に複写されるため、集計で `SUM` 等をしない。

**伝票の取消は共通カラムの `is_deleted` で表す**（物理削除しない）。誰がいつ取消したかは監査列で追跡する。

---

### 2.9. 売上（`sales`）

**主キーは (`sales_slip_number`, `line_number`) の複合キー。**

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `sales_slip_number` | `varchar(20)` | PK | 売上伝票番号 |
| `line_number` | `smallint` | PK | 行番号 |
| `slip_date` | `date` | × | 伝票日付（**伝票単位の値**） |
| `customer_code` | `varchar(10)` | × | 得意先コード（**伝票単位の値**） |
| `tax_unit` | `tinyint` | × | `1`＝請求単位／`2`＝伝票単位／`3`＝内税明細単位。得意先マスタの税区分と複合FKで一致を強制（2.8節） |
| `customer_name` | `nvarchar(60)` | × | 得意先名称の**スナップショット** |
| `slip_type` | `tinyint` | × | `1`＝売上／`2`＝返品／`3`＝値引 |
| `product_code` | `varchar(20)` | × | |
| `product_name` | `nvarchar(60)` | × | 商品名の**スナップショット** |
| `specification` | `nvarchar(60)` | ○ | 規格のスナップショット |
| `unit_name` | `nvarchar(10)` | ○ | 単位のスナップショット |
| `quantity` | `decimal(13,3)` | × | 数量。返品・値引はマイナス |
| `unit_price` | `decimal(15,4)` | × | 単価。`tax_unit` に応じて商品マスタの外税単価／内税単価のいずれかを転記した**スナップショット**（`tax_unit=3` は内税単価、`tax_unit=1,2` は外税単価） |
| `amount` | `decimal(15,2)` | × | 金額 |
| `cost_price` | `decimal(15,4)` | × | 原価のスナップショット（粗利計算用） |
| `tax_category` | `tinyint` | × | 税種別区分の**スナップショット** |
| `tax_rate` | `decimal(5,2)` | × | 税率の**スナップショット** |
| `slip_tax_amount` | `decimal(15,2)` | ○ | **伝票単位**の税額。同一伝票の全行に同値。`tax_unit=2` のときのみ値を持つ |
| `tax_amount` | `decimal(15,2)` | ○ | 行ごとの内税額。`tax_unit=3` のときのみ値を持つ（`amount` は税込金額） |
| `delivery_note_issued_at` | `datetime2(3)` | ○ | 納品書発行日時。**`NULL`＝未発行**（一括発行の対象） |
| `delivery_note_issue_count` | `smallint` | × | 発行回数（再発行で加算） |
| `billing_status` | `tinyint` | × | `1`＝未請求／`2`＝請求済 |
| `settlement_status` | `tinyint` | × | `1`＝未消込／`2`＝一部消込／`3`＝消込完了 |
| `settled_amount` | `decimal(15,2)` | × | 消込済金額 |
| `order_slip_number` | `varchar(20)` | ○ | 受注からの売上化の場合の受注伝票番号 |
| `order_line_number` | `smallint` | ○ | 同、行番号 |
| `billing_number` | `varchar(20)` | ○ | 請求データへの参照。**`NULL`＝未請求**、または `tax_unit=3`（明細請求書との紐付けは連携テーブル2.14で行うため常にNULL） |
| `slip_remarks` | `nvarchar(200)` | ○ | 伝票摘要（**伝票単位の値**。同一伝票の全行に複写。1章参照） |
| `line_remarks` | `nvarchar(100)` | ○ | 行摘要 |
| `internal_remarks` | `nvarchar(200)` | ○ | 社内摘要（**伝票単位の値**。同一伝票の全行に複写。画面表示のみで納品書には印字しない。1章参照） |

**`settlement_status`／`settled_amount` はキャッシュ列。** `SettlementService.RecalculateForBillingGroupAsync`（`src/bmcs_app.Application/Receipt/`）が入金データから得意先単位で再計算する。対象額は本テーブルの `amount`（税抜・税込いずれも `amount` がそのまま対象額になり、消費税分は行レベルの消込に載せない）。`tax_unit`=1/2 は `receipt_allocations`（`billing_number` 経由）、`tax_unit`=3 は `detail_receipts`（直接指定・明細請求書経由の合算）が充当元になる。詳細は `docs/design_document.md` 16章。

**得意先名・商品名・規格・単位・単価・原価・税率をスナップショットとして保持する。** 帳票の再発行時に当時の値で出力する必要があり、マスタ改定の影響を受けてはならないため。後から `ALTER TABLE` で追加しても過去データを埋め戻せないので、ここで確定させる。

#### `tax_unit` と税額カラムの対応（CHECK制約で強制）

税単位ごとの税額カラムの対応は、次のCHECK制約で強制する。

| `tax_unit` | `slip_tax_amount` | `tax_amount` | `billing_number` | 理由 |
|---|---|---|---|---|
| `1`（請求単位） | NULL | NULL | NULL可 | 請求締め時に一括計算するため、伝票時点では税額が存在しない |
| `2`（伝票単位） | NOT NULL | NULL | NULL可 | 伝票登録時に伝票単位で税額を確定する |
| `3`（内税明細単位） | NULL | NOT NULL | 常にNULL | 明細行ごとに内税額を確定する。締め請求データを持たない |

- `CK_sales_tax_amount_by_tax_unit` … `tax_unit` と `slip_tax_amount`/`tax_amount` の対応
- `CK_sales_billing_number_by_tax_unit` … `billing_number IS NULL OR tax_unit IN (1, 2)`

**`tax_amount` / `slip_tax_amount` は1円単位（小数部は常に `.00`）で確定する。** `amount` も得意先の端数区分（`ConsumptionTaxCalculator.CalculateLineAmount`）で1円単位に丸めてから保存する。数量 `decimal(13,3)` ×単価 `decimal(15,4)` の積をそのまま渡すと、SQL Server 側で得意先の端数区分を無視した四捨五入が起きるため。

---

### 2.10. 締め入金（`receipts`）

**主キーは (`receipt_slip_number`, `line_number`)。各明細行は支払手段の内訳（入金方法＋金額）を表す。**
請求への充当は`2.10-1節`の`receipt_allocations`が別途持ち、画面には表示しない内部データとする
（理由は`docs/design_document.md` 17章を参照）。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `receipt_slip_number` | `varchar(20)` | PK | 入金伝票番号 |
| `line_number` | `smallint` | PK | 行番号 |
| `receipt_date` | `date` | × | 入金日（**伝票単位の値**） |
| `customer_code` | `varchar(10)` | × | （**伝票単位の値**） |
| `tax_unit` | `tinyint` | × | `1`＝請求単位／`2`＝伝票単位（内税明細単位の入金は `detail_receipts` が担うため対象外） |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `deposit_method_code` | `varchar(10)` | × | 入金方法（FK → `deposit_methods`。2.6-1節。**行単位の値**） |
| `bank_account_code` | `varchar(10)` | ○ | 入金先口座（FK → `bank_accounts`）。`deposit_methods.requires_bank_account=1`の行のみ必須（**行単位の値**） |
| `bill_due_date` | `date` | ○ | 手形期日。`deposit_methods.requires_bill_due_date=1`の行のみ必須（**行単位の値**） |
| `amount` | `decimal(15,2)` | × | この行の入金額（**行単位の値。伝票合計は`SUM`して求める**） |
| `allocation_status` | `tinyint` | × | `1`＝未充当／`2`＝一部充当／`3`＝充当完了。**キャッシュ列**。同一伝票の`amount`合計と、`receipt_allocations`の`allocated_amount`合計の比較から`SettlementService`が導出する |
| `slip_remarks` | `nvarchar(200)` | ○ | 伝票摘要（**伝票単位の値**。同一伝票の全行に複写。1章参照） |
| `line_remarks` | `nvarchar(100)` | ○ | 行摘要 |

**CHECK制約 `CK_receipts_bank_account_bill_due_date_exclusive`** … `bank_account_code IS NULL OR bill_due_date IS NULL`（口座と手形期日が同時に埋まらないことのみDBで強制する）。「入金方法によって口座・期日のどちらが必須か」という要求方向の検証は、他テーブル（`deposit_methods`）参照が必要でDBのCHECK制約では表現できないため、アプリ層のみで担保する（2.6-1節）。

利用者にとって重要なのは「請求残高がいくら減ったか」であり、どの請求に充当されたかではないため、
画面（入金入力）は本テーブルの明細行のみを直接編集し、充当は保存時に
自動計算して`receipt_allocations`へ書き込む。

#### 2.10-1. 締め入金の充当（`receipt_allocations`）

`receipts`とは別に持つ内部データ。主キーは
(`receipt_slip_number`, `line_number`)。`receipts`とは独立した行番号体系を持つ（支払手段の内訳の
行数と、充当先の請求の件数は一致しない）。`receipts`への外部キーは張らない（`receipts`のPKが複合
[`receipt_slip_number`, `line_number`]で、`receipt_slip_number`単独の一意キーが無いため）。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `receipt_slip_number` | `varchar(20)` | PK | 入金伝票番号 |
| `line_number` | `smallint` | PK | 充当行番号 |
| `customer_code` | `varchar(10)` | × | 非正規化。`SettlementService`が得意先単位で充当行を引くために持つ |
| `tax_unit` | `tinyint` | × | `1`／`2`。`customers`・`billings`への複合FKに必要 |
| `billing_number` | `varchar(20)` | ○ | 充当先の請求データ。**`NULL`＝前受・過入金（充当先未定）** |
| `allocated_amount` | `decimal(15,2)` | × | この行の充当額。**入力データ**（再計算の対象外） |
| `fee_adjustment_amount` | `decimal(15,2)` | × | 振込手数料差額の調整額。**入力データ**。売上明細行への消込済金額には`allocated_amount + fee_adjustment_amount`として反映するが、充当ステータスの判定には含めない |

締め得意先は請求単位で古い順に自動消込するため、1回の入金が複数の請求にまたがる場合は複数行になる。`billing_number` へ充当された額は、`SettlementService` がその billing に紐づく売上明細行へ伝票日付→伝票番号→行番号の古い順に配分する（`docs/design_document.md` 16章）。

---

### 2.11. 明細入金（`detail_receipts`）

**統合対象外（構造が異なる）。** 明細単位（都度得意先）の入金はこのテーブルが担う。主キーは (`detail_receipt_number`, `line_number`)。

締め入金との違いは**充当先が2種類あること**（売上伝票を直接指定する場合と、明細請求書を指定する場合）。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `detail_receipt_number` | `varchar(20)` | PK | 明細入金番号 |
| `line_number` | `smallint` | PK | 行番号 |
| `receipt_date` | `date` | × | （**伝票単位の値**） |
| `customer_code` | `varchar(10)` | × | （**伝票単位の値**） |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `deposit_method_code` | `varchar(10)` | × | 入金方法（FK → `deposit_methods`。2.6-1節）。締め入金と同じマスタを使うが、手形期日を保持する列が無いため`requires_bill_due_date=1`の入金方法は画面側で選択肢から除外する |
| `bank_account_code` | `varchar(10)` | ○ | FK → `bank_accounts`。`deposit_methods.requires_bank_account=1`の行のみ必須 |
| `receipt_amount` | `decimal(15,2)` | × | 入金額（**伝票単位の値**） |
| `target_type` | `tinyint` | × | `1`＝売上明細行を直接指定／`2`＝明細請求書を指定 |
| `target_sales_slip_number` | `varchar(20)` | ○ | `target_type=1` のとき使用。**FK参照先は `sales`** |
| `target_sales_line_number` | `smallint` | ○ | 同上 |
| `target_detail_invoice_number` | `varchar(20)` | ○ | `target_type=2` のとき使用 |
| `allocated_amount` | `decimal(15,2)` | × | この行の充当額。**入力データ**（再計算の対象外） |
| `fee_adjustment_amount` | `decimal(15,2)` | × | **入力データ**。締め入金と同じ扱い（消込済金額には含めるが充当状態の判定には含めない） |
| `allocation_status` | `tinyint` | × | 締め入金と同じ区分。**キャッシュ列**（`SettlementService`が導出） |
| `slip_remarks` | `nvarchar(200)` | ○ | 伝票摘要（**伝票単位の値**。同一伝票の全行に複写。1章参照） |
| `line_remarks` | `nvarchar(100)` | ○ | 行摘要 |

**CHECK 制約** `CK_detail_receipts_target` … `target_type` と実際に埋まっているカラムを一致させる。

**`target_type=1`（直接指定）と `target_type=2`（明細請求書経由）は、同じ売上明細行に同時に効きうる。** `SettlementService`は直接指定分を先に確定し、残額（`amount - 直接充当額`）を明細請求書経由の配分に回す（名指しした指示を導出より優先する）。索引は `IX_detail_receipts_customer_code_receipt_date`／`IX_detail_receipts_target_sales`／`IX_detail_receipts_target_detail_invoice`。

```
(target_type = 1
   AND target_sales_slip_number IS NOT NULL
   AND target_sales_line_number IS NOT NULL
   AND target_detail_invoice_number IS NULL)
OR
(target_type = 2
   AND target_detail_invoice_number IS NOT NULL
   AND target_sales_slip_number IS NULL
   AND target_sales_line_number IS NULL)
```

**`target_sales_slip_number`/`target_sales_line_number` の対象は実際には `tax_unit=3` の `sales` 行のみだが、複合FKにはしない。** `detail_receipts` 側にも `tax_unit` を持たせる非正規化が増えるため、単純な `(sales_slip_number, line_number)` 参照に留め、絞り込みはアプリ側の抽出条件で行う。

---

### 2.12. 請求データ（`billings`）

**明細行を持たないヘッダー1テーブル。主キーは `billing_number`。**

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `billing_number` | `varchar(20)` | PK | 請求番号 |
| `customer_code` | `varchar(10)` | × | |
| `tax_unit` | `tinyint` | × | `1`＝請求単位／`2`＝伝票単位（内税明細単位は請求データを持たないため対象外） |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `billing_date` | `date` | × | 請求年月日 |
| `closing_year_month` | `char(6)` | × | 締め対象年月（`YYYYMM`）。月次締めとの突き合わせに使う |
| `previous_balance` | `decimal(15,2)` | × | 前月請求残高 |
| `receipt_amount` | `decimal(15,2)` | × | 期間内の入金金額 |
| `sales_amount` | `decimal(15,2)` | × | 期間内の売上金額 |
| `tax_amount` | `decimal(15,2)` | × | 消費税額 |
| `current_billing_amount` | `decimal(15,2)` | × | 今回請求金額 |
| `standard_rate_taxable_amount` / `standard_rate_tax_amount` | `decimal(15,2)` | × | **税率別内訳**: 標準税率（`tax_category = 1`）の対価額・消費税額 |
| `reduced_rate_taxable_amount` / `reduced_rate_tax_amount` | `decimal(15,2)` | × | 軽減税率（`tax_category = 2`）の対価額・消費税額 |
| `tax_exempt_amount` | `decimal(15,2)` | × | 非課税（`tax_category = 3`）の対価額 |
| `billing_status` | `tinyint` | × | `1`＝確定／`2`＝解除済 |
| `confirmed_at` / `confirmed_by` | `datetime2(3)` / `varchar(10)` | × | 確定日時・確定者 |
| `released_at` / `released_by` | `datetime2(3)` / `varchar(10)` | ○ | 解除日時・解除者 |

**`UNIQUE (billing_number, tax_unit)`（`UQ_billings_number_tax_unit`）を持つ。** `sales`/`receipt_allocations` から複合FKで参照させるための一意制約（`receipts`自体はbillingへのFKを持たない。2.10節参照）。`billing_number` 単独で既に一意なので論理的には冗長だが、SQL Serverが要求するため必要（2.8節）。

**税率別内訳を子テーブルではなく固定カラムで持つ。** 日本の税率は少数の閉じた集合であり、「請求データはヘッダー1テーブル」という方針を崩さずに済むため。請求書の**明細部分**は売上ジャーナルから都度組み立てるが、**税額は本テーブルの確定値を印字**して再発行時に金額が変わらないようにする。

**締め解除では物理削除せず `billing_status` を解除済にする。** 再締めでは新しい `billing_number` を採番する。一度発行した請求書を追跡できるようにするため。

**二重締め防止のフィルタ付き一意インデックス**: `UQ_billings_customer_closing_ym_confirmed`（`ON billings (customer_code, closing_year_month) WHERE billing_status = 1 AND is_deleted = 0`）。同一得意先・同一締め年月の確定済み請求データが2件存在できないことをDB側でも強制する（`docs/product-spec.md` 共通業務ルール2「二重請求はデータベース側でも拒否する」）。`billing_status = 1`（確定）のみを対象にするフィルタ付きインデックスのため、解除済み（`billing_status = 2`）は対象外になり、締め解除→再締めで新しい`billing_number`を採番する運用を壊さない（非フィルタの一意制約では、この運用が壊れる）。

---

### 2.13. 明細請求書（`detail_invoices`）

明細行を持たないヘッダー1テーブル。主キーは `detail_invoice_number`。明細部分は連携テーブル（2.14）経由で売上ジャーナルから組み立てる。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `detail_invoice_number` | `varchar(20)` | PK | 明細請求書番号 |
| `customer_code` | `varchar(10)` | × | 発行元となる正式な得意先 |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `addressee_name` | `nvarchar(60)` | × | **請求書に印字する宛名。都度入力のスナップショット**（学校のクラス・先生単位など） |
| `issue_date` | `date` | × | 発行日 |
| `sales_amount` / `tax_amount` / `total_amount` | `decimal(15,2)` | × | 税抜・消費税・税込 |
| 税率別内訳 | `decimal(15,2)` | × | `billings` と同じ3区分の固定カラム |
| `invoice_status` | `tinyint` | × | `1`＝発行済／`2`＝取消 |
| `issued_at` / `issued_by` | `datetime2(3)` / `varchar(10)` | × | 発行日時・発行者 |
| `cancelled_at` / `cancelled_by` | `datetime2(3)` / `varchar(10)` | ○ | 取消日時・取消者 |

**代表者印字の要否はここに持たない。** 宛名を書き換えても、発行元となる得意先マスタの `print_representative_flag` に従う（`docs/product-spec.md` 共通業務ルール3）。

---

### 2.14. 明細請求書と売上明細行の連携（`detail_invoice_sales_lines`）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `detail_invoice_number` | `varchar(20)` | PK | FK → `detail_invoices` |
| `sales_slip_number` | `varchar(20)` | PK | FK → `sales`（実際に対象となるのは `tax_unit=3` の行のみ） |
| `sales_line_number` | `smallint` | PK | 同上 |

**対象は `tax_unit=3`（内税明細単位＝都度得意先）の `sales` 行のみに限られる。** これは得意先マスタの相互制約（`税区分 = 内税明細単位 ⇔ closing_day = 0`）から導かれる。`sales` テーブル自体は税単位を問わず参照できるため、この絞り込みはFKでは強制されずアプリ側の抽出条件に依存する（2.11節と同じ理由）。

#### `UNIQUE (sales_slip_number, sales_line_number)` — 二重請求をDBで防ぐ

「多対多」は**伝票レベル**の話（1つの売上伝票の各明細行が別々の明細請求書に分散しうる）であり、**売上明細行レベルでは1行が紐づく明細請求書は最大1つ**である。

この一意制約により、**同じ売上明細行を2枚の明細請求書に載せる二重請求を DB が拒否する**。アプリ側の抽出条件だけに依存しない。

明細請求書を取消したときは、このテーブルの該当行を削除して売上明細行の請求状態を未請求に戻す。行の追加・削除しか発生しないため、**このテーブルは `row_version` を持たない**（監査列は持つ）。

---

### 2.15. 受注（`orders`）

**売上・入金と同じ非正規化で、明細行1テーブル構成とする。** 主キーは (`order_slip_number`, `line_number`)。売上テーブル（2.9）と共通するカラムはそのまま踏襲し、受注固有のカラムのみ以下に示す。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `order_slip_number` | `varchar(20)` | PK | 受注伝票番号 |
| `line_number` | `smallint` | PK | 行番号 |
| `order_date` | `date` | × | 受注日（**伝票単位の値**） |
| `customer_code` | `varchar(10)` | × | （**伝票単位の値**） |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `sub_customer_id` | `varchar(20)` | ○ | 子得意先（学校のクラス・先生等）の指定 |
| `product_code` | `varchar(20)` | × | |
| `product_name` | `nvarchar(60)` | × | スナップショット |
| `specification` | `nvarchar(60)` | ○ | スナップショット |
| `unit_name` | `nvarchar(10)` | ○ | スナップショット |
| `order_quantity` | `decimal(13,3)` | × | 受注数量 |
| `unit_price` | `decimal(15,4)` | × | 単価。得意先の `tax_unit` に応じて商品マスタの外税単価／内税単価のいずれかを転記した**スナップショット**（売上テーブルと同じ選択ルール） |
| `amount` | `decimal(15,2)` | × | 金額 |
| `cost_price` | `decimal(15,4)` | × | 原価のスナップショット（粗利計算用。売上テーブルと同じ理由） |
| `tax_category` | `tinyint` | × | 税種別区分のスナップショット |
| `tax_rate` | `decimal(5,2)` | × | 税率のスナップショット |
| `allocated_quantity` | `decimal(13,3)` | × | 引当数量。**在庫連携はスコープ外のため自動更新ロジックは持たない**（`docs/product-spec.md` 共通業務ルール6） |
| `order_status` | `tinyint` | × | `1`＝未売上／`2`＝一部売上／`3`＝売上完了／`4`＝中止 |
| `sales_confirmed_quantity` | `decimal(13,3)` | × | 売上化済数量。`order_quantity` との比較で `order_status` を判定する |
| `slip_remarks` | `nvarchar(200)` | ○ | 伝票摘要（**伝票単位の値**。同一伝票の全行に複写。1章参照） |
| `line_remarks` | `nvarchar(100)` | ○ | 行摘要 |
| `internal_remarks` | `nvarchar(200)` | ○ | 社内摘要（**伝票単位の値**。同一伝票の全行に複写。画面表示のみで納品書には印字しない。1章参照） |

**税のスナップショットを持つ理由は売上テーブルと同じ**（1章）。受注段階では消費税額そのものは確定しないが、見積・受注控えの表示や売上化時の初期値として使うため、商品マスタからの転記時点の税率を保持する。税額（`tax_amount` 等）は持たない。売上化時は `sales` 側で税額を計算する。

**`order_status` はキャッシュ列。** 売上化・中止・売上取消のたびに、`sales_confirmed_quantity` の更新と同一トランザクション内で更新する（`docs/architecture.md` 6章）。逆遷移（売上取消時に `売上完了`／`一部売上` → `一部売上`／`未売上` に戻す）も同じ処理で扱う。**中止（`4`）は伝票単位の操作かつ終端状態**（`docs/product-spec.md`「受注」参照）。中止時も `sales_confirmed_quantity` は変更しない（分納済みの実績を残す）。実装は `src/bmcs_app.Application/Order/OrderStatusService.cs`（`docs/design_document.md` 7章）。

**納品書発行状態・請求状態・消込状態は持たない。** これらは売上化された後（`sales`）で管理する状態であり、受注はまだ売上・売掛金を発生させていないため対象外。

---

### 2.16. 月次締め（`monthly_closings`）

**得意先×月末日で1レコード（`billings`類似レイアウト）。** 全得意先（`tax_unit`問わず）が対象。

**親子請求（請求集約、1-1節）との関係**: 月次締めは**請求集約せず、`customer_code`（請求集約元）ごとに個別集計する**。得意先元帳・月次締めは会計上の売掛金集計であると同時に支店別営業実績（担当者別売上・粗利、`sales_employee_code`）の集計単位でもあり、請求集約先に合算すると支店別実績が失われるため。集約するのは`billings`（請求）・`receipts`（入金）・売掛残高のみで、`monthly_closings`では請求集約先の行がグループ合算の残高を持ち、請求集約元の行は自社の売上額と税率別の対価額だけを持つ（前月残高・入金額・消費税額・当月残高・税率別の税額は0。合計すると売上が請求集約先の行と二重に数えられるため、集計に使うときは請求集約元の行を除く。`docs/design_document.md` 29章）。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `closing_date` | `date` | PK | 対象月の**月末日**（例: `2026-02-28`）。集計期間は「月初〜この日付」の暦月 |
| `customer_code` | `varchar(10)` | PK | FK（`customer_code`, `tax_unit`の複合）→ `customers` |
| `tax_unit` | `tinyint` | × | 集計時点の得意先税区分のスナップショット |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `previous_balance` | `decimal(15,2)` | × | 前月末売掛残高（＝前月の本テーブルの`closing_balance`。前月の確定行が無ければ元帳の月初残高。請求集約元の行は0） |
| `sales_amount` | `decimal(15,2)` | × | 当月（暦月）売上金額 |
| `receipt_amount` | `decimal(15,2)` | × | 当月（暦月）入金金額 |
| `tax_amount` | `decimal(15,2)` | × | 消費税額。`tax_unit=1`の未確定区間（次回請求締めをまだ通っていない伝票）は仮計算した値（1章参照）。実装は`closing_balance − previous_balance − sales_amount + receipt_amount`で逆算し、前月の仮計算税が確定値に置き換わったずれを吸収する（`design_document.md` 29章）。請求集約元の行は0 |
| `closing_balance` | `decimal(15,2)` | × | 当月末売掛残高（`previous_balance + sales_amount + tax_amount - receipt_amount`） |
| `standard_rate_taxable_amount` / `standard_rate_tax_amount` | `decimal(15,2)` | × | 税率別内訳: 標準税率（`tax_category=1`）の対価額・消費税額 |
| `reduced_rate_taxable_amount` / `reduced_rate_tax_amount` | `decimal(15,2)` | × | 軽減税率（`tax_category=2`）の対価額・消費税額 |
| `tax_exempt_amount` | `decimal(15,2)` | × | 非課税（`tax_category=3`）の対価額 |
| `closing_status` | `tinyint` | × | `1`＝確定／`2`＝解除済。**「未締め」はレコードが存在しない状態で表す**（状態値を持たない） |
| `confirmed_at` / `confirmed_by` | `datetime2(3)` / `varchar(10)` | × | 確定日時・確定者 |
| `released_at` / `released_by` | `datetime2(3)` / `varchar(10)` | ○ | 解除日時・解除者。解除は管理者権限のみ |

**CHECK 制約**:

| 制約名 | 条件 |
|---|---|
| `CK_monthly_closings_tax_unit` | `tax_unit IN (1, 2, 3)` |
| `CK_monthly_closings_status` | `closing_status IN (1, 2)` |

**複合FK** `(customer_code, tax_unit)` → `customers`（`UQ_customers_code_tax_unit`）。`billings`/`sales`/`receipts`と同じパターンで、得意先マスタの税区分との整合をDBで強制する。

**締め解除では`billings`と同様に物理削除せず`closing_status`を解除済にする。** 一度確定した月次残高を追跡できるようにするため（本ファイル1章）。

#### 編集ロックは導出方式（伝票側にフラグを持たない）

**編集可否は既存のキャッシュ列と本テーブル等から導出し、伝票側にロックフラグを持たない。** 締め・解除のたびに大量の伝票行を更新することになるため（`docs/architecture.md` 9章、および本ファイル1章の方針）。判定条件は1章「ジャーナル系テーブルの編集ロック・訂正方式」を正とする。本テーブルは条件③（月次締め）の判定元で、`closing_status = 1`（確定）の行が対象になる。

#### 集計結果を保存する

**本テーブルは集計結果を保存する（都度集計しない）。** 理由は性能ではなく、`tax_unit=1`の得意先の暦月末時点の税額が、都度計算では確定できないため（1章参照。`billings`の締め期間と`monthly_closings`の暦月が食い違い、`sales`側に税額を書き込めない）。**確定した`monthly_closings`行の税額は、都度再計算しても異なる値になり得る**（`billings`確定前の仮計算のため）ので、確定時点の値をこのテーブルに保存し、以後はこの保存値を参照する。

担当者別売上・粗利の集計は、本テーブルの対象外。**保存せず都度集計する**（性能問題が出た場合に別途集計結果テーブルを追加する）。

---

### 2.17. 採番（`slip_number_sequences`）

採番規則は暫定として、年度リセットなしの通し連番・採番テーブル方式とする。**伝票種別ごとに1行を永続保持する。**

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `sequence_key` | `varchar(30)` | PK | 伝票種別。`orders` / `sales_slip` / `receipt_slip` / `detail_receipts` / `billings` / `detail_invoices` |
| `current_value` | `bigint` | × | 現在の採番値。次番は `current_value + 1` |

**採番は伝票登録と同一トランザクション内で行う**（`docs/architecture.md` 6章）。別トランザクションで先に採番すると登録失敗時に欠番が出るため。`UPDATE` の行ロックで直列化するので、`row_version`（楽観的排他）は持たない。

**売上の採番は税単位ごとに分けず `sales_slip` の1系列とする**（`sales` は税単位を問わず1テーブル）。伝票番号が得意先の税区分によって別系列になると、現場で伝票番号から伝票を探すときに混乱するため。

#### 伝票番号の表記形式（暫定）

採番規則そのものは暫定運用中で、伝票番号の表記は以下とする。

- **接頭辞なし・8桁ゼロ埋め10進**（例: `00000001`）。格納先は `varchar(20)` で余裕がある。
- ゼロ埋めにする理由は、伝票番号が `varchar` 列であり、**文字列ソートが数値ソートと一致する**
  必要があるため（`ProductHistoryQueryService` 等が伝票番号で文字列ソートしている）。
- 8桁を超えても `varchar(20)` の範囲内で自然に9桁以上へ伸びる（上限チェックは置かない）。
  ただし**文字列ソート順は壊れる**（`"99999999" > "100000000"`）。1日あたりの伝票発生数から
  桁あふれは現実的でないため許容する。`ORDER BY` を伝票番号に依存する画面が具体化した時点で
  再検討する。
- 実装は `src/bmcs_app.Domain/Numbering/SlipNumberFormatter.cs`
  （`SlipNumberKind → sequence_key` の対応、`long → 8桁ゼロ埋め文字列`）、
  採番本体は `src/bmcs_app.Infrastructure/Numbering/SlipNumberSequenceCommand.cs`
  （`UPDATE ... OUTPUT` による同時実行制御）。採番規則・表記を変える場合はこの2ファイルを直接修正する（抽象化していないため）。

#### このテーブルは EF Core で追跡しない

採番は生SQLの `UPDATE` で行い ChangeTracker を経由しない。追跡した状態で読むと、
別セッションの採番結果を古い値で上書きしうる（`row_version` を持たないため EF Core は検出できない）。
参照するときは必ず `AsNoTracking()` を使う。

#### 重複禁止の最終防衛線

万一採番が重複しても、`sales`/`orders` 等の複合PK（`(伝票番号, 行番号)`）が
2件目の INSERT を拒否する（明細行番号は両方とも1から始まるため必ず衝突する）。
伝票番号の重複禁止をアプリの採番運用だけに頼らないための
安全網であり、将来 PK を単一列に変えないよう留意する。

---

### 2.18. 状態遷移とカラムの対応（網羅性の確認）

`docs/product-spec.md`「伝票の状態遷移」で定義した全状態が、いずれかのカラムで保持できることを示す。

| 対象 | 状態 | 保持するカラム | テーブル |
|---|---|---|---|
| 受注 | 未売上／一部売上／売上完了／中止 | `order_status`（判定は `order_quantity` と `sales_confirmed_quantity` の比較） | `orders` |
| 売上・軸1 | 未発行／発行済 | `delivery_note_issued_at`（`NULL`＝未発行）＋ `delivery_note_issue_count` | `sales` |
| 売上・軸2 | 未請求／請求済 | `billing_status`。紐付け先は締め請求が `billing_number`、明細請求が `detail_invoice_sales_lines` | `sales` |
| 売上・軸3 | 未消込／一部消込／消込完了 | `settlement_status` ＋ `settled_amount` | `sales` |
| 入金 | 未充当／一部充当／充当完了 | `allocation_status` ＋ `allocated_amount` | `receipts`、`detail_receipts` |
| 請求データ | 確定／解除済 | `billing_status` ＋ 確定・解除の日時と実施者 | `billings` |
| 明細請求書 | 発行済／取消 | `invoice_status` ＋ 発行・取消の日時と実施者 | `detail_invoices` |
| 月次締め | 未締め／確定／解除済 | `closing_status`（**未締めはレコード不在で表す**。得意先×月末日で1レコード） | `monthly_closings` |
| 伝票の取消 | — | 共通カラムの `is_deleted`（物理削除しない） | 全伝票テーブル |
| 月次締め・請求締めによる編集ロック | — | **カラムを持たず導出**（`customer_code`＋伝票日付の年月 × `monthly_closings`、または`sales.billing_number`が確定済み`billings`を指すか） | — |

**すべての状態カラムはキャッシュ列**であり、関連伝票の登録・取消・訂正と同一トランザクション内で更新する（`docs/architecture.md` 6章）。「絞り込み条件として頻出する状態はカラムで保持し、それ以外は導出する」という基準に従う（消込ステータスをキャッシュ列とする方針と同じ）。

---

## 3. 命名規則・運用

### 3.1. 命名規則

- **テーブル名・カラム名は `snake_case` とする。** C# 側のエンティティクラス名・プロパティ名は PascalCase とし、変換は EF Core の命名変換に任せる（`docs/architecture.md` 10章）。
- **エンティティクラス名は、ドメイン名だけの単純な命名とする: `Sales` / `Receipt`（締め入金） / `Billing`（請求データ。税単位を表す `Invoice` と語が衝突しないよう、ドメイン名は `Invoice` ではなく `Billing`）。** 税単位は `tax_unit` カラムの値で表す（2.8節）。
- **明細単位の請求・入金は、構造が異なる業務概念として `Detail` を冠して別立てで命名する: `DetailInvoice`（明細請求書）/ `DetailReceipt`（明細入金）。** 統合の対象外（2.11節・2.12節）。明細請求書と売上明細行の連携テーブルは `DetailInvoiceSalesLine`。
- **ユーザー定義ストアドプロシージャには、プレフィックス `usp_` を付ける。**

### 3.2. DDL・スキーマ変更の運用

- **設計上の正はエンティティクラス定義とし、EF Core のマイグレーション機能は使用しない。** DDL は手書きして `scripts/` に連番SQLとして残し、SQLCMD で適用する（「Code-First」という語は、マイグレーションでDDLを生成する運用と誤解されるため使わない）。
- **適用済みDDLの管理**: `scripts/` に `001_xxx.sql` 形式の連番で置き、適用したものは削除・改変しない。スキーマを変更する際は新しい連番ファイルを追加する。
  - `003_create_closing_and_sequence_tables.sql` の `slip_number_sequences` 初期行 INSERT は `IF OBJECT_ID(...) IS NULL` の内側（テーブル作成時のみ実行）にある。**将来 `sequence_key` を追加するときは新しい連番SQLで INSERT すること。**
- **コードとDBの乖離防止**: 仕様変更等でプログラムを修正する際は、対応するライブDB（開発用DB）のテーブル・ストアドプロシージャの変更もコード修正と同一の作業内でSQLCMDを用いて追従させる。乖離が疑われる場合は `sys.columns` / `sys.tables` を SQLCMD で照会し、エンティティ定義と突き合わせて確認する。起動時のスキーマ検証は行わない（起動が遅くなるため）。
- **接続先の環境情報**: `docs/architecture.md` の「開発用データベース環境」を参照。

---

## 4. 暫定運用中の項目

**暫定設定で進めている項目の一覧は `TODO.md` の「保留項目の扱い」を正とする。** ここには DB 設計に直接影響するものだけを挙げる。方針が変わった場合、抽象化していないため該当箇所を直接修正する必要がある。

| 論点 | 現在の暫定 | 見直したときに影響する範囲 |
|---|---|---|
| 採番規則 | 年度リセットなしの通し連番・採番テーブル方式。表記は接頭辞なし・8桁ゼロ埋め（2.17節） | 採番テーブルの構造、伝票番号のカラム長。表記変更時は `SlipNumberFormatter.cs`／`SlipNumberSequenceCommand.cs` を直接修正 |
| 単価決定ロジック | 商品マスタの外税単価／内税単価（得意先の`tax_unit`で選択）を転記する。単価計算マスタは作らず、将来の掛け率マスタ実装に備え`IUnitPriceCalculator`インターフェースとして実装している（2.2節） | 掛け率マスタ実装時は`StandardUnitPriceCalculator`の差し替えのみで対応（DI登録済み） |
| 原価の取得元 | 商品マスタの `standard_cost_price` を転記する（仕入機能がスコープ外のため。2.2節） | 仕入機能を実装する際に最終仕入原価・移動平均等へ再検討 |
| リアルタイム残高 | 都度集計（残高キャッシュ列を持たない） | 性能不足なら残高キャッシュ列を追加 |
| 得意先の支払条件・与信限度額 | 項目を作っていない（設計資料に記載がないため） | `customers` への `ALTER TABLE` で追加 |

支払条件・与信限度額は `docs/design_document.md` の確認事項にも記載している。
