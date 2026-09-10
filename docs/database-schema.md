# bmcs_app データベース設計

> データベースに関する情報（テーブル設計方針・スキーマ上の未確定事項・確定したテーブル/カラム定義など）はこのファイルに記載する。`CLAUDE.md` や `docs/design_document.md` にDB関連の詳細を書かない。

---

## 1. 設計方針

DBスキーマは、各画面仕様書が実際に前提としている業務要件をもとに新規に設計する。**設計上の正はエンティティクラス定義とし、EF Core のマイグレーション機能は使用しない**（DDLの管理方法は3章「命名規則・運用」を参照）。

現時点で分かっている方針は以下：

- 月次締め処理の粗利計算に必要な原価カラム（`cost_price`）は、受注明細・売上明細の両テーブルに設ける。
- 受注明細行には `sub_customer_id`（学校のクラス・先生等の子得意先／請求・納品先指定）カラムを設けるが、**参照先の子得意先マスタは作らず、現時点では未使用（値を持つだけで参照・更新する処理はない）。** 学校・官公庁向けの宛名柔軟性は、子得意先マスタではなく**都度書き換え方式**で実現することが確定した（C-9・2026-09-10確定）。ジャーナル系の画面（受注・売上・入金・明細請求書等）は、得意先コードで検索した後、`customer_name`（画面上の名称欄）を手入力で上書き修正できるようにする。学校－学年－クラスのような階層を持つ得意先も、マスタ上は常に一つの得意先として扱い、学年・クラスの違いは伝票入力時の名称上書きだけで表現する。`sub_customer_id` は将来の別要件に備えて列は残すが、削除しない以外の対応方針はない（実装予定なし）。
- **売上・入金・請求残高は、消費税計算単位（請求単位／伝票単位／明細単位）で物理テーブルを分割しない。** `sales` / `receipt` / `billing` の各1テーブルに統合し、`tax_unit` カラム（1=請求単位／2=伝票単位／3=内税明細単位）で税単位を表す（2026-09-08決定。経緯は本章末尾「税単位別テーブル分割の統合」を参照）。得意先マスタの税区分設定に応じて対象の得意先データがどのテーブル群に属するかが決まる、という考え方自体は変わらないが、それを物理テーブルの選択ではなく `tax_unit` カラムの値で表す。1得意先は常にいずれか1つの単位に属する想定（この前提は変わらない）。**明細単位の入金・請求（`detail_receipt` / `detail_invoice`）は構造が本当に異なるため統合対象外**（繰越残高の概念がない、`target_type` 分岐がある等）。
- **`sales` / `receipt` / `billing` は、得意先マスタとの複合FKで税単位の整合をDBが強制する。** `customer` に `UNIQUE (customer_code, tax_unit)` を持たせ、各テーブルから `(customer_code, tax_unit)` の複合FKで参照する。「伝票の税単位は得意先マスタの税区分と必ず一致する」がDB制約になるため、誤った税単位でINSERTすることはできない。同様に `billing` にも `UNIQUE (billing_number, tax_unit)` を持たせ、`sales` / `receipt` から `(billing_number, tax_unit)` の複合FKで参照することで、税単位をまたいで請求データを参照できないことも強制する（`billing_number IS NULL` の未請求・前受金行はSQL Serverの MATCH SIMPLE によりFK検査対象外になり、そのまま表現できる）。
- **明細入金は、税区分が「明細単位」の得意先専用の入金テーブルとして実装する。** 明細入金を使う得意先は税単位が明細単位の得意先のみの予定であるため、専用テーブルを新設するか `receipt` に一本化するかという判断は不要（明細単位バケット＝明細入金テーブルそのもの）。**`receipt`（締め入金）と `detail_receipt`（明細入金）が別テーブルという非対称は意図的なもの。** 明細入金は「売上伝票または明細請求書を指定したピンポイント消込」であり、締め入金（請求単位で古い順に自動消込）とは保持すべきカラムが異なるため統合しない（税単位が同じだけで構造まで同じとは限らない、というのが `sales`/`receipt`/`billing` 統合との違い）。
- **明細請求書と売上の紐付けは、売上明細（行）単位の連携テーブルで管理する。** 1つの売上の各明細行が、それぞれ別の明細請求書に分散して紐づくことがあるため、売上ヘッダー単位の直接FK（1対多）では表現できない。売上ヘッダー単位ではなく、売上明細行単位での多対多の紐付けが必要。
- **消込ステータスはキャッシュ列方式で管理する。** 売上明細行に消込ステータスのカラムを持ち、入金の登録・取消・訂正時に関連する売上明細のステータスを同一トランザクション内で更新する。都度SUM計算方式（入金明細を都度集計）は、元帳表示・明細請求書候補抽出・月次締めの整合性チェックなど絞り込み表示が頻出するため採用しない。
- **得意先元帳は、アプリ側（LINQ）で複数テーブルを取得してマージする方式とする。** SQLビュー（UNION等）によるDB側結合は、マイグレーションを使わない方針と相性が悪いため採用しない。統合後は `sales`/`receipt` を `customer_code` で絞るだけで済み（内税明細単位の得意先は `sales` と `detail_receipt` を絞る）、税単位に応じて参照テーブルを振り分ける分岐は不要になった。
- **締め対象／都度対象の判定は、得意先マスタの締日カラム（`closing_day`、`0`なら都度・明細）のみで行う。** 専用の区分フラグは別途持たない。現状の業務要件（一般企業=締め、官公庁・学校・都度取引先=都度・明細）ではこれで十分なため。
- **締日カラム（`closing_day`）と税区分カラムは独立ではなく、`税区分 = 内税明細単位 ⇔ closing_day = 0` の相互制約を持つ。** 締め日のある得意先で内税明細単位を使うことはなく、逆に都度得意先で請求単位／伝票単位を使うこともない（業務確認済み）。**この組み合わせに限定される理由はインボイス制度上のもの**（請求単位・伝票単位＝締め得意先は常に外税、内税明細単位＝都度得意先は常に内税。M-8・2026-09-10確定）。残り3パターン（内税×請求単位／内税×伝票単位／外税×明細単位）は業務上発生しないため扱わない。この2カラムを独立に入力できる状態にすると、次の破綻が起きるため制約を明示する。
  - 締め得意先（`closing_day≠0`）＋内税明細単位 … 明細単位バケットには前月繰越残高を持つ請求データ（`billing`）が存在しないため、請求締め処理が実行できない。
  - 都度得意先（`closing_day=0`）＋請求単位 … 請求締め時に消費税を一括計算する前提のため、締めを行わない都度得意先では消費税の確定タイミングが決まらない。
  - 担保方法: 得意先マスタに **CHECK制約**（上記の同値条件）を設け、加えてアプリ側の得意先マスタ登録・更新時バリデーションでも弾く。
