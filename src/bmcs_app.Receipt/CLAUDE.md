# bmcs_app.Receipt（入金登録モジュール）

## 役割
入金伝票の新規登録・検索・更新・削除を行う単独 exe。

## 実装状況
実装完了。

## ビジネスルール
- 入金は得意先単位で登録（伝票No. + 得意先 + 入金日 + 明細）
- 明細: 入金区分（`payment_method_classifications`）+ 金額 + 行摘要
- 請求集計（bmcs_app.Closing）で `invoiced_at` または `ar_aggregated_at` がセットされると編集・削除不可
- ロック判定: `invoiced_at IS NOT NULL OR ar_aggregated_at IS NOT NULL`

## 実装済み機能
- 伝票 CRUD（新規 / 検索 / 保存 / 削除）
- 伝票No. 自動採番（yyyyMMddnnn 形式）
- 前後ナビゲーション（`|◀` / `▶|`）
- 得意先コード欄: Space→ダイアログ検索 / Enter→コード補完
- 伝票No. 欄: Space→伝票検索ダイアログ / Enter→伝票ロード
- 摘要 Enter → 明細 DataGrid 入金区分セルへフォーカス移動
- 明細: 入金区分（ComboBox）/ 金額 / 行摘要
- リアルタイム合計更新（Lines の PropertyChanged）
- 伝票ロック（invoiced_at / ar_aggregated_at が NULL でない場合、保存・削除不可）

## DB テーブル・SP

| 操作 | SP名 / SQL |
|---|---|
| 伝票取得 | `usp_receipts_select`（@receipt_no） |
| 保存（新規/更新）| `usp_receipts_upsert`（@lines に JSON 配列） |
| 削除 | `usp_receipts_delete` |
| 一覧取得 | 直接 SQL（SELECT GROUP BY receipt_no） |

`@lines` JSON フィールド: `line_no`, `payment_method_id`, `amount`, `line_remarks`

### receipts テーブル（主要列）
| 列名 | 型 | 備考 |
|---|---|---|
| receipt_no | nvarchar | 伝票No. |
| receipt_date | date | 入金日 |
| customer_id / customer_code / customer_name | | 得意先（非正規化） |
| line_no | int | 明細行番号 |
| payment_method_id | int | FK to payment_method_classifications |
| amount | decimal | 入金額 |
| slip_remarks / line_remarks | nvarchar | 摘要 |
| invoiced_at / ar_aggregated_at | datetime NULL | 集計日時（ロック判定） |

### ロック判定（Sales と異なる点）
`usp_receipts_select` の結果に **invoiced_at / ar_aggregated_at が含まれる**。
Sales のように別途 SQL でチェックする必要はない（`ReceiptRepository.GetByReceiptNoAsync` 内で SP 結果から直接判定）。

## App.xaml.cs 起動フロー
```
1. CustomerRepository / PaymentMethodRepository を同期ロード
2. LookupService.Initialize(customers, paymentMethods)
3. ReceiptRepository を生成
4. ReceiptMainViewModel(lookupService, receiptRepo) を生成
5. PaymentMethods コレクションに入金区分を追加（DataGrid ComboBox 用）
6. ReceiptMainView を Show
```

## フォーカスフロー
```
[伝票No.] Enter → 伝票を検索して読み込む（SearchCommand）
[得意先コード] 確定 → [摘要]（FocusHelper.MoveNextOnEnter）
[摘要] Enter → 行追加（必要時）→ DataGrid 入金区分セル（FocusField イベント）
```

## 実装時の参照先
- 画面・操作・フォーカスパターン: `bmcs_app.Sales/CLAUDE.md` を参照
- MasterSearchDialog / LookupService は `bmcs_app.Sales` の実装を再利用
- App.xaml.cs 起動フロー・DI パターン: ルート `CLAUDE.md` の「DI・依存注入」を参照
- 伝票ロック方針: ルート `CLAUDE.md` の「伝票ロック方針」を参照

---

## Sales 実装で発覚した注意点（実装前に必読）

### 1. UQ_receipts_line はフィルター付きユニークインデックス
`usp_receipts_upsert`（未実装）が Sales と同じ「論理削除 + 再 INSERT」パターンを使う場合、
`UQ_receipts_line` が通常の UNIQUE CONSTRAINT だと更新時に UNIQUE KEY 違反が発生する。

**対応済み**: `UQ_receipts_line` はフィルター付きインデックス `WHERE (is_deleted = 0)` に変更済み。
新たに UNIQUE 制約を追加する際も同様にフィルター付きで作成すること。

```sql
-- 正しい定義（対応済み）
CREATE UNIQUE NONCLUSTERED INDEX UQ_receipts_line
    ON dbo.receipts (receipt_no, line_no)
    WHERE (is_deleted = 0);
```

### 2. ロック判定は SP 結果に含まれない → 別途 SQL で判定
`usp_receipts_select` の結果セットにロック判定列（`ar_aggregated_at` 等）が含まれない場合は、
SP 呼び出し後に別途 SQL でチェックする。

```csharp
// ReceiptRepository でのパターン例
cmd.CommandText = @"
    SELECT COUNT(1) FROM receipts
    WHERE receipt_no = @receipt_no AND is_deleted = 0
      AND ar_aggregated_at IS NOT NULL";
var isLocked = (int)cmd.ExecuteScalar() > 0;
```

### 3. Space キー + async DelegateCommand の race condition
コード欄の Space キーでダイアログを開くコマンドを **async** にすると、
TextBox の Space 入力がコマンド起動より先に処理されてダイアログが開かない。
起動時にマスタをキャッシュし、コマンドは同期メソッドにすること。

詳細: ルート `CLAUDE.md` の「Space キーとダイアログの注意点」を参照。

### 4. 印刷機能を実装する場合
自社情報（会社名・住所・TEL・FAX・インボイス登録番号）は `company_info` テーブルから取得する。
`CompanyInfoRepository.GetAsync()` を起動時に同期ロードし、VM に `SetCompanyInfo()` で注入する。
詳細は `bmcs_app.Sales/CLAUDE.md` の「印刷」セクションおよび `bmcs_app.Infrastructure/CLAUDE.md` の
「CompanyInfoRepository」を参照。

---

## ルール
- DB 操作はすべて bmcs_app.Infrastructure 経由（StoredProcedure）
- ViewModel に async/await でデータ取得
- コードビハインドにロジックを書かない（フォーカス移動・DataContext 購読のみ許容）
