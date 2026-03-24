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

## 待機中

### Phase 1: [x] 画面構成の確定 → CLAUDE.md 作成
- 社員マスタを動かして画面パターンを確定させる
- Layout・ViewModel・Repository の共通ルールを CLAUDE.md に書き起こす

### Phase 2: [ ] 残りのマスタメンテ
- [x] 得意先マスタ
- [x] 消費税率マスタ
- [ ] 商品マスタ
- [ ] 自社情報
- ~~区分マスタ群（入金方法・課税種別・端数処理・計算単位）~~ ※システムテーブルのため除外

### Phase 3: [ ] 各処理画面
- 受注登録（bmcs_app.Order）
- 売上登録（bmcs_app.Sales）
- 入金登録（bmcs_app.Payment）
- 請求集計・売掛金集計（bmcs_app.Closing）

---
## メモ
- Prism 9 の名前空間: `Prism.Navigation.Regions`（`Prism.Regions` は旧）
- SP select 未定義のマスタは直接 SELECT クエリで取得
- usp_employees_upsert は @row_version 不要（employees テーブルは row_version チェックなし）
