using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels;

/// <summary>
/// モーダルダイアログの ViewModel の基底クラス。
/// ViewModel は View を参照しない（docs/architecture.md 11章）ため、
/// ウィンドウを閉じる操作は <see cref="CloseRequested"/> を経由して
/// <see cref="Services.WindowService.ShowDialog{TWindow, TViewModel, TResult}"/> に行わせる。
/// </summary>
public abstract partial class DialogViewModelBase<TResult> : ViewModelBase
{
    public TResult? Result { get; private set; }

    public event Action? CloseRequested;

    protected void CloseWith(TResult? result)
    {
        Result = result;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseWith(default);
}
