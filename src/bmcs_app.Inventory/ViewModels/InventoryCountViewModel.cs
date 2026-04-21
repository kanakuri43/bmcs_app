using System.Collections.ObjectModel;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Inventory.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Inventory.ViewModels;

public class InventoryCountViewModel : BindableBase
{
    private readonly IInventoryCountRepository _repo;
    private readonly LookupService             _lookup;

    private List<DateOnly> _allDates         = new();
    private int            _currentDateIndex = -1;

    /// <summary>View のコードビハインドが購読。最終行の ProductCode へフォーカスを要求する</summary>
    public event Action<string>? FocusField;
    public static class FocusTargets
    {
        public const string LineProductCodeLast = "LineProductCodeLast";
    }

    // ── ヘッダー ────────────────────────────────────────────
    private DateTime? _editCountDate = DateTime.Today;
    public DateTime? EditCountDate
    {
        get => _editCountDate;
        set => SetProperty(ref _editCountDate, value);
    }

    private string _editNote = string.Empty;
    public string EditNote
    {
        get => _editNote;
        set => SetProperty(ref _editNote, value);
    }

    // ── 明細 ────────────────────────────────────────────────
    public ObservableCollection<InventoryCountLineViewModel> Lines { get; } = new();

    private int _lineCount;
    public int LineCount
    {
        get => _lineCount;
        private set => SetProperty(ref _lineCount, value);
    }

    // ── 統計 ────────────────────────────────────────────────
    private int _totalCountDates;
    public int TotalCountDates
    {
        get => _totalCountDates;
        private set => SetProperty(ref _totalCountDates, value);
    }

    // ── ステータス ──────────────────────────────────────────
    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ── コマンド ────────────────────────────────────────────
    public DelegateCommand NewCommand             { get; }
    public DelegateCommand SaveCommand            { get; }
    public DelegateCommand DeleteCommand          { get; }
    public DelegateCommand AddLineCommand         { get; }
    public DelegateCommand HeaderNoteEnterCommand { get; }
    public DelegateCommand PrevCountCommand       { get; }
    public DelegateCommand NextCountCommand       { get; }

    public InventoryCountViewModel(IInventoryCountRepository repo, LookupService lookup)
    {
        _repo   = repo;
        _lookup = lookup;

        Lines.CollectionChanged += (_, _) => LineCount = Lines.Count;

        NewCommand             = new DelegateCommand(OnNew);
        SaveCommand            = new DelegateCommand(async () => await OnSaveAsync());
        DeleteCommand          = new DelegateCommand(async () => await OnDeleteAsync(),
                                     () => _currentDateIndex >= 0)
                                 .ObservesProperty(() => TotalCountDates);
        AddLineCommand         = new DelegateCommand(OnAddLine);
        HeaderNoteEnterCommand = new DelegateCommand(OnHeaderNoteEnter);
        PrevCountCommand       = new DelegateCommand(async () => await NavigateAsync(_currentDateIndex - 1),
                                     () => _currentDateIndex > 0)
                                 .ObservesProperty(() => TotalCountDates);
        NextCountCommand       = new DelegateCommand(async () => await NavigateAsync(_currentDateIndex + 1),
                                     () => _currentDateIndex >= 0 && _currentDateIndex < _allDates.Count - 1)
                                 .ObservesProperty(() => TotalCountDates);

        _ = LoadInitialAsync();
    }

    // ── 初期ロード ──────────────────────────────────────────
    private async Task LoadInitialAsync()
    {
        try
        {
            _allDates = (await _repo.GetAllDatesAsync()).ToList();
            TotalCountDates = _allDates.Count;
            RaiseCanExecuteChanged();

            if (_allDates.Count > 0)
                await NavigateAsync(_allDates.Count - 1);
            else
                OnNew();
        }
        catch (Exception ex)
        {
            StatusMessage = $"読み込みエラー: {ex.Message}";
        }
    }