- **得意先の締め区分（`closing_day` の 0／非0）と税区分は、登録後に変更できないものとして扱う。** 変更すると既存の売上・入金・請求データの `tax_unit` と食い違い、既存データの整合が取れなくなるため。マスタ管理画面では新規登録時のみ入力可とし、更新時は編集不可（読み取り専用）とする。したがって移行処理は実装しない。**統合後はこれがDB制約でも裏付けられる。** `customer` の `UNIQUE (customer_code, tax_unit)` を `sales`/`receipt`/`billing` から複合FKで参照しているため、伝票が1件でも存在する得意先の `tax_unit` を `UPDATE` すると `ON UPDATE CASCADE` を持たないFK違反（Msg 547）で拒否される。アプリ側では既に編集不可にしているため通常経路では到達しないが、直接SQLを書いた場合の最後の防波堤になる。
- **メニュー構成マスタは親子関係（階層構造）とする。** 権限設定（最小必須権限）は子（末端の機能メニュー）側にのみ持たせ、親（分類の見出し）には権限を持たせない。社員マスタ側は、この子メニューの権限値と比較できる単一の権限レベルカラムを持つ（カラム名等の細部は実装時に決定）。
- **売上・入金・受注のテーブルは、ヘッダーと明細を正規化（別テーブルに分割してFK参照）しない。** 明細行を単位とした1テーブル構成とし、ヘッダー相当の情報（得意先・日付等）は各明細行に持たせる（非正規化）。これは消費税計算単位（請求単位／伝票単位／明細単位）の3系統いずれにも適用する。
  - 売上・入金は、会計上のジャーナル（変更されない記録）としての意味も持つため、明細行単独で完結した記録である必要がある、というのが非正規化の主な理由。**「変更されない」とは常に修正不可という意味ではなく、確定されたら修正できないという意味（2026-09-10確定、C-6）。** 修正不可になる条件・元伝票の直接修正方式については本章末尾「ジャーナル系テーブルの編集ロック・訂正方式（C-6）」および2.16節を参照。
  - **受注も同じ1テーブル構成に統一する。** 受注は売上・入金と異なり状態が変化する仮伝票（未売上／一部売上／売上完了／中止、`docs/product-spec.md` の状態遷移を参照）でジャーナル性は無いが、テーブル構成の一貫性・実装の単純さを優先し、あえて分離しない。行の状態はキャッシュ列（`受注進捗状態`・`売上化済数量`）として明細行自体を更新する。
- **ジャーナル系のテーブル（`sales`／`receipt`／`detail_receipt`／`order_slip`）は、伝票摘要（`slip_remarks`）と行摘要（`line_remarks`）の両方のカラムを持つ（2026-09-09決定）。** 伝票摘要は自由記述のメモで、**同一伝票の全明細行に複写する**（`slip_date`／`customer_code` と同じ「伝票単位の値」。集計時に `SUM` 等で多重計上しないよう、伝票単位で1行に絞ってから扱う点も同様）。行摘要は明細行ごとに独立した値を持つ。ヘッダーのみの集計テーブル（`billing`／`detail_invoice`）はジャーナルではないため対象外（2.8節）。
- **得意先マスタは、一般的な得意先情報として担当者名・住所を保持する。** 共通検索モーダル（伝票入力画面から共通で呼び出される得意先検索）が検索対象とする項目であり、得意先マスタの標準項目として設ける。
- **請求データ（請求残高）は明細行を持たず、集計値のみのヘッダー1テーブルとする。** 請求書発行時の明細部分は、請求データ側に保持せず、売上のジャーナルデータ（売上テーブルの明細行）を参照して都度組み立てる。
- **請求データ（請求残高）は、請求単位・伝票単位の間で構造が共通。** 両者は消費税計算のタイミング（請求時に一括計算 か 伝票登録時に計算済み）が違うだけで、集計後の請求データとしては同じ構造になる。ただし物理的には別テーブルとする（売上・入金のテーブル分割方針と揃える）。明細単位（都度得意先向けの明細請求書）は前月からの繰越残高という概念がなく構造が異なるため、この請求データとは別の既存概念（明細請求書）を使う。
- **インボイス対応のため、商品マスタに税種別区分（標準税率／軽減税率／非課税）を持ち、売上明細行・受注明細行には税率と税種別区分を転記して保持する。** 伝票時点の税率をスナップショットとして行内に持つことで、マスタ側の税率改定が既存伝票に影響しないようにする（非正規化方針と同じ趣旨）。税率別内訳の集計はこのカラムでグループ化して行う。税種別区分は識別子・カラム名に具体的な税率（%）をハードコードしない（税率改定で名称が実態と食い違うため）。実際の税率は税率マスタ（2.3節）で管理する。
- **商品マスタは、外税単価と内税単価を別カラムで持つ。** 得意先の税区分（`tax_unit`）は「請求単位」「伝票単位」「内税明細単位」のいずれかに固定される（1章15行目）ため、**同一得意先への販売は常に外税か内税のどちらか一方のみ**（明細単位の得意先＝内税、それ以外の得意先＝外税で、内税と外税を混在させて売ることはない）。商品選択時は、対象得意先の `tax_unit` に応じて商品マスタの外税単価・内税単価のいずれかを売上明細行・受注明細行の `unit_price` に転記する（内税/外税の選択自体は商品側の属性ではなく、得意先の `tax_unit` から一意に決まる）。非課税品は税の内外の区別がないため、外税単価・内税単価に同じ値を設定する運用とする。
- **請求データ（請求残高）に、確定時点の税率別内訳を保持する。** 保持方法は子テーブルではなく**税種別区分ごとの固定カラム**（例: 標準税率対価額／標準税率消費税額／軽減税率対価額／軽減税率消費税額／非課税対価額。カラム名に具体的な税率（%）はハードコードしない）とする。日本の税種別区分は少数の閉じた集合であり、「請求データは集計値のみのヘッダー1テーブル」という方針を崩さずに済むため。請求書の**明細部分**は従来方針どおり売上ジャーナルから都度組み立てるが、**税額は請求データ側の確定値を印字**し、再発行時に金額が変わらないようにする。
- **自社情報マスタを設ける。** 適格請求書発行事業者の登録番号・自社名称・住所・代表者名を保持し、請求書・明細請求書・納品書の発行元情報として参照する。登録番号は法定記載事項であり、コードへのハードコードは行わない。原則1レコード運用とする。**振込口座は自社情報マスタではなく銀行口座マスタ（`bank_account`）で管理し、請求書に印字する口座はフラグで指定する**（口座は複数持ちうるため、自社情報マスタに1組だけ持たせると銀行マスタと重複する）。
- **伝票の状態は、軸ごとに独立したカラムとして保持する（単一のステータスカラムに集約しない）。** 各状態の業務上の意味・遷移は `docs/product-spec.md` の「伝票の状態遷移」を正とし、ここでは保持するカラムのみを定義する。
  - 受注明細行: 受注進捗状態（未売上／一部売上／売上完了／中止）、売上化済数量
  - 売上明細行: 納品書発行日時（`NULL`＝未発行）、納品書発行回数、請求状態（未請求／請求済）、消込状態（未消込／一部消込／消込完了）、消込済金額
  - 入金明細行: 充当状態（未充当／一部充当／充当完了）、充当済金額
  - 請求データ: 状態（確定／解除済）、確定日時・確定者、解除日時・解除者
  - 明細請求書: 状態（発行済／取消）、発行日時・発行者、取消日時・取消者
