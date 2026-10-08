using System.Windows;
using bmcs_app.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Services;

/// <summary>
/// ウィンドウの生成・表示を担う。
///
/// 売上入力と受注入力を同時に開く運用のため（docs/architecture.md 4章）、
/// <b>ウィンドウ1つにつき DI スコープを1つ作り、ウィンドウを閉じたときに破棄する。</b>
/// これにより DbContext が画面をまたいで共有されず、ある画面の未保存の変更が
/// 別画面の保存時に一緒に書き込まれる事故を防ぐ。
/// </summary>
public class WindowService(IServiceScopeFactory scopeFactory, ILogger<WindowService> logger)
{
    /// <summary>
    /// ウィンドウを新しいスコープで生成して表示する。
    /// </summary>
    /// <typeparam name="TWindow">表示するウィンドウ。</typeparam>
    /// <typeparam name="TViewModel">DataContext に設定する ViewModel。</typeparam>
    /// <param name="configure">
    /// 呼び出し元の文脈（プレビュー対象の伝票No.等）を ViewModel へ渡すためのコールバック
    /// （<see cref="ShowDialog{TWindow, TViewModel, TResult}"/>と同じ位置づけ）。
    /// <b>ここで渡すのはプロパティの設定だけにする。</b> ウィンドウの初期化（DB読込）は
    /// View の <c>Loaded</c> イベント→ViewModel の <c>LoadCommand</c> が担うため
    /// （<c>Show</c>は<c>window.Show()</c>の前に本コールバックを呼ぶが、<c>Loaded</c>はその後に
    /// 非同期で発火する。ここで非同期処理を行うと実行順が保証されない）。
    /// </param>
    public TWindow Show<TWindow, TViewModel>(Action<TViewModel>? configure = null)
        where TWindow : Window
        where TViewModel : notnull
    {
        var scope = scopeFactory.CreateScope();

        try
        {
            var window = scope.ServiceProvider.GetRequiredService<TWindow>();
            var viewModel = scope.ServiceProvider.GetRequiredService<TViewModel>();
            window.DataContext = viewModel;
            configure?.Invoke(viewModel);

            // ウィンドウが閉じられたらスコープを破棄し、DbContext も解放する。
            window.Closed += (_, _) =>
            {
                logger.LogInformation("{Window} を閉じたためスコープを破棄します。", typeof(TWindow).Name);
                scope.Dispose();
            };

            logger.LogInformation("{Window} を新しいスコープで開きます。", typeof(TWindow).Name);
            window.Show();
            return window;
        }
        catch
        {
            // 表示前に失敗した場合はスコープが漏れないよう破棄する。
            scope.Dispose();
            throw;
        }
    }

    /// <summary>
    /// モーダルダイアログを新しいスコープで生成し、選択結果を返す。
    /// <see cref="Show{TWindow, TViewModel}"/> と同様にウィンドウ1つにつきスコープを1つ作る。
    /// </summary>
    /// <param name="configure">呼び出し元の文脈（対象得意先コード等）を ViewModel へ渡すためのコールバック。</param>
    public TResult? ShowDialog<TWindow, TViewModel, TResult>(Action<TViewModel>? configure = null)
        where TWindow : Window
        where TViewModel : DialogViewModelBase<TResult>
    {
        var scope = scopeFactory.CreateScope();

        try
        {
            var window = scope.ServiceProvider.GetRequiredService<TWindow>();
            var viewModel = scope.ServiceProvider.GetRequiredService<TViewModel>();
            window.DataContext = viewModel;
            configure?.Invoke(viewModel);

            viewModel.CloseRequested += window.Close;

            window.Owner = System.Windows.Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive);

            logger.LogInformation("{Window} をモーダルで開きます。", typeof(TWindow).Name);
            window.ShowDialog();

            return viewModel.Result;
        }
        finally
        {
            logger.LogInformation("{Window} を閉じたためスコープを破棄します。", typeof(TWindow).Name);
            scope.Dispose();
        }
    }
}
