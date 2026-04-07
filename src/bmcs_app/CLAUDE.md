# bmcs_app（ランチャー）

## 役割
各 exe をメニューから起動するランチャー。Process.Start で exe を呼び出す。
プリンタ設定画面もここで管理する（別 exe なし・ダイアログとして開く）。

## 実装
- `MainWindowViewModel.LaunchCommand`: `DelegateCommand<string>`
- 引数: `"exeName.exe"` または `"exeName.exe --arg=value"` の文字列を空白で分割して実行
- `AppDomain.CurrentDomain.BaseDirectory`（= `bin/Debug/`）から exe を探す
- 見つからない場合は MessageBox でエラー表示

```csharp
Process.Start(new ProcessStartInfo(path)
{
    UseShellExecute = true,
    Arguments       = args,
});
```

## ボタン一覧と起動コマンド

| ボタン | コマンド | CommandParameter |
|---|---|---|
| 受注登録 | `LaunchCommand` | `bmcs_app.Order.exe`（IsEnabled=False） |
| 売上登録 | `LaunchCommand` | `bmcs_app.Sales.exe` |
| 入金登録 | `LaunchCommand` | `bmcs_app.Receipt.exe` |
| 請求集計 | `LaunchCommand` | `bmcs_app.Closing.exe` |
| 伝票検索 | `LaunchCommand` | `bmcs_app.Search.exe` |
| 社員マスタ | `LaunchCommand` | `bmcs_app.Master.exe` |
| 得意先マスタ | `LaunchCommand` | `bmcs_app.Master.exe --master=customer` |
| 商品マスタ | `LaunchCommand` | `bmcs_app.Master.exe --master=product` |
| 消費税率 | `LaunchCommand` | `bmcs_app.Master.exe --master=taxrate` |
| プリンタ設定 | `OpenPrinterSettingsCommand` | — |

## プリンタ設定機能

### 概要
`OpenPrinterSettingsCommand` で `PrinterSettingsWindow` をダイアログとして開く。
`Owner = Application.Current.MainWindow` を設定してメイン画面の裏に隠れないようにする。

### 設定画面（PrinterSettingsWindow）
- `Views/PrinterSettingsWindow.xaml` / `ViewModels/PrinterSettingsViewModel.cs`
- Windows にインストール済みのプリンタ一覧を `System.Printing.LocalPrintServer` で取得
- 納品書プリンタ / 請求書プリンタ を各 ComboBox で選択
- 「（未設定）」を選択 → 印刷時にダイアログを表示（既存動作と同じ）
- F10 / 保存ボタン で `bmcs_printer_settings.json` に書き込み

### bmcs_printer_settings.json（bmcs_config.json と同フォルダ）
```json
{
  "deliverySlipPrinter": "PrinterName",
  "invoicePrinter": "PrinterName"
}
```

### PrinterSettingsConfig（bmcs_app.Infrastructure）
- `Load()`: ファイルがなければデフォルト（null）を返す。JSON エラーは握りつぶしてデフォルト返却
- `Save(config)`: WriteIndented=true で上書き保存

## 依存プロジェクト
- `bmcs_app.Infrastructure`（PrinterSettingsConfig の読み書き）

## ルール
- プリンタ設定以外の ViewModel にロジックなし（Launch メソッドのみ）
- コードビハインドは `InitializeComponent()` のみ