- **状態カラムはすべてキャッシュ列であり、関連伝票の登録・取消・訂正と同一トランザクション内で更新する。** 「絞り込み条件として頻出する状態はカラムで保持し、それ以外は導出する」という基準に従う（消込ステータスをキャッシュ列とする方針と同じ）。
- **月次締め（`monthly_closing`）は、得意先ごとの暦月末時点の売掛残高を保持するテーブルとする（2026-09-09決定。得意先×月末日で1レコード、`billing`類似レイアウト）。** 締め得意先への請求（`billing`）は得意先ごとの締め日（`closing_day`）期間で集計するが、会計上の月次売掛金は全得意先を暦月（月初〜月末）で集計する必要があり、両者の集計期間が一致しないため。**「請求締め」と「月次締め」は別々の締め処理として併存する。** 都度得意先（`tax_unit=3`）も含め全得意先が対象。
  - 20日締めの得意先の例: `billing`は1/21〜2/20を集計するが、`monthly_closing`は2/1〜2/28を集計する。2/21〜2/28分の売上は、その得意先自身の請求締め（次回3/20締め）をまだ通っておらず、`tax_unit=1`（請求単位）の得意先は伝票時点で税額を確定しない設計（2.9節）のため、この区間の税額は`monthly_closing`確定処理が`ConsumptionTaxCalculator`を「確定させずに」呼び出して仮計算し、`monthly_closing`側のカラムにのみ保存する（`sales.slip_tax_amount`には書き込まない。CHECK制約 `CK_sales_tax_amount_by_tax_unit` に違反するため）。
  - 状態（確定／解除済）、確定日時・確定者、解除日時・解除者を保持する（`billing`と同じ非破壊方式。締め解除で物理削除しない）。
- **ジャーナル系テーブル（`sales`／`receipt`／`detail_receipt`／`order_slip`）の編集ロック・訂正方式（C-6・2026-09-10確定）。** 訂正・取消は**元伝票の直接修正**とし、赤伝（マイナス伝票）方式は採用しない。伝票側にフラグを持たず、次のいずれかに該当する伝票行のみ編集不可（それ以外は直接修正可能）:
  1. **請求締め**: 対象行が確定済みの `billing` に集計済み（`sales.billing_number`／`receipt.billing_number` が確定済み `billing` を指す）
  2. **月次締め**: 対象行の `customer_code` と伝票日付の年月に一致する `monthly_closing` レコードが存在し、`closing_status`＝確定
  3. **入金済み**（2026-09-10追加）: `sales` は `settlement_status`＝消込完了、`receipt`／`detail_receipt` は `allocation_status`＝充当完了
  - 締め・解除のたびに大量の伝票行を更新するのを避けるため、既存のキャッシュ列（`billing_number`／`closing_status`／`settlement_status`／`allocation_status`等）のみで導出する。**この「編集不可」はユーザーによる伝票内容の直接編集を指す。** 締め処理自身が状態カラム（`billing_number`等）を更新することはロック対象外（システム内部の状態遷移であり、ユーザー編集ではないため）。
  - **`order_slip`（受注）はこの3条件のいずれにも該当しない**（受注は請求・消込の対象外）。したがって受注は状態にかかわらず常に直接修正可能。
  - **都度得意先（`tax_unit=3`）の明細請求書発行自体はロック条件に含めない**（採用した解釈。ユーザー回答の3条件に明細請求書発行が明示されていないため）。都度得意先の売上は、消込完了または月次締め確定によってのみロックされる。
- **ステータス値はDB側 `tinyint`、C#側は enum で扱う。** 文字列コードは使わず、画面表示名はアプリ側で解決する。
- それ以外の設計判断は未確定。詳細は4章「未確定のDB設計判断」を参照。

---

## 2. テーブル定義

### 2.0. 全テーブル共通の規約

| 項目 | 決定 | 理由 |
|---|---|---|
| テーブル名 | 単数形 `snake_case` | `sales` / `receipt` / `billing` 等、伝票系テーブルが単数形のため揃える |
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
| `detail_invoice_sales_line`（連携） | `row_version` を持たない | 行の追加・削除しか発生せず、更新がないため |
| `slip_number_sequence`（採番） | `row_version` を持たない | 採番は `UPDATE` の行ロックで直列化する。楽観的排他だと競合時にリトライが必要になり、採番の直列性と相性が悪い |
| `slip_number_sequence`（採番） | `is_deleted` を持たない | 伝票種別ごとに1行を永続保持するため |

**既知の実装漏れ（解消済み）**: `billing_tax_unit_invoice` / `billing_tax_unit_slip` / `detail_invoice` / `monthly_closing` の4テーブルは、この規約に従い `is_deleted` を持つべきだったが、Phase 1-5 の DDL作成時に漏れていた。Phase 1-6 のエンティティ実装時（実際に書き込みを試みて発覚）に `scripts/004_*.sql` / `005_*.sql` で `ALTER TABLE ... ADD is_deleted` を追加し解消した。`002_create_voucher_tables.sql` 自体は改変していない（3章の運用ルールどおり）。

**税種別区分の訂正（解消済み・2026-09-03）**: 税種別区分は当初「課税10%／軽減8%／非課税／不課税」の4区分としていたが、「不課税」は実在しない誤りであり、正しくは3区分（課税10%／軽減8%／非課税）のみ。`scripts/006_remove_non_taxable_category.sql` で `tax_category` の CHECK 制約を `IN (1,2,3)` に締め直し、`billing_tax_unit_invoice` / `billing_tax_unit_slip` / `detail_invoice` の `non_taxable_amount` 列を削除した。`001_create_master_tables.sql` / `002_create_voucher_tables.sql` 自体は改変していない（3章の運用ルールどおり）。

**税単位別テーブル分割の統合（2026-09-08決定）**: 売上・入金・請求を消費税計算単位ごとに `sales_tax_unit_invoice`/`_slip`/`_line`（3テーブル）、`receipt_tax_unit_invoice`/`_slip`（2テーブル）、`billing_tax_unit_invoice`/`_slip`（2テーブル）へ物理分割していたが、次の問題があったため `sales` / `receipt` / `billing` の3テーブルに統合した（`scripts/010_unify_tax_unit_tables.sql`）。

- 伝票番号の重複禁止が、DB制約ではなく「`slip_number_sequence` の単一系列から採番する」というアプリの運用ルールだけで担保されていた。手動SQLや実装ミスで別テーブルに同じ伝票番号を入れても検知できない。
- 「どのテーブルに書くか」自体がアプリ判断であり、DBは一切保証していなかった。得意先の税区分と異なるテーブルにINSERTしてもDBは受け入れてしまう。
- `receipt_tax_unit_invoice`/`_slip` と `billing_tax_unit_invoice`/`_slip` は構造が完全に同一で、分割理由は「売上の分割方針に揃えるため」という自己目的化したものだった。
- 受注（`order_slip`）は既に税単位で分割していない（M-5、2026-09-03決定）。売上側だけ分割していたのは税額カラムの都合であり、NULL許容カラム＋CHECK制約で解消できた。

統合後は `tax_unit` カラム＋CHECK制約（税単位ごとの税額カラムの対応）＋得意先マスタへの複合FK（税単位の整合）でこれらを解消している。詳細は本章1節および2.9〜2.12節を参照。

---

