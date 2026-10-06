using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;

using dosymep.SimpleServices;

using Juru.RevitPlugins.ClashNavigator.Views.Checks;

namespace Juru.RevitPlugins.ClashNavigator.Services;
/// <summary>
/// Показывает ход поиска коллизий и даёт его отменить.
/// </summary>
/// <remarks>
/// Раньше прогресс брался через <c>ServicesProvider.GetPlatformService&lt;IProgressDialogService&gt;()</c>,
/// но <c>WpfUIProgressDialogService</c> требует в конструктор <c>IHasTheme</c>, которого в
/// статическом контейнере нет: служба не создавалась, исключение уходило в обработчик команды
/// окна и терялось - нажатие OK выглядело так, будто ничего не происходит.
/// <para>
/// Поиск идёт в потоке Ревита, поэтому окно живёт в том же потоке, а после каждого обновления
/// диспетчер прокручивает очередь - иначе окно не перерисуется и нажатие на отмену не дойдёт.
/// <c>Progress&lt;T&gt;</c> здесь не годится: он кладёт вызовы в ту же очередь, и все они
/// приходят разом в конце.
/// </para>
/// </remarks>
internal sealed class ClashSearchProgress : IProgress<int>, IDisposable {
    private const int _reportEvery = 20;

    private readonly ClashSearchProgressWindow _window;
    private readonly CancellationTokenSource _cancellation = new();
    private string _stage;
    private int _lastReported;

    public ClashSearchProgress(ILocalizationService localizationService) {
        _window = new ClashSearchProgressWindow(localizationService);
        _ = new WindowInteropHelper(_window) { Owner = Process.GetCurrentProcess().MainWindowHandle };
        _window.Show();
        Pump();
    }

    /// <summary>
    /// Токен, который отменяется по нажатию кнопки отмены.
    /// </summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>
    /// Начинает новый этап с известным числом шагов.
    /// </summary>
    public void StartStage(string stage, int maximum) {
        _stage = stage;
        _lastReported = 0;
        _window.SetStage(stage, maximum);
        Pump();
        CheckCancellation();
    }

    public void Report(int value) {
        if(value - _lastReported >= _reportEvery) {
            _lastReported = value;
            _window.SetProgress(_stage, value);
            Pump();
        }
        CheckCancellation();
    }

    public void Dispose() {
        _window.Close();
        _cancellation.Dispose();
    }

    private void CheckCancellation() {
        if(_window.Cancelled && !_cancellation.IsCancellationRequested) {
            _cancellation.Cancel();
        }
    }

    /// <summary>
    /// Прокручивает очередь сообщений окна, чтобы оно перерисовалось и приняло нажатия.
    /// </summary>
    private void Pump() {
        _window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
