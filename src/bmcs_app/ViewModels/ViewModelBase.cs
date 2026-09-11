using CommunityToolkit.Mvvm.ComponentModel;

namespace bmcs_app.ViewModels;

/// <summary>
/// 全 ViewModel の基底クラス。
/// 進捗表示の見た目（オーバーレイ等）は Phase 0-5 で MahApps に載せる。
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>処理中かどうか。1秒を超えうる処理を <see cref="RunBusyAsync"/> で囲むと立つ。</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// 画面を起動直後の状態へ戻したときに発火する（docs/product-spec.md UI/UX節「登録後のリセット」）。
    /// View 側（<see cref="Behaviors.InitialFocusBehavior"/>）が先頭入力項目へフォーカスを戻すために使う。
    /// </summary>
    public event EventHandler? ResetToInitialState;

    /// <summary>画面クリア後に呼ぶ。<see cref="ResetToInitialState"/> を発火させる。</summary>
    protected void NotifyResetToInitialState() => ResetToInitialState?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 処理を実行中フラグで囲む。多重実行は抑止する。
    /// </summary>
    protected async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