### 2.1. `customer`（得意先マスタ）

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
| `sales_employee_code` | `varchar(10)` | ○ | **自社の**営業担当社員コード（FK → `employee`）。月次締めの担当者別売上・粗利の集計キー |
| `closing_day` | `tinyint` | × | `0`＝都度・明細／`1`〜`31`＝締め日（実日付）。**末日締めは `99` で表す**（実日付31日と区別するための専用値） |
| `tax_unit` | `tinyint` | × | `1`＝請求単位／`2`＝伝票単位／`3`＝内税明細単位 |
| `rounding_type` | `tinyint` | × | `1`＝切捨／`2`＝四捨五入／`3`＝切上 |
| `print_representative_flag` | `bit` | × | 請求書への代表者印字の要否 |

**「得意先側の担当者名」と「自社の営業担当社員コード」を別項目にしている**（同じ「担当者」という語で別概念を指していたため分離した）。月次締めの担当者別集計は後者を使う。

**CHECK 制約**:

| 制約名 | 条件 | 目的 |
|---|---|---|
| `CK_customer_tax_unit_closing_day` | `(tax_unit = 3 AND closing_day = 0) OR (tax_unit IN (1,2) AND (closing_day BETWEEN 1 AND 31 OR closing_day = 99))` | **前章の相互制約（`税区分 = 内税明細単位 ⇔ closing_day = 0`）を DB 側で強制する。** 破綻する組み合わせ（締め得意先×明細単位／都度得意先×請求単位）を登録できないようにする |
| `CK_customer_tax_unit` | `tax_unit IN (1, 2, 3)` | |
| `CK_customer_rounding_type` | `rounding_type IN (1, 2, 3)` | |
| `CK_customer_closing_day` | `closing_day BETWEEN 0 AND 31 OR closing_day = 99` | |

**`UNIQUE (customer_code, tax_unit)`（`UQ_customer_code_tax_unit`）を持つ**（`scripts/010_unify_tax_unit_tables.sql`）。`sales`/`receipt`/`billing` から `(customer_code, tax_unit)` の複合FKで参照させ、伝票の税単位が得意先マスタの税区分と一致することをDBで強制するための一意インデックス。`customer_code` は既にPKで一意なのでこの制約自体が既存データを弾くことはない。

**締め区分（`closing_day` の 0／非0）と `tax_unit` は登録後に変更できない。** アプリ側で更新時は読み取り専用にする（前章の方針）。統合後は前章末尾のとおり、伝票が存在する得意先の `tax_unit` を直接 `UPDATE` すると複合FK違反（Msg 547）で拒否される。

**`rounding_type`（端数区分）も登録後に変更できない**（2026-09-08 決定。TODO.md 5-1）。変更を許すと発行済み伝票の消費税額を後から再現できず、請求締めで金額が合わなくなるため、`closing_day`/`tax_unit` と同じ扱いにする。

**現時点で持たない項目**: 支払条件（サイト）、与信限度額。設計資料に記載がなく推測になるため。締め得意先の運用で必要になる可能性があるため `docs/design_document.md` の確認事項に記録した。

---

### 2.2. `product`（商品マスタ）

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

**CHECK 制約**: `CK_product_tax_category` … `tax_category IN (1, 2, 3)`

**税種別区分から具体的な税率への対応は税率マスタ（2.3節）を参照する。** 施行日付きの税率マスタとして新設した経緯・解決方法は 2.3節を参照。

**単価計算マスタは作らない（現時点）。** 単価は商品マスタの外税単価・内税単価（得意先の `tax_unit` に応じたどちらか一方）を初期値として転記し、手入力で上書きする方式。単価列の選択ロジックは `IUnitPriceCalculator`（`src/bmcs_app.Domain/Calculations/IUnitPriceCalculator.cs`）インターフェースと、その現時点の実装 `StandardUnitPriceCalculator`（TODO.md 4-2）に集約する。**将来的に掛け率マスタ等を実装する予定があるため、単価決定ロジックを差し替え可能なインターフェース（ドライバ）として設計している（M-3・2026-09-10確定。「将来の差し替えを見据えた抽象化は行わない」という既定方針の例外として明示的に採用）。** 掛け率マスタ実装時は `StandardUnitPriceCalculator` を差し替える（DI登録は `ApplicationServiceCollectionExtensions.AddApplication`）だけで対応する想定。

**原価は商品検索モーダルの「過去の取引履歴から」軸でも商品マスタの標準原価を転記する（暫定）。** 過去の売上行が保持する原価（`sales.cost_price`）は当時のスナップショットだが、粗利計算には現在の標準原価を使う方針とし、履歴軸から選んだ場合も再照会せず商品マスタから取り直す。過去実績原価での粗利計算が必要になった場合は `ProductSearchDialogViewModel.AddHistoryToBasket` を直接修正する。

---

### 2.3. `tax_rate_master`（税率マスタ）

**施行日付きの税率マスタとして新設する。** 税種別区分（`tax_category`）から税率への対応を Domain 層の定数として持つ方針（旧方針）を撤回し、税率改定にコード修正なしで追従できるようにする。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `effective_date` | `date` | PK | 適用開始日。この日付以降（次のレコードの適用開始日の前日まで）に適用される税率を表す |
| `standard_tax_rate` | `decimal(5,2)` | × | 通常税率（`tax_category = 1`） |
| `reduced_tax_rate` | `decimal(5,2)` | × | 軽減税率（`tax_category = 2`） |

**終了日は持たない範囲設定。** 適用期間は「自分の `effective_date` 〜 次に新しい `effective_date` を持つレコードの前日まで（最新レコードは無期限）」で決まるため、終了日カラムは不要（隙間・重複が生じない）。

**税率の解決方法**: 伝票登録時、対象の伝票日付以前で最も新しい `effective_date` を持つレコード（`WHERE effective_date <= @slip_date ORDER BY effective_date DESC` の先頭1件）を採用し、`tax_category` に応じて `standard_tax_rate` / `reduced_tax_rate` のいずれかを売上明細行・受注明細行の `tax_rate` に転記する。転記後は伝票側にスナップショットとして残るため、マスタ側の税率改定が既存伝票に影響しないという既存方針（1章）は変わらない。

**非課税（`tax_category = 3`）は本マスタを参照しない。** 非課税の `tax_rate` は `0` を転記する。

---

### 2.4. `employee`（社員マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `employee_code` | `varchar(10)` | PK | 社員コード。起動時パラメータで渡される値 |
| `employee_name` | `nvarchar(40)` | × | |
| `employee_name_kana` | `nvarchar(40)` | ○ | |
| `permission_level` | `tinyint` | × | 権限レベル。`menu.required_permission_level` と比較する |

---

### 2.5. `company_info`（自社情報マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `company_info_id` | `tinyint` | PK | **固定値 `1`**。1レコード運用 |
| `company_name` | `nvarchar(60)` | × | |
| `invoice_registration_number` | `varchar(14)` | × | 適格請求書発行事業者の登録番号（`T` ＋13桁）。法定記載事項 |
| `postal_code` | `varchar(8)` | ○ | |
| `address1` / `address2` | `nvarchar(100)` | ○ | |
| `phone_number` / `fax_number` | `varchar(20)` | ○ | |
| `representative_name` | `nvarchar(40)` | ○ | 代表者名 |

**CHECK 制約**: `CK_company_info_single_row` … `company_info_id = 1`（複数行の登録を防ぐ）

振込口座は持たない（`bank_account` を参照）。

---

