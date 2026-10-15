using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Windows;
using bmcs_app.Application.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace bmcs_app.ViewModels.Master;

/// <summary>クエリ条件（パラメータ）1件の編集用。</summary>
public sealed partial class QueryParamItem(int id, string name, string value) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;

    [ObservableProperty]
    public partial string Value { get; set; } = value;
}

/// <summary>
/// クエリ実行画面（マスタ出力用）。端末ローカルの <c>bmcs_queries.xml</c> に書いたクエリを選んで実行し、
/// 結果をグリッド表示・CSV保存する。
/// </summary>
public partial class QueryRunnerViewModel(QueryRunnerService queryRunner) : ViewModelBase
{
    private DataTable? _result;

    public ObservableCollection<QueryDefinition> Queries { get; } = [];

    public ObservableCollection<QueryParamItem> Params { get; } = [];

    [ObservableProperty]
    public partial QueryDefinition? SelectedQuery { get; set; }

    [ObservableProperty]
    public partial DataView? ResultView { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    partial void OnSelectedQueryChanged(QueryDefinition? value)
    {
        Params.Clear();
        foreach (var p in value?.Params ?? [])
        {
            Params.Add(new QueryParamItem(p.Id, p.Name, p.Value));
        }
    }

    partial void OnResultViewChanged(DataView? value) => SaveCsvCommand.NotifyCanExecuteChanged();

    /// <summary>画面表示時にクエリファイルを読み込む（Window の Loaded から呼ばれる）。</summary>
    [RelayCommand]
    private void Load()
    {
        try
        {
            var selectedId = SelectedQuery?.Id;
            var queries = queryRunner.Load();
            Queries.Clear();
            foreach (var q in queries)
            {
                Queries.Add(q);
            }

            SelectedQuery = Queries.FirstOrDefault(q => q.Id == selectedId) ?? Queries.FirstOrDefault();
            StatusMessage = $"クエリファイル: {QueryRunnerService.FilePath}";
        }
        catch (Exception ex)
        {
            ShowError("クエリファイルを読み込めません。", ex);
        }
    }

    [RelayCommand]
    private Task RunAsync() => RunBusyAsync(async () =>
    {
        if (SelectedQuery is not { } query)
        {
            return;
        }

        var values = Params.ToDictionary(p => p.Id, p => p.Value);
        try
        {
            _result = await queryRunner.RunAsync(query, values);
            ResultView = _result.DefaultView;
            StatusMessage = $"{_result.Rows.Count:N0} 件";
        }
        catch (Exception ex)
        {
            ShowError("クエリの実行に失敗しました。", ex);
            return;
        }

        try
        {
            queryRunner.SaveParamValues(query.Id, values);
        }
        catch (Exception ex)
        {
            StatusMessage += $"（条件値を保存できません: {ex.Message}）";
        }
    });

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void SaveCsv()
    {
        var dialog = new SaveFileDialog
        {
            Title = "CSVファイル保存",
            Filter = "CSVファイル|*.csv",
            FileName = SelectedQuery?.Name,
        };
        if (dialog.ShowDialog() != true || _result is null)
        {
            return;
        }

        try
        {
            QueryRunnerService.WriteCsv(_result, dialog.FileName!);
            StatusMessage = $"CSVファイルを保存しました: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            ShowError("CSVを保存できません。", ex);
        }
    }

    private bool HasResult() => ResultView is not null;

    /// <summary>メモ帳でクエリファイルを編集し、閉じたら再読み込みする。</summary>
    [RelayCommand]
    private Task EditFileAsync() => RunBusyAsync(async () =>
    {
        try
        {
            queryRunner.Load(); // 無ければ雛形を作る
            using var process = Process.Start("notepad", $"\"{QueryRunnerService.FilePath}\"");
            if (process is not null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            ShowError("クエリファイルを開けません。", ex);
            return;
        }

        Load();
    });

    private static void ShowError(string message, Exception ex) =>
        MessageBox.Show($"{message}\n{ex.Message}", "クエリ実行", MessageBoxButton.OK, MessageBoxImage.Error);
}