    // ── ナビゲーション ──────────────────────────────────────
    private async Task NavigateAsync(int index)
    {
        if (index < 0 || index >= _allDates.Count) return;
        try
        {
            var date  = _allDates[index];
            var lines = await _repo.GetByDateAsync(date);
            _currentDateIndex = index;
            EditCountDate     = date.ToDateTime(TimeOnly.MinValue);
            EditNote          = string.Empty;
            Lines.Clear();
            int no = 1;
            foreach (var l in lines)
            {
                var vm = CreateLineVm(no++);
                vm.Load(l);
                Lines.Add(vm);
            }
            LineCount     = Lines.Count;
            StatusMessage = $"{date:yyyy/MM/dd} の棚卸データを表示しています";
            RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"読み込みエラー: {ex.Message}";
        }
    }

    // ── 新規 ────────────────────────────────────────────────
    private void OnNew()
    {
        _currentDateIndex = -1;
        EditCountDate     = DateTime.Today;
        EditNote          = string.Empty;
        Lines.Clear();
        LineCount         = 0;
        StatusMessage     = "新規棚卸を入力してください";
        RaiseCanExecuteChanged();
    }

    // ── ヘッダー備考 Enter ──────────────────────────────────
    private void OnHeaderNoteEnter() => OnAddLine();

    // ── 行追加 ──────────────────────────────────────────────
    private void OnAddLine()
    {
        var vm = CreateLineVm(Lines.Count + 1);
        Lines.Add(vm);
        LineCount = Lines.Count;
        FocusField?.Invoke(FocusTargets.LineProductCodeLast);
    }

    // ── 保存 ────────────────────────────────────────────────
    private async Task OnSaveAsync()
    {
        if (EditCountDate is null)
        {
            StatusMessage = "棚卸日付を入力してください";
            return;
        }
        var validLines = Lines
            .Where(l => l.ProductId.HasValue && l.EditQuantity.HasValue)
            .Select(l => new InventoryCountLineInput(
                l.ProductId!.Value, l.EditQuantity!.Value,
                string.IsNullOrWhiteSpace(l.EditNote) ? null : l.EditNote))
            .ToList();
        if (validLines.Count == 0)
        {
            StatusMessage = "保存する明細がありません（商品・数量を入力してください）";
            return;
        }
        try
        {
            var date = DateOnly.FromDateTime(EditCountDate.Value);
            await _repo.UpsertAsync(date, validLines);

            _allDates = (await _repo.GetAllDatesAsync()).ToList();
            TotalCountDates   = _allDates.Count;
            _currentDateIndex = _allDates.IndexOf(date);
            RaiseCanExecuteChanged();
            StatusMessage = "保存しました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
    }

    // ── 削除 ────────────────────────────────────────────────
    private async Task OnDeleteAsync()
    {
        if (_currentDateIndex < 0 || EditCountDate is null) return;
        try
        {
            var date = DateOnly.FromDateTime(EditCountDate.Value);
            await _repo.DeleteByDateAsync(date);

            _allDates = (await _repo.GetAllDatesAsync()).ToList();
            TotalCountDates = _allDates.Count;

            if (_allDates.Count == 0)
            {
                OnNew();
            }
            else
            {
                var nextIndex = Math.Min(_currentDateIndex, _allDates.Count - 1);
                await NavigateAsync(nextIndex);
            }
            StatusMessage = "削除しました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"削除エラー: {ex.Message}";
        }
    }

    // ── ファクトリ ──────────────────────────────────────────
    private InventoryCountLineViewModel CreateLineVm(int lineNo)
        => new InventoryCountLineViewModel(
            _lookup,
            onDelete: vm =>
            {
                Lines.Remove(vm);
                int no = 1;
                foreach (var l in Lines) l.LineNo = no++;
                LineCount = Lines.Count;
            },
            onNoteEnter: _ => OnAddLine()
        )
        { LineNo = lineNo };

    private void RaiseCanExecuteChanged()
    {
        PrevCountCommand.RaiseCanExecuteChanged();
        NextCountCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }
}