### 2.6. `bank_account`（銀行口座マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `bank_account_code` | `varchar(10)` | PK | |
| `bank_name` | `nvarchar(40)` | × | 銀行名 |
| `branch_name` | `nvarchar(40)` | × | 支店名 |
| `account_type` | `tinyint` | × | `1`＝普通／`2`＝当座 |
| `account_number` | `varchar(10)` | × | |
| `account_holder_name` | `nvarchar(60)` | × | 口座名義 |
| `is_print_on_invoice` | `bit` | × | 請求書・明細請求書に印字する口座かどうか |
| `display_order` | `smallint` | × | 表示順 |

**CHECK 制約**: `CK_bank_account_type` … `account_type IN (1, 2)`

**このテーブルの用途は推測にもとづく。** 設計資料の「銀行マスタ」には用途の記載がないため、**自社の入金口座マスタ**（入金入力で入金先口座を選ぶ／請求書に振込先を印字する）と解釈した。全銀協の金融機関コードマスタである可能性も残るため、`docs/design_document.md` の確認事項に記録した。

---

### 2.7. `menu`（メニュー構成マスタ）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `menu_code` | `varchar(20)` | PK | |
| `parent_menu_code` | `varchar(20)` | ○ | 親メニュー（FK → `menu` の自己参照）。`NULL`＝最上位 |
| `menu_name` | `nvarchar(40)` | × | |
| `display_order` | `smallint` | × | 同一階層内の表示順 |
| `required_permission_level` | `tinyint` | ○ | 最小必須権限。**子（末端の機能メニュー）のみ設定し、親は `NULL`** |
| `screen_key` | `varchar(40)` | ○ | 起動する画面の識別子。親は `NULL` |

**CHECK 制約**: `CK_menu_leaf` … `(screen_key IS NULL AND required_permission_level IS NULL) OR (screen_key IS NOT NULL AND required_permission_level IS NOT NULL)`

→ 「権限は子にのみ持たせ、親（分類の見出し）には持たせない」という方針を DB 側で強制する。親には遷移先画面も権限もなく、子には両方ある。

---

---

### 2.8. 伝票系テーブルの全体像

| 業務概念 | テーブル | 構成 |
|---|---|---|
| 売上（全税単位共通） | `sales` | 明細行1テーブル |
| 入金（締め入金。請求単位／伝票単位共通） | `receipt` | 明細行1テーブル |
| 明細入金（明細単位） | `detail_receipt` | 明細行1テーブル |
| 請求データ（請求単位／伝票単位共通） | `billing` | ヘッダーのみ |
| 明細請求書 | `detail_invoice` | ヘッダーのみ |
| 明細請求書 ↔ 売上明細行の連携 | `detail_invoice_sales_line` | 連携（多対多） |

**2026-09-08決定（統合）**: 以前は税単位（請求単位／伝票単位／内税明細単位）ごとに `sales_tax_unit_invoice`/`_slip`/`_line`、`receipt_tax_unit_invoice`/`_slip`、`billing_tax_unit_invoice`/`_slip` の8テーブルに物理分割していたが、`sales`/`receipt`/`billing` の3テーブルに統合した（経緯は1章末尾を参照、DDLは `scripts/010_unify_tax_unit_tables.sql`）。税単位は各テーブルの `tax_unit` カラム（1=請求単位／2=伝票単位／3=内税明細単位）で表す。

得意先の税区分によって、その得意先のデータがどのテーブル・`tax_unit` 値に入るかが一意に決まる。

| 得意先の税区分 | 売上 | 入金 | 請求 |
|---|---|---|---|
| 請求単位 | `sales`（`tax_unit=1`） | `receipt`（`tax_unit=1`） | `billing`（`tax_unit=1`） |
| 伝票単位 | `sales`（`tax_unit=2`） | `receipt`（`tax_unit=2`） | `billing`（`tax_unit=2`） |
| 内税明細単位（＝都度得意先） | `sales`（`tax_unit=3`） | `detail_receipt` | `detail_invoice`（繰越残高の概念がないため請求データではない） |

**この対応はDBの複合FKで強制される。** `customer` の `UNIQUE (customer_code, tax_unit)` を `sales`/`receipt`/`billing` から `(customer_code, tax_unit)` の複合FKで参照するため、得意先マスタの税区分と異なる `tax_unit` でINSERTすることはできない。同様に `billing` の `UNIQUE (billing_number, tax_unit)` を `sales`/`receipt` から複合FKで参照するため、税単位をまたいで請求データを参照することもできない（`billing_number IS NULL` の行はFK検査対象外）。

#### 非正規化構成の帰結（重要な運用ルール）

売上・入金・明細入金は明細行1テーブル構成のため、**伝票単位の値（伝票日付・得意先コード・入金額・伝票単位の消費税額など）は、同一伝票の全明細行に同じ値が入る。**

> **これらのカラムを `SUM` してはいけない。** 伝票の行数だけ多重計上される。伝票単位で1行に絞ってから扱う（`GROUP BY` 伝票番号など）。

**伝票の取消は共通カラムの `is_deleted` で表す**（物理削除しない）。誰がいつ取消したかは監査列で追跡する。

---

### 2.9. 売上（`sales`）

**主キーは (`sales_slip_number`, `line_number`) の複合キー。** 旧 `sales_tax_unit_invoice`/`_slip`/`_line` の3テーブルを統合したもの（2.8節）。

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

**得意先名・商品名・規格・単位・単価・原価・税率をスナップショットとして保持する。** 帳票の再発行時に当時の値で出力する必要があり、マスタ改定の影響を受けてはならないため。後から `ALTER TABLE` で追加しても過去データを埋め戻せないので、ここで確定させる。

#### `tax_unit` と税額カラムの対応（CHECK制約で強制）

旧・分割構成では「そのテーブルにその列が無い」ことで表現されていた対応を、統合後は次のCHECK制約で明示的に強制する。

| `tax_unit` | `slip_tax_amount` | `tax_amount` | `billing_number` | 理由 |
|---|---|---|---|---|
| `1`（請求単位） | NULL | NULL | NULL可 | 請求締め時に一括計算するため、伝票時点では税額が存在しない |
| `2`（伝票単位） | NOT NULL | NULL | NULL可 | 伝票登録時に伝票単位で税額を確定する |
| `3`（内税明細単位） | NULL | NOT NULL | 常にNULL | 明細行ごとに内税額を確定する。締め請求データを持たない |

- `CK_sales_tax_amount_by_tax_unit` … `tax_unit` と `slip_tax_amount`/`tax_amount` の対応
- `CK_sales_billing_number_by_tax_unit` … `billing_number IS NULL OR tax_unit IN (1, 2)`

**`tax_amount` / `slip_tax_amount` は1円単位（小数部は常に `.00`）で確定する。** `amount` も得意先の端数区分（`ConsumptionTaxCalculator.CalculateLineAmount`）で1円単位に丸めてから保存する。数量 `decimal(13,3)` ×単価 `decimal(15,4)` の積をそのまま渡すと、SQL Server 側で得意先の端数区分を無視した四捨五入が起きるため（TODO.md 5-1）。

---

### 2.10. 締め入金（`receipt`）

