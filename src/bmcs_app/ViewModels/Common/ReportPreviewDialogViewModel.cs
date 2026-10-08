using System.Windows.Documents;
using bmcs_app.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 帳票プレビュー（帳票基盤）。全帳票が共通で使う汎用ダイアログで、
/// 特定の帳票（納品書等）の知識を持たない。呼び出し元は <see cref="Initialize"/> で
/// 帳票種別・ジョブ名・<see cref="FixedDocument"/> を組み立てるデリゲートを渡す。
/// <see cref="FixedPage"/>は1つのビジュアルツリーにしか属せないため、表示・印刷・PDF保存の
/// 都度デリゲートを呼び直して新しい <see cref="FixedDocument"/> を作る（使い回さない）。
/// </summary>
public partial class ReportPreviewDialogViewModel(ReportPrintService reportPrintService)
    : DialogViewModelBase<bool>
{
    private Func<FixedDocument>? _documentFactory;
    private ReportKind _reportKind;
    private string _jobName = string.Empty;

    [ObservableProperty]
    public partial FixedDocument? Document { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>呼び出し元がウィンドウ表示前に文脈を設定する（WindowService.ShowDialog の configure から呼ぶ）。</summary>
    public void Initialize(ReportKind reportKind, string jobName, Func<FixedDocument> documentFactory)
    {
        _reportKind = reportKind;
        _jobName = jobName;
        _documentFactory = documentFactory;
    }

    [RelayCommand]
    private void Load()
    {
        Document = _documentFactory?.Invoke();
    }

    [RelayCommand]
    private void Print()
    {
        if (_documentFactory is null)
        {
            return;
        }

        var result = reportPrintService.Print(_documentFactory(), _reportKind, _jobName);
        StatusMessage = result.Message ?? string.Empty;

        if (result.Success)
        {
            CloseWith(true);
        }
    }

    [RelayCommand]
    private void SavePdf()
    {
        if (_documentFactory is null)
        {
            return;
        }

        var result = reportPrintService.PrintToPdf(_documentFactory(), _jobName);
        StatusMessage = result.Message ?? string.Empty;
    }
}
