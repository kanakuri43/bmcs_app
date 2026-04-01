# タスク管理

## 完了

### [x] マスタメンテ サンプル作成（社員マスタ）
作成ファイル:
- `bmcs_app.Core/Models/Employee.cs`
- `bmcs_app.Core/Interfaces/IEmployeeRepository.cs`
- `bmcs_app.Infrastructure/Repositories/EmployeeRepository.cs`
- `bmcs_app.Master/` (新規プロジェクト)
  - `MasterModule.cs`
  - `Views/EmployeeMaintView.xaml`
  - `ViewModels/EmployeeMaintViewModel.cs`
- `bmcs_app/App.xaml` + `App.xaml.cs`
- `bmcs_app/Views/MainWindow.xaml` + `.xaml.cs`
- `bmcs_app/ViewModels/MainWindowViewModel.cs`

### [x] Phase 1: 画面構成の確定 → CLAUDE.md 作成

### [x] Phase 2: 残りのマスタメンテ（部分）
- [x] 得意先マスタ
- [x] 消費税率マスタ
- [ ] 商品マスタ ← Phase 3 完了後に着手
- [ ] 自社情報 ← 同上

---

## 進行中

### Phase 3: [ ] 売上登録（bmcs_app.Sales）

#### DB スキーマ確認済み
- `sales` テーブル: sale_no, sale_date, customer_id, order_id?, employee_id + 明細行
- SP: `usp_sales_select`, `usp_sales_upsert`（JSON TVP @lines）, `usp_sales_delete`
- 税種別: 1=外税, 2=内税, 3=非課税

#### 作成済みファイル
- `Core/Models/` — `Product.cs`, `TaxTypeClassification.cs`, `SlipSummary.cs`
- `Core/Interfaces/` — `ILookupService.cs`, `IProductRepository.cs`, `ISaleRepository.cs`
- `Infrastructure/Repositories/` — `ProductRepository.cs`, `TaxTypeRepository.cs`, `SaleRepository.cs`（GetSummariesAsync のみ）
- `Sales/Views/` — `SalesMainView.xaml`（レイアウト確定）, `MasterSearchDialog.xaml`
- `Sales/ViewModels/` — `SalesMainViewModel.cs`（プレースホルダ）, `SaleLineViewModel.cs`
- `Sales/Services/` — `LookupService.cs`
- `Sales/App.xaml.cs` — DI 設定済み

#### 実装ステップ
- [x] 3-1. XAML レイアウト作成・確認（画像参照ベースで確定）
- [x] 3-1b. コード検索パターン実装
  - Space キー → `MasterSearchDialog`（全エンティティ共通）
  - Enter → コード直接補完
  - Enter 確定後 → 次フィールドへ自動フォーカス移動
- [x] 3-1c. フォーカス制御完成
  - `FocusHelper`（添付プロパティ）: Enter で次フィールドへ移動
  - `handledEventsToo=true` で KeyBinding 実行後も確実に発火
  - readonly 名称欄は `IsTabStop="False"` でスキップ
  - 伝票No. を検索欄と兼用（Space=ダイアログ / Enter=伝票読込）
  - 初期フォーカス: `FocusManager.FocusedElement` で伝票No. に設定
  - 摘要 Enter → 行ゼロなら自動追加 → DataGrid 商品コードセルへ移動
    （`FocusField` イベント + コードビハインドで `CurrentCell` + `BeginEdit()`）
- [ ] 3-3. `SaleRepository` 完成（upsert / delete / select by slip_no）
- [ ] 3-4. `SalesMainViewModel` 機能実装
  - 伝票読み込み（SearchCommand / PrevSlip / NextSlip）
  - 保存（`usp_sales_upsert` 呼び出し・税計算込み）
  - 削除（`usp_sales_delete`）
- [ ] 3-7. 最終ビルド・動作確認

---

## 待機中

### Phase 4: [ ] 受注登録（bmcs_app.Order）
- DB スキーマ・SP 確認済み
- 売上登録と同パターン（MasterSearchDialog 等の共通部品を再利用）
- [ ] 4-1〜4-7 （売上登録完了後に着手）

### Phase 5: [ ] 入金登録（bmcs_app.Payment）
- DB スキーマ・SP 確認済み
- 明細は入金方法 + 金額のみ（税計算なし）
- [ ] 5-1〜5-7

### Phase 6: [ ] 請求集計・売掛金集計（bmcs_app.Closing）
- Phase 3〜5 完了後に検討

---

## 実装メモ

### 伝票入力画面 共通パターン
- 伝票一覧は `sale_no` でグルーピング（複数行 → 1伝票として表示）
- 明細 DataGrid は編集可能 + 行追加(F2) / 行削除ボタン
- `usp_*_upsert` は JSON TVP（`@lines` パラメータ）でまとめて送信
- 税計算はクライアント側（ViewModel）で実施してから SP へ渡す
- 税率は `tax_rate_periods` テーブルから `sale_date` に対応するレコードを取得

### マスタ参照フィールドのパターン（伝票画面）
- ComboBox は使わず **[コード TextBox] + [名称 TextBox(readonly)]** の2欄構成
- Space キーでダイアログ検索、Enter でコード直接補完
- 詳細は CLAUDE.md「マスタ参照フィールドパターン」参照

### マスタメンテ共通
- Prism 9 の名前空間: `Prism.Navigation.Regions`（`Prism.Regions` は旧）
- SP select 未定義のマスタは直接 SELECT クエリで取得
- usp_employees_upsert は @row_version 不要