**主キーは (`receipt_slip_number`, `line_number`)。各明細行が1件の充当を表す。** 旧 `receipt_tax_unit_invoice`/`_slip` の2テーブルを統合したもの（2.8節）。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `receipt_slip_number` | `varchar(20)` | PK | 入金伝票番号 |
| `line_number` | `smallint` | PK | 行番号 |
| `receipt_date` | `date` | × | 入金日（**伝票単位の値**） |
| `customer_code` | `varchar(10)` | × | （**伝票単位の値**） |
| `tax_unit` | `tinyint` | × | `1`＝請求単位／`2`＝伝票単位（内税明細単位の入金は `detail_receipt` が担うため対象外） |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `receipt_method` | `tinyint` | × | `1`＝現金／`2`＝振込／`3`＝手形／`4`＝相殺 |
| `bank_account_code` | `varchar(10)` | ○ | 入金先口座（FK → `bank_account`）。振込のとき使用 |
| `receipt_amount` | `decimal(15,2)` | × | 入金額（**伝票単位の値。`SUM` してはいけない**） |
| `billing_number` | `varchar(20)` | ○ | 充当先の請求データ。**`NULL`＝前受・過入金（充当先未定）** |
| `allocated_amount` | `decimal(15,2)` | × | この行の充当額 |
| `fee_adjustment_amount` | `decimal(15,2)` | × | 振込手数料差額の調整額 |
| `allocation_status` | `tinyint` | × | `1`＝未充当／`2`＝一部充当／`3`＝充当完了 |
| `slip_remarks` | `nvarchar(200)` | ○ | 伝票摘要（**伝票単位の値**。同一伝票の全行に複写。1章参照） |
| `line_remarks` | `nvarchar(100)` | ○ | 行摘要 |

締め得意先は請求単位で古い順に自動消込するため、1回の入金が複数の請求にまたがる場合は複数行になる。

---

### 2.11. 明細入金（`detail_receipt`）

**統合対象外（構造が異なる）。** 明細単位（都度得意先）の入金はこのテーブルが担う。主キーは (`detail_receipt_number`, `line_number`)。

締め入金との違いは**充当先が2種類あること**（売上伝票を直接指定する場合と、明細請求書を指定する場合）。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `detail_receipt_number` | `varchar(20)` | PK | 明細入金番号 |
| `line_number` | `smallint` | PK | 行番号 |
| `receipt_date` | `date` | × | （**伝票単位の値**） |
| `customer_code` | `varchar(10)` | × | （**伝票単位の値**） |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `receipt_method` | `tinyint` | × | 締め入金と同じ区分 |
| `bank_account_code` | `varchar(10)` | ○ | FK → `bank_account` |
| `receipt_amount` | `decimal(15,2)` | × | 入金額（**伝票単位の値**） |
| `target_type` | `tinyint` | × | `1`＝売上明細行を直接指定／`2`＝明細請求書を指定 |
| `target_sales_slip_number` | `varchar(20)` | ○ | `target_type=1` のとき使用。**FK参照先は `sales`**（統合後） |
| `target_sales_line_number` | `smallint` | ○ | 同上 |
| `target_detail_invoice_number` | `varchar(20)` | ○ | `target_type=2` のとき使用 |
| `allocated_amount` | `decimal(15,2)` | × | この行の充当額 |
| `fee_adjustment_amount` | `decimal(15,2)` | × | |
| `allocation_status` | `tinyint` | × | 締め入金と同じ区分 |
| `slip_remarks` | `nvarchar(200)` | ○ | 伝票摘要（**伝票単位の値**。同一伝票の全行に複写。1章参照） |
| `line_remarks` | `nvarchar(100)` | ○ | 行摘要 |

**CHECK 制約** `CK_detail_receipt_target` … `target_type` と実際に埋まっているカラムを一致させる。

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

**`target_sales_slip_number`/`target_sales_line_number` の対象は実際には `tax_unit=3` の `sales` 行のみだが、複合FKにはしない。** `detail_receipt` 側にも `tax_unit` を持たせる非正規化が増えるため、単純な `(sales_slip_number, line_number)` 参照に留め、絞り込みはアプリ側の抽出条件で行う（`scripts/010_unify_tax_unit_tables.sql` 手順8）。

---

### 2.12. 請求データ（`billing`）

**明細行を持たないヘッダー1テーブル。主キーは `billing_number`。** 旧 `billing_tax_unit_invoice`/`_slip` の2テーブルを統合したもの（2.8節）。分割時から「構造が完全に共通」だった（消費税計算のタイミングが違うだけ）ため、統合コストが最も低かった箇所。

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

**`UNIQUE (billing_number, tax_unit)`（`UQ_billing_number_tax_unit`）を持つ。** `sales`/`receipt` から複合FKで参照させるための一意制約。`billing_number` 単独で既に一意なので論理的には冗長だが、SQL Serverが要求するため必要（2.8節）。

**税率別内訳を子テーブルではなく固定カラムで持つ。** 日本の税率は少数の閉じた集合であり、「請求データはヘッダー1テーブル」という方針を崩さずに済むため。請求書の**明細部分**は売上ジャーナルから都度組み立てるが、**税額は本テーブルの確定値を印字**して再発行時に金額が変わらないようにする。

**締め解除では物理削除せず `billing_status` を解除済にする。** 再締めでは新しい `billing_number` を採番する。一度発行した請求書を追跡できるようにするため。

---

### 2.13. 明細請求書（`detail_invoice`）

明細行を持たないヘッダー1テーブル。主キーは `detail_invoice_number`。明細部分は連携テーブル（2.14）経由で売上ジャーナルから組み立てる。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `detail_invoice_number` | `varchar(20)` | PK | 明細請求書番号 |
| `customer_code` | `varchar(10)` | × | 発行元となる正式な得意先 |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `addressee_name` | `nvarchar(60)` | × | **請求書に印字する宛名。都度入力のスナップショット**（学校のクラス・先生単位など） |
| `issue_date` | `date` | × | 発行日 |
| `sales_amount` / `tax_amount` / `total_amount` | `decimal(15,2)` | × | 税抜・消費税・税込 |
| 税率別内訳 | `decimal(15,2)` | × | `billing` と同じ3区分の固定カラム |
| `invoice_status` | `tinyint` | × | `1`＝発行済／`2`＝取消 |
| `issued_at` / `issued_by` | `datetime2(3)` / `varchar(10)` | × | 発行日時・発行者 |
| `cancelled_at` / `cancelled_by` | `datetime2(3)` / `varchar(10)` | ○ | 取消日時・取消者 |

**代表者印字の要否はここに持たない。** 宛名を書き換えても、発行元となる得意先マスタの `print_representative_flag` に従う（`docs/product-spec.md` 共通業務ルール3）。

---

### 2.14. 明細請求書と売上明細行の連携（`detail_invoice_sales_line`）

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `detail_invoice_number` | `varchar(20)` | PK | FK → `detail_invoice` |
| `sales_slip_number` | `varchar(20)` | PK | FK → `sales`（統合後。実際に対象となるのは `tax_unit=3` の行のみ） |
| `sales_line_number` | `smallint` | PK | 同上 |

**対象は `tax_unit=3`（内税明細単位＝都度得意先）の `sales` 行のみに限られる。** これは得意先マスタの相互制約（`税区分 = 内税明細単位 ⇔ closing_day = 0`）から導かれる。統合後は `sales` テーブル自体は税単位を問わず参照できるため、この絞り込みはFKでは強制されずアプリ側の抽出条件に依存する（2.11節と同じ理由）。

#### `UNIQUE (sales_slip_number, sales_line_number)` — 二重請求をDBで防ぐ

