# bmcs_app.Closing（請求集計・締め処理モジュール）

## 役割
月次の請求集計・売掛金集計・請求書印刷を行う単独 exe。

## 実装済み機能（完成）
- 請求集計（F10）: `usp_invoice_closing` 呼び出し → `invoice_headers` 作成
- 請求集計取り消し（締め解除）: `usp_invoice_closing_cancel` → `invoice_headers` 削除 + `invoiced_at` NULL 戻し（sales・receipts 両方）
- 請求書印刷（F11）: インボイス制度準拠 A4 請求書。自社情報は `company_info` テーブルから取得
- 売掛金集計（F10）: `usp_ar_closing` → `accounts_receivable_histories` 作成
- 売掛金集計取り消し: `usp_ar_closing_cancel` → `accounts_receivable_histories` 削除 + `ar_aggregated_at` NULL 戻し

---

## 画面レイアウト

```
[タブ: 請求処理 | 売掛金集計]
┌──────────────────────────────────────────────────────┐
│ 【請求処理タブ】                                       │
│  締め日  [ComboBox: 末日/X日]                          │
│  処理日付 [DatePicker（月末デフォルト）]               │
│  得意先   (◎) 全得意先  ( ) 指定 [コード][名称]        │
│                              [集計実行(F10)]           │
│ ┌────────────────────────────────────────┐            │
│ │ 対象締日 │ 処理日付   │ 得意先件数      │  ← 集計履歴 │
│ │ 末日    │ 2026/03/31 │ 5               │            │
│ │ ...     │ ...        │ ...             │            │
│ └────────────────────────────────────────┘            │
│                   [請求書印刷(F11)]  [締め解除(F8)]    │
├──────────────────────────────────────────────────────┤
│ 【売掛金集計タブ】                                     │
│  処理日付 [DatePicker（月末デフォルト）]               │
│  得意先   (◎) 全得意先  ( ) 指定 [コード][名称]        │
│                              [集計実行(F10)]           │
│                              [集計取り消し]            │
└──────────────────────────────────────────────────────┘
[StatusBar: 処理メッセージ]
```

- Height="720" Width="1100"
- F10: アクティブタブの集計実行
- F11: 請求書印刷（請求処理タブのみ）
- 「指定」RadioButton は現在 `IsEnabled=False`（全得意先のみ使用可）

---

## キーボードショートカット

| キー | 効果 |
|---|---|
| F10 | 集計実行（アクティブタブへ委譲） |
| F11 | 請求書印刷（履歴選択行の invoice_date で実行） |
| F8  | 締め解除（履歴選択行の invoice_date で実行・確認ダイアログあり） |

---

## 請求処理タブの履歴リスト

集計実行後、実行済みの集計履歴を DataGrid で一覧表示する。

```
列: 対象締日（ClosingDayLabel）/ 処理日付（InvoiceDateLabel）/ 得意先件数（CustomerCount）
```

- 起動時および集計実行・締め解除後に自動リフレッシュ（`LoadHistoryAsync()`）
- **印刷・締め解除は履歴リストで選択した行の日付を使用**（ProcessDate 入力欄は使わない）
- 選択なしの場合、印刷・締め解除ボタンは CanExecute=false（グレーアウト）

### 締め解除の確認ダイアログ
- コードビハインドで `ConfirmCancel` コールバックをセット（MVVM ConfirmCancel パターン）
- `MessageBoxButton.OKCancel` / `MessageBoxResult.Cancel` をデフォルトにする（誤操作防止）

```csharp
// ClosingMainView.xaml.cs
vm.InvoiceTab.ConfirmCancel = message =>
{
    var result = MessageBox.Show(message, "締め解除の確認",
        MessageBoxButton.OKCancel,
        MessageBoxImage.Warning,
        MessageBoxResult.Cancel);       // ← デフォルト Cancel
    return result == MessageBoxResult.OK;
};
```

### InvoiceHistorySummary（Core/Models）
```csharp
public class InvoiceHistorySummary
{
    public DateOnly InvoiceDate    { get; set; }
    public byte     ClosingDay     { get; set; }
    public int      CustomerCount  { get; set; }
    public string ClosingDayLabel  => ClosingDay is 0 or 99 ? "末日" : $"{ClosingDay}日";
    public string InvoiceDateLabel => InvoiceDate.ToString("yyyy/MM/dd");
}
```

`ClosingDay` は `invoice_headers JOIN customers` から `MIN(c.closing_day)` で取得。

---

## ビジネスルール

### 請求集計（usp_invoice_closing）
- パラメータ: `@closing_day tinyint`, `@process_date date`, `@customer_id int = NULL`
- 締め日計算: `closing_day IN (0, 99)` → 月末、それ以外 → DATEFROMPARTS（月末超えは月末丸め）
- Step 1: `sales.invoiced_at = @process_date`（締め日以前の未集計売上）
- Step 2: 再実行時に同一 `customer_id + invoice_date` の `invoice_headers` を先に削除
- Step 3: `receipts.invoiced_at = @process_date`（Step 2 後に実行）
  - 対象: Step 1 で集計対象になった得意先の入金伝票
  - 範囲: `前回 invoice_date < receipt_date <= @closing_date`（Step 2 後なので prev_inv が正確に前回を指す）
