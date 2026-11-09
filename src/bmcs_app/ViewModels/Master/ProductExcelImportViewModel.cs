using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using bmcs_app.Application.Master;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace bmcs_app.ViewModels.Master;

/// <summary>プレビュー・結果一覧の1行。取込実行後は <see cref="StateText"/>・<see cref="Reason"/> を結果で更新する。</summary>
public sealed partial class ProductImportRowItem(ProductImportRow row) : ObservableObject
{
    public ProductImportRow Row { get; } = row;

    [ObservableProperty]
    public partial string StateText { get; set; } = row.Status switch
    {
        ProductImportStatus.New => "新規",
        ProductImportStatus.Update => "更新",
        _ => "エラー",
    };

    [ObservableProperty]
    public partial string Reason { get; set; } = row.Reason ?? string.Empty;
}

/// <summary>
/// 商品マスタExcel取込画面（docs/design_document.md 38章）。ファイル選択→プレビュー→取込実行。
/// 取込は <see cref="ProductExcelImportService"/> が全件を1トランザクションで行う。
/// </summary>
public partial class ProductExcelImportViewModel(ProductExcelImportService importService) : ViewModelBase
{
    public ObservableCollection<ProductImportRowItem> Rows { get; } = [];

    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? FileError { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>取込を実行済みか（同じ読込結果で二重に実行させない）。</summary>
    private bool _imported;

    [RelayCommand]
    private Task SelectFileAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Excelファイル (*.xlsx)|*.xlsx", Title = "商品マスタのExcelを選択" };
        return dialog.ShowDialog() == true ? LoadFileAsync(dialog.FileName) : Task.CompletedTask;
    }

    /// <summary>Excelを読み込んでプレビューを表示する。ボタン選択とファイルD&amp;Dの共通入口。</summary>
    public Task LoadFileAsync(string path) => RunBusyAsync(async () =>
    {
        FilePath = path;
        FileError = null;
        Rows.Clear();
        _imported = false;
        try
        {
            var result = await importService.ReadAsync(path);
            FileError = result.FileError;
            foreach (var row in result.Rows)
            {
                Rows.Add(new ProductImportRowItem(row));
            }

            StatusMessage = "プレビューを表示しました。";
        }
        catch (Exception ex)
        {
            // ClosedXML は壊れた・xlsx でないファイルで様々な例外を投げるため、まとめて読込エラーにする。
            FileError = $"ファイルを読み込めません: {ex.Message}";
            StatusMessage = string.Empty;
        }

        SummaryText = $"新規 {Count(ProductImportStatus.New)}件 / 更新 {Count(ProductImportStatus.Update)}件 / エラー {Count(ProductImportStatus.Error)}件";
    });

    private int Count(ProductImportStatus status) => Rows.Count(r => r.Row.Status == status);

    /// <summary>処理中（<see cref="ViewModelBase.IsBusy"/>）が終わったとき取込実行の有効状態を再評価する。</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsBusy))
        {
            ImportCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanImport => !IsBusy && !_imported && Rows.Any(r => r.Row.Status != ProductImportStatus.Error);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var targets = Rows.Where(r => r.Row.Status != ProductImportStatus.Error).ToList();
        var errors = Rows.Count - targets.Count;
        var confirm = MessageBox.Show(
            $"{targets.Count}件を商品マスタへ取り込みます（新規 {Count(ProductImportStatus.New)}件・更新 {Count(ProductImportStatus.Update)}件）。よろしいですか？\n" +
            "登録済みの商品は、商品名・JANコード・標準原価・定価を上書きします。" +
            (errors > 0 ? $"\nエラーの{errors}件は取り込みません。" : string.Empty),
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            StatusMessage = "取込中...";
            try
            {
                var (inserted, updated) = await importService.ImportAsync(targets.Select(t => t.Row).ToList());
                foreach (var item in targets)
                {
                    item.StateText = "取込済";
                }

                _imported = true;
                StatusMessage = $"取込が完了しました（新規 {inserted}件／更新 {updated}件）。";
            }
            catch (Exception ex)
            {
                StatusMessage = $"取込エラー（1件も反映していません）: {ex.InnerException?.Message ?? ex.Message}";
            }
        });
        ImportCommand.NotifyCanExecuteChanged();
    }
}