「多対多」は**伝票レベル**の話（1つの売上伝票の各明細行が別々の明細請求書に分散しうる）であり、**売上明細行レベルでは1行が紐づく明細請求書は最大1つ**である。

この一意制約により、**同じ売上明細行を2枚の明細請求書に載せる二重請求を DB が拒否する**。アプリ側の抽出条件だけに依存しない。

明細請求書を取消したときは、このテーブルの該当行を削除して売上明細行の請求状態を未請求に戻す。行の追加・削除しか発生しないため、**このテーブルは `row_version` を持たない**（監査列は持つ）。

---

### 2.15. 受注（`order_slip`）

**M-5 の決定（売上・入金と同じ非正規化）に沿い、明細行1テーブル構成とする。** 主キーは (`order_slip_number`, `line_number`)。売上テーブル（2.9）と共通するカラムはそのまま踏襲し、受注固有のカラムのみ以下に示す。

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

**税のスナップショットを持つ理由は売上テーブルと同じ**（`docs/database-schema.md` 1章）。受注段階では消費税額そのものは確定しないが、見積・受注控えの表示や売上化時の初期値として使うため、商品マスタからの転記時点の税率を保持する。税額（`tax_amount` 等）は持たない。売上化時は `sales` 側で税額を計算する。

**`order_status` はキャッシュ列。** 売上化・中止・売上取消のたびに、`sales_confirmed_quantity` の更新と同一トランザクション内で更新する（`docs/architecture.md` 6章）。逆遷移（売上取消時に `売上完了`／`一部売上` → `一部売上`／`未売上` に戻す）も同じ処理で扱う。**中止（`4`）は伝票単位の操作かつ終端状態**（`docs/product-spec.md`「受注」参照）。中止時も `sales_confirmed_quantity` は変更しない（分納済みの実績を残す）。実装は `src/bmcs_app.Application/Order/OrderStatusService.cs`（TODO.md 4-4、`docs/design_document.md` 7章）。

**納品書発行状態・請求状態・消込状態は持たない。** これらは売上化された後（`sales`）で管理する状態であり、受注はまだ売上・売掛金を発生させていないため対象外。

---

### 2.16. 月次締め（`monthly_closing`）

**得意先×月末日で1レコード（`billing`類似レイアウト。2026-09-09決定）。** 全得意先（`tax_unit`問わず）が対象。旧設計（全社単位で月次に1レコード）から変更した（経緯は1章「月次締め（`monthly_closing`）は…」を参照）。

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `closing_date` | `date` | PK | 対象月の**月末日**（例: `2026-02-28`）。集計期間は「月初〜この日付」の暦月 |
| `customer_code` | `varchar(10)` | PK | FK（`customer_code`, `tax_unit`の複合）→ `customer` |
| `tax_unit` | `tinyint` | × | 集計時点の得意先税区分のスナップショット |
| `customer_name` | `nvarchar(60)` | × | スナップショット |
| `previous_balance` | `decimal(15,2)` | × | 前月末売掛残高（＝前月の本テーブルの`closing_balance`） |
| `sales_amount` | `decimal(15,2)` | × | 当月（暦月）売上金額 |
| `receipt_amount` | `decimal(15,2)` | × | 当月（暦月）入金金額 |
| `tax_amount` | `decimal(15,2)` | × | 消費税額。`tax_unit=1`の未確定区間（次回請求締めをまだ通っていない伝票）は仮計算した値（1章参照） |
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
| `CK_monthly_closing_tax_unit` | `tax_unit IN (1, 2, 3)` |
| `CK_monthly_closing_status` | `closing_status IN (1, 2)` |

**複合FK** `(customer_code, tax_unit)` → `customer`（`UQ_customer_code_tax_unit`）。`billing`/`sales`/`receipt`と同じパターンで、得意先マスタの税区分との整合をDBで強制する。

**締め解除では`billing`と同様に物理削除せず`closing_status`を解除済にする。** 一度確定した月次残高を追跡できるようにするため（本ファイル1章）。

#### 編集ロックは導出方式（伝票側にフラグを持たない）

売上・入金の編集可否は、**`customer_code`＋伝票日付の年月と本テーブルを突き合わせて判定する**（1章の編集ロック方針を参照。`billing`への集計済みかどうか・入金済みかどうかも合わせて判定する。C-6・2026-09-10確定で入金済み条件を追加）。

```
編集不可 ⇔
  (customer_code, slip_dateの年月) に一致する monthly_closing レコードが存在し closing_status = 1（確定）
  OR
  billing_number IS NOT NULL（＝紐づく billing.billing_status = 確定。sales/receipt 共通）
  OR
  入金済み（sales.settlement_status = 消込完了 ／ receipt・detail_receipt.allocation_status = 充当完了）
```

`order_slip`（受注）はこの3条件のいずれにも該当しないため常に直接修正可能（受注は請求・消込の対象外。C-6）。

伝票側にロックフラグを持たせない理由は、締め・解除のたびに大量の伝票行を更新することになるため（`docs/architecture.md` 9章、および本ファイル1章の方針）。

#### 集計結果を保存する（旧方針からの変更）

**旧方針（M-6, 2026-09-03決定）は「集計結果は保存せず都度集計する」だったが、本テーブルに関しては撤回した。** 理由は性能ではなく、`tax_unit=1`の得意先の暦月末時点の税額が、都度計算では確定できないため（1章参照。`billing`の締め期間と`monthly_closing`の暦月が食い違い、`sales`側に税額を書き込めない）。**確定した`monthly_closing`行の税額は、都度再計算しても異なる値になり得る**（`billing`確定前の仮計算のため）ので、確定時点の値をこのテーブルに保存し、以後はこの保存値を参照する。

担当者別売上・粗利の集計は、この変更の対象外。従来どおり**保存せず都度集計する**（M-6決定は担当者別集計についてのみ有効。性能問題が出た場合に別途集計結果テーブルを追加する）。

---

### 2.17. 採番（`slip_number_sequence`）

M-2 の暫定設定（年度リセットなしの通し連番・採番テーブル方式）にもとづく。**伝票種別ごとに1行を永続保持する。**

| カラム | 型 | NULL | 内容 |
|---|---|---|---|
| `sequence_key` | `varchar(30)` | PK | 伝票種別。`order_slip` / `sales_slip` / `receipt_slip` / `detail_receipt` / `billing` / `detail_invoice` |
| `current_value` | `bigint` | × | 現在の採番値。次番は `current_value + 1` |

**採番は伝票登録と同一トランザクション内で行う**（`docs/architecture.md` 6章）。別トランザクションで先に採番すると登録失敗時に欠番が出るため。`UPDATE` の行ロックで直列化するので、`row_version`（楽観的排他）は持たない。

税単位によって売上テーブルが3つに分かれるが、**採番は税単位ごとに分けず `sales_slip` の1系列とする**。伝票番号が得意先の税区分によって別系列になると、現場で伝票番号から伝票を探すときに混乱するため。

#### 伝票番号の表記形式（TODO.md 4-1、暫定）

M-2 本体（採番規則そのもの）は未確定のままだが、実装のために以下を暫定として確定した
（2026-09-08 ユーザー確認済み）。

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
  （`UPDATE ... OUTPUT` による同時実行制御）。M-2 が確定した場合はこの2ファイルを直接修正する。