- Step 4: CTE chain → `invoice_headers` INSERT:
  - `affected`: 今回集計された得意先（**`sales JOIN customers` で取得**。sales の customer_code/name は不変でない）
    - `customers` から `postal_code`, `address1`, `address2` も取得して INSERT（住所の再発行固定化）
  - `prev_inv`: 直近の `invoice_headers`（ROW_NUMBER で最新1件）
  - `slip_groups`: `(sale_no, tax_rate_type, tax_type_id, applied_tax_rate)` 単位で集計
  - `sales_agg`: 得意先×税率種別で集計（外税 FLOOR(base×rate)、内税 FLOOR(base×rate/(1+rate))）
  - `receipt_agg`: `receipts.invoiced_at = @process_date` の入金合計（Step 3 でセット済みを参照）
  - `current_invoice_amount = 前残 − 入金 + 売上（標準）+ 税（標準）+ 売上（軽減）+ 税（軽減）`

#### 重要: affected CTE は必ず customers テーブルと JOIN すること
`sales` テーブルの `customer_code` / `customer_name` は更新履歴が残るため同一 `customer_id` で複数種類存在しうる。
`DISTINCT customer_id, customer_code, customer_name` では重複行になり `UQ_invoice_headers` 違反が発生する。

```sql
-- NG: sales の非正規化列をそのまま使う
affected AS (SELECT DISTINCT customer_id, customer_code, customer_name FROM sales WHERE ...)

-- OK: customers テーブルから現在の正しいコード・名称を取得
affected AS (
    SELECT DISTINCT s.customer_id, c.customer_code, c.customer_name
    FROM   sales s
    JOIN   customers c ON s.customer_id = c.customer_id AND c.is_deleted = 0
    WHERE  s.is_deleted = 0 AND s.invoiced_at = @process_date ...
)
```

### 請求集計取り消し（usp_invoice_closing_cancel）
1. `DELETE FROM invoice_headers WHERE invoice_date = @process_date`
2. `UPDATE sales SET invoiced_at = NULL WHERE invoiced_at = @process_date`
3. `UPDATE receipts SET invoiced_at = NULL WHERE invoiced_at = @process_date`

### 売掛金集計（usp_ar_closing）
- パラメータ: `@process_date date`, `@customer_id int = NULL`
- `@closing_day` なし（全得意先対象）
- Step 1: `sales.ar_aggregated_at = @process_date`（対象日以前の未集計売上）
- Step 2: `receipts.ar_aggregated_at = @process_date`（対象日以前の未集計入金）
- Step 3: 同一 `customer_id + closing_date` の `accounts_receivable_histories` を先に削除
- Step 4: CTE chain → `accounts_receivable_histories` INSERT:
  - `affected`: 売上 UNION 入金（入金のみの得意先も対象）
  - `prev_ar`: 直近の `accounts_receivable_histories`
  - slip_groups / sales_agg: 請求集計と同様の税計算
  - `receipt_agg`: ar_aggregated_at = @process_date の入金合計
  - `closing_amount = 前残 + 売上（標準）+ 税（標準）+ 売上（軽減）+ 税（軽減）− 入金`

### 売掛金集計取り消し（usp_ar_closing_cancel）
1. `DELETE FROM accounts_receivable_histories WHERE closing_date = @process_date`
2. `UPDATE receipts SET ar_aggregated_at = NULL WHERE ar_aggregated_at = @process_date`
3. `UPDATE sales SET ar_aggregated_at = NULL WHERE ar_aggregated_at = @process_date`

---

## 請求書印刷（インボイス制度準拠 A4）

### 実装クラス
- `Closing/Services/InvoicePrintData.cs` — 印刷用データモデル（`InvoicePrintData` / `InvoiceSlipLine` / `InvoiceReceiptLine` / `InvoiceTaxBreakdown`）
- `Closing/Services/InvoicePrintHelper.cs` — FixedDocument 構築・印刷実行

### プリンタ選択ロジック
`PrinterSettingsConfig.Load()` で `bmcs_printer_settings.json` を読み込む。
- `InvoicePrinter` が設定済み → そのプリンタへダイアログなしで直接印刷
- 未設定（null/空）または送信失敗 → 従来の印刷ダイアログを表示（フォールバック）

### 得意先住所の取得元
`BuildPrintData` では `InvoiceHeader.CustomerPostalCode/Address1/Address2` を使用する。
`_customers` キャッシュ（現在マスタ）は**使わない**。

```
集計実行時 → usp_invoice_closing が customers から住所を読み取り invoice_headers に保存
再発行時   → invoice_headers の保存済み住所を使用（マスタ変更の影響を受けない）
```

