using System;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;

using Juru.RevitPlugins.Openings.Views;

namespace Juru.RevitPlugins.Openings.Services;

/// <summary>
/// Показывает ход расчёта и даёт его отменить.
/// </summary>
/// <remarks>
/// Поиск пересечений идёт в потоке Revit, поэтому окно живёт в том же потоке, а после каждого
/// обновления диспетчер прокручивает очередь сообщений - иначе окно не перерисуется и нажатие
/// на отмену не дойдёт, а Revit будет выглядеть зависшим. Обновление не на каждом шаге:
/// перерисовка стоит дороже самой проверки одной конструкции.
/// </remarks>
public sealed class OperationProgress : IProgress<int>, IDisposable {
    private const int _reportEvery = 10;

    private readonly ProgressWindow _window;
    private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
    private string _stage = "Идёт расчёт";
    private int _lastReported;

    /// <param name="ownerHandle">Дескриптор главного окна Revit, чтобы окно не уходило за него.</param>
    public OperationProgress(IntPtr ownerHandle) {
        _window = new ProgressWindow();
        new WindowInteropHelper(_window) { Owner = ownerHandle };
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
    }

    public void Report(int value) {
        if(value - _lastReported < _reportEvery) {
            CheckCancellation();
            return;
        }

        _lastReported = value;
        _window.SetProgress(_stage, value);
        Pump();
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
