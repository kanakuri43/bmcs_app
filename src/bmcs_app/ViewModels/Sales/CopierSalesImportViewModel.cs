using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using bmcs_app.Application.Sales;
using bmcs_app.Domain.Import;
using bmcs_app.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace bmcs_app.ViewModels.Sales;

/// <summary>プレビュー・結果グリッドの1行の状態。</summary>
public enum CopierRowState
{
    Importable,
    Imported,
    Error,
    Excluded,
    /// <summary>取込実行で登録できた。</summary>
    Succeeded,
    /// <summary>取込実行で登録に失敗した。</summary>
    Failed,
}

/// <summary>
/// プレビュー・結果グリッドの1行。パーサーのエラー行（<see cref="Preview"/> なし）も同じ型で表す。
/// 取込実行後は <see cref="State"/>・<see cref="Reason"/> を結果で更新する。
/// </summary>
public sealed partial class CopierImportRowItem : ObservableObject
{
    public int LineNumber { get; init; }
    public string MachineNo { get; init; } = string.Empty;
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public DateOnly? ClosingDate { get; init; }
    public string ModelName { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public int? MergedLineCount { get; init; }

    /// <summary>取込対象になりうる行のプレビュー（パーサーエラー行・ファイルエラー行は null）。</summary>
    public CopierImportPreviewRow? Preview { get; init; }

    /// <summary>成功した伝票番号。</summary>
    public string? SalesSlipNumber { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial CopierRowState State { get; set; }

    /// <summary>エラー・対象外・取込済の理由、取込失敗の理由、成功時は伝票番号。</summary>
    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    public string StateText => State switch
    {
        CopierRowState.Importable => "取込可",
        CopierRowState.Imported => "取込済",
        CopierRowState.Error => "エラー",
        CopierRowState.Excluded => "対象外",
        CopierRowState.Succeeded => "登録済",
        _ => "登録失敗",
    };
}

/// <summary>
/// コピー機売上CSV取込画面（docs/design_document.md 30-3）。ファイル選択→プレビュー→取込実行→納品書の連続印刷。
/// 1行ずつの登録は <see cref="CopierSalesImportService"/> に委ね、画面はウィンドウ単位のDIスコープ
/// （専用の DbContext）で動く。
/// </summary>
public partial class CopierSalesImportViewModel(
    CopierSalesImportService importService,
    DeliveryNoteService deliveryNoteService,
    ReportPrintService reportPrintService) : ViewModelBase
{
    public ObservableCollection<CopierImportRowItem> Rows { get; } = [];

    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    /// <summary>汎用商品が使えない理由。使えるなら null。</summary>
    [ObservableProperty]
    public partial string? ProductError { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>画面表示時に汎用商品COPYCHGを確認する。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            ProductError = await importService.CheckProductAsync();
        }
        catch (Exception ex)
        {
            ProductError = $"商品の確認に失敗しました: {ex.Message}";
        }

        ImportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private Task SelectFileAsync() => RunBusyAsync(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "CSVファイル (*.csv)|*.csv", Title = "コピー機売上CSVを選択" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        FilePath = dialog.FileName;
        Rows.Clear();
        try
        {
            var text = await CopierCsvFileReader.ReadAsync(dialog.FileName);
            var parsed = CopierCsvParser.Parse(text);
            if (parsed.FileError is not null)
            {
                Rows.Add(new CopierImportRowItem { State = CopierRowState.Error, Reason = parsed.FileError });
            }
            else
            {
                await BuildPreviewAsync(parsed);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Rows.Add(new CopierImportRowItem { State = CopierRowState.Error, Reason = $"ファイルを読み込めません: {ex.Message}" });
        }

        UpdateSummary();
        StatusMessage = "プレビューを表示しました。";
        ImportCommand.NotifyCanExecuteChanged();
    });

    private async Task BuildPreviewAsync(CopierCsvParseResult parsed)
    {
        var okRows = parsed.Lines.Where(l => l.Row is not null).Select(l => l.Row!).ToList();
        var previews = await importService.PreviewAsync(okRows);
        var previewBySource = previews.ToDictionary(p => p.Source);

        // CSVの行順で並べる。パーサーのエラー行は理由つきで同じ一覧に出す。
        foreach (var line in parsed.Lines)
        {
            if (line.Row is null)
            {
                Rows.Add(new CopierImportRowItem { LineNumber = line.LineNumber, State = CopierRowState.Error, Reason = line.Error ?? string.Empty });
                continue;
            }

            var p = previewBySource[line.Row];
            Rows.Add(new CopierImportRowItem
            {
                LineNumber = p.LineNumber,
                MachineNo = p.MachineNo,
                CustomerCode = p.CustomerCode ?? string.Empty,
                CustomerName = p.CustomerName ?? string.Empty,
                ClosingDate = p.ClosingDate,
                ModelName = p.ModelName,
                Amount = p.Amount,
                MergedLineCount = p.MergedLineCount,
                Preview = p,
                State = p.Status switch
                {
                    CopierImportStatus.Importable => CopierRowState.Importable,
                    CopierImportStatus.Imported => CopierRowState.Imported,
                    CopierImportStatus.Excluded => CopierRowState.Excluded,
                    _ => CopierRowState.Error,
                },
                Reason = p.Reason ?? string.Empty,
            });
        }
    }

    private bool CanImport => !IsBusy && ProductError is null && Rows.Any(r => r.State == CopierRowState.Importable);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var targets = Rows.Where(r => r.State == CopierRowState.Importable && r.Preview is not null).ToList();
        var amount = targets.Sum(r => r.Amount ?? 0m);
        var confirm = MessageBox.Show(
            $"{targets.Count}件（合計 {amount:N0}円・税別）を売上として取り込みます。よろしいですか？\n" +
            "1件ずつ登録するため、途中で失敗した行があっても登録済みの行は取り消されません。",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        List<CopierImportRowItem> succeeded = [];
        await RunBusyAsync(async () =>
        {
            ImportCommand.NotifyCanExecuteChanged(); // 実行中は押せなくする（二重押し防止）
            StatusMessage = "取込中...";
            try
            {
                var results = await importService.ImportAsync(targets.Select(t => t.Preview!).ToList());
                var itemByPreview = targets.ToDictionary(t => t.Preview!);
                foreach (var result in results)
                {
                    var item = itemByPreview[result.Row];
                    if (result.IsSuccess)
                    {
                        item.State = CopierRowState.Succeeded;
                        item.SalesSlipNumber = result.SalesSlipNumber;
                        item.Reason = $"売上No. {result.SalesSlipNumber}";
                        succeeded.Add(item);
                    }
                    else
                    {
                        item.State = CopierRowState.Failed;
                        item.Reason = result.Error ?? string.Empty;
                    }
                }

                StatusMessage = $"取込が完了しました（成功 {succeeded.Count}件／失敗 {results.Count - succeeded.Count}件）。";
            }
            catch (Exception ex)
            {
                StatusMessage = $"取込エラー: {ex.Message}";
            }

            UpdateSummary();
        });
        ImportCommand.NotifyCanExecuteChanged();

        if (succeeded.Count == 0)
        {
            return;
        }

        var print = MessageBox.Show("納品書を印字しますか？", "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (print == MessageBoxResult.Yes)
        {
            await RunBusyAsync(() => PrintDeliveryNotesAsync(succeeded.Select(s => s.SalesSlipNumber!).ToList()));
        }
    }

    /// <summary>
    /// 成功した伝票の納品書をプレビューなしで連続印刷する。印刷が成功した伝票だけ発行記録を付け、
    /// 1枚失敗しても続行する。プリンタは既存の帳票設定（<see cref="ReportPrintService.Print"/>）に従う。
    /// </summary>
    private async Task PrintDeliveryNotesAsync(IReadOnlyList<string> slipNumbers)
    {
        var ok = 0;
        List<string> failures = [];
        foreach (var number in slipNumbers)
        {
            StatusMessage = $"印刷中... 売上No. {number}";
            try
            {
                var data = await deliveryNoteService.GetAsync(number);
                if (data is null)
                {
                    failures.Add($"{number}: 売上が見つかりません");
                    continue;
                }

                var result = reportPrintService.Print(new DeliveryNoteDocumentBuilder(data).Build(), ReportKind.DeliveryNote, $"納品書 {number}");
                if (!result.Success)
                {
                    failures.Add($"{number}: {result.Message}");
                    continue;
                }

                await deliveryNoteService.MarkIssuedAsync(number);
                ok++;
            }
            catch (Exception ex)
            {
                failures.Add($"{number}: {ex.Message}");
            }
        }

        StatusMessage = $"納品書の印刷が完了しました（成功 {ok}件／失敗 {failures.Count}件）。";
        if (failures.Count > 0)
        {
            MessageBox.Show($"{StatusMessage}\n\n失敗:\n{string.Join('\n', failures)}", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateSummary()
    {
        var importable = Rows.Where(r => r.State == CopierRowState.Importable).ToList();
        var parts = new List<string> { $"取込可 {importable.Count}件（合計 {importable.Sum(r => r.Amount ?? 0m):N0}円）" };
        void Add(string label, CopierRowState state)
        {
            var n = Rows.Count(r => r.State == state);
            if (n > 0)
            {
                parts.Add($"{label} {n}件");
            }
        }

        Add("取込済", CopierRowState.Imported);
        Add("エラー", CopierRowState.Error);
        Add("対象外", CopierRowState.Excluded);
        Add("登録済", CopierRowState.Succeeded);
        Add("登録失敗", CopierRowState.Failed);
        SummaryText = string.Join(" / ", parts);
    }
}
