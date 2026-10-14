using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

using Juru.RevitPlugins.ProjectSetup.Server;

namespace Juru.RevitPlugins.ProjectSetup.Services;

/// <summary>Куда исполнитель сообщает, какой шаг делает.</summary>
internal interface IJobProgress
{
    void Step(int done, int total, string what);
}

/// <summary>
/// Ход работы — сразу в двух местах: в окне поверх Revit (пока команда работает, Revit не
/// отвечает, и без окна это выглядит как зависание) и на странице проекта через сервер.
/// </summary>
internal sealed class JobProgress(ProgressWindow window, SetupJobsClient client, ClaimedJob job, string title) : IJobProgress
{
    public void Step(int done, int total, string what)
    {
        string message = $"{done + 1} из {total}: {what}";
        window.Show(title, message, done, total);
        Task.Run(() => client.TryReportProgressAsync(job, message)).GetAwaiter().GetResult();
    }
}

/// <summary>
/// Окно без разметки: одна строка заголовка, одна строка шага и полоса. Живёт в потоке Revit,
/// поэтому перерисовывается принудительной прокачкой очереди сообщений после каждого шага.
/// </summary>
internal sealed class ProgressWindow : Window
{
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _step = new() { Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _bar = new() { Height = 14, Minimum = 0 };

    public ProgressWindow()
    {
        Title = "Подготовка проекта";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(_title);
        panel.Children.Add(_step);
        panel.Children.Add(_bar);
        Content = panel;

        new WindowInteropHelper(this).Owner = Process.GetCurrentProcess().MainWindowHandle;
    }

    public void Show(string title, string step, int done, int total)
    {
        _title.Text = title;
        _step.Text = step;
        _bar.Maximum = Math.Max(1, total);
        _bar.Value = done;
        if (!IsVisible) Show();
        Pump();
    }

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
}
