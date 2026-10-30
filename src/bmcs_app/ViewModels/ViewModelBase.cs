using CommunityToolkit.Mvvm.ComponentModel;

namespace bmcs_app.ViewModels;

/// <summary>
/// 全 ViewModel の基底クラス。
/// 進捗表示の見た目（オーバーレイ等）は MahApps に載せる予定。
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>処理中かどうか。1秒を超えうる処理を <see cref="RunBusyAsync"/> で囲むと立つ。</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// 画面を起動直後の状態へ戻したときに発火する（docs/product-spec.md UI/UX節「登録後のリセット」）。
    /// View 側（<see cref="Behaviors.FocusBehavior"/>）が先頭入力項目へフォーカスを戻すために使う。
    /// </summary>
    public event EventHandler? ResetToInitialState;

    /// <summary>画面クリア後に呼ぶ。<see cref="ResetToInitialState"/> を発火させる。</summary>
    protected void NotifyResetToInitialState() => ResetToInitialState?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// View 側の名前付き入力欄へフォーカスを要求する（docs/product-spec.md UI/UX節
    /// 「ジャーナル系画面の伝票No入力欄の挙動」）。<see cref="Behaviors.FocusBehavior"/> の
    /// <c>FocusKey</c> と一致する要素へフォーカスが移る。
    /// </summary>
    public event EventHandler<string>? FocusRequested;

    /// <summary><paramref name="focusKey"/> と同じ <c>FocusKey</c> を持つ入力欄へフォーカスを要求する。</summary>
    protected void RequestFocus(string focusKey) => FocusRequested?.Invoke(this, focusKey);

    /// <summary>
    /// 画面内のDB操作を1つずつ実行するためのゲート。<c>DbContext</c> はウィンドウ単位で共有され、
    /// スレッドセーフでない（docs/architecture.md 8章）ため、同じ画面で非同期処理が重なると
    /// 「A second operation was started on this context instance…」になる。
    /// 例: 得意先確定の処理が終わる前に商品コードのEnterを押す。
    /// </summary>
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    /// <summary>
    /// 同じ画面の他の処理が終わるまで待ってから実行する（取りこぼさず順番に実行する）。
    /// 照会・検索のようにDBを触るが、処理中表示や多重実行の抑止（<see cref="RunBusyAsync"/>）は
    /// 要らない入口に使う。<paramref name="action"/> の中から、この関数や <see cref="RunBusyAsync"/>
    /// を経由するコマンドを呼ぶと自分自身を待ってしまう（ゲートは再入不可）ので、
    /// 内部処理は公開コマンドではなく通常のメソッドを呼ぶこと。
    /// </summary>
    protected async Task RunExclusiveAsync(Func<Task> action)
    {
        await _operationGate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// 処理を実行中フラグで囲む。多重実行は抑止する。実行は <see cref="RunExclusiveAsync"/> と同じゲートを通り、
    /// 照会などの処理が実行中なら終わるまで待つ。
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
            await RunExclusiveAsync(action);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