#### このテーブルは EF Core で追跡しない

採番は生SQLの `UPDATE` で行い ChangeTracker を経由しない。追跡した状態で読むと、
別セッションの採番結果を古い値で上書きしうる（`row_version` を持たないため EF Core は検出できない）。
参照するときは必ず `AsNoTracking()` を使う。

#### 重複禁止の最終防衛線

万一採番が重複しても、`sales`/`order_slip` 等の複合PK（`(伝票番号, 行番号)`）が
2件目の INSERT を拒否する（明細行番号は両方とも1から始まるため必ず衝突する）。
2.8節で解消した「伝票番号の重複禁止がアプリの運用ルールだけで担保されていた」問題への
安全網であり、将来 PK を単一列に変えないよう留意する。

---

### 2.18. 状態遷移とカラムの対応（網羅性の確認）

`docs/product-spec.md`「伝票の状態遷移」で定義した全状態が、いずれかのカラムで保持できることを示す。

| 対象 | 状態 | 保持するカラム | テーブル |
|---|---|---|---|
| 受注 | 未売上／一部売上／売上完了／中止 | `order_status`（判定は `order_quantity` と `sales_confirmed_quantity` の比較） | `order_slip` |
| 売上・軸1 | 未発行／発行済 | `delivery_note_issued_at`（`NULL`＝未発行）＋ `delivery_note_issue_count` | `sales` |
| 売上・軸2 | 未請求／請求済 | `billing_status`。紐付け先は締め請求が `billing_number`、明細請求が `detail_invoice_sales_line` | `sales` |
| 売上・軸3 | 未消込／一部消込／消込完了 | `settlement_status` ＋ `settled_amount` | `sales` |
| 入金 | 未充当／一部充当／充当完了 | `allocation_status` ＋ `allocated_amount` | `receipt`、`detail_receipt` |
| 請求データ | 確定／解除済 | `billing_status` ＋ 確定・解除の日時と実施者 | `billing` |
| 明細請求書 | 発行済／取消 | `invoice_status` ＋ 発行・取消の日時と実施者 | `detail_invoice` |
| 月次締め | 未締め／確定／解除済 | `closing_status`（**未締めはレコード不在で表す**。得意先×月末日で1レコード） | `monthly_closing` |
| 伝票の取消 | — | 共通カラムの `is_deleted`（物理削除しない） | 全伝票テーブル |
| 月次締め・請求締めによる編集ロック | — | **カラムを持たず導出**（`customer_code`＋伝票日付の年月 × `monthly_closing`、または`sales.billing_number`が確定済み`billing`を指すか） | — |

**すべての状態カラムはキャッシュ列**であり、関連伝票の登録・取消・訂正と同一トランザクション内で更新する（`docs/architecture.md` 6章）。

---

## 3. 命名規則・運用

### 3.1. 命名規則

- **テーブル名・カラム名は `snake_case` とする。** C# 側のエンティティクラス名・プロパティ名は PascalCase とし、変換は EF Core の命名変換に任せる（`docs/architecture.md` 10章）。
- **税単位別テーブルの `{ドメイン}TaxUnit{税単位}` 命名パターンは廃止した（2026-09-08）。** `sales`/`receipt`/`billing` の3テーブルに統合し、税単位は `tax_unit` カラムの値で表す（2.8節）。ドメイン名だけの単純な命名になる: `Sales` / `Receipt`（締め入金） / `Billing`（請求データ。税単位を表す `Invoice` と語が衝突しないよう、ドメイン名は `Invoice` ではなく `Billing`）。
- **明細単位の請求・入金は、構造が異なる業務概念として `Detail` を冠して別立てで命名する: `DetailInvoice`（明細請求書）/ `DetailReceipt`（明細入金）。** 統合の対象外（2.11節・2.12節）。明細請求書と売上明細行の連携テーブルは `DetailInvoiceSalesLine`。
- **ユーザー定義ストアドプロシージャには、プレフィックス `usp_` を付ける。**

### 3.2. DDL・スキーマ変更の運用

- **設計上の正はエンティティクラス定義とし、EF Core のマイグレーション機能は使用しない。** DDL は手書きして `scripts/` に連番SQLとして残し、SQLCMD で適用する（「Code-First」という語は、マイグレーションでDDLを生成する運用と誤解されるため使わない）。
- **適用済みDDLの管理**: `scripts/` に `001_xxx.sql` 形式の連番で置き、適用したものは削除・改変しない。スキーマを変更する際は新しい連番ファイルを追加する。
  - `003_create_closing_and_sequence_tables.sql` の `slip_number_sequence` 初期行 INSERT は `IF OBJECT_ID(...) IS NULL` の内側（テーブル作成時のみ実行）にある。**将来 `sequence_key` を追加するときは新しい連番SQLで INSERT すること。**
- **コードとDBの乖離防止**: 仕様変更等でプログラムを修正する際は、対応するライブDB（開発用DB）のテーブル・ストアドプロシージャの変更もコード修正と同一の作業内でSQLCMDを用いて追従させる。乖離が疑われる場合は `sys.columns` / `sys.tables` を SQLCMD で照会し、エンティティ定義と突き合わせて確認する。起動時のスキーマ検証は行わない（起動が遅くなるため）。
- **接続先の環境情報**: `docs/architecture.md` の「開発用データベース環境」を参照。

---

## 4. 未確定のDB設計判断

**暫定設定で進めている項目の一覧は `TODO.md` の「保留項目の扱い」を正とする。** ここには DB 設計に直接影響するものだけを挙げる。方針が変わった場合、抽象化していないため該当箇所を直接修正する必要がある。

| 論点 | 現在の暫定 | 確定したときに影響する範囲 |
|---|---|---|
| M-2 採番規則 | 年度リセットなしの通し連番・採番テーブル方式。表記は接頭辞なし・8桁ゼロ埋め（2.17節、TODO.md 4-1で暫定確定） | 採番テーブルの構造、伝票番号のカラム長。表記変更時は `SlipNumberFormatter.cs`／`SlipNumberSequenceCommand.cs` を直接修正 |
| M-3 単価決定ロジック | **2026-09-10 業務確認済み。** 商品マスタの外税単価／内税単価（得意先の`tax_unit`で選択）を転記。**単価計算マスタは作っていないが、将来の掛け率マスタ実装に備え`IUnitPriceCalculator`インターフェースとして実装済み**（例外的にドライバ化。M-3） | 掛け率マスタ実装時は`StandardUnitPriceCalculator`の差し替えのみで対応（DI登録済み） |
| M-4 原価の取得元 | **2026-09-10 業務確認済み（確定）。** 仕入機能がスコープ外のため、商品マスタの `standard_cost_price` を転記する | 仕入機能を実装する際に最終仕入原価・移動平均等へ再検討 |
| M-11 リアルタイム残高 | 都度集計（残高キャッシュ列を持たない） | 性能不足なら残高キャッシュ列を追加 |
| 得意先の支払条件・与信限度額 | 項目を作っていない（設計資料に記載がないため） | `customer` への `ALTER TABLE` で追加 |
| 銀行マスタの用途 | 自社の入金口座マスタと解釈（`bank_account`） | 金融機関コードマスタだった場合は構造が変わる |

上位2つ（支払条件・銀行マスタの用途）は `docs/design_document.md` の確認事項にも記載している。
