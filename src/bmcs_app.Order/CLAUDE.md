# bmcs_app.Order（受注登録モジュール）

## 役割
受注伝票の新規登録・検索・更新・削除を行う単独 exe。

## 実装状況
未着手。bmcs_app.Sales の実装パターンをそのまま流用する。

## ビジネスルール
- 受注は CRUD 対応（売上登録済みの受注は変更・削除不可）
- 売上登録済みフラグ `has_sales` は `usp_orders_select` が返す
- 変更・削除可否は `usp_orders_update` / `usp_orders_delete` でもチェックされる

## 実装時の参照先
- 画面・操作・フォーカスパターン: `bmcs_app.Sales/CLAUDE.md` を参照
- MasterSearchDialog / LookupService は `bmcs_app.Sales` の実装を再利用
- App.xaml.cs 起動フロー・DI パターン: ルート `CLAUDE.md` の「DI・依存注入」を参照

---

## Sales 実装で発覚した注意点（実装前に必読）

### 1. UQ_orders_line はフィルター付きユニークインデックス
`usp_orders_upsert`（未実装）が Sales と同じ「論理削除 + 再 INSERT」パターンを使う場合、
`UQ_orders_line` が通常の UNIQUE CONSTRAINT だと更新時に UNIQUE KEY 違反が発生する。

**対応済み**: `UQ_orders_line` はフィルター付きインデックス `WHERE (is_deleted = 0)` に変更済み。
新たに UNIQUE 制約を追加する際も同様にフィルター付きで作成すること。

```sql
-- 正しい定義（対応済み）
CREATE UNIQUE NONCLUSTERED INDEX UQ_orders_line
    ON dbo.orders (order_no, line_no)
    WHERE (is_deleted = 0);
```

### 2. ロック判定は SP 結果に含まれない → 別途 SQL で判定
`usp_orders_select` の結果セットにロック判定列（`has_sales` 等）が含まれない場合は、
Sales と同じように SP 呼び出し後に別途 SQL でチェックする。

```csharp
// Sales の実装例（OrderRepository でも同様のパターンを使う）
var isLocked = (int)lockCmd.ExecuteScalar() > 0;
```

### 3. Space キー + async DelegateCommand の race condition
コード欄の Space キーでダイアログを開くコマンドを **async** にすると、
TextBox の Space 入力がコマンド起動より先に処理されてダイアログが開かない。
起動時にマスタをキャッシュし、コマンドは同期メソッドにすること。

```csharp
// NG: async DelegateCommand は Space キー時に race condition
OpenOrderLookupCommand = new DelegateCommand(async () => await OnOpenOrderLookupAsync());

// OK: 起動時キャッシュを使い同期で開く
OpenOrderLookupCommand = new DelegateCommand(OnOpenOrderLookup); // 同期
```

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