### 印刷レイアウト（A4 縦）
```
タイトル「請  求  書」
────────────────────────────────
〒 000-0000（郵便番号があれば）  自社名
住所1（あれば）                   住所 / TEL / FAX
住所2（あれば）                   登録番号: T...
得意先名 御中
請求日 / 締め日
────────────────────────────────
【集計サマリー】
  前回請求額      ¥ xxx,xxx          ┌──────────────┐
  入金額        - ¥ xxx,xxx          │  今回請求額   │
  ─────────────────                  │  ¥ xxx,xxx   │
  今期売上（税抜・標準）¥ xxx,xxx     └──────────────┘
  今期売上（税抜・軽減）¥ xxx,xxx
  消費税（標準）  ¥ xxx,xxx
  消費税（軽減）  ¥ xxx,xxx
  ─────────────────
  今回請求額      ¥ xxx,xxx
────────────────────────────────
日付 | 伝票No. | 摘要 | 税抜/入金額 | 消費税
  ...売上伝票行...
-- 入金 --（セパレータ行）
  ...入金伝票行...（消費税列は空）
────────────────────────────────
※10%対象   税抜金額: xxx   消費税: xxx
※8%対象（軽減税率）...
※本書は消費税法に基づく適格請求書（インボイス）です
```

### インボイス制度 必須記載事項の対応
| 要件 | 対応箇所 |
|---|---|
| 発行事業者の名称・登録番号 | 右上ボックス（`company_info.invoice_no`） |
| 取引年月日 | 請求日 |
| 取引内容 | 明細テーブル（売上伝票 + 入金伝票） |
| 税率別 税抜金額・適用税率 | フッター税率別集計 |
| 税率別 消費税額 | フッター税率別集計 |
| 受取事業者の名称 | 左上「得意先名 御中」 |

### 複数ページ対応
- ページネーションは売上行＋セパレータ行＋入金行の合計で計算（`PrintRow` リストに統合）
- 1ページ目: フルヘッダー（196px）+ 集計サマリー（178px）+ テーブルヘッダー + フッター（80px）差し引き後の行数
- 続紙: コンパクトヘッダー「請求書（続き）」（34px）+ テーブルヘッダー + フッター差し引き後の行数
- 税率別集計・注記は最終ページのみ表示
- 全得意先指定時は得意先ごとに独立したページセット

### PrintRow（InvoicePrintHelper 内部型）
売上行・入金行・セクション区切り行を `private record PrintRow(...)` で統一管理。
`IsSection=true` のとき `BuildSectionRow()` でグレー背景のセパレータとして描画。

### 売上明細（InvoiceSlipDetail）
伝票単位サマリー。税計算は `(sale_no, tax_type_id, applied_tax_rate)` でグループ化：
- 外税 (tax_type_id=1): `FLOOR(group_base × rate)`
- 内税 (tax_type_id=2): `FLOOR(group_base × rate / (1 + rate))`

### 入金明細（InvoiceReceiptDetail）
`invoiced_at = @invoice_date` で特定。`receipt_no` 単位で GROUP BY して合計金額を取得。

---

## App.xaml.cs 起動フロー

```
1. CustomerRepository を同期ロード
2. CompanyInfoRepository を同期ロード
3. ClosingRepository を生成
4. ClosingMainViewModel(customers, closingRepo) を生成
5. vm.InvoiceTab.SetCompanyInfo(companyInfo)
6. ClosingMainView を Show
```

---

## DB 操作

| 操作 | SP名 / クエリ | 備考 |
|---|---|---|
| 請求集計 | `usp_invoice_closing` | @closing_day / @process_date / @customer_id |
| 請求集計取り消し | `usp_invoice_closing_cancel` | invoice_headers 削除 + sales・receipts の invoiced_at NULL |
| 売掛金集計 | `usp_ar_closing` | @process_date / @customer_id |
| 売掛金集計取り消し | `usp_ar_closing_cancel` | ar_histories 削除 + ar_aggregated_at NULL |
| 集計履歴取得 | 直接 SQL（invoice_headers GROUP BY invoice_date） | InvoiceHistorySummary のリスト |
| 請求ヘッダー取得 | 直接 SQL（invoice_headers JOIN customers） | closingDay で得意先絞り込み |
| 売上明細取得 | 直接 SQL（CTE で伝票別集計） | invoiced_at = @invoice_date |
| 入金明細取得 | 直接 SQL（receipt_no で GROUP BY） | invoiced_at = @invoice_date |
| 税率別集計取得 | 直接 SQL（CTE で税率別集計） | invoiced_at = @invoice_date |

---

## 関連テーブル

| テーブル | 用途 |
|---|---|
| `invoice_headers` | 請求集計結果（前残/入金/売上/税/今回請求額）。`customer_postal_code` / `customer_address1` / `customer_address2` を集計時点で保存 |
| `accounts_receivable_histories` | 売掛金集計結果（前残/売上/入金/今月売掛金） |
| `sales` | invoiced_at / ar_aggregated_at でロック管理 |
| `receipts` | invoiced_at（請求集計）/ ar_aggregated_at（売掛金集計）でロック管理 |
| `company_info` | 自社情報（印刷用）|

---

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure または直接 SQL）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない（ConfirmCancel コールバックのセットのみ許容）
- SQLファイルは英語コメントのみ（日本語コメントは ADO.NET エンコードエラーの原因になる）
